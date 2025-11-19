using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartLists.Core;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Core.Orders
{
    public class ReleaseDateOrder : Order
    {
        public override string Name => "ReleaseDate Ascending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            if (items == null) return [];

            // Sort by release date (day precision), then within the same day: episodes first, then by season/episode
            return items
                .OrderBy(item => OrderUtilities.GetReleaseDate(item).Date)
                .ThenBy(item => OrderUtilities.IsEpisode(item) ? 0 : 1) // Episodes first within same date
                .ThenBy(item => OrderUtilities.IsEpisode(item) ? OrderUtilities.GetSeasonNumber(item) : 0)
                .ThenBy(item => OrderUtilities.IsEpisode(item) ? OrderUtilities.GetEpisodeNumber(item) : 0);
        }

        public override IEnumerable<BaseItem> OrderBy(
            IEnumerable<BaseItem> items,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // refreshCache not used for release date ordering
            return OrderBy(items);
        }

        public override IComparable GetSortKey(
            BaseItem item,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            Dictionary<Guid, int>? itemRandomKeys = null,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // For ReleaseDateOrder - complex multi-level sort: ReleaseDate -> IsEpisode -> Season -> Episode
            var releaseDate = OrderUtilities.GetReleaseDate(item).Date.Ticks;
            // For ascending order, episodes come first
            var isEpisode = OrderUtilities.IsEpisode(item) ? 0 : 1; // Episodes first for ascending
            var seasonNumber = OrderUtilities.IsEpisode(item) ? OrderUtilities.GetSeasonNumber(item) : 0;
            var episodeNumber = OrderUtilities.IsEpisode(item) ? OrderUtilities.GetEpisodeNumber(item) : 0;
            return new ComparableTuple4<long, int, int, int>(releaseDate, isEpisode, seasonNumber, episodeNumber);
        }
    }

    public class ReleaseDateOrderDesc : Order
    {
        public override string Name => "ReleaseDate Descending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            if (items == null) return [];

            // Sort by release date (day precision) descending; within same day, episodes first then season/episode descending
            return items
                .OrderByDescending(item => OrderUtilities.GetReleaseDate(item).Date)
                .ThenBy(item => OrderUtilities.IsEpisode(item) ? 0 : 1) // Episodes first within same date
                .ThenByDescending(item => OrderUtilities.IsEpisode(item) ? OrderUtilities.GetSeasonNumber(item) : 0)
                .ThenByDescending(item => OrderUtilities.IsEpisode(item) ? OrderUtilities.GetEpisodeNumber(item) : 0);
        }

        public override IEnumerable<BaseItem> OrderBy(
            IEnumerable<BaseItem> items,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // refreshCache not used for release date ordering
            return OrderBy(items);
        }

        public override IComparable GetSortKey(
            BaseItem item,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            Dictionary<Guid, int>? itemRandomKeys = null,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // For ReleaseDateOrder - complex multi-level sort: ReleaseDate -> IsEpisode -> Season -> Episode
            var releaseDate = OrderUtilities.GetReleaseDate(item).Date.Ticks;
            // For descending order, flip the episode marker so episodes still come first when sorted descending
            // Original uses .ThenBy() even for descending, we need to account for that
            var isEpisode = OrderUtilities.IsEpisode(item) ? 1 : 0; // Flip for descending so ThenByDescending still puts episodes first,
            var seasonNumber = OrderUtilities.IsEpisode(item) ? OrderUtilities.GetSeasonNumber(item) : 0;
            var episodeNumber = OrderUtilities.IsEpisode(item) ? OrderUtilities.GetEpisodeNumber(item) : 0;
            return new ComparableTuple4<long, int, int, int>(releaseDate, isEpisode, seasonNumber, episodeNumber);
        }
    }
}

