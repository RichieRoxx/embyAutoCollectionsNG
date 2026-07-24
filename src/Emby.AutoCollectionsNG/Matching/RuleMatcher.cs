using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Emby.AutoCollectionsNG.Configuration;

namespace Emby.AutoCollectionsNG.Matching
{
    /// <summary>
    /// Evaluates <see cref="CollectionRule"/> instances against a title/filename triple.
    ///
    /// Deliberately server-independent: no Emby types beyond <see cref="CollectionRule"/>,
    /// <see cref="RuleMatchType"/> and <see cref="MatchField"/> are used here (see CLAUDE.md), so
    /// this class stays unit-testable without a running Emby server.
    ///
    /// Regex objects are compiled once per distinct (pattern, case-sensitivity) combination and
    /// cached for the lifetime of this <see cref="RuleMatcher"/> instance, so repeated calls across
    /// a large library do not pay recompilation cost per item. A single instance is meant to be
    /// reused (e.g. one instance per sync run, or a long-lived singleton) rather than re-created per
    /// call.
    /// </summary>
    public class RuleMatcher
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

        // Cache of successfully compiled regexes, keyed by pattern + case-sensitivity.
        private readonly ConcurrentDictionary<string, Regex> _regexCache =
            new ConcurrentDictionary<string, Regex>();

        // Patterns that failed to compile, so a broken rule can be discovered by calling code
        // (e.g. the sync engine in #6, logged per #10) instead of silently never matching with no
        // way to tell "no match" apart from "broken pattern". Also avoids retrying compilation of a
        // known-bad pattern on every call.
        private readonly ConcurrentDictionary<string, string> _invalidPatterns =
            new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Attempts to compile <paramref name="rule"/>'s pattern ahead of time (relevant for
        /// <see cref="RuleMatchType.Regex"/> rules only; <see cref="RuleMatchType.Contains"/> rules
        /// are always considered valid, whatever their pattern text is) and reports whether it
        /// succeeded. Callers (the sync engine, config validation, etc.) can use this to flag a rule
        /// as broken up front instead of discovering it only via silent non-matches.
        /// </summary>
        /// <param name="rule">The rule to validate.</param>
        /// <param name="error">The compilation error message, or null if the pattern is valid.</param>
        /// <returns>True if the pattern is valid (or the rule is null/not a regex rule).</returns>
        public bool TryCompile(CollectionRule rule, out string error)
        {
            error = null;

            if (rule == null || rule.MatchType != RuleMatchType.Regex)
            {
                return true;
            }

            var regex = GetOrCompileRegex(rule, out error);
            return regex != null;
        }

        /// <summary>
        /// Returns the compilation error recorded for <paramref name="rule"/>'s pattern, if any
        /// (i.e. if it is a regex rule whose pattern is invalid). Returns false and no error when
        /// the rule is null, is not a regex rule, or its pattern has not failed to compile.
        /// </summary>
        public bool TryGetCompileError(CollectionRule rule, out string error)
        {
            error = null;

            if (rule == null || rule.MatchType != RuleMatchType.Regex)
            {
                return false;
            }

            return _invalidPatterns.TryGetValue(CacheKey(rule), out error);
        }

        /// <summary>
        /// Evaluates <paramref name="rule"/> against the given fields. Selects the primary field via
        /// <see cref="CollectionRule.MatchOn"/>; if that doesn't match and
        /// <see cref="CollectionRule.AlsoMatchFileName"/> is set, also tries
        /// <paramref name="fileNameOrPath"/> before giving up.
        ///
        /// Defensive: null/empty field values simply don't match (returns false for that field); an
        /// invalid regex pattern is treated as "no match" rather than thrown - use
        /// <see cref="TryCompile"/> or <see cref="TryGetCompileError"/> to detect a broken rule.
        /// </summary>
        public bool IsMatch(CollectionRule rule, string rawTitle, string normalizedTitle, string fileNameOrPath)
        {
            if (rule == null)
            {
                return false;
            }

            var primaryField = SelectField(rule.MatchOn, rawTitle, normalizedTitle, fileNameOrPath);
            if (MatchesField(rule, primaryField))
            {
                return true;
            }

            if (rule.AlsoMatchFileName && rule.MatchOn != MatchField.FileName)
            {
                if (MatchesField(rule, fileNameOrPath))
                {
                    return true;
                }
            }

            return false;
        }

        private bool MatchesField(CollectionRule rule, string fieldValue)
        {
            if (string.IsNullOrEmpty(fieldValue) || string.IsNullOrEmpty(rule.Pattern))
            {
                return false;
            }

            if (rule.MatchType == RuleMatchType.Contains)
            {
                var comparison = rule.CaseSensitive
                    ? StringComparison.Ordinal
                    : StringComparison.OrdinalIgnoreCase;

                return fieldValue.IndexOf(rule.Pattern, comparison) >= 0;
            }

            var regex = GetOrCompileRegex(rule, out _);
            if (regex == null)
            {
                // Invalid pattern: already recorded in _invalidPatterns by GetOrCompileRegex, so
                // callers can discover it via TryGetCompileError/TryCompile. Treat as "no match"
                // here so one broken rule can't take down matching for the rest.
                return false;
            }

            try
            {
                return regex.IsMatch(fieldValue);
            }
            catch (RegexMatchTimeoutException)
            {
                // Catastrophic backtracking guard: treat a timed-out match as "no match" rather
                // than letting the exception propagate and abort the whole run.
                return false;
            }
        }

        private Regex GetOrCompileRegex(CollectionRule rule, out string error)
        {
            error = null;
            var key = CacheKey(rule);

            if (_regexCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (_invalidPatterns.TryGetValue(key, out var cachedError))
            {
                error = cachedError;
                return null;
            }

            try
            {
                var options = RegexOptions.Compiled;
                if (!rule.CaseSensitive)
                {
                    options |= RegexOptions.IgnoreCase;
                }

                var regex = new Regex(rule.Pattern ?? string.Empty, options, RegexTimeout);
                _regexCache.TryAdd(key, regex);
                return regex;
            }
            catch (ArgumentException ex)
            {
                // Covers both malformed pattern syntax and an invalid MatchTimeout value.
                error = ex.Message;
                _invalidPatterns.TryAdd(key, error);
                return null;
            }
        }

        private static string CacheKey(CollectionRule rule)
        {
            // Case-sensitivity changes RegexOptions, so it must be part of the cache key: two
            // rules sharing a pattern string but differing in CaseSensitive must not share a
            // wrongly-cased compiled Regex. The U+0001 separator cannot occur in a real pattern
            // typed through the config UI, so there is no collision risk with the case-sensitivity
            // suffix.
            return (rule.Pattern ?? string.Empty) + '\u0001' + (rule.CaseSensitive ? '1' : '0');
        }

        private static string SelectField(MatchField field, string rawTitle, string normalizedTitle, string fileNameOrPath)
        {
            switch (field)
            {
                case MatchField.RawTitle:
                    return rawTitle;
                case MatchField.NormalizedTitle:
                    return normalizedTitle;
                case MatchField.FileName:
                    return fileNameOrPath;
                default:
                    return rawTitle;
            }
        }
    }
}
