using Emby.AutoCollectionsNG.Configuration;
using Emby.AutoCollectionsNG.Matching;
using Xunit;

namespace Emby.AutoCollectionsNG.Tests.Matching
{
    /// <summary>
    /// Tests for <see cref="RuleMatcher"/>, including the five example rules documented in
    /// docs/example-config.md evaluated against the three example recording titles from the epic
    /// (issue #1).
    /// </summary>
    public class RuleMatcherTests
    {
        // The three example raw titles from the epic (issue #1).
        private const string Formel1Title =
            "20260705 1742 - ORF 1 HD - Formel 1 Großer Preis von Großbritannien 2026";

        private const string HeuteShowTitle =
            "20260719 2142 - ZDF neo HD - heute-show extra - Das Quiz";

        private const string ZdfMagazinRoyaleTitle =
            "20260627 0012 - ZDF HD - ZDF Magazin Royale";

        // The five example rules from docs/example-config.md.
        private static CollectionRule Formel1Rule() => new CollectionRule
        {
            CollectionName = "Formel 1",
            MatchType = RuleMatchType.Regex,
            Pattern = @"(?i)\bformel\s*1\b",
            CaseSensitive = false,
            Enabled = true,
            MatchOn = MatchField.RawTitle
        };

        private static CollectionRule HeuteShowRule() => new CollectionRule
        {
            CollectionName = "heute-show",
            MatchType = RuleMatchType.Regex,
            Pattern = @"(?i)\bheute[- ]show\b",
            CaseSensitive = false,
            Enabled = true,
            MatchOn = MatchField.RawTitle
        };

        private static CollectionRule ZdfMagazinRoyaleRule() => new CollectionRule
        {
            CollectionName = "ZDF Magazin Royale",
            MatchType = RuleMatchType.Contains,
            Pattern = "ZDF Magazin Royale",
            CaseSensitive = false,
            Enabled = true,
            MatchOn = MatchField.RawTitle
        };

        private static CollectionRule ZdfSatireRule() => new CollectionRule
        {
            CollectionName = "ZDF Satire",
            MatchType = RuleMatchType.Regex,
            Pattern = @"(?i)\b(heute[- ]show|zdf magazin royale|die anstalt)\b",
            CaseSensitive = false,
            Enabled = true,
            MatchOn = MatchField.RawTitle
        };

        private static CollectionRule MotorsportRule() => new CollectionRule
        {
            CollectionName = "Motorsport",
            MatchType = RuleMatchType.Regex,
            Pattern = @"(?i)\b(formel\s*[1-3]|motogp|dtm)\b",
            CaseSensitive = false,
            Enabled = true,
            MatchOn = MatchField.RawTitle
        };

        [Fact]
        public void Formel1Rule_MatchesOnlyFormel1Title()
        {
            var matcher = new RuleMatcher();
            var rule = Formel1Rule();

            Assert.True(matcher.IsMatch(rule, Formel1Title, TitleNormalizer.Normalize(Formel1Title), null));
            Assert.False(matcher.IsMatch(rule, HeuteShowTitle, TitleNormalizer.Normalize(HeuteShowTitle), null));
            Assert.False(matcher.IsMatch(rule, ZdfMagazinRoyaleTitle, TitleNormalizer.Normalize(ZdfMagazinRoyaleTitle), null));
        }

        [Fact]
        public void HeuteShowRule_MatchesOnlyHeuteShowTitle()
        {
            var matcher = new RuleMatcher();
            var rule = HeuteShowRule();

            Assert.False(matcher.IsMatch(rule, Formel1Title, TitleNormalizer.Normalize(Formel1Title), null));
            Assert.True(matcher.IsMatch(rule, HeuteShowTitle, TitleNormalizer.Normalize(HeuteShowTitle), null));
            Assert.False(matcher.IsMatch(rule, ZdfMagazinRoyaleTitle, TitleNormalizer.Normalize(ZdfMagazinRoyaleTitle), null));
        }

