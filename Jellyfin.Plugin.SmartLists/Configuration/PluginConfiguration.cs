using System;
using Jellyfin.Plugin.SmartLists.Core.Enums;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SmartLists.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Gets or sets the default sort order for new playlists.
        /// </summary>
        public string DefaultSortBy { get; set; } = "Name";

        /// <summary>
        /// Gets or sets the default sort direction for new playlists.
        /// </summary>
        public string DefaultSortOrder { get; set; } = "Ascending";

        /// <summary>
        /// Gets or sets the default list type for new lists (Playlist or Collection).
        /// </summary>
        public string DefaultListType { get; set; } = "Playlist";

        /// <summary>
        /// Gets or sets whether new playlists should be public by default.
        /// </summary>
        public bool DefaultMakePublic { get; set; } = false;

        /// <summary>
        /// Gets or sets the default maximum number of items for new playlists.
        /// </summary>
        public int DefaultMaxItems { get; set; } = 500;

        /// <summary>
        /// Gets or sets the default maximum playtime in minutes for new playlists.
        /// </summary>
        public int DefaultMaxPlayTimeMinutes { get; set; } = 0;

        /// <summary>
        /// Gets or sets the prefix text to add to playlist names.
        /// Leave empty to not add a prefix.
        /// </summary>
        public string PlaylistNamePrefix { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the suffix text to add to playlist names.
        /// Leave empty to not add a suffix.
        /// </summary>
        public string PlaylistNameSuffix { get; set; } = "[Smart]";

        /// <summary>
        /// Gets or sets the default auto-refresh mode for new playlists.
        /// </summary>
        public AutoRefreshMode DefaultAutoRefresh { get; set; } = AutoRefreshMode.OnLibraryChanges;

        /// <summary>
        /// Gets or sets the default schedule trigger for new playlists.
        /// </summary>
        public ScheduleTrigger? DefaultScheduleTrigger { get; set; } = null; // No schedule by default

        /// <summary>
        /// Gets or sets the default schedule time for Daily/Weekly triggers.
        /// </summary>
        public TimeSpan DefaultScheduleTime { get; set; } = TimeSpan.FromHours(0); // Midnight (00:00) default

        /// <summary>
        /// Gets or sets the default day of week for Weekly triggers.
        /// </summary>
        public DayOfWeek DefaultScheduleDayOfWeek { get; set; } = DayOfWeek.Sunday; // Sunday default

        /// <summary>
        /// Gets or sets the default day of month for Monthly/Yearly triggers.
        /// </summary>
        public int DefaultScheduleDayOfMonth { get; set; } = 1; // 1st of month default

        /// <summary>
        /// Gets or sets the default month for Yearly triggers.
        /// </summary>
        public int DefaultScheduleMonth { get; set; } = 1; // January default

        /// <summary>
        /// Gets or sets the default interval for Interval triggers.
        /// </summary>
        public TimeSpan DefaultScheduleInterval { get; set; } = TimeSpan.FromMinutes(15); // 15 minutes default

        /// <summary>
        /// Gets or sets the parallel concurrency limit for playlist refreshes and database queries.
        /// 0 or negative = Auto (uses ProcessorCount - 3 for 4+ cores, sequential for 1-3 cores)
        /// 1 = Force sequential processing (no parallelism)
        /// 2+ = Use specified number of parallel threads
        /// Default: 0 (Auto)
        /// </summary>
        public int ParallelConcurrencyLimit { get; set; } = 0;
    }
}