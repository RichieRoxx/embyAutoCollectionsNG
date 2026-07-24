using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.AutoCollectionsNG.Configuration;
using Emby.AutoCollectionsNG.Matching;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;

namespace Emby.AutoCollectionsNG.Sync
{
    /// <summary>
    /// Reconciles Emby collections (BoxSets) against the configured <see cref="CollectionRule"/>s.
    /// Idempotent: running twice with no data/config change makes zero writes.
    ///
    /// Sync target decision (issue #3, see docs/api-notes.md): BoxSets, via
    /// <see cref="ICollectionManager"/>. Only collections whose name matches a rule's
    /// <see cref="CollectionRule.CollectionName"/> are ever touched — any other collection in the
    /// library is left alone, so this plugin only owns what it configured.
    /// </summary>
    public class CollectionSyncService
    {
        // Fetch items in pages rather than one giant array, so memory stays bounded on large libraries.
        private const int PageSize = 500;

        private readonly ILibraryManager _libraryManager;
        private readonly ICollectionManager _collectionManager;
        private readonly ILogger _logger;
        private readonly RuleMatcher _ruleMatcher;

        // Memoizes "top-level ancestor name" per folder InternalId, since walking BaseItem.Parent to
        // the root for every single item would otherwise repeat the same walk for every item that
        // shares a library.
        private readonly Dictionary<long, string> _topAncestorNameCache = new Dictionary<long, string>();

        // Seam for reading a collection's current member IDs, so tests can substitute canned
        // membership for a given collection instance (a bare BoxSet has no live host behind it and
        // can never report members).
        private readonly Func<BaseItem, long[]> _collectionMemberIdsProvider;

