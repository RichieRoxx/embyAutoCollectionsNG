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
| `InternalItemsQuery.DtoOptions` | ✅ | `MediaBrowser.Controller.Dto.DtoOptions` — **use `new DtoOptions(true)`. With minimal fields the returned items have no identity (`InternalId == 0`, `Id == Guid.Empty`); see the ⚠️ note below.** |
| `ILibraryManager.ItemAdded` / `ItemAdding` / `ItemUpdated` / `ItemRemoved` | ✅ | all four events confirmed, `EventHandler<ItemChangeEventArgs>` |
| `MediaBrowser.Controller.Library.ItemChangeEventArgs` | ✅ | `BaseItem Item`, `BaseItem Parent`, `ItemUpdateType UpdateReason`, `BaseItem[] CollectionFolders` — used by the event trigger (#8) to filter out the plugin's own BoxSet writes (check `Item is BoxSet`) and debounce on real item changes |

### Item identity — important gotcha

`BaseItem` carries **two different ID types**:

| Property | Type | Used by |
|---|---|---|
| `BaseItem.Id` | `Guid` | legacy identifier; `GetItemById(Guid)` |
| `BaseItem.InternalId` | `long` (`Int64`) | **this is what `ICollectionManager`/`IPlaylistManager` item-ID parameters expect** |

`BaseItem.Name` (`string`) and `BaseItem.Path` (`string`) are the two
properties the matching engine reads (raw title and filename/path fallback,
respectively).

### ⚠️ `DtoOptions` decides whether items have an identity at all

**Verified on a live Emby 4.9.5 server (2026-07-24).** `InternalItemsQuery.DtoOptions`
does not merely trim optional metadata — with a minimal-fields request the item
repository does not materialize the identity columns:

| Query | `item.Name` | `item.InternalId` | `item.Id` (Guid) |
|---|---|---|---|
| `DtoOptions(false)` | correct | **`0`** | **`Guid.Empty`** |
| `DtoOptions(true)` | correct | correct (e.g. `204457`) | correct |

This is silent and vicious: names, runtime types and even derived values like
`DisplayPreferencesId` look perfectly healthy, so matching logic appears to work
while every ID is zero. Confirmed via reflection over the live objects, and it is
**not** an assembly-version mismatch — the plugin's `MediaBrowser.Controller`
reference resolves to the host's own 4.9.5.0 assembly (`isBaseItem == True`).

Because `ICollectionManager` addresses everything by `InternalId`, the failure
mode is a sync that *reports success while doing nothing*:

- all matched IDs collapse to the single value `0` inside a `HashSet<long>`, so
  every rule reports exactly `1 item` no matter how many actually matched;
- `CreateCollection` receives `ItemIdList = [0]` and persists **no BoxSet** (it
  does create the `Collections` folder, which makes it look like it worked);
- the existing-collection lookup finds a BoxSet with `InternalId == 0`, so each
  run treats it as new (breaking idempotency) and
  `AddToCollection(0, [0])` throws `ArgumentException: No collection exists with
  the supplied Id`.

So: **always `new DtoOptions(true)` for any query whose results are used by ID.**
If this is ever narrowed for performance, re-verify that `InternalId` is still
populated.

Source: reflection against `mediabrowser.server.core` 4.9.1.90; **live-server
observation on Emby 4.9.5.0, 2026-07-24**.

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
| `MediaBrowser.Model.Logging.ILogger` | ✅ | `Info/Warn/Debug/Error(string message, object[] paramList)` (i.e. `params object[]`, string-format style), `ErrorException(string, Exception, object[])`, `FatalException(...)`. **Gotcha:** also declares `ReadOnlyMemory<char>`-based overloads, which pulls in a transitive need for the `System.Memory` NuGet package even if you only call the `string`-based overloads — a netstandard2.0 project referencing this interface won't compile without it (see `Emby.AutoCollectionsNG.csproj`). **Packaging consequence (issue #11):** `dotnet publish` shows `System.Memory` itself transitively brings in `System.Buffers.dll`, `System.Numerics.Vectors.dll`, and `System.Runtime.CompilerServices.Unsafe.dll` — a plain `dotnet build` output folder does **not** include any of these (class libraries aren't copy-local by default), only `dotnet publish` does. All four must ship alongside `Emby.AutoCollectionsNG.dll` in the Emby `plugins` folder; the `MediaBrowser.*.dll`/`Emby.*.dll` files `publish` also copies must NOT be shipped since the Emby host provides those itself. Whether the host already has a compatible `System.Memory` loaded for its own use (making our copies redundant-but-harmless) vs. genuinely needing ours to load at all was not verified against a real server — see README "Known limitations". |
| `MediaBrowser.Controller.Dto.DtoOptions` | ✅ | has a parameterless ctor plus `DtoOptions(bool allFields)` and `DtoOptions(ItemFields[] fields)`. `new DtoOptions(false)` compiles and runs, but **items then come back without `InternalId`/`Id`** — use `new DtoOptions(true)` whenever results are addressed by ID (see the ⚠️ note under "Querying items") |
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

