using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist
{
    /// <summary>
    /// Reusable caching helper for efficient batch playlist refreshes.
    /// Implements the same advanced caching strategy used by legacy scheduled tasks.
    /// </summary>
    public class PlaylistRefreshCache
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;
        private readonly PlaylistService _playlistService;
        private readonly SmartPlaylistStore _playlistStore;
        private readonly ILogger _logger;

        // Cache storage
        private readonly Dictionary<Guid, BaseItem[]> _userMediaCache = new();
        private readonly Dictionary<Guid, (int mediaCount, int playlistCount)> _userCacheStats = new();
        
        public PlaylistRefreshCache(
            ILibraryManager libraryManager,
            IUserManager userManager,
            PlaylistService playlistService,
            SmartPlaylistStore playlistStore,
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
        /// <param name="updateLastRefreshTime">Whether to update LastScheduledRefresh timestamp</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Results for each playlist</returns>
        public async Task<List<PlaylistRefreshResult>> RefreshPlaylistsWithCacheAsync(
            List<SmartPlaylistDto> playlists,
            bool updateLastRefreshTime = false,
            CancellationToken cancellationToken = default)
        {
            var results = new List<PlaylistRefreshResult>();
            
            if (!playlists.Any())
            {
                return results;
            }

            _logger.LogDebug("Starting cached refresh of {PlaylistCount} playlists", playlists.Count);
            var totalStopwatch = Stopwatch.StartNew();

            try
            {
                // Group playlists by user (same as legacy tasks)
                var playlistsByUser = playlists
                    .Where(p => p.UserId != Guid.Empty)
                    .GroupBy(p => p.UserId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                if (!playlistsByUser.Any())
                {
                    _logger.LogWarning("No playlists with valid UserId found for cached refresh");
                    return results;
                }

                // Build user media cache ONCE for all playlists
                await BuildUserMediaCacheAsync(playlistsByUser);

                // Process playlists using cached media
                foreach (var kvp in playlistsByUser)
                {
                    var userId = kvp.Key;
                    var userPlaylists = kvp.Value;
                    if (!_userMediaCache.ContainsKey(userId))
                    {
                        continue; // User not found, already logged warning
                    }

                    var user = _userManager.GetUserById(userId);
                    var relevantUserMedia = _userMediaCache[userId];
                    
                    _logger.LogDebug("Processing {PlaylistCount} playlists for user {Username} using cached media ({MediaCount} items)", 
                        userPlaylists.Count, user?.Username ?? "Unknown", relevantUserMedia.Length);

                    // Use the same advanced caching as legacy tasks
                    var userResults = await ProcessUserPlaylistsWithAdvancedCachingAsync(
                        user, userPlaylists, relevantUserMedia, updateLastRefreshTime, cancellationToken);
                    
                    results.AddRange(userResults);
                }

                totalStopwatch.Stop();
                
                // Log cache effectiveness summary
                var totalProcessedPlaylists = _userCacheStats.Values.Sum(s => s.playlistCount);
                var totalCachedItems = _userCacheStats.Values.Sum(s => s.mediaCount);
                var successCount = results.Count(r => r.Success);
                
                _logger.LogInformation("Cached refresh completed in {ElapsedTime}ms: {SuccessCount}/{TotalCount} playlists processed using {CachedItemCount} cached media items across {UserCount} users", 
                    totalStopwatch.ElapsedMilliseconds, successCount, totalProcessedPlaylists, totalCachedItems, _userCacheStats.Count);

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
                        PlaylistId = playlist.Id,
                        PlaylistName = playlist.Name,
                        Success = false,
                        Message = $"Cache refresh failed: {ex.Message}"
                    });
                }
                
                return results;
            }
            finally
            {
                // Clear cache after use
                ClearCache();
            }
        }

        private Task BuildUserMediaCacheAsync(Dictionary<Guid, List<SmartPlaylistDto>> playlistsByUser)
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

                // Build cache for this user's media (expensive operations happen HERE only)
                var mediaFetchStopwatch = Stopwatch.StartNew();
                var relevantUserMedia = GetRelevantUserMedia(user);
                mediaFetchStopwatch.Stop();
                
                _userMediaCache[userId] = relevantUserMedia;
                _userCacheStats[userId] = (relevantUserMedia.Length, userPlaylists.Count);
                
                _logger.LogDebug("Cached {MediaCount} items for user {Username} ({UserId}) in {ElapsedTime}ms - will be shared across {PlaylistCount} playlists", 
                    relevantUserMedia.Length, user.Username, userId, mediaFetchStopwatch.ElapsedMilliseconds, userPlaylists.Count);
            }
            
            return Task.CompletedTask;
        }

        private async Task<List<PlaylistRefreshResult>> ProcessUserPlaylistsWithAdvancedCachingAsync(
            User user,
            List<SmartPlaylistDto> userPlaylists,
            BaseItem[] relevantUserMedia,
            bool updateLastRefreshTime,
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
                    _logger.LogDebug("Processing playlist {PlaylistName} with {RuleSetCount} rule sets using cached media ({MediaCount} items)", 
                        playlist.Name, playlist.ExpressionSets?.Count ?? 0, relevantUserMedia.Length);
                    
                    // OPTIMIZATION: Get media specifically for this playlist's media types using cache
                    var mediaTypesForClosure = playlist.MediaTypes?.ToList() ?? new List<string>();
                    var mediaTypesKey = MediaTypesKey.Create(mediaTypesForClosure, playlist);
                    
                    var playlistSpecificMedia = userMediaTypeCache.GetOrAdd(mediaTypesKey, _ =>
                        new Lazy<BaseItem[]>(() =>
                        {
                            var media = _playlistService.GetAllUserMediaForPlaylist(user, mediaTypesForClosure, playlist).ToArray();
                            _logger.LogDebug("Cached {MediaCount} items for MediaTypes [{MediaTypes}] for user '{Username}'", 
                                media.Length, mediaTypesKey, user.Username);
                            return media;
                        }, LazyThreadSafetyMode.ExecutionAndPublication)
                    ).Value;
                    
                    _logger.LogDebug("Playlist {PlaylistName} with MediaTypes [{MediaTypes}] has {PlaylistSpecificCount} specific items vs {CachedCount} cached items", 
                        playlist.Name, mediaTypesKey, playlistSpecificMedia.Length, relevantUserMedia.Length);
                    
                    var (success, message, jellyfinPlaylistId) = await _playlistService.ProcessPlaylistRefreshWithCachedMediaAsync(
                        playlist, 
                        user, 
                        playlistSpecificMedia,
                        async (updatedDto) => await _playlistStore.SaveAsync(updatedDto),
                        cancellationToken);
                    
                    if (success && updateLastRefreshTime)
                    {
                        playlist.LastScheduledRefresh = DateTime.Now;
                        await _playlistStore.SaveAsync(playlist);
                    }
                    
                    playlistStopwatch.Stop();
                    
                    results.Add(new PlaylistRefreshResult
                    {
                        PlaylistId = playlist.Id,
                        PlaylistName = playlist.Name,
                        Success = success,
                        Message = message,
                        ElapsedMilliseconds = playlistStopwatch.ElapsedMilliseconds,
                        JellyfinPlaylistId = jellyfinPlaylistId
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
                        PlaylistId = playlist.Id,
                        PlaylistName = playlist.Name,
                        Success = false,
                        Message = $"Exception: {ex.Message}",
                        ElapsedMilliseconds = playlistStopwatch.ElapsedMilliseconds
                    });
                }
            }

            return results;
        }

        private BaseItem[] GetRelevantUserMedia(User user)
        {
            // Get all media types that might be needed by any playlist
            // This mirrors the logic from RefreshPlaylistsTaskBase
            var allUserMedia = new List<BaseItem>();
            
            // Get movies
            allUserMedia.AddRange(_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                IsVirtualItem = false,
                Recursive = true
            }));
            
            // Get episodes  
            allUserMedia.AddRange(_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                IsVirtualItem = false,
                Recursive = true
            }));
            
            // Get audio
            allUserMedia.AddRange(_libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Audio },
                IsVirtualItem = false,
                Recursive = true
            }));

            return allUserMedia.ToArray();
        }

        private void ClearCache()
        {
            _userMediaCache.Clear();
            _userCacheStats.Clear();
        }
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
