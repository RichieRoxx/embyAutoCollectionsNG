using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoCollectionsNG.Configuration;
using Emby.AutoCollectionsNG.Sync;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Logging;
using Moq;
using Xunit;

namespace Emby.AutoCollectionsNG.Tests.Sync
{
    /// <summary>
    /// Tests the reconciliation algorithm in <see cref="CollectionSyncService"/> against mocked
    /// <see cref="ILibraryManager"/>/<see cref="ICollectionManager"/> (both interfaces, fully
    /// mockable) plus real, directly-constructed <see cref="Video"/>/<see cref="BoxSet"/> instances
    /// for item data (both have public parameterless constructors and settable Name/Path/InternalId -
    /// see docs/emby-api-cheatsheet.md). The one seam that can't use a real object is reading an
    /// existing collection's current members (Folder.GetChildren always returns empty on a bare
    /// instance with no live Emby host behind it) - that's why CollectionSyncService accepts an
    /// injectable collectionMemberIdsProvider, which these tests use instead.
    /// </summary>
    public class CollectionSyncServiceTests
    {
        private static Video MakeItem(long id, string? name, string? path = null)
        {
            return new Video { InternalId = id, Name = name, Path = path };
        }

        private static BoxSet MakeExistingCollection(long id, string name)
        {
            return new BoxSet { InternalId = id, Name = name };
        }

