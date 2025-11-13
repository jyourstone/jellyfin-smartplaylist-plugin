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
                new PluginPageInfo
                {
                    Name = "config.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.js",
                }
            ];
        }
    }
}

