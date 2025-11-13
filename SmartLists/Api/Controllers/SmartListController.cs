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
using Jellyfin.Plugin.SmartLists.Core;
using Jellyfin.Plugin.SmartLists.Core.Constants;
using Jellyfin.Plugin.SmartLists.Core.Models;
using Jellyfin.Plugin.SmartLists.Services.Playlists;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using AutoRefreshService = Jellyfin.Plugin.SmartLists.Services.Shared.AutoRefreshService;
using Jellyfin.Plugin.SmartLists.Utilities;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Jellyfin.Plugin.SmartLists.Api.Controllers
{
    /// <summary>
    /// SmartLists API controller.
    /// </summary>
    [ApiController]
    [Authorize(Policy = "RequiresElevation")]
    [Route("Plugins/SmartLists")]
    [Produces("application/json")]
    public partial class SmartListController(
        ILogger<SmartListController> logger,
        IServerApplicationPaths applicationPaths,
        IUserManager userManager,
        ILibraryManager libraryManager,
        IPlaylistManager playlistManager,
        IUserDataManager userDataManager,
        IProviderManager providerManager,
        IManualRefreshService manualRefreshService) : ControllerBase
    {
        private readonly IServerApplicationPaths _applicationPaths = applicationPaths;
        private readonly IUserManager _userManager = userManager;
        private readonly ILibraryManager _libraryManager = libraryManager;
        private readonly IPlaylistManager _playlistManager = playlistManager;
        private readonly IUserDataManager _userDataManager = userDataManager;
        private readonly IProviderManager _providerManager = providerManager;
        private readonly IManualRefreshService _manualRefreshService = manualRefreshService;

        private Services.Playlists.PlaylistStore GetPlaylistStore()
        {
            var fileSystem = new SmartListFileSystem(_applicationPaths);
            return new Services.Playlists.PlaylistStore(fileSystem, _userManager);
        }



        private Services.Playlists.PlaylistService GetPlaylistService()
        {
            try
            {
                // Use a wrapper logger that implements ILogger<PlaylistService>
                var playlistServiceLogger = new PlaylistServiceLogger(logger);
                return new Services.Playlists.PlaylistService(_userManager, _libraryManager, _playlistManager, _userDataManager, playlistServiceLogger, _providerManager);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create PlaylistService");
                throw;
            }
        }

        // Wrapper class to adapt the controller logger for PlaylistService
        private sealed class PlaylistServiceLogger(ILogger logger) : ILogger<Services.Playlists.PlaylistService>
        {

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                logger.Log(logLevel, eventId, state, exception, formatter);
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logger.IsEnabled(logLevel);
            }

            IDisposable? ILogger.BeginScope<TState>(TState state)
            {
                return logger.BeginScope(state);
            }
        }

        /// <summary>
        /// Gets the user ID for a playlist.
        /// </summary>
        /// <param name="playlist">The playlist.</param>
        /// <returns>The user ID, or Guid.Empty if not found.</returns>
        private static Guid GetPlaylistUserId(SmartPlaylistDto playlist)
        {
            // If UserId field is set and not empty, use it
            if (playlist.UserId != Guid.Empty)
            {
                return playlist.UserId;
            }

            return Guid.Empty;
        }

        /// <summary>
        /// Validates a regex pattern to prevent injection attacks and ReDoS vulnerabilities.
        /// </summary>
        /// <param name="pattern">The regex pattern to validate.</param>
        /// <param name="errorMessage">Output parameter containing error message if validation fails.</param>
        /// <returns>True if the pattern is valid, false otherwise.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3012:Review code for regex injection vulnerabilities", Justification = "Pattern is validated with length limits and timeout to prevent ReDoS attacks")]
        private static bool IsValidRegexPattern(string pattern, out string errorMessage)
        {
            errorMessage = string.Empty;

            // Check for null or empty pattern
            if (string.IsNullOrWhiteSpace(pattern))
            {
                errorMessage = "Regex pattern cannot be null or empty";
                return false;
            }

            // Limit pattern length to prevent ReDoS attacks
            const int maxPatternLength = 1000;
            if (pattern.Length > maxPatternLength)
            {
                errorMessage = $"Regex pattern exceeds maximum length of {maxPatternLength} characters";
                return false;
            }

            // Try to compile the pattern with a timeout to detect ReDoS vulnerabilities
            try
            {
                var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
                // Test with a simple string to ensure it compiles correctly
                _ = regex.IsMatch("test");
            }
            catch (ArgumentException)
            {
                // Invalid pattern syntax - this is acceptable, will be caught later
                // We just want to ensure it doesn't cause ReDoS
            }
            catch (RegexMatchTimeoutException)
            {
                errorMessage = "Regex pattern is too complex and may cause performance issues";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets the current user ID from Jellyfin claims.
        /// </summary>
        /// <returns>The current user ID, or Guid.Empty if not found.</returns>
        private Guid GetCurrentUserId()
        {
            try
            {
                logger.LogDebug("Attempting to determine current user ID from Jellyfin claims...");

                // Get user ID from Jellyfin-specific claims
                var userIdClaim = User.FindFirst("Jellyfin-UserId")?.Value;
                logger.LogDebug("Jellyfin-UserId claim: {UserId}", userIdClaim ?? "null");

                if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var userId))
                {
                    logger.LogDebug("Found current user ID from Jellyfin-UserId claim: {UserId}", userId);
                    return userId;
                }

                logger.LogWarning("Could not determine current user ID from Jellyfin-UserId claim");
                return Guid.Empty;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting current user ID");
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
                var playlists = await playlistStore.GetAllAsync();
                return Ok(playlists);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving smart playlists");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving smart playlists");
            }
        }

        /// <summary>
        /// Get a specific smart playlist by ID.
        /// </summary>
        /// <param name="id">The playlist ID.</param>
        /// <returns>The smart playlist.</returns>
        [HttpGet("{id}")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "ID is validated as GUID before use, preventing path injection")]
        public async Task<ActionResult<SmartPlaylistDto>> GetSmartPlaylist([FromRoute, Required] string id)
        {
            try
            {
                if (!Guid.TryParse(id, out var guidId))
                {
                    return BadRequest("Invalid playlist ID format");
                }

                var playlistStore = GetPlaylistStore();
                var playlist = await playlistStore.GetByIdAsync(guidId);
                if (playlist == null)
                {
                    return NotFound();
                }
                return Ok(playlist);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving smart playlist {PlaylistId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving smart playlist");
            }
        }

        /// <summary>
        /// Create a new smart playlist.
        /// </summary>
        /// <param name="playlist">The smart playlist to create.</param>
        /// <returns>The created smart playlist.</returns>
        [HttpPost]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3012:Review code for regex injection vulnerabilities", Justification = "Regex patterns are validated with IsValidRegexPattern method including length limits and timeout")]
        public async Task<ActionResult<SmartPlaylistDto>> CreateSmartPlaylist([FromBody] SmartPlaylistDto? playlist)
        {
            if (playlist == null)
            {
                logger.LogWarning("CreateSmartPlaylist called with null playlist data");
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = "Playlist data is required",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // Set defaults for optional fields
            // These fields are optional for creation (we generate/set them)
            if (string.IsNullOrEmpty(playlist.Id))
            {
                playlist.Id = Guid.NewGuid().ToString();
            }

            if (playlist.Order == null)
            {
                playlist.Order = new OrderDto { SortOptions = [] };
            }
            else if (playlist.Order.SortOptions == null || playlist.Order.SortOptions.Count == 0)
            {
                // Order is provided but SortOptions is empty - initialize it
                playlist.Order.SortOptions = [];
            }

            // Ensure Type is set correctly
            playlist.Type = Core.Enums.SmartListType.Playlist;

            // Now validate model state after setting defaults
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .SelectMany(x => x.Value!.Errors.Select(e => 
                    {
                        var fieldName = string.IsNullOrEmpty(x.Key) ? "Unknown" : x.Key;
                        var errorMessage = string.IsNullOrEmpty(e.ErrorMessage) ? "Invalid value" : e.ErrorMessage;
                        // Include exception message if available (for deserialization errors)
                        if (e.Exception != null && !string.IsNullOrEmpty(e.Exception.Message))
                        {
                            errorMessage = $"{errorMessage} ({e.Exception.Message})";
                        }
                        return $"{fieldName}: {errorMessage}";
                    }))
                    .ToList();
                
                var errorMessage = errors.Count > 0 
                    ? string.Join("; ", errors) 
                    : "One or more validation errors occurred";
                
                logger.LogWarning("Model validation failed for CreateSmartPlaylist: {Errors}", errorMessage);
                
                // Return detailed error response that will be serialized properly
                var problemDetails = new ValidationProblemDetails(ModelState)
                {
                    Title = "Validation Error",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = errorMessage
                };
                
                return BadRequest(problemDetails);
            }

            // Additional validation for required fields
            if (string.IsNullOrWhiteSpace(playlist.Name))
            {
                logger.LogWarning("CreateSmartPlaylist called with empty Name");
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = "Playlist name is required",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            if (playlist.UserId == Guid.Empty)
            {
                logger.LogWarning("CreateSmartPlaylist called with empty UserId. Name={Name}", playlist.Name);
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = "Playlist owner (UserId) is required",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var stopwatch = Stopwatch.StartNew();
            logger.LogDebug("CreateSmartPlaylist called for playlist: {PlaylistName}", playlist.Name);
            logger.LogDebug("Playlist data received: Name={Name}, UserId={UserId}, Public={Public}, ExpressionSets={ExpressionSetCount}, MediaTypes={MediaTypes}",
                playlist.Name, playlist.UserId, playlist.Public, playlist.ExpressionSets?.Count ?? 0,
                playlist.MediaTypes != null ? string.Join(",", playlist.MediaTypes) : "None");

            if (playlist.ExpressionSets != null)
            {
                logger.LogDebug("ExpressionSets count: {Count}", playlist.ExpressionSets.Count);
                for (int i = 0; i < playlist.ExpressionSets.Count; i++)
                {
                    var set = playlist.ExpressionSets[i];
                    logger.LogDebug("ExpressionSet {Index}: {ExpressionCount} expressions", i, set?.Expressions?.Count ?? 0);
                    if (set?.Expressions != null)
                    {
                        for (int j = 0; j < set.Expressions.Count; j++)
                        {
                            var expr = set.Expressions[j];
                            logger.LogDebug("Expression {SetIndex}.{ExprIndex}: {MemberName} {Operator} '{TargetValue}'",
                                i, j, expr?.MemberName, expr?.Operator, expr?.TargetValue);
                        }
                    }
                }
            }

            try
            {
                // Ensure Type is set (should be set by constructor, but ensure it's correct)
                if (playlist.Type == Core.Enums.SmartListType.Collection)
                {
                    logger.LogWarning("CreateSmartPlaylist called with Collection type, this endpoint is for Playlists only");
                    return BadRequest("This endpoint is for creating playlists only. Use the collections endpoint for collections.");
                }
                playlist.Type = Core.Enums.SmartListType.Playlist;

                if (string.IsNullOrEmpty(playlist.Id))
                {
                    playlist.Id = Guid.NewGuid().ToString();
                    logger.LogDebug("Generated new playlist ID: {Id}", playlist.Id);
                }

                // Ensure FileName is set (will be set by store, but initialize here for validation)
                if (string.IsNullOrEmpty(playlist.FileName))
                {
                    playlist.FileName = $"{playlist.Id}.json";
                }

                // Ensure Order is initialized if not provided
                if (playlist.Order == null)
                {
                    playlist.Order = new OrderDto { SortOptions = [] };
                }

                var playlistStore = GetPlaylistStore();

                // Check for duplicate names
                var existingPlaylists = await playlistStore.GetAllAsync();
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
                                    // Validate regex pattern to prevent injection attacks
                                    if (!IsValidRegexPattern(expression.TargetValue, out var validationError))
                                    {
                                        return BadRequest($"Invalid regex pattern: {validationError}");
                                    }

                                    try
                                    {
                                        // Use a timeout to prevent ReDoS attacks
                                        // Pattern is already validated by IsValidRegexPattern above
                                        var regex = new Regex(expression.TargetValue, RegexOptions.None, TimeSpan.FromMilliseconds(100));
                                    }
                                    catch (ArgumentException ex)
                                    {
                                        logger.LogError(ex, "Invalid regex pattern '{Pattern}' during validation", expression.TargetValue);
                                        return BadRequest($"Invalid regex pattern '{expression.TargetValue}': {ex.Message}");
                                    }
                                    catch (RegexMatchTimeoutException ex)
                                    {
                                        logger.LogError(ex, "Regex pattern '{Pattern}' timed out during validation", expression.TargetValue);
                                        return BadRequest($"Regex pattern '{expression.TargetValue}' is too complex or caused a timeout");
                                    }
                                }
                            }
                        }
                    }
                }

                // Set DateCreated to current time for new playlists
                playlist.DateCreated = DateTime.UtcNow;

                var createdPlaylist = await playlistStore.SaveAsync(playlist);
                logger.LogInformation("Created smart playlist: {PlaylistName}", playlist.Name);

                // Update the auto-refresh cache with the new playlist
                AutoRefreshService.Instance?.UpdatePlaylistInCache(createdPlaylist);

                // Clear the rule cache to ensure the new playlist rules are properly compiled
                SmartList.ClearRuleCache(logger);
                logger.LogDebug("Cleared rule cache after creating playlist '{PlaylistName}'", playlist.Name);

                logger.LogDebug("Calling RefreshWithTimeoutAsync for {PlaylistName}", playlist.Name);
                var playlistService = GetPlaylistService();
                var (success, message, jellyfinPlaylistId) = await playlistService.RefreshWithTimeoutAsync(createdPlaylist);

                // If refresh was successful, save the Jellyfin playlist ID
                if (success && !string.IsNullOrEmpty(jellyfinPlaylistId))
                {
                    createdPlaylist.JellyfinPlaylistId = jellyfinPlaylistId;
                    await playlistStore.SaveAsync(createdPlaylist);
                    logger.LogDebug("Saved Jellyfin playlist ID {JellyfinPlaylistId} for smart playlist {PlaylistName}",
                        jellyfinPlaylistId, createdPlaylist.Name);
                }
                stopwatch.Stop();

                if (!success)
                {
                    logger.LogWarning("Failed to refresh newly created playlist {PlaylistName}: {Message}", playlist.Name, message);
                    // Still return the created playlist but log the warning
                    return CreatedAtAction(nameof(GetSmartPlaylist), new { id = createdPlaylist.Id }, createdPlaylist);
                }

                // DEBUG: Check the MediaType of the created Jellyfin playlist
                try
                {
                    var user = _userManager.GetUserById(createdPlaylist.UserId);
                    if (user != null)
                    {
                        var smartPlaylistName = NameFormatter.FormatPlaylistName(createdPlaylist.Name);
                        var query = new InternalItemsQuery(user)
                        {
                            IncludeItemTypes = [BaseItemKind.Playlist],
                            Recursive = true,
                            Name = smartPlaylistName,
                        };
                        var jellyfinPlaylist = _libraryManager.GetItemsResult(query).Items.OfType<Playlist>().FirstOrDefault();

                        if (jellyfinPlaylist != null)
                        {
                            var mediaTypeProperty = jellyfinPlaylist.GetType().GetProperty("MediaType");
                            var currentMediaType = mediaTypeProperty?.GetValue(jellyfinPlaylist)?.ToString() ?? "Unknown";

                            // Log MediaType for debugging - note this is a known Jellyfin limitation
                            logger.LogDebug("Created Jellyfin playlist '{PlaylistName}' has MediaType: {MediaType}.",
                                smartPlaylistName, currentMediaType);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Error checking MediaType of created playlist");
                }

                logger.LogDebug("Finished RefreshWithTimeoutAsync for {PlaylistName} in {ElapsedTime}ms", playlist.Name, stopwatch.ElapsedMilliseconds);

                return CreatedAtAction(nameof(GetSmartPlaylist), new { id = createdPlaylist.Id }, createdPlaylist);
            }
            catch (ArgumentException ex) when (ex.Message.Contains("Invalid regex pattern"))
            {
                stopwatch.Stop();
                logger.LogError(ex, "Unexpected regex validation error in smart playlist creation after {ElapsedTime}ms", stopwatch.ElapsedMilliseconds);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger.LogError(ex, "Error creating smart playlist after {ElapsedTime}ms", stopwatch.ElapsedMilliseconds);
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "ID is validated as GUID before use, preventing path injection")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3012:Review code for regex injection vulnerabilities", Justification = "Regex patterns are validated with IsValidRegexPattern method including length limits and timeout")]
        public async Task<ActionResult<SmartPlaylistDto>> UpdateSmartPlaylist([FromRoute, Required] string id, [FromBody, Required] SmartPlaylistDto playlist)
        {
            if (playlist == null)
            {
                return BadRequest("Playlist data is required");
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                if (!Guid.TryParse(id, out var guidId))
                {
                    return BadRequest("Invalid playlist ID format");
                }

                var playlistStore = GetPlaylistStore();
                var existingPlaylist = await playlistStore.GetByIdAsync(guidId);
                if (existingPlaylist == null)
                {
                    return NotFound("Smart playlist not found");
                }

                // Check for duplicate names (excluding the current playlist being updated)
                var allPlaylists = await playlistStore.GetAllAsync();
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
                                    // Validate regex pattern to prevent injection attacks
                                    if (!IsValidRegexPattern(expression.TargetValue, out var validationError))
                                    {
                                        return BadRequest($"Invalid regex pattern: {validationError}");
                                    }

                                    try
                                    {
                                        // Use a timeout to prevent ReDoS attacks
                                        // Pattern is already validated by IsValidRegexPattern above
                                        var regex = new Regex(expression.TargetValue, RegexOptions.None, TimeSpan.FromMilliseconds(100));
                                    }
                                    catch (ArgumentException ex)
                                    {
                                        logger.LogError(ex, "Invalid regex pattern '{Pattern}' during validation", expression.TargetValue);
                                        return BadRequest($"Invalid regex pattern '{expression.TargetValue}': {ex.Message}");
                                    }
                                    catch (RegexMatchTimeoutException ex)
                                    {
                                        logger.LogError(ex, "Regex pattern '{Pattern}' timed out during validation", expression.TargetValue);
                                        return BadRequest($"Regex pattern '{expression.TargetValue}' is too complex or caused a timeout");
                                    }
                                }
                            }
                        }
                    }
                }

                // Preserve UserId if not provided (frontend might not send it during edit)
                var originalUserId = GetPlaylistUserId(existingPlaylist);
                if (playlist.UserId == Guid.Empty && originalUserId != Guid.Empty)
                {
                    playlist.UserId = originalUserId;
                }
                var newUserId = playlist.UserId;

                bool ownershipChanging = originalUserId != Guid.Empty && newUserId != originalUserId;
                bool nameChanging = !string.Equals(existingPlaylist.Name, playlist.Name, StringComparison.OrdinalIgnoreCase);
                bool enabledStatusChanging = existingPlaylist.Enabled != playlist.Enabled;

                // Log enabled status changes
                if (enabledStatusChanging)
                {
                    logger.LogDebug("Playlist enabled status changing from {OldStatus} to {NewStatus} for playlist '{PlaylistName}'",
                        existingPlaylist.Enabled ? "enabled" : "disabled",
                        playlist.Enabled ? "enabled" : "disabled",
                        existingPlaylist.Name);
                }

                if (ownershipChanging)
                {
                    logger.LogDebug("Playlist ownership changing from user {OldUserId} to {NewUserId} for playlist '{PlaylistName}'",
                        originalUserId, newUserId, existingPlaylist.Name);

                    // Note: Ownership changes will be handled by the PlaylistService during refresh
                    // The playlist will be updated in place rather than recreated
                }
                else if (nameChanging)
                {
                    logger.LogDebug("Playlist name changing from '{OldName}' to '{NewName}' for user {UserId}",
                        existingPlaylist.Name, playlist.Name, originalUserId);

                    // Note: Name changes will be handled by the PlaylistService during refresh
                    // The playlist will be updated in place rather than recreated
                }

                playlist.Id = id;

                // Preserve original creation timestamp
                if (existingPlaylist.DateCreated.HasValue)
                {
                    playlist.DateCreated = existingPlaylist.DateCreated;
                }

                // Preserve the Jellyfin playlist ID from the existing playlist if it exists
                if (!string.IsNullOrEmpty(existingPlaylist.JellyfinPlaylistId))
                {
                    playlist.JellyfinPlaylistId = existingPlaylist.JellyfinPlaylistId;
                    logger.LogDebug("Preserved Jellyfin playlist ID {JellyfinPlaylistId} from existing playlist", existingPlaylist.JellyfinPlaylistId);
                }

                var updatedPlaylist = await playlistStore.SaveAsync(playlist);

                // Update the auto-refresh cache with the updated playlist
                AutoRefreshService.Instance?.UpdatePlaylistInCache(updatedPlaylist);

                // Clear the rule cache to ensure any rule changes are properly reflected
                SmartList.ClearRuleCache(logger);
                logger.LogDebug("Cleared rule cache after updating playlist '{PlaylistName}'", playlist.Name);

                // Immediately update the Jellyfin playlist using the single playlist service with timeout
                var playlistService = GetPlaylistService();
                var (success, message, jellyfinPlaylistId) = await playlistService.RefreshWithTimeoutAsync(updatedPlaylist);

                // If refresh was successful, save the Jellyfin playlist ID
                if (success && !string.IsNullOrEmpty(jellyfinPlaylistId))
                {
                    updatedPlaylist.JellyfinPlaylistId = jellyfinPlaylistId;
                    await playlistStore.SaveAsync(updatedPlaylist);
                    logger.LogDebug("Saved Jellyfin playlist ID {JellyfinPlaylistId} for smart playlist {PlaylistName}",
                        jellyfinPlaylistId, updatedPlaylist.Name);
                }

                stopwatch.Stop();

                if (!success)
                {
                    logger.LogWarning("Failed to refresh updated playlist {PlaylistName}: {Message}", playlist.Name, message);
                    // Still return the updated playlist but log the warning
                    return Ok(updatedPlaylist);
                }

                logger.LogInformation("Updated SmartList: {PlaylistName} in {ElapsedTime}ms", playlist.Name, stopwatch.ElapsedMilliseconds);

                return Ok(updatedPlaylist);
            }
            catch (ArgumentException ex) when (ex.Message.Contains("Invalid regex pattern"))
            {
                stopwatch.Stop();
                logger.LogError(ex, "Unexpected regex validation error in smart playlist update after {ElapsedTime}ms", stopwatch.ElapsedMilliseconds);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger.LogError(ex, "Error updating smart playlist {PlaylistId} after {ElapsedTime}ms", id, stopwatch.ElapsedMilliseconds);
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "ID is validated as GUID before use, preventing path injection")]
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
                var playlist = await playlistStore.GetByIdAsync(guidId);
                if (playlist == null)
                {
                    return NotFound("Smart playlist not found");
                }

                // Handle the Jellyfin playlist based on user choice
                var playlistService = GetPlaylistService();
                if (deleteJellyfinPlaylist)
                {
                    await playlistService.DeleteAsync(playlist);
                    logger.LogInformation("Deleted smart playlist: {PlaylistName}", playlist.Name);
                }
                else
                {
                    // Remove the suffix/prefix from the playlist name
                    await playlistService.RemoveSmartSuffixAsync(playlist);
                    logger.LogInformation("Deleted smart playlist configuration: {PlaylistName}", playlist.Name);
                }

                // Then delete the smart playlist configuration
                await playlistStore.DeleteAsync(guidId).ConfigureAwait(false);

                // Remove the playlist from auto-refresh cache
                AutoRefreshService.Instance?.RemovePlaylistFromCache(id);

                return NoContent();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting smart playlist {PlaylistId}", id);
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
                    new { Value = "Name", Label = "Name" },
                    new { Value = "SeriesName", Label = "Series Name" },
                    new { Value = "SimilarTo", Label = "Similar To" },
                    new { Value = "OfficialRating", Label = "Parental Rating" },
                    new { Value = "Overview", Label = "Overview" },
                    new { Value = "ProductionYear", Label = "Production Year" },
                    new { Value = "ReleaseDate", Label = "Release Date" }
                    // Note: ItemType (Media Type) is intentionally excluded from UI fields
                    // because users select media type (Audio/Video) before creating rules
                },
                VideoFields = new[]
                {
                    new { Value = "Resolution", Label = "Resolution" },
                    new { Value = "Framerate", Label = "Framerate" },
                    new { Value = "VideoCodec", Label = "Video Codec" },
                    new { Value = "VideoProfile", Label = "Video Profile" },
                    new { Value = "VideoRange", Label = "Video Range" },
                    new { Value = "VideoRangeType", Label = "Video Range Type" },
                },
                AudioFields = new[]
                {
                    new { Value = "AudioLanguages", Label = "Audio Languages" },
                    new { Value = "AudioBitrate", Label = "Audio Bitrate (kbps)" },
                    new { Value = "AudioSampleRate", Label = "Audio Sample Rate (Hz)" },
                    new { Value = "AudioBitDepth", Label = "Audio Bit Depth" },
                    new { Value = "AudioCodec", Label = "Audio Codec" },
                    new { Value = "AudioProfile", Label = "Audio Profile" },
                    new { Value = "AudioChannels", Label = "Audio Channels" },
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
                    new { Value = "RuntimeMinutes", Label = "Runtime (Minutes)" },
                },

                FileFields = new[]
                {
                    new { Value = "FileName", Label = "File Name" },
                    new { Value = "FolderPath", Label = "Folder Path" },
                    new { Value = "DateModified", Label = "Date Modified" },
                },
                LibraryFields = new[]
                {
                    new { Value = "DateCreated", Label = "Date Added to Library" },
                    new { Value = "DateLastRefreshed", Label = "Last Metadata Refresh" },
                    new { Value = "DateLastSaved", Label = "Last Database Save" },
                },
                PeopleFields = new[]
                {
                    new { Value = "People", Label = "People" },
                },
                PeopleSubFields = new[]
                {
                    new { Value = "People", Label = "People (All)" },
                    new { Value = "Actors", Label = "Actors" },
                    new { Value = "Directors", Label = "Directors" },
                    new { Value = "Composers", Label = "Composers" },
                    new { Value = "Writers", Label = "Writers" },
                    new { Value = "GuestStars", Label = "Guest Stars" },
                    new { Value = "Producers", Label = "Producers" },
                    new { Value = "Conductors", Label = "Conductors" },
                    new { Value = "Lyricists", Label = "Lyricists" },
                    new { Value = "Arrangers", Label = "Arrangers" },
                    new { Value = "SoundEngineers", Label = "Sound Engineers" },
                    new { Value = "Mixers", Label = "Mixers" },
                    new { Value = "Remixers", Label = "Remixers" },
                    new { Value = "Creators", Label = "Creators" },
                    new { Value = "PersonArtists", Label = "Artists (Person Role)" },
                    new { Value = "PersonAlbumArtists", Label = "Album Artists (Person Role)" },
                    new { Value = "Authors", Label = "Authors" },
                    new { Value = "Illustrators", Label = "Illustrators" },
                    new { Value = "Pencilers", Label = "Pencilers" },
                    new { Value = "Inkers", Label = "Inkers" },
                    new { Value = "Colorists", Label = "Colorists" },
                    new { Value = "Letterers", Label = "Letterers" },
                    new { Value = "CoverArtists", Label = "Cover Artists" },
                    new { Value = "Editors", Label = "Editors" },
                    new { Value = "Translators", Label = "Translators" },
                },
                CollectionFields = new[]
                {
                    new { Value = "Collections", Label = "Collections" },
                    new { Value = "Genres", Label = "Genres" },
                    new { Value = "Studios", Label = "Studios" },
                    new { Value = "Tags", Label = "Tags" },
                    new { Value = "Artists", Label = "Artists" },
                    new { Value = "AlbumArtists", Label = "Album Artists" },
                },
                SimilarityComparisonFields = new[]
                {
                    new { Value = "Genre", Label = "Genre" },
                    new { Value = "Tags", Label = "Tags" },
                    new { Value = "Actors", Label = "Actors" },
                    new { Value = "Writers", Label = "Writers" },
                    new { Value = "Producers", Label = "Producers" },
                    new { Value = "Directors", Label = "Directors" },
                    new { Value = "Studios", Label = "Studios" },
                    new { Value = "Audio Languages", Label = "Audio Languages" },
                    new { Value = "Name", Label = "Name" },
                    new { Value = "Production Year", Label = "Production Year" },
                    new { Value = "Parental Rating", Label = "Parental Rating" },
                },
                Operators = Core.Constants.Operators.AllOperators,
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
                    new { Value = "CommunityRating Descending", Label = "Community Rating Descending" },
                    new { Value = "Similarity Ascending", Label = "Similarity Ascending" },
                    new { Value = "Similarity Descending", Label = "Similarity Descending" },
                    new { Value = "PlayCount (owner) Ascending", Label = "Play Count (owner) Ascending" },
                    new { Value = "PlayCount (owner) Descending", Label = "Play Count (owner) Descending" },
                }
            };

            return Ok(fields);
        }

        /// <summary>
        /// Static readonly field operators dictionary for performance optimization.
        /// </summary>
        private static readonly Dictionary<string, string[]> _fieldOperators = Core.Constants.Operators.GetFieldOperatorsDictionary();

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
                    .Select(u => new
                    {
                        u.Id,
                        Name = u.Username,
                    })
                    .OrderBy(u => u.Name)
                    .ToList();

                return Ok(users);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving users");
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

                return Ok(new
                {
                    user.Id,
                    Name = user.Username,
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting current user");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error getting current user");
            }
        }

        /// <summary>
        /// Enable a smart playlist.
        /// </summary>
        /// <param name="id">The playlist ID.</param>
        /// <returns>Success message.</returns>
        [HttpPost("{id}/enable")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "ID is validated as GUID before use, preventing path injection")]
        public async Task<ActionResult> EnableSmartPlaylist([FromRoute, Required] string id)
        {
            try
            {
                if (!Guid.TryParse(id, out var guidId))
                {
                    return BadRequest("Invalid playlist ID format");
                }

                var playlistStore = GetPlaylistStore();
                var playlist = await playlistStore.GetByIdAsync(guidId);
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
                    var (success, message, jellyfinPlaylistId) = await playlistService.RefreshWithTimeoutAsync(playlist);

                    if (!success)
                    {
                        logger.LogWarning("Failed to enable playlist {PlaylistName}: {Message}", playlist.Name, message);
                        throw new InvalidOperationException(message);
                    }

                    // If refresh was successful, save the Jellyfin playlist ID
                    if (!string.IsNullOrEmpty(jellyfinPlaylistId))
                    {
                        playlist.JellyfinPlaylistId = jellyfinPlaylistId;
                        logger.LogDebug("Captured Jellyfin playlist ID {JellyfinPlaylistId} for smart playlist {PlaylistName}",
                            jellyfinPlaylistId, playlist.Name);
                    }

                    // Only save the configuration if the Jellyfin operation succeeds
                    await playlistStore.SaveAsync(playlist);

                    // Update the auto-refresh cache with the enabled playlist
                    AutoRefreshService.Instance?.UpdatePlaylistInCache(playlist);

                    logger.LogInformation("Enabled smart playlist: {PlaylistId} - {PlaylistName}", id, playlist.Name);
                    return Ok(new { message = $"Smart playlist '{playlist.Name}' has been enabled" });
                }
                catch (Exception jellyfinEx)
                {
                    // Restore original state if Jellyfin operation fails
                    playlist.Enabled = originalEnabledState;
                    logger.LogError(jellyfinEx, "Failed to enable Jellyfin playlist for {PlaylistId} - {PlaylistName}", id, playlist.Name);
                    throw;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error enabling smart playlist {PlaylistId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error enabling smart playlist");
            }
        }

        /// <summary>
        /// Disable a smart playlist.
        /// </summary>
        /// <param name="id">The playlist ID.</param>
        /// <returns>Success message.</returns>
        [HttpPost("{id}/disable")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "ID is validated as GUID before use, preventing path injection")]
        public async Task<ActionResult> DisableSmartPlaylist([FromRoute, Required] string id)
        {
            try
            {
                if (!Guid.TryParse(id, out var guidId))
                {
                    return BadRequest("Invalid playlist ID format");
                }

                var playlistStore = GetPlaylistStore();
                var playlist = await playlistStore.GetByIdAsync(guidId);
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
                    await playlistService.DisableAsync(playlist);

                    // Clear the Jellyfin playlist ID since the playlist no longer exists
                    playlist.JellyfinPlaylistId = null;

                    // Only save the configuration if the Jellyfin operation succeeds
                    await playlistStore.SaveAsync(playlist);

                    // Update the auto-refresh cache with the disabled playlist
                    AutoRefreshService.Instance?.UpdatePlaylistInCache(playlist);

                    logger.LogInformation("Disabled smart playlist: {PlaylistId} - {PlaylistName}", id, playlist.Name);
                    return Ok(new { message = $"Smart playlist '{playlist.Name}' has been disabled" });
                }
                catch (Exception jellyfinEx)
                {
                    // Restore original state if Jellyfin operation fails
                    playlist.Enabled = originalEnabledState;
                    logger.LogError(jellyfinEx, "Failed to disable Jellyfin playlist for {PlaylistId} - {PlaylistName}", id, playlist.Name);
                    throw;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error disabling smart playlist {PlaylistId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error disabling smart playlist");
            }
        }

        /// <summary>
        /// Trigger a refresh of a specific smart playlist.
        /// </summary>
        /// <param name="id">The playlist ID.</param>
        /// <returns>Success message.</returns>
        [HttpPost("{id}/refresh")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "ID is validated as GUID before use, preventing path injection")]
        public async Task<ActionResult> TriggerSinglePlaylistRefresh([FromRoute, Required] string id)
        {
            try
            {
                if (!Guid.TryParse(id, out var guidId))
                {
                    return BadRequest("Invalid playlist ID format");
                }

                var playlistStore = GetPlaylistStore();
                var playlist = await playlistStore.GetByIdAsync(guidId);
                if (playlist == null)
                {
                    return NotFound("Smart playlist not found");
                }

                // Use the manual refresh service for consistency
                var (success, message, jellyfinPlaylistId) = await _manualRefreshService.RefreshSinglePlaylistAsync(playlist);

                if (success)
                {
                    // Update the playlist with the returned Jellyfin playlist ID only if it's valid
                    if (!string.IsNullOrEmpty(jellyfinPlaylistId))
                    {
                        playlist.JellyfinPlaylistId = jellyfinPlaylistId;
                        logger.LogDebug("Updated JellyfinPlaylistId to {JellyfinPlaylistId} for manually refreshed playlist: {PlaylistName}",
                            jellyfinPlaylistId, playlist.Name);
                    }
                    else if (string.IsNullOrEmpty(playlist.JellyfinPlaylistId))
                    {
                        logger.LogWarning("No Jellyfin playlist ID returned for manually refreshed playlist: {PlaylistName}, and no existing ID to preserve",
                            playlist.Name);
                    }
                    else
                    {
                        logger.LogWarning("No Jellyfin playlist ID returned for manually refreshed playlist: {PlaylistName}, preserving existing ID: {ExistingId}",
                            playlist.Name, playlist.JellyfinPlaylistId);
                    }

                    // Save the playlist to persist LastRefreshed timestamp (and JellyfinPlaylistId if updated)
                    await playlistStore.SaveAsync(playlist);

                    return Ok(new { message = $"Smart playlist '{playlist.Name}' has been refreshed successfully" });
                }
                else
                {
                    return BadRequest(new { message });
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error refreshing single smart playlist {PlaylistId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error refreshing smart playlist");
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
                // Use ManualRefreshService to refresh all playlists directly
                var result = await _manualRefreshService.RefreshAllPlaylistsAsync();

                if (result.Success)
                {
                    return Ok(new { message = result.Message });
                }
                else
                {
                    // Map "already in progress" to HTTP 409 Conflict for better API semantics
                    if (result.Message.Contains("already in progress"))
                    {
                        return Conflict(new { message = result.Message });
                    }
                    return StatusCode(StatusCodes.Status500InternalServerError, result.Message);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error triggering smart playlist refresh");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error triggering smart playlist refresh");
            }
        }

        /// <summary>
        /// Directly refresh all smart playlists.
        /// This method processes all enabled playlists sequentially for each user.
        /// </summary>
        /// <returns>Success message.</returns>
        [HttpPost("refresh-direct")]
        public async Task<ActionResult> RefreshAllPlaylistsDirect()
        {
            try
            {
                // The ManualRefreshService now handles lock acquisition internally for the entire operation
                var result = await _manualRefreshService.RefreshAllPlaylistsAsync();

                if (result.Success)
                {
                    return Ok(new { message = result.Message });
                }
                else
                {
                    // Map "already in progress" to HTTP 409 Conflict for better API semantics
                    if (result.Message.Contains("already in progress"))
                    {
                        return Conflict(new { message = result.Message });
                    }
                    return StatusCode(StatusCodes.Status500InternalServerError, result.Message);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Manual playlist refresh was cancelled by client");
                return StatusCode(499, "Refresh operation was cancelled");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during manual playlist refresh");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error during manual playlist refresh");
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
                var fileSystem = new SmartListFileSystem(_applicationPaths);
                var filePaths = fileSystem.GetAllSmartListFilePaths();

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
                var zipFileName = $"smartlists_export_{timestamp}.zip";

                logger.LogInformation("Exported {PlaylistCount} smart playlists to {FileName}", filePaths.Length, zipFileName);

                return File(zipStream.ToArray(), "application/zip", zipFileName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error exporting smart playlists");
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
                var existingPlaylists = await playlistStore.GetAllAsync();
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
                        continue; // Skip non-JSON files,
                    }

                    // Skip system files (like macOS ._filename files)
                    if (entry.Name.StartsWith("._") || entry.Name.StartsWith(".DS_Store"))
                    {
                        logger.LogDebug("Skipping system file: {FileName}", entry.Name);
                        continue;
                    }

                    try
                    {
                        using var entryStream = entry.Open();
                        var playlist = await JsonSerializer.DeserializeAsync<SmartPlaylistDto>(entryStream);

                        if (playlist == null || string.IsNullOrEmpty(playlist.Id))
                        {
                            logger.LogWarning("Invalid playlist data in file {FileName}: {Issue}",
                                entry.Name, playlist == null ? "null playlist" : "empty ID");
                            importResults.Add(new { fileName = entry.Name, status = "error", message = "Invalid or empty playlist data" });
                            errorCount++;
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(playlist.Name))
                        {
                            logger.LogWarning("Playlist in file {FileName} has no name", entry.Name);
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
                                        logger.LogWarning("Playlist '{PlaylistName}' references non-existent user {UserId} but cannot determine importing user for reassignment",
                                            playlist.Name, playlist.UserId);
                                        importResults.Add(new { fileName = entry.Name, status = "error", message = "Cannot reassign playlist - unable to determine importing user" });
                                        errorCount++;
                                        continue; // Skip this entire playlist,
                                    }
                                }

                                logger.LogWarning("Playlist '{PlaylistName}' references non-existent user {UserId}, reassigning to importing user {CurrentUserId}",
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
                            logger.LogInformation("Reassigned user references in playlist '{PlaylistName}' due to non-existent users", playlist.Name);
                        }

                        if (existingIds.Contains(playlist.Id))
                        {
                            importResults.Add(new { fileName = entry.Name, playlistName = playlist.Name, status = "skipped", message = "Playlist with this ID already exists" });
                            skippedCount++;
                            continue;
                        }

                        // Import the playlist
                        await playlistStore.SaveAsync(playlist);

                        // Update the auto-refresh cache with the imported playlist
                        AutoRefreshService.Instance?.UpdatePlaylistInCache(playlist);

                        importResults.Add(new { fileName = entry.Name, playlistName = playlist.Name, status = "imported", message = "Successfully imported" });
                        importedCount++;

                        logger.LogDebug("Imported playlist {PlaylistName} (ID: {PlaylistId}) from {FileName}",
                            playlist.Name, playlist.Id, entry.Name);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error importing playlist from {FileName}", entry.Name);
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
                    details = importResults,
                };

                logger.LogInformation("Import completed: {Imported} imported, {Skipped} skipped, {Errors} errors",
                    importedCount, skippedCount, errorCount);

                return Ok(summary);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error importing smart playlists");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error importing smart playlists");
            }
        }

        /// <summary>
        /// Restart the schedule timer (useful for debugging timer issues)
        /// </summary>
        [HttpPost("Timer/Restart")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult RestartScheduleTimer()
        {
            try
            {
                var autoRefreshService = AutoRefreshService.Instance;
                if (autoRefreshService == null)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, "AutoRefreshService is not available");
                }

                autoRefreshService.RestartScheduleTimer();

                var nextCheck = autoRefreshService.GetNextScheduledCheckTime();
                var isRunning = autoRefreshService.IsScheduleTimerRunning();

                return Ok(new
                {
                    message = "Schedule timer restarted successfully",
                    isRunning = isRunning,
                    nextScheduledCheck = nextCheck?.ToString("o"),
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error restarting schedule timer");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error restarting schedule timer");
            }
        }

        /// <summary>
        /// Get schedule timer status
        /// </summary>
        [HttpGet("Timer/Status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult GetScheduleTimerStatus()
        {
            try
            {
                var autoRefreshService = AutoRefreshService.Instance;
                if (autoRefreshService == null)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, "AutoRefreshService is not available");
                }

                var isRunning = autoRefreshService.IsScheduleTimerRunning();
                var nextCheck = autoRefreshService.GetNextScheduledCheckTime();

                return Ok(new
                {
                    isRunning = isRunning,
                    nextScheduledCheck = nextCheck?.ToString("o"),
                    currentTime = DateTime.Now.ToString("o"),
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting schedule timer status");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error getting schedule timer status");
            }
        }


    }
}