        private static (Mock<ILibraryManager> library, Mock<ICollectionManager> collections, Mock<ILogger> logger) MakeMocks(
            BaseItem[] items,
            BaseItem[]? existingCollections = null)
        {
            var library = new Mock<ILibraryManager>();
            library
                .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns((InternalItemsQuery q) =>
                    q.IncludeItemTypes != null && q.IncludeItemTypes.Contains("BoxSet")
                        ? existingCollections ?? Array.Empty<BaseItem>()
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

        private static CollectionRule Rule(
            string collectionName,
            RuleMatchType matchType,
            string pattern,
            bool enabled = true,
            bool caseSensitive = false,
            MatchField matchOn = MatchField.RawTitle,
            bool alsoMatchFileName = false,
            string[]? libraryFilter = null,
            string[]? itemTypeFilter = null)
        {
            return new CollectionRule
            {
                CollectionName = collectionName,
                MatchType = matchType,
                Pattern = pattern,
                Enabled = enabled,
                CaseSensitive = caseSensitive,
                MatchOn = matchOn,
                AlsoMatchFileName = alsoMatchFileName,
                LibraryFilter = libraryFilter ?? Array.Empty<string>(),
                ItemTypeFilter = itemTypeFilter ?? Array.Empty<string>()
            };
        }

        [Fact]
        public async Task CreatesNewCollection_WhenItemsMatch()
        {
            var items = new BaseItem[]
            {
                MakeItem(1, "Formel 1 Großer Preis von Großbritannien 2026"),
                MakeItem(2, "Something unrelated")
            };
            var (library, collectionManager, logger) = MakeMocks(items);

            var config = new PluginConfiguration
            {
                Rules = new[] { Rule("Formel 1", RuleMatchType.Regex, @"(?i)\bformel\s*1\b") }
            };

            var sut = new CollectionSyncService(library.Object, collectionManager.Object, logger.Object);
            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            // The collection is created empty and populated afterwards: passing ItemIdList to
            // CreateCollection makes the live host return null and persist nothing, while
            // AddToCollection works. See CollectionSyncService.ReconcileCollectionAsync.
            collectionManager.Verify(
                m => m.CreateCollection(It.Is<CollectionCreationOptions>(o =>
                    o.Name == "Formel 1" && o.ItemIdList.Length == 0)),
                Times.Once);
            collectionManager.Verify(
                m => m.AddToCollection(9999, It.Is<long[]>(ids => ids.Length == 1 && ids[0] == 1)),
                Times.Once);
            Assert.Single(result.Collections);
            Assert.True(result.Collections[0].Created);
            Assert.Equal(1, result.RuleHitCounts["Formel 1"]);
        }

        [Fact]
        public async Task NoCollectionCreated_WhenNothingMatches()
        {
            var items = new BaseItem[] { MakeItem(1, "Nothing relevant here") };
            var (library, collectionManager, logger) = MakeMocks(items);

            var config = new PluginConfiguration
            {
                Rules = new[] { Rule("Formel 1", RuleMatchType.Regex, @"(?i)\bformel\s*1\b") }
            };

            var sut = new CollectionSyncService(library.Object, collectionManager.Object, logger.Object);
            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            collectionManager.Verify(m => m.CreateCollection(It.IsAny<CollectionCreationOptions>()), Times.Never);
            Assert.Empty(result.Collections);
            Assert.Equal(0, result.RuleHitCounts["Formel 1"]);
        }

        [Fact]
        public async Task AddsNewMatches_ToExistingCollection_WithoutDuplicatingExistingMembers()
        {
            var items = new BaseItem[]
            {
                MakeItem(1, "ZDF Magazin Royale Folge 1"),
                MakeItem(2, "ZDF Magazin Royale Folge 2")
            };
            var existing = MakeExistingCollection(100, "ZDF Magazin Royale");
            var (library, collectionManager, logger) = MakeMocks(items, new BaseItem[] { existing });

            var config = new PluginConfiguration
            {
                Rules = new[] { Rule("ZDF Magazin Royale", RuleMatchType.Contains, "ZDF Magazin Royale") }
            };

            // Item 1 is already a member; item 2 is new.
            var sut = new CollectionSyncService(
                library.Object,
                collectionManager.Object,
                logger.Object,
                collectionMemberIdsProvider: _ => new long[] { 1 });

            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            collectionManager.Verify(m => m.CreateCollection(It.IsAny<CollectionCreationOptions>()), Times.Never);
            collectionManager.Verify(m => m.AddToCollection(100, It.Is<long[]>(ids => ids.Length == 1 && ids[0] == 2)), Times.Once);
            collectionManager.Verify(m => m.RemoveFromCollection(It.IsAny<BoxSet>(), It.IsAny<long[]>()), Times.Never);
            Assert.Single(result.Collections);
            Assert.Equal(1, result.Collections[0].ItemsAdded);
            Assert.Equal(0, result.Collections[0].ItemsRemoved);
        }

        [Fact]
        public async Task RemovesStaleMembers_ThatNoLongerMatch()
        {
            // Item 1 no longer matches (title changed); item 2 still matches.
            var items = new BaseItem[] { MakeItem(2, "heute-show extra") };
            var existing = MakeExistingCollection(100, "heute-show");
            var (library, collectionManager, logger) = MakeMocks(items, new BaseItem[] { existing });

            var config = new PluginConfiguration
            {
                Rules = new[] { Rule("heute-show", RuleMatchType.Regex, @"(?i)\bheute[- ]show\b") }
            };

            var sut = new CollectionSyncService(
                library.Object,
                collectionManager.Object,
                logger.Object,
                collectionMemberIdsProvider: _ => new long[] { 1, 2 });

            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            collectionManager.Verify(m => m.RemoveFromCollection(existing, It.Is<long[]>(ids => ids.Length == 1 && ids[0] == 1)), Times.Once);
            collectionManager.Verify(m => m.AddToCollection(It.IsAny<long>(), It.IsAny<long[]>()), Times.Never);
            Assert.Equal(1, result.Collections[0].ItemsRemoved);
        }

        [Fact]
        public async Task Idempotent_SecondRunWithNoChanges_MakesZeroWrites()
        {
            var items = new BaseItem[] { MakeItem(1, "Formel 1 Rennen") };
            var existing = MakeExistingCollection(100, "Formel 1");
            var (library, collectionManager, logger) = MakeMocks(items, new BaseItem[] { existing });

            var config = new PluginConfiguration
            {
                Rules = new[] { Rule("Formel 1", RuleMatchType.Regex, @"(?i)\bformel\s*1\b") }
            };

            // Existing membership already exactly matches desired state.
            var sut = new CollectionSyncService(
                library.Object,
                collectionManager.Object,
                logger.Object,
                collectionMemberIdsProvider: _ => new long[] { 1 });

            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            collectionManager.Verify(m => m.CreateCollection(It.IsAny<CollectionCreationOptions>()), Times.Never);
            collectionManager.Verify(m => m.AddToCollection(It.IsAny<long>(), It.IsAny<long[]>()), Times.Never);
            collectionManager.Verify(m => m.RemoveFromCollection(It.IsAny<BoxSet>(), It.IsAny<long[]>()), Times.Never);
            library.Verify(m => m.DeleteItem(It.IsAny<BaseItem>(), It.IsAny<DeleteOptions>()), Times.Never);
            Assert.Empty(result.Collections);
        }

        [Fact]
        public async Task DeletesEmptyCollection_WhenConfiguredTo()
        {
            var items = new BaseItem[] { MakeItem(1, "Nothing matches anymore") };
            var existing = MakeExistingCollection(100, "Formel 1");
            var (library, collectionManager, logger) = MakeMocks(items, new BaseItem[] { existing });

            var config = new PluginConfiguration
            {
                DeleteEmptyCollections = true,
                Rules = new[] { Rule("Formel 1", RuleMatchType.Regex, @"(?i)\bformel\s*1\b") }
            };

            var sut = new CollectionSyncService(
                library.Object,
                collectionManager.Object,
                logger.Object,
                collectionMemberIdsProvider: _ => new long[] { 1 });

            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            library.Verify(m => m.DeleteItem(existing, It.Is<DeleteOptions>(o => o.DeleteFileLocation == false)), Times.Once);
            collectionManager.Verify(m => m.RemoveFromCollection(It.IsAny<BoxSet>(), It.IsAny<long[]>()), Times.Never);
            Assert.True(result.Collections.Single().Deleted);
        }

        [Fact]
        public async Task KeepsEmptyCollection_ButRemovesStaleMembers_WhenDeleteNotConfigured()
        {
            var items = new BaseItem[] { MakeItem(1, "Nothing matches anymore") };
            var existing = MakeExistingCollection(100, "Formel 1");
            var (library, collectionManager, logger) = MakeMocks(items, new BaseItem[] { existing });

            var config = new PluginConfiguration
            {
                DeleteEmptyCollections = false,
                Rules = new[] { Rule("Formel 1", RuleMatchType.Regex, @"(?i)\bformel\s*1\b") }
            };

            var sut = new CollectionSyncService(
                library.Object,
                collectionManager.Object,
                logger.Object,
                collectionMemberIdsProvider: _ => new long[] { 42 });

            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            library.Verify(m => m.DeleteItem(It.IsAny<BaseItem>(), It.IsAny<DeleteOptions>()), Times.Never);
            collectionManager.Verify(m => m.RemoveFromCollection(existing, It.Is<long[]>(ids => ids.Length == 1 && ids[0] == 42)), Times.Once);
        }

        [Fact]
        public async Task DisabledRule_IsIgnoredEntirely()
        {
            var items = new BaseItem[] { MakeItem(1, "Formel 1 Rennen") };
            var (library, collectionManager, logger) = MakeMocks(items);

            var config = new PluginConfiguration
            {
                Rules = new[] { Rule("Formel 1", RuleMatchType.Regex, @"(?i)\bformel\s*1\b", enabled: false) }
            };

            var sut = new CollectionSyncService(library.Object, collectionManager.Object, logger.Object);
            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            collectionManager.Verify(m => m.CreateCollection(It.IsAny<CollectionCreationOptions>()), Times.Never);
            Assert.False(result.RuleHitCounts.ContainsKey("Formel 1"));
        }

        [Fact]
        public async Task InvalidRegexRule_IsSkippedAndRecordedAsError_WithoutBlockingOtherRules()
        {
            var items = new BaseItem[]
            {
                MakeItem(1, "Formel 1 Rennen"),
                MakeItem(2, "irrelevant")
            };
            var (library, collectionManager, logger) = MakeMocks(items);

            var config = new PluginConfiguration
            {
                Rules = new[]
                {
                    Rule("Broken Rule", RuleMatchType.Regex, "(unterminated["),
                    Rule("Formel 1", RuleMatchType.Regex, @"(?i)\bformel\s*1\b")
                }
            };

            var sut = new CollectionSyncService(library.Object, collectionManager.Object, logger.Object);
            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            Assert.True(result.RuleErrors.ContainsKey("Broken Rule"));
            collectionManager.Verify(
                m => m.CreateCollection(It.Is<CollectionCreationOptions>(o => o.Name == "Formel 1")),
                Times.Once);
            collectionManager.Verify(
                m => m.CreateCollection(It.Is<CollectionCreationOptions>(o => o.Name == "Broken Rule")),
                Times.Never);
        }

        [Fact]
        public async Task MultipleRulesTargetingSameCollection_UnionTheirMatches()
        {
            var items = new BaseItem[]
            {
                MakeItem(1, "heute-show extra"),
                MakeItem(2, "ZDF Magazin Royale"),
                MakeItem(3, "irrelevant")
            };
            var (library, collectionManager, logger) = MakeMocks(items);

            var config = new PluginConfiguration
            {
                Rules = new[]
                {
                    Rule("ZDF Satire", RuleMatchType.Regex, @"(?i)\bheute[- ]show\b"),
                    Rule("ZDF Satire", RuleMatchType.Contains, "ZDF Magazin Royale")
                }
            };

            var sut = new CollectionSyncService(library.Object, collectionManager.Object, logger.Object);
            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            collectionManager.Verify(
                m => m.CreateCollection(It.Is<CollectionCreationOptions>(o =>
                    o.Name == "ZDF Satire" && o.ItemIdList.Length == 0)),
                Times.Once);
            collectionManager.Verify(
                m => m.AddToCollection(9999, It.Is<long[]>(ids => ids.OrderBy(x => x).SequenceEqual(new long[] { 1, 2 }))),
                Times.Once);
        }

        [Fact]
        public async Task ItemTypeFilter_ExcludesNonMatchingTypes()
        {
            var video = MakeItem(1, "Formel 1 Rennen");
            var boxSetLikeButNotMatchingType = new BoxSet { InternalId = 2, Name = "Formel 1 Rennen (weird type)" };
            var items = new BaseItem[] { video, boxSetLikeButNotMatchingType };
            var (library, collectionManager, logger) = MakeMocks(items);

            var config = new PluginConfiguration
            {
                Rules = new[]
                {
                    Rule("Formel 1", RuleMatchType.Regex, @"(?i)\bformel\s*1\b", itemTypeFilter: new[] { "Video" })
                }
            };

            var sut = new CollectionSyncService(library.Object, collectionManager.Object, logger.Object);
            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            collectionManager.Verify(
                m => m.CreateCollection(It.Is<CollectionCreationOptions>(o => o.ItemIdList.Length == 0)),
                Times.Once);
            collectionManager.Verify(
                m => m.AddToCollection(9999, It.Is<long[]>(ids => ids.Length == 1 && ids[0] == 1)),
                Times.Once);
        }

        [Fact]
        public async Task ItemsWithNoTitle_AreSkipped_AndCounted()
        {
            var items = new BaseItem[] { MakeItem(1, null), MakeItem(2, "") , MakeItem(3, "Formel 1 Rennen")};
            var (library, collectionManager, logger) = MakeMocks(items);

            var config = new PluginConfiguration
            {
                Rules = new[] { Rule("Formel 1", RuleMatchType.Regex, @"(?i)\bformel\s*1\b") }
            };

            var sut = new CollectionSyncService(library.Object, collectionManager.Object, logger.Object);
            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            Assert.Equal(2, result.ItemsSkippedNoTitle);
            Assert.Equal(3, result.ItemsScanned);
        }

        [Fact]
        public async Task NoRulesConfigured_DoesNothing_AndDoesNotThrow()
        {
            var (library, collectionManager, logger) = MakeMocks(Array.Empty<BaseItem>());
            var config = new PluginConfiguration();

            var sut = new CollectionSyncService(library.Object, collectionManager.Object, logger.Object);
            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            collectionManager.Verify(m => m.CreateCollection(It.IsAny<CollectionCreationOptions>()), Times.Never);
            Assert.Empty(result.Collections);
        }

        [Fact]
        public async Task Cancellation_StopsTheRun()
        {
            var items = new BaseItem[] { MakeItem(1, "Formel 1 Rennen") };
            var (library, collectionManager, logger) = MakeMocks(items);

            var config = new PluginConfiguration
            {
                Rules = new[] { Rule("Formel 1", RuleMatchType.Regex, @"(?i)\bformel\s*1\b") }
            };

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var sut = new CollectionSyncService(library.Object, collectionManager.Object, logger.Object);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => sut.SyncAsync(config, null, cts.Token));
        }

        // --- Issue #10: hardening/verification gap-fill tests below ---

        [Fact]
        public async Task ItemWithNullPath_AndAlsoMatchFileNameTrue_DoesNotThrow_AndSimplyDoesNotMatchViaFileName()
        {
            // Name is present (so the item isn't skipped as "no title"), Path is null (common for
            // items without a resolvable file path), and the rule falls back to filename matching.
            // Must not throw - a null fallback field is just "doesn't match", per RuleMatcher's
            // null/empty-safe contract (see RuleMatcherTests), exercised here through the full
            // CollectionSyncService pipeline with a real BaseItem instance.
            var items = new BaseItem[] { MakeItem(1, "Some unrelated title", path: null) };
            var (library, collectionManager, logger) = MakeMocks(items);

            var config = new PluginConfiguration
            {
                Rules = new[]
                {
                    Rule("Formel 1", RuleMatchType.Contains, "Formel", alsoMatchFileName: true)
                }
            };

            var sut = new CollectionSyncService(library.Object, collectionManager.Object, logger.Object);
            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            Assert.Equal(0, result.RuleHitCounts["Formel 1"]);
            Assert.Empty(result.Errors);
            Assert.Empty(result.Collections);
            collectionManager.Verify(m => m.CreateCollection(It.IsAny<CollectionCreationOptions>()), Times.Never);
        }

        [Fact]
        public async Task ItemWithNullPath_AndAlsoMatchFileNameTrue_StillMatchesViaPrimaryField()
        {
            // Same null-Path setup, but this time the primary field does match, so the filename
            // fallback (null) is never even consulted - included as a companion to the case above to
            // show both branches of the null-Path/AlsoMatchFileName combination are safe.
            var items = new BaseItem[] { MakeItem(1, "Formel 1 Rennen", path: null) };
            var (library, collectionManager, logger) = MakeMocks(items);

            var config = new PluginConfiguration
            {
                Rules = new[]
                {
                    Rule("Formel 1", RuleMatchType.Contains, "Formel", alsoMatchFileName: true)
                }
            };

            var sut = new CollectionSyncService(library.Object, collectionManager.Object, logger.Object);
            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            Assert.Equal(1, result.RuleHitCounts["Formel 1"]);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public async Task InvalidRegexRule_DefinedBeforeAValidRule_StillLetsTheValidRuleRun_RegardlessOfArrayOrder()
        {
            // Same intent as InvalidRegexRule_IsSkippedAndRecordedAsError_WithoutBlockingOtherRules,
            // but with a *third*, later-still valid rule added after the broken one, and the broken
            // rule placed first specifically to guard against any hidden ordering dependency (e.g.
            // an early-exit on first error) creeping in over time.
            var items = new BaseItem[]
            {
                MakeItem(1, "Formel 1 Rennen"),
                MakeItem(2, "ZDF Magazin Royale")
            };
            var (library, collectionManager, logger) = MakeMocks(items);

            var config = new PluginConfiguration
            {
                Rules = new[]
                {
                    Rule("Broken Rule", RuleMatchType.Regex, "(["),
                    Rule("Formel 1", RuleMatchType.Regex, @"(?i)\bformel\s*1\b"),
                    Rule("ZDF Magazin Royale", RuleMatchType.Contains, "ZDF Magazin Royale")
                }
            };

            var sut = new CollectionSyncService(library.Object, collectionManager.Object, logger.Object);
            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            Assert.True(result.RuleErrors.ContainsKey("Broken Rule"));
            Assert.False(string.IsNullOrEmpty(result.RuleErrors["Broken Rule"]));
            Assert.Equal(1, result.RuleHitCounts["Formel 1"]);
            Assert.Equal(1, result.RuleHitCounts["ZDF Magazin Royale"]);
            collectionManager.Verify(
                m => m.CreateCollection(It.Is<CollectionCreationOptions>(o => o.Name == "Formel 1")),
                Times.Once);
            collectionManager.Verify(
                m => m.CreateCollection(It.Is<CollectionCreationOptions>(o => o.Name == "ZDF Magazin Royale")),
                Times.Once);
        }

        [Fact]
        public async Task OneCollectionFailure_DoesNotPreventOtherCollectionsInTheSameRunFromSucceeding()
        {
            // Two independent rules targeting two existing collections. AddToCollection throws for
            // collection "A" only; collection "B" must still be reconciled successfully in the same
            // SyncAsync call, and the failure on "A" must be recorded, not thrown up to the caller.
            var items = new BaseItem[]
            {
                MakeItem(1, "Collection A item"),
                MakeItem(2, "Collection B item")
            };
            var existingA = MakeExistingCollection(100, "Collection A");
            var existingB = MakeExistingCollection(200, "Collection B");
            var (library, collectionManager, logger) = MakeMocks(items, new BaseItem[] { existingA, existingB });

            collectionManager
                .Setup(m => m.AddToCollection(100, It.IsAny<long[]>()))
                .ThrowsAsync(new InvalidOperationException("simulated failure for collection A"));
            collectionManager
                .Setup(m => m.AddToCollection(200, It.IsAny<long[]>()))
                .Returns(Task.CompletedTask);

            var config = new PluginConfiguration
            {
                Rules = new[]
                {
                    Rule("Collection A", RuleMatchType.Contains, "Collection A"),
                    Rule("Collection B", RuleMatchType.Contains, "Collection B")
                }
            };

            var sut = new CollectionSyncService(
                library.Object,
                collectionManager.Object,
                logger.Object,
                collectionMemberIdsProvider: _ => Array.Empty<long>());

            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            // "A" failed and is recorded as an error with enough context to act on (collection name,
            // item ids, and the underlying exception message).
            Assert.Contains(result.Errors, e =>
                e.Contains("Collection A", StringComparison.Ordinal) &&
                e.Contains("simulated failure for collection A", StringComparison.Ordinal));
            Assert.DoesNotContain(result.Collections, c => c.CollectionName == "Collection A");

            // "B" was still fully processed in the same run.
            Assert.Contains(result.Collections, c => c.CollectionName == "Collection B" && c.ItemsAdded == 1);
            collectionManager.Verify(m => m.AddToCollection(200, It.IsAny<long[]>()), Times.Once);
        }

        [Fact]
        public async Task Cancellation_MidRun_DoesNotApplyAnyPartialCollectionChanges_AndASubsequentFullRunHeals()
        {
            // Simulate cancellation being requested while the item scan is still in progress (e.g. an
            // external shutdown signal). Since collection reconciliation only happens after the full
            // item scan completes, and the reconciliation loop itself re-checks the token before each
            // collection, a cancellation raised during scanning must result in zero collection writes -
            // there is no partial/half-applied collection state to clean up. A second, uncancelled run
            // against the same (now-external) state must then reach the correct end state on its own.
            var items = new BaseItem[] { MakeItem(1, "Formel 1 Rennen") };

            var library = new Mock<ILibraryManager>();
            var collectionManager = new Mock<ICollectionManager>();
            var logger = new Mock<ILogger>();

            using var cts = new CancellationTokenSource();

            library
                .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns((InternalItemsQuery q) =>
                {
                    if (q.IncludeItemTypes != null && q.IncludeItemTypes.Contains("BoxSet"))
                    {
                        return Array.Empty<BaseItem>();
                    }

                    // Simulate an external cancellation request arriving while this page is being
                    // fetched. The loop's next iteration (or the reconciliation loop right after)
                    // will observe it via ThrowIfCancellationRequested().
                    cts.Cancel();
                    return items;
                });

            collectionManager
                .Setup(m => m.CreateCollection(It.IsAny<CollectionCreationOptions>()))
                .ReturnsAsync((CollectionCreationOptions o) => new BoxSet { Name = o.Name, InternalId = 9999 });
            collectionManager
                .Setup(m => m.AddToCollection(It.IsAny<long>(), It.IsAny<long[]>()))
                .Returns(Task.CompletedTask);

            var config = new PluginConfiguration
            {
                Rules = new[] { Rule("Formel 1", RuleMatchType.Regex, @"(?i)\bformel\s*1\b") }
            };

            var sut = new CollectionSyncService(library.Object, collectionManager.Object, logger.Object);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => sut.SyncAsync(config, null, cts.Token));

            // No partial write happened: the cancelled run never reached collection reconciliation.
            collectionManager.Verify(m => m.CreateCollection(It.IsAny<CollectionCreationOptions>()), Times.Never);
            collectionManager.Verify(m => m.AddToCollection(It.IsAny<long>(), It.IsAny<long[]>()), Times.Never);

            // A subsequent, uncancelled run against the same (unaffected) library state reaches the
            // correct end state - the earlier cancellation left nothing to heal, but the run still
            // completes normally and produces the expected collection.
            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            collectionManager.Verify(
                m => m.CreateCollection(It.Is<CollectionCreationOptions>(o => o.Name == "Formel 1" && o.ItemIdList.Length == 0)),
                Times.Once);
            collectionManager.Verify(
                m => m.AddToCollection(9999, It.Is<long[]>(ids => ids.Length == 1)),
                Times.Once);
            Assert.Single(result.Collections);
            Assert.True(result.Collections[0].Created);
        }

        [Fact]
        public async Task LargeLibrarySimulation_TenThousandItems_CompletesCorrectlyAndWithinABoundedTime()
        {
            // Issue #10 acceptance criterion: "simulate >= 10k items and document runtime/memory
            // behavior". This is an in-memory algorithmic simulation, not a real Emby server under
            // real I/O - see docs/performance-notes.md for the honest scope of what this does and
            // does not validate. The mock below actually respects StartIndex/Limit (i.e. it paginates
            // for real rather than returning everything on the first call), matching the realistic
            // shape of a live ILibraryManager.GetItemList implementation, so this also exercises the
            // paging loop itself rather than just the per-item matching cost.
            const int itemCount = 12_000;
            var allItems = new BaseItem[itemCount];
            for (var i = 0; i < itemCount; i++)
            {
                // Every 7th item matches "Formel 1"; every 11th matches "heute-show"; the rest matches
                // neither - a realistic mix of hits and misses across a large library.
                string name;
                if (i % 7 == 0)
                {
                    name = $"20260101 0100 - Sender HD - Formel 1 Rennen {i}";
                }
                else if (i % 11 == 0)
                {
                    name = $"20260101 0100 - Sender HD - heute-show extra {i}";
                }
                else
                {
                    name = $"20260101 0100 - Sender HD - Irrelevant Recording {i}";
                }

                allItems[i] = MakeItem(i, name);
            }

            var library = new Mock<ILibraryManager>();
            library
                .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns((InternalItemsQuery q) =>
                {
                    if (q.IncludeItemTypes != null && q.IncludeItemTypes.Contains("BoxSet"))
                    {
                        return Array.Empty<BaseItem>();
                    }

                    // Real pagination: only return the requested [StartIndex, StartIndex+Limit) slice,
                    // exactly as a real ILibraryManager would for a paged query.
                    var start = q.StartIndex ?? 0;
                    var limit = q.Limit ?? allItems.Length;
                    return allItems.Skip(start).Take(limit).ToArray();
                });

            var collectionManager = new Mock<ICollectionManager>();
            collectionManager
                .Setup(m => m.CreateCollection(It.IsAny<CollectionCreationOptions>()))
                .ReturnsAsync((CollectionCreationOptions o) => new BoxSet { Name = o.Name, InternalId = 9999 });
            collectionManager
                .Setup(m => m.AddToCollection(It.IsAny<long>(), It.IsAny<long[]>()))
                .Returns(Task.CompletedTask);

            var logger = new Mock<ILogger>();

            var config = new PluginConfiguration
            {
                Rules = new[]
                {
                    Rule("Formel 1", RuleMatchType.Regex, @"(?i)\bformel\s*1\b"),
                    Rule("heute-show", RuleMatchType.Regex, @"(?i)\bheute[- ]show\b"),
                    Rule("Irrelevant Catch-All", RuleMatchType.Contains, "Irrelevant Recording")
                }
            };

            var sut = new CollectionSyncService(library.Object, collectionManager.Object, logger.Object);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await sut.SyncAsync(config, null, CancellationToken.None);
            stopwatch.Stop();

            Assert.Equal(itemCount, result.ItemsScanned);
            Assert.Equal(0, result.ItemsSkippedNoTitle);

            var expectedFormel1 = Enumerable.Range(0, itemCount).Count(i => i % 7 == 0);
            var expectedHeuteShow = Enumerable.Range(0, itemCount).Count(i => i % 7 != 0 && i % 11 == 0);
            var expectedIrrelevant = itemCount - expectedFormel1 - expectedHeuteShow;

            Assert.Equal(expectedFormel1, result.RuleHitCounts["Formel 1"]);
            Assert.Equal(expectedHeuteShow, result.RuleHitCounts["heute-show"]);
            Assert.Equal(expectedIrrelevant, result.RuleHitCounts["Irrelevant Catch-All"]);
            Assert.Equal(3, result.Collections.Count);
            Assert.Empty(result.Errors);

            // Regression guard against something accidentally becoming quadratic: this is a generous
            // bound (a correctly-behaving run of this size takes well under a second on typical CI
            // hardware; observed wall-clock time is reported in the issue #10 write-up), not a tight
            // performance assertion - it exists only to catch an O(n^2) (or worse) regression, not to
            // benchmark the implementation.
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"Sync of {itemCount} items took {stopwatch.Elapsed}, which is suspiciously slow - possible algorithmic regression.");
        }

