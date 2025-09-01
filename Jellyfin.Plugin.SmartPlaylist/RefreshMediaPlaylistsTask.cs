using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Data.Entities;
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
        public override string Name => "Refresh Media Smart Playlists";
        public override string Description => "Refreshes smart playlists for all media content except audio/music (movies, TV shows, books, music videos, home videos, and photos)";
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
                    !MediaTypes.AudioOnly.Contains(mediaType)));
        }

        protected override IEnumerable<BaseItem> GetRelevantUserMedia(User user)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.Series, BaseItemKind.MusicVideo, BaseItemKind.Video, BaseItemKind.Photo, BaseItemKind.Book],
                Recursive = true
            };

            return libraryManager.GetItemsResult(query).Items;
        }
    }
} 