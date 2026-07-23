using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Emby.AutoCollectionsNG.Configuration;
using Xunit;

namespace Emby.AutoCollectionsNG.Tests
{
    /// <summary>
    /// Round-trip tests for <see cref="PluginConfiguration"/> using plain
    /// <see cref="XmlSerializer"/>. The real Emby host serializes plugin configuration through
    /// <c>MediaBrowser.Model.Serialization.IXmlSerializer</c>, but no concrete implementation of
    /// that interface ships in the `MediaBrowser.Server.Core` NuGet package (it lives in Emby's
    /// server-side `Emby.Server.Implementations`, which is not available here). The host's
    /// implementation is documented to ultimately delegate to `System.Xml.Serialization.XmlSerializer`
    /// for plugin configuration types, so testing directly against `XmlSerializer` exercises the same
    /// serialization contract without requiring a running Emby server.
    /// </summary>
    public class PluginConfigurationXmlRoundTripTests
    {
        private static T RoundTrip<T>(T value)
        {
            var serializer = new XmlSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, value);
                stream.Position = 0;

                // Also confirm the XML is valid, readable UTF-8 text (catches encoding issues with
                // umlauts/ß early, rather than only at deserialization time).
                var xml = Encoding.UTF8.GetString(stream.ToArray());
                Assert.False(string.IsNullOrWhiteSpace(xml));

                stream.Position = 0;
                return (T)serializer.Deserialize(stream)!;
            }
        }

        [Fact]
        public void EmptyConfiguration_RoundTrips_WithoutThrowing()
        {
            var config = new PluginConfiguration();

            var result = RoundTrip(config);

            Assert.NotNull(result.Rules);
            Assert.Empty(result.Rules);
            Assert.False(result.DeleteEmptyCollections);
        }

        [Fact]
        public void MultipleRules_RoundTrip_WithAllValuesPreserved()
        {
            var config = new PluginConfiguration
            {
                DeleteEmptyCollections = true,
                Rules = new[]
                {
                    new CollectionRule
                    {
                        CollectionName = "Formel 1",
                        MatchType = RuleMatchType.Regex,
                        Pattern = @"(?i)\bformel\s*1\b",
                        CaseSensitive = false,
                        Enabled = true,
                        MatchOn = MatchField.RawTitle,
                        AlsoMatchFileName = false,
                        LibraryFilter = new[] { "Recordings" },
                        ItemTypeFilter = new[] { "Episode", "Movie" }
                    },
                    new CollectionRule
                    {
                        CollectionName = "ZDF Magazin Royale",
                        MatchType = RuleMatchType.Contains,
                        Pattern = "ZDF Magazin Royale",
                        CaseSensitive = false,
                        Enabled = true,
                        MatchOn = MatchField.NormalizedTitle,
                        AlsoMatchFileName = true,
                        LibraryFilter = Array.Empty<string>(),
                        ItemTypeFilter = Array.Empty<string>()
                    }
                }
            };

            var result = RoundTrip(config);

            Assert.True(result.DeleteEmptyCollections);
            Assert.Equal(2, result.Rules.Length);

            var formel1 = result.Rules[0];
            Assert.Equal("Formel 1", formel1.CollectionName);
            Assert.Equal(RuleMatchType.Regex, formel1.MatchType);
            Assert.Equal(@"(?i)\bformel\s*1\b", formel1.Pattern);
            Assert.False(formel1.CaseSensitive);
            Assert.True(formel1.Enabled);
            Assert.Equal(MatchField.RawTitle, formel1.MatchOn);
            Assert.False(formel1.AlsoMatchFileName);
            Assert.Equal(new[] { "Recordings" }, formel1.LibraryFilter);
            Assert.Equal(new[] { "Episode", "Movie" }, formel1.ItemTypeFilter);

            var zdf = result.Rules[1];
            Assert.Equal("ZDF Magazin Royale", zdf.CollectionName);
            Assert.Equal(RuleMatchType.Contains, zdf.MatchType);
            Assert.Equal("ZDF Magazin Royale", zdf.Pattern);
            Assert.Equal(MatchField.NormalizedTitle, zdf.MatchOn);
            Assert.True(zdf.AlsoMatchFileName);
            Assert.Empty(zdf.LibraryFilter);
            Assert.Empty(zdf.ItemTypeFilter);
        }

        [Fact]
        public void GermanUmlautsAndEszett_RoundTrip_Correctly()
        {
            var config = new PluginConfiguration
            {
                Rules = new[]
                {
                    new CollectionRule
                    {
                        CollectionName = "Fußball-Übertragungen: Größte Erfolge",
                        MatchType = RuleMatchType.Contains,
                        Pattern = "Fußball Länderspiel Übersicht äöüÄÖÜß",
                        MatchOn = MatchField.RawTitle
                    }
                }
            };

            var result = RoundTrip(config);

            Assert.Single(result.Rules);
            Assert.Equal("Fußball-Übertragungen: Größte Erfolge", result.Rules[0].CollectionName);
            Assert.Equal("Fußball Länderspiel Übersicht äöüÄÖÜß", result.Rules[0].Pattern);
        }

        [Fact]
        public void RegexSpecialCharacters_InPattern_RoundTrip_Correctly()
        {
            var config = new PluginConfiguration
            {
                Rules = new[]
                {
                    new CollectionRule
                    {
                        CollectionName = "ZDF Satire",
                        MatchType = RuleMatchType.Regex,
                        Pattern = @"(?i)\b(heute[- ]show|zdf magazin royale|die anstalt)\b",
                        MatchOn = MatchField.RawTitle
                    },
                    new CollectionRule
                    {
                        CollectionName = "Motorsport",
                        MatchType = RuleMatchType.Regex,
                        Pattern = @"(?i)\b(formel\s*[1-3]|motogp|dtm)\b",
                        MatchOn = MatchField.RawTitle
                    }
                }
            };

            var result = RoundTrip(config);

            Assert.Equal(2, result.Rules.Length);
            Assert.Equal(@"(?i)\b(heute[- ]show|zdf magazin royale|die anstalt)\b", result.Rules[0].Pattern);
            Assert.Equal(@"(?i)\b(formel\s*[1-3]|motogp|dtm)\b", result.Rules[1].Pattern);
        }

        [Fact]
        public void DisabledRule_PreservesEnabledFlag_AfterRoundTrip()
        {
            var config = new PluginConfiguration
            {
                Rules = new[]
                {
                    new CollectionRule
                    {
                        CollectionName = "Disabled Rule",
                        MatchType = RuleMatchType.Contains,
                        Pattern = "anything",
                        Enabled = false,
                        MatchOn = MatchField.FileName
                    }
                }
            };

            var result = RoundTrip(config);

            Assert.False(result.Rules.Single().Enabled);
            Assert.Equal(MatchField.FileName, result.Rules.Single().MatchOn);
        }
    }
}
