# Emby API Cheat Sheet (verified surface)

Purpose: **prevent fantasy APIs.** Only interfaces/methods with confirmed
existence are listed here.

**Verification method:** the signatures below were confirmed by loading the
actual `MediaBrowser.Common.dll` / `MediaBrowser.Model.dll` /
`MediaBrowser.Controller.dll` assemblies from NuGet package
`mediabrowser.server.core` **4.9.1.90** (latest stable at time of writing;
`MediaBrowser.Common` 4.9.1.90 is a transitive dependency) via .NET
reflection and printing the real public members. This is ground truth for
that package version, not documentation guesswork. If a future package
version changes a signature, update this file and note the version.

Status legend: ✅ confirmed via reflection · ⚠️ plausible, needs runtime/live-server verification · ❓ open (spike)

## Plugin basics

| Element | Status | Signature |
|---|---|---|
| `MediaBrowser.Common.Plugins.BasePlugin<TConfig>` | ✅ | abstract class; ctor `(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)` |
| `Name` | ✅ | `abstract string Name { get; }` — **must** override |
| `Description` | ✅ | `virtual string Description { get; }` — override recommended |
| `Id` | ✅ | `virtual Guid Id { get; }` — override with a fixed GUID, generated once, never changed |
| `Configuration` | ✅ | `TConfigurationType Configuration { get; set; }` |
| `UpdateConfiguration(BasePluginConfiguration)` | ✅ | `virtual void UpdateConfiguration(...)` — called by the host when the admin UI saves |
| `SaveConfiguration()` | ✅ | `virtual void SaveConfiguration()` |
| `MediaBrowser.Common.Configuration.IApplicationPaths` | ✅ | namespace confirmed (not `MediaBrowser.Model.Configuration` as initially assumed) |
| `MediaBrowser.Model.Plugins.BasePluginConfiguration` | ✅ | configuration base class; persisted as XML in the `config` folder |
| `MediaBrowser.Model.Serialization.IXmlSerializer` | ✅ | `SerializeToString/File(object)`, `DeserializeFromString/File/Stream/Bytes(Type, ...)` |
| `MediaBrowser.Controller.Plugins.IServerEntryPoint` | ✅ | `void Run()`; **extends `System.IDisposable`** (confirmed) — implement `Dispose()` for cleanup |
| Dependency injection | ✅ | inject services via constructor (e.g. `ILibraryManager`, `ILogger`, `ICollectionManager`) |

Sources: reflection against `mediabrowser.server.core` 4.9.1.90; dev.emby.media/doc/plugins/dev.

## Querying items

| Element | Status | Signature |
|---|---|---|
| `MediaBrowser.Controller.Library.ILibraryManager` | ✅ | central library access |
| `GetItemList(InternalItemsQuery query)` | ✅ | `BaseItem[] GetItemList(InternalItemsQuery)` — returns an **array**, not `IEnumerable` |
| `GetItemById(Guid id)` / `GetItemById(long id)` | ✅ | both overloads exist — see ID note below |
| `MediaBrowser.Controller.Entities.InternalItemsQuery` | ✅ | large filter class; relevant fields confirmed below |
| `InternalItemsQuery.IncludeItemTypes` | ✅ | `string[]` |
| `InternalItemsQuery.Recursive` | ✅ | `bool` |
| `InternalItemsQuery.Path` | ✅ | `string` — usable for the filename/path fallback match |
| `InternalItemsQuery.StartIndex` / `Limit` | ✅ | `int?` each — use for chunked/paged queries on large libraries |
| `InternalItemsQuery.ParentIds` / `TopParentIds` | ✅ | `long[]` — usable for the library filter |
| `InternalItemsQuery.DtoOptions` | ✅ | `MediaBrowser.Controller.Dto.DtoOptions` — keep minimal for perf |

### Item identity — important gotcha

`BaseItem` carries **two different ID types**:

| Property | Type | Used by |
|---|---|---|
| `BaseItem.Id` | `Guid` | legacy identifier; `GetItemById(Guid)` |
| `BaseItem.InternalId` | `long` (`Int64`) | **this is what `ICollectionManager`/`IPlaylistManager` item-ID parameters expect** |

`BaseItem.Name` (`string`) and `BaseItem.Path` (`string`) are the two
properties the matching engine reads (raw title and filename/path fallback,
respectively).

Source: reflection against `mediabrowser.server.core` 4.9.1.90.

## Collections

