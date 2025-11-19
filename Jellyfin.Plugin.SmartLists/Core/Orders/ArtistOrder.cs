using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartLists.Core;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Core.Orders
{
    public class ArtistOrder : PropertyOrder<string>
    {
        public override string Name => "Artist Ascending";
        protected override bool IsDescending => false;
        protected override IComparer<string> Comparer => OrderUtilities.SharedNaturalComparer;

        protected override string GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            try
            {
                // Try to get Artists property (it's a list, so we'll use the first one for sorting)
                var artistsProperty = item.GetType().GetProperty("Artists");
                if (artistsProperty != null)
                {
                    var value = artistsProperty.GetValue(item);
                    if (value is IEnumerable<string> artists)
                    {
                        var firstArtist = artists.FirstOrDefault();
                        if (firstArtist != null)
                            return firstArtist;
                    }
                }
            }
            catch
            {
                // Ignore errors and return empty string
            }
            return "";
        }
    }

    public class ArtistOrderDesc : PropertyOrder<string>
    {
        public override string Name => "Artist Descending";
        protected override bool IsDescending => true;
        protected override IComparer<string> Comparer => OrderUtilities.SharedNaturalComparer;

        protected override string GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);
            try
            {
                // Try to get Artists property (it's a list, so we'll use the first one for sorting)
                var artistsProperty = item.GetType().GetProperty("Artists");
                if (artistsProperty != null)
                {
                    var value = artistsProperty.GetValue(item);
                    if (value is IEnumerable<string> artists)
                    {
                        var firstArtist = artists.FirstOrDefault();
                        if (firstArtist != null)
                            return firstArtist;
                    }
                }
            }
            catch
            {
                // Ignore errors and return empty string
            }
            return "";
        }
    }
}