        public CollectionSyncService(
            ILibraryManager libraryManager,
            ICollectionManager collectionManager,
            ILogger logger,
            RuleMatcher ruleMatcher = null,
            Func<BaseItem, long[]> collectionMemberIdsProvider = null)
        {
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _collectionManager = collectionManager ?? throw new ArgumentNullException(nameof(collectionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ruleMatcher = ruleMatcher ?? new RuleMatcher();
            _collectionMemberIdsProvider = collectionMemberIdsProvider ?? GetCollectionMemberIds;
        }

        /// <summary>
        /// Reads a collection's current members by querying the library for its children, rather
        /// than calling <c>Folder.GetChildren</c> on the fetched BoxSet.
        ///
        /// <c>Folder.GetChildren</c> returns nothing here even for a real, fully-identified BoxSet
        /// loaded from the library (observed on a live Emby 4.9.5 server: a collection with 90
        /// persisted members reported zero children). Because the reconciliation diffs desired
        /// against current membership, that made every run re-add every member - the sync logged
        /// "+107 / -0" on a collection that already contained those items, breaking the idempotency
        /// requirement. Querying by <see cref="InternalItemsQuery.ParentIds"/> is how Emby's own
        /// clients list a collection's contents.
        /// </summary>
        private long[] GetCollectionMemberIds(BaseItem collection)
        {
            if (collection == null || collection.InternalId == 0)
            {
                return Array.Empty<long>();
            }

            var members = _libraryManager.GetItemList(new InternalItemsQuery
            {
                ParentIds = new[] { collection.InternalId },
                // All fields, or the returned members come back with InternalId 0 - see BuildItemQuery.
                DtoOptions = new DtoOptions(true)
            });

            return (members ?? Array.Empty<BaseItem>()).Select(c => c.InternalId).ToArray();
        }

        public async Task<SyncResult> SyncAsync(PluginConfiguration config, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var result = new SyncResult();
            _topAncestorNameCache.Clear();

            var rules = config?.Rules ?? Array.Empty<CollectionRule>();
            var enabledRules = rules.Where(r => r != null && r.Enabled).ToArray();

            var validRules = new List<CollectionRule>();
            foreach (var rule in enabledRules)
            {
                if (string.IsNullOrWhiteSpace(rule.CollectionName))
                {
                    _logger.Error("Auto Collections NG: skipping a rule with no CollectionName set.");
                    continue;
                }

                if (!_ruleMatcher.TryCompile(rule, out var error))
                {
                    _logger.Error("Auto Collections NG: rule '{0}' has an invalid pattern and will be skipped: {1}", rule.CollectionName, error);
                    result.RuleErrors[rule.CollectionName] = error;
                    continue;
                }

                validRules.Add(rule);
            }

            if (validRules.Count == 0)
            {
                _logger.Info("Auto Collections NG: no valid enabled rules, nothing to sync.");
                return result;
            }

            // Per-rule matched item IDs, then unioned by target collection name below - multiple
            // rules may target the same collection.
            var perRuleMatches = validRules.ToDictionary(r => r, _ => new HashSet<long>());

            var query = BuildItemQuery(validRules);
            var startIndex = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                query.StartIndex = startIndex;
                query.Limit = PageSize;

                BaseItem[] page;
                try
                {
                    page = _libraryManager.GetItemList(query);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to query items at offset {startIndex}: {ex.Message}");
                    _logger.ErrorException("Auto Collections NG: failed to query items at offset {0}", ex, startIndex);
                    break;
                }

                if (page == null || page.Length == 0)
                {
                    break;
                }

                foreach (var item in page)
                {
                    ProcessItem(item, validRules, perRuleMatches, result);
                }

                result.ItemsScanned += page.Length;
                progress?.Report(Math.Min(99, startIndex));

                if (page.Length < PageSize)
                {
                    break;
                }

                startIndex += PageSize;
            }

            foreach (var rule in validRules)
            {
                result.RuleHitCounts[rule.CollectionName] = result.RuleHitCounts.TryGetValue(rule.CollectionName, out var existing)
                    ? existing + perRuleMatches[rule].Count
                    : perRuleMatches[rule].Count;
            }

            var desiredByCollectionName = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
            foreach (var rule in validRules)
            {
                if (!desiredByCollectionName.TryGetValue(rule.CollectionName, out var set))
                {
                    set = new HashSet<long>();
                    desiredByCollectionName[rule.CollectionName] = set;
                }

                set.UnionWith(perRuleMatches[rule]);
            }

            var existingCollectionsByName = LoadExistingCollections(cancellationToken, result);

            var targetCollectionNames = validRules.Select(r => r.CollectionName).Distinct(StringComparer.Ordinal);
            foreach (var collectionName in targetCollectionNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var desiredIds = desiredByCollectionName.TryGetValue(collectionName, out var ids) ? ids : new HashSet<long>();
                existingCollectionsByName.TryGetValue(collectionName, out var existing);

                await ReconcileCollectionAsync(collectionName, desiredIds, existing, config?.DeleteEmptyCollections ?? false, result)
                    .ConfigureAwait(false);
            }

            progress?.Report(100);
            return result;
        }

        // Caps how many internal IDs get spelled out in an error message, so a failure on a
        // collection with thousands of pending adds/removes doesn't produce an unreadable log line -
        // the count is already logged separately, this just adds "which ones" context for the common
        // case of a handful of items.
        private const int MaxIdsInErrorMessage = 20;

        private static string FormatIds(long[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                return string.Empty;
            }

            if (ids.Length <= MaxIdsInErrorMessage)
            {
                return string.Join(", ", ids);
            }

            return string.Join(", ", ids.Take(MaxIdsInErrorMessage)) + $", … (+{ids.Length - MaxIdsInErrorMessage} more)";
        }

        private InternalItemsQuery BuildItemQuery(List<CollectionRule> validRules)
        {
            var query = new InternalItemsQuery
            {
                Recursive = true,
                // MUST be all-fields. With `new DtoOptions(false)` the Emby item repository does
                // not materialize the identity columns: every returned BaseItem came back with
                // InternalId == 0 and Id == Guid.Empty (verified on a live Emby 4.9.5 server, and
                // confirmed via reflection - it is not an assembly mismatch). That silently broke
                // the entire sync, since ICollectionManager addressses items by InternalId: all
                // matched IDs collapsed to the single value 0 in the per-rule HashSet, so every
                // rule reported "1 item", CreateCollection received ItemIdList=[0] and persisted
                // no BoxSet at all, and AddToCollection threw "No collection exists with the
                // supplied Id". Do not "optimize" this back to minimal fields without re-verifying
                // that InternalId survives. See docs/emby-api-cheatsheet.md, "Querying items".
                DtoOptions = new DtoOptions(true)
            };

            // If every rule restricts item types, push the union down into the query as a perf
            // optimization. If any rule wants "all types" (empty filter), we can't restrict globally
            // without missing items for that rule, so leave IncludeItemTypes unset in that case.
            var allRulesRestrictTypes = validRules.All(r => r.ItemTypeFilter != null && r.ItemTypeFilter.Length > 0);
            if (allRulesRestrictTypes)
            {
                query.IncludeItemTypes = validRules
                    .SelectMany(r => r.ItemTypeFilter)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            return query;
        }

        private void ProcessItem(
            BaseItem item,
            List<CollectionRule> validRules,
            Dictionary<CollectionRule, HashSet<long>> perRuleMatches,
            SyncResult result)
        {
            var rawTitle = item.Name;
            if (string.IsNullOrWhiteSpace(rawTitle))
            {
                result.ItemsSkippedNoTitle++;
                return;
            }

            string normalizedTitle;
            try
            {
                normalizedTitle = TitleNormalizer.Normalize(rawTitle);
            }
            catch (Exception ex)
            {
                // Defensive: normalization must never abort a whole run over one odd title.
                _logger.ErrorException("Auto Collections NG: failed to normalize title '{0}'", ex, rawTitle);
                normalizedTitle = rawTitle;
            }

            var fileNameOrPath = item.Path;
            var itemTypeName = item.GetType().Name;
            var libraryName = GetTopAncestorName(item);

            foreach (var rule in validRules)
            {
                if (rule.LibraryFilter != null && rule.LibraryFilter.Length > 0
                    && !rule.LibraryFilter.Any(f => string.Equals(f, libraryName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (rule.ItemTypeFilter != null && rule.ItemTypeFilter.Length > 0
                    && !rule.ItemTypeFilter.Any(f => string.Equals(f, itemTypeName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                bool isMatch;
                try
                {
                    isMatch = _ruleMatcher.IsMatch(rule, rawTitle, normalizedTitle, fileNameOrPath);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Rule '{rule.CollectionName}' failed while matching item '{rawTitle}': {ex.Message}");
                    _logger.ErrorException("Auto Collections NG: rule '{0}' failed while matching item '{1}'", ex, rule.CollectionName, rawTitle);
                    continue;
                }

                if (isMatch)
                {
                    perRuleMatches[rule].Add(item.InternalId);
                }
            }
        }

        /// <summary>
        /// Walks up <see cref="BaseItem.Parent"/> to the topmost ancestor and returns its name, used
        /// as a best-effort stand-in for "which library this item belongs to" so
        /// <see cref="CollectionRule.LibraryFilter"/> has something concrete to compare against
        /// without an extra library-resolution API call. This is an MVP design choice, not a
        /// guaranteed match to Emby's internal library-scoping in every possible folder setup -
        /// revisit if it doesn't hold up for a given library topology (see docs/PLAN.md #10).
        /// </summary>
        private string GetTopAncestorName(BaseItem item)
        {
            var current = item.Parent;
            if (current == null)
            {
                return null;
            }

            if (_topAncestorNameCache.TryGetValue(current.InternalId, out var cached))
            {
                return cached;
            }

            var walked = new List<long>();
            Folder top = current;
            while (top.Parent != null)
            {
                walked.Add(top.InternalId);
                top = top.Parent;
            }

            var name = top.Name;
            foreach (var id in walked)
            {
                _topAncestorNameCache[id] = name;
            }

            _topAncestorNameCache[top.InternalId] = name;
            return name;
        }

        private Dictionary<string, BaseItem> LoadExistingCollections(CancellationToken cancellationToken, SyncResult result)
        {
            var byName = new Dictionary<string, BaseItem>(StringComparer.Ordinal);

            BaseItem[] collections;
            try
            {
                collections = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "BoxSet" },
                    Recursive = true,
                    // All-fields for the same reason as BuildItemQuery: with minimal fields the
                    // returned BoxSet also carries InternalId == 0, so the collection could never
                    // be matched to an existing one (every run re-"created" it) and
                    // AddToCollection(existing.InternalId, ...) threw "No collection exists with
                    // the supplied Id".
                    DtoOptions = new DtoOptions(true)
                });
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to query existing collections: {ex.Message}");
                _logger.ErrorException("Auto Collections NG: failed to query existing collections", ex);
                return byName;
            }

            foreach (var collection in collections ?? Array.Empty<BaseItem>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(collection.Name))
                {
                    continue;
                }

                if (byName.ContainsKey(collection.Name))
                {
                    _logger.Warn("Auto Collections NG: found more than one collection named '{0}'; using the first one found and ignoring the rest.", collection.Name);
                    continue;
                }

                byName[collection.Name] = collection;
            }

            return byName;
        }

        /// <summary>
        /// Second attempt at creating a collection, with <see cref="CollectionCreationOptions.ParentId"/>
        /// pointed explicitly at the library's collections folder.
        ///
        /// Background (live Emby 4.9.5): calling <c>CreateCollection</c> with only Name+ItemIdList
        /// returns <c>null</c> and persists nothing - no exception, no server-side log entry. The
        /// host resolves the parent collections folder itself in that case, and a null result is
        /// consistent with that internal lookup coming up empty when nothing tells it which folder
        /// to use.
        ///
        /// The folder is identified by <c>CollectionType == "boxsets"</c> rather than by its
        /// display name, which is user-visible and localizable. <c>CollectionType</c> is read
        /// reflectively on purpose: it is not part of the API surface confirmed in
        /// docs/emby-api-cheatsheet.md, so this reads it defensively and falls back to matching the
        /// folder name instead of assuming a compile-time member that may not exist.
        /// </summary>
        private async Task<BoxSet> TryCreateInCollectionsFolderAsync(
            string collectionName,
            HashSet<long> desiredIds,
            SyncResult result)
        {
            BaseItem[] folders;
            try
            {
                folders = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { "CollectionFolder" },
                    Recursive = true,
                    DtoOptions = new DtoOptions(true)
                });
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Auto Collections NG: could not look up the collections folder", ex);
                return null;
            }

            BaseItem target = null;
            foreach (var folder in folders ?? Array.Empty<BaseItem>())
            {
                var collectionType = ReadCollectionType(folder);
                _logger.Info(
                    "Auto Collections NG: candidate library folder '{0}' (internalId {1}, collectionType '{2}').",
                    folder.Name,
                    folder.InternalId,
                    collectionType ?? "<none>");

                if (string.Equals(collectionType, "boxsets", StringComparison.OrdinalIgnoreCase)
                    || (target == null && string.Equals(folder.Name, "Collections", StringComparison.OrdinalIgnoreCase)))
                {
                    target = folder;
                }
            }

            if (target == null || target.InternalId == 0)
            {
                _logger.Error("Auto Collections NG: no usable collections folder found, cannot create '{0}'.", collectionName);
                return null;
            }

            try
            {
                var created = await _collectionManager.CreateCollection(new CollectionCreationOptions
                {
                    Name = collectionName,
                    ItemIdList = desiredIds.ToArray(),
                    ParentId = target.InternalId
                }).ConfigureAwait(false);

                _logger.Info(
                    "Auto Collections NG: retried creating '{0}' under folder '{1}' (internalId {2}) -> {3}.",
                    collectionName,
                    target.Name,
                    target.InternalId,
                    created == null ? "still null" : "internalId " + created.InternalId);

                return created;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Retrying creation of collection '{collectionName}' under the collections folder failed: {ex.Message}");
                _logger.ErrorException("Auto Collections NG: retrying creation of '{0}' failed", ex, collectionName);
                return null;
            }
        }

        /// <summary>
        /// Reads a library folder's <c>CollectionType</c> reflectively, returning null when the
        /// property doesn't exist or can't be read. See <see cref="TryCreateInCollectionsFolderAsync"/>
        /// for why this isn't a direct member access.
        /// </summary>
        private static string ReadCollectionType(BaseItem folder)
        {
            try
            {
                var property = folder.GetType().GetProperty("CollectionType");
                return property?.GetValue(folder) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task ReconcileCollectionAsync(
            string collectionName,
            HashSet<long> desiredIds,
            BaseItem existing,
            bool deleteEmptyCollections,
            SyncResult result)
        {
            var outcome = new CollectionSyncOutcome(collectionName);

            if (existing == null)
            {
                if (desiredIds.Count == 0)
                {
                    // Nothing matches and no collection exists yet - requirement #5: a collection is
                    // only created once at least one item matches.
                    return;
                }

                try
                {
                    // The returned BoxSet is the only way to tell whether the host actually
                    // persisted anything: CreateCollection can return without throwing while no
                    // BoxSet ends up in the library, in which case reporting "created" would be a
                    // phantom success that also breaks the next run's idempotency check.
                    var created = await _collectionManager.CreateCollection(new CollectionCreationOptions
                    {
                        Name = collectionName,
                        ItemIdList = desiredIds.ToArray()
                    }).ConfigureAwait(false);

                    if (created == null || created.InternalId == 0)
                    {
                        // The host bailed out internally without throwing. Its collection manager
                        // resolves a parent "collections" library folder itself when the options
                        // don't name one, and returning null is what that lookup failing looks
                        // like from out here - so retry once, explicitly pointing ParentId at the
                        // library's collections folder.
                        created = await TryCreateInCollectionsFolderAsync(collectionName, desiredIds, result).ConfigureAwait(false);
                    }

                    if (created == null || created.InternalId == 0)
                    {
                        var detail = created == null
                            ? "the host returned no BoxSet"
                            : "the host returned a BoxSet with InternalId 0";
                        result.Errors.Add($"Failed to create collection '{collectionName}': {detail}, so nothing was persisted.");
                        _logger.Error(
                            "Auto Collections NG: creating collection '{0}' did not persist anything - {1}. Requested {2} item(s).",
                            collectionName,
                            detail,
                            desiredIds.Count);
                        return;
                    }

                    outcome.Created = true;
                    outcome.ItemsAdded = desiredIds.Count;
                    _logger.Info(
                        "Auto Collections NG: created collection '{0}' (internalId {1}) with {2} item(s).",
                        collectionName,
                        created.InternalId,
                        desiredIds.Count);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to create collection '{collectionName}': {ex.Message}");
                    _logger.ErrorException("Auto Collections NG: failed to create collection '{0}'", ex, collectionName);
                    return;
                }

                result.Collections.Add(outcome);
                return;
            }

            if (desiredIds.Count == 0 && deleteEmptyCollections)
            {
                try
                {
                    _libraryManager.DeleteItem(existing, new DeleteOptions { DeleteFileLocation = false });
                    outcome.Deleted = true;
                    _logger.Info("Auto Collections NG: deleted empty collection '{0}'.", collectionName);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to delete empty collection '{collectionName}': {ex.Message}");
                    _logger.ErrorException("Auto Collections NG: failed to delete empty collection '{0}'", ex, collectionName);
                    return;
                }

                result.Collections.Add(outcome);
                return;
            }

            if (!(existing is Folder))
            {
                result.Errors.Add($"Collection '{collectionName}' was found but is not a Folder-derived item (actual type: {existing.GetType().Name}); skipping.");
                _logger.Error("Auto Collections NG: collection '{0}' was found but is not a Folder-derived item (actual type: {1}); skipping.", collectionName, existing.GetType().Name);
                return;
            }

            HashSet<long> currentIds;
            try
            {
                currentIds = new HashSet<long>(_collectionMemberIdsProvider(existing) ?? Array.Empty<long>());
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Failed to read current members of collection '{collectionName}': {ex.Message}");
                _logger.ErrorException("Auto Collections NG: failed to read current members of collection '{0}'", ex, collectionName);
                return;
            }

            var toAdd = desiredIds.Where(id => !currentIds.Contains(id)).ToArray();
            var toRemove = currentIds.Where(id => !desiredIds.Contains(id)).ToArray();

            if (toAdd.Length == 0 && toRemove.Length == 0)
            {
                // Idempotency: nothing changed, make zero writes.
                return;
            }

            if (toAdd.Length > 0)
            {
                try
                {
                    await _collectionManager.AddToCollection(existing.InternalId, toAdd).ConfigureAwait(false);
                    outcome.ItemsAdded = toAdd.Length;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to add {toAdd.Length} item(s) (internal ids: {FormatIds(toAdd)}) to collection '{collectionName}': {ex.Message}");
                    _logger.ErrorException("Auto Collections NG: failed to add item(s) (internal ids: {0}) to collection '{1}'", ex, FormatIds(toAdd), collectionName);
                }
            }

            if (toRemove.Length > 0)
            {
                if (existing is BoxSet existingBoxSet)
                {
                    try
                    {
                        _collectionManager.RemoveFromCollection(existingBoxSet, toRemove);
                        outcome.ItemsRemoved = toRemove.Length;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Failed to remove {toRemove.Length} item(s) (internal ids: {FormatIds(toRemove)}) from collection '{collectionName}': {ex.Message}");
                        _logger.ErrorException("Auto Collections NG: failed to remove item(s) (internal ids: {0}) from collection '{1}'", ex, FormatIds(toRemove), collectionName);
                    }
                }
                else
                {
                    // RemoveFromCollection requires a BoxSet specifically; this shouldn't happen since
                    // LoadExistingCollections only queries IncludeItemTypes=["BoxSet"], but guard
                    // against it rather than silently dropping the removal.
                    result.Errors.Add($"Collection '{collectionName}' has {toRemove.Length} stale member(s) but isn't a BoxSet (actual type: {existing.GetType().Name}); skipping removal.");
                    _logger.Error("Auto Collections NG: collection '{0}' has stale members but isn't a BoxSet (actual type: {1}); skipping removal.", collectionName, existing.GetType().Name);
                }
            }

            if (outcome.ItemsAdded > 0 || outcome.ItemsRemoved > 0)
            {
                _logger.Info(
                    "Auto Collections NG: updated collection '{0}': +{1} / -{2}.",
                    collectionName,
                    outcome.ItemsAdded,
                    outcome.ItemsRemoved);
                result.Collections.Add(outcome);
            }
        }
    }
}