| Element | Status | Signature |
|---|---|---|
| `MediaBrowser.Controller.Collections.ICollectionManager` | ✅ | manager for BoxSets/collections |
| `CreateCollection(CollectionCreationOptions options)` | ✅ | `Task<BoxSet> CreateCollection(CollectionCreationOptions)` |
| `AddToCollection(long collectionId, long[] itemIds)` | ✅ | `Task AddToCollection(long, long[])` — **`collectionId`/`itemIds` are `InternalId` (long), not `Guid`** |
| `RemoveFromCollection(BoxSet item, long[] itemIds)` | ✅ | `void RemoveFromCollection(BoxSet, long[])` — **asymmetric with `AddToCollection`: takes the `BoxSet` instance itself, not an ID** |
| `CollectionCreationOptions` | ✅ | `Name` (string), `ParentId` (long), `IsLocked` (bool), `ProviderIds`, `ItemIdList` (long[]), `UserIds` (long[]) |
| Events `CollectionCreated` / `ItemsAddedToCollection` / `ItemsRemovedFromCollection` | ✅ | exist on `ICollectionManager`; relevant for avoiding sync loops in the event trigger (#8) |
| `MediaBrowser.Controller.Entities.BoxSet` | ✅ | extends `Folder` (so it's a container); implements `IItemByName` |
| Reading current members of a BoxSet | ✅ | `Folder.GetChildren(User)` / `Folder.GetRecursiveChildren()` (inherited by `BoxSet`) |
| `MediaBrowser.Controller.Entities.Movies.Movie` | ✅ | extends `Video`; implements `ISupportsBoxSetGrouping` (movie-specific auto-grouping — **not** required for manual `AddToCollection`) |
| `MediaBrowser.Controller.Entities.TV.Episode`, `MediaBrowser.Controller.Entities.Video` | ✅ | both extend `BaseItem`/`Video`; **no type restriction visible on `AddToCollection`'s signature itself** — it takes plain `long[]` IDs regardless of item type |

**❓ OPEN SPIKE (#3) — reflection can't answer this, needs a live server:**
The interface signatures place no compile-time restriction on which item
types can be added to a collection (recordings included), and `BoxSet` is
architecturally just a `Folder`. That is reasonably strong static evidence
the API itself doesn't block recordings. What reflection **cannot** confirm:
whether the Emby **web UI** renders a BoxSet containing `Video`/`Episode`-typed
recordings the same way it renders a movie collection, and whether Live
TV/DVR recordings specifically (as opposed to a `.ts` file resolved as a
plain `Video`) behave differently. This needs an actual Emby server with
sample recordings — track the result here once available. Fallback:
`IPlaylistManager` (see below).

Sources: reflection against `mediabrowser.server.core` 4.9.1.90 (this session);
Emby community "Get/Create Collections Plugin Service"; Jellyfin
`CollectionManager.cs` (API-related, but **not** identical 1:1 — do not copy
blindly).

## Playlists (fallback target, #3)

| Element | Status | Signature |
|---|---|---|
| `MediaBrowser.Controller.Playlists.IPlaylistManager` | ✅ | confirmed to exist |
| `CreatePlaylist(PlaylistCreationRequest)` | ✅ | `Task<PlaylistCreationResult> CreatePlaylist(PlaylistCreationRequest)` |
| `AddToPlaylist(long playlistId, long[] itemIds, User user)` | ✅ | sync variant; there's also an async `Task<AddToPlaylistResult> AddToPlaylist(Playlist, long[], bool skipDuplicates, User, CancellationToken)` |
| `RemoveFromPlaylist(long playlistId, long[] entryIds)` | ✅ | `Task RemoveFromPlaylist(long, long[])` |
| `MediaBrowser.Controller.Playlists.Playlist` | ✅ | extends `Folder`, same shape as `BoxSet` |

Note: playlist entry IDs (`entryIds` for removal) are **not** necessarily the
same as item IDs — `Playlist` has its own entries concept. Verify before using
this as the fallback in #3.

## Scheduled tasks

| Element | Status | Signature |
|---|---|---|
| `MediaBrowser.Model.Tasks.IScheduledTask` | ✅ | `Task Execute(CancellationToken cancellationToken, IProgress<double> progress)`; `IEnumerable<TaskTriggerInfo> GetDefaultTriggers()`; `string Name/Key/Description/Category { get; }` |
| Automatic type discovery | ✅ | task class public in the plugin assembly ⇒ gets discovered by the host |
| `MediaBrowser.Model.Tasks.ITaskManager` | ✅ | `QueueScheduledTask(IScheduledTask task, TaskOptions options)` and a parameterless `QueueScheduledTask<T>()`-style overload set — use this to trigger the task manually (UI button #9, event trigger #8) |
| `ITaskManager.ScheduledTasks` | ✅ | `IScheduledTaskWorker[]` — enumerate to find our task's worker if `QueueScheduledTask(IScheduledTask, ...)` needs a worker instance instead |
| `ITaskManager` events | ✅ | `TaskExecuting`, `TaskCompleted` |

Source: reflection against `mediabrowser.server.core` 4.9.1.90.

## Configuration UI

| Approach | Status | Note |
|---|---|---|
| Declarative plugin UI (auto-generated from config model) | ❓ | attributes like `[DisplayName]`, `[Description]`; **unclear** whether it supports editable lists, action buttons, and dynamic dropdowns (#9) — not reflectable, needs live-server/dashboard testing |
| Classic embedded HTML configuration page | ⚠️ | fallback: `IHasWebPages`/embedded resource + JS `ApiClient` pattern; verify against example plugins |

Source: dev.emby.media/doc/plugins/ui (UI rendering behavior can't be verified via reflection — needs a live dashboard).

## Logging

| Element | Status | Note |
|---|---|---|
| `ILogger` (Emby SDK) | ✅ | via DI; Info = numbers/summary, Debug = item details |

---

**Maintaining this file:** whoever verifies a ⚠️/❓ row changes the status to
✅ and adds the concrete signature plus source (reflection output, live-server
observation, or a linked doc). Do not add anything here that isn't confirmed.
