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
        /// Registers the plugin's hand-authored configuration page (issue #9) with the Emby
        /// dashboard. <see cref="MediaBrowser.Model.Plugins.IHasWebPages"/> and
        /// <see cref="PluginPageInfo"/> are confirmed via reflection against
        /// mediabrowser.server.core 4.9.1.90 (see docs/emby-api-cheatsheet.md, "Configuration UI").
        ///
        /// Two resources are registered, mirroring how the shipped Emby plugins structure their
        /// config pages (verified against a live Emby 4.9 server on 2026-07-23):
        ///   1. The HTML page (main config page, shown in the dashboard menu). Its root element
        ///      carries <c>class="view"</c> and <c>data-controller="__plugin/autocollectionsngjs"</c>.
        ///   2. The controller JS module the HTML references by that name. The dashboard resolves
        ///      <c>__plugin/autocollectionsngjs</c> to <c>/emby/web/ConfigurationPage?name=autocollectionsngjs</c>,
        ///      which only serves resources registered here - hence this second entry. The
        ///      <see cref="PluginPageInfo.Name"/> MUST match the data-controller name in the HTML.
        ///
        /// Why both: the view manager loads the HTML via <c>innerHTML</c> (so an inline &lt;script&gt;
        /// would never execute) and requires the view root to match
        /// <c>querySelector('.view, div[data-role="page"]')</c>. See the comment block at the top of
        /// configPage.html for the full rendering contract.
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

            yield return new PluginPageInfo
            {
                // MUST match data-controller="__plugin/autocollectionsngjs" in configPage.html.
                Name = "autocollectionsngjs",
                EmbeddedResourcePath = "Emby.AutoCollectionsNG.Configuration.configPage.js",
                // This resource is the controller JS, not a page in its own right. IsMainConfigPage
                // and EnableInMainMenu must be set false EXPLICITLY: in this SDK version
                // PluginPageInfo.IsMainConfigPage defaults to true, so leaving it unset would make
                // the dashboard treat the .js file as a second main config page / menu entry.
                IsMainConfigPage = false,
                EnableInMainMenu = false
            };
        }
    }
}
