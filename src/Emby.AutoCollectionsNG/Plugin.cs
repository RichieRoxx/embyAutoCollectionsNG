using System;
using System.Collections.Generic;
using Emby.AutoCollectionsNG.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Emby.AutoCollectionsNG
{
    /// <summary>
    /// Entry point for the Auto Collections NG plugin. Generated once; do not change <see cref="Id"/>
    /// after release, since Emby uses it to identify this plugin's stored configuration and data folder.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static Plugin Instance { get; private set; }

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public override string Name => "Auto Collections NG";

        public override string Description =>
            "Automatically maintains collections from title rules (regex/contains), primarily for DVR recordings.";

        public override Guid Id => new Guid("29df6479-d417-492c-8396-1b6a4bca7bb0");

        /// <summary>
        /// Registers the plugin's hand-authored HTML/JS configuration page (issue #9) with the
        /// Emby dashboard. <see cref="MediaBrowser.Model.Plugins.IHasWebPages"/> and
        /// <see cref="PluginPageInfo"/> are confirmed via reflection against
        /// mediabrowser.server.core 4.9.1.90 (see docs/emby-api-cheatsheet.md, "Configuration UI").
        ///
        /// UNCERTAIN (no live Emby server available to confirm - see the comment block at the top
        /// of Configuration/configPage.html for the full list): whether the dashboard actually
        /// discovers and loads this page from the exact embedded-resource path below at runtime,
        /// and whether the page's JS `ApiClient` calls (plugin-configuration read/write, scheduled
        /// task listing/start) behave the way the script assumes. What IS verified here is purely
        /// static: this method compiles against the real SDK types, and
        /// ConfigPageEmbeddedResourceTests confirms the HTML is actually embedded and retrievable
        /// from the compiled assembly under this exact resource name.
        /// </summary>
        public IEnumerable<PluginPageInfo> GetPages()
        {
            yield return new PluginPageInfo
            {
                Name = "AutoCollectionsNGConfigPage",
                DisplayName = Name,
                // Default .NET embedded-resource manifest name: RootNamespace ("Emby.AutoCollectionsNG",
                // see the csproj) + folder path + file name, each separator replaced with '.'.
                EmbeddedResourcePath = "Emby.AutoCollectionsNG.Configuration.configPage.html",
                IsMainConfigPage = true,
                EnableInMainMenu = true
            };
        }
    }
}
