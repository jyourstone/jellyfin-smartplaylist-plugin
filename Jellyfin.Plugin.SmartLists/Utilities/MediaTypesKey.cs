using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.SmartLists.Core.Constants;
using Jellyfin.Plugin.SmartLists.Core.Models;

namespace Jellyfin.Plugin.SmartLists.Utilities
{
    /// <summary>
    /// Cache key for media types to avoid string collision issues
    /// </summary>
    internal readonly record struct MediaTypesKey : IEquatable<MediaTypesKey>
    {
        private readonly string[] _sortedTypes;
        private readonly bool _hasCollectionsExpansion;

        private MediaTypesKey(string[] sortedTypes, bool hasCollectionsExpansion = false)
        {
            _sortedTypes = sortedTypes;
            _hasCollectionsExpansion = hasCollectionsExpansion;
        }

        public static MediaTypesKey Create(List<string> mediaTypes)
        {
            return Create(mediaTypes, null);
        }

        public static MediaTypesKey Create(List<string> mediaTypes, SmartListDto? dto)
        {
            if (mediaTypes == null || mediaTypes.Count == 0)
            {
                return new MediaTypesKey([], false);
            }

            // Deduplicate to ensure identical cache keys for equivalent content (e.g., ["Movie", "Movie"] = ["Movie"])
            var sortedTypes = mediaTypes.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();

            // Determine Collections expansion flag
            bool collectionsExpansionFlag = false;
            if (dto != null)
            {
                // Include Collections episode expansion in cache key to avoid incorrect caching
                // when same media types have different expansion settings
                var hasCollectionsExpansion = dto.ExpressionSets?.Any(set =>
                    set.Expressions?.Any(expr =>
                        expr.MemberName == "Collections" && expr.IncludeEpisodesWithinSeries == true) == true) == true;

                // Use boolean flag instead of string marker to distinguish caches with Collections expansion
                collectionsExpansionFlag = hasCollectionsExpansion && sortedTypes.Contains(MediaTypes.Episode) && !sortedTypes.Contains(MediaTypes.Series);
            }

            return new MediaTypesKey(sortedTypes, collectionsExpansionFlag);
        }

        public bool Equals(MediaTypesKey other)
        {
            // Handle null arrays (default struct case) and use SequenceEqual for cleaner comparison
            var thisArray = _sortedTypes ?? [];
            var otherArray = other._sortedTypes ?? [];

            return thisArray.AsSpan().SequenceEqual(otherArray.AsSpan()) &&
                   _hasCollectionsExpansion == other._hasCollectionsExpansion;
        }

        public override int GetHashCode()
        {
            // Handle null array (default struct case)
            var array = _sortedTypes ?? [];

            // Use HashCode.Combine for better distribution
            var hashCode = new HashCode();
            foreach (var item in array)
            {
                hashCode.Add(item, StringComparer.Ordinal);
            }
            hashCode.Add(_hasCollectionsExpansion);

            return hashCode.ToHashCode();
        }

        public override string ToString()
        {
            var array = _sortedTypes ?? [];
            var typesString = string.Join(",", array);
            return _hasCollectionsExpansion ? $"{typesString}[CollectionsExpansion]" : typesString;
        }
    }
}

