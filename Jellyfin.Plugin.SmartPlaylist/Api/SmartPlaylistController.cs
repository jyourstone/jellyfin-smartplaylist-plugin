using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SmartPlaylist.QueryEngine;
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
using System.Diagnostics;

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
                // Use Microsoft.Extensions.Logging.Abstractions.NullLogger to avoid LoggerFactory resource leaks
                // Since PlaylistService operations are logged at the controller level, this is acceptable
                var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<PlaylistService>.Instance;
                return new PlaylistService(_userManager, _libraryManager, _playlistManager, _userDataManager, logger, _providerManager);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create PlaylistService");
                throw;
            }
        }

        /// <summary>
        /// Gets a user-friendly label for a field name.
        /// </summary>
        /// <param name="fieldName">The field name</param>
        /// <returns>The user-friendly label</returns>
        private static string GetFieldLabel(string fieldName)
        {
            return fieldName switch
            {
                "DateCreated" => "Date Created",
                "DateLastRefreshed" => "Date Last Refreshed",
                "DateLastSaved" => "Date Last Saved",
                "DateModified" => "Date Modified",
                "ReleaseDate" => "Release Date",
                "ProductionYear" => "Production Year",
                "CommunityRating" => "Community Rating",
                "CriticRating" => "Critic Rating",
                "RuntimeMinutes" => "Runtime (Minutes)",
                "IsPlayed" => "Is Played",
                "IsFavorite" => "Is Favorite",
                "PlayCount" => "Play Count",
                "ItemType" => "Media Type",
                "OfficialRating" => "Parental Rating",
                "AudioLanguages" => "Audio Languages",
                "FileName" => "File Name",
                "FolderPath" => "Folder Path",
                "Tags" => "Tags",
                "Artists" => "Artists",
                "AlbumArtists" => "Album Artists",
                _ => fieldName
            };
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
                    _logger.LogWarning("No Jellyfin playlist found with name '{PlaylistName}' for user '{UserName}' ({UserId})", playlistName, user.Username, userId);
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
                    _logger.LogInformation("Triggering SmartPlaylist refresh task");
                    _taskManager.Execute(refreshTask, new TaskOptions());
                }
                else
                {
                    _logger.LogWarning("Smart playlist refresh task not found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering SmartPlaylist refresh task");
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
                        _logger.LogDebug("Successfully migrated playlist '{PlaylistName}' to use User ID", playlist.Name);
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
            var stopwatch = Stopwatch.StartNew();
            _logger.LogDebug("CreateSmartPlaylist called for playlist: {PlaylistName}", playlist?.Name);
            _logger.LogDebug("Playlist data received: Name={Name}, UserId={UserId}, Public={Public}, ExpressionSets={ExpressionSetCount}, MediaTypes={MediaTypes}", 
                playlist?.Name, playlist?.UserId, playlist?.Public, playlist?.ExpressionSets?.Count ?? 0, 
                playlist?.MediaTypes != null ? string.Join(",", playlist.MediaTypes) : "None");
            
            if (playlist?.ExpressionSets != null)
            {
                _logger.LogDebug("ExpressionSets count: {Count}", playlist.ExpressionSets.Count);
                for (int i = 0; i < playlist.ExpressionSets.Count; i++)
                {
                    var set = playlist.ExpressionSets[i];
                    _logger.LogDebug("ExpressionSet {Index}: {ExpressionCount} expressions", i, set?.Expressions?.Count ?? 0);
                    if (set?.Expressions != null)
                    {
                        for (int j = 0; j < set.Expressions.Count; j++)
                        {
                            var expr = set.Expressions[j];
                            _logger.LogDebug("Expression {SetIndex}.{ExprIndex}: {MemberName} {Operator} '{TargetValue}'", 
                                i, j, expr?.MemberName, expr?.Operator, expr?.TargetValue);
                        }
                    }
                }
            }
            
            try
            {
                if (string.IsNullOrEmpty(playlist.Id))
                {
                    playlist.Id = Guid.NewGuid().ToString();
                    _logger.LogDebug("Generated new playlist ID: {Id}", playlist.Id);
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

                // Validate regex patterns before saving
                if (playlist.ExpressionSets != null)
                {
                    foreach (var expressionSet in playlist.ExpressionSets)
                    {
                        if (expressionSet.Expressions != null)
                        {
                            foreach (var expression in expressionSet.Expressions)
                            {
                                if (expression.Operator == "MatchRegex" && !string.IsNullOrEmpty(expression.TargetValue))
                                {
                                    try
                                    {
                                        var regex = new System.Text.RegularExpressions.Regex(expression.TargetValue, System.Text.RegularExpressions.RegexOptions.None);
                                    }
                                    catch (ArgumentException ex)
                                    {
                                        _logger.LogError(ex, "Invalid regex pattern '{Pattern}' during validation", expression.TargetValue);
                                        return BadRequest($"Invalid regex pattern '{expression.TargetValue}': {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }

                var createdPlaylist = await playlistStore.SaveAsync(playlist);
                _logger.LogInformation("Created smart playlist: {PlaylistName}", playlist.Name);
                
                // Clear the rule cache to ensure the new playlist rules are properly compiled
                SmartPlaylist.ClearRuleCache(_logger);
                _logger.LogDebug("Cleared rule cache after creating playlist '{PlaylistName}'", playlist.Name);
                
                _logger.LogDebug("Calling RefreshSinglePlaylistWithTimeoutAsync for {PlaylistName}", playlist.Name);
                var playlistService = GetPlaylistService();
                var (success, message) = await playlistService.RefreshSinglePlaylistWithTimeoutAsync(createdPlaylist);
                stopwatch.Stop();
                
                if (!success)
                {
                    _logger.LogWarning("Failed to refresh newly created playlist {PlaylistName}: {Message}", playlist.Name, message);
                    // Still return the created playlist but log the warning
                    return CreatedAtAction(nameof(GetSmartPlaylist), new { id = createdPlaylist.Id }, createdPlaylist);
                }
                
                // DEBUG: Check the MediaType of the created Jellyfin playlist
                try
                {
                    var user = _userManager.GetUserById(createdPlaylist.UserId);
                    if (user != null)
                    {
                        var smartPlaylistName = createdPlaylist.Name + " [Smart]";
                        var query = new InternalItemsQuery(user)
                        {
                            IncludeItemTypes = [BaseItemKind.Playlist],
                            Recursive = true,
                            Name = smartPlaylistName
                        };
                        var jellyfinPlaylist = _libraryManager.GetItemsResult(query).Items.OfType<Playlist>().FirstOrDefault();
                        
                        if (jellyfinPlaylist != null)
                        {
                            var mediaTypeProperty = jellyfinPlaylist.GetType().GetProperty("MediaType");
                            var currentMediaType = mediaTypeProperty?.GetValue(jellyfinPlaylist)?.ToString() ?? "Unknown";
                            
                            // Log MediaType for debugging - note this is a known Jellyfin limitation
                            _logger.LogDebug("Created Jellyfin playlist '{PlaylistName}' has MediaType: {MediaType}. Note: Jellyfin defaults to 'Audio' for all playlists regardless of content.", 
                                smartPlaylistName, currentMediaType);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking MediaType of created playlist");
                }
                
                _logger.LogDebug("Finished RefreshSinglePlaylistWithTimeoutAsync for {PlaylistName} in {ElapsedTime}ms", playlist.Name, stopwatch.ElapsedMilliseconds);
                
                return CreatedAtAction(nameof(GetSmartPlaylist), new { id = createdPlaylist.Id }, createdPlaylist);
            }
            catch (ArgumentException ex) when (ex.Message.Contains("Invalid regex pattern"))
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Unexpected regex validation error in smart playlist creation after {ElapsedTime}ms", stopwatch.ElapsedMilliseconds);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error creating smart playlist after {ElapsedTime}ms", stopwatch.ElapsedMilliseconds);
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
            var stopwatch = Stopwatch.StartNew();
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

                // Validate regex patterns before saving
                if (playlist.ExpressionSets != null)
                {
                    foreach (var expressionSet in playlist.ExpressionSets)
                    {
                        if (expressionSet.Expressions != null)
                        {
                            foreach (var expression in expressionSet.Expressions)
                            {
                                if (expression.Operator == "MatchRegex" && !string.IsNullOrEmpty(expression.TargetValue))
                                {
                                    try
                                    {
                                        var regex = new System.Text.RegularExpressions.Regex(expression.TargetValue, System.Text.RegularExpressions.RegexOptions.None);
                                    }
                                    catch (ArgumentException ex)
                                    {
                                        _logger.LogError(ex, "Invalid regex pattern '{Pattern}' during validation", expression.TargetValue);
                                        return BadRequest($"Invalid regex pattern '{expression.TargetValue}': {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }

                // Check if ownership is changing
                var originalUserId = await GetPlaylistUserIdAsync(existingPlaylist);
                var newUserId = playlist.UserId;
                
                bool ownershipChanging = originalUserId != Guid.Empty && newUserId != originalUserId;
                bool nameChanging = !string.Equals(existingPlaylist.Name, playlist.Name, StringComparison.OrdinalIgnoreCase);
                bool enabledStatusChanging = existingPlaylist.Enabled != playlist.Enabled;
                
                // Log enabled status changes
                if (enabledStatusChanging)
                {
                    _logger.LogDebug("Playlist enabled status changing from {OldStatus} to {NewStatus} for playlist '{PlaylistName}'", 
                        existingPlaylist.Enabled ? "enabled" : "disabled", 
                        playlist.Enabled ? "enabled" : "disabled", 
                        existingPlaylist.Name);
                }
                
                if (ownershipChanging)
                {
                    _logger.LogDebug("Playlist ownership changing from user {OldUserId} to {NewUserId} for playlist '{PlaylistName}'", 
                        originalUserId, newUserId, existingPlaylist.Name);
                    
                    // Delete the old playlist from the original user
                    var oldPlaylistName = $"{existingPlaylist.Name} [Smart]";
                    DeleteJellyfinPlaylist(oldPlaylistName, originalUserId);
                }
                else if (nameChanging)
                {
                    _logger.LogDebug("Playlist name changing from '{OldName}' to '{NewName}' for user {UserId}", 
                        existingPlaylist.Name, playlist.Name, originalUserId);
                    
                    // Delete the old playlist with the old name
                    var oldPlaylistName = $"{existingPlaylist.Name} [Smart]";
                    DeleteJellyfinPlaylist(oldPlaylistName, originalUserId);
                }

                playlist.Id = id;
                var updatedPlaylist = await playlistStore.SaveAsync(playlist);
                
                // Clear the rule cache to ensure any rule changes are properly reflected
                SmartPlaylist.ClearRuleCache(_logger);
                _logger.LogDebug("Cleared rule cache after updating playlist '{PlaylistName}'", playlist.Name);
                
                // Immediately update the Jellyfin playlist using the single playlist service with timeout
                var playlistService = GetPlaylistService();
                var (success, message) = await playlistService.RefreshSinglePlaylistWithTimeoutAsync(updatedPlaylist);
                stopwatch.Stop();
                
                if (!success)
                {
                    _logger.LogWarning("Failed to refresh updated playlist {PlaylistName}: {Message}", playlist.Name, message);
                    // Still return the updated playlist but log the warning
                    return Ok(updatedPlaylist);
                }
                
                _logger.LogInformation("Updated SmartPlaylist: {PlaylistName} in {ElapsedTime}ms", playlist.Name, stopwatch.ElapsedMilliseconds);
                
                return Ok(updatedPlaylist);
            }
            catch (ArgumentException ex) when (ex.Message.Contains("Invalid regex pattern"))
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Unexpected regex validation error in smart playlist update after {ElapsedTime}ms", stopwatch.ElapsedMilliseconds);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error updating smart playlist {PlaylistId} after {ElapsedTime}ms", id, stopwatch.ElapsedMilliseconds);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error updating smart playlist");
            }
        }

        /// <summary>
        /// Delete a smart playlist.
        /// </summary>
        /// <param name="id">The playlist ID.</param>
        /// <param name="deleteJellyfinPlaylist">Whether to also delete the corresponding Jellyfin playlist. Defaults to true for backward compatibility.</param>
        /// <returns>No content.</returns>
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteSmartPlaylist([FromRoute, Required] string id, [FromQuery] bool deleteJellyfinPlaylist = true)
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
                
                // Handle the Jellyfin playlist based on user choice
                var playlistService = GetPlaylistService();
                if (deleteJellyfinPlaylist)
                {
                    await playlistService.DeletePlaylistAsync(playlist);
                    _logger.LogInformation("Deleted smart playlist: {PlaylistName}", playlist.Name);
                }
                else
                {
                    // Remove the [Smart] suffix from the playlist name
                    await playlistService.RemoveSmartSuffixAsync(playlist);
                    _logger.LogInformation("Deleted smart playlist configuration: {PlaylistName}", playlist.Name);
                }
                
                // Then delete the smart playlist configuration
                playlistStore.Delete(Guid.Empty, id);
                
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
                DateFields = FieldDefinitions.DateFields.Select(field => new { Value = field, Label = GetFieldLabel(field) }).ToArray(),
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
                    new { Value = "Tags", Label = "Tags" },
                    new { Value = "Artists", Label = "Artists" },
                    new { Value = "AlbumArtists", Label = "Album Artists" }
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
                    new { Value = "MatchRegex", Label = "Matches Regex (.NET syntax)" },
                    new { Value = "After", Label = "After" },
                    new { Value = "Before", Label = "Before" },
                    new { Value = "NewerThan", Label = "Newer Than" },
                    new { Value = "OlderThan", Label = "Older Than" }
                },
                OrderOptions = new[]
                {
                    new { Value = "NoOrder", Label = "No Order" },
                    new { Value = "Name Ascending", Label = "Name Ascending" },
                    new { Value = "Name Descending", Label = "Name Descending" },
                    new { Value = "ProductionYear Ascending", Label = "Production Year Ascending" },
                    new { Value = "ProductionYear Descending", Label = "Production Year Descending" },
                    new { Value = "DateCreated Ascending", Label = "Date Created Ascending" },
                    new { Value = "DateCreated Descending", Label = "Date Created Descending" },
                    new { Value = "ReleaseDate Ascending", Label = "Release Date Ascending" },
                    new { Value = "ReleaseDate Descending", Label = "Release Date Descending" },
                    new { Value = "CommunityRating Ascending", Label = "Community Rating Ascending" },
                    new { Value = "CommunityRating Descending", Label = "Community Rating Descending" }
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
        /// Enable a smart playlist.
        /// </summary>
        /// <param name="id">The playlist ID.</param>
        /// <returns>Success message.</returns>
        [HttpPost("{id}/enable")]
        public async Task<ActionResult> EnableSmartPlaylist([FromRoute, Required] string id)
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
                    return NotFound("Smart playlist not found");
                }
                
                // Temporarily set enabled state for the Jellyfin operation
                var originalEnabledState = playlist.Enabled;
                playlist.Enabled = true;
                
                try
                {
                    // Create/update the Jellyfin playlist FIRST
                    var playlistService = GetPlaylistService();
                    await playlistService.EnablePlaylistAsync(playlist);
                    
                    // Only save the configuration if the Jellyfin operation succeeds
                    await playlistStore.SaveAsync(playlist);
                    
                    _logger.LogInformation("Enabled smart playlist: {PlaylistId} - {PlaylistName}", id, playlist.Name);
                    return Ok(new { message = $"Smart playlist '{playlist.Name}' has been enabled" });
                }
                catch (Exception jellyfinEx)
                {
                    // Restore original state if Jellyfin operation fails
                    playlist.Enabled = originalEnabledState;
                    _logger.LogError(jellyfinEx, "Failed to enable Jellyfin playlist for {PlaylistId} - {PlaylistName}", id, playlist.Name);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enabling smart playlist {PlaylistId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error enabling smart playlist");
            }
        }

        /// <summary>
        /// Disable a smart playlist.
        /// </summary>
        /// <param name="id">The playlist ID.</param>
        /// <returns>Success message.</returns>
        [HttpPost("{id}/disable")]
        public async Task<ActionResult> DisableSmartPlaylist([FromRoute, Required] string id)
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
                    return NotFound("Smart playlist not found");
                }
                
                // Temporarily set disabled state for the Jellyfin operation
                var originalEnabledState = playlist.Enabled;
                playlist.Enabled = false;
                
                try
                {
                    // Remove the Jellyfin playlist FIRST
                    var playlistService = GetPlaylistService();
                    await playlistService.DisablePlaylistAsync(playlist);
                    
                    // Only save the configuration if the Jellyfin operation succeeds
                    await playlistStore.SaveAsync(playlist);
                    
                    _logger.LogInformation("Disabled smart playlist: {PlaylistId} - {PlaylistName}", id, playlist.Name);
                    return Ok(new { message = $"Smart playlist '{playlist.Name}' has been disabled" });
                }
                catch (Exception jellyfinEx)
                {
                    // Restore original state if Jellyfin operation fails
                    playlist.Enabled = originalEnabledState;
                    _logger.LogError(jellyfinEx, "Failed to disable Jellyfin playlist for {PlaylistId} - {PlaylistName}", id, playlist.Name);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling smart playlist {PlaylistId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error disabling smart playlist");
            }
        }

        /// <summary>
        /// Trigger a refresh of all smart playlists.
        /// </summary>
        /// <returns>Success message.</returns>
        [HttpPost("refresh")]
        public async Task<ActionResult> TriggerRefresh()
        {
            try
            {
                var playlistService = GetPlaylistService();
                var (success, message) = await playlistService.TryRefreshAllPlaylistsAsync();
                
                if (success)
                {
                    // If we got the lock, trigger the actual scheduled task
                    TriggerPlaylistRefresh();
                    return Ok(new { message = "Smart playlist refresh task triggered successfully" });
                }
                else
                {
                    return Conflict(new { message });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering smart playlist refresh");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error triggering smart playlist refresh");
            }
        }
    }
} 