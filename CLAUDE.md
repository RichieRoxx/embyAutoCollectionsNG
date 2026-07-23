# CLAUDE.md — Guardrails for Coding Agents

This file is loaded automatically as context. It is **binding** for every
change in this repository. If an instruction here conflicts with an ad-hoc
idea, this file wins — or you ask first.

## What this project is

An Emby **server plugin** (C#, `netstandard2.0`) that automatically maintains
collections based on configurable title rules (regex/contains) — primarily
for TV recordings from an OpenViX/Vu+ receiver. **Local only, no cloud, no
internet metadata.**

The work is broken down into GitHub issues #1–#11 (epic = #1). Details,
architecture, and sequencing are in [`docs/PLAN.md`](docs/PLAN.md).

## Language

- **English only** for all code: identifiers, comments, commit messages, and
  documentation files in this repository (README, CLAUDE.md, `docs/**`).
- German product/domain terms that are part of the actual subject matter
  (e.g. recording titles like "heute-show", "ZDF Magazin Royale", or literal
  example filenames) may of course appear as data/examples — that's not a
  language violation, it's the content being described.
- GitHub issue titles/bodies created for planning purposes may remain as
  originally written, but any new issues or comments you author going forward
  should be in English for consistency.

## The most important rules (never break these without asking first)

1. **NO fantasy APIs.** Only use Emby interfaces/methods whose existence and
   signature are confirmed — see
   [`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md). If an API
   point is unclear, **mark it explicitly as uncertain** (comment
   `// UNCERTAIN: ...` + a note on the issue) instead of inventing something.
   A `// TODO` with an open question beats a plausible-sounding method that
   doesn't exist.
2. **One work package at a time.** Work on exactly the issue that's next in
   line and respect the **dependencies** stated in that issue. Don't pull
   forward anything that builds on a still-open predecessor.
3. **Spike #3 gates the sync engine (#6).** The target mechanism (BoxSet vs.
   playlist vs. tag) is only decided after the spike. Do **not** build #6
   before `docs/emby-api-cheatsheet.md` documents that decision.
4. **Minimal robust first version.** Prefer the smallest solution that
   satisfies the acceptance criteria. No feature creep beyond the issue.

## Technical guardrails

- **Target framework:** `netstandard2.0`. Don't use language/BCL features that
  won't compile there (no `net6+`-only APIs, no forced nullable-context, etc.).
- **Only server dependency:** NuGet `mediabrowser.server.core` (4.8.x). Don't
  pull in further runtime dependencies without justifying it in the issue.
- **No network access at runtime.** No HTTP, no external services, no open
  ports, no TMDb/online metadata. Everything strictly local.
- **Keep the matching logic server-independent.** `TitleNormalizer` and
  `RuleMatcher` (#5) must **not** reference any Emby types beyond the
  project's own rule classes, so they stay unit-testable without a running
  server.
- **Idempotency is mandatory (#6).** Running twice without any data change ⇒
  the second run makes **0 writes**. No state that produces duplicates on
  repetition.
- **Defensive against missing data.** Null/empty titles, missing metadata,
  items that vanish between query and apply: skip + count + log, never abort
  the whole run.
- **Run regex safely.** Always with a `MatchTimeout` (~1s) against
  catastrophic backtracking; catch invalid patterns, mark the rule as broken,
  keep the remaining rules running. Compile/cache regex **once per rule**, not
  per item.
- **No feedback loops.** The sync writes collections; an event listener (#8)
  must not have its own writes trigger another sync (filter BoxSet events,
  debounce).

## Definition of done (per issue)

- All acceptance criteria of the issue are met.
- Code builds without new warnings (`dotnet build`).
- Unit tests pass where the issue calls for them (at least #5); CI is green.
- New/changed public behavior doesn't conflict with the logging requirements
  from #10 (meaningful errors with context).
- Short progress comment/checkbox update on the corresponding issue.

## Git workflow

- Develop on the assigned feature branch. **Do not** push to `main`/default
  directly unless explicitly instructed to.
- Meaningful commit messages (what + why). One commit per logical unit.
- **Do not open a pull request unless explicitly requested.**
- Secrets/local paths/server addresses do not belong in code or commits.

## Commands (tooling reference)

> Will be filled in once the project skeleton (#2) is in place. Expected
> commands:

```bash
dotnet build -c Release       # build the plugin (output: Emby.AutoCollectionsNG.dll)
dotnet test                   # unit tests (matching engine, config round-trip)
```

**Deployment (manual):** copy the built DLL into the Emby server's `plugins`
folder, restart the server. Paths: Windows
`%AppData%\Emby-Server\plugins`, Linux typically `/var/lib/emby/plugins` or
`/config/plugins` (Docker).

## Verified Emby API surface

The concrete, confirmed interfaces/signatures live centrally in
[`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md). **Always look
there first** before calling an Emby API. Update that file whenever you've
verified another API (with source).