        [Fact]
        public void ZdfMagazinRoyaleRule_MatchesOnlyZdfMagazinRoyaleTitle()
        {
            var matcher = new RuleMatcher();
            var rule = ZdfMagazinRoyaleRule();

            Assert.False(matcher.IsMatch(rule, Formel1Title, TitleNormalizer.Normalize(Formel1Title), null));
            Assert.False(matcher.IsMatch(rule, HeuteShowTitle, TitleNormalizer.Normalize(HeuteShowTitle), null));
            Assert.True(matcher.IsMatch(rule, ZdfMagazinRoyaleTitle, TitleNormalizer.Normalize(ZdfMagazinRoyaleTitle), null));
        }

        [Fact]
        public void ZdfSatireRule_MatchesHeuteShowAndZdfMagazinRoyale_ButNotFormel1()
        {
            var matcher = new RuleMatcher();
            var rule = ZdfSatireRule();

            Assert.False(matcher.IsMatch(rule, Formel1Title, TitleNormalizer.Normalize(Formel1Title), null));
            Assert.True(matcher.IsMatch(rule, HeuteShowTitle, TitleNormalizer.Normalize(HeuteShowTitle), null));
            Assert.True(matcher.IsMatch(rule, ZdfMagazinRoyaleTitle, TitleNormalizer.Normalize(ZdfMagazinRoyaleTitle), null));
        }

        [Fact]
        public void MotorsportRule_MatchesOnlyFormel1Title()
        {
            var matcher = new RuleMatcher();
            var rule = MotorsportRule();

            Assert.True(matcher.IsMatch(rule, Formel1Title, TitleNormalizer.Normalize(Formel1Title), null));
            Assert.False(matcher.IsMatch(rule, HeuteShowTitle, TitleNormalizer.Normalize(HeuteShowTitle), null));
            Assert.False(matcher.IsMatch(rule, ZdfMagazinRoyaleTitle, TitleNormalizer.Normalize(ZdfMagazinRoyaleTitle), null));
        }

        [Fact]
        public void Formel1Rule_AlsoMatchesAgainstNormalizedTitle_WhenMatchOnIsNormalizedTitle()
        {
            var matcher = new RuleMatcher();
            var rule = Formel1Rule();
            rule.MatchOn = MatchField.NormalizedTitle;

            var normalized = TitleNormalizer.Normalize(Formel1Title);

            Assert.True(matcher.IsMatch(rule, Formel1Title, normalized, null));
        }

