# Plan: Regex-based Auto Collections for Emby Recordings

This document summarizes the architecture analysis and the work plan.
The work packages are tracked as GitHub issues; the epic
[#1](https://github.com/RichieRoxx/embyAutoCollectionsNG/issues/1) is the
overview.

## Architecture

The plugin consists of three building blocks:

| Building block | Emby mechanism | Purpose |
|---|---|---|
| Server plugin | `BasePlugin<TConfig>` + `BasePluginConfiguration` | base, configuration, admin UI |
| Scheduled task | `IScheduledTask` | periodic sync + manual trigger via dashboard |
| Event hook (optional) | `IServerEntryPoint` + `ILibraryManager` events | sync after library changes, with debounce |

**Timer vs. trigger:** hybrid approach. The scheduled task is the primary,
robust execution mode (MVP); an event-based trigger with a debounce window is
added on top (issue #8). A purely event-based approach would be fragile on its
own: mass events during a library scan, possibly incomplete metadata on
`ItemAdded`, and rule changes that don't trigger any event at all.

## Verified APIs

- NuGet `mediabrowser.server.core` (4.8.x), target `netstandard2.0`
- Querying items: `ILibraryManager.GetItemList(InternalItemsQuery)`
- Collections: `ICollectionManager` (`MediaBrowser.Controller.Collections`)
  with `CreateCollection` / `AddToCollection` / `RemoveFromCollection`
- Tasks: `IScheduledTask`, manually via dashboard or `ITaskManager`
- Deployment: plugin DLL into the server's `plugins` folder, restart

## Open uncertainties (spike, issue #3)

1. Whether Emby collections (BoxSets) accept recordings/`.ts` videos and
   display them correctly in the UI — collections are primarily designed for
   movies. **Resolved as far as possible without a live server:** static API
   evidence (reflection) shows no restriction, so `BoxSet` is the primary
   target; live UI rendering is still unverified. See
   [`docs/api-notes.md`](api-notes.md). Fallback: playlists
   (`IPlaylistManager`).
2. Whether the declarative plugin UI (auto-generated from the config model)
   supports list editing and action buttons — fallback: classic embedded HTML
   configuration page. Still open, addressed in #9.

## Work packages (order = processing sequence)

| # | Issue | Type | Depends on |
|---|---|---|---|
| 1 | #2 Project skeleton, build & deployment | setup | — |
| 2 | #3 Spike: verify collections for recordings | research | #2 |
| 3 | #4 Configuration model with rule list | feature | #2 |
| 4 | #5 Matching engine + normalization + tests | feature | #4 |
| 5 | #6 Sync engine (idempotent reconciliation) | feature | #3, #4, #5 |
| 6 | #7 Scheduled task + manual trigger | feature | #6 |
| 7 | #8 Event trigger with debounce | enhancement | #7 |
| 8 | #9 Configuration UI + sync button | ui | #4, #7 |
| 9 | #10 Logging, error handling, robustness | quality | #6, #7 |
| 10 | #11 Packaging, docs & release | docs | all |

**MVP = issues #2–#7** (rules via config, periodic + manual sync).

## Hardening / robustness verification (issue #10)

Issue #10 is a verification pass over the sync engine (#6) and scheduled task
(#7), which were already built with idempotency, defensive error handling,
and safe regex execution as hard requirements from the start (see
`CLAUDE.md`). #10 added targeted tests proving those guarantees hold
(invalid-regex isolation regardless of rule order, null-`Path` items with
`AlsoMatchFileName`, one collection's failure not blocking others in the same
run, cancellation leaving no partial collection writes) plus a large-in-memory
(12k item) regression guard against accidental quadratic behavior. It also
improved a few error messages to include the affected item IDs. See
[`docs/performance-notes.md`](performance-notes.md) for the honest scope of
the large-library simulation (what it validates vs. what still needs a live
server).

## References

- https://dev.emby.media/doc/plugins/index.html
- https://dev.emby.media/doc/plugins/dev/index.html
- https://dev.emby.media/doc/plugins/ui/index.html
- https://github.com/MediaBrowser/Emby/wiki/How-to-build-a-Server-Plugin
