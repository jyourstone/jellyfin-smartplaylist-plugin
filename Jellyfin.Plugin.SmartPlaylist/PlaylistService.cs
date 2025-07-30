using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Data.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Playlists;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist
{
    /// <summary>
    /// Service for handling individual smart playlist operations.
    /// </summary>
    public interface IPlaylistService
    {
        Task<(bool Success, string Message, string JellyfinPlaylistId)> RefreshSinglePlaylistAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default);
        Task<(bool Success, string Message, string JellyfinPlaylistId)> RefreshSinglePlaylistWithTimeoutAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default);
        Task<(bool Success, string Message, string JellyfinPlaylistId)> ProcessPlaylistRefreshWithCachedMediaAsync(SmartPlaylistDto dto, User user, BaseItem[] allUserMedia, Func<SmartPlaylistDto, Task> saveCallback = null, CancellationToken cancellationToken = default);
        Task DeletePlaylistAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default);
        Task RemoveSmartSuffixAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default);
        Task EnablePlaylistAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default);
        Task DisablePlaylistAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default);
        Task<(bool Success, string Message)> TryRefreshAllPlaylistsAsync(CancellationToken cancellationToken = default);
    }

    public class PlaylistService(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IPlaylistManager playlistManager,
        IUserDataManager userDataManager,
        ILogger<PlaylistService> logger,
        IProviderManager providerManager) : IPlaylistService
    {
        private readonly IUserManager _userManager = userManager;
        private readonly ILibraryManager _libraryManager = libraryManager;
        private readonly IPlaylistManager _playlistManager = playlistManager;
        private readonly IUserDataManager _userDataManager = userDataManager;
        private readonly ILogger<PlaylistService> _logger = logger;
        private readonly IProviderManager _providerManager = providerManager;

        // Global semaphore to prevent concurrent refresh operations while preserving internal parallelism
        private static readonly SemaphoreSlim _refreshOperationLock = new(1, 1);



        /// <summary>
        /// Core method to process a single playlist refresh with cached media.
        /// This method is used by both single playlist refresh and batch processing.
        /// </summary>
        /// <param name="dto">The playlist DTO to process</param>
        /// <param name="user">The user for this playlist (already resolved)</param>
        /// <param name="allUserMedia">All media items for the user (can be cached)</param>
        /// <param name="saveCallback">Optional callback to save the DTO when JellyfinPlaylistId is updated</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Tuple of (success, message, jellyfinPlaylistId)</returns>
        public async Task<(bool Success, string Message, string JellyfinPlaylistId)> ProcessPlaylistRefreshWithCachedMediaAsync(
            SmartPlaylistDto dto, 
            User user, 
            BaseItem[] allUserMedia, 
            Func<SmartPlaylistDto, Task> saveCallback = null,
            CancellationToken cancellationToken = default)
        {
            return await ProcessPlaylistRefreshAsync(dto, user, allUserMedia, _logger, saveCallback, cancellationToken);
        }

        /// <summary>
        /// Core method to process a single playlist refresh. This is the shared logic used by both
        /// single playlist refresh and batch playlist refresh operations.
        /// </summary>
        /// <param name="dto">The playlist DTO to process</param>
        /// <param name="user">The user for this playlist (already resolved)</param>
        /// <param name="allUserMedia">All media items for the user (can be cached)</param>
        /// <param name="logger">Logger to use for this operation</param>
        /// <param name="saveCallback">Optional callback to save the DTO when JellyfinPlaylistId is updated</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Tuple of (success, message, jellyfinPlaylistId)</returns>
        private async Task<(bool Success, string Message, string JellyfinPlaylistId)> ProcessPlaylistRefreshAsync(
            SmartPlaylistDto dto, 
            User user, 
            BaseItem[] allUserMedia, 
            ILogger logger, 
            Func<SmartPlaylistDto, Task> saveCallback = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                logger.LogDebug("Processing playlist refresh: {PlaylistName}", dto.Name);
                
                // Check if playlist is enabled
                if (!dto.Enabled)
                {
                    logger.LogDebug("Smart playlist '{PlaylistName}' is disabled. Skipping refresh.", dto.Name);
                    return (true, "Playlist is disabled", string.Empty);
                }

                var smartPlaylist = new SmartPlaylist(dto);
                
                // Log the playlist rules
                logger.LogDebug("Processing playlist {PlaylistName} with {RuleSetCount} rule sets", dto.Name, dto.ExpressionSets?.Count ?? 0);
                
                logger.LogDebug("Found {MediaCount} total media items for user {User}", allUserMedia.Length, user.Username);
                
                var newItems = smartPlaylist.FilterPlaylistItems(allUserMedia, _libraryManager, user, _userDataManager, logger).ToArray();
                logger.LogDebug("Playlist {PlaylistName} filtered to {FilteredCount} items from {TotalCount} total items", 
                    dto.Name, newItems.Length, allUserMedia.Length);
                
                var newLinkedChildren = newItems.Select(itemId => 
                {
                    var item = _libraryManager.GetItemById(itemId);
                    return new LinkedChild 
                    { 
                        ItemId = itemId,
                        Path = item?.Path  // Set the Path property to prevent cleanup task from removing items
                    };
                }).ToArray();

                // Try to find existing playlist by Jellyfin playlist ID first, then by current naming format, then by old format
                Playlist existingPlaylist = null;
                
                logger.LogDebug("Looking for playlist: User={UserId}, JellyfinPlaylistId={JellyfinPlaylistId}", 
                    user.Id, dto.JellyfinPlaylistId);
                
                // First try to find by Jellyfin playlist ID (most reliable)
                if (!string.IsNullOrEmpty(dto.JellyfinPlaylistId) && Guid.TryParse(dto.JellyfinPlaylistId, out var jellyfinPlaylistId))
                {
                    if (_libraryManager.GetItemById(jellyfinPlaylistId) is Playlist playlistById)
                    {
                        existingPlaylist = playlistById;
                        logger.LogDebug("Found existing playlist by Jellyfin playlist ID: {JellyfinPlaylistId} - {PlaylistName}",
                            dto.JellyfinPlaylistId, existingPlaylist.Name);
                    }
                    else
                    {
                        logger.LogDebug("No playlist found by Jellyfin playlist ID: {JellyfinPlaylistId}", dto.JellyfinPlaylistId);
                    }
                }
                
                // Fallback to name-based lookup using OLD format (for backward compatibility)
                if (existingPlaylist == null)
                {
                    // Use the old hardcoded format for finding existing playlists
                    var oldFormatName = dto.Name + " [Smart]";
                    existingPlaylist = GetPlaylistByName(user, oldFormatName);
                    if (existingPlaylist != null)
                    {
                        logger.LogDebug("Found existing playlist by old format name: {PlaylistName}", oldFormatName);
                        
                        // Save the Jellyfin playlist ID now that we found it
                        dto.JellyfinPlaylistId = existingPlaylist.Id.ToString();
                        if (saveCallback != null)
                        {
                            try
                            {
                                await saveCallback(dto);
                                logger.LogDebug("Saved Jellyfin playlist ID {JellyfinPlaylistId} for playlist found by name fallback {PlaylistName}", 
                                    existingPlaylist.Id, dto.Name);
                            }
                            catch (Exception saveEx)
                            {
                                logger.LogWarning(saveEx, "Failed to save playlist DTO after name fallback for {PlaylistName}, but continuing with operation", dto.Name);
                            }
                        }
                    }
                    else
                    {
                        logger.LogDebug("No playlist found by old format name: {PlaylistName}", oldFormatName);
                    }
                }
                
                // Now that we've found the existing playlist (or not), apply the new naming format
                var smartPlaylistName = PlaylistNameFormatter.FormatPlaylistName(dto.Name);
                
                if (existingPlaylist != null)
                {
                    logger.LogDebug("Processing existing playlist: {PlaylistName} (ID: {PlaylistId})", existingPlaylist.Name, existingPlaylist.Id);
                    
                    // Check if the playlist name needs to be updated
                    var currentName = existingPlaylist.Name;
                    var expectedName = smartPlaylistName;
                    var nameChanged = currentName != expectedName;
                    
                    if (nameChanged)
                    {
                        logger.LogDebug("Playlist name changing from '{OldName}' to '{NewName}'", currentName, expectedName);
                        existingPlaylist.Name = expectedName;
                    }
                    
                    // Check if ownership needs to be updated
                    var ownershipChanged = existingPlaylist.OwnerUserId != user.Id;
                    if (ownershipChanged)
                    {
                        logger.LogDebug("Playlist ownership changing from {OldOwner} to {NewOwner}", existingPlaylist.OwnerUserId, user.Id);
                        existingPlaylist.OwnerUserId = user.Id;
                    }
                    
                    // Check if we need to update the playlist due to public/private setting change
                    // Use OpenAccess property instead of Shares.Any() as revealed by debugging
                    var openAccessProperty = existingPlaylist.GetType().GetProperty("OpenAccess");
                    bool isCurrentlyPublic = false;
                    if (openAccessProperty != null)
                    {
                        isCurrentlyPublic = (bool)(openAccessProperty.GetValue(existingPlaylist) ?? false);
                    }
                    else
                    {
                        // Fallback to share manipulation check when OpenAccess property is not available
                        isCurrentlyPublic = existingPlaylist.Shares.Any();
                    }
                    
                    var publicStatusChanged = isCurrentlyPublic != dto.Public;
                    if (publicStatusChanged)
                    {
                        logger.LogDebug("Playlist public status changing from {OldPublic} to {NewPublic}", isCurrentlyPublic, dto.Public);
                    }
                    
                    // Update the playlist if any changes are needed
                    if (nameChanged || ownershipChanged || publicStatusChanged)
                    {
                        await existingPlaylist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken);
                        logger.LogDebug("Updated existing playlist: {PlaylistName}", existingPlaylist.Name);
                    }
                    
                    // Update the playlist items
                    await UpdatePlaylistPublicStatusAsync(existingPlaylist, dto.Public, newLinkedChildren, cancellationToken);
                    
                    // Refresh metadata
                    await RefreshPlaylistMetadataAsync(existingPlaylist, cancellationToken);
                    
                    logger.LogDebug("Successfully updated existing playlist: {PlaylistName} with {ItemCount} items", 
                        existingPlaylist.Name, newLinkedChildren.Length);
                    
                    return (true, $"Updated playlist '{existingPlaylist.Name}' with {newLinkedChildren.Length} items", existingPlaylist.Id.ToString());
                }
                else
                {
                    // Create new playlist
                    logger.LogDebug("Creating new playlist: {PlaylistName}", smartPlaylistName);
                    
                    var newPlaylistId = await CreateNewPlaylistAsync(smartPlaylistName, user.Id, dto.Public, newLinkedChildren, cancellationToken);
                    
                    // Update the DTO with the new Jellyfin playlist ID
                    dto.JellyfinPlaylistId = newPlaylistId;
                    
                    // Save the DTO if a callback is provided
                    if (saveCallback != null)
                    {
                        try
                        {
                            await saveCallback(dto);
                            logger.LogDebug("Saved playlist DTO with new Jellyfin playlist ID {JellyfinPlaylistId} for playlist {PlaylistName}", 
                                newPlaylistId, dto.Name);
                        }
                        catch (Exception saveEx)
                        {
                            logger.LogWarning(saveEx, "Failed to save playlist DTO for {PlaylistName}, but continuing with operation", dto.Name);
                        }
                    }
                    
                    logger.LogDebug("Successfully created new playlist: {PlaylistName} with {ItemCount} items", 
                        smartPlaylistName, newLinkedChildren.Length);
                    
                    return (true, $"Created playlist '{smartPlaylistName}' with {newLinkedChildren.Length} items", newPlaylistId);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing playlist refresh for '{PlaylistName}': {ErrorMessage}", dto.Name, ex.Message);
                return (false, $"Error processing playlist '{dto.Name}': {ex.Message}", string.Empty);
            }
        }

        public async Task<(bool Success, string Message, string JellyfinPlaylistId)> RefreshSinglePlaylistAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default)
        {
            // This is the internal method that assumes the lock is already held
            var stopwatch = Stopwatch.StartNew();
            try
            {
                _logger.LogDebug("Refreshing single smart playlist: {PlaylistName}", dto.Name);
                _logger.LogDebug("PlaylistService.RefreshSinglePlaylistAsync called with: Name={Name}, UserId={UserId}, Public={Public}, Enabled={Enabled}, ExpressionSets={ExpressionSetCount}, MediaTypes={MediaTypes}", 
                    dto.Name, dto.UserId, dto.Public, dto.Enabled, dto.ExpressionSets?.Count ?? 0, 
                    dto.MediaTypes != null ? string.Join(",", dto.MediaTypes) : "None");

                // Get the user for this playlist
                var user = GetPlaylistUser(dto);
                if (user == null)
                {
                    _logger.LogWarning("No user found for playlist '{PlaylistName}'. Skipping.", dto.Name);
                    return (false, "No user found for playlist", string.Empty);
                }

                var allUserMedia = GetAllUserMedia(user, dto.MediaTypes).ToArray();
                
                var (success, message, jellyfinPlaylistId) = await ProcessPlaylistRefreshAsync(dto, user, allUserMedia, _logger, null, cancellationToken);
                
                stopwatch.Stop();
                _logger.LogDebug("Single playlist refresh completed in {ElapsedMs}ms: {Message}", stopwatch.ElapsedMilliseconds, message);
                
                return (success, message, jellyfinPlaylistId);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error in RefreshSinglePlaylistAsync for '{PlaylistName}' after {ElapsedMs}ms: {ErrorMessage}", 
                    dto.Name, stopwatch.ElapsedMilliseconds, ex.Message);
                return (false, $"Error refreshing playlist '{dto.Name}': {ex.Message}", string.Empty);
            }
        }

        public async Task<(bool Success, string Message, string JellyfinPlaylistId)> RefreshSinglePlaylistWithTimeoutAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default)
        {
            // Option A: Wait up to 5 seconds for create/edit operations
            _logger.LogDebug("Attempting to acquire refresh lock for single playlist: {PlaylistName} (5-second timeout)", dto.Name);
            
            try
            {
                if (await _refreshOperationLock.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
                {
                    try
                    {
                        _logger.LogDebug("Acquired refresh lock for single playlist: {PlaylistName}", dto.Name);
                        var (success, message, playlistId) = await RefreshSinglePlaylistAsync(dto, cancellationToken);
                        return (success, message, playlistId);
                    }
                    finally
                    {
                        _refreshOperationLock.Release();
                        _logger.LogDebug("Released refresh lock for single playlist: {PlaylistName}", dto.Name);
                    }
                }
                else
                {
                    _logger.LogDebug("Timeout waiting for refresh lock for single playlist: {PlaylistName}", dto.Name);
                    return (false, "Playlist refresh is already in progress. Your changes are saved, trying again in 5 seconds...", string.Empty);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Refresh operation cancelled for playlist: {PlaylistName}", dto.Name);
                return (false, "Refresh operation was cancelled.", string.Empty);
            }
        }

        public Task<(bool Success, string Message)> TryRefreshAllPlaylistsAsync(CancellationToken cancellationToken = default)
        {
            // Option B: Immediate return for manual refresh all
            _logger.LogDebug("Attempting to acquire refresh lock for all playlists (immediate return)");
            
            try
            {
                if (_refreshOperationLock.Wait(0, cancellationToken))
                {
                    try
                    {
                        _logger.LogDebug("Acquired refresh lock for all playlists - delegating to scheduled task");
                        // Don't actually do the refresh here - just trigger the scheduled task
                        // The scheduled task will handle the actual refresh with proper batching and optimization
                        return Task.FromResult((true, "Playlist refresh started successfully"));
                    }
                    finally
                    {
                        _refreshOperationLock.Release();
                        _logger.LogDebug("Released refresh lock for all playlists");
                    }
                }
                else
                {
                    _logger.LogDebug("Refresh lock already held - refresh already in progress");
                    return Task.FromResult((false, "Playlist refresh is already in progress. Please try again in a moment."));
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Refresh operation cancelled for all playlists");
                return Task.FromResult((false, "Refresh operation was cancelled."));
            }
        }

        /// <summary>
        /// Acquires the global refresh lock for use by the scheduled task.
        /// This should be called by the scheduled task to ensure exclusive access.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>An IDisposable that releases the lock when disposed</returns>
        public static async Task<IDisposable> AcquireRefreshLockAsync(CancellationToken cancellationToken = default)
        {
            await _refreshOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new RefreshLockDisposable();
        }

        /// <summary>
        /// Helper class to ensure the refresh lock is properly released.
        /// </summary>
        private class RefreshLockDisposable : IDisposable
        {
            public void Dispose()
            {
                _refreshOperationLock.Release();
            }
        }

        public Task DeletePlaylistAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default)
        {
            try
            {
                var user = GetPlaylistUser(dto);
                if (user == null)
                {
                    _logger.LogWarning("No user found for playlist '{PlaylistName}'. Cannot delete Jellyfin playlist.", dto.Name);
                    return Task.CompletedTask;
                }

                Playlist existingPlaylist = null;
                
                // Try to find by Jellyfin playlist ID only (no name fallback for deletion)
                if (!string.IsNullOrEmpty(dto.JellyfinPlaylistId) && Guid.TryParse(dto.JellyfinPlaylistId, out var jellyfinPlaylistId))
                {
                    if (_libraryManager.GetItemById(jellyfinPlaylistId) is Playlist playlistById)
                    {
                        existingPlaylist = playlistById;
                        _logger.LogDebug("Found playlist by Jellyfin playlist ID for deletion: {JellyfinPlaylistId} - {PlaylistName}",
                            dto.JellyfinPlaylistId, existingPlaylist.Name);
                    }
                    else
                    {
                        _logger.LogWarning("No Jellyfin playlist found by ID '{JellyfinPlaylistId}' for deletion. Playlist may have been manually deleted.", dto.JellyfinPlaylistId);
                    }
                }
                else
                {
                    _logger.LogWarning("No Jellyfin playlist ID available for playlist '{PlaylistName}'. Cannot delete Jellyfin playlist.", dto.Name);
                }
                
                if (existingPlaylist != null)
                {
                    _logger.LogInformation("Deleting Jellyfin playlist '{PlaylistName}' (ID: {PlaylistId}) for user '{UserName}'", 
                        existingPlaylist.Name, existingPlaylist.Id, user.Username);
                    _libraryManager.DeleteItem(existingPlaylist, new DeleteOptions { DeleteFileLocation = true }, true);
                }
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting smart playlist {PlaylistName}", dto.Name);
                throw;
            }
        }

        public async Task RemoveSmartSuffixAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default)
        {
            try
            {
                var user = GetPlaylistUser(dto);
                if (user == null)
                {
                    _logger.LogWarning("No user found for playlist '{PlaylistName}'. Cannot remove smart suffix.", dto.Name);
                    return;
                }

                Playlist existingPlaylist = null;
                
                // Try to find by Jellyfin playlist ID only (no name fallback for suffix removal)
                if (!string.IsNullOrEmpty(dto.JellyfinPlaylistId) && Guid.TryParse(dto.JellyfinPlaylistId, out var jellyfinPlaylistId))
                {
                    if (_libraryManager.GetItemById(jellyfinPlaylistId) is Playlist playlistById)
                    {
                        existingPlaylist = playlistById;
                        _logger.LogDebug("Found playlist by Jellyfin playlist ID for suffix removal: {JellyfinPlaylistId} - {PlaylistName}",
                            dto.JellyfinPlaylistId, existingPlaylist.Name);
                    }
                    else
                    {
                        _logger.LogWarning("No Jellyfin playlist found by ID '{JellyfinPlaylistId}' for suffix removal. Playlist may have been manually deleted.", dto.JellyfinPlaylistId);
                    }
                }
                else
                {
                    _logger.LogWarning("No Jellyfin playlist ID available for playlist '{PlaylistName}'.", dto.Name);
                }
                
                if (existingPlaylist != null)
                {
                    var oldName = existingPlaylist.Name;
                    _logger.LogInformation("Removing smart playlist '{PlaylistName}' (ID: {PlaylistId}) for user '{UserName}'", 
                        oldName, existingPlaylist.Id, user.Username);
                    
                    // Get the current smart playlist name format to see what needs to be removed
                    var currentSmartName = PlaylistNameFormatter.FormatPlaylistName(dto.Name);
                    
                    // Check if the playlist name matches the current smart format
                    if (oldName == currentSmartName)
                    {
                        // Remove the smart playlist naming and keep just the base name
                        existingPlaylist.Name = dto.Name;
                        
                        // Save the changes
                        await existingPlaylist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                        
                        _logger.LogDebug("Successfully renamed playlist from '{OldName}' to '{NewName}' for user '{UserName}'", 
                            oldName, dto.Name, user.Username);
                    }
                    else
                    {
                        // Try to remove prefix and suffix even if they don't match current settings
                        // This handles cases where the user changed their prefix/suffix settings
                        var config = Plugin.Instance?.Configuration;
                        if (config != null)
                        {
                            var prefix = config.PlaylistNamePrefix ?? "";
                            var suffix = config.PlaylistNameSuffix ?? "[Smart]";
                            
                            var baseName = dto.Name;
                            var expectedName = PlaylistNameFormatter.FormatPlaylistNameWithSettings(baseName, prefix, suffix);
                            
                            // If the playlist name matches this pattern, remove the prefix and suffix
                            if (oldName == expectedName)
                            {
                                existingPlaylist.Name = baseName;
                                
                                // Save the changes
                                await existingPlaylist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                                
                                _logger.LogDebug("Successfully renamed playlist from '{OldName}' to '{NewName}' for user '{UserName}' (removed prefix/suffix)", 
                                    oldName, baseName, user.Username);
                            }
                            else
                            {
                                _logger.LogWarning("Playlist name '{OldName}' doesn't match expected smart format '{ExpectedName}'. Skipping rename.", 
                                    oldName, expectedName);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Playlist name '{OldName}' doesn't match expected smart format '{ExpectedName}'. Skipping rename.", 
                                oldName, currentSmartName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing smart suffix from playlist {PlaylistName}", dto.Name);
                throw;
            }
        }

        public async Task EnablePlaylistAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Enabling smart playlist: {PlaylistName}", dto.Name);
                
                // Use timeout approach for enable operations since they involve creating/editing playlists
                var (success, message, _) = await RefreshSinglePlaylistWithTimeoutAsync(dto, cancellationToken);
                
                if (success)
                {
                    _logger.LogInformation("Successfully enabled smart playlist: {PlaylistName}", dto.Name);
                }
                else
                {
                    _logger.LogWarning("Failed to enable smart playlist {PlaylistName}: {Message}", dto.Name, message);
                    throw new InvalidOperationException(message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enabling smart playlist {PlaylistName}", dto.Name);
                throw;
            }
        }

        public async Task DisablePlaylistAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Disabling smart playlist: {PlaylistName}", dto.Name);
                
                // Use timeout approach for disable operations since they involve deleting playlists
                if (await _refreshOperationLock.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken))
                {
                    try
                    {
                        _logger.LogDebug("Acquired refresh lock for disabling playlist: {PlaylistName}", dto.Name);
                        await DeletePlaylistAsync(dto, cancellationToken);
                        _logger.LogInformation("Successfully disabled smart playlist: {PlaylistName}", dto.Name);
                    }
                    finally
                    {
                        _refreshOperationLock.Release();
                        _logger.LogDebug("Released refresh lock for disabling playlist: {PlaylistName}", dto.Name);
                    }
                }
                else
                {
                    _logger.LogWarning("Timeout waiting for refresh lock to disable playlist: {PlaylistName}", dto.Name);
                    throw new InvalidOperationException("Playlist refresh is already in progress. Please try again in a moment.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling smart playlist {PlaylistName}", dto.Name);
                throw;
            }
        }

        private async Task UpdatePlaylistPublicStatusAsync(Playlist playlist, bool isPublic, LinkedChild[] linkedChildren, CancellationToken cancellationToken)
        {
                            _logger.LogDebug("Updating playlist {PlaylistName} public status to {PublicStatus} and items to {ItemCount}",
                    playlist.Name, isPublic ? "public" : "private", linkedChildren.Length);
            
            // Update the playlist items
            playlist.LinkedChildren = linkedChildren;
            
            // Note: Jellyfin defaults playlist MediaType to "Audio" regardless of content - this is a known Jellyfin limitation
            
            // Update the public status by setting the OpenAccess property
            var openAccessProperty = playlist.GetType().GetProperty("OpenAccess");
            if (openAccessProperty != null && openAccessProperty.CanWrite)
            {
                _logger.LogDebug("Setting playlist {PlaylistName} OpenAccess property to {IsPublic}", playlist.Name, isPublic);
                openAccessProperty.SetValue(playlist, isPublic);
            }
            else
            {
                // Fallback to share manipulation if OpenAccess property is not available
                _logger.LogWarning("OpenAccess property not found or not writable, falling back to share manipulation");
                if (isPublic && !playlist.Shares.Any())
                {
                    _logger.LogDebug("Making playlist {PlaylistName} public by adding share", playlist.Name);
                    var ownerId = playlist.OwnerUserId;
                    var newShare = new MediaBrowser.Model.Entities.PlaylistUserPermissions(ownerId, false);
                    
                    playlist.Shares = [.. playlist.Shares, newShare];
                }
                else if (!isPublic && playlist.Shares.Any())
                {
                    _logger.LogDebug("Making playlist {PlaylistName} private by clearing shares", playlist.Name);
                    playlist.Shares = [];
                }
            }
            
            // Save the changes
            await playlist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            
            // Log the final state using OpenAccess property
            var finalOpenAccessProperty = playlist.GetType().GetProperty("OpenAccess");
            bool isFinallyPublic = finalOpenAccessProperty != null ? (bool)(finalOpenAccessProperty.GetValue(playlist) ?? false) : playlist.Shares.Any();
            _logger.LogDebug("Playlist {PlaylistName} updated: OpenAccess = {OpenAccess}, Shares count = {SharesCount}", 
                playlist.Name, isFinallyPublic, playlist.Shares?.Count ?? 0);
            
            // Refresh metadata to generate cover images
            await RefreshPlaylistMetadataAsync(playlist, cancellationToken).ConfigureAwait(false);
        }

        private async Task<string> CreateNewPlaylistAsync(string playlistName, Guid userId, bool isPublic, LinkedChild[] linkedChildren, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Creating new smart playlist {PlaylistName} with {ItemCount} items and {PublicStatus} status", 
                playlistName, linkedChildren.Length, isPublic ? "public" : "private");
            
            var result = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
            {
                Name = playlistName,
                UserId = userId,
                Public = isPublic
            }).ConfigureAwait(false);

            _logger.LogDebug("Playlist creation result: ID = {PlaylistId}", result.Id);

            if (_libraryManager.GetItemById(result.Id) is Playlist newPlaylist)
            {
                _logger.LogDebug("Retrieved new playlist: Name = {Name}, Shares count = {SharesCount}, Public = {Public}", 
                    newPlaylist.Name, newPlaylist.Shares?.Count ?? 0, newPlaylist.Shares.Any());
                
                newPlaylist.LinkedChildren = linkedChildren;
                
                // Note: Jellyfin defaults playlist MediaType to "Audio" regardless of content - this is a known Jellyfin limitation
                
                await newPlaylist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                
                // Log the final state after update
                _logger.LogDebug("After update - Playlist {PlaylistName}: Shares count = {SharesCount}, Public = {Public}", 
                    newPlaylist.Name, newPlaylist.Shares?.Count ?? 0, newPlaylist.Shares.Any());
                
                // Refresh metadata to generate cover images
                await RefreshPlaylistMetadataAsync(newPlaylist, cancellationToken).ConfigureAwait(false);
                
                return result.Id.ToString();
            }
            else
            {
                _logger.LogWarning("Failed to retrieve newly created playlist with ID {PlaylistId}", result.Id);
                return string.Empty;
            }
        }

        private Playlist GetPlaylistByName(User user, string name)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Playlist],
                Recursive = true,
                Name = name
            };
            
            return _libraryManager.GetItemsResult(query).Items.OfType<Playlist>().FirstOrDefault();
        }

        private User GetPlaylistUser(SmartPlaylistDto playlist)
        {
            // If new UserId field is set and not empty, use it
            if (playlist.UserId != Guid.Empty)
            {
                return _userManager.GetUserById(playlist.UserId);
            }

            // Legacy migration: if old User field is set, try to find the user
#pragma warning disable CS0618 // Type or member is obsolete
            if (!string.IsNullOrEmpty(playlist.User))
            {
                var user = _userManager.GetUserByName(playlist.User);
                if (user != null)
                {
                    _logger.LogDebug("Found legacy user '{UserName}' for playlist '{PlaylistName}'", playlist.User, playlist.Name);
                    return user;
                }
                else
                {
                    _logger.LogWarning("Legacy playlist '{PlaylistName}' references non-existent user '{UserName}'", playlist.Name, playlist.User);
                }
            }
#pragma warning restore CS0618 // Type or member is obsolete

            return null;
        }
        private IEnumerable<BaseItem> GetAllUserMedia(User user, List<string> mediaTypes = null)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = GetBaseItemKindsFromMediaTypes(mediaTypes),
                Recursive = true
            };

            return _libraryManager.GetItemsResult(query).Items;
        }

        /// <summary>
        /// Maps string media types to BaseItemKind enums for API-level filtering
        /// </summary>
        private BaseItemKind[] GetBaseItemKindsFromMediaTypes(List<string> mediaTypes)
        {
            // If no media types specified, return all supported types (backward compatibility)
            if (mediaTypes == null || mediaTypes.Count == 0)
            {
                return [BaseItemKind.Movie, BaseItemKind.Audio, BaseItemKind.Episode, BaseItemKind.Series];
            }

            var baseItemKinds = new List<BaseItemKind>();
            
            foreach (var mediaType in mediaTypes)
            {
                switch (mediaType)
                {
                    case "Movie":
                        baseItemKinds.Add(BaseItemKind.Movie);
                        break;
                    case "Audio":
                        baseItemKinds.Add(BaseItemKind.Audio);
                        break;
                    case "Episode":
                        baseItemKinds.Add(BaseItemKind.Episode);
                        break;
                    case "Series":
                        baseItemKinds.Add(BaseItemKind.Series);
                        break;
                    default:
                        _logger?.LogWarning("Unknown media type '{MediaType}' - skipping", mediaType);
                        break;
                }
            }

            // Fallback to all types if no valid media types were found
            if (baseItemKinds.Count == 0)
            {
                _logger?.LogWarning("No valid media types found, falling back to all supported types");
                return [BaseItemKind.Movie, BaseItemKind.Audio, BaseItemKind.Episode, BaseItemKind.Series];
            }

            return [.. baseItemKinds];
        }

        private async Task RefreshPlaylistMetadataAsync(Playlist playlist, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var directoryService = new BasicDirectoryService();
                
                // Check if playlist is empty
                if (playlist.LinkedChildren == null || playlist.LinkedChildren.Length == 0)
                {
                    _logger.LogDebug("Playlist {PlaylistName} is empty - clearing any existing cover images", playlist.Name);
                    
                    // Force metadata refresh to clear existing cover images for empty playlists
                    var clearOptions = new MetadataRefreshOptions(directoryService)
                    {
                        MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                        ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                        ReplaceAllImages = true,  // Clear all existing images
                        ReplaceAllMetadata = true  // Clear all metadata to ensure clean state
                    };
                    
                    await _providerManager.RefreshSingleItem(playlist, clearOptions, cancellationToken).ConfigureAwait(false);
                    
                    stopwatch.Stop();
                    _logger.LogDebug("Cover image clearing completed for empty playlist {PlaylistName} in {ElapsedTime}ms", playlist.Name, stopwatch.ElapsedMilliseconds);
                    return;
                }
                
                _logger.LogDebug("Triggering metadata refresh for playlist {PlaylistName} to generate cover image", playlist.Name);
                
                var refreshOptions = new MetadataRefreshOptions(directoryService)
                {
                    MetadataRefreshMode = MetadataRefreshMode.Default,
                    ImageRefreshMode = MetadataRefreshMode.Default,
                    ReplaceAllMetadata = true, // Force regeneration of playlist metadata 
                    ReplaceAllImages = true   // Force regeneration of playlist cover images
                };
                
                await _providerManager.RefreshSingleItem(playlist, refreshOptions, cancellationToken).ConfigureAwait(false);
                
                stopwatch.Stop();
                _logger.LogDebug("Cover image generation completed for playlist {PlaylistName} in {ElapsedTime}ms", playlist.Name, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogWarning(ex, "Failed to refresh metadata for playlist {PlaylistName} after {ElapsedTime}ms. Cover image may not be generated.", playlist.Name, stopwatch.ElapsedMilliseconds);
            }
        }


    }

    /// <summary>
    /// Basic DirectoryService implementation for playlist metadata refresh.
    /// </summary>
    public class BasicDirectoryService : IDirectoryService
    {
        public List<FileSystemMetadata> GetDirectories(string path) => [];
        public List<FileSystemMetadata> GetFiles(string path) => [];
        public FileSystemMetadata[] GetFileSystemEntries(string path) => [];
        public FileSystemMetadata GetFile(string path) => null;
        public FileSystemMetadata GetDirectory(string path) => null;
        public FileSystemMetadata GetFileSystemEntry(string path) => null;
        public IReadOnlyList<string> GetFilePaths(string path) => [];
        public IReadOnlyList<string> GetFilePaths(string path, bool clearCache, bool sort) => [];
        public bool IsAccessible(string path) => false;
    }
} 