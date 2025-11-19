using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartLists.Core;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Core.Orders
{
    public class AlbumNameOrder : PropertyOrder<string>
    {
        public override string Name => "AlbumName Ascending";
        protected override bool IsDescending => false;
        protected override IComparer<string> Comparer => OrderUtilities.SharedNaturalComparer;

        protected override string GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            // Album is already a property on BaseItem
            return item.Album ?? "";
        }
    }

    public class AlbumNameOrderDesc : PropertyOrder<string>
    {
        public override string Name => "AlbumName Descending";
        protected override bool IsDescending => true;
        protected override IComparer<string> Comparer => OrderUtilities.SharedNaturalComparer;

        protected override string GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            // Album is already a property on BaseItem
            return item.Album ?? "";
        }
    }
}

