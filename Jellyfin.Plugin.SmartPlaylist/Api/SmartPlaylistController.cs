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
                // Use a wrapper logger that implements ILogger<PlaylistService>
                var playlistServiceLogger = new PlaylistServiceLogger(_logger);
                return new PlaylistService(_userManager, _libraryManager, _playlistManager, _userDataManager, playlistServiceLogger, _providerManager);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create PlaylistService");
                throw;
            }
        }

        // Wrapper class to adapt the controller logger for PlaylistService
        private class PlaylistServiceLogger(ILogger logger) : ILogger<PlaylistService>
        {
            private readonly ILogger _logger = logger;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                _logger.Log(logLevel, eventId, state, exception, formatter);
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return _logger.IsEnabled(logLevel);
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return _logger.BeginScope(state);
            }
        }

        private void TriggerPlaylistRefresh()
        {
            try
            {
                // Find both audio and video refresh tasks
                var audioTask = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "RefreshAudioSmartPlaylists");
                var videoTask = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "RefreshVideoSmartPlaylists");
                
                bool anyTaskTriggered = false;
                
                if (audioTask != null)
                {
                    _logger.LogInformation("Triggering Audio SmartPlaylist refresh task");
                    _taskManager.Execute(audioTask, new TaskOptions());
                    anyTaskTriggered = true;
                }
                else
                {
                    _logger.LogWarning("Audio SmartPlaylist refresh task not found");
                }
                
                if (videoTask != null)
                {
                    _logger.LogInformation("Triggering Video SmartPlaylist refresh task");
                    _taskManager.Execute(videoTask, new TaskOptions());
                    anyTaskTriggered = true;
                }
                else
                {
                    _logger.LogWarning("Video SmartPlaylist refresh task not found");
                }
                
                if (!anyTaskTriggered)
                {
                    _logger.LogWarning("No SmartPlaylist refresh tasks found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering SmartPlaylist refresh tasks");
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
                var (success, message, jellyfinPlaylistId) = await playlistService.RefreshSinglePlaylistWithTimeoutAsync(createdPlaylist);
                
                // If refresh was successful, save the Jellyfin playlist ID
                if (success && !string.IsNullOrEmpty(jellyfinPlaylistId))
                {
                    createdPlaylist.JellyfinPlaylistId = jellyfinPlaylistId;
                    await playlistStore.SaveAsync(createdPlaylist);
                    _logger.LogDebug("Saved Jellyfin playlist ID {JellyfinPlaylistId} for smart playlist {PlaylistName}", 
                        jellyfinPlaylistId, createdPlaylist.Name);
                }
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
                        var smartPlaylistName = PlaylistNameFormatter.FormatPlaylistName(createdPlaylist.Name);
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
                    
                    // Note: Ownership changes will be handled by the PlaylistService during refresh
                    // The playlist will be updated in place rather than recreated
                }
                else if (nameChanging)
                {
                    _logger.LogDebug("Playlist name changing from '{OldName}' to '{NewName}' for user {UserId}", 
                        existingPlaylist.Name, playlist.Name, originalUserId);
                    
                    // Note: Name changes will be handled by the PlaylistService during refresh
                    // The playlist will be updated in place rather than recreated
                }

                playlist.Id = id;
                
                // Preserve the Jellyfin playlist ID from the existing playlist if it exists
                if (!string.IsNullOrEmpty(existingPlaylist.JellyfinPlaylistId))
                {
                    playlist.JellyfinPlaylistId = existingPlaylist.JellyfinPlaylistId;
                    _logger.LogDebug("Preserved Jellyfin playlist ID {JellyfinPlaylistId} from existing playlist", existingPlaylist.JellyfinPlaylistId);
                }
                
                var updatedPlaylist = await playlistStore.SaveAsync(playlist);
                
                // Clear the rule cache to ensure any rule changes are properly reflected
                SmartPlaylist.ClearRuleCache(_logger);
                _logger.LogDebug("Cleared rule cache after updating playlist '{PlaylistName}'", playlist.Name);
                
                // Immediately update the Jellyfin playlist using the single playlist service with timeout
                var playlistService = GetPlaylistService();
                var (success, message, jellyfinPlaylistId) = await playlistService.RefreshSinglePlaylistWithTimeoutAsync(updatedPlaylist);
                
                // If refresh was successful, save the Jellyfin playlist ID
                if (success && !string.IsNullOrEmpty(jellyfinPlaylistId))
                {
                    updatedPlaylist.JellyfinPlaylistId = jellyfinPlaylistId;
                    await playlistStore.SaveAsync(updatedPlaylist);
                    _logger.LogDebug("Saved Jellyfin playlist ID {JellyfinPlaylistId} for smart playlist {PlaylistName}", 
                        jellyfinPlaylistId, updatedPlaylist.Name);
                }
                
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
                    // Remove the suffix/prefix from the playlist name
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
                    new { Value = "Overview", Label = "Overview" },
                    new { Value = "ProductionYear", Label = "Production Year" },
                    new { Value = "ReleaseDate", Label = "Release Date" }
                    // Note: ItemType (Media Type) is intentionally excluded from UI fields
                    // because users select media type (Audio/Video) before creating rules
                },
                RatingsPlaybackFields = new[]
                {
                    new { Value = "CommunityRating", Label = "Community Rating" },
                    new { Value = "CriticRating", Label = "Critic Rating" },
                    new { Value = "IsFavorite", Label = "Is Favorite" },
                    new { Value = "IsPlayed", Label = "Is Played" },
                    new { Value = "LastPlayedDate", Label = "Last Played" },
                    new { Value = "NextUnwatched", Label = "Next Unwatched" },
                    new { Value = "PlayCount", Label = "Play Count" },
                    new { Value = "RuntimeMinutes", Label = "Runtime (Minutes)" }
                },

                FileFields = new[]
                {
                    new { Value = "FileName", Label = "File Name" },
                    new { Value = "FolderPath", Label = "Folder Path" },
                    new { Value = "DateModified", Label = "Date Modified" }
                },
                LibraryFields = new[]
                {
                    new { Value = "DateCreated", Label = "Date Added to Library" },
                    new { Value = "DateLastRefreshed", Label = "Last Metadata Refresh" },
                    new { Value = "DateLastSaved", Label = "Last Database Save" }
                },
                CollectionFields = new[]
                {
                    new { Value = "Collections", Label = "Collections" },
                    new { Value = "People", Label = "People" },
                    new { Value = "Genres", Label = "Genres" },
                    new { Value = "Studios", Label = "Studios" },
                    new { Value = "Tags", Label = "Tags" },
                    new { Value = "Artists", Label = "Artists" },
                    new { Value = "AlbumArtists", Label = "Album Artists" }
                },
                Operators = new[]
                {
                    new { Value = "Equal", Label = "equals" },
                    new { Value = "NotEqual", Label = "not equals" },
                    new { Value = "Contains", Label = "contains" },
                    new { Value = "NotContains", Label = "not contains" },
                    new { Value = "IsIn", Label = "is in" },
                    new { Value = "IsNotIn", Label = "is not in" },
                    new { Value = "GreaterThan", Label = "greater than" },
                    new { Value = "LessThan", Label = "less than" },
                    new { Value = "GreaterThanOrEqual", Label = "greater than or equal" },
                    new { Value = "LessThanOrEqual", Label = "less than or equal" },
                    new { Value = "MatchRegex", Label = "matches regex (.NET syntax)" },
                    new { Value = "After", Label = "after" },
                    new { Value = "Before", Label = "before" },
                    new { Value = "NewerThan", Label = "newer than" },
                    new { Value = "OlderThan", Label = "older than" }
                },
                FieldOperators = new Dictionary<string, string[]>
                {
                    // List fields - multi-valued fields
                    // Note: IsNotIn and NotContains excluded from Collections to avoid confusion with series expansion logic
                    ["Collections"] = ["Contains", "IsIn", "MatchRegex"],
                    ["People"] = ["Contains", "NotContains", "IsIn", "IsNotIn", "MatchRegex"],
                    ["Genres"] = ["Contains", "NotContains", "IsIn", "IsNotIn", "MatchRegex"],
                    ["Studios"] = ["Contains", "NotContains", "IsIn", "IsNotIn", "MatchRegex"],
                    ["Tags"] = ["Contains", "NotContains", "IsIn", "IsNotIn", "MatchRegex"],
                    ["Artists"] = ["Contains", "NotContains", "IsIn", "IsNotIn", "MatchRegex"],
                    ["AlbumArtists"] = ["Contains", "NotContains", "IsIn", "IsNotIn", "MatchRegex"],
                    ["AudioLanguages"] = ["Contains", "NotContains", "IsIn", "IsNotIn", "MatchRegex"],
                    
                    // Simple fields - single-choice fields
                    ["ItemType"] = ["Equal", "NotEqual"],
                    
                    // Boolean fields - true/false fields
                    ["IsPlayed"] = ["Equal", "NotEqual"],
                    ["IsFavorite"] = ["Equal", "NotEqual"],
                    ["NextUnwatched"] = ["Equal", "NotEqual"],
                    
                    // Numeric fields - number-based fields
                    ["ProductionYear"] = ["Equal", "NotEqual", "GreaterThan", "LessThan", "GreaterThanOrEqual", "LessThanOrEqual"],
                    ["CommunityRating"] = ["Equal", "NotEqual", "GreaterThan", "LessThan", "GreaterThanOrEqual", "LessThanOrEqual"],
                    ["CriticRating"] = ["Equal", "NotEqual", "GreaterThan", "LessThan", "GreaterThanOrEqual", "LessThanOrEqual"],
                    ["RuntimeMinutes"] = ["Equal", "NotEqual", "GreaterThan", "LessThan", "GreaterThanOrEqual", "LessThanOrEqual"],
                    ["PlayCount"] = ["Equal", "NotEqual", "GreaterThan", "LessThan", "GreaterThanOrEqual", "LessThanOrEqual"],
                    
                    // Date fields - date/time fields
                    ["DateCreated"] = ["Equal", "NotEqual", "After", "Before", "NewerThan", "OlderThan"],
                    ["DateLastRefreshed"] = ["Equal", "NotEqual", "After", "Before", "NewerThan", "OlderThan"],
                    ["DateLastSaved"] = ["Equal", "NotEqual", "After", "Before", "NewerThan", "OlderThan"],
                    ["DateModified"] = ["Equal", "NotEqual", "After", "Before", "NewerThan", "OlderThan"],
                    ["ReleaseDate"] = ["Equal", "NotEqual", "After", "Before", "NewerThan", "OlderThan"],
                    ["LastPlayedDate"] = ["Equal", "NotEqual", "After", "Before", "NewerThan", "OlderThan"]
                },
                OrderOptions = new[]
                {
                    new { Value = "NoOrder", Label = "No Order" },
                    new { Value = "Random", Label = "Random" },
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
        /// Trigger a refresh of a specific smart playlist.
        /// </summary>
        /// <param name="id">The playlist ID.</param>
        /// <returns>Success message.</returns>
        [HttpPost("{id}/refresh")]
        public async Task<ActionResult> TriggerSinglePlaylistRefresh([FromRoute, Required] string id)
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
                
                var playlistService = GetPlaylistService();
                var (success, message, jellyfinPlaylistId) = await playlistService.RefreshSinglePlaylistWithTimeoutAsync(playlist);
                
                if (success)
                {
                    _logger.LogInformation("Successfully refreshed single playlist: {PlaylistId} - {PlaylistName}", id, playlist.Name);
                    return Ok(new { message = $"Smart playlist '{playlist.Name}' has been refreshed successfully" });
                }
                else
                {
                    _logger.LogWarning("Failed to refresh single playlist: {PlaylistId} - {PlaylistName}. Error: {ErrorMessage}", id, playlist.Name, message);
                    return BadRequest(new { message });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing single smart playlist {PlaylistId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error refreshing smart playlist");
            }
        }

        /// <summary>
        /// Trigger a refresh of all smart playlists (both audio and video).
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
                    // If we got the lock, trigger the actual scheduled tasks
                    TriggerPlaylistRefresh();
                    return Ok(new { message = "Smart playlist refresh tasks triggered successfully" });
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