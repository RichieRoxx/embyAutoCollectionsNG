# Emby Auto Collections NG

An Emby **server plugin** that automatically maintains collections based on
configurable title rules (regex or contains) — primarily for **TV recordings**
from an OpenViX/Vu+ receiver. Fully local: no cloud, no online metadata, no
open ports.

> **Status:** In development. Work is broken down into GitHub issues #1–#11;
> currently the foundation (planning & guardrails) is in place. See
> [`docs/PLAN.md`](docs/PLAN.md) for the roadmap.

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

## Features (target)

- Automatically create and maintain collections (create, update, optionally
  delete empty ones).
- Rules with: collection name, match type (`regex`/`contains`), pattern,
  optional case sensitivity, enabled flag, library filter, item type filter.
- Matching against the **Emby item title** (not just the filename), optionally
  also against filename/path as a fallback.
- **Title normalization** for receiver recordings: strip leading date/time,
  strip channel prefix, normalize Unicode/whitespace. Two-stage matching
  (raw title → normalized title).
- **Idempotent** sync: running it repeatedly never creates duplicates.
- Runs **periodically** (scheduled task) **and** on demand (dashboard task or
  UI button), optionally also triggered by library changes.
- Meaningful logging: hits per rule, changes per collection, errors with
  context.

## Architecture (summary)

Three building blocks of the Emby plugin model:

| Building block | Emby mechanism | Purpose |
|---|---|---|
| Server plugin | `BasePlugin<TConfig>` + `BasePluginConfiguration` | base, configuration, admin UI |
| Scheduled task | `IScheduledTask` | periodic sync + manual trigger |
| Event hook (optional) | `IServerEntryPoint` + `ILibraryManager` events | sync after library changes (with debounce) |

**Timer vs. trigger:** hybrid. The scheduled task is the primary, robust
execution mode; an event-based trigger with a debounce window complements it
for faster reaction to new recordings. Rationale and details in
[`docs/PLAN.md`](docs/PLAN.md).

## Configuration model

Rules are maintained as a list in the plugin configuration (Emby persists this
as XML; an editing UI is planned). Example rules:

| Collection | Type | Pattern |
|---|---|---|
| Formel 1 | regex | `(?i)\bformel\s*1\b` |
| heute-show | regex | `(?i)\bheute[- ]show\b` |
| ZDF Magazin Royale | contains | `ZDF Magazin Royale` (caseSensitive: false) |
| ZDF Satire | regex | `(?i)\b(heute[- ]show\|zdf magazin royale\|die anstalt)\b` |
| Motorsport | regex | `(?i)\b(formel\s*[1-3]\|motogp\|dtm)\b` |

## Build

> Will become concrete once the project skeleton (issue #2) is in place.

```bash
dotnet build -c Release     # produces the plugin DLL
dotnet test                 # unit tests (matching engine, config round-trip)
```

- Language/runtime: C#, target `netstandard2.0`
- Server dependency: NuGet `mediabrowser.server.core` (4.8.x)

## Installation

1. Build the plugin or download the release DLL.
2. Copy the DLL into the Emby server's `plugins` folder:
   - Windows: `%AppData%\Emby-Server\plugins`
   - Linux: typically `/var/lib/emby/plugins`
   - Docker: `/config/plugins`
3. Restart the Emby server. The plugin appears in the dashboard under
   *Plugins*.
4. Configure rules and trigger the sync via *Scheduled Tasks* (or the UI
   button).

## Known limitations / open items

- **Collections for recordings:** Emby collections (BoxSets) are primarily
  designed for movies. Whether they cleanly accept recordings (`.ts`) and
  display them correctly in the UI will be verified in a spike (issue #3);
  fallbacks (playlists or tags) are accounted for in the design.
- **Configuration UI:** Whether the declarative Emby plugin UI supports
  editable rule lists and action buttons is still open (issue #9); the
  fallback is a classic embedded HTML page.

The current status of these points is tracked in
[`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md).

## For contributors / coding agents

Binding guardrails (e.g. "no fantasy APIs", idempotency, work package order)
live in [`CLAUDE.md`](CLAUDE.md). The verified Emby API surface is in
[`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md). Roadmap in
[`docs/PLAN.md`](docs/PLAN.md).
