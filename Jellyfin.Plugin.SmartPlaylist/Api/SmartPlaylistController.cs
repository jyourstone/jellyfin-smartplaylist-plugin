using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist.Api
{
    /// <summary>
    /// SmartPlaylist API controller.
    /// </summary>
    [ApiController]
    [Authorize(Policy = "RequiresElevation")]
    [Route("Plugins/SmartPlaylist")]
    [Produces("application/json")]
    public class SmartPlaylistController : ControllerBase
    {
        private readonly ILogger<SmartPlaylistController> _logger;
        private readonly IServerApplicationPaths _applicationPaths;
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly ITaskManager _taskManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="SmartPlaylistController"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{SmartPlaylistController}"/> interface.</param>
        /// <param name="applicationPaths">Instance of the <see cref="IServerApplicationPaths"/> interface.</param>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
        public SmartPlaylistController(
            ILogger<SmartPlaylistController> logger, 
            IServerApplicationPaths applicationPaths,
            IUserManager userManager,
            ILibraryManager libraryManager,
            ITaskManager taskManager)
        {
            _logger = logger;
            _applicationPaths = applicationPaths;
            _userManager = userManager;
            _libraryManager = libraryManager;
            _taskManager = taskManager;
        }

        private ISmartPlaylistStore GetPlaylistStore()
        {
            var fileSystem = new SmartPlaylistFileSystem(_applicationPaths);
            return new SmartPlaylistStore(fileSystem, _userManager);
        }

        private void DeleteJellyfinPlaylist(string playlistName, string userName)
        {
            try
            {
                var user = _userManager.GetUserByName(userName);
                if (user == null)
                {
                    _logger.LogWarning("User '{UserName}' not found when trying to delete Jellyfin playlist '{PlaylistName}'", userName, playlistName);
                    return;
                }

                var query = new InternalItemsQuery(user)
                {
                    IncludeItemTypes = new[] { BaseItemKind.Playlist },
                    Recursive = true,
                    Name = playlistName
                };

                var existingPlaylist = _libraryManager.GetItemsResult(query).Items.OfType<Playlist>().FirstOrDefault();
                if (existingPlaylist != null)
                {
                    _logger.LogInformation("Deleting Jellyfin playlist '{PlaylistName}' for user '{UserName}'", playlistName, userName);
                    _libraryManager.DeleteItem(existingPlaylist, new DeleteOptions { DeleteFileLocation = true }, true);
                }
                else
                {
                    _logger.LogInformation("No Jellyfin playlist found with name '{PlaylistName}' for user '{UserName}'", playlistName, userName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting Jellyfin playlist '{PlaylistName}' for user '{UserName}'", playlistName, userName);
            }
        }

        private void TriggerPlaylistRefresh()
        {
            try
            {
                var refreshTask = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "RefreshSmartPlaylists");
                if (refreshTask != null)
                {
                    _logger.LogInformation("Triggering smart playlist refresh task");
                    _taskManager.Execute(refreshTask, new TaskOptions());
                }
                else
                {
                    _logger.LogWarning("Smart playlist refresh task not found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering smart playlist refresh task");
            }
        }

        /// <summary>
        /// Get all smart playlists.
        /// </summary>
        /// <returns>List of smart playlists.</returns>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SmartPlaylistDto>>> GetSmartPlaylists()
        {
            try
            {
                var playlistStore = GetPlaylistStore();
                var playlists = await playlistStore.GetAllSmartPlaylistsAsync();
                return Ok(playlists);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving smart playlists");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving smart playlists");
            }
        }

        /// <summary>
        /// Get a specific smart playlist by ID.
        /// </summary>
        /// <param name="id">The playlist ID.</param>
        /// <returns>The smart playlist.</returns>
        [HttpGet("{id}")]
        public async Task<ActionResult<SmartPlaylistDto>> GetSmartPlaylist([FromRoute, Required] string id)
        {
            try
            {
                if (!Guid.TryParse(id, out var guidId))
                {
                    return BadRequest("Invalid playlist ID format");
                }
                
                var playlistStore = GetPlaylistStore();
                var playlist = await playlistStore.GetSmartPlaylistAsync(guidId);
                if (playlist == null)
                {
                    return NotFound();
                }
                return Ok(playlist);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving smart playlist {PlaylistId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving smart playlist");
            }
        }

        /// <summary>
        /// Create a new smart playlist.
        /// </summary>
        /// <param name="playlist">The smart playlist to create.</param>
        /// <returns>The created smart playlist.</returns>
        [HttpPost]
        public async Task<ActionResult<SmartPlaylistDto>> CreateSmartPlaylist([FromBody, Required] SmartPlaylistDto playlist)
        {
            try
            {
                if (string.IsNullOrEmpty(playlist.Id))
                {
                    playlist.Id = Guid.NewGuid().ToString();
                }
                
                if (string.IsNullOrEmpty(playlist.FileName))
                {
                    // Let the store handle the filename generation to keep it simple and consistent.
                    // The store will use the playlist name to create a clean filename.
                }

                var playlistStore = GetPlaylistStore();
                
                // Check for duplicate names
                var existingPlaylists = await playlistStore.GetAllSmartPlaylistsAsync();
                if (existingPlaylists.Any(p => p.Name.Equals(playlist.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    return BadRequest($"A smart playlist with the name '{playlist.Name}' already exists. Please choose a different name.");
                }

                var createdPlaylist = await playlistStore.SaveAsync(playlist);
                _logger.LogInformation("Created smart playlist: {PlaylistName}", playlist.Name);
                
                // Trigger the refresh task to immediately create the Jellyfin playlist
                TriggerPlaylistRefresh();
                
                return CreatedAtAction(nameof(GetSmartPlaylist), new { id = createdPlaylist.Id }, createdPlaylist);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating smart playlist");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error creating smart playlist");
            }
        }

        /// <summary>
        /// Update an existing smart playlist.
        /// </summary>
        /// <param name="id">The playlist ID.</param>
        /// <param name="playlist">The updated smart playlist.</param>
        /// <returns>The updated smart playlist.</returns>
        [HttpPut("{id}")]
        public async Task<ActionResult<SmartPlaylistDto>> UpdateSmartPlaylist([FromRoute, Required] string id, [FromBody, Required] SmartPlaylistDto playlist)
        {
            try
            {
                playlist.Id = id;
                var playlistStore = GetPlaylistStore();
                await playlistStore.SaveAsync(playlist);
                _logger.LogInformation("Updated smart playlist: {PlaylistName}", playlist.Name);
                
                return Ok(playlist);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating smart playlist {PlaylistId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error updating smart playlist");
            }
        }

        /// <summary>
        /// Delete a smart playlist.
        /// </summary>
        /// <param name="id">The playlist ID.</param>
        /// <returns>No content.</returns>
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteSmartPlaylist([FromRoute, Required] string id)
        {
            try
            {
                if (!Guid.TryParse(id, out var guidId))
                {
                    return BadRequest("Invalid playlist ID format");
                }
                
                var playlistStore = GetPlaylistStore();
                
                // First get the playlist details before deleting
                var playlist = await playlistStore.GetSmartPlaylistAsync(guidId);
                if (playlist == null)
                {
                    return NotFound("Smart playlist not found");
                }
                
                // Delete the corresponding Jellyfin playlist first
                if (!string.IsNullOrEmpty(playlist.User) && !string.IsNullOrEmpty(playlist.Name))
                {
                    // Use the same naming convention as creation: add [Smart] suffix
                    var smartPlaylistName = playlist.Name + " [Smart]";
                    DeleteJellyfinPlaylist(smartPlaylistName, playlist.User);
                }
                
                // Then delete the smart playlist configuration
                playlistStore.Delete(Guid.Empty, id);
                _logger.LogInformation("Deleted smart playlist and corresponding Jellyfin playlist: {PlaylistId} - {PlaylistName}", id, playlist.Name);
                
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting smart playlist {PlaylistId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error deleting smart playlist");
            }
        }

        /// <summary>
        /// Get available field options for smart playlist rules.
        /// </summary>
        /// <returns>Available field options.</returns>
        [HttpGet("fields")]
        public ActionResult<object> GetAvailableFields()
        {
            var fields = new
            {
                ContentFields = new[]
                {
                    new { Value = "Album", Label = "Album" },
                    new { Value = "AudioLanguages", Label = "Audio Languages" },
                    new { Value = "ItemType", Label = "Media Type" },
                    new { Value = "Name", Label = "Name" },
                    new { Value = "OfficialRating", Label = "Parental Rating" },
                    new { Value = "ProductionYear", Label = "Production Year" }
                },
                RatingsPlaybackFields = new[]
                {
                    new { Value = "CommunityRating", Label = "Community Rating" },
                    new { Value = "CriticRating", Label = "Critic Rating" },
                    new { Value = "IsFavorite", Label = "Is Favorite" },
                    new { Value = "IsPlayed", Label = "Is Played" },
                    new { Value = "PlayCount", Label = "Play Count" },
                    new { Value = "RuntimeMinutes", Label = "Runtime (Minutes)" }
                },
                DateFields = new[]
                {
                    new { Value = "DateCreated", Label = "Date Created" },
                    new { Value = "DateLastRefreshed", Label = "Date Last Refreshed" },
                    new { Value = "DateLastSaved", Label = "Date Last Saved" },
                    new { Value = "DateModified", Label = "Date Modified" }
                },
                FileFields = new[]
                {
                    new { Value = "FileName", Label = "File Name" },
                    new { Value = "FolderPath", Label = "Folder Path" }
                },
                CollectionFields = new[]
                {
                    new { Value = "Genres", Label = "Genres" },
                    new { Value = "Studios", Label = "Studios" },
                    new { Value = "Tags", Label = "Tags" }
                },
                Operators = new[]
                {
                    new { Value = "Equal", Label = "Equals" },
                    new { Value = "NotEqual", Label = "Not Equals" },
                    new { Value = "Contains", Label = "Contains" },
                    new { Value = "NotContains", Label = "Not Contains" },
                    new { Value = "GreaterThan", Label = "Greater Than" },
                    new { Value = "LessThan", Label = "Less Than" },
                    new { Value = "GreaterThanOrEqual", Label = "Greater Than or Equal" },
                    new { Value = "LessThanOrEqual", Label = "Less Than or Equal" },
                    new { Value = "MatchRegex", Label = "Matches Regex (.NET syntax)" }
                },
                OrderOptions = new[]
                {
                    new { Value = "NoOrder", Label = "No Order" },
                    new { Value = "Release Date Ascending", Label = "Release Date Ascending" },
                    new { Value = "Release Date Descending", Label = "Release Date Descending" }
                }
            };

            return Ok(fields);
        }

        /// <summary>
        /// Trigger a refresh of all smart playlists.
        /// </summary>
        /// <returns>Success message.</returns>
        [HttpPost("refresh")]
        public ActionResult TriggerRefresh()
        {
            try
            {
                TriggerPlaylistRefresh();
                return Ok(new { message = "Smart playlist refresh task triggered successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering smart playlist refresh");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error triggering smart playlist refresh");
            }
        }
    }
} 