using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartLists.Core;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Core.Orders
{
    public class SeriesNameIgnoreArticlesOrder : PropertyOrder<string>
    {
        public override string Name => "SeriesName (Ignore Articles) Ascending";
        protected override bool IsDescending => false;
        protected override IComparer<string> Comparer => OrderUtilities.SharedNaturalComparer;

        protected override string GetSortValue(
            BaseItem item,
            User? user = null,
            IUserDataManager? userDataManager = null,
            ILogger? logger = null,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);

            // Try to get Series SortName from cache first (for episodes)
            if (refreshCache != null && item is Episode episode && episode.SeriesId != Guid.Empty)
            {
                if (refreshCache.SeriesSortNameById.TryGetValue(episode.SeriesId, out var cachedSortName) && !string.IsNullOrEmpty(cachedSortName))
                {
                    return cachedSortName;
                }
                if (refreshCache.SeriesNameById.TryGetValue(episode.SeriesId, out var cachedName))
                {
                    return OrderUtilities.StripLeadingArticles(cachedName);
                }
            }

            // Fallback to item properties
            // Use SortName if set (as-is, without article stripping), otherwise strip articles from SeriesName
            if (!string.IsNullOrEmpty(item.SortName))
                return item.SortName;

            try
            {
                // SeriesName property for episodes
                var seriesNameProperty = item.GetType().GetProperty("SeriesName");
                if (seriesNameProperty != null)
                {
                    var value = seriesNameProperty.GetValue(item);
                    if (value is string seriesName)
                        return OrderUtilities.StripLeadingArticles(seriesName ?? "");
                }
            }
            catch
            {
                // Ignore errors and return empty string
            }
            return "";
        }
    }

    public class SeriesNameIgnoreArticlesOrderDesc : PropertyOrder<string>
    {
        public override string Name => "SeriesName (Ignore Articles) Descending";
        protected override bool IsDescending => true;
        protected override IComparer<string> Comparer => OrderUtilities.SharedNaturalComparer;

        protected override string GetSortValue(
            BaseItem item,
            User? user = null,
            IUserDataManager? userDataManager = null,
            ILogger? logger = null,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            ArgumentNullException.ThrowIfNull(item);

            // Try to get Series SortName from cache first (for episodes)
            if (refreshCache != null && item is Episode episode && episode.SeriesId != Guid.Empty)
            {
                if (refreshCache.SeriesSortNameById.TryGetValue(episode.SeriesId, out var cachedSortName) && !string.IsNullOrEmpty(cachedSortName))
                {
                    return cachedSortName;
                }
                if (refreshCache.SeriesNameById.TryGetValue(episode.SeriesId, out var cachedName))
                {
                    return OrderUtilities.StripLeadingArticles(cachedName);
                }
            }

            // Fallback to item properties
            // Use SortName if set (as-is, without article stripping), otherwise strip articles from SeriesName
            if (!string.IsNullOrEmpty(item.SortName))
                return item.SortName;

            try
            {
                // SeriesName property for episodes
                var seriesNameProperty = item.GetType().GetProperty("SeriesName");
                if (seriesNameProperty != null)
                {
                    var value = seriesNameProperty.GetValue(item);
                    if (value is string seriesName)
                        return OrderUtilities.StripLeadingArticles(seriesName ?? "");
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

