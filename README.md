# Emby Auto Collections NG

An Emby **server plugin** that automatically maintains collections based on
configurable title rules (regex or contains) — primarily for **TV recordings**
from an OpenViX/Vu+ receiver. Fully local: no cloud, no online metadata, no
open ports.

> **Status:** Feature-complete for v0.1.0. All planned work packages
> (GitHub issues #2–#10) are implemented and tested (77 unit tests). See
> [`docs/PLAN.md`](docs/PLAN.md) for the roadmap and
> [`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md) /
> [`docs/api-notes.md`](docs/api-notes.md) for what's confirmed vs. what
> still needs verification against a real Emby server (see
> [Known limitations](#known-limitations--open-items) below).

## Why?

Emby has native collections, but they're built for movie/TMDb collections. For
local recordings, what's needed instead is **dynamic, rule-based** collections
like "Formel 1", "heute-show", or "ZDF Magazin Royale" that populate
automatically from item titles — without manually assigning items.

Example filenames of such recordings:

```
20260705 1742 - ORF 1 HD - Formel 1 Großer Preis von Großbritannien 2026.ts
20260719 2142 - ZDF neo HD - heute-show extra - Das Quiz.ts
20260627 0012 - ZDF HD - ZDF Magazin Royale.ts
```

## Features

- Automatically creates and maintains collections (create, update, optionally
  delete empty ones once nothing matches anymore).
- Rules with: collection name, match type (`regex`/`contains`), pattern, case
  sensitivity, enabled flag, match field (raw title / normalized title /
  filename), a filename fallback, library filter, and item type filter.
- Matches against the **Emby item title**, with an optional fallback to the
  filename/path.
- **Title normalization** for receiver recordings: strips the leading
  date/time prefix and channel name, normalizes Unicode/whitespace — e.g.
  `20260719 2142 - ZDF neo HD - heute-show extra - Das Quiz` becomes
  `heute-show extra - Das Quiz`.
- **Idempotent** sync: running it repeatedly with no changes makes zero
  writes.
- Runs **periodically** (daily scheduled task, default 04:00), **on demand**
  (from the Emby dashboard's Scheduled Tasks, or the plugin's own "Sync Now"
  button), and **on library changes** (debounced, so a burst of new
  recordings collapses into roughly one sync instead of one per item).
- A **configuration UI** in the Emby dashboard: add/edit/remove rules and
  trigger a sync, without hand-editing the config file.
- Structured logging: hits per rule, items added/removed per collection,
  errors with enough context (rule name, item IDs, collection name) to act
  on.

## Architecture (summary)

Three building blocks of the Emby plugin model, all implemented:

| Building block | Emby mechanism | Purpose |
|---|---|---|
| Server plugin | `BasePlugin<PluginConfiguration>` + `IHasWebPages` | base, configuration, admin UI |
| Scheduled task | `AutoCollectionsSyncTask : IScheduledTask` | periodic sync + manual trigger, daily at 04:00 by default |
| Event hook | `LibraryChangeListener : IServerEntryPoint` | debounced sync after library changes (default: 5-minute quiet window) |

The scheduled task is the primary, robust execution mode; the event-based
trigger complements it for faster reaction to new recordings, and both
ultimately call the same `CollectionSyncService`. Rationale and details in
[`docs/PLAN.md`](docs/PLAN.md).

**Sync target:** Emby collections (`BoxSet`s) via `ICollectionManager`. See
[Known limitations](#known-limitations--open-items) for what's confirmed vs.
still open about how recordings render inside a BoxSet.

## Configuration model

Rules are a list in the plugin configuration (`PluginConfiguration.Rules`),
edited via the dashboard UI or the underlying XML config file. Global options:
`DeleteEmptyCollections` (default `false`), `TriggerOnLibraryChanges` (default
`true`), `DebounceMinutes` (default `5`).

Example rules:

| Collection | Type | Pattern |
|---|---|---|
| Formel 1 | regex | `(?i)\bformel\s*1\b` |
| heute-show | regex | `(?i)\bheute[- ]show\b` |
| ZDF Magazin Royale | contains | `ZDF Magazin Royale` (caseSensitive: false) |
| ZDF Satire | regex | `(?i)\b(heute[- ]show\|zdf magazin royale\|die anstalt)\b` |
| Motorsport | regex | `(?i)\b(formel\s*[1-3]\|motogp\|dtm)\b` |

See [`docs/example-config.md`](docs/example-config.md) for more detail on
these rules, including a sample serialized XML fragment.

## Configuration UI

The plugin registers a config page in the Emby dashboard (Plugins → Auto
Collections NG): a table of rules (add/remove rows, edit every field inline),
a "delete empty collections" checkbox, a **Save** button, and a **Sync Now**
button that triggers an immediate sync independent of the daily schedule.

## Example sync run

A log excerpt from a scheduled/manual run (`AutoCollectionsSyncTask`,
`ILogger` output at Info/Debug level):

```
Auto Collections NG: starting scheduled/manual sync.
Auto Collections NG: created collection 'Formel 1' with 3 item(s).
Auto Collections NG: updated collection 'heute-show': +2 / -1.
Auto Collections NG: sync finished. Items scanned=842, skipped (no title)=1, collections created=1, updated=1, deleted=0, errors=0.
Auto Collections NG: rule 'Formel 1' matched 3 item(s).
Auto Collections NG: rule 'heute-show' matched 5 item(s).
Auto Collections NG: rule 'ZDF Magazin Royale' matched 0 item(s).
```

## Build

```bash
dotnet build -c Release     # produces src/Emby.AutoCollectionsNG/bin/Release/netstandard2.0/Emby.AutoCollectionsNG.dll
dotnet test                 # 77 unit tests: matching engine, config round-trip, sync engine, scheduled task, event trigger, config UI
```

- Language/runtime: C#, target `netstandard2.0`
- Dependencies: NuGet `MediaBrowser.Server.Core` (4.9.1.90) and `System.Memory`
  (required transitively by Emby's `ILogger` interface — see
  [`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md))
