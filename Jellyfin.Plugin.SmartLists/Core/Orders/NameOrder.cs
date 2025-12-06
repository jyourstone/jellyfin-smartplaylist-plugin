using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.SmartLists.Core;
using Jellyfin.Plugin.SmartLists.Services.Shared;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartLists.Core.Orders
{
    /// <summary>
    /// Helper class for shared name sorting logic.
    /// </summary>
    internal static class NameSortHelper
    {
        // Compiled regex patterns for performance - avoids regex compilation overhead on every sort operation
        private static readonly Regex AudioAutoSortPattern = new(@"^\d+\s+-\s+", RegexOptions.Compiled);
        private static readonly Regex EpisodeAutoSortPattern = new(@"^\d{3,} - \d{4,} - ", RegexOptions.Compiled);

        /// <summary>
        /// Gets the sort value for name-based sorting, handling auto-generated SortName patterns.
        /// </summary>
        internal static string GetNameSortValue(BaseItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            
            // For Audio items, SortName is often auto-generated as "0001 - Track Title" (track number - title).
            // If the user selected "Name", they likely want alphabetical by Title.
            // However, if the user MANUALLY set a SortName, we should respect it.
            // We detect auto-generated SortName by pattern: digits, space, hyphen, space.
            if (item is Audio && !string.IsNullOrEmpty(item.SortName) &&
                AudioAutoSortPattern.IsMatch(item.SortName))
            {
                return item.Name ?? "";
            }
            
            // For Episodes, SortName is auto-generated as "001 - 0001 - Title", which forces chronological sort.
            // If the user selected "Name", they likely want alphabetical by Title.
            // However, if the user MANUALLY set a SortName (e.g. "A"), we should respect it.
            // We detect auto-generated SortName by pattern: 3+ digits, hyphen, 4+ digits, hyphen.
            if (item is Episode && !string.IsNullOrEmpty(item.SortName) &&
                EpisodeAutoSortPattern.IsMatch(item.SortName))
            {
                return item.Name ?? "";
            }
            
            // Use SortName if set, otherwise fall back to Name
            return !string.IsNullOrEmpty(item.SortName) ? item.SortName : (item.Name ?? "");
        }
    }

    public class NameOrder : PropertyOrder<string>
    {
        public override string Name => "Name Ascending";
        protected override bool IsDescending => false;
        protected override IComparer<string> Comparer => OrderUtilities.SharedNaturalComparer;

        protected override string GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            return NameSortHelper.GetNameSortValue(item);
        }
    }

    public class NameOrderDesc : PropertyOrder<string>
    {
        public override string Name => "Name Descending";
        protected override bool IsDescending => true;
        protected override IComparer<string> Comparer => OrderUtilities.SharedNaturalComparer;

        protected override string GetSortValue(BaseItem item, User? user = null, IUserDataManager? userDataManager = null, ILogger? logger = null, RefreshQueueService.RefreshCache? refreshCache = null)
        {
            return NameSortHelper.GetNameSortValue(item);
        }
    }
}

