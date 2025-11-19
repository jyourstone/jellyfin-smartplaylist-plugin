using System;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Core.Orders
{
    public class ProductionYearOrder : PropertyOrder<int>
    {
        public override string Name => "ProductionYear Ascending";
        protected override bool IsDescending => false;
        protected override int GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            return item.ProductionYear ?? 0;
        }
    }

    public class ProductionYearOrderDesc : PropertyOrder<int>
    {
        public override string Name => "ProductionYear Descending";
        protected override bool IsDescending => true;
        protected override int GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            return item.ProductionYear ?? 0;
        }
    }
}