        /// <summary>
        /// Live TV guide entries must never be treated as collection members.
        ///
        /// On a DVR setup they massively outnumber real recordings (3409 LiveTvProgram entries vs
        /// 188 recordings on the server this was found on), and Emby silently refuses them:
        /// AddToCollection accepts the call but never persists the membership, so the same items
        /// were re-added on every run, and CreateCollection returns null outright when every
        /// candidate is a LiveTvProgram - meaning the collection was never created at all.
        /// </summary>
        [Fact]
        public async Task LiveTvGuideEntries_AreNeverCollectionMembers()
        {
            var recording = MakeItem(1, "Formel 1 Rennen");
            var guideEntry = new LiveTvProgram { InternalId = 2, Name = "Formel 1 Qualifying" };

            var (library, collectionManager, logger) = MakeMocks(new BaseItem[] { recording, guideEntry });

            var config = new PluginConfiguration
            {
                Rules = new[] { Rule("Formel 1", RuleMatchType.Contains, "Formel 1") }
            };

            var sut = new CollectionSyncService(library.Object, collectionManager.Object, logger.Object);
            var result = await sut.SyncAsync(config, null, CancellationToken.None);

            // Only the real recording counts as a match, and only it is ever added.
            Assert.Equal(1, result.RuleHitCounts["Formel 1"]);
            collectionManager.Verify(
                m => m.AddToCollection(9999, It.Is<long[]>(ids => ids.Length == 1 && ids[0] == 1)),
                Times.Once);
        }

