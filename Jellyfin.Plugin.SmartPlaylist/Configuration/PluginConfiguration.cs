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
    }
} 