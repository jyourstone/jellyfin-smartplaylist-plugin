using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartPlaylist.Constants;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist
{
    /// <summary>
    /// Cache key for media types to avoid string collision issues
    /// </summary>
    internal readonly record struct MediaTypesKey : IEquatable<MediaTypesKey>
    {
        private readonly string[] _sortedTypes;
        private readonly bool _hasCollectionsExpansion;

        private MediaTypesKey(string[] sortedTypes, bool hasCollectionsExpansion = false)
        {
            _sortedTypes = sortedTypes;
            _hasCollectionsExpansion = hasCollectionsExpansion;
        }

        public static MediaTypesKey Create(List<string> mediaTypes)
        {
            return Create(mediaTypes, null);
        }
        
        public static MediaTypesKey Create(List<string> mediaTypes, SmartPlaylistDto dto)
        {
            if (mediaTypes == null || mediaTypes.Count == 0)
            {
                return new MediaTypesKey([], false);
            }

            // Deduplicate to ensure identical cache keys for equivalent content (e.g., ["Movie", "Movie"] = ["Movie"])
            var sortedTypes = mediaTypes.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            
            // Determine Collections expansion flag
            bool collectionsExpansionFlag = false;
            if (dto != null)
            {
                // Include Collections episode expansion in cache key to avoid incorrect caching
                // when same media types have different expansion settings
                var hasCollectionsExpansion = dto.ExpressionSets?.Any(set => 
                    set.Expressions?.Any(expr => 
                        expr.MemberName == "Collections" && expr.IncludeEpisodesWithinSeries == true) == true) == true;
                
                // Use boolean flag instead of string marker to distinguish caches with Collections expansion            
                collectionsExpansionFlag = hasCollectionsExpansion && sortedTypes.Contains(MediaTypes.Episode) && !sortedTypes.Contains(MediaTypes.Series);
            }
            
            return new MediaTypesKey(sortedTypes, collectionsExpansionFlag);
        }

        public bool Equals(MediaTypesKey other)
        {
            // Handle null arrays (default struct case) and use SequenceEqual for cleaner comparison
            var thisArray = _sortedTypes ?? [];
            var otherArray = other._sortedTypes ?? [];
            
            return thisArray.AsSpan().SequenceEqual(otherArray.AsSpan()) && 
                   _hasCollectionsExpansion == other._hasCollectionsExpansion;
        }



        public override int GetHashCode()
        {
            // Handle null array (default struct case)
            var array = _sortedTypes ?? [];
            
            var hash = new HashCode();
            foreach (var type in array)
            {
                hash.Add(type, StringComparer.Ordinal);
            }
            hash.Add(_hasCollectionsExpansion);
            return hash.ToHashCode();
        }

        public override string ToString()
        {
            // Handle null array (default struct case)
            var array = _sortedTypes ?? [];
            var typesStr = array.Length == 0 ? "(empty)" : string.Join(",", array);
            return _hasCollectionsExpansion ? $"{typesStr}+CollectionsExpansion" : typesStr;
        }
    }

    /// <summary>
    /// Abstract base class for playlist refresh tasks.
    /// </summary>
    public abstract class RefreshPlaylistsTaskBase(
        IUserManager userManager,
        ILibraryManager libraryManager,
        ILogger logger,
        IServerApplicationPaths serverApplicationPaths,
        IPlaylistManager playlistManager,
        IUserDataManager userDataManager,
        IProviderManager providerManager) : IScheduledTask
    {
        protected readonly IUserManager userManager = userManager;
        protected readonly ILibraryManager libraryManager = libraryManager;
        protected readonly ILogger logger = logger;
        protected readonly IServerApplicationPaths serverApplicationPaths = serverApplicationPaths;
        protected readonly IPlaylistManager playlistManager = playlistManager;
        protected readonly IUserDataManager userDataManager = userDataManager;
        protected readonly IProviderManager providerManager = providerManager;

        // Simple semaphore to prevent concurrent migration saves (rare but can cause file corruption)
        private static readonly SemaphoreSlim _migrationSemaphore = new(1, 1);

        private PlaylistService GetPlaylistService()
        {
            try
            {
                // Use a wrapper logger that implements ILogger<PlaylistService>
                var playlistServiceLogger = new PlaylistServiceLogger(logger);
                return new PlaylistService(userManager, libraryManager, playlistManager, userDataManager, playlistServiceLogger, providerManager);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create PlaylistService");
                throw;
            }
        }

        // Wrapper class to adapt the refresh task logger for PlaylistService
        private class PlaylistServiceLogger(ILogger logger) : ILogger<PlaylistService>
        {
            public IDisposable BeginScope<TState>(TState state) => logger.BeginScope(state);
            public bool IsEnabled(LogLevel logLevel) => logger.IsEnabled(logLevel);
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) 
                => logger.Log(logLevel, eventId, state, exception, formatter);
        }

        /// <summary>
        /// Formats playlist name based on plugin configuration settings.
        /// </summary>
        /// <param name="playlistName">The base playlist name</param>
        /// <returns>The formatted playlist name</returns>
        public static string FormatPlaylistName(string playlistName)
        {
            return PlaylistNameFormatter.FormatPlaylistName(playlistName);
        }

        // Abstract properties that must be implemented by derived classes
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract string Key { get; }
        
        /// <summary>
        /// Gets the category.
        /// </summary>
        /// <value>The category.</value>
        public string Category => "Library";

        /// <summary>
        /// Filters playlists based on media type for the specific task implementation.
        /// </summary>
        /// <param name="playlists">All available playlists</param>
        /// <returns>Filtered playlists for this task type</returns>
        protected abstract IEnumerable<SmartPlaylistDto> FilterPlaylistsByMediaType(IEnumerable<SmartPlaylistDto> playlists);

        /// <summary>
        /// Gets media items optimized for the specific task type.
        /// </summary>
        /// <param name="user">The user</param>
        /// <returns>Media items relevant to this task type</returns>
        protected abstract IEnumerable<BaseItem> GetRelevantUserMedia(User user);

        /// <summary>
        /// Gets the media types this task handles (for logging purposes).
        /// </summary>
        protected abstract string GetHandledMediaTypes();

        /// <summary>
        /// Executes the task.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="progress">The progress.</param>
        /// <returns>Task.</returns>
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Declare cache variables outside try block so they're accessible in finally
            Dictionary<Guid, BaseItem[]> userMediaCache = [];
            Dictionary<Guid, (int MediaCount, int PlaylistCount)> userCacheStats = [];
            
            // Acquire the global refresh lock for the duration of the scheduled task
            using var refreshLock = await PlaylistService.AcquireRefreshLockAsync(cancellationToken);
            
            try
            {
                logger.LogDebug("Starting {TaskType} refresh task (acquired global refresh lock)", GetType().Name);

                // Create playlist store
                var fileSystem = new SmartPlaylistFileSystem(serverApplicationPaths);
                var plStore = new SmartPlaylistStore(fileSystem, userManager);

                var allDtos = await plStore.GetAllSmartPlaylistsAsync().ConfigureAwait(false);
                
                // Filter playlists by media type for this specific task
                var relevantDtos = FilterPlaylistsByMediaType(allDtos).ToArray();
                
                logger.LogInformation("Found {RelevantCount} relevant playlists out of {TotalCount} total (handling {MediaTypes})", 
                    relevantDtos.Length, allDtos.Length, GetHandledMediaTypes());
                
                if (relevantDtos.Length == 0)
                {
                    logger.LogDebug("No relevant playlists found for {TaskType}, exiting early", GetType().Name);
                    progress?.Report(100);
                    return;
                }
                
                // Log disabled playlists for informational purposes
                var disabledPlaylists = relevantDtos.Where(dto => !dto.Enabled).ToList();
                if (disabledPlaylists.Count > 0)
                {
                    var disabledNames = string.Join(", ", disabledPlaylists.Select(p => $"'{p.Name}'"));
                    logger.LogDebug("Skipping {DisabledCount} disabled playlists: {DisabledNames}", disabledPlaylists.Count, disabledNames);
                }
                
                // OPTIMIZATION: Cache media per user to avoid repeated fetching
                // Variables moved outside try block for cleanup in finally
                
                // Pre-process to resolve users and group playlists by actual user (not dto.UserId)
                // This ensures cache keys match the users that will actually be used during processing
                var resolvedPlaylists = new List<(SmartPlaylistDto dto, User user)>();
                var enabledPlaylists = relevantDtos.Where(dto => dto.Enabled).ToList();
                
                logger.LogDebug("Resolving users for {PlaylistCount} enabled playlists", enabledPlaylists.Count);
                
                foreach (var dto in enabledPlaylists)
                {
                    var user = await GetPlaylistUserAsync(dto);
                    if (user != null)
                    {
                        resolvedPlaylists.Add((dto, user));
                    }
                    else
                    {
                        logger.LogWarning("User not found for playlist '{PlaylistName}'. Skipping.", dto.Name);
                    }
                }
                
                // Group by actual resolved user ID
                var playlistsByUser = resolvedPlaylists
                    .GroupBy(p => p.user.Id)
                    .ToDictionary(g => g.Key, g => g.ToList());
                
                logger.LogDebug("Grouped {UserCount} users with {TotalPlaylists} playlists after user resolution", 
                    playlistsByUser.Count, resolvedPlaylists.Count);
                
                // Fetch media for each user once (optimized for this task type)
                foreach (var (userId, userPlaylistPairs) in playlistsByUser)
                {
                    var user = userPlaylistPairs.First().user; // All pairs have the same user
                    
                    var mediaFetchStopwatch = Stopwatch.StartNew();
                    var relevantUserMedia = GetRelevantUserMedia(user).ToArray();
                    mediaFetchStopwatch.Stop();
                    
                    userMediaCache[userId] = relevantUserMedia;
                    userCacheStats[userId] = (relevantUserMedia.Length, userPlaylistPairs.Count);
                    
                    logger.LogDebug("Cached {MediaCount} {MediaTypes} items for user '{Username}' ({UserId}) in {ElapsedTime}ms - will be shared across {PlaylistCount} playlists", 
                        relevantUserMedia.Length, GetHandledMediaTypes(), user.Username, userId, mediaFetchStopwatch.ElapsedMilliseconds, userPlaylistPairs.Count);
                }
                
                // Process playlists using cached media
                var processedCount = 0;
                var totalPlaylists = resolvedPlaylists.Count;
                
                // OPTIMIZATION: Process playlists in parallel batches for better performance
                var maxConcurrency = Environment.ProcessorCount; // Use available CPU cores
                logger.LogDebug("Using parallel processing with max concurrency of {MaxConcurrency} (CPU cores)", maxConcurrency);
                
                foreach (var (userId, userPlaylistPairs) in playlistsByUser)
                {
                    var user = userPlaylistPairs.First().user; // All pairs have the same user
                    var userPlaylists = userPlaylistPairs.Select(p => p.dto).ToList();
                    
                    var relevantUserMedia = userMediaCache[userId]; // Guaranteed to exist
                    logger.LogDebug("Processing {PlaylistCount} playlists for user '{Username}' using cached {MediaTypes} media ({MediaCount} items)", 
                        userPlaylists.Count, user.Username, GetHandledMediaTypes(), relevantUserMedia.Length);
                    
                    // OPTIMIZATION: Cache media by MediaTypes to avoid redundant queries for playlists with same media types
                    // Use Lazy<T> to ensure value factory executes only once per key, even under concurrent access
                    var userMediaTypeCache = new ConcurrentDictionary<MediaTypesKey, Lazy<BaseItem[]>>();
                    
                    // OPTIMIZATION: Create PlaylistService once per user, not once per playlist
                    var playlistService = GetPlaylistService();
                    
                    // Process playlists in parallel batches
                    var batchSize = Math.Max(1, maxConcurrency);
                    for (int i = 0; i < userPlaylists.Count; i += batchSize)
                    {
                        var batch = userPlaylists.Skip(i).Take(batchSize).ToList();
                        logger.LogDebug("Processing batch {BatchNumber} with {BatchSize} playlists for user '{Username}'", 
                            (i / batchSize) + 1, batch.Count, user.Username);
                        
                        var tasks = batch.Select(async dto =>
                        {
                            var playlistStopwatch = Stopwatch.StartNew();
                            try
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                
                                // User is already resolved - use the cached user instead of re-resolving
                                var playlistUser = user;
                                
                                // Validate that the playlist user is valid
                                if (playlistUser.Id == Guid.Empty)
                                {
                                    logger.LogWarning("Playlist '{PlaylistName}' has invalid user ID. Skipping.", dto.Name);
                                    return false; // Return failure status
                                }
                                
                                logger.LogDebug("Processing playlist {PlaylistName} with {RuleSetCount} rule sets using cached {MediaTypes} media ({MediaCount} items)", 
                                    dto.Name, dto.ExpressionSets?.Count ?? 0, GetHandledMediaTypes(), relevantUserMedia.Length);
                                
                                // OPTIMIZATION: Get media specifically for this playlist's media types using cache
                                // This ensures Movie playlists only get movies, not episodes/series, while avoiding redundant queries
                                var mediaTypesKey = MediaTypesKey.Create(dto.MediaTypes, dto);
                                var mediaTypesForClosure = dto.MediaTypes; // Avoid capturing entire dto in closure
                                // NOTE: Lazy<T> caches exceptions. This is intentional for database operations
                                // where failures typically indicate serious issues that should fail fast
                                // rather than retry repeatedly during the same scheduled task execution.
                                var playlistSpecificMedia = userMediaTypeCache.GetOrAdd(mediaTypesKey, _ =>
                                    new Lazy<BaseItem[]>(() =>
                                    {
                                        var media = playlistService.GetAllUserMediaForPlaylist(playlistUser, mediaTypesForClosure, dto).ToArray();
                                        logger.LogDebug("Cached {MediaCount} items for MediaTypes [{MediaTypes}] for user '{Username}'", 
                                            media.Length, mediaTypesKey, user.Username);
                                        return media;
                                    }, LazyThreadSafetyMode.ExecutionAndPublication)
                                ).Value;
                                
                                logger.LogDebug("Playlist {PlaylistName} with MediaTypes [{MediaTypes}] has {PlaylistSpecificCount} specific items vs {CachedCount} cached items", 
                                    dto.Name, mediaTypesKey, playlistSpecificMedia.Length, relevantUserMedia.Length);
                                
                                var (success, message, jellyfinPlaylistId) = await playlistService.ProcessPlaylistRefreshWithCachedMediaAsync(
                                    dto, 
                                    playlistUser, 
                                    playlistSpecificMedia, // Use playlist-specific media instead of generic cached media
                                    async (updatedDto) => await plStore.SaveAsync(updatedDto), // Save callback for when JellyfinPlaylistId is updated
                                    cancellationToken);
                                
                                playlistStopwatch.Stop();
                                if (success)
                                {
                                    logger.LogDebug("Playlist {PlaylistName} processed successfully in {ElapsedTime}ms: {Message}", 
                                        dto.Name, playlistStopwatch.ElapsedMilliseconds, message);
                                }
                                else
                                {
                                    logger.LogWarning("Playlist {PlaylistName} processing failed after {ElapsedTime}ms: {Message}", 
                                        dto.Name, playlistStopwatch.ElapsedMilliseconds, message);
                                }
                                
                                return success;
                            }
                            catch (Exception ex)
                            {
                                playlistStopwatch.Stop();
                                logger.LogError(ex, "Error processing playlist {PlaylistName} after {ElapsedTime}ms", dto.Name, playlistStopwatch.ElapsedMilliseconds);
                                return false; // Return failure status, but don't propagate exception
                            }
                        });
                        
                        // Wait for all tasks in this batch to complete and collect results
                        var results = await Task.WhenAll(tasks);
                        
                        // Log batch completion summary
                        var successCount = results.Count(r => r);
                        var failureCount = results.Count(r => !r);
                        if (failureCount > 0)
                        {
                            logger.LogWarning("Batch {BatchNumber} completed with {SuccessCount} successes and {FailureCount} failures", 
                                (i / batchSize) + 1, successCount, failureCount);
                        }
                        else
                        {
                            logger.LogDebug("Batch {BatchNumber} completed successfully with {SuccessCount} playlists", 
                                (i / batchSize) + 1, successCount);
                        }
                        
                        // Update progress after each batch
                        processedCount += batch.Count;
                        progress?.Report((double)processedCount / totalPlaylists * 100);
                    }
                }

                // Log optimization summary
                var totalMediaFetches = userCacheStats.Count;
                var totalMediaItems = userCacheStats.Values.Sum(s => s.MediaCount);
                var totalPlaylistsProcessed = userCacheStats.Values.Sum(s => s.PlaylistCount);
                var estimatedSavings = totalPlaylistsProcessed - totalMediaFetches;
                
                logger.LogDebug("BATCH PROCESSING SUMMARY ({TaskType}): Fetched {MediaTypes} media {FetchCount} times for {UserCount} users, processed {PlaylistCount} playlists. Estimated {Savings} fewer media fetches than sequential processing.", 
                    GetType().Name, GetHandledMediaTypes(), totalMediaFetches, userCacheStats.Count, totalPlaylistsProcessed, estimatedSavings);

                progress?.Report(100);
                stopwatch.Stop();
                logger.LogDebug("{TaskType} refresh task completed successfully in {TotalTime}ms", GetType().Name, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger.LogError(ex, "Error occurred during {TaskType} refresh task after {ElapsedTime}ms", GetType().Name, stopwatch.ElapsedMilliseconds);
                throw;
            }
            finally
            {
                // Clean up memory - explicitly clear the cache to free memory from large media collections
                // This prevents memory leaks when processing large libraries with thousands of media items
                if (userMediaCache != null)
                {
                    logger.LogDebug("Cleaning up media cache containing {CacheSize} user collections", userMediaCache.Count);
                    userMediaCache.Clear();
                }
                
                userCacheStats.Clear();
            }
        }

        /// <summary>
        /// Gets the default triggers.
        /// </summary>
        /// <returns>IEnumerable{TaskTriggerInfo}.</returns>
        public virtual IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return
            [
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromHours(1).Ticks
                }
            ];
        }

        /// <summary>
        /// Gets the user for a playlist, handling migration from old User field to new UserId field.
        /// </summary>
        /// <param name="playlist">The playlist.</param>
        /// <returns>The user, or null if not found.</returns>
        private async Task<User> GetPlaylistUserAsync(SmartPlaylistDto playlist)
        {
            // If new UserId field is set and not empty, use it
            if (playlist.UserId != Guid.Empty)
            {
                return userManager.GetUserById(playlist.UserId);
            }

            // Legacy migration: if old User field is set, try to find the user and migrate
#pragma warning disable CS0618 // Type or member is obsolete
            if (!string.IsNullOrEmpty(playlist.User))
            {
                var user = userManager.GetUserByName(playlist.User);
                if (user != null)
                {
                    logger.LogDebug("Migrating playlist '{PlaylistName}' from username '{UserName}' to User ID '{UserId}'", 
                        playlist.Name, playlist.User, user.Id);
                    
                    // Update the playlist with the User ID and save it
                    playlist.UserId = user.Id;
                    playlist.User = null; // Clear the old field
                    
                    try
                    {
                        // Use semaphore to prevent concurrent migration saves (rare but can cause file corruption)
                        await _migrationSemaphore.WaitAsync();
                        try
                        {
                            var fileSystem = new SmartPlaylistFileSystem(serverApplicationPaths);
                            var playlistStore = new SmartPlaylistStore(fileSystem, userManager);
                            await playlistStore.SaveAsync(playlist);
                            logger.LogDebug("Successfully migrated playlist '{PlaylistName}' to use User ID", playlist.Name);
                        }
                        finally
                        {
                            _migrationSemaphore.Release();
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to save migrated playlist '{PlaylistName}', but will continue with operation", playlist.Name);
                    }
                    
                    return user;
                }
                else
                {
                    logger.LogWarning("Legacy playlist '{PlaylistName}' references non-existent user '{UserName}'", playlist.Name, playlist.User);
                }
            }
#pragma warning restore CS0618 // Type or member is obsolete

            return null;
        }
    }
} 