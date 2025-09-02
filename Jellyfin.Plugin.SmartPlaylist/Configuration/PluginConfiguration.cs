using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SmartPlaylist.Configuration
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
        /// Gets or sets whether new playlists should be public by default.
        /// </summary>
        public bool DefaultMakePublic { get; set; } = false;

        /// <summary>
        /// Gets or sets the default maximum number of items for new playlists.
        /// </summary>
        public int DefaultMaxItems { get; set; } = 500;

        /// <summary>
        /// Gets or sets the default maximum play time in minutes for new playlists.
        /// </summary>
        public int DefaultMaxPlayTimeMinutes { get; set; } = 0;

        /// <summary>
        /// Gets or sets the prefix text to add to playlist names.
        /// Leave empty to not add a prefix.
        /// </summary>
        public string PlaylistNamePrefix { get; set; } = "";

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
        /// Gets or sets the default scheduled refresh setting for new playlists.
        /// </summary>
        public bool DefaultRefreshOnSchedule { get; set; } = false;
    }
} 