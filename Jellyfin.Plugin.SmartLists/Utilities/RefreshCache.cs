using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SmartLists.Core.Models;
using Jellyfin.Plugin.SmartLists.Services.Abstractions;
using Jellyfin.Plugin.SmartLists.Services.Playlists;
using Jellyfin.Plugin.SmartLists.Utilities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists
{
    /// <summary>
    /// Reusable caching helper for efficient batch playlist refreshes.
    /// Implements the same advanced caching strategy used by legacy scheduled tasks.
    /// </summary>
    public class RefreshCache
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly ISmartListService<SmartPlaylistDto> _playlistService;
        private readonly ISmartListStore<SmartPlaylistDto> _playlistStore;
        private readonly ILogger _logger;

        // No longer using instance-level cache - converted to per-invocation for thread safety

        public RefreshCache(
            ILibraryManager libraryManager,
            IUserManager userManager,
            ISmartListService<SmartPlaylistDto> playlistService,
            ISmartListStore<SmartPlaylistDto> playlistStore,
            ILogger logger)
        {
            _libraryManager = libraryManager;
            _userManager = userManager;
            _playlistService = playlistService;
            _playlistStore = playlistStore;
            _logger = logger;
        }

        /// <summary>
        /// Refreshes multiple playlists efficiently using shared caching.
        /// </summary>
        /// <param name="playlists">Playlists to refresh</param>
        /// <param name="updateLastRefreshTime">Whether to update LastRefreshed timestamp</param>
        /// <param name="batchProgressCallback">Optional callback invoked before processing each playlist (playlist ID)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Results for each playlist</returns>
        public async Task<List<PlaylistRefreshResult>> RefreshPlaylistsWithCacheAsync(
            List<SmartPlaylistDto> playlists,
            bool updateLastRefreshTime = false,
            Action<string>? batchProgressCallback = null,
            CancellationToken cancellationToken = default)
        {
            var results = new List<PlaylistRefreshResult>();

            if (playlists == null)
            {
                _logger.LogWarning("Playlists parameter is null, returning empty results");
                return results;
            }

            if (!playlists.Any())
            {
                return results;
            }

            _logger.LogDebug("Starting cached refresh of {PlaylistCount} playlists", playlists.Count);
            var totalStopwatch = Stopwatch.StartNew();

            try
            {
                // Create per-invocation cache dictionaries for thread safety
                var userMediaCache = new Dictionary<Guid, BaseItem[]>();
                var userCacheStats = new Dictionary<Guid, (int mediaCount, int playlistCount)>();

                // Handle playlists with missing/invalid User first
                // Helper predicate to check if a playlist has a valid user ID
                static bool IsValidUserId(SmartPlaylistDto p) => 
                    !string.IsNullOrEmpty(p.UserId) && 
                    Guid.TryParse(p.UserId, out var userId) && 
                    userId != Guid.Empty;

                var playlistsWithInvalidUser = playlists
                    .Where(p => !IsValidUserId(p))
                    .ToList();
                if (playlistsWithInvalidUser.Any())
                {
                    _logger.LogWarning("Found {InvalidCount} playlists with missing or invalid User, adding failure results", playlistsWithInvalidUser.Count);

                    // Add failure results for playlists with invalid User
                    foreach (var playlist in playlistsWithInvalidUser)
                    {
                        results.Add(new PlaylistRefreshResult
                        {
                            PlaylistId = playlist.Id ?? string.Empty,
                            PlaylistName = playlist.Name,
                            Success = false,
                            Message = "Missing or invalid User",
                            JellyfinPlaylistId = string.Empty,
                        });
                    }
                }

                // Group playlists by user (same as legacy tasks)
                var playlistsByUser = playlists
                    .Where(IsValidUserId)
                    .GroupBy(p => Guid.Parse(p.UserId))
                    .ToDictionary(g => g.Key, g => g.ToList());

                if (!playlistsByUser.Any())
                {
                    _logger.LogWarning("No playlists with valid UserId found for cached refresh");
                    return results; // Will contain failure results for invalid UserIds if any,
                }

                // Build user media cache ONCE for all playlists
                await BuildUserMediaCacheAsync(playlistsByUser, userMediaCache, userCacheStats);

                // Process playlists using cached media
                foreach (var kvp in playlistsByUser)
                {
                    var userId = kvp.Key;
                    var userPlaylists = kvp.Value;

                    // Use TryGetValue to avoid double lookup and handle missing cache entries
                    if (!userMediaCache.TryGetValue(userId, out var relevantUserMedia))
                    {
                        _logger.LogWarning("No cached media found for user {UserId}, adding failure results for {PlaylistCount} playlists", userId, userPlaylists.Count);

                        // Add failure results for all playlists of this user instead of silently dropping them
                        foreach (var playlist in userPlaylists)
                        {
                            results.Add(new PlaylistRefreshResult
                            {
                                PlaylistId = playlist.Id ?? string.Empty,
                                PlaylistName = playlist.Name,
                                Success = false,
                                Message = $"User {userId} not found or cache missing",
                                JellyfinPlaylistId = string.Empty,
                            });
                        }
                        continue;
                    }

                    var user = _userManager.GetUserById(userId);
                    if (user == null)
                    {
                        _logger.LogError("User {UserId} not found in UserManager, adding failure results for {PlaylistCount} playlists", userId, userPlaylists.Count);

                        // Add failure results for all playlists of this user
                        foreach (var playlist in userPlaylists)
                        {
                            results.Add(new PlaylistRefreshResult
                            {
                                PlaylistId = playlist.Id ?? string.Empty,
                                PlaylistName = playlist.Name,
                                Success = false,
                                Message = $"User {userId} not found in system",
                                JellyfinPlaylistId = string.Empty,
                            });
                        }
                        continue;
                    }

                    _logger.LogDebug("Processing {PlaylistCount} playlists for user {Username} using cached media ({MediaCount} items)",
                        userPlaylists.Count, user.Username, relevantUserMedia.Length);

                    // Use the same advanced caching as legacy tasks
                    var userResults = await ProcessUserPlaylistsWithAdvancedCachingAsync(
                        user, userPlaylists, relevantUserMedia, updateLastRefreshTime, batchProgressCallback, cancellationToken);

                    results.AddRange(userResults);
                }

                totalStopwatch.Stop();

                // Log cache effectiveness summary
                var totalProcessedPlaylists = userCacheStats.Values.Sum(s => s.playlistCount);
                var totalCachedItems = userCacheStats.Values.Sum(s => s.mediaCount);
                var successCount = results.Count(r => r.Success);

                _logger.LogInformation("Cached refresh completed in {ElapsedTime}ms: {SuccessCount}/{TotalCount} playlists processed using {CachedItemCount} cached media items across {UserCount} users",
                    totalStopwatch.ElapsedMilliseconds, successCount, totalProcessedPlaylists, totalCachedItems, userCacheStats.Count);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh playlists with caching");

                // Return failure results for all playlists
                foreach (var playlist in playlists)
                {
                    results.Add(new PlaylistRefreshResult
                    {
                        PlaylistId = playlist.Id ?? string.Empty,
                        PlaylistName = playlist.Name,
                        Success = false,
                        Message = $"Cache refresh failed: {ex.Message}",
                    });
                }

                return results;
            }
            finally
            {
                // No longer need to clear cache - using per-invocation dictionaries for thread safety
            }
        }

        private Task BuildUserMediaCacheAsync(
            Dictionary<Guid, List<SmartPlaylistDto>> playlistsByUser,
            Dictionary<Guid, BaseItem[]> userMediaCache,
            Dictionary<Guid, (int mediaCount, int playlistCount)> userCacheStats)
        {
            foreach (var kvp in playlistsByUser)
            {
                var userId = kvp.Key;
                var userPlaylists = kvp.Value;
                var user = _userManager.GetUserById(userId);
                if (user == null)
                {
                    _logger.LogWarning("User {UserId} not found, skipping {PlaylistCount} playlists", userId, userPlaylists.Count);
                    continue;
                }

                // Check if any playlist actually needs "all media" (expensive operation)
                var needsAllMedia = userPlaylists.Any(p => p.MediaTypes == null || p.MediaTypes.Count == 0);

                if (needsAllMedia)
                {
                    // Build cache for this user's media (expensive operations happen HERE only)
                    var mediaFetchStopwatch = Stopwatch.StartNew();
                    var relevantUserMedia = GetRelevantUserMedia(user);
                    mediaFetchStopwatch.Stop();

                    userMediaCache[userId] = relevantUserMedia;
                    userCacheStats[userId] = (relevantUserMedia.Length, userPlaylists.Count);

                    _logger.LogDebug("Cached {MediaCount} items for user {Username} ({UserId}) in {ElapsedTime}ms - will be shared across {PlaylistCount} playlists",
                        relevantUserMedia.Length, user.Username, userId, mediaFetchStopwatch.ElapsedMilliseconds, userPlaylists.Count);
                }
                else
                {
                    // Skip expensive all-media fetch since no playlist needs it
                    userMediaCache[userId] = new BaseItem[0]; // Empty array as sentinel
                    userCacheStats[userId] = (0, userPlaylists.Count); // No media fetched

                    _logger.LogDebug("Skipped expensive media fetch for user {Username} ({UserId}) - no playlists require all media (will use per-media-type caching)",
                        user.Username, userId);
                }
            }

            return Task.CompletedTask;
        }

        private async Task<List<PlaylistRefreshResult>> ProcessUserPlaylistsWithAdvancedCachingAsync(
            User user,
            List<SmartPlaylistDto> userPlaylists,
            BaseItem[] relevantUserMedia,
            bool updateLastRefreshTime,
            Action<string>? batchProgressCallback,
            CancellationToken cancellationToken)
        {
            var results = new List<PlaylistRefreshResult>();

            // OPTIMIZATION: Cache media by MediaTypes to avoid redundant queries for playlists with same media types
            // Use Lazy<T> to ensure value factory executes only once per key, even under concurrent access
            var userMediaTypeCache = new ConcurrentDictionary<MediaTypesKey, Lazy<BaseItem[]>>();

            foreach (var playlist in userPlaylists)
            {
                var playlistStopwatch = Stopwatch.StartNew();

                try
                {
                    // Invoke batch progress callback before processing this playlist
                    batchProgressCallback?.Invoke(playlist.Id ?? string.Empty);

                    _logger.LogDebug("Processing playlist {PlaylistName} with {RuleSetCount} rule sets using cached media ({MediaCount} items)",
                        playlist.Name, playlist.ExpressionSets?.Count ?? 0, relevantUserMedia.Length);

                    // OPTIMIZATION: Get media specifically for this playlist's media types using cache
                    var mediaTypesForClosure = playlist.MediaTypes?.ToList() ?? new List<string>();
                    var mediaTypesKey = MediaTypesKey.Create(mediaTypesForClosure, playlist);

                    var playlistSpecificMedia = userMediaTypeCache.GetOrAdd(mediaTypesKey, _ =>
                        new Lazy<BaseItem[]>(() =>
                        {
                            // Use interface method instead of casting
                            var media = _playlistService.GetAllUserMediaForPlaylist(user, mediaTypesForClosure, playlist).ToArray();
                            _logger.LogDebug("Cached {MediaCount} items for MediaTypes [{MediaTypes}] for user '{Username}'",
                                media.Length, mediaTypesKey, user.Username);
                            return media;
                        }, LazyThreadSafetyMode.ExecutionAndPublication)
                    ).Value;

                    _logger.LogDebug("Playlist {PlaylistName} with MediaTypes [{MediaTypes}] has {PlaylistSpecificCount} specific items vs {CachedCount} cached items",
                        playlist.Name, mediaTypesKey, playlistSpecificMedia.Length, relevantUserMedia.Length);

                    // Use interface method instead of casting
                    var (success, message, jellyfinPlaylistId) = await _playlistService.ProcessPlaylistRefreshWithCachedMediaAsync(
                        playlist,
                        user,
                        playlistSpecificMedia,
                        async (updatedDto) => await _playlistStore.SaveAsync(updatedDto),
                        null,
                        cancellationToken);

                    if (success && updateLastRefreshTime)
                    {
                        playlist.LastRefreshed = DateTime.UtcNow; // Use UTC for consistent timestamps across timezones
                        await _playlistStore.SaveAsync(playlist);
                    }

                    playlistStopwatch.Stop();

                    results.Add(new PlaylistRefreshResult
                    {
                        PlaylistId = playlist.Id ?? string.Empty,
                        PlaylistName = playlist.Name,
                        Success = success,
                        Message = message,
                        ElapsedMilliseconds = playlistStopwatch.ElapsedMilliseconds,
                        JellyfinPlaylistId = jellyfinPlaylistId,
                    });

                    if (success)
                    {
                        _logger.LogDebug("Playlist {PlaylistName} processed successfully in {ElapsedTime}ms: {Message}",
                            playlist.Name, playlistStopwatch.ElapsedMilliseconds, message);
                    }
                    else
                    {
                        _logger.LogWarning("Playlist {PlaylistName} processing failed after {ElapsedTime}ms: {Message}",
                            playlist.Name, playlistStopwatch.ElapsedMilliseconds, message);
                    }
                }
                catch (Exception ex)
                {
                    playlistStopwatch.Stop();
                    _logger.LogError(ex, "Failed to process playlist {PlaylistName} after {ElapsedTime}ms",
                        playlist.Name, playlistStopwatch.ElapsedMilliseconds);

                    results.Add(new PlaylistRefreshResult
                    {
                        PlaylistId = playlist.Id ?? string.Empty,
                        PlaylistName = playlist.Name,
                        Success = false,
                        Message = $"Exception: {ex.Message}",
                        ElapsedMilliseconds = playlistStopwatch.ElapsedMilliseconds,
                    });
                }
            }

            return results;
        }

        private BaseItem[] GetRelevantUserMedia(User user)
        {
            // Get all supported media types that might be needed by any playlist
            // Use the centralized MediaTypes constants to ensure completeness
            var allUserMedia = new List<BaseItem>();

            // Get all supported BaseItemKind types from the MediaTypes constants
            var supportedItemTypes = Core.Constants.MediaTypes.MediaTypeToBaseItemKind.Values.ToArray();

            // Single query to get all supported media types at once (more efficient)
            allUserMedia.AddRange(_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = supportedItemTypes,
                IsVirtualItem = false,
                Recursive = true,
            }));

            return allUserMedia.ToArray();
        }

        // ClearCache method removed - no longer needed with per-invocation cache dictionaries
    }

    /// <summary>
    /// Result of a single playlist refresh operation
    /// </summary>
    public class PlaylistRefreshResult
    {
        public string PlaylistId { get; set; } = string.Empty;
        public string PlaylistName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public long ElapsedMilliseconds { get; set; }
        public string JellyfinPlaylistId { get; set; } = string.Empty;
    }

}
