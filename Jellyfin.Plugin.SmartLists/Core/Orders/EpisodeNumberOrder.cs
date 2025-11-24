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
    public class EpisodeNumberOrder : Order
    {
        public override string Name => "EpisodeNumber Ascending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            if (items == null) return [];

            // Sort by Episode Number -> Season Number -> Name
            return items
                .OrderBy(item => OrderUtilities.GetEpisodeNumber(item))
                .ThenBy(item => OrderUtilities.GetSeasonNumber(item))
                .ThenBy(item => item.Name ?? "", OrderUtilities.SharedNaturalComparer);
        }

        public override IEnumerable<BaseItem> OrderBy(
            IEnumerable<BaseItem> items,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // refreshCache not used for episode number ordering
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
            // Return composite key matching OrderBy logic: EpisodeNumber -> SeasonNumber -> Name
            var episodeNumber = OrderUtilities.GetEpisodeNumber(item);
            var seasonNumber = OrderUtilities.GetSeasonNumber(item);
            var name = item.Name ?? "";
            return new ComparableTuple4<int, int, string, string>(
                episodeNumber,
                seasonNumber,
                name,
                "", // Fourth element not used, but ComparableTuple4 requires 4 elements
                comparer3: OrderUtilities.SharedNaturalComparer);
        }
    }

    public class EpisodeNumberOrderDesc : Order
    {
        public override string Name => "EpisodeNumber Descending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            if (items == null) return [];

            // Sort by Episode Number (descending) -> Season Number (descending) -> Name (descending)
            return items
                .OrderByDescending(item => OrderUtilities.GetEpisodeNumber(item))
                .ThenByDescending(item => OrderUtilities.GetSeasonNumber(item))
                .ThenByDescending(item => item.Name ?? "", OrderUtilities.SharedNaturalComparer);
        }

        public override IEnumerable<BaseItem> OrderBy(
            IEnumerable<BaseItem> items,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // refreshCache not used for episode number ordering
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
            // Return composite key matching OrderBy logic: EpisodeNumber -> SeasonNumber -> Name
            // NOTE: Do NOT negate values here! The sorting direction is controlled by
            // OrderBy vs OrderByDescending in ApplyMultipleOrders for multi-level sorting.
            // Negating here would cause a double reversal.
            var episodeNumber = OrderUtilities.GetEpisodeNumber(item);
            var seasonNumber = OrderUtilities.GetSeasonNumber(item);
            var name = item.Name ?? "";
            return new ComparableTuple4<int, int, string, string>(
                episodeNumber,
                seasonNumber,
                name,
                "", // Fourth element not used, but ComparableTuple4 requires 4 elements
                comparer3: OrderUtilities.SharedNaturalComparer);
        }
    }
}