        [Fact]
        public void GermanUmlautsAndEszett_MatchCorrectly_ForContainsAndRegex()
        {
            var matcher = new RuleMatcher();
            const string title = "Fußball: Länderspiel Übertragung äöüÄÖÜß";

            var containsRule = new CollectionRule
            {
                MatchType = RuleMatchType.Contains,
                Pattern = "Länderspiel",
                CaseSensitive = false,
                MatchOn = MatchField.RawTitle
            };
            Assert.True(matcher.IsMatch(containsRule, title, title, null));

            var regexRule = new CollectionRule
            {
                MatchType = RuleMatchType.Regex,
                Pattern = @"Fußball.*äöüÄÖÜß",
                CaseSensitive = false,
                MatchOn = MatchField.RawTitle
            };
            Assert.True(matcher.IsMatch(regexRule, title, title, null));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Contains_RespectsCaseSensitivity(bool caseSensitive)
        {
            var matcher = new RuleMatcher();
            var rule = new CollectionRule
            {
                MatchType = RuleMatchType.Contains,
                Pattern = "formel 1",
                CaseSensitive = caseSensitive,
                MatchOn = MatchField.RawTitle
            };

            var result = matcher.IsMatch(rule, Formel1Title, Formel1Title, null);

            // Formel1Title contains "Formel 1" (capitalized), not the lowercase "formel 1".
            Assert.Equal(!caseSensitive, result);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Regex_RespectsCaseSensitivity(bool caseSensitive)
        {
            var matcher = new RuleMatcher();
            var rule = new CollectionRule
            {
                MatchType = RuleMatchType.Regex,
                Pattern = @"\bformel\s*1\b",
                CaseSensitive = caseSensitive,
                MatchOn = MatchField.RawTitle
            };

            var result = matcher.IsMatch(rule, Formel1Title, Formel1Title, null);

            // Pattern has no (?i) inline flag here, so case sensitivity is governed purely by
            // CaseSensitive/RegexOptions.IgnoreCase.
            Assert.Equal(!caseSensitive, result);
        }

        [Fact]
        public void AlsoMatchFileName_FallsBackToFileName_WhenPrimaryFieldDoesNotMatch()
        {
            var matcher = new RuleMatcher();
            var rule = new CollectionRule
            {
                MatchType = RuleMatchType.Contains,
                Pattern = "Formel",
                CaseSensitive = false,
                MatchOn = MatchField.RawTitle,
                AlsoMatchFileName = true
            };

            // Title doesn't contain the pattern, but the file name does.
            var result = matcher.IsMatch(rule, "Some Unrelated Title", "Some Unrelated Title", "Formel1_2026.ts");

            Assert.True(result);
        }

        [Fact]
        public void AlsoMatchFileName_False_DoesNotFallBackToFileName()
        {
            var matcher = new RuleMatcher();
            var rule = new CollectionRule
            {
                MatchType = RuleMatchType.Contains,
                Pattern = "Formel",
                CaseSensitive = false,
                MatchOn = MatchField.RawTitle,
                AlsoMatchFileName = false
            };

            var result = matcher.IsMatch(rule, "Some Unrelated Title", "Some Unrelated Title", "Formel1_2026.ts");

            Assert.False(result);
        }

        [Fact]
        public void IsMatch_NullRule_ReturnsFalse_DoesNotThrow()
        {
            var matcher = new RuleMatcher();

            var result = matcher.IsMatch(null, "title", "title", "file.ts");

            Assert.False(result);
        }

        [Theory]
        [InlineData(null, null, null)]
        [InlineData("", "", "")]
        public void IsMatch_NullOrEmptyFields_ReturnsFalse_DoesNotThrow(string rawTitle, string normalizedTitle, string fileName)
        {
            var matcher = new RuleMatcher();
            var rule = new CollectionRule
            {
                MatchType = RuleMatchType.Contains,
                Pattern = "anything",
                MatchOn = MatchField.RawTitle,
                AlsoMatchFileName = true
            };

            var result = matcher.IsMatch(rule, rawTitle, normalizedTitle, fileName);

            Assert.False(result);
        }

        [Fact]
        public void IsMatch_NullPattern_ReturnsFalse_DoesNotThrow()
        {
            var matcher = new RuleMatcher();
            var rule = new CollectionRule
            {
                MatchType = RuleMatchType.Contains,
                Pattern = null,
                MatchOn = MatchField.RawTitle
            };

            var result = matcher.IsMatch(rule, "some title", "some title", null);

            Assert.False(result);
        }

        [Fact]
        public void InvalidRegexPattern_IsMatch_ReturnsFalse_DoesNotThrow()
        {
            var matcher = new RuleMatcher();
            var rule = new CollectionRule
            {
                MatchType = RuleMatchType.Regex,
                Pattern = "[unterminated(class", // invalid regex syntax
                MatchOn = MatchField.RawTitle
            };

            var result = matcher.IsMatch(rule, "any title", "any title", null);

            Assert.False(result);
        }

        [Fact]
        public void InvalidRegexPattern_IsDetectableByCaller_ViaTryCompile()
        {
            var matcher = new RuleMatcher();
            var rule = new CollectionRule
            {
                MatchType = RuleMatchType.Regex,
                Pattern = "[unterminated(class",
                MatchOn = MatchField.RawTitle
            };

            var isValid = matcher.TryCompile(rule, out var error);

            Assert.False(isValid);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void InvalidRegexPattern_IsDetectableByCaller_ViaTryGetCompileError_AfterIsMatch()
        {
            var matcher = new RuleMatcher();
            var rule = new CollectionRule
            {
                MatchType = RuleMatchType.Regex,
                Pattern = "(unclosed group",
                MatchOn = MatchField.RawTitle
            };

            // Trigger compilation as a side effect of a normal match call, without the caller
            // explicitly calling TryCompile first.
            var matched = matcher.IsMatch(rule, "any title", "any title", null);
            Assert.False(matched);

            var found = matcher.TryGetCompileError(rule, out var error);
            Assert.True(found);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Fact]
        public void ValidRegexPattern_TryCompile_ReportsNoError()
        {
            var matcher = new RuleMatcher();
            var rule = Formel1Rule();

            var isValid = matcher.TryCompile(rule, out var error);

            Assert.True(isValid);
            Assert.Null(error);

            var found = matcher.TryGetCompileError(rule, out var storedError);
            Assert.False(found);
            Assert.Null(storedError);
        }

        [Fact]
        public void ContainsRule_TryCompile_AlwaysValid_RegardlessOfPatternContent()
        {
            var matcher = new RuleMatcher();
            var rule = new CollectionRule
            {
                MatchType = RuleMatchType.Contains,
                Pattern = "[this looks like invalid regex but is a plain Contains pattern",
                MatchOn = MatchField.RawTitle
            };

            var isValid = matcher.TryCompile(rule, out var error);

            Assert.True(isValid);
            Assert.Null(error);
        }

        [Fact]
        public void OneBrokenRule_DoesNotPreventOtherRulesFromMatching()
        {
            var matcher = new RuleMatcher();
            var brokenRule = new CollectionRule
            {
                MatchType = RuleMatchType.Regex,
                Pattern = "[broken(",
                MatchOn = MatchField.RawTitle
            };
            var goodRule = Formel1Rule();

            var brokenResult = matcher.IsMatch(brokenRule, Formel1Title, Formel1Title, null);
            var goodResult = matcher.IsMatch(goodRule, Formel1Title, Formel1Title, null);

            Assert.False(brokenResult);
            Assert.True(goodResult);
        }

        [Fact]
        public void RegexMatch_RepeatedCalls_UseCachedCompiledRegex_AndStillMatchCorrectly()
        {
            // We can't directly observe "compiled once" from the outside without reflection, but we
            // can confirm repeated evaluation against the same rule instance remains fast and
            // correct - which is what the cache exists to guarantee for large libraries.
            var matcher = new RuleMatcher();
            var rule = Formel1Rule();

            for (var i = 0; i < 1000; i++)
            {
                Assert.True(matcher.IsMatch(rule, Formel1Title, Formel1Title, null));
                Assert.False(matcher.IsMatch(rule, HeuteShowTitle, HeuteShowTitle, null));
            }
        }

        [Fact]
        public void NormalRegex_WithTimeoutConfigured_StillMatchesCorrectly()
        {
            // Confirms the MatchTimeout guard (required by CLAUDE.md/#5) doesn't interfere with a
            // normal, well-behaved pattern. Deliberately not attempting to trigger catastrophic
            // backtracking here, since that would be slow and flaky by nature.
            var matcher = new RuleMatcher();
            var rule = new CollectionRule
            {
                MatchType = RuleMatchType.Regex,
                Pattern = @"^\d{8}\s+\d{4}\s*-\s*.+-\s*Formel\s*1\s+.+$",
                CaseSensitive = false,
                MatchOn = MatchField.RawTitle
            };

            Assert.True(matcher.IsMatch(rule, Formel1Title, Formel1Title, null));
        }
    }
}
