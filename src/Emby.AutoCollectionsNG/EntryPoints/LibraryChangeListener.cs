using System;
using System.Threading;
using Emby.AutoCollectionsNG.Configuration;
using Emby.AutoCollectionsNG.ScheduledTasks;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.AutoCollectionsNG.EntryPoints
{
    /// <summary>
    /// Faster-reacting, additional trigger on top of <see cref="AutoCollectionsSyncTask"/>'s scheduled
    /// run (issue #8): listens for <see cref="ILibraryManager.ItemAdded"/> /
    /// <see cref="ILibraryManager.ItemUpdated"/> and queues a sync via <see cref="ITaskManager"/> a
    /// short debounce window after the last qualifying event, so a burst of events (e.g. hundreds of
    /// recordings appearing during an initial library scan) collapses into roughly one sync instead of
    /// one per item. This does not replace the scheduled task - both remain active.
    /// </summary>
    public class LibraryChangeListener : IServerEntryPoint
    {
        private readonly ILibraryManager _libraryManager;
        private readonly Action _triggerSync;
        private readonly Func<PluginConfiguration> _configProvider;
        private readonly Func<TimeSpan> _debounceWindowProvider;
        private readonly ILogger _logger;

        // Guards _subscribed/_disposed/_debounceTimer so Run()/Dispose()/the timer callback/event
        // handlers (which may fire on arbitrary threadpool threads) never race each other.
        private readonly object _gate = new object();
        private Timer _debounceTimer;
        private bool _subscribed;
        private bool _disposed;

        /// <summary>
        /// Constructor used by the Emby host via standard plugin dependency injection. Builds its own
        /// <see cref="AutoCollectionsSyncTask"/> instance to queue (kept private to this listener - it
        /// is separate from the identically-configured task the host discovers and schedules on its
        /// own via <see cref="Emby.AutoCollectionsNG.ScheduledTasks.AutoCollectionsSyncTask"/>'s public
        /// DI constructor); <see cref="ITaskManager.QueueScheduledTask"/> only needs an
        /// <see cref="MediaBrowser.Model.Tasks.IScheduledTask"/> instance to run, not specifically the
        /// host's own registered one.
        /// </summary>
        public LibraryChangeListener(
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            IUserManager userManager,
            ITaskManager taskManager,
            ILogger logger)
            : this(
                libraryManager,
                BuildTaskTrigger(libraryManager, collectionManager, userManager, taskManager, logger),
                () => Plugin.Instance.Configuration,
                debounceWindowProvider: null,
                logger: logger)
        {
        }

        /// <summary>
        /// Test/advanced seam: accepts a fake "trigger a sync" delegate, a fake configuration
        /// provider, and - importantly for tests that must not sleep for real minutes - a fake
        /// debounce-window provider that returns a <see cref="TimeSpan"/> directly instead of going
        /// through <see cref="PluginConfiguration.DebounceMinutes"/> (an integer number of minutes,
        /// too coarse for a fast unit test). When null, the real path is used: read
        /// <see cref="PluginConfiguration.DebounceMinutes"/> fresh from <paramref name="configProvider"/>
        /// on every call, converted to a <see cref="TimeSpan"/>.
        /// </summary>
        internal LibraryChangeListener(
            ILibraryManager libraryManager,
            Action triggerSync,
            Func<PluginConfiguration> configProvider,
            Func<TimeSpan> debounceWindowProvider,
            ILogger logger)
        {
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _triggerSync = triggerSync ?? throw new ArgumentNullException(nameof(triggerSync));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configProvider = configProvider ?? (() => Plugin.Instance.Configuration);
            _debounceWindowProvider = debounceWindowProvider ?? (() => DebounceWindowFromConfig(_configProvider));

            // Idle (Timeout.Infinite due time) until the first qualifying event schedules it.
            _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        }

        private static TimeSpan DebounceWindowFromConfig(Func<PluginConfiguration> configProvider)
        {
            var config = configProvider?.Invoke() ?? new PluginConfiguration();
            var minutes = config.DebounceMinutes;
            return minutes > 0 ? TimeSpan.FromMinutes(minutes) : TimeSpan.Zero;
        }

        private static Action BuildTaskTrigger(
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            IUserManager userManager,
            ITaskManager taskManager,
            ILogger logger)
        {
            if (taskManager == null)
            {
                throw new ArgumentNullException(nameof(taskManager));
            }

            var syncTask = new AutoCollectionsSyncTask(libraryManager, collectionManager, userManager, logger);
            return () => taskManager.QueueScheduledTask(syncTask, new TaskOptions());
        }

        /// <summary>
        /// Subscribes to library change events. Subscription itself is unconditional - it does NOT
        /// gate on <see cref="PluginConfiguration.TriggerOnLibraryChanges"/> here, deliberately: the
        /// Emby host calls <see cref="Run"/> exactly once at server startup, so if subscribing were
        /// conditional on the flag's value at that moment, a user later flipping the setting back on
        /// in the config UI would have no effect until a server restart (there would be nothing left
        /// to react to the change and re-subscribe). Instead this always subscribes, and
        /// <see cref="OnItemChanged"/> re-reads <see cref="PluginConfiguration.TriggerOnLibraryChanges"/>
        /// fresh on every single event and no-ops when it is false. Net effect: toggling the setting
        /// in either direction takes effect on the very next library event, no restart required in
        /// either direction. This is the one dynamic-reconfiguration claim actually implemented here -
        /// there is no separate "live re-subscribe" mechanism, because none is needed.
        /// </summary>
        public void Run()
        {
            lock (_gate)
            {
                if (_disposed || _subscribed)
                {
                    return;
                }

                _libraryManager.ItemAdded += OnItemChanged;
                _libraryManager.ItemUpdated += OnItemChanged;
                _subscribed = true;
            }

            _logger.Info("Auto Collections NG: library change listener started (watching ItemAdded/ItemUpdated).");
        }

        private void OnItemChanged(object sender, ItemChangeEventArgs args)
        {
            // Never react to our own collection writes - CollectionSyncService creates/updates BoxSet
            // instances, and reacting to those would be a sync-triggers-sync feedback loop (see
            // CLAUDE.md "No feedback loops").
            if (args?.Item is BoxSet)
            {
                return;
            }

            PluginConfiguration config;
            try
            {
                config = _configProvider();
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Auto Collections NG: failed to read plugin configuration in library change listener.", ex);
                return;
            }

            if (config == null || !config.TriggerOnLibraryChanges)
            {
                return;
            }

            ScheduleDebouncedSync();
        }

        private void ScheduleDebouncedSync()
        {
            TimeSpan window;
            try
            {
                window = _debounceWindowProvider();
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Auto Collections NG: failed to read the debounce window in library change listener.", ex);
                return;
            }

            if (window < TimeSpan.Zero)
            {
                window = TimeSpan.Zero;
            }

            lock (_gate)
            {
                // Disposed after the window was read but before we get the lock - nothing to schedule.
                if (_disposed)
                {
                    return;
                }

                // Reset (or start) the single debounce timer: every qualifying event pushes the fire
                // time further out, so a burst of events results in one callback after the burst goes
                // quiet for `window`, not one callback per event.
                _debounceTimer.Change(window, Timeout.InfiniteTimeSpan);
            }
        }

        private void OnDebounceElapsed(object state)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
            }

            try
            {
                _logger.Info("Auto Collections NG: debounce window elapsed after library changes; queueing sync.");
                _triggerSync();
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Auto Collections NG: failed to queue sync from library change listener.", ex);
            }
        }

        /// <summary>
        /// Unsubscribes and disposes the debounce timer. Safe to call multiple times, and safe to call
        /// even if <see cref="Run"/> was never invoked (e.g. the host disposing entry points
        /// defensively during shutdown before ever starting them).
        /// </summary>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (_subscribed)
                {
                    _libraryManager.ItemAdded -= OnItemChanged;
                    _libraryManager.ItemUpdated -= OnItemChanged;
                    _subscribed = false;
                }

                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }
        }
    }
}
