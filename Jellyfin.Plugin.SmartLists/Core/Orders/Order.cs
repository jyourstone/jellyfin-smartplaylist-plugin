using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Core.Orders
{
    public abstract class Order
    {
        public abstract string Name { get; }

        public virtual IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            return items ?? [];
        }

        public virtual IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items, User user, IUserDataManager? userDataManager, ILogger? logger, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // Default implementation falls back to the simple OrderBy method
            return OrderBy(items);
        }

        /// <summary>
        /// Creates a comparable sort key for an item. This method is used for multi-sort scenarios
        /// and should return the same value that would be used for sorting in single-sort scenarios.
        /// </summary>
        public virtual IComparable GetSortKey(
            BaseItem item,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            Dictionary<Guid, int>? itemRandomKeys = null,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // Default implementation returns name as fallback
            return item.Name ?? "";
        }
    }
}

