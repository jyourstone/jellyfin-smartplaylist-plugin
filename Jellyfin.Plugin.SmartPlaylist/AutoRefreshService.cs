using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.SmartPlaylist.Constants;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist
{
    /// <summary>
    /// Represents the previous state of UserData for change detection
    /// </summary>
    internal class UserDataState
    {
        public bool Played { get; set; }
        public int PlayCount { get; set; }
        public bool IsFavorite { get; set; }
        public DateTime? LastPlayedDate { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    public enum LibraryChangeType
    {
        Added,
        Removed,
        Updated
    }

    /// <summary>
    /// Service for handling automatic smart playlist refreshes based on library changes.
    /// Implements intelligent batching and debouncing to handle high-frequency library events.
    /// </summary>
    public class AutoRefreshService : IDisposable
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<AutoRefreshService> _logger;
        private readonly ISmartPlaylistStore _playlistStore;
        private readonly IPlaylistService _playlistService;
        private readonly IUserDataManager _userDataManager;
        
        // Static reference for API access to cache management
        public static AutoRefreshService Instance { get; private set; }
        

        
        // State tracking
        private volatile bool _disposed = false;
        
        // UserData state tracking for change detection
        private readonly ConcurrentDictionary<string, UserDataState> _userDataStateCache = new();
        private const int MAX_USERDATA_CACHE_SIZE = 1000; // Limit cache size to prevent memory leaks
        
        // Performance optimization: Cache mapping rule types to playlists that use them
        // Key format: "MediaType+FieldType" (e.g., "Movie+IsPlayed", "Episode+SeriesName")
        private readonly ConcurrentDictionary<string, HashSet<string>> _ruleTypeToPlaylistsCache = new();
        private volatile bool _cacheInitialized = false;
        private readonly object _cacheInvalidationLock = new();
        
        // Batch processing for library events (add/remove) to avoid spam during bulk operations
        private readonly ConcurrentDictionary<string, DateTime> _pendingLibraryRefreshes = new();
        private readonly Timer _batchProcessTimer;
        private readonly TimeSpan _batchDelay = TimeSpan.FromSeconds(3); // Short delay for batching library events
        
        public AutoRefreshService(
            ILibraryManager libraryManager,
            ILogger<AutoRefreshService> logger,
            ISmartPlaylistStore playlistStore,
            IPlaylistService playlistService,
            IUserDataManager userDataManager)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _playlistStore = playlistStore;
            _playlistService = playlistService;
            _userDataManager = userDataManager;
            
            // Set static instance for API access
            Instance = this;
            
            // Initialize batch processing timer (runs every 1 second to check for pending refreshes)
            _batchProcessTimer = new Timer(ProcessPendingBatchRefreshes, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            

            
            // Subscribe to library events
            _libraryManager.ItemAdded += OnItemAdded;
            _libraryManager.ItemRemoved += OnItemRemoved;
            _libraryManager.ItemUpdated += OnItemUpdated;
            
            // Subscribe to user data events (for playback status changes)
            _userDataManager.UserDataSaved += OnUserDataSaved;
            
            _logger.LogDebug("AutoRefreshService initialized (library batch delay: {DelaySeconds}s; user data: immediate)", _batchDelay.TotalSeconds);
            
            // Initialize the rule cache in the background
            _ = Task.Run(InitializeRuleCache);
        }
        
        private async Task InitializeRuleCache()
        {
            try
            {
                _logger.LogDebug("Initializing rule type cache for performance optimization...");
                
                var playlists = await _playlistStore.GetAllSmartPlaylistsAsync();
                var cacheEntries = 0;
                
                foreach (var playlist in playlists)
                {
                    if (playlist.Enabled && playlist.AutoRefresh != AutoRefreshMode.Never)
                    {
                        AddPlaylistToRuleCache(playlist);
                        cacheEntries++;
                    }
                }
                
                _cacheInitialized = true;
                _logger.LogInformation("Rule type cache initialized with {CacheEntries} playlist mappings across {RuleTypes} rule types", 
                    cacheEntries, _ruleTypeToPlaylistsCache.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize rule type cache - will fall back to checking all playlists");
            }
        }
        
        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (_disposed) return;
            if (e.Item == null) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleLibraryChangeAsync(e.Item, LibraryChangeType.Added, isLibraryEvent: true).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling item added event for {ItemName}", e.Item?.Name ?? "unknown");
                }
            });
        }
        
        private void OnItemRemoved(object sender, ItemChangeEventArgs e)
        {
            if (_disposed) return;
            if (e.Item == null) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleLibraryChangeAsync(e.Item, LibraryChangeType.Removed, isLibraryEvent: true).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling item removed event for {ItemName}", e.Item?.Name ?? "unknown");
                }
            });
        }
        
        private void OnItemUpdated(object sender, ItemChangeEventArgs e)
        {
            if (_disposed) return;
            if (e.Item == null) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleLibraryChangeAsync(e.Item, LibraryChangeType.Updated, isLibraryEvent: true).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling item updated event for {ItemName}", e.Item?.Name ?? "unknown");
                }
            });
        }
        
        private void OnUserDataSaved(object sender, UserDataSaveEventArgs e)
        {
            if (_disposed) return;
            if (e.Item == null) return;
            
            // Handle playback status changes (IsPlayed, PlayCount, etc.)
            try
            {
                var item = _libraryManager.GetItemById(e.Item.Id);
                if (item != null && IsRelevantUserDataChange(e))
                {
                    _logger.LogDebug("Relevant user data change for item '{ItemName}' by user {UserId} - triggering auto-refresh", 
                        item.Name, e.UserId);
                    
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await HandleLibraryChangeAsync(item, LibraryChangeType.Updated, isLibraryEvent: false, triggeringUserId: e.UserId).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error handling user data change for {ItemName}", item?.Name ?? "unknown");
                        }
                    });
                }
                else if (item != null)
                {
                    _logger.LogDebug("Ignoring non-relevant user data change for item '{ItemName}' (progress update, etc.)", item.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling UserDataSaved event for item {ItemId}", e.Item?.Id);
            }
        }
        
        private bool IsRelevantUserDataChange(UserDataSaveEventArgs e)
        {
            if (e.UserData == null) return false;
            
            try
            {
                var userData = e.UserData;
                var itemId = e.Item.Id.ToString();
                var userId = e.UserId.ToString();
                var cacheKey = $"{itemId}:{userId}";
                
                // Extract current state
                var currentState = new UserDataState
                {
                    Played = userData.Played,
                    PlayCount = userData.PlayCount,
                    IsFavorite = userData.IsFavorite,
                    LastPlayedDate = userData.LastPlayedDate,
                    LastUpdated = DateTime.UtcNow
                };
                
                // Check if we have previous state
                if (_userDataStateCache.TryGetValue(cacheKey, out var previousState))
                {
                    // Compare with previous state to detect meaningful changes
                    var hasSignificantChange = 
                        currentState.Played != previousState.Played ||           // Watch/unwatch
                        currentState.PlayCount != previousState.PlayCount ||     // Play count changed
                        currentState.IsFavorite != previousState.IsFavorite ||   // Favorite status changed
                        (currentState.LastPlayedDate != previousState.LastPlayedDate && 
                         currentState.LastPlayedDate.HasValue);                  // Last played date set (not cleared)
                    
                    if (hasSignificantChange)
                    {
                        _logger.LogDebug("Detected significant UserData change for item {ItemId}: Played {PrevPlayed}→{CurrPlayed}, PlayCount {PrevCount}→{CurrCount}, Favorite {PrevFav}→{CurrFav}", 
                            itemId, previousState.Played, currentState.Played, previousState.PlayCount, currentState.PlayCount, 
                            previousState.IsFavorite, currentState.IsFavorite);
                        
                        // Update cache with new state
                        _userDataStateCache[cacheKey] = currentState;
                        
                        // Cleanup cache if it gets too large
                        CleanupUserDataCacheIfNeeded();
                        
                        return true;
                    }
                    else
                    {
                        // No significant change - likely just a progress update
                        return false;
                    }
                }
                else
                {
                    // First time seeing this item/user combo - store state and be conservative
                    _userDataStateCache[cacheKey] = currentState;
                    
                    // For first-time events, only trigger if it's a meaningful state (watched, favorite, etc.)
                    var isMeaningfulState = currentState.Played || currentState.PlayCount > 0 || 
                                          currentState.IsFavorite || currentState.LastPlayedDate.HasValue;
                    
                    if (isMeaningfulState)
                    {
                        _logger.LogDebug("First UserData event for item {ItemId} with meaningful state: Played={Played}, PlayCount={PlayCount}, Favorite={IsFavorite}", 
                            itemId, currentState.Played, currentState.PlayCount, currentState.IsFavorite);
                    }
                    
                    return isMeaningfulState;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking UserData relevance - assuming relevant");
                return true; // Default to processing if we can't determine
            }
        }
        
        private void CleanupUserDataCacheIfNeeded()
        {
            if (_userDataStateCache.Count > MAX_USERDATA_CACHE_SIZE)
            {
                try
                {
                    // Remove oldest entries (simple cleanup - remove 25% of entries)
                    var entriesToRemove = _userDataStateCache.Count / 4;
                    var oldestEntries = _userDataStateCache
                        .OrderBy(kvp => kvp.Value.LastUpdated)
                        .Take(entriesToRemove)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    
                    foreach (var key in oldestEntries)
                    {
                        _userDataStateCache.TryRemove(key, out _);
                    }
                    
                    _logger.LogDebug("Cleaned up {Count} old UserData cache entries", oldestEntries.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cleaning up UserData cache");
                }
            }
        }
        
        private async Task HandleLibraryChangeAsync(BaseItem item, LibraryChangeType changeType, bool isLibraryEvent, Guid? triggeringUserId = null)
        {
            try
            {
                // Skip processing if the item is a playlist itself - playlists updating shouldn't trigger other playlist refreshes
                if (item is MediaBrowser.Controller.Playlists.Playlist)
                {
                    _logger.LogDebug("Skipping auto-refresh for playlist item '{ItemName}' - playlists don't trigger other playlist refreshes", item.Name);
                    return;
                }
                
                // Find playlists that might be affected by this change
                var affectedPlaylistIds = await GetAffectedPlaylistsAsync(item, changeType, triggeringUserId).ConfigureAwait(false);
                
                if (affectedPlaylistIds.Any())
                {
                    if (isLibraryEvent)
                    {
                        // Library events (add/remove/update) - use batching to avoid spam during bulk operations
                        _logger.LogDebug("Queued {PlaylistCount} playlists for batched refresh due to {ChangeType} of '{ItemName}'", 
                            affectedPlaylistIds.Count, changeType, item.Name);
                        
                        foreach (var playlistId in affectedPlaylistIds)
                        {
                            _pendingLibraryRefreshes[playlistId] = DateTime.UtcNow.Add(_batchDelay);
                        }
                    }
                    else
                    {
                        // UserData events (playback status) - process immediately for instant feedback
                        _logger.LogDebug("Triggering instant refresh for {PlaylistCount} playlists due to playback status change for '{ItemName}'", 
                            affectedPlaylistIds.Count, item.Name);
                        
                        _ = Task.Run(async () => await ProcessPlaylistRefreshes(affectedPlaylistIds, isUserDataRefresh: true));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling library change for item {ItemName}", item.Name);
            }
        }
        
        private void ProcessPendingBatchRefreshes(object state)
        {
            if (_disposed) return;
            
            try
            {
                var now = DateTime.UtcNow;
                var readyToProcess = new List<string>();
                
                // Find playlists that are ready to be refreshed (delay has passed)
                foreach (var kvp in _pendingLibraryRefreshes.ToList())
                {
                    if (now >= kvp.Value)
                    {
                        readyToProcess.Add(kvp.Key);
                        _pendingLibraryRefreshes.TryRemove(kvp.Key, out _);
                    }
                }
                
                if (readyToProcess.Any())
                {
                    _logger.LogInformation("Auto-refreshing {PlaylistCount} smart playlists after library changes", readyToProcess.Count);
                    _logger.LogDebug("Processing batched refresh for {PlaylistCount} playlists after {DelaySeconds}s delay", 
                        readyToProcess.Count, _batchDelay.TotalSeconds);
                    
                    // Process the batch in background
                    _ = Task.Run(async () => await ProcessPlaylistRefreshes(readyToProcess, isUserDataRefresh: false));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing pending batch refreshes");
            }
        }
        
        private void AddPlaylistToRuleCache(SmartPlaylistDto playlist)
        {
            var mediaTypes = playlist.MediaTypes?.ToList() ?? [.. MediaTypes.All];
            
            // Handle playlists with no rules - they should be refreshed for any change to their media types
            if (playlist.ExpressionSets == null || !playlist.ExpressionSets.Any() || 
                !playlist.ExpressionSets.Any(es => es.Expressions?.Any() == true))
            {
                foreach (var mediaType in mediaTypes)
                {
                    // Use a special cache key for "any field" in this media type
                    var cacheKey = $"{mediaType}+*";
                    
                    _ruleTypeToPlaylistsCache.AddOrUpdate(
                        cacheKey,
                        new HashSet<string> { playlist.Id },
                        (key, existing) =>
                        {
                            existing.Add(playlist.Id);
                            return existing;
                        }
                    );
                }
                return;
            }
            
            // Handle playlists with specific rules
            foreach (var expressionSet in playlist.ExpressionSets)
            {
                if (expressionSet.Expressions == null) continue;
                
                foreach (var expression in expressionSet.Expressions)
                {
                    foreach (var mediaType in mediaTypes)
                    {
                        var cacheKey = $"{mediaType}+{expression.MemberName}";
                        
                        _ruleTypeToPlaylistsCache.AddOrUpdate(
                            cacheKey,
                            new HashSet<string> { playlist.Id },
                            (key, existing) =>
                            {
                                existing.Add(playlist.Id);
                                return existing;
                            }
                        );
                    }
                }
            }
        }
        
        private void RemovePlaylistFromRuleCache(string playlistId)
        {
            foreach (var kvp in _ruleTypeToPlaylistsCache.ToList())
            {
                kvp.Value.Remove(playlistId);
                if (kvp.Value.Count == 0)
                {
                    _ruleTypeToPlaylistsCache.TryRemove(kvp.Key, out _);
                }
            }
        }
        
        public void InvalidateRuleCache()
        {
            lock (_cacheInvalidationLock)
            {
                _ruleTypeToPlaylistsCache.Clear();
                _cacheInitialized = false;
            }
            
            // Start cache initialization outside the lock to avoid holding it unnecessarily
            _ = Task.Run(InitializeRuleCache);
        }
        
        public void UpdatePlaylistInCache(SmartPlaylistDto playlist)
        {
            lock (_cacheInvalidationLock)
            {
                if (!_cacheInitialized) return;
                
                try
                {
                    // Remove old entries for this playlist
                    RemovePlaylistFromRuleCache(playlist.Id);
                    
                    // Add new entries if playlist is enabled and has auto-refresh
                    if (playlist.Enabled && playlist.AutoRefresh != AutoRefreshMode.Never)
                    {
                        AddPlaylistToRuleCache(playlist);
                        _logger.LogDebug("Updated cache for playlist '{PlaylistName}'", playlist.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating cache for playlist '{PlaylistName}' - invalidating cache", playlist.Name);
                    // Clear cache and mark as uninitialized, but don't call InvalidateRuleCache to avoid recursive lock
                    _ruleTypeToPlaylistsCache.Clear();
                    _cacheInitialized = false;
                }
            }
            
            // If we had an error and cleared the cache, reinitialize it outside the lock
            if (!_cacheInitialized)
            {
                _ = Task.Run(InitializeRuleCache);
            }
        }
        
        public void RemovePlaylistFromCache(string playlistId)
        {
            lock (_cacheInvalidationLock)
            {
                if (!_cacheInitialized) return;
                
                try
                {
                    RemovePlaylistFromRuleCache(playlistId);
                    _logger.LogDebug("Removed playlist {PlaylistId} from cache", playlistId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error removing playlist {PlaylistId} from cache - invalidating cache", playlistId);
                    // Clear cache and mark as uninitialized, but don't call InvalidateRuleCache to avoid recursive lock
                    _ruleTypeToPlaylistsCache.Clear();
                    _cacheInitialized = false;
                }
            }
            
            // If we had an error and cleared the cache, reinitialize it outside the lock
            if (!_cacheInitialized)
            {
                _ = Task.Run(InitializeRuleCache);
            }
        }
        
        private async Task<List<string>> GetAffectedPlaylistsAsync(BaseItem item, LibraryChangeType changeType, Guid? triggeringUserId = null)
        {
            // Use cache for performance optimization if available
            if (_cacheInitialized)
            {
                return await GetAffectedPlaylistsFromCacheAsync(item, changeType, triggeringUserId).ConfigureAwait(false);
            }
            
            // Fallback to checking all playlists if cache not ready
            return await GetAffectedPlaylistsFallbackAsync(item, changeType, triggeringUserId).ConfigureAwait(false);
        }
        
        private async Task<List<string>> GetAffectedPlaylistsFromCacheAsync(BaseItem item, LibraryChangeType changeType, Guid? triggeringUserId = null)
        {
            var affectedPlaylists = new HashSet<string>();
            
            try
            {
                var mediaType = GetMediaTypeFromItem(item);
                
                // Get potential fields that could be affected by this change
                var potentialFields = GetPotentialAffectedFields(item, changeType);
                
                foreach (var field in potentialFields)
                {
                    var cacheKey = $"{mediaType}+{field}";
                    
                    if (_ruleTypeToPlaylistsCache.TryGetValue(cacheKey, out var playlistIds))
                    {
                        foreach (var playlistId in playlistIds)
                        {
                            affectedPlaylists.Add(playlistId);
                        }
                    }
                }
                
                // Also check for playlists with no rules (wildcard entries) for this media type
                var wildcardKey = $"{mediaType}+*";
                if (_ruleTypeToPlaylistsCache.TryGetValue(wildcardKey, out var wildcardPlaylistIds))
                {
                    foreach (var playlistId in wildcardPlaylistIds)
                    {
                        affectedPlaylists.Add(playlistId);
                    }
                }
                
                // Apply user-specific filtering for UserData events
                if (triggeringUserId.HasValue)
                {
                    var filteredPlaylists = await FilterPlaylistsByUserAsync(affectedPlaylists.ToList(), triggeringUserId.Value).ConfigureAwait(false);
                    _logger.LogDebug("Cache-based filtering: {ItemName} ({MediaType}) affects {PlaylistCount} playlists after user filtering (user: {UserId})", 
                        item.Name, mediaType, filteredPlaylists.Count, triggeringUserId.Value);
                    return filteredPlaylists;
                }
                
                _logger.LogDebug("Cache-based filtering: {ItemName} ({MediaType}) potentially affects {PlaylistCount} playlists", 
                    item.Name, mediaType, affectedPlaylists.Count);
                
                return affectedPlaylists.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error using cache to determine affected playlists for item {ItemName} - falling back", item.Name);
                return await GetAffectedPlaylistsFallbackAsync(item, changeType, triggeringUserId).ConfigureAwait(false);
            }
        }
        
        private async Task<List<string>> GetAffectedPlaylistsFallbackAsync(BaseItem item, LibraryChangeType changeType, Guid? triggeringUserId = null)
        {
            var affectedPlaylists = new List<string>();
            
            try
            {
                // Get all playlists that have auto-refresh enabled
                var allPlaylists = await _playlistStore.GetAllSmartPlaylistsAsync().ConfigureAwait(false);
                var autoRefreshPlaylists = allPlaylists.Where(p => p.AutoRefresh != AutoRefreshMode.Never && p.Enabled);
                
                foreach (var playlist in autoRefreshPlaylists)
                {
                    // Check if this playlist should be refreshed based on the change type
                    bool shouldRefresh = changeType switch
                    {
                        LibraryChangeType.Added or LibraryChangeType.Removed => 
                            playlist.AutoRefresh >= AutoRefreshMode.OnLibraryChanges,
                        LibraryChangeType.Updated => 
                            playlist.AutoRefresh >= AutoRefreshMode.OnAllChanges,
                        _ => false
                    };
                    
                    if (shouldRefresh)
                    {
                        // Additional filtering: check if the item type matches the playlist's media types
                        if (IsItemRelevantToPlaylist(item, playlist))
                        {
                            // User-specific filtering for UserData events (playback status changes)
                            if (triggeringUserId.HasValue && !IsUserRelevantToPlaylist(triggeringUserId.Value, playlist))
                            {
                                _logger.LogDebug("Skipping playlist '{PlaylistName}' - user {UserId} not relevant to this playlist", 
                                    playlist.Name, triggeringUserId.Value);
                                continue;
                            }
                            
                            affectedPlaylists.Add(playlist.Id);
                        }
                    }
                }
                
                _logger.LogDebug("Fallback filtering: {ItemName} potentially affects {PlaylistCount} playlists", 
                    item.Name, affectedPlaylists.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error determining affected playlists for item {ItemName}", item.Name);
            }
            
            return affectedPlaylists;
        }
        
        private bool IsUserRelevantToPlaylist(Guid userId, SmartPlaylistDto playlist)
        {
            try
            {
                // Check if the user is the playlist owner
                if (playlist.UserId == userId)
                {
                    return true;
                }
                
                // Check if the playlist has user-specific rules that reference this user
                if (playlist.ExpressionSets != null)
                {
                    foreach (var expressionSet in playlist.ExpressionSets)
                    {
                        if (expressionSet.Expressions != null)
                        {
                            foreach (var expression in expressionSet.Expressions)
                            {
                                // Check if this expression is user-specific and references our user
                                if (!string.IsNullOrEmpty(expression.UserId) && 
                                    Guid.TryParse(expression.UserId, out var expressionUserId) && 
                                    expressionUserId == userId)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking user relevance for playlist {PlaylistName}", playlist.Name);
                return true; // Default to including the playlist if we can't determine relevance
            }
        }
        
        private async Task<List<string>> FilterPlaylistsByUserAsync(List<string> playlistIds, Guid userId)
        {
            var filteredPlaylists = new List<string>();
            
            try
            {
                foreach (var playlistId in playlistIds)
                {
                    var playlist = await _playlistStore.GetSmartPlaylistAsync(Guid.Parse(playlistId)).ConfigureAwait(false);
                    if (playlist != null && IsUserRelevantToPlaylist(userId, playlist))
                    {
                        filteredPlaylists.Add(playlistId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error filtering playlists by user {UserId} - returning all playlists", userId);
                return playlistIds; // Return all playlists if filtering fails
            }
            
            return filteredPlaylists;
        }
        
        private List<string> GetPotentialAffectedFields(BaseItem item, LibraryChangeType changeType)
        {
            var fields = new List<string>();
            
            // Always check these fields for any change
            fields.AddRange(["Name", "DateCreated", "DateModified", "CommunityRating", "CriticRating"]);
            
            // Add fields specific to change type
            switch (changeType)
            {
                case LibraryChangeType.Added:
                case LibraryChangeType.Removed:
                    // Library changes affect these fields
                    fields.AddRange(["FolderPath", "Genres", "Studios", "Tags", "ProductionYear"]);
                    break;
                    
                case LibraryChangeType.Updated:
                    // Updates could affect any field, including playback status
                    fields.AddRange([
                        "IsPlayed", "PlayCount", "LastPlayedDate", "IsFavorite",
                        "FolderPath", "Genres", "Studios", "Tags", "ProductionYear",
                        "OfficialRating", "SeriesName", "SeasonNumber", "EpisodeNumber",
                        "NextUnwatched"
                    ]);
                    break;
            }
            
            return fields.Distinct().ToList();
        }
        
        private string GetMediaTypeFromItem(BaseItem item) => GetMediaTypeForItem(item);
        
        private bool IsItemRelevantToPlaylist(BaseItem item, SmartPlaylistDto playlist)
        {
            // If no media types specified, assume all types are relevant
            if (playlist.MediaTypes == null || !playlist.MediaTypes.Any())
                return true;
                
            // Check if the item's type matches any of the playlist's media types
            var itemMediaType = GetMediaTypeForItem(item);
            return playlist.MediaTypes.Contains(itemMediaType);
        }
        
        private string GetMediaTypeForItem(BaseItem item)
        {
            // Map Jellyfin item types to our MediaTypes constants
            return item switch
            {
                MediaBrowser.Controller.Entities.Movies.Movie => MediaTypes.Movie,
                MediaBrowser.Controller.Entities.TV.Series => MediaTypes.Series,
                MediaBrowser.Controller.Entities.TV.Episode => MediaTypes.Episode,
                MediaBrowser.Controller.Entities.Audio.Audio => MediaTypes.Audio,
                MediaBrowser.Controller.Entities.MusicVideo => MediaTypes.MusicVideo,
                MediaBrowser.Controller.Entities.Photo => MediaTypes.Photo,
                MediaBrowser.Controller.Entities.Book => MediaTypes.Book,
                _ => "Unknown"
            };
        }
        

        
        private async Task ProcessPlaylistRefreshes(List<string> playlistIds, bool isUserDataRefresh)
        {
            if (_disposed) return;
            
            try
            {
                if (isUserDataRefresh)
                {
                    _logger.LogInformation("Auto-refreshing {PlaylistCount} smart playlists due to playback status changes", playlistIds.Count);
                }
                
                var refreshTasks = playlistIds.Select(async playlistId =>
                {
                    try
                    {
                        var playlist = await _playlistStore.GetSmartPlaylistAsync(Guid.Parse(playlistId));
                        if (playlist != null)
                        {
                            var (success, message, _) = await _playlistService.RefreshSinglePlaylistWithTimeoutAsync(playlist);
                            _logger.LogDebug("Auto-refresh completed for playlist '{PlaylistName}': {Success} - {Message}", 
                                playlist.Name, success, message);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error auto-refreshing playlist {PlaylistId}", playlistId);
                    }
                });
                
                await Task.WhenAll(refreshTasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing playlist refreshes");
            }
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            // Unsubscribe from events
            _libraryManager.ItemAdded -= OnItemAdded;
            _libraryManager.ItemRemoved -= OnItemRemoved;
            _libraryManager.ItemUpdated -= OnItemUpdated;
            _userDataManager.UserDataSaved -= OnUserDataSaved;
            
            // Dispose timer
            _batchProcessTimer?.Dispose();
            
            // Clear pending refreshes
            _pendingLibraryRefreshes.Clear();
            
            // Clear UserData state cache
            _userDataStateCache.Clear();
            
            // Clear static instance
            if (Instance == this)
                Instance = null;
            
            _logger.LogDebug("AutoRefreshService disposed");
        }
    }
}
