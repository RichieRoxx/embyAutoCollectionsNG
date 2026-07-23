using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoCollectionsNG.Configuration;
using Emby.AutoCollectionsNG.ScheduledTasks;
using Emby.AutoCollectionsNG.Sync;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using Moq;
using Xunit;

namespace Emby.AutoCollectionsNG.Tests.ScheduledTasks
{
    /// <summary>
    /// Tests <see cref="AutoCollectionsSyncTask"/> via the internal test seam that accepts an
    /// already-built <see cref="CollectionSyncService"/> plus a fake configuration provider, so no
    /// live <see cref="Emby.AutoCollectionsNG.Plugin"/> singleton is required (same mocking pattern
    /// as <see cref="Emby.AutoCollectionsNG.Tests.Sync.CollectionSyncServiceTests"/>: mocked
    /// <see cref="ILibraryManager"/>/<see cref="ICollectionManager"/>/<see cref="ILogger"/>, real
    /// directly-constructed <see cref="Video"/> instances for item data).
    /// </summary>
    public class AutoCollectionsSyncTaskTests
    {
        private static (Mock<ILibraryManager> library, Mock<ICollectionManager> collections, Mock<ILogger> logger) MakeMocks(
            BaseItem[] items)
        {
            var library = new Mock<ILibraryManager>();
            library
                .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns((InternalItemsQuery q) =>
                    q.IncludeItemTypes != null && q.IncludeItemTypes.Contains("BoxSet")
                        ? Array.Empty<BaseItem>()
                        : items);

            var collections = new Mock<ICollectionManager>();
            collections
                .Setup(m => m.CreateCollection(It.IsAny<CollectionCreationOptions>()))
                .ReturnsAsync((CollectionCreationOptions o) => new BoxSet { Name = o.Name, InternalId = 9999 });
            collections
                .Setup(m => m.AddToCollection(It.IsAny<long>(), It.IsAny<long[]>()))
                .Returns(Task.CompletedTask);

            var logger = new Mock<ILogger>();

            return (library, collections, logger);
        }

        private static AutoCollectionsSyncTask MakeTask(
            BaseItem[] items,
            PluginConfiguration config,
            out Mock<ILibraryManager> library,
            out Mock<ILogger> logger)
        {
            var mocks = MakeMocks(items);
            library = mocks.library;
            logger = mocks.logger;

            var syncService = new CollectionSyncService(mocks.library.Object, mocks.collections.Object, mocks.logger.Object);
            return new AutoCollectionsSyncTask(syncService, mocks.logger.Object, () => config);
        }

        [Fact]
        public void ExposesSensibleMetadata()
        {
            var config = new PluginConfiguration();
            var task = MakeTask(Array.Empty<BaseItem>(), config, out _, out _);

            Assert.False(string.IsNullOrWhiteSpace(task.Name));
            Assert.False(string.IsNullOrWhiteSpace(task.Key));
            Assert.False(string.IsNullOrWhiteSpace(task.Description));
            Assert.False(string.IsNullOrWhiteSpace(task.Category));
        }

        [Fact]
        public void GetDefaultTriggers_ReturnsAtLeastOneTrigger()
        {
            var config = new PluginConfiguration();
            var task = MakeTask(Array.Empty<BaseItem>(), config, out _, out _);

            var triggers = task.GetDefaultTriggers().ToList();

            Assert.NotEmpty(triggers);
        }

        [Fact]
        public async Task Execute_RunsSync_WithConfiguredRules_AndCompletesWithoutThrowing()
        {
            var items = new BaseItem[] { new Video { InternalId = 1, Name = "Formel 1 Rennen" } };
            var config = new PluginConfiguration
            {
                Rules = new[]
                {
                    new CollectionRule
                    {
                        CollectionName = "Formel 1",
                        MatchType = RuleMatchType.Regex,
                        Pattern = @"(?i)\bformel\s*1\b",
                        Enabled = true
                    }
                }
            };

            var task = MakeTask(items, config, out var library, out _);

            await task.Execute(CancellationToken.None, null);

            // The task actually reached the library (i.e. it ran the sync with the provided config),
            // rather than doing nothing.
            library.Verify(m => m.GetItemList(It.IsAny<InternalItemsQuery>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task Execute_WithNoItems_CompletesWithoutThrowing()
        {
            var config = new PluginConfiguration();
            var task = MakeTask(Array.Empty<BaseItem>(), config, out _, out _);

            await task.Execute(CancellationToken.None, null);
        }

        [Fact]
        public async Task Execute_AlreadyCancelledToken_ThrowsAndDoesNotCompleteNormally()
        {
            var config = new PluginConfiguration
            {
                Rules = new[]
                {
                    new CollectionRule { CollectionName = "X", MatchType = RuleMatchType.Contains, Pattern = "x", Enabled = true }
                }
            };
            var task = MakeTask(Array.Empty<BaseItem>(), config, out _, out _);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.Execute(cts.Token, null));
        }

        [Fact]
        public async Task Execute_ConcurrentSecondCall_DoesNotRunASecondSync_WhileFirstIsInFlight()
        {
            var library = new Mock<ILibraryManager>();
            var collections = new Mock<ICollectionManager>();
            var logger = new Mock<ILogger>();

            var gate = new SemaphoreSlim(0, 1);
            var entered = new SemaphoreSlim(0, 1);
            var itemQueryCount = 0;

            library
                .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns((InternalItemsQuery q) =>
                {
                    if (q.IncludeItemTypes != null && q.IncludeItemTypes.Contains("BoxSet"))
                    {
                        return Array.Empty<BaseItem>();
                    }

                    // First call into the "real item query" path: signal we've entered, then block
                    // until the test releases us, so a concurrent Execute() call has a window to
                    // observe the reentrancy guard while the first sync is still in flight.
                    var callNumber = Interlocked.Increment(ref itemQueryCount);
                    if (callNumber == 1)
                    {
                        entered.Release();
                        gate.Wait();
                    }

                    return Array.Empty<BaseItem>();
                });

            var config = new PluginConfiguration
            {
                Rules = new[]
                {
                    new CollectionRule { CollectionName = "X", MatchType = RuleMatchType.Contains, Pattern = "x", Enabled = true }
                }
            };

            var syncService = new CollectionSyncService(library.Object, collections.Object, logger.Object);
            var task = new AutoCollectionsSyncTask(syncService, logger.Object, () => config);

            var firstRun = Task.Run(() => task.Execute(CancellationToken.None, null));

            // Wait until the first run is definitely inside the (blocked) sync.
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "First run never reached the blocking item query.");

            // Second call while the first is still in flight - should return immediately without
            // reaching the library manager a second time.
            await task.Execute(CancellationToken.None, null);

            // Release the first run and let it finish.
            gate.Release();
            await firstRun;

            Assert.Equal(1, itemQueryCount);
        }
    }
}
