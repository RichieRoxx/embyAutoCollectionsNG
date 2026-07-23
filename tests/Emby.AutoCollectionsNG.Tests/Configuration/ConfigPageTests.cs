using System;
using System.IO;
using System.Linq;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Xunit;

namespace Emby.AutoCollectionsNG.Tests.Configuration
{
    /// <summary>
    /// Tests for the plugin's configuration UI registration (issue #9): <see cref="Plugin"/>
    /// implementing <c>MediaBrowser.Model.Plugins.IHasWebPages</c> and the embedded HTML page it
    /// points at.
    ///
    /// These tests can only mechanically verify what's checkable without a live Emby server: that
    /// <see cref="Plugin.GetPages"/> returns a well-formed <see cref="PluginPageInfo"/>, and that
    /// the HTML file it names as <see cref="PluginPageInfo.EmbeddedResourcePath"/> is actually
    /// embedded in the compiled assembly and retrievable under that exact resource name. They do
    /// NOT and cannot verify that the Emby dashboard actually loads/renders this page at runtime,
    /// or that the page's JS API calls (ApiClient.getPluginConfiguration etc.) work against a real
    /// host - see the comment block at the top of Configuration/configPage.html for that list of
    /// genuinely unverified assumptions.
    /// </summary>
    public class ConfigPageTests : IDisposable
    {
        private readonly string _tempRoot;

