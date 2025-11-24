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
    /// Base class for user-data-based sorting with safe caching and error handling
    /// </summary>
    public abstract class UserDataOrder : Order
    {
        protected abstract bool IsDescending { get; }

        // Unified method that can use cache
        protected abstract int GetUserDataValue(
            BaseItem item,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null);

        public override IEnumerable<BaseItem> OrderBy(
            IEnumerable<BaseItem> items,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            if (items == null) return [];
            if (userDataManager == null || user == null)
            {
                logger?.LogWarning("UserDataManager or User is null for {OrderType} sorting, returning unsorted items", GetType().Name);
                return items;
            }

            try
            {
                var list = items as IList<BaseItem> ?? items.ToList();

                // Pre-cache user data for performance
                var sortValueCache = new Dictionary<BaseItem, int>(list.Count);
                foreach (var item in list)
                {
                    try
                    {
                        sortValueCache[item] = GetUserDataValue(item, user, userDataManager, logger, refreshCache);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error getting user data for item {ItemName}", item.Name);
                        sortValueCache[item] = 0;
                    }
                }

                // Sort using cached values with DateCreated as tie-breaker
                return IsDescending
                    ? list.OrderByDescending(item => sortValueCache[item]).ThenByDescending(item => item.DateCreated)
                    : list.OrderBy(item => sortValueCache[item]).ThenBy(item => item.DateCreated);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error in {OrderType} sorting", GetType().Name);
                return items;
            }
        }

        public override IComparable GetSortKey(
            BaseItem item,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            Dictionary<Guid, int>? itemRandomKeys = null,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            try
            {
                // Delegate to unified method
                return GetUserDataValue(item, user, userDataManager, logger, refreshCache);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error getting user data value for item {ItemName} in {OrderType}, returning default value 0", item?.Name ?? "Unknown", GetType().Name);
                return 0;
            }
        }
    }
}

