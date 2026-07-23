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
| `MediaBrowser.Controller.Entities.BoxSet` | ✅ | extends `Folder` (so it's a container); implements `IItemByName`; public parameterless ctor, `Name`/`InternalId`/`Parent` all publicly settable — constructible directly in unit tests without a live host |
| Reading current members of a BoxSet | ✅ | `Folder.GetChildren(InternalItemsQuery)` (also `GetChildren(User)` / `GetRecursiveChildren()`) — **not virtual**, and on a `Folder`/`BoxSet` instance not attached to a live Emby host it always returns an empty array regardless of conceptual contents. Not testable directly; `CollectionSyncService` routes membership reads through an injectable delegate instead (see `docs/PLAN.md` #6). |
| `MediaBrowser.Controller.Library.DeleteOptions` | ✅ | parameterless ctor; `DeleteFileLocation` (bool), `DeleteFromExternalProvider` (bool), `CollectionFolders` (`BaseItem[]`) — used with `ILibraryManager.DeleteItem(BaseItem, DeleteOptions)` to remove an empty collection |
| `MediaBrowser.Model.Logging.ILogger` | ✅ | `Info/Warn/Debug/Error(string message, object[] paramList)` (i.e. `params object[]`, string-format style), `ErrorException(string, Exception, object[])`, `FatalException(...)`. **Gotcha:** also declares `ReadOnlyMemory<char>`-based overloads, which pulls in a transitive need for the `System.Memory` NuGet package even if you only call the `string`-based overloads — a netstandard2.0 project referencing this interface won't compile without it (see `Emby.AutoCollectionsNG.csproj`). |
| `MediaBrowser.Controller.Dto.DtoOptions` | ✅ | has a parameterless ctor plus `DtoOptions(bool allFields)` and `DtoOptions(ItemFields[] fields)` — `new DtoOptions(false)` is a valid minimal-fields instantiation |
| `MediaBrowser.Controller.Entities.Movies.Movie` | ✅ | extends `Video`; implements `ISupportsBoxSetGrouping` (movie-specific auto-grouping — **not** required for manual `AddToCollection`) |
| `MediaBrowser.Controller.Entities.TV.Episode`, `MediaBrowser.Controller.Entities.Video` | ✅ | both extend `BaseItem`/`Video`; **no type restriction visible on `AddToCollection`'s signature itself** — it takes plain `long[]` IDs regardless of item type |

**Decision (#3):** primary sync target is `ICollectionManager`/`BoxSet`.
Reflection shows no compile-time restriction on which item types can be
added to a collection, and `BoxSet` is architecturally just a `Folder`. That
is reasonably strong static evidence the API itself doesn't block
recordings — but it does **not** confirm runtime/UI behavior. **❓ Genuinely
open, needs a live server:** whether the Emby web UI renders a BoxSet
containing `Video`/`Episode`-typed recordings the same way it renders a
movie collection, and whether Live TV/DVR recordings specifically behave
differently from a plain resolved `Video`. Full writeup and the fallback
plan (`IPlaylistManager`) are in [`docs/api-notes.md`](api-notes.md).

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
| `MediaBrowser.Model.Tasks.TaskTriggerInfo` | ✅ | class, parameterless ctor; properties: `string Type { get; set; }`, `long? TimeOfDayTicks { get; set; }`, `long? IntervalTicks { get; set; }`, `SystemEvent? SystemEvent { get; set; }`, `DayOfWeek? DayOfWeek { get; set; }`, `long? MaxRuntimeTicks { get; set; }`. Static string constants for `Type`: `TriggerDaily` = `"DailyTrigger"`, `TriggerWeekly` = `"WeeklyTrigger"`, `TriggerInterval` = `"IntervalTrigger"`, `TriggerSystemEvent` = `"SystemEventTrigger"`, `TriggerStartup` = `"StartupTrigger"`. A daily-at-a-specific-time trigger is `new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerDaily, TimeOfDayTicks = TimeSpan.FromHours(4).Ticks }` (ticks since midnight). Used by `AutoCollectionsSyncTask.GetDefaultTriggers()` (#7). |
| `MediaBrowser.Model.Tasks.TaskOptions` | ✅ | parameterless ctor + `TaskOptions(TaskOptions cloneFrom)`; `long? MaxRuntimeTicks { get; set; }`, `bool HasManualInteraction { get; set; }`, `LogSeverity LogLevel { get; }` (read-only) |
| `MediaBrowser.Model.Tasks.TaskCompletionStatus` | ✅ | enum: `Completed`, `Failed`, `Cancelled`, `Aborted` |

Source: reflection against `mediabrowser.server.core` 4.9.1.90 (`MediaBrowser.Model.dll`, loaded from
`~/.nuget/packages/mediabrowser.common/4.9.1.90/lib/netstandard2.0/MediaBrowser.Model.dll` via a
throwaway reflection probe, this session, for issue #7).

## Configuration UI

| Element | Status | Signature |
|---|---|---|
| `MediaBrowser.Model.Plugins.IHasWebPages` | ✅ | `IEnumerable<PluginPageInfo> GetPages()` — implement on `Plugin` to register pages |
| `MediaBrowser.Model.Plugins.PluginPageInfo` | ✅ | class; `Name` (string), `DisplayName` (string), `EmbeddedResourcePath` (string — matches an `<EmbeddedResource>` in the csproj), `EnableInMainMenu`/`EnableInUserMenu` (bool), `MenuSection`/`FeatureId`/`MenuIcon` (string), `IsMainConfigPage` (bool) |
| `MediaBrowser.Controller.Plugins.IPluginConfigurationPage` | ✅ | alternate/older interface: `Stream GetHtmlStream()`, `Name` (string), `ConfigurationPageType`, `Plugin` (`IPlugin`) |

**Decision (#9):** `IHasWebPages` + an embedded-resource HTML/JS page is the
confirmed, real mechanism — not a "declarative UI auto-generated from
attributes" (that was an unverified assumption from documentation
skimming, not something reflection found any evidence of; `PluginPageInfo`
just points at a static HTML resource, so all UI markup/JS is authored by
hand, same as any other embedded-HTML Emby plugin page). The
`Emby.Web.GenericEdit.dll` assembly (shipped in the `mediabrowser.common`
package) does exist and hints at some kind of generic property-editor
support, but no interface found via reflection so far wires it into
`IHasWebPages`/`PluginPageInfo` automatically — treat any generic-editor
approach as unconfirmed unless someone finds and documents the actual
connecting API. The hand-authored HTML/JS page against `IHasWebPages` is
the path with real, confirmed signatures, so that's what #9 is built on.

**Still open, needs a live server:** whether the embedded page actually
renders/loads correctly end-to-end in the Emby dashboard (embedded resource
naming/casing conventions, JS `ApiClient` availability in the page context)
can't be confirmed via reflection or compilation — flag this the same way
as the #3 spike if #9 can't be tested against a live server either.

Source: reflection against `mediabrowser.server.core`/`mediabrowser.common`
4.9.1.90 (this session, for issue #9); dev.emby.media/doc/plugins/ui.

## Logging

| Element | Status | Note |
|---|---|---|
| `ILogger` (Emby SDK) | ✅ | via DI; Info = numbers/summary, Debug = item details |

---

**Maintaining this file:** whoever verifies a ⚠️/❓ row changes the status to
✅ and adds the concrete signature plus source (reflection output, live-server
observation, or a linked doc). Do not add anything here that isn't confirmed.
