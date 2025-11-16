using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartLists.Core.Models;
using Jellyfin.Plugin.SmartLists.Services.Playlists;
using Jellyfin.Plugin.SmartLists.Services.Collections;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using Jellyfin.Plugin.SmartLists.Utilities;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Services.Shared
{
    /// <summary>
    /// Result of a refresh operation with separate messages for user notification and logging.
    /// </summary>
    public class RefreshResult
    {
        /// <summary>
        /// Whether the operation was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// User-friendly notification message to display in the UI.
        /// </summary>
        public string NotificationMessage { get; set; } = string.Empty;

        /// <summary>
        /// Detailed log message for debugging and logging purposes.
        /// </summary>
        public string LogMessage { get; set; } = string.Empty;

        /// <summary>
        /// Number of successful refresh operations.
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// Number of failed refresh operations.
        /// </summary>
        public int FailureCount { get; set; }
    }

    /// <summary>
    /// Service for handling manual refresh operations initiated by users.
    /// This includes both individual playlist refreshes and "Refresh All" operations from the UI.
    /// </summary>
    public interface IManualRefreshService
    {
        /// <summary>
        /// Refresh all smart playlists manually.
        /// This method processes ALL playlists regardless of their ScheduleTrigger settings.
        /// </summary>
        /// <param name="batchOffset">Offset for batch tracking (used when refreshing all lists together)</param>
        /// <param name="totalBatchCount">Total count for unified batch tracking (used when refreshing all lists together)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Refresh result with separate notification and log messages</returns>
        Task<RefreshResult> RefreshAllPlaylistsAsync(int batchOffset = 0, int? totalBatchCount = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Refresh all smart lists (both playlists and collections) manually.
        /// This method processes ALL lists regardless of their ScheduleTrigger settings.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Refresh result with separate notification and log messages</returns>
        Task<RefreshResult> RefreshAllListsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Refresh a single smart playlist manually.
        /// </summary>
        /// <param name="playlist">The playlist to refresh</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Tuple of (success, message, jellyfinPlaylistId)</returns>
        Task<(bool Success, string Message, string? JellyfinPlaylistId)> RefreshSinglePlaylistAsync(Core.Models.SmartPlaylistDto playlist, CancellationToken cancellationToken = default);

        /// <summary>
        /// Refresh a single smart collection manually.
        /// </summary>
        /// <param name="collection">The collection to refresh</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Tuple of (success, message, jellyfinCollectionId)</returns>
        Task<(bool Success, string Message, string? JellyfinCollectionId)> RefreshSingleCollectionAsync(Core.Models.SmartCollectionDto collection, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implementation of manual refresh service that handles user-initiated refresh operations.
    /// This consolidates logic for both individual playlist refreshes and "refresh all" operations.
    /// </summary>
    public class ManualRefreshService(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IServerApplicationPaths applicationPaths,
        IPlaylistManager playlistManager,
        ICollectionManager collectionManager,
        IUserDataManager userDataManager,
        IProviderManager providerManager,
        ILogger<ManualRefreshService> logger,
        Microsoft.Extensions.Logging.ILoggerFactory loggerFactory,
        RefreshStatusService refreshStatusService) : IManualRefreshService
    {
        // Note: Manual refresh now uses the shared PlaylistService lock to prevent concurrent operations
        private readonly IUserManager _userManager = userManager;
        private readonly ILibraryManager _libraryManager = libraryManager;
        private readonly IServerApplicationPaths _applicationPaths = applicationPaths;
        private readonly IPlaylistManager _playlistManager = playlistManager;
        private readonly ICollectionManager _collectionManager = collectionManager;
        private readonly IUserDataManager _userDataManager = userDataManager;
        private readonly IProviderManager _providerManager = providerManager;
        private readonly ILogger<ManualRefreshService> _logger = logger;
        private readonly Microsoft.Extensions.Logging.ILoggerFactory _loggerFactory = loggerFactory;
        private readonly RefreshStatusService _refreshStatusService = refreshStatusService;

        /// <summary>
        /// Gets the user for a playlist, handling migration from old User field to new UserId field.
        /// </summary>
        /// <param name="playlist">The playlist.</param>
        /// <returns>The user, or null if not found.</returns>
        private async Task<User?> GetPlaylistUserAsync(SmartPlaylistDto playlist)
        {
            var userId = await GetPlaylistUserIdAsync(playlist);
            if (userId == Guid.Empty)
            {
                return null;
            }

            return _userManager.GetUserById(userId);
        }

        /// <summary>
        /// Gets the user ID for a playlist.
        /// </summary>
        /// <param name="playlist">The playlist.</param>
        /// <returns>The user ID, or Guid.Empty if not found.</returns>
        private static Task<Guid> GetPlaylistUserIdAsync(SmartPlaylistDto playlist)
        {
            if (!string.IsNullOrEmpty(playlist.UserId) && Guid.TryParse(playlist.UserId, out var userId))
            {
                return Task.FromResult(userId);
            }
            return Task.FromResult(Guid.Empty);
        }

        /// <summary>
        /// Formats elapsed time for user notifications. Shows seconds if under 60 seconds (rounded to integer if >= 1 second), minutes if 60+ seconds.
        /// </summary>
        /// <param name="elapsedMilliseconds">Elapsed time in milliseconds.</param>
        /// <returns>Formatted time string (e.g., "0.5 seconds", "2 seconds", or "3 minutes").</returns>
        private static string FormatElapsedTime(long elapsedMilliseconds)
        {
            var elapsedSeconds = elapsedMilliseconds / 1000.0;
            
            if (elapsedSeconds < 60)
            {
                if (elapsedSeconds < 1.0)
                {
                    return $"{elapsedSeconds:F1} seconds";
                }
                else
                {
                    var seconds = (int)Math.Round(elapsedSeconds);
                    return seconds == 1 ? "1 second" : $"{seconds} seconds";
                }
            }
            else
            {
                var minutes = (int)Math.Round(elapsedSeconds / 60.0);
                return minutes == 1 ? "1 minute" : $"{minutes} minutes";
            }
        }

        /// <summary>
        /// Checks if a refresh result indicates actual failures (failure count > 0).
        /// </summary>
        /// <param name="result">The refresh result to check.</param>
        /// <returns>True if there are actual failures (FailureCount > 0), false otherwise.</returns>
        private static bool HasActualFailures(RefreshResult result)
        {
            return result?.FailureCount > 0;
        }

        /// <summary>
        /// Creates a PlaylistService instance for playlist operations.
        /// </summary>
        private Services.Playlists.PlaylistService GetPlaylistService()
        {
            // Create a logger specifically for PlaylistService
            var playlistServiceLogger = _loggerFactory.CreateLogger<PlaylistService>();

            return new PlaylistService(
                _userManager,
                _libraryManager,
                _playlistManager,
                _userDataManager,
                playlistServiceLogger,
                _providerManager);
        }

        /// <summary>
        /// Creates a CollectionService instance for collection operations.
        /// </summary>
        private Services.Collections.CollectionService GetCollectionService()
        {
            // Create a logger specifically for CollectionService
            var collectionServiceLogger = _loggerFactory.CreateLogger<Services.Collections.CollectionService>();

            return new Services.Collections.CollectionService(
                _libraryManager,
                _collectionManager,
                _userManager,
                _userDataManager,
                collectionServiceLogger,
                _providerManager);
        }

        /// <summary>
        /// Refresh all smart playlists manually without using Jellyfin scheduled tasks.
        /// This method performs the same work as the scheduled tasks but processes ALL playlists
        /// regardless of their ScheduleTrigger settings, since this is a manual operation.
        /// This method uses immediate failure if another refresh is already in progress.
        /// </summary>
        /// <param name="batchOffset">Offset for batch tracking (used when refreshing all lists together)</param>
        /// <param name="totalBatchCount">Total count for unified batch tracking (used when refreshing all lists together)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public async Task<RefreshResult> RefreshAllPlaylistsAsync(int batchOffset = 0, int? totalBatchCount = null, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            // Declare cache variables at method level so they're accessible in finally for cleanup
            var allUserMediaTypeCaches = new List<ConcurrentDictionary<MediaTypesKey, Lazy<BaseItem[]>>>();

            // Try to acquire the shared refresh lock (same as scheduled tasks) with immediate failure
            var (lockAcquired, lockHandle) = await Services.Playlists.PlaylistService.TryAcquireRefreshLockAsync(cancellationToken);
            if (!lockAcquired)
            {
                var message = "A refresh is already in progress. Please try again shortly.";
                _logger.LogInformation("Manual playlist refresh request rejected - another refresh is already in progress");
                return new RefreshResult
                {
                    Success = false,
                    NotificationMessage = message,
                    LogMessage = message,
                    SuccessCount = 0,
                    FailureCount = 0
                };
            }

            try
            {
                _logger.LogInformation("Starting manual refresh of all smart playlists (acquired refresh lock)");

                // Create playlist store
                var fileSystem = new SmartListFileSystem(_applicationPaths);
                var plStore = new PlaylistStore(fileSystem);
                var playlistService = GetPlaylistService();

                var allDtos = await plStore.GetAllAsync().ConfigureAwait(false);

                _logger.LogInformation("Found {TotalCount} total playlists for manual refresh", allDtos.Length);

                if (allDtos.Length == 0)
                {
                    stopwatch.Stop();
                    var message = "No playlists found to refresh";
                    var earlyLogMessage = $"{message} (completed in {stopwatch.ElapsedMilliseconds}ms)";
                    _logger.LogInformation(earlyLogMessage);
                    return new RefreshResult
                    {
                        Success = true,
                        NotificationMessage = message,
                        LogMessage = earlyLogMessage,
                        SuccessCount = 0,
                        FailureCount = 0
                    };
                }

                // Log disabled playlists for informational purposes
                var disabledPlaylists = allDtos.Where(dto => !dto.Enabled).ToList();
                if (disabledPlaylists.Count > 0)
                {
                    var disabledNames = string.Join(", ", disabledPlaylists.Select(p => $"'{p.Name}'"));
                    _logger.LogDebug("Skipping {DisabledCount} disabled playlists: {DisabledNames}", disabledPlaylists.Count, disabledNames);
                }

                // Process all enabled playlists (not just legacy ones - this is a manual trigger)
                var enabledPlaylists = allDtos.Where(dto => dto.Enabled).ToList();
                _logger.LogInformation("Processing {EnabledCount} enabled playlists", enabledPlaylists.Count);

                // Pre-resolve users and group playlists by user
                var resolvedPlaylists = new List<(SmartPlaylistDto dto, User user)>();
                var playlistsWithoutUser = new List<SmartPlaylistDto>();

                foreach (var dto in enabledPlaylists)
                {
                    var user = await GetPlaylistUserAsync(dto);
                    if (user != null)
                    {
                        resolvedPlaylists.Add((dto, user));
                    }
                    else
                    {
                        _logger.LogWarning("User not found for playlist '{PlaylistName}'. Will track as failure.", dto.Name);
                        playlistsWithoutUser.Add(dto);
                    }
                }

                // Track playlists without users as failures
                foreach (var dto in playlistsWithoutUser)
                {
                    var listId = dto.Id ?? Guid.NewGuid().ToString();
                    _refreshStatusService?.StartOperation(
                        listId,
                        dto.Name,
                        Core.Enums.SmartListType.Playlist,
                        Core.Enums.RefreshTriggerType.Manual,
                        0,
                        batchCurrentIndex: batchOffset + resolvedPlaylists.Count + playlistsWithoutUser.IndexOf(dto) + 1,
                        batchTotalCount: totalBatchCount ?? (enabledPlaylists.Count));
                    _refreshStatusService?.CompleteOperation(
                        listId,
                        false,
                        "User not found for playlist");
                }

                // Group by user for efficient media caching
                var playlistsByUser = resolvedPlaylists
                    .GroupBy(p => p.user.Id)
                    .ToDictionary(g => g.Key, g => g.ToList());

                _logger.LogDebug("Grouped {UserCount} users with {TotalPlaylists} playlists after user resolution",
                    playlistsByUser.Count, resolvedPlaylists.Count);

                // Process playlists with proper MediaTypes filtering
                // Start counts with playlists that already failed (no user)
                var processedCount = playlistsWithoutUser.Count;
                var successCount = 0;
                var failureCount = playlistsWithoutUser.Count;

                // Calculate total count of all playlists for batch tracking
                // Include playlists without users in the total count so they're all tracked
                var totalPlaylistCount = playlistsByUser.Values.Sum(userPlaylistPairs => userPlaylistPairs.Count) + playlistsWithoutUser.Count;
                var batchTotalCount = totalBatchCount ?? totalPlaylistCount;
                var currentPlaylistIndex = batchOffset; // Start from offset if provided

                foreach (var (userId, userPlaylistPairs) in playlistsByUser)
                {
                    var user = userPlaylistPairs.First().user;
                    var userPlaylists = userPlaylistPairs.Select(p => p.dto).ToList();

                    _logger.LogDebug("Processing {PlaylistCount} playlists sequentially for user '{Username}' (parallelism will be used for expensive operations within each playlist)",
                        userPlaylists.Count, user.Username);

                    // OPTIMIZATION: Cache media by MediaTypes to avoid redundant queries for playlists with same media types
                    // Use Lazy<T> to ensure value factory executes only once per key, even under concurrent access
                    var userMediaTypeCache = new ConcurrentDictionary<MediaTypesKey, Lazy<BaseItem[]>>();
                    allUserMediaTypeCaches.Add(userMediaTypeCache); // Track for cleanup

                    foreach (var dto in userPlaylists)
                    {
                        // Increment current index for batch tracking (1-based for display)
                        // Do this before validation so skipped playlists are still counted in batch progress
                        currentPlaylistIndex++;
                        
                        // Generate listId once at the start to ensure StartOperation and CompleteOperation use same ID
                        var listId = dto.Id ?? Guid.NewGuid().ToString();
                        
                        var playlistStopwatch = Stopwatch.StartNew();
                        var operationStarted = false;
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            // Start tracking refresh operation early, before any operations that might fail
                            // This ensures failures are always reported to the status service
                            _refreshStatusService?.StartOperation(
                                listId,
                                dto.Name,
                                Core.Enums.SmartListType.Playlist,
                                Core.Enums.RefreshTriggerType.Manual,
                                0, // Will update with actual count after media fetch
                                batchCurrentIndex: currentPlaylistIndex,
                                batchTotalCount: batchTotalCount);
                            operationStarted = true;

                            // Validate that the playlist user is valid
                            if (user.Id == Guid.Empty)
                            {
                                _logger.LogWarning("Playlist '{PlaylistName}' has invalid user ID. Skipping.", dto.Name);
                                _refreshStatusService?.CompleteOperation(listId, false, "Invalid user ID");
                                failureCount++;
                                processedCount++;
                                continue;
                            }

                            // OPTIMIZATION: Get media specifically for this playlist's media types using cache
                            // This ensures Movie playlists only get movies, not episodes/series, while avoiding redundant queries
                            var mediaTypesForClosure = dto.MediaTypes?.ToList() ?? []; // Create defensive copy to prevent accidental modifications
                            var mediaTypesKey = MediaTypesKey.Create(mediaTypesForClosure, dto);

                            // NOTE: Lazy<T> caches exceptions. This is intentional for database operations
                            // where failures typically indicate serious issues that should fail fast
                            // rather than retry repeatedly during the same manual refresh operation.
                            var playlistSpecificMedia = userMediaTypeCache.GetOrAdd(mediaTypesKey, _ =>
                                new Lazy<BaseItem[]>(() =>
                                {
                                    var media = playlistService.GetAllUserMediaForPlaylist(user, mediaTypesForClosure, dto).ToArray();
                                    _logger.LogDebug("Cached {MediaCount} items for MediaTypes [{MediaTypes}] for user '{Username}'",
                                        media.Length, mediaTypesKey, user.Username);
                                    return media;
                                }, LazyThreadSafetyMode.ExecutionAndPublication)
                            ).Value;

                            _logger.LogDebug("Playlist {PlaylistName} with MediaTypes [{MediaTypes}] has {PlaylistSpecificCount} specific items",
                                dto.Name, mediaTypesKey, playlistSpecificMedia.Length);

                            // Update operation with actual media count now that we have it
                            _refreshStatusService?.UpdateProgress(listId, 0, playlistSpecificMedia.Length);

                            // Create progress callback
                            Action<int, int>? progressCallback = (processed, total) =>
                            {
                                _refreshStatusService?.UpdateProgress(listId, processed, total);
                            };

                            // Track if this is a new playlist (JellyfinPlaylistId was empty before refresh)
                            var wasNewPlaylist = string.IsNullOrEmpty(dto.JellyfinPlaylistId);
                            
                            var refreshResult = await playlistService.ProcessPlaylistRefreshWithCachedMediaAsync(
                                dto,
                                user,
                                playlistSpecificMedia, // Use properly filtered and cached media
                                async (updatedDto) => await plStore.SaveAsync(updatedDto),
                                progressCallback,
                                cancellationToken);

                            playlistStopwatch.Stop();
                            
                            // Complete status tracking
                            _refreshStatusService?.CompleteOperation(
                                listId,
                                refreshResult.Success,
                                refreshResult.Success ? null : refreshResult.Message);
                            
                            if (refreshResult.Success)
                            {
                                // Save the playlist to persist LastRefreshed timestamp
                                // Note: For new playlists, the saveCallback already saved the DTO (with JellyfinPlaylistId),
                                // but ProcessPlaylistRefreshWithCachedMediaAsync updates LastRefreshed after the callback,
                                // so we need to save again to persist the updated timestamp.
                                // For existing playlists, we need to save to persist LastRefreshed.
                                await plStore.SaveAsync(dto);

                                successCount++;
                                _logger.LogDebug("Playlist {PlaylistName} processed successfully in {ElapsedTime}ms: {Message}",
                                    dto.Name, playlistStopwatch.ElapsedMilliseconds, refreshResult.Message);
                            }
                            else
                            {
                                failureCount++;
                                _logger.LogWarning("Playlist {PlaylistName} processing failed after {ElapsedTime}ms: {Message}",
                                    dto.Name, playlistStopwatch.ElapsedMilliseconds, refreshResult.Message);
                            }

                            processedCount++;
                        }
                        catch (OperationCanceledException)
                        {
                            playlistStopwatch.Stop();
                            
                            // Only complete operation if it was started
                            if (operationStarted)
                            {
                                _refreshStatusService?.CompleteOperation(
                                    listId,
                                    false,
                                    "Refresh operation was cancelled");
                            }
                            
                            _logger.LogInformation("Direct refresh operation was cancelled");
                            throw;
                        }
                        catch (Exception ex)
                        {
                            playlistStopwatch.Stop();
                            
                            // Only complete operation if it was started
                            if (operationStarted)
                            {
                                _refreshStatusService?.CompleteOperation(
                                    listId,
                                    false,
                                    ex.Message);
                            }
                            
                            failureCount++;
                            processedCount++;
                            _logger.LogError(ex, "Error processing playlist {PlaylistName} after {ElapsedTime}ms", dto.Name, playlistStopwatch.ElapsedMilliseconds);
                        }
                    }
                }

                stopwatch.Stop();
                var elapsedTime = FormatElapsedTime(stopwatch.ElapsedMilliseconds);
                var logMessage = $"Direct refresh completed: {successCount} successful, {failureCount} failed out of {processedCount} processed playlists (completed in {stopwatch.ElapsedMilliseconds}ms)";
                
                string notificationMessage;
                if (failureCount == 0)
                {
                    notificationMessage = $"All playlists refreshed successfully in {elapsedTime}.";
                }
                else
                {
                    notificationMessage = $"Playlist refresh completed: {successCount} successful, {failureCount} failed out of {processedCount} processed (in {elapsedTime})";
                }
                
                _logger.LogInformation(logMessage);

                return new RefreshResult
                {
                    Success = failureCount == 0,
                    NotificationMessage = notificationMessage,
                    LogMessage = logMessage,
                    SuccessCount = successCount,
                    FailureCount = failureCount
                };
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                var message = "Refresh operation was cancelled";
                var logMessage = $"Manual playlist refresh was cancelled (after {stopwatch.ElapsedMilliseconds}ms)";
                _logger.LogInformation(logMessage);
                return new RefreshResult
                {
                    Success = false,
                    NotificationMessage = message,
                    LogMessage = logMessage,
                    SuccessCount = 0,
                    FailureCount = 0
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var notificationMessage = "An error occurred during playlist refresh. Please check the logs for details.";
                var logMessage = $"Error during manual playlist refresh (after {stopwatch.ElapsedMilliseconds}ms): {ex.Message}";
                _logger.LogError(ex, logMessage);
                return new RefreshResult
                {
                    Success = false,
                    NotificationMessage = notificationMessage,
                    LogMessage = logMessage,
                    SuccessCount = 0,
                    FailureCount = 0
                };
            }
            finally
            {
                // Clean up memory - explicitly clear the caches to free memory from large media collections
                // This prevents memory leaks when processing large libraries with thousands of media items
                if (allUserMediaTypeCaches != null && allUserMediaTypeCaches.Count > 0)
                {
                    var totalCaches = allUserMediaTypeCaches.Count;
                    var totalCacheEntries = allUserMediaTypeCaches.Sum(cache => cache.Count);
                    _logger.LogDebug("Cleaning up {CacheCount} media type caches containing {TotalEntries} cache entries",
                        totalCaches, totalCacheEntries);

                    foreach (var cache in allUserMediaTypeCaches)
                    {
                        cache.Clear();
                    }
                    allUserMediaTypeCaches.Clear();
                }

                lockHandle?.Dispose();
                _logger.LogDebug("Released shared refresh lock");
            }
        }

        /// <summary>
        /// Refresh all smart lists (both playlists and collections) manually.
        /// This method processes ALL lists regardless of their ScheduleTrigger settings, since this is a manual operation.
        /// This method uses immediate failure if another refresh is already in progress.
        /// </summary>
        public async Task<RefreshResult> RefreshAllListsAsync(CancellationToken cancellationToken = default)
        {
            var overallStopwatch = Stopwatch.StartNew();
            RefreshResult? playlistResult = null;
            RefreshResult? collectionResult = null;

            try
            {
                _logger.LogInformation("Starting manual refresh of all smart lists (playlists and collections)");

                // Calculate total count of all lists upfront for unified batch tracking
                var fileSystem = new SmartListFileSystem(_applicationPaths);
                var plStore = new PlaylistStore(fileSystem);
                var collectionStore = new Services.Collections.CollectionStore(fileSystem);

                var allPlaylists = await plStore.GetAllAsync().ConfigureAwait(false);
                var enabledPlaylists = allPlaylists.Where(dto => dto.Enabled).ToList();
                
                var allCollections = await collectionStore.GetAllAsync().ConfigureAwait(false);
                var enabledCollections = allCollections.Where(dto => dto.Enabled).ToList();

                var totalListsCount = enabledPlaylists.Count + enabledCollections.Count;
                _logger.LogInformation("Found {PlaylistCount} enabled playlists and {CollectionCount} enabled collections (total: {TotalCount} lists)",
                    enabledPlaylists.Count, enabledCollections.Count, totalListsCount);

                // Refresh playlists first with unified batch tracking
                _logger.LogInformation("Refreshing all playlists...");
                playlistResult = await RefreshAllPlaylistsAsync(batchOffset: 0, totalBatchCount: totalListsCount, cancellationToken).ConfigureAwait(false);

                // Refresh collections with unified batch tracking (offset by playlist count)
                // Always attempt both, even if playlists had failures
                _logger.LogInformation("Refreshing all collections...");
                collectionResult = await RefreshAllCollectionsAsync(batchOffset: enabledPlaylists.Count, totalBatchCount: totalListsCount, cancellationToken).ConfigureAwait(false);

                overallStopwatch.Stop();
                var elapsedTime = FormatElapsedTime(overallStopwatch.ElapsedMilliseconds);
                
                // Check if there were any actual failures (based on failure count, not Success flag)
                var playlistHasFailures = HasActualFailures(playlistResult);
                var collectionHasFailures = HasActualFailures(collectionResult);
                
                string notificationMessage;
                string logMessage;
                
                if (!playlistHasFailures && !collectionHasFailures)
                {
                    // Simple success message when everything succeeds
                    notificationMessage = $"All lists refreshed successfully in {elapsedTime}.";
                }
                else
                {
                    // Include details when there are failures
                    notificationMessage = $"All lists refreshed. Playlists: {playlistResult.NotificationMessage}. Collections: {collectionResult.NotificationMessage}.";
                }
                
                logMessage = $"All lists refresh completed. Playlists: {playlistResult.LogMessage}. Collections: {collectionResult.LogMessage}. (Total time: {overallStopwatch.ElapsedMilliseconds}ms)";
                _logger.LogInformation(logMessage);

                return new RefreshResult
                {
                    Success = !playlistHasFailures && !collectionHasFailures,
                    NotificationMessage = notificationMessage,
                    LogMessage = logMessage,
                    SuccessCount = playlistResult.SuccessCount + collectionResult.SuccessCount,
                    FailureCount = playlistResult.FailureCount + collectionResult.FailureCount
                };
            }
            catch (OperationCanceledException)
            {
                overallStopwatch.Stop();
                var message = "Refresh operation was cancelled";
                var logMessage = $"Manual refresh of all lists was cancelled (after {overallStopwatch.ElapsedMilliseconds}ms)";
                _logger.LogInformation(logMessage);
                return new RefreshResult
                {
                    Success = false,
                    NotificationMessage = message,
                    LogMessage = logMessage,
                    SuccessCount = 0,
                    FailureCount = 0
                };
            }
            catch (Exception ex)
            {
                overallStopwatch.Stop();
                var notificationMessage = "An error occurred during list refresh. Please check the logs for details.";
                var logMessage = $"Error during manual refresh of all lists (after {overallStopwatch.ElapsedMilliseconds}ms): {ex.Message}";
                _logger.LogError(ex, logMessage);
                return new RefreshResult
                {
                    Success = false,
                    NotificationMessage = notificationMessage,
                    LogMessage = logMessage,
                    SuccessCount = 0,
                    FailureCount = 0
                };
            }
        }

        /// <summary>
        /// Refresh all smart collections manually without using Jellyfin scheduled tasks.
        /// This method processes ALL collections regardless of their ScheduleTrigger settings, since this is a manual operation.
        /// This method uses immediate failure if another refresh is already in progress.
        /// </summary>
        /// <param name="batchOffset">Offset for batch tracking (used when refreshing all lists together)</param>
        /// <param name="totalBatchCount">Total count for unified batch tracking (used when refreshing all lists together)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        private async Task<RefreshResult> RefreshAllCollectionsAsync(int batchOffset = 0, int? totalBatchCount = null, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();

            // Try to acquire the collection refresh lock with immediate failure (no waiting)
            // We need to hold the lock for the entire refresh operation to prevent concurrent refreshes
            _logger.LogDebug("Attempting to acquire collection refresh lock for manual refresh (immediate return)");
            
            var (lockAcquired, lockHandle) = await Services.Collections.CollectionService.TryAcquireRefreshLockAsync(cancellationToken);
            if (!lockAcquired)
            {
                var message = "A refresh is already in progress. Please try again shortly.";
                _logger.LogInformation("Manual collection refresh request rejected - another refresh is already in progress");
                return new RefreshResult
                {
                    Success = false,
                    NotificationMessage = message,
                    LogMessage = message,
                    SuccessCount = 0,
                    FailureCount = 0
                };
            }

            try
            {
                _logger.LogInformation("Starting manual refresh of all smart collections (acquired refresh lock)");

                // Create collection store
                var fileSystem = new SmartListFileSystem(_applicationPaths);
                var collectionStore = new Services.Collections.CollectionStore(fileSystem);

                var allDtos = await collectionStore.GetAllAsync().ConfigureAwait(false);

                _logger.LogInformation("Found {TotalCount} total collections for manual refresh", allDtos.Length);

                if (allDtos.Length == 0)
                {
                    stopwatch.Stop();
                    var message = "No collections found to refresh";
                    var earlyLogMessage = $"{message} (completed in {stopwatch.ElapsedMilliseconds}ms)";
                    _logger.LogInformation(earlyLogMessage);
                    return new RefreshResult
                    {
                        Success = true,
                        NotificationMessage = message,
                        LogMessage = earlyLogMessage,
                        SuccessCount = 0,
                        FailureCount = 0
                    };
                }

                // Log disabled collections for informational purposes
                var disabledCollections = allDtos.Where(dto => !dto.Enabled).ToList();
                if (disabledCollections.Count > 0)
                {
                    var disabledNames = string.Join(", ", disabledCollections.Select(c => $"'{c.Name}'"));
                    _logger.LogDebug("Skipping {DisabledCount} disabled collections: {DisabledNames}", disabledCollections.Count, disabledNames);
                }

                // Process all enabled collections
                var enabledCollections = allDtos.Where(dto => dto.Enabled).ToList();
                _logger.LogInformation("Processing {EnabledCount} enabled collections", enabledCollections.Count);

                var processedCount = 0;
                var successCount = 0;
                var failureCount = 0;
                var totalCollectionCount = enabledCollections.Count;
                // Use unified batch count if provided (for "Refresh All Lists"), otherwise use collection-only count
                var batchTotalCount = totalBatchCount ?? totalCollectionCount;
                var currentCollectionIndex = batchOffset; // Start from offset if provided

                foreach (var dto in enabledCollections)
                {
                    // Generate listId once at the start to ensure StartOperation and CompleteOperation use same ID
                    var listId = dto.Id ?? Guid.NewGuid().ToString();
                    
                    var collectionStopwatch = Stopwatch.StartNew();
                    var operationStarted = false;
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Increment current index for batch tracking (1-based for display)
                        currentCollectionIndex++;

                        // Start tracking refresh operation early, before any operations that might fail
                        // This ensures failures are always reported to the status service
                        _refreshStatusService?.StartOperation(
                            listId,
                            dto.Name,
                            Core.Enums.SmartListType.Collection,
                            Core.Enums.RefreshTriggerType.Manual,
                            0,
                            batchCurrentIndex: currentCollectionIndex,
                            batchTotalCount: batchTotalCount);
                        operationStarted = true;

                        // Get collection service
                        var collectionService = GetCollectionService();

                        // Create progress callback
                        Action<int, int>? progressCallback = (processed, total) =>
                        {
                            _refreshStatusService?.UpdateProgress(listId, processed, total);
                        };

                        var (success, message, collectionId) = await collectionService.RefreshAsync(dto, progressCallback, cancellationToken).ConfigureAwait(false);

                        collectionStopwatch.Stop();
                        
                        // Complete status tracking
                        _refreshStatusService?.CompleteOperation(
                            listId,
                            success,
                            success ? null : message);
                        
                        if (success)
                        {
                            // Save the collection to persist LastRefreshed timestamp
                            await collectionStore.SaveAsync(dto).ConfigureAwait(false);

                            successCount++;
                            _logger.LogDebug("Collection {CollectionName} processed successfully in {ElapsedTime}ms: {Message}",
                                dto.Name, collectionStopwatch.ElapsedMilliseconds, message);
                        }
                        else
                        {
                            failureCount++;
                            _logger.LogWarning("Collection {CollectionName} processing failed after {ElapsedTime}ms: {Message}",
                                dto.Name, collectionStopwatch.ElapsedMilliseconds, message);
                        }

                        processedCount++;
                    }
                    catch (OperationCanceledException)
                    {
                        collectionStopwatch.Stop();
                        
                        // Only complete operation if it was started
                        if (operationStarted)
                        {
                            _refreshStatusService?.CompleteOperation(
                                listId,
                                false,
                                "Refresh operation was cancelled");
                        }
                        
                        _logger.LogInformation("Direct collection refresh operation was cancelled");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        collectionStopwatch.Stop();
                        
                        // Only complete operation if it was started
                        if (operationStarted)
                        {
                            _refreshStatusService?.CompleteOperation(
                                listId,
                                false,
                                ex.Message);
                        }
                        
                        failureCount++;
                        processedCount++;
                        _logger.LogError(ex, "Error processing collection {CollectionName} after {ElapsedTime}ms", dto.Name, collectionStopwatch.ElapsedMilliseconds);
                    }
                }

                stopwatch.Stop();
                var elapsedTime = FormatElapsedTime(stopwatch.ElapsedMilliseconds);
                var logMessage = $"Direct refresh completed: {successCount} successful, {failureCount} failed out of {processedCount} processed collections (completed in {stopwatch.ElapsedMilliseconds}ms)";
                
                string notificationMessage;
                if (failureCount == 0)
                {
                    notificationMessage = $"All collections refreshed successfully in {elapsedTime}.";
                }
                else
                {
                    notificationMessage = $"Collection refresh completed: {successCount} successful, {failureCount} failed out of {processedCount} processed (in {elapsedTime})";
                }
                
                _logger.LogInformation(logMessage);

                return new RefreshResult
                {
                    Success = failureCount == 0,
                    NotificationMessage = notificationMessage,
                    LogMessage = logMessage,
                    SuccessCount = successCount,
                    FailureCount = failureCount
                };
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                var message = "Refresh operation was cancelled";
                var logMessage = $"Manual collection refresh was cancelled (after {stopwatch.ElapsedMilliseconds}ms)";
                _logger.LogInformation(logMessage);
                return new RefreshResult
                {
                    Success = false,
                    NotificationMessage = message,
                    LogMessage = logMessage,
                    SuccessCount = 0,
                    FailureCount = 0
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var notificationMessage = "An error occurred during collection refresh. Please check the logs for details.";
                var logMessage = $"Error during manual collection refresh (after {stopwatch.ElapsedMilliseconds}ms): {ex.Message}";
                _logger.LogError(ex, logMessage);
                return new RefreshResult
                {
                    Success = false,
                    NotificationMessage = notificationMessage,
                    LogMessage = logMessage,
                    SuccessCount = 0,
                    FailureCount = 0
                };
            }
            finally
            {
                // Always release the refresh lock
                lockHandle?.Dispose();
                _logger.LogDebug("Released collection refresh lock after manual refresh");
            }
        }

        /// <summary>
        /// Refresh a single smart playlist manually.
        /// This method provides the same functionality as the individual "Refresh" button in the UI.
        /// </summary>
        public async Task<(bool Success, string Message, string? JellyfinPlaylistId)> RefreshSinglePlaylistAsync(SmartPlaylistDto playlist, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(playlist);

            var listId = playlist.Id ?? Guid.NewGuid().ToString();
            bool operationStarted = false;

            try
            {
                _logger.LogInformation("Starting manual refresh of single playlist: {PlaylistName} ({PlaylistId})", playlist.Name, playlist.Id);

                var playlistService = GetPlaylistService();
                
                // Try to acquire the lock with immediate timeout (no waiting)
                // If we can't get it, return immediately without starting status tracking
                var (lockAcquired, lockHandle) = await Services.Playlists.PlaylistService.TryAcquireRefreshLockAsync(cancellationToken);
                if (!lockAcquired)
                {
                    _logger.LogInformation("Playlist refresh already in progress for: {PlaylistName} ({PlaylistId}). Lock could not be acquired.", playlist.Name, playlist.Id);
                    return (false, "Playlist refresh is already in progress, please try again in a moment.", string.Empty);
                }

                try
                {
                    _logger.LogDebug("Successfully acquired lock for playlist: {PlaylistName} ({PlaylistId}). Starting status tracking.", playlist.Name, playlist.Id);
                    
                    // We got the lock! Now start tracking the operation
                    operationStarted = true;
                    _refreshStatusService.StartOperation(
                        listId,
                        playlist.Name,
                        Core.Enums.SmartListType.Playlist,
                        Core.Enums.RefreshTriggerType.Manual,
                        0);

                    // Create progress callback
                    Action<int, int>? progressCallback = (processed, total) =>
                    {
                        _refreshStatusService.UpdateProgress(listId, processed, total);
                    };
                    
                    // Call RefreshAsync directly (not RefreshWithTimeoutAsync) since we already hold the lock
                    var (success, message, playlistId) = await playlistService.RefreshAsync(playlist, progressCallback, cancellationToken);

                    // Complete status tracking
                    _refreshStatusService.CompleteOperation(
                        listId,
                        success,
                        success ? null : message);

                    if (success)
                    {
                        _logger.LogInformation("Successfully refreshed single playlist: {PlaylistName} ({PlaylistId}) - Jellyfin ID: {JellyfinPlaylistId}",
                            playlist.Name, playlist.Id, playlistId ?? "none");
                    }
                    else
                    {
                        _logger.LogWarning("Failed to refresh single playlist: {PlaylistName} ({PlaylistId}). Error: {ErrorMessage}",
                            playlist.Name, playlist.Id, message);
                    }

                    return (success, message, playlistId);
                }
                finally
                {
                    // Always release the lock
                    lockHandle?.Dispose();
                    _logger.LogDebug("Released refresh lock for single playlist: {PlaylistName}", playlist.Name);
                }
            }
            catch (OperationCanceledException)
            {
                if (operationStarted)
                {
                    _refreshStatusService.CompleteOperation(listId, false, "Refresh operation was cancelled");
                }
                _logger.LogInformation("Single playlist refresh was cancelled for playlist: {PlaylistName} ({PlaylistId})", playlist.Name, playlist.Id);
                return (false, "Refresh operation was cancelled", string.Empty);
            }
            catch (Exception ex)
            {
                if (operationStarted)
                {
                    _refreshStatusService.CompleteOperation(listId, false, ex.Message);
                }
                _logger.LogError(ex, "Error during single playlist refresh for playlist: {PlaylistName} ({PlaylistId})", playlist.Name, playlist.Id);
                return (false, $"Error during playlist refresh: {ex.Message}", string.Empty);
            }
        }

        /// <summary>
        /// Refresh a single smart collection manually.
        /// This method provides the same functionality as the individual "Refresh" button in the UI.
        /// </summary>
        public async Task<(bool Success, string Message, string? JellyfinCollectionId)> RefreshSingleCollectionAsync(SmartCollectionDto collection, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(collection);

            var listId = collection.Id ?? Guid.NewGuid().ToString();
            bool operationStarted = false;

            try
            {
                _logger.LogInformation("Starting manual refresh of single collection: {CollectionName} ({CollectionId})", collection.Name, collection.Id);

                var collectionService = GetCollectionService();
                
                // Try to acquire the lock with immediate timeout (no waiting)
                // If we can't get it, return immediately without starting status tracking
                var (lockAcquired, lockHandle) = await Services.Collections.CollectionService.TryAcquireRefreshLockAsync(cancellationToken);
                if (!lockAcquired)
                {
                    _logger.LogInformation("Collection refresh already in progress for: {CollectionName} ({CollectionId}). Lock could not be acquired.", collection.Name, collection.Id);
                    return (false, "Collection refresh is already in progress, please try again in a moment.", string.Empty);
                }

                try
                {
                    _logger.LogDebug("Successfully acquired lock for collection: {CollectionName} ({CollectionId}). Starting status tracking.", collection.Name, collection.Id);
                    
                    // We got the lock! Now start tracking the operation
                    operationStarted = true;
                    _refreshStatusService.StartOperation(
                        listId,
                        collection.Name,
                        Core.Enums.SmartListType.Collection,
                        Core.Enums.RefreshTriggerType.Manual,
                        0);

                    // Create progress callback
                    Action<int, int>? progressCallback = (processed, total) =>
                    {
                        _refreshStatusService.UpdateProgress(listId, processed, total);
                    };
                    
                    // Call RefreshAsync directly (not RefreshWithTimeoutAsync) since we already hold the lock
                    var (success, message, collectionId) = await collectionService.RefreshAsync(collection, progressCallback, cancellationToken);

                    // Complete status tracking
                    _refreshStatusService.CompleteOperation(
                        listId,
                        success,
                        success ? null : message);

                    if (success)
                    {
                        _logger.LogInformation("Successfully refreshed single collection: {CollectionName} ({CollectionId}) - Jellyfin ID: {JellyfinCollectionId}",
                            collection.Name, collection.Id, collectionId ?? "none");
                    }
                    else
                    {
                        _logger.LogWarning("Failed to refresh single collection: {CollectionName} ({CollectionId}). Error: {ErrorMessage}",
                            collection.Name, collection.Id, message);
                    }

                    return (success, message, collectionId);
                }
                finally
                {
                    // Always release the lock
                    lockHandle?.Dispose();
                    _logger.LogDebug("Released refresh lock for single collection: {CollectionName}", collection.Name);
                }
            }
            catch (OperationCanceledException)
            {
                if (operationStarted)
                {
                    _refreshStatusService.CompleteOperation(listId, false, "Refresh operation was cancelled");
                }
                _logger.LogInformation("Single collection refresh was cancelled for collection: {CollectionName} ({CollectionId})", collection.Name, collection.Id);
                return (false, "Refresh operation was cancelled", string.Empty);
            }
            catch (Exception ex)
            {
                if (operationStarted)
                {
                    _refreshStatusService.CompleteOperation(listId, false, ex.Message);
                }
                _logger.LogError(ex, "Error during single collection refresh for collection: {CollectionName} ({CollectionId})", collection.Name, collection.Id);
                return (false, $"Error during collection refresh: {ex.Message}", string.Empty);
            }
        }
    }
}
