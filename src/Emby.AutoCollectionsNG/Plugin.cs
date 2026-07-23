using System;
using Emby.AutoCollectionsNG.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace Emby.AutoCollectionsNG
{
    /// <summary>
    /// Entry point for the Auto Collections NG plugin. Generated once; do not change <see cref="Id"/>
    /// after release, since Emby uses it to identify this plugin's stored configuration and data folder.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>
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
    }
}
