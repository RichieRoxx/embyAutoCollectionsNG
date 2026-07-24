using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Emby.AutoCollectionsNG.Matching
{
    /// <summary>
    /// Normalizes raw DVR recording titles (e.g.
    /// "20260705 1742 - ORF 1 HD - Formel 1 Großer Preis von Großbritannien 2026") into a clean
    /// title suitable for display/matching (e.g. "Formel 1 Großer Preis von Großbritannien 2026").
    ///
    /// Deliberately server-independent: no Emby types are used here (see CLAUDE.md), so this class
    /// stays unit-testable without a running Emby server.
    /// </summary>
    public static class TitleNormalizer
    {
        // A leading date/time prefix as produced by DVR recordings, e.g. "20260705 1742 - ".
        // 8-digit date, whitespace, 4-digit time, optional whitespace, dash, optional whitespace.
        private static readonly Regex DatePrefixRegex = new Regex(
            @"^\d{8}\s+\d{4}\s*-\s*",
            RegexOptions.Compiled);

        private static readonly Regex WhitespaceRunRegex = new Regex(
            @"\s+",
            RegexOptions.Compiled);

        private const string SegmentSeparator = " - ";

        /// <summary>
        /// Normalizes <paramref name="rawTitle"/>. Null or empty input is returned unchanged
        /// (never throws).
        /// </summary>
        public static string Normalize(string rawTitle)
        {
            if (string.IsNullOrEmpty(rawTitle))
            {
                return rawTitle;
            }

            var title = rawTitle;

            var dateMatch = DatePrefixRegex.Match(title);
            if (dateMatch.Success)
            {
                // Only strip the following channel-name segment when a date prefix was actually
                // found and removed - a title with no date prefix might legitimately start with a
                // hyphenated word (e.g. "Spider-Man: No Way Home") and must be left alone.
                title = title.Substring(dateMatch.Length);

                var separatorIndex = title.IndexOf(SegmentSeparator, StringComparison.Ordinal);
                if (separatorIndex >= 0)
                {
                    title = title.Substring(separatorIndex + SegmentSeparator.Length);
                }
            }

            title = title.Normalize(NormalizationForm.FormC);
            title = WhitespaceRunRegex.Replace(title, " ").Trim();

            return title;
        }
    }
}
