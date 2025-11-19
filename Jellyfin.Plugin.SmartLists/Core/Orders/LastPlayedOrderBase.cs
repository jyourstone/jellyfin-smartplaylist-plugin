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
    /// Base class for LastPlayed ordering to eliminate duplication
    /// </summary>
    public abstract class LastPlayedOrderBase : Order
    {
        protected abstract bool IsDescending { get; }

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
                logger?.LogWarning("UserDataManager or User is null for LastPlayed sorting, returning unsorted items");
                return items;
            }

            try
            {
                // Pre-fetch all user data to avoid repeated database calls during sorting
                var list = items as IList<BaseItem> ?? items.ToList();
                var sortValueCache = new Dictionary<BaseItem, DateTime>(list.Count);

                foreach (var item in list)
                {
                    try
                    {
                        object? userData = null;
                        
                        // Try to get user data from cache if available
                        if (refreshCache != null && refreshCache.UserDataCache.TryGetValue((item.Id, user.Id), out var cachedUserData))
                        {
                            userData = cachedUserData;
                        }
                        else
                        {
                            userData = userDataManager.GetUserData(user, item);
                        }
                        
                        var lastPlayedProp = userData?.GetType().GetProperty("LastPlayedDate");
                        if (lastPlayedProp != null)
                        {
                            var lastPlayedValue = lastPlayedProp.GetValue(userData);
                            if (lastPlayedValue is DateTime dt && dt != DateTime.MinValue)
                            {
                                sortValueCache[item] = dt;
                            }
                            else
                            {
                                sortValueCache[item] = DateTime.MinValue; // Never played = oldest
                            }
                        }
                        else
                        {
                            sortValueCache[item] = DateTime.MinValue;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error getting user data for item {ItemName} for user {UserId}", item.Name, user.Id);
                        sortValueCache[item] = DateTime.MinValue; // Default to never played
                    }
                }

                // Sort using cached DateTime values directly (no tie-breaker to avoid album grouping)
                return IsDescending
                    ? list.OrderByDescending(item => sortValueCache[item])
                    : list.OrderBy(item => sortValueCache[item]);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error in LastPlayed sorting for user {UserId}, returning unsorted items", user.Id);
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
                object? userData = null;
                
                // Try to get user data from cache if available
                if (refreshCache != null && refreshCache.UserDataCache.TryGetValue((item.Id, user.Id), out var cachedUserData))
                {
                    userData = cachedUserData;
                }
                else if (userDataManager != null)
                {
                    userData = userDataManager.GetUserData(user, item);
                }
                
                var lastPlayedProp = userData?.GetType().GetProperty("LastPlayedDate");
                if (lastPlayedProp != null)
                {
                    var lastPlayedValue = lastPlayedProp.GetValue(userData);
                    if (lastPlayedValue is DateTime dt && dt != DateTime.MinValue)
                    {
                        return dt.Ticks;
                    }
                }
                return DateTime.MinValue.Ticks; // Never played = oldest
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error getting last played date for item {ItemId} user {UserId}", item.Id, user.Id);
                return DateTime.MinValue.Ticks;
            }
        }
    }
}

