using System;
using MediaBrowser.Model.Plugins;

namespace Emby.AutoCollectionsNG.Configuration
{
    /// <summary>
    /// Persisted plugin configuration: the list of collection rules plus global options.
    /// The Emby host persists <c>BasePluginConfiguration</c>-derived types as XML (via
    /// <c>IXmlSerializer</c>, which for plugin configuration ultimately delegates to
    /// <see cref="System.Xml.Serialization.XmlSerializer"/>), so all members must be
    /// XML-serializer friendly: public read/write auto-properties, arrays instead of generic
    /// collection interfaces, and no custom constructors that require arguments.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// The configured collection rules. Defaults to an empty array (not null) so a freshly
        /// created configuration never throws on serialization or when iterated by the sync engine.
        /// </summary>
        public CollectionRule[] Rules { get; set; } = Array.Empty<CollectionRule>();

        /// <summary>
        /// When true, the sync engine removes collections that end up with zero members after a run
        /// (e.g. because the matching rule was deleted or no longer matches anything).
        /// </summary>
        public bool DeleteEmptyCollections { get; set; } = false;

        /// <summary>
        /// When true, <see cref="Emby.AutoCollectionsNG.EntryPoints.LibraryChangeListener"/> reacts to
        /// library add/update events (debounced) and queues a sync in addition to the scheduled task
        /// (#7). When false, only the scheduled task (and manual "run now") sync the library.
        /// </summary>
        public bool TriggerOnLibraryChanges { get; set; } = true;

        /// <summary>
        /// How long <see cref="Emby.AutoCollectionsNG.EntryPoints.LibraryChangeListener"/> waits after
        /// the last qualifying library event before actually queueing a sync, so a burst of events
        /// (e.g. an initial library scan adding hundreds of recordings) collapses into roughly one
        /// sync instead of one per item.
        /// </summary>
        public int DebounceMinutes { get; set; } = 5;
    }

    /// <summary>
    /// A single rule describing how to group matching items into a named collection.
    /// </summary>
    public class CollectionRule
    {
        /// <summary>
        /// Name of the target collection (BoxSet) this rule maintains.
        /// </summary>
        public string CollectionName { get; set; }

        /// <summary>
        /// How <see cref="Pattern"/> is applied to the matched field.
        /// </summary>
        public RuleMatchType MatchType { get; set; }

        /// <summary>
        /// The regex or substring pattern, depending on <see cref="MatchType"/>.
        /// </summary>
        public string Pattern { get; set; }

        /// <summary>
        /// Whether matching is case-sensitive. Defaults to false since recording titles from DVR
        /// sources are inconsistently cased.
        /// </summary>
        public bool CaseSensitive { get; set; } = false;

        /// <summary>
        /// Whether this rule is active. Disabled rules are kept in the configuration but skipped by
        /// the sync engine, so users can temporarily turn a rule off without losing its settings.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Which field of an item this rule matches against.
        /// </summary>
        public MatchField MatchOn { get; set; }

        /// <summary>
        /// When true, the rule also tries to match against the file name/path in addition to
        /// <see cref="MatchOn"/>, as a fallback for items with unreliable title metadata.
        /// </summary>
        public bool AlsoMatchFileName { get; set; } = false;

        /// <summary>
        /// Optional list of library names to restrict this rule to. Empty means "all libraries".
        /// </summary>
        public string[] LibraryFilter { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Optional list of item type names (e.g. "Episode", "Movie") to restrict this rule to.
        /// Empty means "all item types".
        /// </summary>
        public string[] ItemTypeFilter { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// The kind of match a <see cref="CollectionRule"/> performs.
    /// </summary>
    public enum RuleMatchType
    {
        Regex,
        Contains
    }

    /// <summary>
    /// Which field of an item a <see cref="CollectionRule"/> is evaluated against.
    /// </summary>
    public enum MatchField
    {
        RawTitle,
        NormalizedTitle,
        FileName
    }
}
