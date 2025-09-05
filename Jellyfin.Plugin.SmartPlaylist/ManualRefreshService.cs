using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Entities;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist
{
    /// <summary>
    /// Service for handling manual refresh operations initiated by users.
    /// This includes both individual playlist refreshes and "Refresh All" operations from the UI.
    /// </summary>
    public interface IManualRefreshService
    {
        /// <summary>
        /// Refresh all smart playlists manually, bypassing the Jellyfin task system.
        /// This method processes ALL playlists regardless of their ScheduleTrigger settings.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Tuple of (success, message)</returns>
        Task<(bool Success, string Message)> RefreshAllPlaylistsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Refresh a single smart playlist manually.
        /// </summary>
        /// <param name="playlist">The playlist to refresh</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Tuple of (success, message, jellyfinPlaylistId)</returns>
        Task<(bool Success, string Message, string JellyfinPlaylistId)> RefreshSinglePlaylistAsync(SmartPlaylistDto playlist, CancellationToken cancellationToken = default);
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
        IUserDataManager userDataManager,
        IProviderManager providerManager,
        ILogger<ManualRefreshService> logger,
        Microsoft.Extensions.Logging.ILoggerFactory loggerFactory) : IManualRefreshService
    {
        // Global lock to prevent concurrent manual refresh operations
        private static readonly SemaphoreSlim _globalRefreshLock = new(1, 1);
        
        // Simple semaphore to prevent concurrent migration saves (rare but can cause file corruption)
        private static readonly SemaphoreSlim _migrationSemaphore = new(1, 1);
        private readonly IUserManager _userManager = userManager;
        private readonly ILibraryManager _libraryManager = libraryManager;
        private readonly IServerApplicationPaths _applicationPaths = applicationPaths;
        private readonly IPlaylistManager _playlistManager = playlistManager;
        private readonly IUserDataManager _userDataManager = userDataManager;
        private readonly IProviderManager _providerManager = providerManager;
        private readonly ILogger<ManualRefreshService> _logger = logger;
        private readonly Microsoft.Extensions.Logging.ILoggerFactory _loggerFactory = loggerFactory;

        /// <summary>
        /// Gets the user for a playlist, handling migration from old User field to new UserId field.
        /// </summary>
        /// <param name="playlist">The playlist.</param>
        /// <returns>The user, or null if not found.</returns>
        private async Task<User> GetPlaylistUserAsync(SmartPlaylistDto playlist)
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
        private Task<Guid> GetPlaylistUserIdAsync(SmartPlaylistDto playlist)
        {
            return Task.FromResult(playlist.UserId);
        }

        /// <summary>
        /// Creates a PlaylistService instance for playlist operations.
        /// </summary>
        private IPlaylistService GetPlaylistService()
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
        /// Refresh all smart playlists manually without using Jellyfin scheduled tasks.
        /// This method performs the same work as the scheduled tasks but processes ALL playlists
        /// regardless of their ScheduleTrigger settings, since this is a manual operation.
        /// This method uses immediate failure if another refresh is already in progress.
        /// </summary>
        public async Task<(bool Success, string Message)> RefreshAllPlaylistsAsync(CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Declare cache variables at method level so they're accessible in finally for cleanup
            var allUserMediaTypeCaches = new List<ConcurrentDictionary<MediaTypesKey, Lazy<BaseItem[]>>>();
            
            // Prevent concurrent manual refresh runs with immediate failure
            if (!await _globalRefreshLock.WaitAsync(0, cancellationToken))
            {
                _logger.LogInformation("Manual refresh request rejected - another refresh is already in progress");
                return (false, "A playlist refresh is already in progress. Please try again shortly.");
            }
            
            try
            {
                _logger.LogInformation("Starting manual refresh of all smart playlists (acquired refresh lock)");

                // Create playlist store
                var fileSystem = new SmartPlaylistFileSystem(_applicationPaths);
                var plStore = new SmartPlaylistStore(fileSystem, _userManager);
                var playlistService = GetPlaylistService();

                var allDtos = await plStore.GetAllSmartPlaylistsAsync().ConfigureAwait(false);
                
                _logger.LogInformation("Found {TotalCount} total playlists for manual refresh", allDtos.Length);
                
                if (allDtos.Length == 0)
                {
                    stopwatch.Stop();
                    var message = "No playlists found to refresh";
                    _logger.LogInformation("{Message} (completed in {ElapsedTime}ms)", message, stopwatch.ElapsedMilliseconds);
                    return (true, message);
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
                
                foreach (var dto in enabledPlaylists)
                {
                    var user = await GetPlaylistUserAsync(dto);
                    if (user != null)
                    {
                        resolvedPlaylists.Add((dto, user));
                    }
                    else
                    {
                        _logger.LogWarning("User not found for playlist '{PlaylistName}'. Skipping.", dto.Name);
                    }
                }

                // Group by user for efficient media caching
                var playlistsByUser = resolvedPlaylists
                    .GroupBy(p => p.user.Id)
                    .ToDictionary(g => g.Key, g => g.ToList());

                _logger.LogDebug("Grouped {UserCount} users with {TotalPlaylists} playlists after user resolution", 
                    playlistsByUser.Count, resolvedPlaylists.Count);

                // Process playlists with proper MediaTypes filtering
                var processedCount = 0;
                var successCount = 0;
                var failureCount = 0;

                foreach (var (userId, userPlaylistPairs) in playlistsByUser)
                {
                    var user = userPlaylistPairs.First().user;
                    var userPlaylists = userPlaylistPairs.Select(p => p.dto).ToList();
                    
                    _logger.LogDebug("Processing {PlaylistCount} playlists for user '{Username}'", 
                        userPlaylists.Count, user.Username);

                    // OPTIMIZATION: Cache media by MediaTypes to avoid redundant queries for playlists with same media types
                    // Use Lazy<T> to ensure value factory executes only once per key, even under concurrent access
                    var userMediaTypeCache = new ConcurrentDictionary<MediaTypesKey, Lazy<BaseItem[]>>();
                    allUserMediaTypeCaches.Add(userMediaTypeCache); // Track for cleanup

                    foreach (var dto in userPlaylists)
                    {
                        var playlistStopwatch = Stopwatch.StartNew();
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            // Validate that the playlist user is valid
                            if (user.Id == Guid.Empty)
                            {
                                _logger.LogWarning("Playlist '{PlaylistName}' has invalid user ID. Skipping.", dto.Name);
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

                            var refreshResult = await playlistService.ProcessPlaylistRefreshWithCachedMediaAsync(
                                dto, 
                                user, 
                                playlistSpecificMedia, // Use properly filtered and cached media
                                async (updatedDto) => await plStore.SaveAsync(updatedDto),
                                cancellationToken);
                            
                            playlistStopwatch.Stop();
                            if (refreshResult.Success)
                            {
                                // Save the playlist to persist LastRefreshed timestamp (same as legacy tasks)
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
                            _logger.LogInformation("Direct refresh operation was cancelled");
                            throw;
                        }
                        catch (Exception ex)
                        {
                            playlistStopwatch.Stop();
                            failureCount++;
                            processedCount++;
                            _logger.LogError(ex, "Error processing playlist {PlaylistName} after {ElapsedTime}ms", dto.Name, playlistStopwatch.ElapsedMilliseconds);
                        }
                    }
                }

                stopwatch.Stop();
                var resultMessage = $"Direct refresh completed: {successCount} successful, {failureCount} failed out of {processedCount} processed playlists (completed in {stopwatch.ElapsedMilliseconds}ms)";
                _logger.LogInformation(resultMessage);
                
                return (true, resultMessage);
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                _logger.LogInformation("Manual playlist refresh was cancelled (after {ElapsedTime}ms)", stopwatch.ElapsedMilliseconds);
                return (false, "Refresh operation was cancelled");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error during manual playlist refresh (after {ElapsedTime}ms)", stopwatch.ElapsedMilliseconds);
                return (false, $"Error during manual playlist refresh: {ex.Message}");
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
                
                _globalRefreshLock.Release();
                _logger.LogDebug("Released manual refresh lock");
            }
        }

        /// <summary>
        /// Refresh a single smart playlist manually.
        /// This method provides the same functionality as the individual "Refresh" button in the UI.
        /// </summary>
        public async Task<(bool Success, string Message, string JellyfinPlaylistId)> RefreshSinglePlaylistAsync(SmartPlaylistDto playlist, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Starting manual refresh of single playlist: {PlaylistName} ({PlaylistId})", playlist.Name, playlist.Id);

                var playlistService = GetPlaylistService();
                var result = await playlistService.RefreshSinglePlaylistWithTimeoutAsync(playlist, cancellationToken);
                
                if (result.Success)
                {
                    _logger.LogInformation("Successfully refreshed single playlist: {PlaylistName} ({PlaylistId}) - Jellyfin ID: {JellyfinPlaylistId}", 
                        playlist.Name, playlist.Id, result.JellyfinPlaylistId ?? "none");
                }
                else
                {
                    _logger.LogWarning("Failed to refresh single playlist: {PlaylistName} ({PlaylistId}). Error: {ErrorMessage}", 
                        playlist.Name, playlist.Id, result.Message);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Single playlist refresh was cancelled for playlist: {PlaylistName} ({PlaylistId})", playlist.Name, playlist.Id);
                return (false, "Refresh operation was cancelled", string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during single playlist refresh for playlist: {PlaylistName} ({PlaylistId})", playlist.Name, playlist.Id);
                return (false, $"Error during playlist refresh: {ex.Message}", string.Empty);
            }
        }
    }
}
