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
    /// <summary>
    /// Generic base class for simple property-based sorting to eliminate code duplication
    /// </summary>
    public abstract class PropertyOrder<T> : Order where T : IComparable<T>, IComparable
    {
        /// <summary>
        /// Gets the sort key for an item. This is the unified method used for both single-sort and multi-sort.
        /// Subclasses implement this method, and both OrderBy and GetSortKey use it.
        /// </summary>
        protected abstract T GetSortValue(
            BaseItem item,
            User? user = null,
            IUserDataManager? userDataManager = null,
            ILogger? logger = null,
            RefreshQueueService.RefreshCache? refreshCache = null);
        protected abstract bool IsDescending { get; }
        protected virtual IComparer<T> Comparer => Comparer<T>.Default;

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            if (items == null) return [];

            // Use GetSortValue - the unified sorting logic
            return IsDescending
                ? items.OrderByDescending(item => GetSortValue(item), Comparer)
                : items.OrderBy(item => GetSortValue(item), Comparer);
        }

        public override IEnumerable<BaseItem> OrderBy(
            IEnumerable<BaseItem> items,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            if (items == null) return [];

            // Use unified GetSortValue with cache
            return IsDescending
                ? items.OrderByDescending(item => GetSortValue(item, user, userDataManager, logger, refreshCache), Comparer)
                : items.OrderBy(item => GetSortValue(item, user, userDataManager, logger, refreshCache), Comparer);
        }

        public override IComparable GetSortKey(
            BaseItem item,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            Dictionary<Guid, int>? itemRandomKeys = null,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // Delegate to unified GetSortValue
            return GetSortValue(item, user, userDataManager, logger, refreshCache);
        }
    }
}

