using System;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Core.Orders
{
    public class CommunityRatingOrder : PropertyOrder<float>
    {
        public override string Name => "CommunityRating Ascending";
        protected override bool IsDescending => false;
        protected override float GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            return item.CommunityRating ?? 0;
        }
    }

    public class CommunityRatingOrderDesc : PropertyOrder<float>
    {
        public override string Name => "CommunityRating Descending";
        protected override bool IsDescending => true;
        protected override float GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            return item.CommunityRating ?? 0;
        }
    }
}

