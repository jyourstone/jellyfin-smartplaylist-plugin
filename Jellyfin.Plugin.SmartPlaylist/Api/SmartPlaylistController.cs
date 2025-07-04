using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;    
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
    public class SmartPlaylistController(
        ILogger<SmartPlaylistController> logger, 
        IServerApplicationPaths applicationPaths,
        IUserManager userManager,
        ILibraryManager libraryManager,
        ITaskManager taskManager,
        IPlaylistManager playlistManager,
        IUserDataManager userDataManager,
        IProviderManager providerManager) : ControllerBase
    {
        private readonly ILogger<SmartPlaylistController> _logger = logger;
        private readonly IServerApplicationPaths _applicationPaths = applicationPaths;
        private readonly IUserManager _userManager = userManager;
        private readonly ILibraryManager _libraryManager = libraryManager;
        private readonly ITaskManager _taskManager = taskManager;
        private readonly IPlaylistManager _playlistManager = playlistManager;
        private readonly IUserDataManager _userDataManager = userDataManager;
        private readonly IProviderManager _providerManager = providerManager;

        private SmartPlaylistStore GetPlaylistStore()
        {
            var fileSystem = new SmartPlaylistFileSystem(_applicationPaths);
            return new SmartPlaylistStore(fileSystem, _userManager);
        }

        private PlaylistService GetPlaylistService()
        {
            try
            {
                // Use the same logger pattern as the rest of the controller
                var loggerFactory = new LoggerFactory();
                var logger = loggerFactory.CreateLogger<PlaylistService>();
                return new PlaylistService(_userManager, _libraryManager, _playlistManager, _userDataManager, logger, _providerManager);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create PlaylistService");
                throw;
            }
        }

        private void DeleteJellyfinPlaylist(string playlistName, Guid userId)
        {
            try
            {
                var user = _userManager.GetUserById(userId);
                if (user == null)
                {
                    _logger.LogWarning("User with ID '{UserId}' not found when trying to delete Jellyfin playlist '{PlaylistName}'", userId, playlistName);
                    return;
                }

                var query = new InternalItemsQuery(user)
                {
                    IncludeItemTypes = [BaseItemKind.Playlist],
                    Recursive = true,
                    Name = playlistName
                };

                var existingPlaylist = _libraryManager.GetItemsResult(query).Items.OfType<Playlist>().FirstOrDefault();
                if (existingPlaylist != null)
                {
                    _logger.LogInformation("Deleting Jellyfin playlist '{PlaylistName}' for user '{UserName}' ({UserId})", playlistName, user.Username, userId);
                    _libraryManager.DeleteItem(existingPlaylist, new DeleteOptions { DeleteFileLocation = true }, true);
                }
                else
                {
                    _logger.LogInformation("No Jellyfin playlist found with name '{PlaylistName}' for user '{UserName}' ({UserId})", playlistName, user.Username, userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting Jellyfin playlist '{PlaylistName}' for user ID '{UserId}'", playlistName, userId);
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
        /// Gets the user ID for a playlist, handling migration from old User field to new UserId field.
        /// </summary>
        /// <param name="playlist">The playlist.</param>
        /// <returns>The user ID, or Guid.Empty if not found.</returns>
        private async Task<Guid> GetPlaylistUserIdAsync(SmartPlaylistDto playlist)
        {
            // If new UserId field is set and not empty, use it
            if (playlist.UserId != Guid.Empty)
            {
                return playlist.UserId;
            }

            // Legacy migration: if old User field is set, try to find the user and migrate
#pragma warning disable CS0618 // Type or member is obsolete
            if (!string.IsNullOrEmpty(playlist.User))
            {
                var user = _userManager.GetUserByName(playlist.User);
                if (user != null)
                {
                    _logger.LogInformation("Migrating playlist '{PlaylistName}' from username '{UserName}' to User ID '{UserId}'", 
                        playlist.Name, playlist.User, user.Id);
                    
                    // Update the playlist with the User ID and save it
                    playlist.UserId = user.Id;
                    playlist.User = null; // Clear the old field
                    
                    try
                    {
                        var playlistStore = GetPlaylistStore();
                        await playlistStore.SaveAsync(playlist);
                        _logger.LogInformation("Successfully migrated playlist '{PlaylistName}' to use User ID", playlist.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to save migrated playlist '{PlaylistName}', but will continue with operation", playlist.Name);
                    }
                    
                    return user.Id;
                }
                else
                {
                    _logger.LogWarning("Legacy playlist '{PlaylistName}' references non-existent user '{UserName}'", playlist.Name, playlist.User);
                }
            }
#pragma warning restore CS0618 // Type or member is obsolete

            return Guid.Empty;
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
            _logger.LogInformation("[DEBUG] CreateSmartPlaylist called for playlist: {PlaylistName}", playlist?.Name);
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
                _logger.LogInformation("[DEBUG] Calling RefreshSinglePlaylistAsync for {PlaylistName}", playlist.Name);
                var playlistService = GetPlaylistService();
                await playlistService.RefreshSinglePlaylistAsync(createdPlaylist);
                _logger.LogInformation("[DEBUG] Finished RefreshSinglePlaylistAsync for {PlaylistName}", playlist.Name);
                
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
                if (!Guid.TryParse(id, out var guidId))
                {
                    return BadRequest("Invalid playlist ID format");
                }

                var playlistStore = GetPlaylistStore();
                var existingPlaylist = await playlistStore.GetSmartPlaylistAsync(guidId);
                if (existingPlaylist == null)
                {
                    return NotFound("Smart playlist not found");
                }

                // Check for duplicate names (excluding the current playlist being updated)
                var allPlaylists = await playlistStore.GetAllSmartPlaylistsAsync();
                var duplicateName = allPlaylists.FirstOrDefault(p => 
                    p.Id != id && 
                    p.Name.Equals(playlist.Name, StringComparison.OrdinalIgnoreCase));
                
                if (duplicateName != null)
                {
                    return BadRequest($"A smart playlist with the name '{playlist.Name}' already exists. Please choose a different name.");
                }

                // Check if ownership is changing
                var originalUserId = await GetPlaylistUserIdAsync(existingPlaylist);
                var newUserId = playlist.UserId;
                
                bool ownershipChanging = originalUserId != Guid.Empty && newUserId != originalUserId;
                bool nameChanging = !string.Equals(existingPlaylist.Name, playlist.Name, StringComparison.OrdinalIgnoreCase);
                
                if (ownershipChanging)
                {
                    _logger.LogInformation("Playlist ownership changing from user {OldUserId} to {NewUserId} for playlist '{PlaylistName}'", 
                        originalUserId, newUserId, existingPlaylist.Name);
                    
                    // Delete the old playlist from the original user
                    var oldPlaylistName = $"{existingPlaylist.Name} [Smart]";
                    DeleteJellyfinPlaylist(oldPlaylistName, originalUserId);
                }
                else if (nameChanging)
                {
                    _logger.LogInformation("Playlist name changing from '{OldName}' to '{NewName}' for user {UserId}", 
                        existingPlaylist.Name, playlist.Name, originalUserId);
                    
                    // Delete the old playlist with the old name
                    var oldPlaylistName = $"{existingPlaylist.Name} [Smart]";
                    DeleteJellyfinPlaylist(oldPlaylistName, originalUserId);
                }

                playlist.Id = id;
                var updatedPlaylist = await playlistStore.SaveAsync(playlist);
                _logger.LogInformation("Updated smart playlist: {PlaylistName}", playlist.Name);
                
                // Immediately update the Jellyfin playlist using the single playlist service
                var playlistService = GetPlaylistService();
                await playlistService.RefreshSinglePlaylistAsync(updatedPlaylist);
                
                return Ok(updatedPlaylist);
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
                
                // Delete the corresponding Jellyfin playlist using the service
                var playlistService = GetPlaylistService();
                await playlistService.DeletePlaylistAsync(playlist);
                
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
                    new { Value = "People", Label = "People" },
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
        /// Get all users for the user selection dropdown.
        /// </summary>
        /// <returns>List of users.</returns>
        [HttpGet("users")]
        public ActionResult<object> GetUsers()
        {
            try
            {
                var users = _userManager.Users
                    .Where(u => !u.HasPermission(PermissionKind.IsDisabled))
                    .Select(u => new { 
                        u.Id, 
                        Name = u.Username
                    })
                    .OrderBy(u => u.Name)
                    .ToList();

                return Ok(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving users");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving users");
            }
        }

        /// <summary>
        /// Get the current user's information.
        /// </summary>
        /// <returns>Current user info.</returns>
        [HttpGet("currentuser")]
        public ActionResult<object> GetCurrentUser()
        {
            try
            {
                // Try to get the current user ID from the request context
                var userIdClaim = User.FindFirst("UserId")?.Value ?? User.FindFirst("sub")?.Value;
                
                if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                {
                    return BadRequest("Unable to determine current user");
                }

                var user = _userManager.GetUserById(userId);
                if (user == null)
                {
                    return NotFound("Current user not found");
                }

                return Ok(new { 
                    user.Id, 
                    Name = user.Username
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current user");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error getting current user");
            }
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