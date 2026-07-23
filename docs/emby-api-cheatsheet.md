# Emby API Cheat Sheet (verified surface)

Purpose: **prevent fantasy APIs.** Only interfaces/methods with confirmed
existence are listed here. Signatures may differ slightly depending on the
`mediabrowser.server.core` version — verify against the actually referenced
NuGet version when implementing, and correct this file if it deviates.

Status legend: ✅ confirmed · ⚠️ plausible, but needs verification against the SDK · ❓ open (spike)

## Plugin basics

| Element | Status | Note |
|---|---|---|
| `BasePlugin<TConfig>` | ✅ | plugin base class; ctor `(IApplicationPaths, IXmlSerializer)`; override `Id` GUID |
| `BasePluginConfiguration` | ✅ | configuration base class; persisted as XML in the `config` folder |
| `IServerEntryPoint` | ✅ | entry point for initialization/event hooks; `Run()` + `Dispose()` |
| Dependency injection | ✅ | inject services via constructor (e.g. `ILibraryManager`, `ILogger`) |

Sources: dev.emby.media/doc/plugins/dev, MediaBrowser/Emby wiki "How to build
a Server Plugin".

## Querying items

| Element | Status | Note |
|---|---|---|
| `ILibraryManager` | ✅ | central access to the library |
| `ILibraryManager.GetItemList(InternalItemsQuery)` | ✅ | returns items; filtering via the query |
| `InternalItemsQuery` | ✅ | filter object; includes `IncludeItemTypes`, `Limit`, `StartIndex`, `Recursive`, `DtoOptions` |
| Paging via `Limit`/`StartIndex` | ⚠️ | query in chunks for large libraries; verify property names against the SDK |

`InternalItemsQuery` fields vary slightly by version — verify in the object
browser / SDK before use. Source: `Emby.Server.Implementations/Library/LibraryManager.cs`.

## Collections

| Element | Status | Note |
|---|---|---|
| `ICollectionManager` (`MediaBrowser.Controller.Collections`) | ✅ | manager for BoxSets/collections |
| `CreateCollection(...)` | ✅ | creates a collection; verify exact options/parameter shape against the SDK |
| `AddToCollection(collectionId, itemIds)` | ✅ | adds items; verify exact types (Guid vs. string/long) against the SDK |
| `RemoveFromCollection(collectionId, itemIds)` | ✅ | removes items |
| Events `ItemsAddedToCollection` / `ItemsRemovedFromCollection` / `CollectionCreated` | ⚠️ | exist on the collection manager; relevant for avoiding sync loops |
| Querying BoxSets | ⚠️ | via `InternalItemsQuery` with `IncludeItemTypes = ["BoxSet"]`; verify children/members against the SDK |

**❓ OPEN SPIKE (#3):** Whether `ICollectionManager` accepts recordings/`.ts`
videos (item type depends on library: `Movie`/`Video`/`Episode`) and whether
the Emby UI displays such a collection correctly is **not confirmed**. Record
the result and final signatures here. Fallbacks: `IPlaylistManager` (playlists)
or a tag-based approach.

Sources: Emby community "Get/Create Collections Plugin Service", Jellyfin
`CollectionManager.cs` (API-related, but **not** identical 1:1 — do not copy
blindly).

## Scheduled tasks

| Element | Status | Note |
|---|---|---|
| `IScheduledTask` | ✅ | `Name`/`Description`/`Category`, `Execute(...)`, `GetDefaultTriggers()` |
| Automatic type discovery | ✅ | task class public in the plugin assembly ⇒ gets discovered |
| `ITaskManager` | ✅ | trigger the task programmatically (for UI button #9 and event trigger #8) |
| Progress/CancellationToken in `Execute` | ⚠️ | verify signature against the SDK (typically `IProgress<double>` + `CancellationToken`) |

Sources: dev.emby.media/reference `ITaskManager`, Emby "Scheduled Tasks".

## Configuration UI

| Approach | Status | Note |
|---|---|---|
| Declarative plugin UI (auto-generated from config model) | ❓ | attributes like `[DisplayName]`, `[Description]`; **unclear** whether it supports editable lists, action buttons, and dynamic dropdowns (#9) |
| Classic embedded HTML configuration page | ⚠️ | fallback: `IHasWebPages`/embedded resource + JS `ApiClient` pattern; verify against example plugins |

Source: dev.emby.media/doc/plugins/ui.

## Logging

| Element | Status | Note |
|---|---|---|
| `ILogger` (Emby SDK) | ✅ | via DI; Info = numbers/summary, Debug = item details |

---

**Maintaining this file:** whoever verifies a ⚠️/❓ row changes the status to
✅ and adds the concrete signature plus source. Do not add anything here that
isn't confirmed.
