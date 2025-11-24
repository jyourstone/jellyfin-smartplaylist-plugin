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
    /// <summary>
    /// Base class for similarity ordering to eliminate duplication
    /// </summary>
    public abstract class SimilarityOrderBase : Order
    {
        protected abstract bool IsDescending { get; }

        // Initialize to empty dictionary instead of null-suppression
        public ConcurrentDictionary<Guid, float> Scores { get; set; } = new();

        public override IEnumerable<BaseItem> OrderBy(IEnumerable<BaseItem> items)
        {
            if (items == null) return [];
            if (Scores.Count == 0)
            {
                // No scores available, return items unsorted
                return items;
            }

            // Sort by similarity score, then by name for deterministic ordering when scores are equal
            var orderedItems = IsDescending
                ? items.OrderByDescending(item => Scores.TryGetValue(item.Id, out var score) ? score : 0)
                : items.OrderBy(item => Scores.TryGetValue(item.Id, out var score) ? score : 0);

            return orderedItems.ThenBy(item => item.Name ?? "", OrderUtilities.SharedNaturalComparer);
        }

        public override IEnumerable<BaseItem> OrderBy(
            IEnumerable<BaseItem> items,
            User user,
            IUserDataManager? userDataManager,
            ILogger? logger,
            RefreshQueueService.RefreshCache? refreshCache = null)
        {
            // Similarity ordering only depends on pre-computed scores, so user context and cache are not needed
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
            if (Scores.TryGetValue(item.Id, out var score))
            {
                return score;
            }
            return 0f;
        }
    }

    public class SimilarityOrder : SimilarityOrderBase
    {
        public override string Name => "Similarity Descending";
        protected override bool IsDescending => true;
    }

    public class SimilarityOrderAsc : SimilarityOrderBase
    {
        public override string Name => "Similarity Ascending";
        protected override bool IsDescending => false;
    }
}

