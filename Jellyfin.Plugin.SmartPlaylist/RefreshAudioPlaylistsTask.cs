using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Data.Entities;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.SmartPlaylist.Constants;

namespace Jellyfin.Plugin.SmartPlaylist
{
    /// <summary>
    /// Scheduled task for refreshing audio/music smart playlists.
    /// </summary>
    public class RefreshAudioPlaylistsTask(
        IUserManager userManager,
        ILibraryManager libraryManager,
        ILogger<RefreshAudioPlaylistsTask> logger,
        IServerApplicationPaths serverApplicationPaths,
        IPlaylistManager playlistManager,
        IUserDataManager userDataManager,
        IProviderManager providerManager) : RefreshPlaylistsTaskBase(userManager, libraryManager, logger, serverApplicationPaths, playlistManager, userDataManager, providerManager)
    {
        public override string Name => "Refresh Audio SmartPlaylists";
        public override string Description => "Refresh all audio/music SmartPlaylists";
        public override string Key => "RefreshAudioSmartPlaylists";

        protected override string GetHandledMediaTypes()
        {
            return "audio";
        }

        protected override IEnumerable<SmartPlaylistDto> FilterPlaylistsByMediaType(IEnumerable<SmartPlaylistDto> playlists)
        {
            return playlists.Where(playlist => 
                playlist.MediaTypes != null && 
                playlist.MediaTypes.Any(mediaType => 
                    MediaTypes.AudioOnly.Contains(mediaType)));
        }

        protected override IEnumerable<BaseItem> GetRelevantUserMedia(User user)
        {
            var query = new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Audio, BaseItemKind.AudioBook],
                Recursive = true
            };

            return libraryManager.GetItemsResult(query).Items;
        }

        /// <summary>
        /// Gets the default triggers - runs once daily at 2:30 AM.
        /// </summary>
        /// <returns>IEnumerable{TaskTriggerInfo}.</returns>
        public override IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return
            [
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(3.5).Ticks // 3:30 AM
                }
            ];
        }
    }
} 