        public ConfigPageTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "acng-plugin-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup only; not test-critical.
            }
        }

        private Plugin CreatePlugin()
        {
            return new Plugin(new FakeApplicationPaths(_tempRoot), new FakeXmlSerializer());
        }

        [Fact]
        public void GetPages_ReturnsExactlyOneMainConfigPage_WithNonEmptyResourcePath()
        {
            var plugin = CreatePlugin();

            var pages = plugin.GetPages().ToList();

            var mainPages = pages.Where(p => p.IsMainConfigPage).ToList();
            Assert.Single(mainPages);

            var page = mainPages[0];
            Assert.False(string.IsNullOrWhiteSpace(page.EmbeddedResourcePath));
            Assert.False(string.IsNullOrWhiteSpace(page.Name));
            Assert.False(string.IsNullOrWhiteSpace(page.DisplayName));
        }

        [Fact]
        public void ConfigPage_EmbeddedResource_ExistsInCompiledAssembly_AndIsReadableNonEmptyHtml()
        {
            var plugin = CreatePlugin();
            var page = plugin.GetPages().Single(p => p.IsMainConfigPage);

            var assembly = typeof(Plugin).Assembly;
            var resourceNames = assembly.GetManifestResourceNames();

            // The core check for this issue: the resource name PluginPageInfo promises must
            // actually exist in the compiled assembly's manifest, or the dashboard would 404/fail
            // to load the page even if everything else about the mechanism worked.
            Assert.Contains(page.EmbeddedResourcePath, resourceNames);

            using (var stream = assembly.GetManifestResourceStream(page.EmbeddedResourcePath))
            {
                Assert.NotNull(stream);
                Assert.True(stream.Length > 0);

                using (var reader = new StreamReader(stream))
                {
                    var html = reader.ReadToEnd();

                    Assert.False(string.IsNullOrWhiteSpace(html));
                    // Sanity checks that this is really the rule-editor page and not some other
                    // accidental embedded resource under the same path.
                    Assert.Contains("acngSaveBtn", html, StringComparison.Ordinal);
                    Assert.Contains("acngSyncBtn", html, StringComparison.Ordinal);

                    // Rendering-contract checks (verified against a live Emby 4.9 server): the view
                    // manager selects the page via querySelector('.view, div[data-role="page"]') and
                    // loads the logic from the referenced controller module. If either of these
                    // regresses, the page silently fails to open - which is the exact bug this
                    // structure fixes - so lock them in.
                    Assert.Contains("class=\"view\"", html, StringComparison.Ordinal);
                    Assert.Contains("data-controller=\"__plugin/autocollectionsngjs\"", html, StringComparison.Ordinal);
                    // The logic must NOT be an inline <script> (innerHTML-inserted scripts never run).
                    Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        [Fact]
        public void ConfigPage_ControllerJsResource_IsRegistered_Embedded_AndReferencedByHtml()
        {
            var plugin = CreatePlugin();
            var pages = plugin.GetPages().ToList();

            // The HTML's data-controller name must be registered as its own page, or the dashboard
            // route /emby/web/ConfigurationPage?name=autocollectionsngjs (which only serves
            // registered resources) can't hand the browser the controller module.
            var jsPage = pages.Single(p => p.Name == "autocollectionsngjs");
            Assert.False(jsPage.IsMainConfigPage);
            Assert.Equal("Emby.AutoCollectionsNG.Configuration.configPage.js", jsPage.EmbeddedResourcePath);

            var assembly = typeof(Plugin).Assembly;
            Assert.Contains(jsPage.EmbeddedResourcePath, assembly.GetManifestResourceNames());

            using (var stream = assembly.GetManifestResourceStream(jsPage.EmbeddedResourcePath))
            {
                Assert.NotNull(stream);
                using (var reader = new StreamReader(stream))
                {
                    var js = reader.ReadToEnd();
                    Assert.False(string.IsNullOrWhiteSpace(js));
                    // Confirms this is the AMD controller module in the shape Emby require()s.
                    Assert.Contains("define(", js, StringComparison.Ordinal);
                    Assert.Contains("BaseView", js, StringComparison.Ordinal);
                    Assert.Contains("return View", js, StringComparison.Ordinal);
                    Assert.Contains("ApiClient", js, StringComparison.Ordinal);
                }
            }
        }

        [Fact]
        public void ConfigPage_EmbeddedResourcePath_MatchesDotNetDefaultManifestNamingConvention()
        {
            // Documents/locks in the convention the csproj comment and Plugin.cs rely on:
            // RootNamespace + folder path + file name, dot-separated.
            var plugin = CreatePlugin();
            var page = plugin.GetPages().Single(p => p.IsMainConfigPage);

            Assert.Equal("Emby.AutoCollectionsNG.Configuration.configPage.html", page.EmbeddedResourcePath);
        }

        /// <summary>
        /// Minimal hand-written stub for <see cref="IApplicationPaths"/>. Deliberately not a Moq
        /// mock: the interface declares <c>GetImageCachePath()</c>/<c>GetCachePath()</c> returning
        /// <see cref="ReadOnlySpan{T}"/> (a ref struct), which dynamic-proxy-based mocking
        /// frameworks generally cannot intercept/generate proxies for. A plain implementation
        /// sidesteps that entirely.
        /// </summary>
        private sealed class FakeApplicationPaths : IApplicationPaths
        {
            public FakeApplicationPaths(string root)
            {
                ProgramDataPath = root;
                ProgramSystemPath = root;
                DataPath = root;
                VirtualDataPath = root;
                ImageCachePath = root;
                PluginsPath = root;
                PluginConfigurationsPath = root;
                TempUpdatePath = root;
                LogDirectoryPath = root;
                ConfigurationDirectoryPath = root;
                SystemConfigurationFilePath = Path.Combine(root, "system.xml");
                CachePath = root;
                TempDirectory = root;
            }

            public string ProgramDataPath { get; }
            public string ProgramSystemPath { get; }
            public string DataPath { get; }
            public string VirtualDataPath { get; }
            public string ImageCachePath { get; }
            public string PluginsPath { get; }
            public string PluginConfigurationsPath { get; }
            public string TempUpdatePath { get; }
            public string LogDirectoryPath { get; }
            public string ConfigurationDirectoryPath { get; }
            public string SystemConfigurationFilePath { get; }
            public string CachePath { get; }
            public string TempDirectory { get; }

            public ReadOnlySpan<char> GetImageCachePath() => ImageCachePath.AsSpan();

            public ReadOnlySpan<char> GetCachePath() => CachePath.AsSpan();

#pragma warning disable CS0067 // Never raised: this stub never changes its (fixed) cache path.
            public event EventHandler CachePathChanged = delegate { };
#pragma warning restore CS0067
        }

        /// <summary>
        /// Minimal hand-written stub for <see cref="IXmlSerializer"/> so <see cref="Plugin"/> can
        /// be constructed without a real Emby host. Deserialize* calls return a fresh default
        /// instance of the requested type (mirroring "no config file yet -> first-run defaults");
        /// Serialize* calls are no-ops. Nothing under test here depends on round-trip fidelity of
        /// this stub - that's already covered separately by PluginConfigurationXmlRoundTripTests
        /// against the real <see cref="System.Xml.Serialization.XmlSerializer"/>.
        /// </summary>
        private sealed class FakeXmlSerializer : IXmlSerializer
        {
            public object DeserializeFromFile(Type type, string file) => Activator.CreateInstance(type)!;

            public object DeserializeFromStream(Type type, Stream stream) => Activator.CreateInstance(type)!;

            public object DeserializeFromBytes(Type type, byte[] buffer) => Activator.CreateInstance(type)!;

            public object DeserializeFromString(string text, Type type) => Activator.CreateInstance(type)!;

            public void SerializeToStream(object obj, Stream stream)
            {
            }

            public void SerializeToFile(object obj, string file)
            {
            }

            public string SerializeToString(object obj) => string.Empty;
        }
    }
}
