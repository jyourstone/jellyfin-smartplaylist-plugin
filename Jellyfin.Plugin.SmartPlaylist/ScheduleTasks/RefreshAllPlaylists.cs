using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Playlists;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist.ScheduleTasks
{
    /// <summary>
    /// Basic DirectoryService implementation for playlist metadata refresh.
    /// Provides empty/safe implementations to avoid NullReferenceExceptions.
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

    /// <summary>
    /// Class RefreshAllPlaylists.
    /// </summary>
    public class RefreshAllPlaylists(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IPlaylistManager playlistManager,
        IUserDataManager userDataManager,
        ILogger<RefreshAllPlaylists> logger,
        IServerApplicationPaths serverApplicationPaths,
        IProviderManager providerManager) : IScheduledTask
    {

        /// <summary>
        /// Gets the name of the task.
        /// </summary>
        /// <value>The name.</value>
        public string Name => "Refresh all SmartPlaylists";

        /// <summary>
        /// Gets the description.
        /// </summary>
        /// <value>The description.</value>
        public string Description => "Refresh all SmartPlaylists";

        /// <summary>
        /// Gets the category.
        /// </summary>
        /// <value>The category.</value>
        public string Category => "Library";

        public string Key => "RefreshSmartPlaylists";

        /// <summary>
        /// Executes the task.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="progress">The progress.</param>
        /// <returns>Task.</returns>
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                logger.LogInformation("Starting SmartPlaylist refresh task");

                // Create playlist store
                var fileSystem = new SmartPlaylistFileSystem(serverApplicationPaths);
                var plStore = new SmartPlaylistStore(fileSystem, userManager);

                var dtos = await plStore.GetAllSmartPlaylistsAsync().ConfigureAwait(false);
                logger.LogInformation("Found {Count} smart playlists to process", dtos.Length);
                
                var allUsers = userManager.Users;

                for (int i = 0; i < dtos.Length; i++)
                {
                    var playlistStopwatch = Stopwatch.StartNew();
                    var dto = dtos[i];
                    cancellationToken.ThrowIfCancellationRequested();

                    progress?.Report((double)i / dtos.Length * 100);

                    // Handle migration from old User field to new UserId field
                    var user = await GetPlaylistUserAsync(dto);
                    if (user == null)
                    {
                        logger.LogWarning("No user found for playlist '{PlaylistName}'. Skipping.", dto.Name);
                        continue;
                    }
                    
                    var smartPlaylist = new SmartPlaylist(dto);
                    
                    // Log the playlist processing
                    logger.LogInformation("Processing playlist {PlaylistName} with {RuleSetCount} rule sets", dto.Name, dto.ExpressionSets.Count);
                    
                    var allUserMedia = GetAllUserMedia(user).ToArray();
                    logger.LogDebug("Found {MediaCount} total media items for user {User}", allUserMedia.Length, user.Username);
                    
                    var newItems = smartPlaylist.FilterPlaylistItems(allUserMedia, libraryManager, user, userDataManager, logger).ToArray();
                    logger.LogInformation("Playlist {PlaylistName} filtered to {FilteredCount} items from {TotalCount} total items", 
                        dto.Name, newItems.Length, allUserMedia.Length);
                    
                    var newLinkedChildren = newItems.Select(itemId => 
                    {
                        var item = libraryManager.GetItemById(itemId);
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
                        
                        logger.LogDebug("Playlist {PlaylistName} status check: currently public = {CurrentlyPublic} (OpenAccess), should be public = {ShouldBePublic}, shares count = {SharesCount}", 
                            smartPlaylistName, isCurrentlyPublic, shouldBePublic, existingPlaylist.Shares?.Count ?? 0);
                        
                        if (isCurrentlyPublic != shouldBePublic)
                        {
                            logger.LogInformation("Public status changed for playlist {PlaylistName}. Updating playlist directly (was {OldStatus}, now {NewStatus})", 
                                smartPlaylistName, isCurrentlyPublic ? "public" : "private", shouldBePublic ? "public" : "private");
                            
                            // Update the existing playlist directly using Jellyfin's playlist update API
                            await UpdatePlaylistPublicStatusAsync(existingPlaylist, dto.Public, newLinkedChildren, cancellationToken);
                        }
                        else
                        {
                            // Public status hasn't changed, just update the items
                            logger.LogInformation("Updating smart playlist {PlaylistName} for user {User} with {ItemCount} items (status remains {PublicStatus})", 
                                smartPlaylistName, user.Username, newLinkedChildren.Length, shouldBePublic ? "public" : "private");
                            
                            existingPlaylist.LinkedChildren = newLinkedChildren;
                            await existingPlaylist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                            
                            logger.LogDebug("After item update - Playlist {PlaylistName}: Shares count = {SharesCount}, Public = {Public}", 
                                existingPlaylist.Name, existingPlaylist.Shares?.Count ?? 0, existingPlaylist.Shares.Any());
                            
                            // Refresh metadata to generate cover images
                            await RefreshPlaylistMetadataAsync(existingPlaylist, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        logger.LogInformation("Creating new smart playlist {PlaylistName} for user {User} with {ItemCount} items and {PublicStatus} status", 
                            smartPlaylistName, user.Username, newLinkedChildren.Length, dto.Public ? "public" : "private");
                        
                        var result = await playlistManager.CreatePlaylist(new PlaylistCreationRequest
                        {
                            Name = smartPlaylistName,
                            UserId = user.Id,
                            Public = dto.Public
                        }).ConfigureAwait(false);

                        if (libraryManager.GetItemById(result.Id) is Playlist newPlaylist)
                        {
                            logger.LogDebug("New playlist created: Name = {Name}, Shares count = {SharesCount}, Public = {Public}", 
                                newPlaylist.Name, newPlaylist.Shares?.Count ?? 0, newPlaylist.Shares.Any());
                            
                            newPlaylist.LinkedChildren = newLinkedChildren;
                            await newPlaylist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                            
                            logger.LogDebug("After update - Playlist {PlaylistName}: Shares count = {SharesCount}, Public = {Public}", 
                                newPlaylist.Name, newPlaylist.Shares?.Count ?? 0, newPlaylist.Shares.Any());
                            
                            // Refresh metadata to generate cover images
                            await RefreshPlaylistMetadataAsync(newPlaylist, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    
                    playlistStopwatch.Stop();
                    logger.LogInformation("Playlist {PlaylistName} processed in {ElapsedTime}ms", dto.Name, playlistStopwatch.ElapsedMilliseconds);
                }

                progress?.Report(100);
                stopwatch.Stop();
                logger.LogInformation("SmartPlaylist refresh task completed successfully in {TotalTime}ms", stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger.LogError(ex, "Error occurred during SmartPlaylist refresh task after {ElapsedTime}ms", stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// Triggers metadata processing for playlist to generate cover images.
        /// This simulates the manual "update metadata" action that generates cover images.
        /// </summary>
        /// <param name="playlist">The playlist to refresh.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        private async Task RefreshPlaylistMetadataAsync(Playlist playlist, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                logger.LogInformation("Triggering metadata refresh for playlist {PlaylistName} to generate cover image", playlist.Name);
                
                // Only generate cover images for playlists that have content
                // This avoids NullReferenceExceptions for empty playlists
                if (playlist.LinkedChildren == null || playlist.LinkedChildren.Length == 0)
                {
                    logger.LogDebug("Skipping cover image generation for empty playlist {PlaylistName} - no content to generate image from", playlist.Name);
                    return;
                }
                
                // Use the provider manager to trigger cover image generation
                // Use a basic DirectoryService implementation to avoid NullReferenceExceptions
                var directoryService = new BasicDirectoryService();
                var refreshOptions = new MetadataRefreshOptions(directoryService)
                {
                    MetadataRefreshMode = MetadataRefreshMode.Default,
                    ImageRefreshMode = MetadataRefreshMode.Default,
                    ReplaceAllMetadata = false,
                    ReplaceAllImages = false
                };
                
                await providerManager.RefreshSingleItem(playlist, refreshOptions, cancellationToken).ConfigureAwait(false);
                
                stopwatch.Stop();
                logger.LogDebug("Cover image generation completed for playlist {PlaylistName} in {ElapsedTime}ms", playlist.Name, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                logger.LogWarning(ex, "Failed to refresh metadata for playlist {PlaylistName} after {ElapsedTime}ms. Cover image may not be generated.", playlist.Name, stopwatch.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Gets the default triggers.
        /// </summary>
        /// <returns>IEnumerable{TaskTriggerInfo}.</returns>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return
            [
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfoType.IntervalTrigger,
                    IntervalTicks = TimeSpan.FromMinutes(30).Ticks
                }
            ];
        }

        private async Task UpdatePlaylistPublicStatusAsync(Playlist playlist, bool isPublic, LinkedChild[] linkedChildren, CancellationToken cancellationToken)
        {
            logger.LogInformation("Updating playlist {PlaylistName} public status to {PublicStatus} and items to {ItemCount}", 
                playlist.Name, isPublic ? "public" : "private", linkedChildren.Length);
            
            // Update the playlist items
            playlist.LinkedChildren = linkedChildren;
            
            // Update the public status by setting the OpenAccess property
            var openAccessProperty = playlist.GetType().GetProperty("OpenAccess");
            if (openAccessProperty != null && openAccessProperty.CanWrite)
            {
                logger.LogDebug("Setting playlist {PlaylistName} OpenAccess property to {IsPublic}", playlist.Name, isPublic);
                openAccessProperty.SetValue(playlist, isPublic);
            }
            else
            {
                // Fallback to share manipulation if OpenAccess property is not available
                logger.LogWarning("OpenAccess property not found or not writable, falling back to share manipulation");
                if (isPublic && !playlist.Shares.Any())
                {
                    logger.LogDebug("Making playlist {PlaylistName} public by adding share", playlist.Name);
                    var ownerId = playlist.OwnerUserId;
                    var newShare = new MediaBrowser.Model.Entities.PlaylistUserPermissions(ownerId, false);
                    
                    var sharesList = playlist.Shares.ToList();
                    sharesList.Add(newShare);
                    playlist.Shares = [.. sharesList];
                }
                else if (!isPublic && playlist.Shares.Any())
                {
                    logger.LogDebug("Making playlist {PlaylistName} private by clearing shares", playlist.Name);
                    playlist.Shares = [];
                }
            }
            
            // Save the changes
            await playlist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
            
            // Log the final state using OpenAccess property
            var finalOpenAccessProperty = playlist.GetType().GetProperty("OpenAccess");
            bool isFinallyPublic = finalOpenAccessProperty != null ? (bool)(finalOpenAccessProperty.GetValue(playlist) ?? false) : playlist.Shares.Any();
            logger.LogInformation("Playlist {PlaylistName} updated: OpenAccess = {OpenAccess}, Shares count = {SharesCount}", 
                playlist.Name, isFinallyPublic, playlist.Shares?.Count ?? 0);
            
            // Refresh metadata to generate cover images
            await RefreshPlaylistMetadataAsync(playlist, cancellationToken).ConfigureAwait(false);
        }

        private Playlist GetPlaylist(User user, string name)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Playlist],
                Recursive = true,
                Name = name
            };
            
            return libraryManager.GetItemsResult(query).Items.OfType<Playlist>().FirstOrDefault();
        }

        private IEnumerable<BaseItem> GetAllUserMedia(User user)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Audio, BaseItemKind.Episode],
                Recursive = true
            };

            return libraryManager.GetItemsResult(query).Items;
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
                    logger.LogInformation("Migrating playlist '{PlaylistName}' from username '{UserName}' to User ID '{UserId}'", 
                        playlist.Name, playlist.User, user.Id);
                    
                    // Update the playlist with the User ID and save it
                    playlist.UserId = user.Id;
                    playlist.User = null; // Clear the old field
                    
                    try
                    {
                        var fileSystem = new SmartPlaylistFileSystem(serverApplicationPaths);
                        var playlistStore = new SmartPlaylistStore(fileSystem, userManager);
                        await playlistStore.SaveAsync(playlist);
                        logger.LogInformation("Successfully migrated playlist '{PlaylistName}' to use User ID", playlist.Name);
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