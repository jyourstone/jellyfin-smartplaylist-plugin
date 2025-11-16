using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SmartLists.Core;
using Jellyfin.Plugin.SmartLists.Core.Constants;
using Jellyfin.Plugin.SmartLists.Core.Models;
using Jellyfin.Plugin.SmartLists.Services.Playlists;
using Jellyfin.Plugin.SmartLists.Services.Collections;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using AutoRefreshService = Jellyfin.Plugin.SmartLists.Services.Shared.AutoRefreshService;
using Jellyfin.Plugin.SmartLists.Utilities;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Collections;
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
        ICollectionManager collectionManager,
        IUserDataManager userDataManager,
        IProviderManager providerManager,
        IManualRefreshService manualRefreshService,
        RefreshStatusService refreshStatusService) : ControllerBase
    {
        private readonly IServerApplicationPaths _applicationPaths = applicationPaths;
        private readonly IUserManager _userManager = userManager;
        private readonly ILibraryManager _libraryManager = libraryManager;
        private readonly IPlaylistManager _playlistManager = playlistManager;
        private readonly ICollectionManager _collectionManager = collectionManager;
        private readonly IUserDataManager _userDataManager = userDataManager;
        private readonly IProviderManager _providerManager = providerManager;
        private readonly IManualRefreshService _manualRefreshService = manualRefreshService;
        private readonly RefreshStatusService _refreshStatusService = refreshStatusService;

        private Services.Playlists.PlaylistStore GetPlaylistStore()
        {
            var fileSystem = new SmartListFileSystem(_applicationPaths);
            return new Services.Playlists.PlaylistStore(fileSystem);
        }

        private Services.Collections.CollectionStore GetCollectionStore()
        {
            var fileSystem = new SmartListFileSystem(_applicationPaths);
            return new Services.Collections.CollectionStore(fileSystem);
        }

        private Services.Playlists.PlaylistService GetPlaylistService()
        {
            try
            {
                // Use a generic wrapper logger that implements ILogger<PlaylistService>
                var playlistServiceLogger = new ServiceLoggerAdapter<Services.Playlists.PlaylistService>(logger);
                return new Services.Playlists.PlaylistService(_userManager, _libraryManager, _playlistManager, _userDataManager, playlistServiceLogger, _providerManager);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create PlaylistService");
                throw;
            }
        }

        private Services.Collections.CollectionService GetCollectionService()
        {
            try
            {
                // Use a generic wrapper logger that implements ILogger<CollectionService>
                var collectionServiceLogger = new ServiceLoggerAdapter<Services.Collections.CollectionService>(logger);
                return new Services.Collections.CollectionService(_libraryManager, _collectionManager, _userManager, _userDataManager, collectionServiceLogger, _providerManager);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create CollectionService");
                throw;
            }
        }

        // Generic wrapper class to adapt the controller logger for service-specific loggers
        private sealed class ServiceLoggerAdapter<T>(ILogger logger) : ILogger<T>
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
            // If User field is set and not empty, parse and return it
            if (!string.IsNullOrEmpty(playlist.UserId) && Guid.TryParse(playlist.UserId, out var userId) && userId != Guid.Empty)
            {
                return userId;
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
        /// Get all smart lists (playlists and collections).
        /// </summary>
        /// <param name="type">Optional filter by type (Playlist or Collection).</param>
        /// <returns>List of smart lists.</returns>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SmartListDto>>> GetSmartLists([FromQuery] string? type = null)
        {
            try
            {
                var allLists = new List<SmartListDto>();
                
                // Get playlists
                if (type == null || type.Equals("Playlist", StringComparison.OrdinalIgnoreCase))
                {
                    var playlistStore = GetPlaylistStore();
                    var playlists = await playlistStore.GetAllAsync();
                    allLists.AddRange(playlists);
                }
                
                // Get collections
                if (type == null || type.Equals("Collection", StringComparison.OrdinalIgnoreCase))
                {
                    var collectionStore = GetCollectionStore();
                    var collections = await collectionStore.GetAllAsync();
                    allLists.AddRange(collections);
                }
                
                return Ok(allLists);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving smart lists");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving smart lists");
            }
        }

        /// <summary>
        /// Get a specific smart list by ID (playlist or collection).
        /// </summary>
        /// <param name="id">The list ID.</param>
        /// <returns>The smart list.</returns>
        [HttpGet("{id}")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "ID is validated as GUID before use, preventing path injection")]
        public async Task<ActionResult<SmartListDto>> GetSmartList([FromRoute, Required] string id)
        {
            try
            {
                if (!Guid.TryParse(id, out var guidId))
                {
                    return BadRequest("Invalid list ID format");
                }

                // Try playlist first
                var playlistStore = GetPlaylistStore();
                var playlist = await playlistStore.GetByIdAsync(guidId);
                if (playlist != null)
                {
                    return Ok(playlist);
                }

                // Try collection
                var collectionStore = GetCollectionStore();
                var collection = await collectionStore.GetByIdAsync(guidId);
                if (collection != null)
                {
                    return Ok(collection);
                }

                return NotFound();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving smart list {ListId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving smart list");
            }
        }

        /// <summary>
        /// Create a new smart list (playlist or collection).
        /// </summary>
        /// <param name="list">The smart list to create (playlist or collection).</param>
        /// <returns>The created smart list.</returns>
        [HttpPost]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3012:Review code for regex injection vulnerabilities", Justification = "Regex patterns are validated with IsValidRegexPattern method including length limits and timeout")]
        public async Task<ActionResult<SmartListDto>> CreateSmartList([FromBody] SmartListDto? list)
        {
            if (list == null)
            {
                logger.LogWarning("CreateSmartList called with null list data");
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = "List data is required",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            // Route to appropriate handler based on type
            if (list.Type == Core.Enums.SmartListType.Collection)
            {
                return await CreateCollectionInternal(list as SmartCollectionDto ?? JsonSerializer.Deserialize<SmartCollectionDto>(JsonSerializer.Serialize(list))!);
            }
            else
            {
                return await CreatePlaylistInternal(list as SmartPlaylistDto ?? JsonSerializer.Deserialize<SmartPlaylistDto>(JsonSerializer.Serialize(list))!);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3012:Review code for regex injection vulnerabilities", Justification = "Regex patterns are validated with IsValidRegexPattern method including length limits and timeout")]
        private async Task<ActionResult<SmartListDto>> CreatePlaylistInternal(SmartPlaylistDto playlist)
        {

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

            if (string.IsNullOrEmpty(playlist.UserId) || !Guid.TryParse(playlist.UserId, out var playlistUserId) || playlistUserId == Guid.Empty)
            {
                logger.LogWarning("CreateSmartPlaylist called with empty or invalid User. Name={Name}", playlist.Name);
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = "Playlist owner (User) is required",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var stopwatch = Stopwatch.StartNew();
            logger.LogDebug("CreateSmartPlaylist called for playlist: {PlaylistName}", playlist.Name);
            logger.LogDebug("Playlist data received: Name={Name}, User={User}, Public={Public}, ExpressionSets={ExpressionSetCount}, MediaTypes={MediaTypes}",
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
                var (success, message, jellyfinPlaylistId) = await playlistService.RefreshWithTimeoutAsync(
                    createdPlaylist, 
                    progressCallback: null,
                    refreshStatusService: _refreshStatusService,
                    triggerType: Core.Enums.RefreshTriggerType.Manual,
                    cancellationToken: default);

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
                    return CreatedAtAction(nameof(GetSmartList), new { id = createdPlaylist.Id }, createdPlaylist);
                }

                // DEBUG: Check the MediaType of the created Jellyfin playlist
                try
                {
                    if (Guid.TryParse(createdPlaylist.UserId, out var userId))
                    {
                        var user = _userManager.GetUserById(userId);
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
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Error checking MediaType of created playlist");
                }

                logger.LogDebug("Finished RefreshWithTimeoutAsync for {PlaylistName} in {ElapsedTime}ms", playlist.Name, stopwatch.ElapsedMilliseconds);

                return CreatedAtAction(nameof(GetSmartList), new { id = createdPlaylist.Id }, createdPlaylist);
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3012:Review code for regex injection vulnerabilities", Justification = "Regex patterns are validated with IsValidRegexPattern method including length limits and timeout")]
        private async Task<ActionResult<SmartListDto>> CreateCollectionInternal(SmartCollectionDto collection)
        {
            // Set defaults for optional fields
            if (string.IsNullOrEmpty(collection.Id))
            {
                collection.Id = Guid.NewGuid().ToString();
            }

            if (collection.Order == null)
            {
                collection.Order = new OrderDto { SortOptions = [] };
            }
            else if (collection.Order.SortOptions == null || collection.Order.SortOptions.Count == 0)
            {
                collection.Order.SortOptions = [];
            }

            // Set default owner user if not specified
            if (string.IsNullOrEmpty(collection.UserId) || !Guid.TryParse(collection.UserId, out var userId) || userId == Guid.Empty)
            {
                // Default to currently logged-in user
                var currentUserId = GetCurrentUserId();
                
                if (currentUserId != Guid.Empty)
                {
                    var currentUser = _userManager.GetUserById(currentUserId);
                    if (currentUser != null)
                    {
                        collection.UserId = currentUser.Id.ToString("D");
                        logger.LogDebug("Set default collection owner to currently logged-in user: {Username} ({UserId})", currentUser.Username, currentUser.Id);
                    }
                    else
                    {
                        logger.LogWarning("Current user ID {UserId} not found, falling back to first user", currentUserId);
                        var defaultUser = _userManager.Users.FirstOrDefault();
                        if (defaultUser != null)
                        {
                            collection.UserId = defaultUser.Id.ToString("D");
                            logger.LogDebug("Set default collection owner to first user: {Username} ({UserId})", defaultUser.Username, defaultUser.Id);
                        }
                    }
                }
                else
                {
                    logger.LogWarning("Could not determine current user, falling back to first user");
                    var defaultUser = _userManager.Users.FirstOrDefault();
                    if (defaultUser != null)
                    {
                        collection.UserId = defaultUser.Id.ToString("D");
                        logger.LogDebug("Set default collection owner to first user: {Username} ({UserId})", defaultUser.Username, defaultUser.Id);
                    }
                }
                
                if (string.IsNullOrEmpty(collection.UserId))
                {
                    logger.LogError("No users found to set as collection owner");
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Configuration Error",
                        Detail = "No users found. At least one user must exist to create collections.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }
            }

            // Ensure Type is set correctly
            collection.Type = Core.Enums.SmartListType.Collection;

            // Validate required fields
            if (string.IsNullOrWhiteSpace(collection.Name))
            {
                logger.LogWarning("CreateCollectionInternal called with empty Name");
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = "Collection name is required",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var stopwatch = Stopwatch.StartNew();
            logger.LogDebug("CreateCollectionInternal called for collection: {CollectionName}", collection.Name);

            try
            {
                if (string.IsNullOrEmpty(collection.Id))
                {
                    collection.Id = Guid.NewGuid().ToString();
                    logger.LogDebug("Generated new collection ID: {Id}", collection.Id);
                }

                if (string.IsNullOrEmpty(collection.FileName))
                {
                    collection.FileName = $"{collection.Id}.json";
                }

                if (collection.Order == null)
                {
                    collection.Order = new OrderDto { SortOptions = [] };
                }

                var collectionStore = GetCollectionStore();

                // Check for duplicate collection names (Jellyfin doesn't allow collections with the same name)
                var formattedName = NameFormatter.FormatPlaylistName(collection.Name);
                var allCollections = await collectionStore.GetAllAsync();
                var duplicateCollection = allCollections.FirstOrDefault(c => 
                    c.Id != collection.Id && 
                    string.Equals(NameFormatter.FormatPlaylistName(c.Name), formattedName, StringComparison.OrdinalIgnoreCase));
                
                if (duplicateCollection != null)
                {
                    logger.LogWarning("Cannot create collection '{CollectionName}' - a collection with this name already exists", collection.Name);
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Validation Error",
                        Detail = $"A collection named '{formattedName}' already exists. Jellyfin does not allow multiple collections with the same name.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                // Validate regex patterns before saving
                if (collection.ExpressionSets != null)
                {
                    foreach (var expressionSet in collection.ExpressionSets)
                    {
                        if (expressionSet.Expressions != null)
                        {
                            foreach (var expression in expressionSet.Expressions)
                            {
                                if (expression.Operator == "MatchRegex" && !string.IsNullOrEmpty(expression.TargetValue))
                                {
                                    if (!IsValidRegexPattern(expression.TargetValue, out var validationError))
                                    {
                                        return BadRequest($"Invalid regex pattern: {validationError}");
                                    }

                                    try
                                    {
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

                // Set DateCreated to current time for new collections
                collection.DateCreated = DateTime.UtcNow;

                var createdCollection = await collectionStore.SaveAsync(collection);
                logger.LogInformation("Created smart collection: {CollectionName}", collection.Name);

                // Update the auto-refresh cache with the new collection
                AutoRefreshService.Instance?.UpdateCollectionInCache(createdCollection);

                // Clear the rule cache
                SmartList.ClearRuleCache(logger);
                logger.LogDebug("Cleared rule cache after creating collection '{CollectionName}'", collection.Name);

                logger.LogDebug("Calling RefreshWithTimeoutAsync for {CollectionName}", collection.Name);
                var collectionService = GetCollectionService();
                var (success, message, jellyfinCollectionId) = await collectionService.RefreshWithTimeoutAsync(
                    createdCollection, 
                    progressCallback: null,
                    refreshStatusService: _refreshStatusService,
                    triggerType: Core.Enums.RefreshTriggerType.Manual,
                    cancellationToken: default);

                // If refresh was successful, save the Jellyfin collection ID
                if (success && !string.IsNullOrEmpty(jellyfinCollectionId))
                {
                    createdCollection.JellyfinCollectionId = jellyfinCollectionId;
                    await collectionStore.SaveAsync(createdCollection);
                    logger.LogDebug("Saved Jellyfin collection ID {JellyfinCollectionId} for smart collection {CollectionName}",
                        jellyfinCollectionId, createdCollection.Name);
                }
                stopwatch.Stop();

                if (!success)
                {
                    logger.LogWarning("Failed to refresh newly created collection {CollectionName}: {Message}", collection.Name, message);
                    return CreatedAtAction(nameof(GetSmartList), new { id = createdCollection.Id }, createdCollection);
                }

                logger.LogDebug("Finished RefreshWithTimeoutAsync for {CollectionName} in {ElapsedTime}ms", collection.Name, stopwatch.ElapsedMilliseconds);

                return CreatedAtAction(nameof(GetSmartList), new { id = createdCollection.Id }, createdCollection);
            }
            catch (ArgumentException ex) when (ex.Message.Contains("Invalid regex pattern"))
            {
                stopwatch.Stop();
                logger.LogError(ex, "Unexpected regex validation error in smart collection creation after {ElapsedTime}ms", stopwatch.ElapsedMilliseconds);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger.LogError(ex, "Error creating smart collection after {ElapsedTime}ms", stopwatch.ElapsedMilliseconds);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error creating smart collection");
            }
        }

        /// <summary>
        /// Update an existing smart list (playlist or collection).
        /// </summary>
        /// <param name="id">The list ID.</param>
        /// <param name="list">The updated smart list.</param>
        /// <returns>The updated smart list.</returns>
        [HttpPut("{id}")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "ID is validated as GUID before use, preventing path injection")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3012:Review code for regex injection vulnerabilities", Justification = "Regex patterns are validated with IsValidRegexPattern method including length limits and timeout")]
        public async Task<ActionResult<SmartListDto>> UpdateSmartList([FromRoute, Required] string id, [FromBody, Required] SmartListDto list)
        {
            if (list == null)
            {
                return BadRequest("List data is required");
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                if (!Guid.TryParse(id, out var guidId))
                {
                    return BadRequest("Invalid list ID format");
                }

                // Determine type and route to appropriate handler
                // Try to find existing list to determine type
                var playlistStore = GetPlaylistStore();
                var existingPlaylist = await playlistStore.GetByIdAsync(guidId);
                if (existingPlaylist != null)
                {
                    // Handle type conversion: playlist → collection
                    if (list.Type == Core.Enums.SmartListType.Collection)
                    {
                        logger.LogInformation("Converting playlist '{Name}' to collection", existingPlaylist.Name);
                        
                        // Convert to collection DTO and create as new collection
                        var collectionDto = list as SmartCollectionDto ?? JsonSerializer.Deserialize<SmartCollectionDto>(JsonSerializer.Serialize(list))!;
                        collectionDto.Id = id; // Keep the same ID
                        collectionDto.FileName = existingPlaylist.FileName; // Keep the same filename
                        collectionDto.JellyfinCollectionId = null; // Clear old Jellyfin ID
                        
                        // Create the new Jellyfin collection first (this populates JellyfinCollectionId)
                        var collectionService = GetCollectionService();
                        var refreshResult = await collectionService.RefreshWithTimeoutAsync(
                            collectionDto,
                            progressCallback: null,
                            refreshStatusService: _refreshStatusService,
                            triggerType: Core.Enums.RefreshTriggerType.Manual,
                            cancellationToken: default);
                        
                        if (!refreshResult.Success)
                        {
                            logger.LogError("Failed to create collection during conversion: {Message}", refreshResult.Message);
                            return StatusCode(StatusCodes.Status500InternalServerError, $"Failed to create collection: {refreshResult.Message}");
                        }
                        
                        // Save to collection store with the populated JellyfinCollectionId
                        var newCollectionStore = GetCollectionStore();
                        await newCollectionStore.SaveAsync(collectionDto);
                        
                        // Only delete the old playlist after successful conversion
                        var playlistService = GetPlaylistService();
                        await playlistService.DeleteAsync(existingPlaylist);
                        await playlistStore.DeleteAsync(guidId);
                        
                        logger.LogInformation("Successfully converted playlist to collection '{Name}' (JellyfinCollectionId: {Id})", 
                            collectionDto.Name, collectionDto.JellyfinCollectionId);
                        return Ok(collectionDto);
                    }
                    
                    // Normal playlist update
                    return await UpdatePlaylistInternal(id, guidId, list as SmartPlaylistDto ?? JsonSerializer.Deserialize<SmartPlaylistDto>(JsonSerializer.Serialize(list))!);
                }

                var collectionStore = GetCollectionStore();
                var existingCollection = await collectionStore.GetByIdAsync(guidId);
                if (existingCollection != null)
                {
                    // Handle type conversion: collection → playlist
                    if (list.Type == Core.Enums.SmartListType.Playlist)
                    {
                        logger.LogInformation("Converting collection '{Name}' to playlist", existingCollection.Name);
                        
                        // Convert to playlist DTO and create as new playlist
                        var playlistDto = list as SmartPlaylistDto ?? JsonSerializer.Deserialize<SmartPlaylistDto>(JsonSerializer.Serialize(list))!;
                        playlistDto.Id = id; // Keep the same ID
                        playlistDto.FileName = existingCollection.FileName; // Keep the same filename
                        playlistDto.JellyfinPlaylistId = null; // Clear old Jellyfin ID
                        
                        // Ensure User field is set (required for playlists)
                        if (string.IsNullOrEmpty(playlistDto.UserId))
                        {
                            playlistDto.UserId = existingCollection.UserId; // Carry over from collection
                        }
                        
                        // Create the new Jellyfin playlist first (this populates JellyfinPlaylistId)
                        var playlistService = GetPlaylistService();
                        var refreshResult = await playlistService.RefreshWithTimeoutAsync(
                            playlistDto,
                            progressCallback: null,
                            refreshStatusService: _refreshStatusService,
                            triggerType: Core.Enums.RefreshTriggerType.Manual,
                            cancellationToken: default);
                        
                        if (!refreshResult.Success)
                        {
                            logger.LogError("Failed to create playlist during conversion: {Message}", refreshResult.Message);
                            return StatusCode(StatusCodes.Status500InternalServerError, $"Failed to create playlist: {refreshResult.Message}");
                        }
                        
                        // Save to playlist store with the populated JellyfinPlaylistId
                        var newPlaylistStore = GetPlaylistStore();
                        await newPlaylistStore.SaveAsync(playlistDto);
                        
                        // Only delete the old collection after successful conversion
                        var collectionService = GetCollectionService();
                        await collectionService.DeleteAsync(existingCollection);
                        await collectionStore.DeleteAsync(guidId);
                        
                        logger.LogInformation("Successfully converted collection to playlist '{Name}' (JellyfinPlaylistId: {Id})", 
                            playlistDto.Name, playlistDto.JellyfinPlaylistId);
                        return Ok(playlistDto);
                    }
                    
                    // Normal collection update
                    return await UpdateCollectionInternal(id, guidId, list as SmartCollectionDto ?? JsonSerializer.Deserialize<SmartCollectionDto>(JsonSerializer.Serialize(list))!);
                }

                return NotFound("Smart list not found");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger.LogError(ex, "Error updating smart list {ListId} after {ElapsedTime}ms", id, stopwatch.ElapsedMilliseconds);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error updating smart list");
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3012:Review code for regex injection vulnerabilities", Justification = "Regex patterns are validated with IsValidRegexPattern method including length limits and timeout")]
        private async Task<ActionResult<SmartListDto>> UpdatePlaylistInternal(string id, Guid guidId, SmartPlaylistDto playlist)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var playlistStore = GetPlaylistStore();
                var existingPlaylist = await playlistStore.GetByIdAsync(guidId);
                if (existingPlaylist == null)
                {
                    return NotFound("Smart playlist not found");
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

                // Preserve User if not provided (frontend might not send it during edit)
                var originalUserId = GetPlaylistUserId(existingPlaylist);
                var newUserIdParsed = Guid.Empty;
                if ((string.IsNullOrEmpty(playlist.UserId) || !Guid.TryParse(playlist.UserId, out newUserIdParsed) || newUserIdParsed == Guid.Empty) && originalUserId != Guid.Empty)
                {
                    playlist.UserId = originalUserId.ToString("D");
                    newUserIdParsed = originalUserId;
                }
                else if (!string.IsNullOrEmpty(playlist.UserId) && !Guid.TryParse(playlist.UserId, out newUserIdParsed))
                {
                    newUserIdParsed = Guid.Empty;
                }
                var newUserId = newUserIdParsed;

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
                var (success, message, jellyfinPlaylistId) = await playlistService.RefreshWithTimeoutAsync(
                    updatedPlaylist, 
                    progressCallback: null,
                    refreshStatusService: _refreshStatusService,
                    triggerType: Core.Enums.RefreshTriggerType.Manual,
                    cancellationToken: default);

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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3012:Review code for regex injection vulnerabilities", Justification = "Regex patterns are validated with IsValidRegexPattern method including length limits and timeout")]
        private async Task<ActionResult<SmartListDto>> UpdateCollectionInternal(string id, Guid guidId, SmartCollectionDto collection)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var collectionStore = GetCollectionStore();
                var existingCollection = await collectionStore.GetByIdAsync(guidId);
                if (existingCollection == null)
                {
                    return NotFound("Smart collection not found");
                }

                // Set default owner user if not specified (same as CreateCollectionInternal)
                if (string.IsNullOrEmpty(collection.UserId) || !Guid.TryParse(collection.UserId, out var userId) || userId == Guid.Empty)
                {
                    // Default to currently logged-in user
                    var currentUserId = GetCurrentUserId();
                    
                    if (currentUserId != Guid.Empty)
                    {
                        var currentUser = _userManager.GetUserById(currentUserId);
                        if (currentUser != null)
                        {
                            collection.UserId = currentUser.Id.ToString("D");
                            logger.LogDebug("Set default collection owner to currently logged-in user during update: {Username} ({UserId})", currentUser.Username, currentUser.Id);
                        }
                        else
                        {
                            logger.LogWarning("Current user ID {UserId} not found during update, falling back to first user", currentUserId);
                            var defaultUser = _userManager.Users.FirstOrDefault();
                            if (defaultUser != null)
                            {
                                collection.UserId = defaultUser.Id.ToString("D");
                                logger.LogDebug("Set default collection owner to first user during update: {Username} ({UserId})", defaultUser.Username, defaultUser.Id);
                            }
                        }
                    }
                    else
                    {
                        logger.LogWarning("Could not determine current user during update, falling back to first user");
                        var defaultUser = _userManager.Users.FirstOrDefault();
                        if (defaultUser != null)
                        {
                            collection.UserId = defaultUser.Id.ToString("D");
                            logger.LogDebug("Set default collection owner to first user during update: {Username} ({UserId})", defaultUser.Username, defaultUser.Id);
                        }
                    }
                    
                    if (string.IsNullOrEmpty(collection.UserId))
                    {
                        logger.LogError("No users found to set as collection owner during update");
                        return BadRequest(new ProblemDetails
                        {
                            Title = "Configuration Error",
                            Detail = "No users found. At least one user must exist to update collections.",
                            Status = StatusCodes.Status400BadRequest
                        });
                    }
                }

                // Check for duplicate collection names (Jellyfin doesn't allow collections with the same name)
                // Only check if the name is changing
                bool nameChanging = !string.Equals(existingCollection.Name, collection.Name, StringComparison.OrdinalIgnoreCase);
                if (nameChanging)
                {
                    var formattedName = NameFormatter.FormatPlaylistName(collection.Name);
                    var allCollections = await collectionStore.GetAllAsync();
                    var duplicateCollection = allCollections.FirstOrDefault(c => 
                        c.Id != guidId.ToString() && 
                        string.Equals(NameFormatter.FormatPlaylistName(c.Name), formattedName, StringComparison.OrdinalIgnoreCase));
                    
                    if (duplicateCollection != null)
                    {
                        logger.LogWarning("Cannot update collection '{OldName}' to '{NewName}' - a collection with this name already exists", 
                            existingCollection.Name, collection.Name);
                        return BadRequest(new ProblemDetails
                        {
                            Title = "Validation Error",
                            Detail = $"A collection named '{formattedName}' already exists. Jellyfin does not allow multiple collections with the same name.",
                            Status = StatusCodes.Status400BadRequest
                        });
                    }
                }

                // Validate regex patterns before saving
                if (collection.ExpressionSets != null)
                {
                    foreach (var expressionSet in collection.ExpressionSets)
                    {
                        if (expressionSet.Expressions != null)
                        {
                            foreach (var expression in expressionSet.Expressions)
                            {
                                if (expression.Operator == "MatchRegex" && !string.IsNullOrEmpty(expression.TargetValue))
                                {
                                    if (!IsValidRegexPattern(expression.TargetValue, out var validationError))
                                    {
                                        return BadRequest($"Invalid regex pattern: {validationError}");
                                    }

                                    try
                                    {
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

                collection.Id = id;

                // Preserve original creation timestamp
                if (existingCollection.DateCreated.HasValue)
                {
                    collection.DateCreated = existingCollection.DateCreated;
                }

                // Preserve the Jellyfin collection ID from the existing collection if it exists
                if (!string.IsNullOrEmpty(existingCollection.JellyfinCollectionId))
                {
                    collection.JellyfinCollectionId = existingCollection.JellyfinCollectionId;
                    logger.LogDebug("Preserved Jellyfin collection ID {JellyfinCollectionId} from existing collection", existingCollection.JellyfinCollectionId);
                }

                var updatedCollection = await collectionStore.SaveAsync(collection);

                // Update the auto-refresh cache with the updated collection
                AutoRefreshService.Instance?.UpdateCollectionInCache(updatedCollection);

                // Clear the rule cache to ensure any rule changes are properly reflected
                SmartList.ClearRuleCache(logger);
                logger.LogDebug("Cleared rule cache after updating collection '{CollectionName}'", collection.Name);

                // Immediately update the Jellyfin collection using the single collection service with timeout
                var collectionService = GetCollectionService();
                var (success, message, jellyfinCollectionId) = await collectionService.RefreshWithTimeoutAsync(
                    updatedCollection, 
                    progressCallback: null,
                    refreshStatusService: _refreshStatusService,
                    triggerType: Core.Enums.RefreshTriggerType.Manual,
                    cancellationToken: default);

                // If refresh was successful, save the Jellyfin collection ID
                if (success && !string.IsNullOrEmpty(jellyfinCollectionId))
                {
                    updatedCollection.JellyfinCollectionId = jellyfinCollectionId;
                    await collectionStore.SaveAsync(updatedCollection);
                    logger.LogDebug("Saved Jellyfin collection ID {JellyfinCollectionId} for smart collection {CollectionName}",
                        jellyfinCollectionId, updatedCollection.Name);
                }

                stopwatch.Stop();

                if (!success)
                {
                    logger.LogWarning("Failed to refresh updated collection {CollectionName}: {Message}", collection.Name, message);
                    return Ok(updatedCollection);
                }

                logger.LogInformation("Updated SmartList: {CollectionName} in {ElapsedTime}ms", collection.Name, stopwatch.ElapsedMilliseconds);

                return Ok(updatedCollection);
            }
            catch (ArgumentException ex) when (ex.Message.Contains("Invalid regex pattern"))
            {
                stopwatch.Stop();
                logger.LogError(ex, "Unexpected regex validation error in smart collection update after {ElapsedTime}ms", stopwatch.ElapsedMilliseconds);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger.LogError(ex, "Error updating smart collection {CollectionId} after {ElapsedTime}ms", id, stopwatch.ElapsedMilliseconds);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error updating smart collection");
            }
        }

        /// <summary>
        /// Delete a smart list (playlist or collection).
        /// </summary>
        /// <param name="id">The list ID.</param>
        /// <param name="deleteJellyfinList">Whether to also delete the corresponding Jellyfin playlist/collection. Defaults to true for backward compatibility.</param>
        /// <returns>No content.</returns>
        [HttpDelete("{id}")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "ID is validated as GUID before use, preventing path injection")]
        public async Task<ActionResult> DeleteSmartList([FromRoute, Required] string id, [FromQuery] bool deleteJellyfinList = true)
        {
            try
            {
                if (!Guid.TryParse(id, out var guidId))
                {
                    return BadRequest("Invalid list ID format");
                }

                // Try playlist first
                var playlistStore = GetPlaylistStore();
                var playlist = await playlistStore.GetByIdAsync(guidId);
                if (playlist != null)
                {
                    var playlistService = GetPlaylistService();
                    if (deleteJellyfinList)
                    {
                        await playlistService.DeleteAsync(playlist);
                        logger.LogInformation("Deleted smart playlist: {PlaylistName}", playlist.Name);
                    }
                    else
                    {
                        await playlistService.RemoveSmartSuffixAsync(playlist);
                        logger.LogInformation("Deleted smart playlist configuration: {PlaylistName}", playlist.Name);
                    }

                    await playlistStore.DeleteAsync(guidId).ConfigureAwait(false);
                    AutoRefreshService.Instance?.RemovePlaylistFromCache(id);
                    return NoContent();
                }

                // Try collection
                var collectionStore = GetCollectionStore();
                var collection = await collectionStore.GetByIdAsync(guidId);
                if (collection != null)
                {
                    var collectionService = GetCollectionService();
                    if (deleteJellyfinList)
                    {
                        await collectionService.DeleteAsync(collection);
                        logger.LogInformation("Deleted smart collection: {CollectionName}", collection.Name);
                    }
                    else
                    {
                        await collectionService.RemoveSmartSuffixAsync(collection);
                        logger.LogInformation("Deleted smart collection configuration: {CollectionName}", collection.Name);
                    }

                    await collectionStore.DeleteAsync(guidId).ConfigureAwait(false);
                    AutoRefreshService.Instance?.RemoveCollectionFromCache(id);
                    return NoContent();
                }

                return NotFound("Smart list not found");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting smart list {ListId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error deleting smart list");
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
        /// Get all libraries for collection assignment.
        /// </summary>
        /// <returns>List of libraries.</returns>
        [HttpGet("libraries")]
        public ActionResult<object> GetLibraries()
        {
            try
            {
                // Get virtual folders (libraries) from library manager
                var virtualFolders = _libraryManager.GetVirtualFolders();
                
                var libraries = virtualFolders
                    .Select(vf => new
                    {
                        Id = vf.ItemId.ToString(),
                        Name = vf.Name,
                        CollectionType = vf.CollectionType
                    })
                    .OrderBy(l => l.Name)
                    .ToList();

                return Ok(libraries);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving libraries");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error retrieving libraries");
            }
        }

        /// <summary>
        /// Enable a smart list (playlist or collection).
        /// </summary>
        /// <param name="id">The list ID.</param>
        /// <returns>Success message.</returns>
        [HttpPost("{id}/enable")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "ID is validated as GUID before use, preventing path injection")]
        public async Task<ActionResult> EnableSmartList([FromRoute, Required] string id)
        {
            try
            {
                if (!Guid.TryParse(id, out var guidId))
                {
                    return BadRequest("Invalid list ID format");
                }

                // Try playlist first
                var playlistStore = GetPlaylistStore();
                var playlist = await playlistStore.GetByIdAsync(guidId);
                if (playlist != null)
                {
                    // Temporarily set enabled state for the Jellyfin operation
                    var originalEnabledState = playlist.Enabled;
                    playlist.Enabled = true;

                    try
                    {
                        var playlistService = GetPlaylistService();
                        var (success, message, jellyfinPlaylistId) = await playlistService.RefreshWithTimeoutAsync(
                            playlist,
                            progressCallback: null,
                            refreshStatusService: _refreshStatusService,
                            triggerType: Core.Enums.RefreshTriggerType.Manual,
                            cancellationToken: default);

                        if (!success)
                        {
                            // Check if it's a lock conflict
                            if (message.Contains("already in progress"))
                            {
                                logger.LogWarning("Failed to enable playlist {PlaylistName}: Refresh lock could not be acquired", playlist.Name);
                                playlist.Enabled = originalEnabledState;
                                return StatusCode(StatusCodes.Status409Conflict, "A refresh is already in progress. Please try again shortly.");
                            }

                            logger.LogWarning("Failed to enable playlist {PlaylistName}: {Message}", playlist.Name, message);
                            playlist.Enabled = originalEnabledState;
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
                        playlist.Enabled = originalEnabledState;
                        logger.LogError(jellyfinEx, "Failed to enable Jellyfin playlist for {PlaylistId} - {PlaylistName}", id, playlist.Name);
                        throw;
                    }
                }

                // Try collection
                var collectionStore = GetCollectionStore();
                var collection = await collectionStore.GetByIdAsync(guidId);
                if (collection != null)
                {
                    var originalEnabledState = collection.Enabled;
                    collection.Enabled = true;

                    try
                    {
                        var collectionService = GetCollectionService();
                        var (success, message, jellyfinCollectionId) = await collectionService.RefreshWithTimeoutAsync(
                            collection,
                            progressCallback: null,
                            refreshStatusService: _refreshStatusService,
                            triggerType: Core.Enums.RefreshTriggerType.Manual,
                            cancellationToken: default);

                        if (!success)
                        {
                            // Check if it's a lock conflict
                            if (message.Contains("already in progress"))
                            {
                                logger.LogWarning("Failed to enable collection {CollectionName}: Refresh lock could not be acquired", collection.Name);
                                collection.Enabled = originalEnabledState;
                                return StatusCode(StatusCodes.Status409Conflict, "A refresh is already in progress. Please try again shortly.");
                            }

                            logger.LogWarning("Failed to enable collection {CollectionName}: {Message}", collection.Name, message);
                            collection.Enabled = originalEnabledState;
                            throw new InvalidOperationException(message);
                        }

                        if (!string.IsNullOrEmpty(jellyfinCollectionId))
                        {
                            collection.JellyfinCollectionId = jellyfinCollectionId;
                        }

                        await collectionStore.SaveAsync(collection);
                        AutoRefreshService.Instance?.UpdateCollectionInCache(collection);

                        logger.LogInformation("Enabled smart collection: {CollectionId} - {CollectionName}", id, collection.Name);
                        return Ok(new { message = $"Smart collection '{collection.Name}' has been enabled" });
                    }
                    catch (Exception jellyfinEx)
                    {
                        collection.Enabled = originalEnabledState;
                        logger.LogError(jellyfinEx, "Failed to enable Jellyfin collection for {CollectionId} - {CollectionName}", id, collection.Name);
                        throw;
                    }
                }

                return NotFound("Smart list not found");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error enabling smart list {ListId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error enabling smart list");
            }
        }

        /// <summary>
        /// Disable a smart list (playlist or collection).
        /// </summary>
        /// <param name="id">The list ID.</param>
        /// <returns>Success message.</returns>
        [HttpPost("{id}/disable")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "ID is validated as GUID before use, preventing path injection")]
        public async Task<ActionResult> DisableSmartList([FromRoute, Required] string id)
        {
            try
            {
                if (!Guid.TryParse(id, out var guidId))
                {
                    return BadRequest("Invalid list ID format");
                }

                // Try playlist first
                var playlistStore = GetPlaylistStore();
                var playlist = await playlistStore.GetByIdAsync(guidId);
                if (playlist != null)
                {
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
                        playlist.Enabled = originalEnabledState;
                        logger.LogError(jellyfinEx, "Failed to disable Jellyfin playlist for {PlaylistId} - {PlaylistName}", id, playlist.Name);
                        throw;
                    }
                }

                // Try collection
                var collectionStore = GetCollectionStore();
                var collection = await collectionStore.GetByIdAsync(guidId);
                if (collection != null)
                {
                    var originalEnabledState = collection.Enabled;
                    collection.Enabled = false;

                    try
                    {
                        var collectionService = GetCollectionService();
                        await collectionService.DisableAsync(collection);

                        collection.JellyfinCollectionId = null;
                        await collectionStore.SaveAsync(collection);
                        AutoRefreshService.Instance?.UpdateCollectionInCache(collection);

                        logger.LogInformation("Disabled smart collection: {CollectionId} - {CollectionName}", id, collection.Name);
                        return Ok(new { message = $"Smart collection '{collection.Name}' has been disabled" });
                    }
                    catch (Exception jellyfinEx)
                    {
                        collection.Enabled = originalEnabledState;
                        logger.LogError(jellyfinEx, "Failed to disable Jellyfin collection for {CollectionId} - {CollectionName}", id, collection.Name);
                        throw;
                    }
                }

                return NotFound("Smart list not found");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error disabling smart list {ListId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Error disabling smart list");
            }
        }

        /// <summary>
        /// Trigger a refresh of a specific smart list (playlist or collection).
        /// </summary>
        /// <param name="id">The list ID.</param>
        /// <returns>Success message.</returns>
        [HttpPost("{id}/refresh")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA3003:Review code for file path injection vulnerabilities", Justification = "ID is validated as GUID before use, preventing path injection")]
        public async Task<ActionResult> TriggerSingleListRefresh([FromRoute, Required] string id)
        {
            string? listName = null;
            Core.Enums.SmartListType? listType = null;
            
            try
            {
                if (!Guid.TryParse(id, out var guidId))
                {
                    // Track error in status service
                    _refreshStatusService?.StartOperation(
                        id,
                        $"List ({id})",
                        Core.Enums.SmartListType.Playlist,
                        Core.Enums.RefreshTriggerType.Manual,
                        0);
                    _refreshStatusService?.CompleteOperation(id, false, "Invalid list ID format");
                    
                    return BadRequest("Invalid list ID format");
                }

                // Try playlist first
                var playlistStore = GetPlaylistStore();
                var playlist = await playlistStore.GetByIdAsync(guidId);
                if (playlist != null)
                {
                    listName = playlist.Name;
                    listType = Core.Enums.SmartListType.Playlist;
                    
                    var (success, message, jellyfinPlaylistId) = await _manualRefreshService.RefreshSinglePlaylistAsync(playlist);

                    if (success)
                    {
                        if (!string.IsNullOrEmpty(jellyfinPlaylistId))
                        {
                            playlist.JellyfinPlaylistId = jellyfinPlaylistId;
                        }

                        await playlistStore.SaveAsync(playlist);
                        return Ok(new { message = $"Smart playlist '{playlist.Name}' has been refreshed successfully" });
                    }
                    else
                    {
                        return BadRequest(new { message });
                    }
                }

                // Try collection
                var collectionStore = GetCollectionStore();
                var collection = await collectionStore.GetByIdAsync(guidId);
                if (collection != null)
                {
                    listName = collection.Name;
                    listType = Core.Enums.SmartListType.Collection;
                    
                    var (success, message, jellyfinCollectionId) = await _manualRefreshService.RefreshSingleCollectionAsync(collection);

                    if (success)
                    {
                        if (!string.IsNullOrEmpty(jellyfinCollectionId))
                        {
                            collection.JellyfinCollectionId = jellyfinCollectionId;
                        }

                        await collectionStore.SaveAsync(collection);
                        return Ok(new { message = $"Smart collection '{collection.Name}' has been refreshed successfully" });
                    }
                    else
                    {
                        return BadRequest(new { message });
                    }
                }

                // List not found - track error in status service
                _refreshStatusService?.StartOperation(
                    id,
                    $"List ({id})",
                    Core.Enums.SmartListType.Playlist,
                    Core.Enums.RefreshTriggerType.Manual,
                    0);
                _refreshStatusService?.CompleteOperation(id, false, "Smart list not found");
                
                return NotFound("Smart list not found");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error refreshing single smart list {ListId}", id);
                
                // Track error in status service if not already tracked
                if (!_refreshStatusService?.HasOngoingOperation(id) ?? true)
                {
                    _refreshStatusService?.StartOperation(
                        id,
                        listName ?? $"List ({id})",
                        listType ?? Core.Enums.SmartListType.Playlist,
                        Core.Enums.RefreshTriggerType.Manual,
                        0);
                }
                _refreshStatusService?.CompleteOperation(id, false, $"Error refreshing smart list: {ex.Message}");
                
                return StatusCode(StatusCodes.Status500InternalServerError, "Error refreshing smart list");
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
                    return Ok(new { message = result.NotificationMessage });
                }
                else
                {
                    // Map "already in progress" to HTTP 409 Conflict for better API semantics
                    if (result.NotificationMessage.Contains("already in progress"))
                    {
                        return Conflict(new { message = result.NotificationMessage });
                    }
                    return StatusCode(StatusCodes.Status500InternalServerError, result.NotificationMessage);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error triggering smart playlist refresh");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error triggering smart playlist refresh");
            }
        }

        /// <summary>
        /// Directly refresh all smart lists (both playlists and collections).
        /// This method processes all enabled lists sequentially for each user.
        /// </summary>
        /// <returns>Success message.</returns>
        [HttpPost("refresh-direct")]
        public async Task<ActionResult> RefreshAllPlaylistsDirect()
        {
            try
            {
                // The ManualRefreshService now handles lock acquisition internally for the entire operation
                // This now refreshes both playlists and collections
                var result = await _manualRefreshService.RefreshAllListsAsync();

                if (result.Success)
                {
                    return Ok(new { message = result.NotificationMessage });
                }
                else
                {
                    // Map "already in progress" to HTTP 409 Conflict for better API semantics
                    if (result.NotificationMessage.Contains("already in progress"))
                    {
                        return Conflict(new { message = result.NotificationMessage });
                    }
                    return StatusCode(StatusCodes.Status500InternalServerError, result.NotificationMessage);
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Manual list refresh was cancelled by client");
                return StatusCode(499, "Refresh operation was cancelled");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during manual list refresh");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error during manual list refresh");
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
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
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
        /// Import smart lists (playlists and collections) from a ZIP file.
        /// </summary>
        /// <param name="file">ZIP file containing smart list JSON files.</param>
        /// <returns>Import results with counts of imported and skipped lists.</returns>
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
                var collectionStore = GetCollectionStore();
                var existingPlaylists = await playlistStore.GetAllAsync();
                var existingCollections = await collectionStore.GetAllAsync();
                var existingPlaylistIds = existingPlaylists.Select(p => p.Id).ToHashSet();
                var existingCollectionIds = existingCollections.Select(c => c.Id).ToHashSet();

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                };

                var importResults = new List<object>();
                int importedPlaylistCount = 0;
                int importedCollectionCount = 0;
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
                        // Read JSON content to check Type property first
                        string jsonContent;
                        using (var entryStream = entry.Open())
                        {
                            using var reader = new StreamReader(entryStream);
                            jsonContent = await reader.ReadToEndAsync();
                        }
                        using var jsonDoc = JsonDocument.Parse(jsonContent);

                        // Determine if this is a playlist or collection based on Type property
                        Core.Enums.SmartListType listType = Core.Enums.SmartListType.Playlist; // Default to Playlist for backward compatibility
                        bool hasTypeProperty = jsonDoc.RootElement.TryGetProperty("Type", out var typeElement);

                        if (hasTypeProperty)
                        {
                            if (typeElement.ValueKind == JsonValueKind.String)
                            {
                                var typeString = typeElement.GetString();
                                if (Enum.TryParse<Core.Enums.SmartListType>(typeString, ignoreCase: true, out var parsedType))
                                {
                                    listType = parsedType;
                                }
                            }
                            else if (typeElement.ValueKind == JsonValueKind.Number)
                            {
                                var typeValue = typeElement.GetInt32();
                                listType = typeValue == 1 ? Core.Enums.SmartListType.Collection : Core.Enums.SmartListType.Playlist;
                            }
                        }

                        // Deserialize to the correct type based on the Type field
                        if (listType == Core.Enums.SmartListType.Playlist)
                        {
                            var playlist = JsonSerializer.Deserialize<SmartPlaylistDto>(jsonContent, jsonOptions);
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

                            // Ensure type is set
                            playlist.Type = Core.Enums.SmartListType.Playlist;

                            // Validate and potentially reassign user references
                            bool reassignedUsers = false;
                            Guid currentUserId = Guid.Empty;

                            // Check playlist owner
                            if (!string.IsNullOrEmpty(playlist.UserId) && Guid.TryParse(playlist.UserId, out var playlistUserIdParsed) && playlistUserIdParsed != Guid.Empty)
                            {
                                var user = _userManager.GetUserById(playlistUserIdParsed);
                                if (user == null)
                                {
                                    // Only get current user ID when we need to reassign
                                    if (currentUserId == Guid.Empty)
                                    {
                                        currentUserId = GetCurrentUserId();
                                        if (currentUserId == Guid.Empty)
                                        {
                                            logger.LogWarning("Playlist '{PlaylistName}' references non-existent user {User} but cannot determine importing user for reassignment",
                                                playlist.Name, playlist.UserId);
                                            importResults.Add(new { fileName = entry.Name, status = "error", message = "Cannot reassign playlist - unable to determine importing user" });
                                            errorCount++;
                                            continue; // Skip this entire playlist,
                                        }
                                    }

                                    logger.LogWarning("Playlist '{PlaylistName}' references non-existent user {User}, reassigning to importing user {CurrentUserId}",
                                        playlist.Name, playlist.UserId, currentUserId);

                                    playlist.UserId = currentUserId.ToString("D");
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

                            if (existingPlaylistIds.Contains(playlist.Id))
                            {
                                importResults.Add(new { fileName = entry.Name, listName = playlist.Name, listType = "Playlist", status = "skipped", message = "Playlist with this ID already exists" });
                                skippedCount++;
                                continue;
                            }

                            // Import the playlist
                            await playlistStore.SaveAsync(playlist);

                            // Update the auto-refresh cache with the imported playlist
                            AutoRefreshService.Instance?.UpdatePlaylistInCache(playlist);

                            importResults.Add(new { fileName = entry.Name, listName = playlist.Name, listType = "Playlist", status = "imported", message = "Successfully imported" });
                            importedPlaylistCount++;

                            logger.LogDebug("Imported playlist {PlaylistName} (ID: {PlaylistId}) from {FileName}",
                                playlist.Name, playlist.Id, entry.Name);
                        }
                        else if (listType == Core.Enums.SmartListType.Collection)
                        {
                            var collection = JsonSerializer.Deserialize<SmartCollectionDto>(jsonContent, jsonOptions);
                            if (collection == null || string.IsNullOrEmpty(collection.Id))
                            {
                                logger.LogWarning("Invalid collection data in file {FileName}: {Issue}",
                                    entry.Name, collection == null ? "null collection" : "empty ID");
                                importResults.Add(new { fileName = entry.Name, status = "error", message = "Invalid or empty collection data" });
                                errorCount++;
                                continue;
                            }

                            if (string.IsNullOrWhiteSpace(collection.Name))
                            {
                                logger.LogWarning("Collection in file {FileName} has no name", entry.Name);
                                importResults.Add(new { fileName = entry.Name, status = "error", message = "Collection must have a name" });
                                errorCount++;
                                continue;
                            }

                            // Ensure type is set
                            collection.Type = Core.Enums.SmartListType.Collection;

                            // Validate and potentially reassign user references (collections use User property from base class)
                            bool reassignedUsers = false;
                            Guid currentUserId = Guid.Empty;

                            // Check collection user
                            if (!string.IsNullOrEmpty(collection.UserId) && Guid.TryParse(collection.UserId, out var collectionUserIdParsed) && collectionUserIdParsed != Guid.Empty)
                            {
                                var user = _userManager.GetUserById(collectionUserIdParsed);
                                if (user == null)
                                {
                                    // Only get current user ID when we need to reassign
                                    if (currentUserId == Guid.Empty)
                                    {
                                        currentUserId = GetCurrentUserId();
                                        if (currentUserId == Guid.Empty)
                                        {
                                            logger.LogWarning("Collection '{CollectionName}' references non-existent user {User} but cannot determine importing user for reassignment",
                                                collection.Name, collection.UserId);
                                            importResults.Add(new { fileName = entry.Name, status = "error", message = "Cannot reassign collection - unable to determine importing user" });
                                            errorCount++;
                                            continue; // Skip this entire collection,
                                        }
                                    }

                                    logger.LogWarning("Collection '{CollectionName}' references non-existent user {User}, reassigning to importing user {CurrentUserId}",
                                        collection.Name, collection.UserId, currentUserId);

                                    collection.UserId = currentUserId.ToString("D");
                                    reassignedUsers = true;
                                }
                            }

                            // Add note to import results if users were reassigned
                            if (reassignedUsers)
                            {
                                logger.LogInformation("Reassigned user references in collection '{CollectionName}' due to non-existent users", collection.Name);
                            }

                            if (existingCollectionIds.Contains(collection.Id))
                            {
                                importResults.Add(new { fileName = entry.Name, listName = collection.Name, listType = "Collection", status = "skipped", message = "Collection with this ID already exists" });
                                skippedCount++;
                                continue;
                            }

                            // Import the collection
                            await collectionStore.SaveAsync(collection);

                            // Update the auto-refresh cache with the imported collection
                            AutoRefreshService.Instance?.UpdateCollectionInCache(collection);

                            importResults.Add(new { fileName = entry.Name, listName = collection.Name, listType = "Collection", status = "imported", message = "Successfully imported" });
                            importedCollectionCount++;

                            logger.LogDebug("Imported collection {CollectionName} (ID: {CollectionId}) from {FileName}",
                                collection.Name, collection.Id, entry.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error importing smart list from {FileName}", entry.Name);
                        importResults.Add(new { fileName = entry.Name, status = "error", message = ex.Message });
                        errorCount++;
                    }
                }

                var totalImported = importedPlaylistCount + importedCollectionCount;
                var summary = new
                {
                    totalFiles = archive.Entries.Count(e => e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)),
                    imported = totalImported,
                    importedPlaylists = importedPlaylistCount,
                    importedCollections = importedCollectionCount,
                    skipped = skippedCount,
                    errors = errorCount,
                    details = importResults,
                };

                logger.LogInformation("Import completed: {Imported} imported ({Playlists} playlists, {Collections} collections), {Skipped} skipped, {Errors} errors",
                    totalImported, importedPlaylistCount, importedCollectionCount, skippedCount, errorCount);

                return Ok(summary);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error importing smart lists");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error importing smart lists");
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

        /// <summary>
        /// Get refresh status including ongoing operations, history, and statistics
        /// </summary>
        [HttpGet("Status")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult GetRefreshStatus()
        {
            try
            {
                if (_refreshStatusService == null)
                {
                    logger.LogWarning("RefreshStatusService is null in GetRefreshStatus");
                    return StatusCode(StatusCodes.Status500InternalServerError, "RefreshStatusService is not available");
                }

                var ongoing = _refreshStatusService.GetOngoingOperations().Select(op => new
                {
                    listId = op.ListId,
                    listName = op.ListName,
                    listType = op.ListType.ToString(),
                    triggerType = op.TriggerType.ToString(),
                    startTime = op.StartTime.ToString("o"),
                    totalItems = op.TotalItems,
                    processedItems = op.ProcessedItems,
                    estimatedTimeRemaining = op.EstimatedTimeRemaining?.TotalSeconds,
                    elapsedTime = op.ElapsedTime.TotalSeconds,
                    errorMessage = op.ErrorMessage,
                    batchCurrentIndex = op.BatchCurrentIndex,
                    batchTotalCount = op.BatchTotalCount
                }).ToList();

                var history = _refreshStatusService.GetRefreshHistory().Select(h => new
                {
                    listId = h.ListId,
                    listName = h.ListName,
                    listType = h.ListType.ToString(),
                    triggerType = h.TriggerType.ToString(),
                    startTime = h.StartTime.ToString("o"),
                    endTime = h.EndTime?.ToString("o"),
                    duration = h.Duration.TotalSeconds,
                    success = h.Success,
                    errorMessage = h.ErrorMessage
                }).ToList();

                var statistics = _refreshStatusService.GetStatistics();

                return Ok(new
                {
                    ongoingOperations = ongoing,
                    history = history,
                    statistics = new
                    {
                        totalLists = statistics.TotalLists,
                        ongoingOperationsCount = statistics.OngoingOperationsCount,
                        lastRefreshTime = statistics.LastRefreshTime?.ToString("o"),
                        averageRefreshDuration = statistics.AverageRefreshDuration?.TotalSeconds,
                        successfulRefreshes = statistics.SuccessfulRefreshes,
                        failedRefreshes = statistics.FailedRefreshes
                    }
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting refresh status");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error getting refresh status");
            }
        }

        /// <summary>
        /// Get refresh history (last refresh per list)
        /// </summary>
        [HttpGet("Status/History")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult GetRefreshHistory()
        {
            try
            {
                var history = _refreshStatusService.GetRefreshHistory().Select(h => new
                {
                    listId = h.ListId,
                    listName = h.ListName,
                    listType = h.ListType.ToString(),
                    triggerType = h.TriggerType.ToString(),
                    startTime = h.StartTime.ToString("o"),
                    endTime = h.EndTime?.ToString("o"),
                    duration = h.Duration.TotalSeconds,
                    success = h.Success,
                    errorMessage = h.ErrorMessage
                }).ToList();

                return Ok(history);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting refresh history");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error getting refresh history");
            }
        }

        /// <summary>
        /// Get ongoing refresh operations
        /// </summary>
        [HttpGet("Status/Ongoing")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult GetOngoingOperations()
        {
            try
            {
                var ongoing = _refreshStatusService.GetOngoingOperations().Select(op => new
                {
                    listId = op.ListId,
                    listName = op.ListName,
                    listType = op.ListType.ToString(),
                    triggerType = op.TriggerType.ToString(),
                    startTime = op.StartTime.ToString("o"),
                    totalItems = op.TotalItems,
                    processedItems = op.ProcessedItems,
                    estimatedTimeRemaining = op.EstimatedTimeRemaining?.TotalSeconds,
                    elapsedTime = op.ElapsedTime.TotalSeconds,
                    errorMessage = op.ErrorMessage,
                    batchCurrentIndex = op.BatchCurrentIndex,
                    batchTotalCount = op.BatchTotalCount
                }).ToList();

                return Ok(ongoing);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting ongoing operations");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error getting ongoing operations");
            }
        }


    }
}