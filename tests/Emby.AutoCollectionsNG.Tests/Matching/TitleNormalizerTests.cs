using Emby.AutoCollectionsNG.Matching;
using Xunit;

namespace Emby.AutoCollectionsNG.Tests.Matching
{
    /// <summary>
    /// Tests for <see cref="TitleNormalizer"/>, using the example recording titles from the epic
    /// (issue #1) plus regression cases for titles that don't start with a date/time prefix.
    /// </summary>
    public class TitleNormalizerTests
    {
        [Theory]
        [InlineData(
            "20260705 1742 - ORF 1 HD - Formel 1 Großer Preis von Großbritannien 2026",
            "Formel 1 Großer Preis von Großbritannien 2026")]
        [InlineData(
            "20260719 2142 - ZDF neo HD - heute-show extra - Das Quiz",
            "heute-show extra - Das Quiz")]
        [InlineData(
            "20260627 0012 - ZDF HD - ZDF Magazin Royale",
            "ZDF Magazin Royale")]
        public void Normalize_StripsDatePrefixAndChannelSegment(string rawTitle, string expected)
        {
            Assert.Equal(expected, TitleNormalizer.Normalize(rawTitle));
        }

        [Fact]
        public void Normalize_HyphenInsideChannelStrippedSegment_DoesNotBreakChannelStripping()
        {
            // The channel-stripping step must look for " - " (space-dash-space), not any hyphen,
            // so an internal hyphen inside the surviving title (e.g. "heute-show") is preserved.
            var result = TitleNormalizer.Normalize("20260719 2142 - ZDF neo HD - heute-show extra - Das Quiz");

            Assert.Contains("heute-show", result);
            Assert.Equal("heute-show extra - Das Quiz", result);
        }

        [Fact]
        public void Normalize_TitleWithoutDatePrefix_PassesThroughUnchanged()
        {
            // Regression case: a real title that happens to contain a hyphen must not be mangled
            // just because it superficially resembles a "prefix - segment" shape somewhere in it.
            const string title = "Spider-Man: No Way Home";

            Assert.Equal(title, TitleNormalizer.Normalize(title));
        }

        [Fact]
        public void Normalize_TitleWithoutDatePrefixAndWithChannelLikeDash_KeepsEverything()
        {
            // No leading 8-digit-date + 4-digit-time prefix at all -> nothing gets stripped, even
            // though the string contains a " - " sequence that could look like a channel separator.
            const string title = "Die Sendung - Der Film";

            Assert.Equal(title, TitleNormalizer.Normalize(title));
        }

        [Fact]
        public void Normalize_GermanUmlautsAndEszett_ArePreserved()
        {
            const string title = "20260101 2000 - ARD HD - Fußball: Länderspiel Übertragung äöüÄÖÜß";

            var result = TitleNormalizer.Normalize(title);

            Assert.Equal("Fußball: Länderspiel Übertragung äöüÄÖÜß", result);
        }

        [Fact]
        public void Normalize_CollapsesRunsOfWhitespace()
        {
            const string title = "20260101 2000 - ARD   HD -   Title   With   Extra   Spaces  ";

            var result = TitleNormalizer.Normalize(title);

            Assert.Equal("Title With Extra Spaces", result);
        }

        [Fact]
        public void Normalize_DatePrefixWithoutChannelSegment_OnlyStripsDatePrefix()
        {
            // Date prefix present, but no further " - " separated channel segment follows.
            const string title = "20260101 2000 - Just A Title Without Channel Separator";

            var result = TitleNormalizer.Normalize(title);

            Assert.Equal("Just A Title Without Channel Separator", result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Normalize_NullOrEmptyInput_DoesNotThrow(string input)
        {
            var result = TitleNormalizer.Normalize(input);

            Assert.True(string.IsNullOrEmpty(result));
        }
    }
}
