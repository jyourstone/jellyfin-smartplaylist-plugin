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
    public class SeriesNameOrder : PropertyOrder<string>
    {
        public override string Name => "SeriesName Ascending";
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

            // Try to get Series Name from cache first (for episodes)
            if (refreshCache != null && item is Episode episode && episode.SeriesId != Guid.Empty)
            {
                // For strict SeriesName sort, we prefer the Display Name (e.g. "The IT Crowd")
                // This allows users to choose between "The IT Crowd" (SeriesName) and "IT Crowd" (IgnoreArticles/SortName)
                if (refreshCache.SeriesNameById.TryGetValue(episode.SeriesId, out var cachedName))
                {
                    return cachedName;
                }
                if (refreshCache.SeriesSortNameById.TryGetValue(episode.SeriesId, out var cachedSortName) && !string.IsNullOrEmpty(cachedSortName))
                {
                    return cachedSortName;
                }
            }

            // Fallback to item properties
            // Use SortName if set, otherwise fall back to SeriesName
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
                        return seriesName ?? "";
                }
            }
            catch
            {
                // Ignore errors and return empty string
            }
            return "";
        }
    }

    public class SeriesNameOrderDesc : PropertyOrder<string>
    {
        public override string Name => "SeriesName Descending";
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

            // Try to get Series Name from cache first (for episodes)
            if (refreshCache != null && item is Episode episode && episode.SeriesId != Guid.Empty)
            {
                // For strict SeriesName sort, we prefer the Display Name (e.g. "The IT Crowd")
                // This allows users to choose between "The IT Crowd" (SeriesName) and "IT Crowd" (IgnoreArticles/SortName)
                if (refreshCache.SeriesNameById.TryGetValue(episode.SeriesId, out var cachedName))
                {
                    return cachedName;
                }
                if (refreshCache.SeriesSortNameById.TryGetValue(episode.SeriesId, out var cachedSortName) && !string.IsNullOrEmpty(cachedSortName))
                {
                    return cachedSortName;
                }
            }

            // Fallback to item properties
            // Use SortName if set, otherwise fall back to SeriesName
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
                        return seriesName ?? "";
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

