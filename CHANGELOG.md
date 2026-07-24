# Changelog

All notable changes to this project are documented here.
This project follows [Semantic Versioning](https://semver.org/).

## [0.1.0] — 2026-07-24

First release. An Emby server plugin that maintains collections from
configurable title rules (regex/contains), aimed at TV recordings from an
OpenViX/Vu+ receiver. Fully local — no network access at runtime.

Verified end-to-end against a live **Emby 4.9.5** server on Synology DSM:
collections are created, populated and rendered in the Emby UI, the dashboard
configuration page loads and saves, and a repeated sync makes zero writes.

### Added

- Rule-based collection sync: collection name, match type (`regex`/`contains`),
  pattern, case sensitivity, enabled flag, match field (raw title / normalized
  title / filename), filename fallback, library filter and item type filter.
- Title normalization for receiver recordings — strips the leading date/time
  prefix and channel name and normalizes Unicode/whitespace, so
  `20260719 2142 - ZDF neo HD - heute-show extra - Das Quiz` matches as
  `heute-show extra - Das Quiz`.
- Idempotent reconciliation: a run with no data or config change makes zero
  writes.
- Three sync triggers: a daily scheduled task (default 04:00), on-demand from
  the dashboard or the plugin's own **Sync Now** button, and debounced library
  change events.
- Configuration page in the Emby dashboard for editing rules and triggering a
  sync.
- Structured logging: per-rule hit counts, per-collection add/remove counts and
  errors carrying rule name, collection name and item IDs.
- Safe regex handling: per-rule compilation with a match timeout; a broken
  pattern is reported and skipped without aborting the run.

### Notes on Emby behaviour

Several host behaviours are silent failure modes and are documented in
[`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md):

- Queries must request all fields (`DtoOptions(true)`) or returned items carry
  no identity at all (`InternalId == 0`), while names and types still look
  correct.
- `CreateCollection` reports failure by returning `null` — never by throwing,
  and without any server-side log entry. It also requires a non-empty item
  list.
- Live TV guide entries (`LiveTvProgram`) are rejected as collection members
  without any error, so they are excluded from matching. Write rules against
  recording titles rather than EPG titles.
- Collection membership is read via `InternalItemsQuery.CollectionIds`;
  `Folder.GetChildren` and `ParentIds` both return nothing for a real BoxSet.

[0.1.0]: https://github.com/RichieRoxx/embyAutoCollectionsNG/releases/tag/v0.1.0
