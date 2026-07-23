using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoCollectionsNG.Configuration;
using Emby.AutoCollectionsNG.Sync;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
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

            collectionManager.Verify(
                m => m.CreateCollection(It.Is<CollectionCreationOptions>(o =>
                    o.Name == "Formel 1" && o.ItemIdList.Length == 1 && o.ItemIdList[0] == 1)),
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
                    o.Name == "ZDF Satire" && o.ItemIdList.OrderBy(x => x).SequenceEqual(new long[] { 1, 2 }))),
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
                m => m.CreateCollection(It.Is<CollectionCreationOptions>(o =>
                    o.ItemIdList.Length == 1 && o.ItemIdList[0] == 1)),
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
    }
}
