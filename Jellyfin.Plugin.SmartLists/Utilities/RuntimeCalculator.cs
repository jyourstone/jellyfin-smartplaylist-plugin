using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.SmartLists.Core.Models;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Utilities
{
    /// <summary>
    /// Utility class for calculating runtime statistics for smart lists.
    /// </summary>
    public static class RuntimeCalculator
    {
        /// <summary>
        /// Calculates the total runtime in minutes for all items in a list.
        /// </summary>
        /// <param name="itemIds">Array of item GUIDs</param>
        /// <param name="mediaLookup">Dictionary mapping item GUIDs to BaseItem objects</param>
        /// <param name="logger">Logger for diagnostics</param>
        /// <returns>Total runtime in minutes, or null if no items have runtime information</returns>
        public static double? CalculateTotalRuntimeMinutes(Guid[] itemIds, Dictionary<Guid, BaseItem> mediaLookup, ILogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(itemIds);
            ArgumentNullException.ThrowIfNull(mediaLookup);
            
            double totalMinutes = 0.0;
            int itemsWithRuntime = 0;

            foreach (var itemId in itemIds)
            {
                if (mediaLookup.TryGetValue(itemId, out var item))
                {
                    if (item.RunTimeTicks.HasValue)
                    {
                        var itemMinutes = TimeSpan.FromTicks(item.RunTimeTicks.Value).TotalMinutes;
                        totalMinutes += itemMinutes;
                        itemsWithRuntime++;
                    }
                }
            }

            // Only return runtime if at least one item has runtime information
            if (itemsWithRuntime > 0)
            {
                return totalMinutes;
            }

            return null;
        }
    }
}

