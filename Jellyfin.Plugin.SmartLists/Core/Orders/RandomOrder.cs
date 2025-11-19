using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Core.Orders
{
    public class RandomOrder : Order
    {
        public override string Name => "Random";

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            if (items == null) return [];

            // Convert to list to ensure stable enumeration
            var itemsList = items.ToList();
            if (itemsList.Count == 0) return [];

            // Use current ticks as seed for different results each refresh
            // Suppress CA5394: Random is acceptable here - we're not using it for security purposes, just for shuffling playlist items
#pragma warning disable CA5394
            var random = new Random((int)(DateTime.Now.Ticks & 0x7FFFFFFF));
#pragma warning restore CA5394

            // Create a list of items with their random keys to ensure consistent random values
            // Suppress CA5394: Random.Next() is acceptable here - we're not using it for security purposes, just for shuffling playlist items
#pragma warning disable CA5394
            var itemsWithKeys = itemsList.Select(item => new { Item = item, Key = random.Next() }).ToList();
#pragma warning restore CA5394

            // Sort by the pre-generated random keys
            return itemsWithKeys.OrderBy(x => x.Key).Select(x => x.Item);
        }

        public override IEnumerable<BaseItem> OrderBy(
            IEnumerable<BaseItem> items,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // refreshCache not used for random ordering
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
            // For random order, use pre-generated random key that's different each refresh
            // but stable within this sort operation
            if (itemRandomKeys != null && itemRandomKeys.TryGetValue(item.Id, out var randomKey))
            {
                return randomKey;
            }
            // Fallback to hash if not pre-generated (shouldn't happen in multi-sort, but needed for single-sort)
            return item.Id.GetHashCode();
        }
    }
}

