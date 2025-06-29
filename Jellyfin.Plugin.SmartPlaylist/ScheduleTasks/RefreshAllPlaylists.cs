using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Providers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Playlists;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SmartPlaylist.ScheduleTasks
{
    /// <summary>
    /// Basic DirectoryService implementation for playlist metadata refresh.
    /// Provides empty/safe implementations to avoid NullReferenceExceptions.
    /// </summary>
    public class BasicDirectoryService : IDirectoryService
    {
        public List<FileSystemMetadata> GetDirectories(string path) => new List<FileSystemMetadata>();
        public List<FileSystemMetadata> GetFiles(string path) => new List<FileSystemMetadata>();
        public FileSystemMetadata[] GetFileSystemEntries(string path) => Array.Empty<FileSystemMetadata>();
        public FileSystemMetadata GetFile(string path) => null;
        public FileSystemMetadata GetDirectory(string path) => null;
        public FileSystemMetadata GetFileSystemEntry(string path) => null;
        public IReadOnlyList<string> GetFilePaths(string path) => Array.Empty<string>();
        public IReadOnlyList<string> GetFilePaths(string path, bool clearCache, bool sort) => Array.Empty<string>();
        public bool IsAccessible(string path) => false;
    }

    /// <summary>
    /// Class RefreshAllPlaylists.
    /// </summary>
    public class RefreshAllPlaylists : IScheduledTask
    {
        private readonly IUserManager _userManager;
        private readonly ILibraryManager _libraryManager;
        private readonly IPlaylistManager _playlistManager;
        private readonly IUserDataManager _userDataManager;
        private readonly ILogger<RefreshAllPlaylists> _logger;
        private readonly IServerApplicationPaths _serverApplicationPaths;
        private readonly IProviderManager _providerManager;

        public RefreshAllPlaylists(
            IUserManager userManager,
            ILibraryManager libraryManager,
            IPlaylistManager playlistManager,
            IUserDataManager userDataManager,
            ILogger<RefreshAllPlaylists> logger,
            IServerApplicationPaths serverApplicationPaths,
            IProviderManager providerManager)
        {
            _userManager = userManager;
            _libraryManager = libraryManager;
            _playlistManager = playlistManager;
            _userDataManager = userDataManager;
            _logger = logger;
            _serverApplicationPaths = serverApplicationPaths;
            _providerManager = providerManager;
        }

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
            try
            {
                _logger.LogInformation("Starting SmartPlaylist refresh task");

                // Create playlist store
                var fileSystem = new SmartPlaylistFileSystem(_serverApplicationPaths);
                var plStore = new SmartPlaylistStore(fileSystem, _userManager);

                var dtos = await plStore.GetAllSmartPlaylistsAsync().ConfigureAwait(false);
                _logger.LogInformation("Found {Count} smart playlists to process", dtos.Length);
                
                var allUsers = _userManager.Users;

                for (int i = 0; i < dtos.Length; i++)
                {
                    var dto = dtos[i];
                    cancellationToken.ThrowIfCancellationRequested();

                    progress?.Report((double)i / dtos.Length * 100);

                    var user = _userManager.GetUserByName(dto.User);
                    if (user == null)
                    {
                        _logger.LogWarning("No user found with name '{User}' for playlist '{PlaylistName}'. Skipping.", dto.User, dto.Name);
                        continue;
                    }
                    
                    var smartPlaylist = new SmartPlaylist(dto);
                    
                    // Log the playlist rules
                    _logger.LogInformation("Processing playlist {PlaylistName} with {RuleSetCount} rule sets", dto.Name, dto.ExpressionSets.Count);

                    foreach (var set in dto.ExpressionSets)
                    {
                        foreach (var rule in set.Expressions)
                        {
                            _logger.LogInformation("Rule: {MemberName} {Operator} '{TargetValue}'", rule.MemberName, rule.Operator, rule.TargetValue);
                        }
                    }
                    
                    var allUserMedia = GetAllUserMedia(user).ToArray();
                    _logger.LogDebug("Found {MediaCount} total media items for user {User}", allUserMedia.Length, user.Username);
                    
                    var newItems = smartPlaylist.FilterPlaylistItems(allUserMedia, _libraryManager, user, _userDataManager, _logger).ToArray();
                    _logger.LogInformation("Playlist {PlaylistName} filtered to {FilteredCount} items from {TotalCount} total items", 
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
                        _logger.LogInformation("Updating smart playlist {PlaylistName} for user {User} with {ItemCount} items", smartPlaylistName, user.Username, newLinkedChildren.Length);
                        existingPlaylist.LinkedChildren = newLinkedChildren;
                        await existingPlaylist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                        
                        // Refresh metadata to generate cover images
                        await RefreshPlaylistMetadataAsync(existingPlaylist, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogInformation("Creating new smart playlist {PlaylistName} for user {User} with {ItemCount} items", smartPlaylistName, user.Username, newLinkedChildren.Length);
                        
                        var result = await _playlistManager.CreatePlaylist(new PlaylistCreationRequest
                        {
                            Name = smartPlaylistName,
                            UserId = user.Id,
                            Public = dto.Public
                        }).ConfigureAwait(false);

                        if (_libraryManager.GetItemById(result.Id) is Playlist newPlaylist)
                        {
                            newPlaylist.LinkedChildren = newLinkedChildren;
                            await newPlaylist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                            
                            // Refresh metadata to generate cover images
                            await RefreshPlaylistMetadataAsync(newPlaylist, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                progress?.Report(100);
                _logger.LogInformation("SmartPlaylist refresh task completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during SmartPlaylist refresh task");
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
            try
            {
                _logger.LogInformation("Triggering metadata refresh for playlist {PlaylistName} to generate cover image", playlist.Name);
                
                // Only generate cover images for playlists that have content
                // This avoids NullReferenceExceptions for empty playlists
                if (playlist.LinkedChildren == null || playlist.LinkedChildren.Length == 0)
                {
                    _logger.LogDebug("Skipping cover image generation for empty playlist {PlaylistName} - no content to generate image from", playlist.Name);
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
                
                await _providerManager.RefreshSingleItem(playlist, refreshOptions, cancellationToken).ConfigureAwait(false);
                
                _logger.LogDebug("Cover image generation completed for playlist {PlaylistName}", playlist.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh metadata for playlist {PlaylistName}. Cover image may not be generated.", playlist.Name);
            }
        }

        /// <summary>
        /// Gets the default triggers.
        /// </summary>
        /// <returns>IEnumerable{TaskTriggerInfo}.</returns>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerInterval,
                    IntervalTicks = TimeSpan.FromMinutes(30).Ticks
                }
            };
        }

        private Playlist GetPlaylist(User user, string name)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Playlist },
                Recursive = true,
                Name = name
            };
            
            return _libraryManager.GetItemsResult(query).Items.OfType<Playlist>().FirstOrDefault();
        }

        private IEnumerable<BaseItem> GetAllUserMedia(User user)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Audio, BaseItemKind.Episode },
                Recursive = true
            };

            return _libraryManager.GetItemsResult(query).Items;
        }
    }
}