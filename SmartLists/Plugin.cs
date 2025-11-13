using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SmartLists.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SmartLists
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Plugin class name is required by Jellyfin plugin system")]
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public override Guid Id => Guid.Parse("A0A2A7B2-747A-4113-8B39-757A9D267C79");
        public override string Name => "SmartLists";
        public override string Description => "Create smart, rule-based playlists and collections in Jellyfin.";

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static Plugin? Instance { get; private set; }

        /// <summary>
        /// Gets the plugin's web pages.
        /// </summary>
        /// <returns>The web pages.</returns>
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return [
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.html",
                },
                // Core utilities and constants (must load first)
                new PluginPageInfo
                {
                    Name = "config-core.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-core.js",
                },
                // Formatters and option generators
                new PluginPageInfo
                {
                    Name = "config-formatters.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-formatters.js",
                },
                // Schedule management
                new PluginPageInfo
                {
                    Name = "config-schedules.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-schedules.js",
                },
                // Sort management
                new PluginPageInfo
                {
                    Name = "config-sorts.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-sorts.js",
                },
                // Rule management
                new PluginPageInfo
                {
                    Name = "config-rules.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-rules.js",
                },
                // Playlist CRUD operations
                new PluginPageInfo
                {
                    Name = "config-playlists.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-playlists.js",
                },
                // Filtering and search
                new PluginPageInfo
                {
                    Name = "config-filters.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-filters.js",
                },
                // Bulk actions
                new PluginPageInfo
                {
                    Name = "config-bulk-actions.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-bulk-actions.js",
                },
                // API calls
                new PluginPageInfo
                {
                    Name = "config-api.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-api.js",
                },
                // Initialization (must load last)
                new PluginPageInfo
                {
                    Name = "config-init.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config-init.js",
                },
                // Legacy config.js (kept for backward compatibility, but should be empty or minimal)
                new PluginPageInfo
                {
                    Name = "config.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.js",
                }
            ];
        }
    }
}

