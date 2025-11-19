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
    public class TrackNumberOrder : Order
    {
        public override string Name => "TrackNumber Ascending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            if (items == null) return [];

            // Sort by Album -> Disc Number -> Track Number -> Name
            return items
                .OrderBy(item => item.Album ?? "", OrderUtilities.SharedNaturalComparer)
                .ThenBy(item => OrderUtilities.GetDiscNumber(item))
                .ThenBy(item => OrderUtilities.GetTrackNumber(item))
                .ThenBy(item => item.Name ?? "", OrderUtilities.SharedNaturalComparer);
        }

        public override IEnumerable<BaseItem> OrderBy(
            IEnumerable<BaseItem> items,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // refreshCache not used for track number ordering
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
            // For TrackNumberOrder - complex multi-level sort: Album -> Disc -> Track -> Name
            var album = item.Album ?? "";
            var discNumber = OrderUtilities.GetDiscNumber(item);
            var trackNumber = OrderUtilities.GetTrackNumber(item);
            var name = item.Name ?? "";
            
            // FIX: Pass SharedNaturalComparer for both album AND name to match OrderBy behavior
            return new ComparableTuple4<string, int, int, string>(
                album, discNumber, trackNumber, name,
                OrderUtilities.SharedNaturalComparer,  // for album
                null,  // for discNumber (int uses default)
                null,  // for trackNumber (int uses default)
                OrderUtilities.SharedNaturalComparer   // for name - THIS WAS MISSING
            );
        }
    }

    public class TrackNumberOrderDesc : Order
    {
        public override string Name => "TrackNumber Descending";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            if (items == null) return [];

            // Sort by Album (descending) -> Disc Number (descending) -> Track Number (descending) -> Name (descending)
            return items
                .OrderByDescending(item => item.Album ?? "", OrderUtilities.SharedNaturalComparer)
                .ThenByDescending(item => OrderUtilities.GetDiscNumber(item))
                .ThenByDescending(item => OrderUtilities.GetTrackNumber(item))
                .ThenByDescending(item => item.Name ?? "", OrderUtilities.SharedNaturalComparer);
        }

        public override IEnumerable<BaseItem> OrderBy(
            IEnumerable<BaseItem> items,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // refreshCache not used for track number ordering
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
            // For TrackNumberOrder - complex multi-level sort: Album -> Disc -> Track -> Name
            var album = item.Album ?? "";
            var discNumber = OrderUtilities.GetDiscNumber(item);
            var trackNumber = OrderUtilities.GetTrackNumber(item);
            var name = item.Name ?? "";
            
            // FIX: Pass SharedNaturalComparer for both album AND name to match OrderBy behavior
            return new ComparableTuple4<string, int, int, string>(
                album, discNumber, trackNumber, name,
                OrderUtilities.SharedNaturalComparer,  // for album
                null,  // for discNumber (int uses default)
                null,  // for trackNumber (int uses default)
                OrderUtilities.SharedNaturalComparer   // for name - THIS WAS MISSING
            );
        }
    }
}