        /// <summary>
        /// Regression guard for a live-server failure that no mock can reproduce (see
        /// docs/emby-api-cheatsheet.md, "DtoOptions decides whether items have an identity at all").
        ///
        /// On a real Emby 4.9.5 server, querying with <c>new DtoOptions(false)</c> returns items
        /// whose <c>InternalId</c> is 0 and whose <c>Id</c> is <see cref="Guid.Empty"/>, while
        /// names and types look perfectly fine. Since <c>ICollectionManager</c> addresses
        /// everything by <c>InternalId</c>, the sync then silently does nothing while reporting
        /// success: every rule collapses to "1 item" (all IDs are the same 0 in a HashSet),
        /// CreateCollection persists no BoxSet, and AddToCollection throws "No collection exists
        /// with the supplied Id".
        ///
        /// Mocks happily return fully-populated items regardless of DtoOptions, so the only thing
        /// a unit test can protect is the request itself: every query this service issues must ask
        /// for all fields.
        /// </summary>
        [Fact]
        public async Task SyncAsync_AlwaysQueriesWithAllFields_SoReturnedItemsCarryTheirInternalId()
        {
            var capturedQueries = new List<InternalItemsQuery>();

            var library = new Mock<ILibraryManager>();
            library
                .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
                .Returns((InternalItemsQuery q) =>
                {
                    capturedQueries.Add(q);
                    return q.IncludeItemTypes != null && q.IncludeItemTypes.Contains("BoxSet")
                        ? Array.Empty<BaseItem>()
                        : new BaseItem[] { MakeItem(1, "Formel 1 - Qualifying") };
                });

            var collections = new Mock<ICollectionManager>();
            collections
                .Setup(m => m.CreateCollection(It.IsAny<CollectionCreationOptions>()))
                .ReturnsAsync((CollectionCreationOptions o) => new BoxSet { Name = o.Name, InternalId = 9999 });

            var config = new PluginConfiguration
            {
                Rules = new[] { Rule("Formel 1", RuleMatchType.Contains, "Formel 1") }
            };

            var service = new CollectionSyncService(library.Object, collections.Object, new Mock<ILogger>().Object);
            await service.SyncAsync(config, null, CancellationToken.None);

            Assert.NotEmpty(capturedQueries);

            // "All fields" is expressed as whatever DtoOptions(true) produces, so this stays correct
            // if Emby ever adds new ItemFields values.
            var allFieldsCount = new DtoOptions(true).Fields?.Length ?? 0;

            foreach (var query in capturedQueries)
            {
                Assert.NotNull(query.DtoOptions);
                Assert.Equal(allFieldsCount, query.DtoOptions.Fields?.Length ?? 0);
            }
        }
    }
}
