using System;
using System.Collections.Concurrent;
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
    public class SimilarityOrder : Order
    {
        public override string Name => "Similarity Descending";

        // Scores dictionary will be set by SmartList before sorting
        public ConcurrentDictionary<Guid, float> Scores { get; set; } = null!;

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            if (items == null) return [];
            if (Scores == null || Scores.Count == 0)
            {
                // No scores available, return items unsorted
                return items;
            }

            // Sort by similarity score (highest first), then by name for deterministic ordering when scores are equal
            return items
                .OrderByDescending(item => Scores.TryGetValue(item.Id, out var score) ? score : 0)
                .ThenBy(item => item.Name ?? "", OrderUtilities.SharedNaturalComparer);
        }

        public override IEnumerable<BaseItem> OrderBy(
            IEnumerable<BaseItem> items,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // refreshCache not used for similarity ordering
            return OrderBy(items);
        }

        public override IComparable GetSortKey(
            BaseItem item,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            Dictionary<Guid, int>? itemRandomKeys = null,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            if (Scores != null && Scores.TryGetValue(item.Id, out var score))
            {
                return score;
            }
            return 0f;
        }
    }

    public class SimilarityOrderAsc : Order
    {
        public override string Name => "Similarity Ascending";

        // Scores dictionary will be set by SmartList before sorting
        public ConcurrentDictionary<Guid, float> Scores { get; set; } = null!;

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            if (items == null) return [];
            if (Scores == null || Scores.Count == 0)
            {
                // No scores available, return items unsorted
                return items;
            }

            // Sort by similarity score (lowest first), then by name for deterministic ordering when scores are equal
            return items
                .OrderBy(item => Scores.TryGetValue(item.Id, out var score) ? score : 0)
                .ThenBy(item => item.Name ?? "", OrderUtilities.SharedNaturalComparer);
        }

        public override IEnumerable<BaseItem> OrderBy(
            IEnumerable<BaseItem> items,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // refreshCache not used for similarity ordering
            return OrderBy(items);
        }

        public override IComparable GetSortKey(
            BaseItem item,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            Dictionary<Guid, int>? itemRandomKeys = null,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            if (Scores != null && Scores.TryGetValue(item.Id, out var score))
            {
                return score;
            }
            return 0f;
        }
    }
}