**VERIFIED end-to-end against a live Emby server (2026-07-23, server
`4.9.x` on Synology; dashboard-ui source + the shipped MBBackup/XmlTV/Fanart
plugin config pages inspected directly).** The dashboard's rendering contract
for an `IHasWebPages` config page is **not** "any HTML gets injected and its
inline `<script>` runs." It is specific, and getting it wrong makes the page
silently fail to open (menu link does nothing / blank view):

- **Root element must match `querySelector('.view, div[data-role="page"]')`.**
  `viewmanager.loadView` → `normalizeNewView` does `div.innerHTML = html` then
  `div.querySelector('.view, div[data-role="page"]')` and uses the result as
  *the* view element. If the page's root has neither `class="view"` nor
  `data-role="page"`, this returns `null` and the very next line
  (`view.getAttribute("data-require")`) throws — the page never renders. Give
  the root `class="view"` (Emby's own pages also add `is="emby-scroller"` +
  scroller classes; a plain `<div class="view">` with a child `.content-primary`
  is enough — the view manager upgrades it to a scroller automatically).
- **Inline `<script>` does NOT execute.** Because the HTML is loaded via
  `innerHTML`, any `<script>` in it is inert (browser spec). There is **no**
  script re-execution anywhere in the dashboard-ui. All shipped Emby plugins
  put their logic in a **separate JS module** referenced from the root element
  via `data-controller="__plugin/<name>"`.
- **`data-controller="__plugin/<name>"` → separate registered JS resource.**
  The view manager strips `__plugin/`, calls
  `pluginManager.getConfigurationResourceUrl(<name>)` →
  `/emby/web/ConfigurationPage?name=<name>`, and `require()`s it as an **AMD
  module** whose return value is a **view-controller constructor**. That route
  only serves resources registered via `IHasWebPages`, so the JS must be a
  **second `PluginPageInfo`** whose `Name` equals `<name>` (lowercase; e.g.
  `autocollectionsngjs`). Serving content type is derived from the resource's
  `.js` extension.
- **Controller module shape** (confirmed from MBBackup's `embybackupjs`):
  ```js
  define(['baseView', /* 'loading', 'emby-input', ... as needed */], function (BaseView) {
      function View(view, params) {           // view = the root .view element
          BaseView.apply(this, arguments);     // BaseView sets this.view / this.params
          // bind listeners via view.querySelector(...) here
      }
      Object.assign(View.prototype, BaseView.prototype);
      View.prototype.onResume = function (options) {   // runs every time the page is shown
          BaseView.prototype.onResume.apply(this, arguments);
          // load config here
      };
      return View;                             // returned ctor is `new`-ed as the controller
  });
  ```
  Instantiated as `new View(viewElement, params)` (see
  `viewmanager.js`; `baseview.js` `BaseView(view, params){ this.view = view; this.params = params; ... }`).
- **`ApiClient` config methods confirmed present and used exactly as assumed:**
  `ApiClient.getPluginConfiguration(pluginId)` and
  `ApiClient.updatePluginConfiguration(pluginId, config)` (both Promise-returning),
  plus `ApiClient.getScheduledTasks()` / `ApiClient.startScheduledTask(id)` /
  `ApiClient.getUrl(...)` / `ApiClient.ajax(...)`. `ApiClient` is a page global.
- **Plugin config is served as JSON with PascalCase keys** (matching the C#
  property names). Enum representation was not pinned down, so the controller
  still reads enums tolerantly (string name or numeric index).
- **Gotcha: `PluginPageInfo.IsMainConfigPage` defaults to `true`.** A
  secondary resource entry (like the controller JS) MUST set
  `IsMainConfigPage = false` (and `EnableInMainMenu = false`) explicitly, or
  the dashboard treats it as an additional main config page / menu entry.
  Confirmed by unit test against the real SDK type (an unset flag came back
  `true`).

This is implemented in `Configuration/configPage.html` (root `class="view"`
+ `data-controller`) and `Configuration/configPage.js` (the AMD controller),
both registered in `Plugin.GetPages()`. The earlier version — a single HTML
resource with a non-`.view` root and an inline `<script>` — is exactly why the
config page would not open, and is the bug fixed here.

Source: reflection against `mediabrowser.server.core`/`mediabrowser.common`
4.9.1.90; **live server dashboard-ui + shipped-plugin inspection, 2026-07-23**;
dev.emby.media/doc/plugins/ui.

## Logging

| Element | Status | Note |
|---|---|---|
| `ILogger` (Emby SDK) | ✅ | via DI; Info = numbers/summary, Debug = item details |

---

**Maintaining this file:** whoever verifies a ⚠️/❓ row changes the status to
✅ and adds the concrete signature plus source (reflection output, live-server
observation, or a linked doc). Do not add anything here that isn't confirmed.
