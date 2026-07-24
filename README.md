# Emby Auto Collections NG

An Emby **server plugin** that automatically maintains collections based on
configurable title rules (regex or contains) — primarily for **TV recordings**
from an OpenViX/Vu+ receiver. Fully local: no cloud, no online metadata, no
open ports.

> **Status:** v0.1.0 — feature-complete and **verified end-to-end against a
> live Emby 4.9.5 server** (Synology DSM). All planned work packages (GitHub
> issues #2–#10) are implemented and covered by 80 unit tests. Collections are
> created, populated and shown in the Emby UI, the dashboard config page loads
> and saves, and a second sync run makes zero writes. See
> [`docs/PLAN.md`](docs/PLAN.md) for the roadmap and
> [`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md) for the
> verified Emby API surface, including several behaviours that are easy to get
> wrong (see [Known limitations](#known-limitations--open-items)).

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
- **Ignores Live TV guide data.** EPG entries (`LiveTvProgram`) are not library
  content and are silently rejected by Emby as collection members; on a DVR
  setup they also vastly outnumber real recordings. They are excluded from
  matching, along with library-structure folders and existing collections.
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

Real output from the live server this was verified on — first a run that
creates a collection, then an immediate second run showing idempotency:

```
Auto Collections NG: starting scheduled/manual sync.
Auto Collections NG: creating 'heute show' for 8 item(s) of type(s): Episode x8.
Auto Collections NG: using collections folder 'Collections' (internalId 204456).
Auto Collections NG: created collection 'heute show' (internalId 204459) with 8 item(s).
Auto Collections NG: sync finished. Items scanned=3648, skipped (no title)=0, collections created=1, updated=0, deleted=0, errors=0.

Auto Collections NG: starting scheduled/manual sync.
Auto Collections NG: sync finished. Items scanned=3649, skipped (no title)=0, collections created=0, updated=0, deleted=0, errors=0.
```

## Build

```bash
dotnet build -c Release     # produces src/Emby.AutoCollectionsNG/bin/Release/netstandard2.0/Emby.AutoCollectionsNG.dll
dotnet test                 # 80 unit tests: matching engine, config round-trip, sync engine, scheduled task, event trigger, config UI
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
   - Synology (DSM package): `/volume1/@appdata/EmbyServer/plugins` — the files
     must be owned by the `emby` user and readable by it (`chown emby:emby`,
     `chmod 644`), then restart with `synopkg restart EmbyServer`
3. Restart the Emby server. The plugin appears in the dashboard under
   *Plugins*, with its own configuration page.
4. Configure rules in the plugin's config page (or edit the XML config
   directly) and either wait for the daily scheduled sync, click **Sync Now**,
   or let a new/changed recording trigger a debounced sync automatically.

Shipping the `System.Memory` shim DLLs alongside the plugin was confirmed to
work on a live server. Whether the host would also have loaded compatible
copies on its own is still unknown, but shipping them is harmless and is the
safe default either way.

## Known limitations / open items

v0.1.0 was verified end-to-end against a live **Emby 4.9.5** server on Synology
DSM: collections are created, populated and rendered in the Emby UI, the
dashboard config page loads/saves, and repeated runs make zero writes. The
items below are what remains genuinely unverified or deliberately limited.

**Verified and resolved** (previously flagged as unknown):

- Collections of `Episode`-typed DVR recordings do display correctly in the
  Emby UI — the documented `IPlaylistManager` fallback is not needed.
- The dashboard config page loads and saves; `ApiClient.getPluginConfiguration`
  / `updatePluginConfiguration` / `getScheduledTasks` behave as assumed, and
  plugin-config JSON uses PascalCase.
- Shipping the `System.Memory` shim DLLs alongside the plugin works on a real
  server.

**Behaviours worth knowing** (all documented in
[`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md)):

- **Live TV guide entries can't be collection members.** Emby rejects
  `LiveTvProgram` items silently — `CreateCollection` returns `null` if every
  candidate is one, and `AddToCollection` accepts them but never persists.
  They are excluded from matching. Practical consequence: write rules against
  your **recording** titles, not against EPG titles. Recordings from a Vu+
  receiver look like `20260419 2242 - ZDF neo HD - heute-show`, so a rule
  matching `heute ` (with a trailing space) will find guide entries only, while
  `heute-show` finds the recordings.
- **`CreateCollection` needs a non-empty item list** and signals failure by
  returning `null`, never by throwing and without any server-side log entry.

**Still open:**

- **`LibraryFilter` matching** is a best-effort walk to an item's topmost
  `BaseItem.Parent` ancestor, compared by name — there is no simpler confirmed
  API for "which library is this item in". Fine for typical setups; may not
  hold for unusual library topologies. See
  `CollectionSyncService.GetTopAncestorName` and `docs/PLAN.md`.
- **Large-library performance** is validated with a 12,000-item *in-memory*
  simulation (~59 ms, no O(n²) behaviour) plus a real run over 3,648 items.
  Neither is a substitute for a very large library under real I/O load. See
  [`docs/performance-notes.md`](docs/performance-notes.md).
- **Only tested on Emby 4.9.5** (Synology). The plugin compiles against SDK
  4.9.1.90, the newest published package; other 4.9.x builds are expected to
  work but are unverified.

## For contributors / coding agents

Binding guardrails (e.g. "no fantasy APIs", idempotency, work package order)
live in [`CLAUDE.md`](CLAUDE.md). The verified Emby API surface is in
[`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md). Roadmap in
[`docs/PLAN.md`](docs/PLAN.md).
