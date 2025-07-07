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
        Task RefreshSinglePlaylistAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default);
        Task DeletePlaylistAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default);
        Task RemoveSmartSuffixAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default);
        Task EnablePlaylistAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default);
        Task DisablePlaylistAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default);
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

        public async Task RefreshSinglePlaylistAsync(SmartPlaylistDto dto, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                _logger.LogDebug("Refreshing single smart playlist: {PlaylistName}", dto.Name);
                _logger.LogDebug("PlaylistService.RefreshSinglePlaylistAsync called with: Name={Name}, UserId={UserId}, Public={Public}, Enabled={Enabled}, ExpressionSets={ExpressionSetCount}, MediaTypes={MediaTypes}", 
                    dto.Name, dto.UserId, dto.Public, dto.Enabled, dto.ExpressionSets?.Count ?? 0, 
                    dto.MediaTypes != null ? string.Join(",", dto.MediaTypes) : "None");

                // Check if playlist is enabled
                if (!dto.Enabled)
                {
                    _logger.LogDebug("Smart playlist '{PlaylistName}' is disabled. Skipping refresh.", dto.Name);
                    return;
                }

                // Get the user for this playlist
                var user = GetPlaylistUser(dto);
                if (user == null)
                {
                    _logger.LogWarning("No user found for playlist '{PlaylistName}'. Skipping.", dto.Name);
                    return;
                }

                var smartPlaylist = new SmartPlaylist(dto);
                
                // Log the playlist rules
                _logger.LogDebug("Processing playlist {PlaylistName} with {RuleSetCount} rule sets", dto.Name, dto.ExpressionSets.Count);

                var allUserMedia = GetAllUserMedia(user).ToArray();
                _logger.LogDebug("Found {MediaCount} total media items for user {User}", allUserMedia.Length, user.Username);
                
                var newItems = smartPlaylist.FilterPlaylistItems(allUserMedia, _libraryManager, user, _userDataManager, _logger).ToArray();
                _logger.LogDebug("Playlist {PlaylistName} filtered to {FilteredCount} items from {TotalCount} total items", 
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

                // Add [Smart] suffix to distinguish from regular playlists
                var smartPlaylistName = dto.Name + " [Smart]";
                var existingPlaylist = GetPlaylist(user, smartPlaylistName);
                
                if (existingPlaylist != null)
                {
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
                        // Fallback to shares if OpenAccess property is not available
                        isCurrentlyPublic = existingPlaylist.Shares.Any();
                    }
                    bool shouldBePublic = dto.Public;
                    
                    _logger.LogDebug("Playlist {PlaylistName} status check: currently public = {CurrentlyPublic} (OpenAccess), should be public = {ShouldBePublic}, shares count = {SharesCount}", 
                        smartPlaylistName, isCurrentlyPublic, shouldBePublic, existingPlaylist.Shares?.Count ?? 0);
                    
                    if (isCurrentlyPublic != shouldBePublic)
                    {
                        _logger.LogDebug("Public status changed for playlist {PlaylistName}. Updating public status (was {OldStatus}, now {NewStatus})", 
                            smartPlaylistName, isCurrentlyPublic ? "public" : "private", shouldBePublic ? "public" : "private");
                        
                        // Update the playlist's public status directly
                        await UpdatePlaylistPublicStatusAsync(existingPlaylist, dto.Public, newLinkedChildren, cancellationToken);
                    }
                    else
                    {
                        // Public status hasn't changed, just update the items
                        _logger.LogDebug("Updating smart playlist {PlaylistName} for user {User} with {ItemCount} items", smartPlaylistName, user.Username, newLinkedChildren.Length);
                        existingPlaylist.LinkedChildren = newLinkedChildren;
                        await existingPlaylist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                        
                        // Refresh metadata to generate cover images
                        await RefreshPlaylistMetadataAsync(existingPlaylist, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    // Create new playlist
                    await CreateNewPlaylistAsync(smartPlaylistName, user.Id, dto.Public, newLinkedChildren, cancellationToken);
                }

                stopwatch.Stop();
                _logger.LogInformation("Successfully refreshed smart playlist: {PlaylistName} in {ElapsedTime}ms", dto.Name, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "Error refreshing smart playlist {PlaylistName} after {ElapsedTime}ms", dto.Name, stopwatch.ElapsedMilliseconds);
                throw;
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

                var smartPlaylistName = dto.Name + " [Smart]";
                var existingPlaylist = GetPlaylist(user, smartPlaylistName);
                
                if (existingPlaylist != null)
                {
                    _logger.LogDebug("Deleting Jellyfin playlist '{PlaylistName}' for user '{UserName}'", smartPlaylistName, user.Username);
                    _libraryManager.DeleteItem(existingPlaylist, new DeleteOptions { DeleteFileLocation = true }, true);
                }
                else
                {
                    _logger.LogWarning("No Jellyfin playlist found with name '{PlaylistName}' for user '{UserName}'", smartPlaylistName, user.Username);
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

                var smartPlaylistName = dto.Name + " [Smart]";
                var existingPlaylist = GetPlaylist(user, smartPlaylistName);
                
                if (existingPlaylist != null)
                {
                    _logger.LogDebug("Removing '[Smart]' suffix from playlist '{PlaylistName}' for user '{UserName}'", smartPlaylistName, user.Username);
                    
                    // Rename the playlist by removing the [Smart] suffix
                    existingPlaylist.Name = dto.Name;
                    
                    // Save the changes
                    await existingPlaylist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                    
                    _logger.LogDebug("Successfully renamed playlist from '{OldName}' to '{NewName}' for user '{UserName}'", 
                        smartPlaylistName, dto.Name, user.Username);
                }
                else
                {
                    _logger.LogWarning("No Jellyfin playlist found with name '{PlaylistName}' for user '{UserName}'", smartPlaylistName, user.Username);
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
                
                // Refresh the playlist to create/update the Jellyfin playlist
                await RefreshSinglePlaylistAsync(dto, cancellationToken);
                
                _logger.LogInformation("Successfully enabled smart playlist: {PlaylistName}", dto.Name);
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
                
                // Delete the Jellyfin playlist
                await DeletePlaylistAsync(dto, cancellationToken);
                
                _logger.LogInformation("Successfully disabled smart playlist: {PlaylistName}", dto.Name);
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

        private async Task CreateNewPlaylistAsync(string playlistName, Guid userId, bool isPublic, LinkedChild[] linkedChildren, CancellationToken cancellationToken)
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
                await newPlaylist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                
                // Log the final state after update
                _logger.LogDebug("After update - Playlist {PlaylistName}: Shares count = {SharesCount}, Public = {Public}", 
                    newPlaylist.Name, newPlaylist.Shares?.Count ?? 0, newPlaylist.Shares.Any());
                
                // Refresh metadata to generate cover images
                await RefreshPlaylistMetadataAsync(newPlaylist, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("Failed to retrieve newly created playlist with ID {PlaylistId}", result.Id);
            }
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

        private Playlist GetPlaylist(User user, string name)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Playlist],
                Recursive = true,
                Name = name
            };
            
            return _libraryManager.GetItemsResult(query).Items.OfType<Playlist>().FirstOrDefault();
        }

        private IEnumerable<BaseItem> GetAllUserMedia(User user)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Audio, BaseItemKind.Episode],
                Recursive = true
            };

            return _libraryManager.GetItemsResult(query).Items;
        }

        private async Task RefreshPlaylistMetadataAsync(Playlist playlist, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                _logger.LogDebug("Triggering metadata refresh for playlist {PlaylistName} to generate cover image", playlist.Name);
                
                // Only generate cover images for playlists that have content
                if (playlist.LinkedChildren == null || playlist.LinkedChildren.Length == 0)
                {
                    _logger.LogDebug("Skipping cover image generation for empty playlist {PlaylistName} - no content to generate image from", playlist.Name);
                    return;
                }
                
                var directoryService = new BasicDirectoryService();
                var refreshOptions = new MetadataRefreshOptions(directoryService)
                {
                    MetadataRefreshMode = MetadataRefreshMode.Default,
                    ImageRefreshMode = MetadataRefreshMode.Default,
                    ReplaceAllMetadata = true  // Force regeneration of playlist metadata and cover images
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