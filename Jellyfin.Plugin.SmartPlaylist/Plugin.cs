using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SmartPlaylist.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartPlaylist
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) 
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public override Guid Id => Guid.Parse("A0A2A7B2-747A-4113-8B39-757A9D267C79");
        public override string Name => "SmartPlaylist";
        public override string Description => "A rebuilt and modernized plugin to create smart, rule-based playlists in Jellyfin.";

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static Plugin Instance { get; private set; }

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
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.html"
                },
                new PluginPageInfo
                {
                    Name = "config.js",
                    EmbeddedResourcePath = GetType().Namespace + ".Configuration.config.js"
                }
            ];
        }
    }
}