- Test-only dependencies (not shipped in the plugin): `xunit`, `Moq`

## Installation

1. Download a release archive from the [Releases page](../../releases), or
   build it yourself:
   ```bash
   dotnet publish src/Emby.AutoCollectionsNG/Emby.AutoCollectionsNG.csproj -c Release -o publish
   ```
   `dotnet publish` (not `build`) is needed so the small compatibility DLLs
   the plugin depends on are collected alongside it — a plain `dotnet build`
   output folder omits them.
2. Copy `Emby.AutoCollectionsNG.dll` **and** these compatibility shim DLLs
   from the publish output into the Emby server's `plugins` folder — but
   **not** the `MediaBrowser.*.dll`/`Emby.*.dll` files also present in that
   folder, since those are provided by the Emby host itself:
   - `System.Memory.dll`, `System.Buffers.dll`, `System.Numerics.Vectors.dll`,
     `System.Runtime.CompilerServices.Unsafe.dll`
   - (a released zip from the Releases page already contains exactly this
     filtered set — see the release workflow's packaging step)
   - Windows: `%AppData%\Emby-Server\plugins`
   - Linux: typically `/var/lib/emby/plugins`
   - Docker: `/config/plugins`
3. Restart the Emby server. The plugin appears in the dashboard under
   *Plugins*, with its own configuration page.
4. Configure rules in the plugin's config page (or edit the XML config
   directly) and either wait for the daily scheduled sync, click **Sync Now**,
   or let a new/changed recording trigger a debounced sync automatically.

**Uncertain, flagged honestly:** whether the Emby host already has a
compatible `System.Memory` (and friends) loaded for its own use — in which
case shipping our own copies alongside is harmless — or whether the plugin
genuinely needs its own copies to load at all, wasn't verified against a real
server. Shipping them is the safe default either way.

## Known limitations / open items

This project's guardrails (see [`CLAUDE.md`](CLAUDE.md)) require flagging
what's genuinely unverified rather than presenting assumptions as confirmed.
Everything below was built against the real, reflection-verified Emby SDK
API surface (see [`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md))
and is exercised by unit tests, but **no live Emby server was available**
during development to confirm live/runtime behavior:

- **Collections for recordings:** static API evidence (via .NET reflection)
  shows `ICollectionManager` places no restriction on item types, and `BoxSet`
  is architecturally just a `Folder` — but whether the Emby web UI actually
  renders a BoxSet of `Video`/`Episode`-typed recordings the way it renders a
  movie collection is unconfirmed. Full writeup and a documented fallback
  (`IPlaylistManager`) in [`docs/api-notes.md`](docs/api-notes.md).
- **Configuration UI runtime behavior:** the config page compiles, is
  correctly embedded and retrievable from the assembly (verified by test),
  and is written defensively — but whether the dashboard's `ApiClient`
  JavaScript global exists with exactly the methods the page calls
  (`getPluginConfiguration`, `updatePluginConfiguration`, `getScheduledTasks`,
  `startScheduledTask`), and whether plugin-config JSON uses PascalCase or
  camelCase property names, is unconfirmed. The page reads defensively for
  either casing; see the comment block at the top of
  `src/Emby.AutoCollectionsNG/Configuration/configPage.html`.
- **`LibraryFilter` matching:** implemented via a best-effort walk to an
  item's topmost `BaseItem.Parent` ancestor and comparing its name, since
  there's no simpler confirmed API for "which library is this item in"
  without an extra resolution call. Documented in
  `CollectionSyncService.GetTopAncestorName` and `docs/PLAN.md`.
- **Large-library performance:** validated with a 12,000-item **in-memory**
  simulation (~59ms, correct results, no accidental O(n²) behavior) — this is
  a regression guard on the plugin's own algorithm, not a substitute for
  testing against a real Emby database under real I/O load. See
  [`docs/performance-notes.md`](docs/performance-notes.md).

If you run this against a real Emby server, please report back on the above —
particularly whether BoxSets of recordings display correctly and whether the
config page loads/saves as expected — so these can be resolved with real
evidence instead of the best-available static analysis they're built on today.

## For contributors / coding agents

Binding guardrails (e.g. "no fantasy APIs", idempotency, work package order)
live in [`CLAUDE.md`](CLAUDE.md). The verified Emby API surface is in
[`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md). Roadmap in
[`docs/PLAN.md`](docs/PLAN.md).
