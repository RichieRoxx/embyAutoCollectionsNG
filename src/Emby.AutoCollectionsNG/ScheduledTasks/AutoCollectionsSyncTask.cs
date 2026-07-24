using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoCollectionsNG.Configuration;
using Emby.AutoCollectionsNG.Sync;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.AutoCollectionsNG.ScheduledTasks
{
    /// <summary>
    /// Emby scheduled task that runs <see cref="CollectionSyncService"/> against the plugin's
    /// current configuration. Public class in the plugin assembly ⇒ auto-discovered by the Emby
    /// host (see docs/emby-api-cheatsheet.md, "Scheduled tasks"); this also makes it reachable via
    /// the dashboard's "run now" button without any extra registration.
    /// </summary>
    public class AutoCollectionsSyncTask : IScheduledTask
    {
        private readonly CollectionSyncService _syncService;
        private readonly ILogger _logger;
        private readonly Func<PluginConfiguration> _configProvider;

        // Reentrancy guard: 0 = idle, 1 = a sync is currently running. Emby's task host is not
        // expected to invoke Execute concurrently for the same task instance, but the guard is
        // implemented defensively per issue #7 rather than relying on that assumption.
        private int _isRunning;

        /// <summary>
        /// Constructor used by the Emby host via standard plugin dependency injection.
        /// </summary>
        public AutoCollectionsSyncTask(
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            IUserManager userManager,
            ILogger logger)
            : this(new CollectionSyncService(libraryManager, collectionManager, logger, userManager: userManager), logger, null)
        {
        }

        /// <summary>
        /// Test/advanced seam: accepts an already-built <see cref="CollectionSyncService"/> and an
        /// optional configuration provider, so unit tests can supply mocked Emby dependencies and a
        /// fake <see cref="PluginConfiguration"/> without needing a live <see cref="Plugin.Instance"/>.
        /// </summary>
        internal AutoCollectionsSyncTask(CollectionSyncService syncService, ILogger logger, Func<PluginConfiguration> configProvider)
        {
            _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configProvider = configProvider ?? (() => Plugin.Instance.Configuration);
        }

        public string Name => "Sync Auto Collections";

        public string Key => "AutoCollectionsNGSync";

        public string Description =>
            "Scans the library and creates/updates/removes collections based on the configured title rules (regex/contains).";

        public string Category => "Library";

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            {
                _logger.Info("Auto Collections NG: a sync is already running; skipping this trigger.");
                return;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var config = _configProvider() ?? new PluginConfiguration();

                _logger.Info("Auto Collections NG: starting scheduled/manual sync.");

                var result = await _syncService.SyncAsync(config, progress, cancellationToken).ConfigureAwait(false);

                LogSummary(result);
            }
            finally
            {
                Interlocked.Exchange(ref _isRunning, 0);
            }
        }

        private void LogSummary(SyncResult result)
        {
            var created = result.Collections.Count(c => c.Created);
            var deleted = result.Collections.Count(c => c.Deleted);
            var updated = result.Collections.Count(c => !c.Created && !c.Deleted);

            _logger.Info(
                "Auto Collections NG: sync finished. Items scanned={0}, skipped (no title)={1}, collections created={2}, updated={3}, deleted={4}, errors={5}.",
                result.ItemsScanned,
                result.ItemsSkippedNoTitle,
                created,
                updated,
                deleted,
                result.Errors.Count);

            foreach (var hit in result.RuleHitCounts)
            {
                _logger.Debug("Auto Collections NG: rule '{0}' matched {1} item(s).", hit.Key, hit.Value);
            }

            foreach (var ruleError in result.RuleErrors)
            {
                _logger.Error("Auto Collections NG: rule '{0}' was skipped: {1}", ruleError.Key, ruleError.Value);
            }

            foreach (var error in result.Errors)
            {
                _logger.Error("Auto Collections NG: {0}", error);
            }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            // Daily at 04:00 local time - a quiet-hours default per docs/PLAN.md. Verified shape of
            // TaskTriggerInfo via reflection against MediaBrowser.Model.dll 4.9.1.90 (see
            // docs/emby-api-cheatsheet.md): Type = TaskTriggerInfo.TriggerDaily plus TimeOfDayTicks
            // (ticks since midnight) is how a "once daily at a specific time" trigger is expressed.
            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerDaily,
                TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
            };
        }
    }
}
