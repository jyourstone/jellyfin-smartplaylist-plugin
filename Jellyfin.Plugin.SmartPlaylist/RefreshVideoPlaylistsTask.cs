using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist
{
    /// <summary>
    /// Scheduled task for refreshing video smart playlists (movies, series, episodes).
    /// </summary>
    public class RefreshVideoPlaylistsTask(
        IUserManager userManager,
        ILibraryManager libraryManager,
        ILogger<RefreshVideoPlaylistsTask> logger,
        IServerApplicationPaths serverApplicationPaths,
        IPlaylistManager playlistManager,
        IUserDataManager userDataManager,
        IProviderManager providerManager) : RefreshPlaylistsTaskBase(userManager, libraryManager, logger, serverApplicationPaths, playlistManager, userDataManager, providerManager)
    {
        public override string Name => "Refresh Video SmartPlaylists";
        public override string Description => "Refresh all video SmartPlaylists (movies, series, episodes)";
        public override string Key => "RefreshVideoSmartPlaylists";

        protected override string GetHandledMediaTypes()
        {
            return "video";
        }

        protected override IEnumerable<SmartPlaylistDto> FilterPlaylistsByMediaType(IEnumerable<SmartPlaylistDto> playlists)
        {
            return playlists.Where(playlist => 
                playlist.MediaTypes != null && 
                playlist.MediaTypes.Any(mediaType => 
                    mediaType == "Movie" || 
                    mediaType == "Series" || 
                    mediaType == "Episode"));
        }

        protected override IEnumerable<BaseItem> GetRelevantUserMedia(User user)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.Series],
                Recursive = true
            };

            return libraryManager.GetItemsResult(query).Items;
        }
    }
} 