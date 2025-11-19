using System;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Core.Orders
{
    public class DateCreatedOrder : PropertyOrder<DateTime>
    {
        public override string Name => "DateCreated Ascending";
        protected override bool IsDescending => false;
        protected override DateTime GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            return item.DateCreated;
        }
    }

    public class DateCreatedOrderDesc : PropertyOrder<DateTime>
    {
        public override string Name => "DateCreated Descending";
        protected override bool IsDescending => true;
        protected override DateTime GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            return item.DateCreated;
        }
    }
}

