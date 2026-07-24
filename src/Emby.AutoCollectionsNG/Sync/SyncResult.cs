using System.Collections.Generic;

namespace Emby.AutoCollectionsNG.Sync
{
    /// <summary>
    /// Summary of a single <see cref="CollectionSyncService"/> run, used both for logging (#10) and
    /// for tests to assert on outcomes without parsing log output.
    /// </summary>
    public class SyncResult
    {
        public int ItemsScanned { get; set; }

        public int ItemsSkippedNoTitle { get; set; }

        /// <summary>Collection name (or rule identifier) -&gt; number of items that matched.</summary>
        public Dictionary<string, int> RuleHitCounts { get; } = new Dictionary<string, int>();

        /// <summary>Collection/rule name -&gt; the reason the rule was skipped (e.g. invalid pattern).</summary>
        public Dictionary<string, string> RuleErrors { get; } = new Dictionary<string, string>();

        public List<CollectionSyncOutcome> Collections { get; } = new List<CollectionSyncOutcome>();

        /// <summary>Non-fatal errors encountered while processing individual items/collections.</summary>
        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>What happened to a single target collection during a sync run.</summary>
    public class CollectionSyncOutcome
    {
        public CollectionSyncOutcome(string collectionName)
        {
            CollectionName = collectionName;
        }

        public string CollectionName { get; }

        public bool Created { get; set; }

        public bool Deleted { get; set; }

        public int ItemsAdded { get; set; }

        public int ItemsRemoved { get; set; }
    }
}
