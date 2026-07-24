using System;
using System.Threading;
using Emby.AutoCollectionsNG.Configuration;
using Emby.AutoCollectionsNG.EntryPoints;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using Moq;
using Xunit;

namespace Emby.AutoCollectionsNG.Tests.EntryPoints
{
    /// <summary>
    /// Tests <see cref="LibraryChangeListener"/> via its internal test seam, which accepts a fake
    /// "trigger a sync" delegate (a counter, here) and - crucially - a fake debounce-window provider
    /// that hands back a <see cref="TimeSpan"/> directly instead of going through
    /// <see cref="PluginConfiguration.DebounceMinutes"/>. That keeps these tests fast and deterministic:
    /// they exercise the exact same <see cref="Timer"/>-reset debounce mechanism the real
    /// minutes-based path uses (see <see cref="LibraryChangeListener"/>'s private
    /// DebounceWindowFromConfig helper), just with a millisecond-scale window instead of waiting for
    /// real minutes. <see cref="ILibraryManager"/> is a mockable interface (per
    /// docs/emby-api-cheatsheet.md); its <c>ItemAdded</c>/<c>ItemUpdated</c> events are raised directly
    /// via Moq's <c>Raise</c>. <see cref="BoxSet"/>/<see cref="Video"/> are constructed directly (both
    /// have public parameterless constructors, confirmed in the cheat sheet).
    /// </summary>
    public class LibraryChangeListenerTests
    {
        private static readonly TimeSpan TinyDebounceWindow = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

        private static (LibraryChangeListener listener, Mock<ILibraryManager> library, CountingTrigger trigger) MakeListener(
            PluginConfiguration config,
            TimeSpan? debounceWindow = null)
        {
            var library = new Mock<ILibraryManager>();
            var logger = new Mock<ILogger>();
            var trigger = new CountingTrigger();
            var window = debounceWindow ?? TinyDebounceWindow;

            var listener = new LibraryChangeListener(
                library.Object,
                trigger.Trigger,
                () => config,
                () => window,
                logger.Object);

            return (listener, library, trigger);
        }

        private static void RaiseItemAdded(Mock<ILibraryManager> library, BaseItem item)
        {
            var args = new ItemChangeEventArgs(library.Object) { Item = item };
            library.Raise(m => m.ItemAdded += null, library.Object, args);
        }

        [Fact]
        public void ItemAdded_ForNormalItem_EventuallyTriggersExactlyOneSync_AfterDebounceWindow()
        {
            var config = new PluginConfiguration { TriggerOnLibraryChanges = true };
            var (listener, library, trigger) = MakeListener(config);
            listener.Run();

            RaiseItemAdded(library, new Video { InternalId = 1, Name = "Some Recording" });

            Assert.True(trigger.WaitForAtLeast(1, WaitTimeout), "Sync was never triggered within the timeout.");
            // Give any spurious extra callback a moment to show up before asserting the final count.
            Thread.Sleep(TinyDebounceWindow + TinyDebounceWindow);
            Assert.Equal(1, trigger.Count);

            listener.Dispose();
        }

        [Fact]
        public void MultipleRapidItemAddedEvents_WithinDebounceWindow_CollapseIntoExactlyOneSync()
        {
            var config = new PluginConfiguration { TriggerOnLibraryChanges = true };
            var (listener, library, trigger) = MakeListener(config);
            listener.Run();

            // Fire a burst of events, each one well within the debounce window of the previous one, so
            // the timer keeps getting reset instead of ever firing mid-burst.
            for (var i = 0; i < 20; i++)
            {
                RaiseItemAdded(library, new Video { InternalId = i, Name = "Recording " + i });
                Thread.Sleep(5);
            }

            Assert.True(trigger.WaitForAtLeast(1, WaitTimeout), "Sync was never triggered within the timeout.");
            // Wait past the debounce window once more to make sure no further callback sneaks in.
            Thread.Sleep(TinyDebounceWindow + TinyDebounceWindow);
            Assert.Equal(1, trigger.Count);

            listener.Dispose();
        }

