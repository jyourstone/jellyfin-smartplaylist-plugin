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
                .ThenBy(item => GetDiscNumber(item))
                .ThenBy(item => GetTrackNumber(item))
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
            var discNumber = GetDiscNumber(item);
            var trackNumber = GetTrackNumber(item);
            var name = item.Name ?? "";
            return new ComparableTuple4<string, int, int, string>(album, discNumber, trackNumber, name, OrderUtilities.SharedNaturalComparer);
        }

        private static int GetDiscNumber(BaseItem item)
        {
            try
            {
                // For audio items, ParentIndexNumber represents the disc number
                var parentIndexProperty = item.GetType().GetProperty("ParentIndexNumber");
                if (parentIndexProperty != null)
                {
                    var value = parentIndexProperty.GetValue(item);
                    if (value is int discNum)
                        return discNum;
                }
            }
            catch
            {
                // Ignore errors and return 0
            }
            return 0;
        }

        private static int GetTrackNumber(BaseItem item)
        {
            try
            {
                // For audio items, IndexNumber represents the track number
                var indexProperty = item.GetType().GetProperty("IndexNumber");
                if (indexProperty != null)
                {
                    var value = indexProperty.GetValue(item);
                    if (value is int trackNum)
                        return trackNum;
                }
            }
            catch
            {
                // Ignore errors and return 0
            }
            return 0;
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
                .ThenByDescending(item => GetDiscNumber(item))
                .ThenByDescending(item => GetTrackNumber(item))
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
            var discNumber = GetDiscNumber(item);
            var trackNumber = GetTrackNumber(item);
            var name = item.Name ?? "";
            return new ComparableTuple4<string, int, int, string>(album, discNumber, trackNumber, name, OrderUtilities.SharedNaturalComparer);
        }

        private static int GetDiscNumber(BaseItem item)
        {
            try
            {
                // For audio items, ParentIndexNumber represents the disc number
                var parentIndexProperty = item.GetType().GetProperty("ParentIndexNumber");
                if (parentIndexProperty != null)
                {
                    var value = parentIndexProperty.GetValue(item);
                    if (value is int discNum)
                        return discNum;
                }
            }
            catch
            {
                // Ignore errors and return 0
            }
            return 0;
        }

        private static int GetTrackNumber(BaseItem item)
        {
            try
            {
                // For audio items, IndexNumber represents the track number
                var indexProperty = item.GetType().GetProperty("IndexNumber");
                if (indexProperty != null)
                {
                    var value = indexProperty.GetValue(item);
                    if (value is int trackNum)
                        return trackNum;
                }
            }
            catch
            {
                // Ignore errors and return 0
            }
            return 0;
        }
    }
}

