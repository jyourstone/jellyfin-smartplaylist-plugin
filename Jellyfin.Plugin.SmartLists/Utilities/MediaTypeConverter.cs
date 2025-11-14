using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.SmartLists.Core.Constants;
using Jellyfin.Plugin.SmartLists.Core.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Utilities
{
    /// <summary>
    /// Utility class for converting media types to BaseItemKind enums.
    /// </summary>
    public static class MediaTypeConverter
    {
        /// <summary>
        /// Maps string media types to BaseItemKind enums for API-level filtering.
        /// </summary>
        /// <param name="mediaTypes">List of media type strings</param>
        /// <param name="dto">Optional DTO for smart query expansion (Collections episode expansion)</param>
        /// <param name="logger">Optional logger for diagnostics</param>
        /// <returns>Array of BaseItemKind enums</returns>
        public static BaseItemKind[] GetBaseItemKindsFromMediaTypes(
            List<string>? mediaTypes,
            SmartListDto? dto = null,
            ILogger? logger = null)
        {
            // This method should only be called after validation, so empty media types should not happen
            if (mediaTypes == null || mediaTypes.Count == 0)
            {
                logger?.LogError("GetBaseItemKindsFromMediaTypes called with empty media types - this should have been caught by validation");
                throw new InvalidOperationException("No media types specified - this should have been caught by validation");
            }

            var baseItemKinds = new List<BaseItemKind>();

            foreach (var mediaType in mediaTypes)
            {
                if (MediaTypes.MediaTypeToBaseItemKind.TryGetValue(mediaType, out var baseItemKind))
                {
                    baseItemKinds.Add(baseItemKind);
                }
                else
                {
                    logger?.LogWarning("Unknown media type '{MediaType}' - skipping", mediaType);
                }
            }

            // Smart Query Expansion: If Episodes media type is selected AND Collections episode expansion is enabled,
            // also include Series in the query so we can find series in collections and expand them to episodes
            if (dto != null && baseItemKinds.Contains(BaseItemKind.Episode) && !baseItemKinds.Contains(BaseItemKind.Series))
            {
                var hasCollectionsEpisodeExpansion = dto.ExpressionSets?.Any(set =>
                    set.Expressions?.Any(expr =>
                        expr.MemberName == "Collections" && expr.IncludeEpisodesWithinSeries == true) == true) == true;

                if (hasCollectionsEpisodeExpansion)
                {
                    baseItemKinds.Add(BaseItemKind.Series);
                    logger?.LogDebug("Auto-including Series in query for Episodes media type due to Collections episode expansion");
                }
            }

            // This should not happen if validation is working correctly
            if (baseItemKinds.Count == 0)
            {
                logger?.LogError("No valid media types found after processing - this should have been caught by validation");
                throw new InvalidOperationException("No valid media types found - this should have been caught by validation");
            }

            return [.. baseItemKinds];
        }
    }
}

