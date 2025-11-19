using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Core.Orders
{
    public class PlayCountOrder : UserDataOrder
    {
        public override string Name => "PlayCount (owner) Ascending";
        protected override bool IsDescending => false;

        protected override int GetUserDataValue(
            BaseItem item,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            return GetPlayCountFromUserData(item, user, userDataManager, logger, refreshCache);
        }

        /// <summary>
        /// Shared logic for extracting PlayCount from user data
        /// </summary>
        public static int GetPlayCountFromUserData(
            BaseItem item,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(user);
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
                    // Fallback to fetching from userDataManager
                    userData = userDataManager.GetUserData(user, item);
                }

                // Use reflection to safely extract PlayCount from userData
                var playCountProp = userData?.GetType().GetProperty("PlayCount");
                if (playCountProp != null)
                {
                    var playCountValue = playCountProp.GetValue(userData);
                    if (playCountValue is int pc)
                        return pc;
                    if (playCountValue != null)
                        return Convert.ToInt32(playCountValue, System.Globalization.CultureInfo.InvariantCulture);
                }
                return 0;
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Error extracting PlayCount from userData for item {ItemName}", item.Name);
                return 0;
            }
        }
    }

    public class PlayCountOrderDesc : UserDataOrder
    {
        public override string Name => "PlayCount (owner) Descending";
        protected override bool IsDescending => true;

        protected override int GetUserDataValue(
            BaseItem item,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            return PlayCountOrder.GetPlayCountFromUserData(item, user, userDataManager, logger, refreshCache);
        }
    }
}

