using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartPlaylist.Constants;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist
{
    /// <summary>
    /// Scheduled task for refreshing media smart playlists (everything except audio/music).
    /// Handles movies, TV shows, books, photos, music videos, and home videos.
    /// </summary>
    public class RefreshMediaPlaylistsTask(
        IUserManager userManager,
        ILibraryManager libraryManager,
        ILogger<RefreshMediaPlaylistsTask> logger,
        IServerApplicationPaths serverApplicationPaths,
        IPlaylistManager playlistManager,
        IUserDataManager userDataManager,
        IProviderManager providerManager) : RefreshPlaylistsTaskBase(userManager, libraryManager, logger, serverApplicationPaths, playlistManager, userDataManager, providerManager)
    {
        public override string Name => "Refresh Media SmartPlaylists (Legacy)";
        public override string Description => "Refreshes all legacy smart playlists for all media content except audio/music, that does not use the new individual playlist refresh functionality.";
        public override string Key => "RefreshMediaSmartPlaylists";

        protected override string GetHandledMediaTypes()
        {
            return "media";
        }

        protected override IEnumerable<SmartPlaylistDto> FilterPlaylistsByMediaType(IEnumerable<SmartPlaylistDto> playlists)
        {
            return playlists.Where(playlist => 
                playlist.MediaTypes != null && 
                playlist.MediaTypes.Any(mediaType => 
                    !MediaTypes.AudioOnlySet.Contains(mediaType)));
        }

        protected override IEnumerable<BaseItem> GetRelevantUserMedia(User user)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = MediaTypes.GetNonAudioBaseItemKinds(),
                Recursive = true
            };

            return libraryManager.GetItemsResult(query).Items;
        }
    }
} 