        [Fact]
        public void ItemAdded_WithBoxSetItem_NeverTriggersSync()
        {
            var config = new PluginConfiguration { TriggerOnLibraryChanges = true };
            var (listener, library, trigger) = MakeListener(config);
            listener.Run();

            RaiseItemAdded(library, new BoxSet { InternalId = 42, Name = "Our Own Collection" });

            // Wait comfortably past the debounce window - nothing should ever fire.
            Thread.Sleep(TinyDebounceWindow + TinyDebounceWindow + TimeSpan.FromMilliseconds(100));
            Assert.Equal(0, trigger.Count);

            listener.Dispose();
        }

        [Fact]
        public void ItemAdded_WithTriggerOnLibraryChangesDisabled_IsIgnoredEntirely()
        {
            var config = new PluginConfiguration { TriggerOnLibraryChanges = false };
            var (listener, library, trigger) = MakeListener(config);
            listener.Run();

            RaiseItemAdded(library, new Video { InternalId = 1, Name = "Some Recording" });

            // Wait comfortably past the debounce window - the debounce timer should never even have
            // been started, so nothing fires.
            Thread.Sleep(TinyDebounceWindow + TinyDebounceWindow + TimeSpan.FromMilliseconds(100));
            Assert.Equal(0, trigger.Count);

            listener.Dispose();
        }

        [Fact]
        public void ItemUpdated_ForNormalItem_AlsoTriggersDebouncedSync()
        {
            var config = new PluginConfiguration { TriggerOnLibraryChanges = true };
            var (listener, library, trigger) = MakeListener(config);
            listener.Run();

            var args = new ItemChangeEventArgs(library.Object) { Item = new Video { InternalId = 1, Name = "Some Recording" } };
            library.Raise(m => m.ItemUpdated += null, library.Object, args);

            Assert.True(trigger.WaitForAtLeast(1, WaitTimeout), "Sync was never triggered within the timeout.");

            listener.Dispose();
        }

        [Fact]
        public void Dispose_IsSafe_WhenCalledTwice_AndWhenRunWasNeverCalled()
        {
            var config = new PluginConfiguration { TriggerOnLibraryChanges = true };
            var (listener, _, _) = MakeListener(config);

            // Run() deliberately not called.
            var exception = Record.Exception(() =>
            {
                listener.Dispose();
                listener.Dispose();
            });

            Assert.Null(exception);
        }

        [Fact]
        public void Dispose_AfterRun_UnsubscribesAndStopsFurtherTriggers()
        {
            var config = new PluginConfiguration { TriggerOnLibraryChanges = true };
            var (listener, library, trigger) = MakeListener(config);
            listener.Run();

            listener.Dispose();

            // Firing the event after Dispose() should not throw and, since we unsubscribed, the
            // (now-disposed) listener must not observe it at all.
            var exception = Record.Exception(() => RaiseItemAdded(library, new Video { InternalId = 1, Name = "X" }));
            Assert.Null(exception);

            Thread.Sleep(TinyDebounceWindow + TinyDebounceWindow);
            Assert.Equal(0, trigger.Count);
        }

        /// <summary>
        /// Thread-safe counter standing in for the real "queue a sync via ITaskManager" delegate, with
        /// a wait helper so tests don't have to poll/sleep blindly for the debounce timer to fire.
        /// </summary>
        private sealed class CountingTrigger
        {
            private int _count;
            private readonly ManualResetEventSlim _fired = new ManualResetEventSlim(false);

            public int Count => Volatile.Read(ref _count);

            public void Trigger()
            {
                Interlocked.Increment(ref _count);
                _fired.Set();
            }

            public bool WaitForAtLeast(int expectedCount, TimeSpan timeout)
            {
                var deadline = DateTime.UtcNow + timeout;
                while (DateTime.UtcNow < deadline)
                {
                    if (Count >= expectedCount)
                    {
                        return true;
                    }

                    _fired.Wait(TimeSpan.FromMilliseconds(20));
                }

                return Count >= expectedCount;
            }
        }
    }
}
