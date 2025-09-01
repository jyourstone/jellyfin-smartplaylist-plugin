using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    public partial class SmartPlaylistController(
        ILogger<SmartPlaylistController> logger, 
        IServerApplicationPaths applicationPaths,
        IUserManager userManager,
        ILibraryManager libraryManager,
        ITaskManager taskManager,
        IPlaylistManager playlistManager,
        IUserDataManager userDataManager,
        IProviderManager providerManager,
        IHttpContextAccessor httpContextAccessor) : ControllerBase
    {
        private readonly ILogger<SmartPlaylistController> _logger = logger;
        private readonly IServerApplicationPaths _applicationPaths = applicationPaths;
        private readonly IUserManager _userManager = userManager;
        private readonly ILibraryManager _libraryManager = libraryManager;
        private readonly ITaskManager _taskManager = taskManager;
        private readonly IPlaylistManager _playlistManager = playlistManager;
        private readonly IUserDataManager _userDataManager = userDataManager;
        private readonly IProviderManager _providerManager = providerManager;
        private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

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
                var mediaTask = _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask.Key == "RefreshMediaSmartPlaylists");
                
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
                
                if (mediaTask != null)
                {
                    _logger.LogInformation("Triggering Media SmartPlaylist refresh task");
                    _taskManager.Execute(mediaTask, new TaskOptions());
                    anyTaskTriggered = true;
                }
                else
                {
                    _logger.LogWarning("Media SmartPlaylist refresh task not found");
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
        /// Gets the current user ID from Jellyfin claims.
        /// </summary>
        /// <returns>The current user ID, or Guid.Empty if not found.</returns>
        private Guid GetCurrentUserId()
        {
            try
            {
                _logger.LogDebug("Attempting to determine current user ID from Jellyfin claims...");
                
                // Get user ID from Jellyfin-specific claims
                var userIdClaim = User.FindFirst("Jellyfin-UserId")?.Value;
                _logger.LogDebug("Jellyfin-UserId claim: {UserId}", userIdClaim ?? "null");
                
                if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var userId))
                {
                    _logger.LogDebug("Found current user ID from Jellyfin-UserId claim: {UserId}", userId);
                    return userId;
                }

                _logger.LogWarning("Could not determine current user ID from Jellyfin-UserId claim");
                return Guid.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current user ID");
                return Guid.Empty;
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
                                        var regex = new Regex(expression.TargetValue, RegexOptions.None);
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
                                        var regex = new Regex(expression.TargetValue, RegexOptions.None);
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
                    new { Value = "ReleaseDate", Label = "Release Date" },
                    new { Value = "Resolution", Label = "Resolution" },
                    new { Value = "SeriesName", Label = "Series Name" },
                    new { Value = "Framerate", Label = "Framerate" }
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
                Operators = Constants.Operators.AllOperators,
                FieldOperators = GetFieldOperators(),
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
        /// Static readonly field operators dictionary for performance optimization.
        /// </summary>
        private static readonly Dictionary<string, string[]> _fieldOperators = Constants.Operators.GetFieldOperatorsDictionary();

        /// <summary>
        /// Gets the field operators dictionary using centralized constants.
        /// </summary>
        /// <returns>Dictionary mapping field names to their allowed operators</returns>
        private static Dictionary<string, string[]> GetFieldOperators()
        {
            return _fieldOperators;
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
                // Use the improved helper method to get current user ID
                var userId = GetCurrentUserId();
                
                if (userId == Guid.Empty)
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

        /// <summary>
        /// Export all smart playlists as a ZIP file.
        /// </summary>
        /// <returns>ZIP file containing all playlist JSON files.</returns>
        [HttpPost("export")]
        public async Task<ActionResult> ExportPlaylists()
        {
            try
            {
                var fileSystem = new SmartPlaylistFileSystem(_applicationPaths);
                var filePaths = fileSystem.GetAllSmartPlaylistFilePaths();
                
                if (filePaths.Length == 0)
                {
                    return BadRequest(new { message = "No smart playlists found to export" });
                }

                using var zipStream = new MemoryStream();
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
                {
                    foreach (var filePath in filePaths)
                    {
                        var fileName = Path.GetFileName(filePath);
                        var entry = archive.CreateEntry(fileName);
                        
                        using var entryStream = entry.Open();
                        using var fileStream = System.IO.File.OpenRead(filePath);
                        await fileStream.CopyToAsync(entryStream);
                    }
                }

                zipStream.Position = 0;
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var zipFileName = $"smartplaylists_export_{timestamp}.zip";
                
                _logger.LogInformation("Exported {PlaylistCount} smart playlists to {FileName}", filePaths.Length, zipFileName);
                
                return File(zipStream.ToArray(), "application/zip", zipFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting smart playlists");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error exporting smart playlists");
            }
        }

        /// <summary>
        /// Import smart playlists from a ZIP file.
        /// </summary>
        /// <param name="file">ZIP file containing playlist JSON files.</param>
        /// <returns>Import results with counts of imported and skipped playlists.</returns>
        [HttpPost("import")]
        public async Task<ActionResult> ImportPlaylists([FromForm] IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new { message = "No file uploaded" });
                }

                if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = "File must be a ZIP archive" });
                }

                var playlistStore = GetPlaylistStore();
                var existingPlaylists = await playlistStore.GetAllSmartPlaylistsAsync();
                var existingIds = existingPlaylists.Select(p => p.Id).ToHashSet();

                var importResults = new List<object>();
                int importedCount = 0;
                int skippedCount = 0;
                int errorCount = 0;

                using var zipStream = new MemoryStream();
                await file.CopyToAsync(zipStream);
                zipStream.Position = 0;

                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
                
                foreach (var entry in archive.Entries)
                {
                    if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Skip non-JSON files
                    }
                    
                    // Skip system files (like macOS ._filename files)
                    if (entry.Name.StartsWith("._") || entry.Name.StartsWith(".DS_Store"))
                    {
                        _logger.LogDebug("Skipping system file: {FileName}", entry.Name);
                        continue;
                    }

                    try
                    {
                        using var entryStream = entry.Open();
                        var playlist = await JsonSerializer.DeserializeAsync<SmartPlaylistDto>(entryStream);
                        
                        if (playlist == null || string.IsNullOrEmpty(playlist.Id))
                        {
                            _logger.LogWarning("Invalid playlist data in file {FileName}: {Issue}", 
                                entry.Name, playlist == null ? "null playlist" : "empty ID");
                            importResults.Add(new { fileName = entry.Name, status = "error", message = "Invalid or empty playlist data" });
                            errorCount++;
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(playlist.Name))
                        {
                            _logger.LogWarning("Playlist in file {FileName} has no name", entry.Name);
                            importResults.Add(new { fileName = entry.Name, status = "error", message = "Playlist must have a name" });
                            errorCount++;
                            continue;
                        }

                        // Validate and potentially reassign user references
                        bool reassignedUsers = false;
                        Guid currentUserId = Guid.Empty;
                        
                        // Check playlist owner
                        if (playlist.UserId != Guid.Empty)
                        {
                            var user = _userManager.GetUserById(playlist.UserId);
                            if (user == null)
                            {
                                // Only get current user ID when we need to reassign
                                if (currentUserId == Guid.Empty)
                                {
                                    currentUserId = GetCurrentUserId();
                                    if (currentUserId == Guid.Empty)
                                    {
                                        _logger.LogWarning("Playlist '{PlaylistName}' references non-existent user {UserId} but cannot determine importing user for reassignment", 
                                            playlist.Name, playlist.UserId);
                                        importResults.Add(new { fileName = entry.Name, status = "error", message = "Cannot reassign playlist - unable to determine importing user" });
                                        errorCount++;
                                        continue; // Skip this entire playlist
                                    }
                                }
                                
                                _logger.LogWarning("Playlist '{PlaylistName}' references non-existent user {UserId}, reassigning to importing user {CurrentUserId}", 
                                    playlist.Name, playlist.UserId, currentUserId);
                                
                                playlist.UserId = currentUserId;
                                reassignedUsers = true;
                            }
                        }

                        // Note: We don't reassign user-specific expression rules if the referenced user doesn't exist.
                        // The system will naturally fall back to the playlist owner for such rules.

                        // Add note to import results if users were reassigned
                        if (reassignedUsers)
                        {
                            _logger.LogInformation("Reassigned user references in playlist '{PlaylistName}' due to non-existent users", playlist.Name);
                        }

                        if (existingIds.Contains(playlist.Id))
                        {
                            importResults.Add(new { fileName = entry.Name, playlistName = playlist.Name, status = "skipped", message = "Playlist with this ID already exists" });
                            skippedCount++;
                            continue;
                        }

                        // Import the playlist
                        await playlistStore.SaveAsync(playlist);
                        importResults.Add(new { fileName = entry.Name, playlistName = playlist.Name, status = "imported", message = "Successfully imported" });
                        importedCount++;
                        
                        _logger.LogDebug("Imported playlist {PlaylistName} (ID: {PlaylistId}) from {FileName}", 
                            playlist.Name, playlist.Id, entry.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error importing playlist from {FileName}", entry.Name);
                        importResults.Add(new { fileName = entry.Name, status = "error", message = ex.Message });
                        errorCount++;
                    }
                }

                var summary = new
                {
                    totalFiles = archive.Entries.Count(e => e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)),
                    imported = importedCount,
                    skipped = skippedCount,
                    errors = errorCount,
                    details = importResults
                };

                _logger.LogInformation("Import completed: {Imported} imported, {Skipped} skipped, {Errors} errors", 
                    importedCount, skippedCount, errorCount);

                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing smart playlists");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error importing smart playlists");
            }
        }


    }
} 