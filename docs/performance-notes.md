# Performance notes (issue #10)

This is a short, honest note on what the "large-library" test in
`CollectionSyncServiceTests.LargeLibrarySimulation_TenThousandItems_CompletesCorrectlyAndWithinABoundedTime`
does and does not tell us.

## What the test does

- Builds **12,000** in-memory `Video` items with a realistic mix of titles
  (roughly 1/7 match a "Formel 1" rule, roughly 1/11 of the remainder match a
  "heute-show" rule, the rest match a third catch-all `Contains` rule) so all
  three configured rules do real work across the whole set.
- Feeds them through a mocked `ILibraryManager.GetItemList` that **actually
  paginates**: it slices the in-memory array by the query's `StartIndex`/
  `Limit` instead of returning everything on the first call, so
  `CollectionSyncService`'s paging loop (page size 500, see
  `CollectionSyncService.PageSize`) is exercised the same way it would be
  against a real, correctly-implemented `ILibraryManager`.
- Runs a full `CollectionSyncService.SyncAsync` against 3 rules and asserts:
  - all 12,000 items are scanned (`SyncResult.ItemsScanned`),
  - rule hit counts exactly match the expected split,
  - all three collections are created,
  - zero errors,
  - wall-clock time stays under a generous 10-second bound.

## Observed numbers (this session, dev container)

| Metric | Value |
|---|---|
| Items | 12,000 |
| Rules | 3 (2 regex, 1 contains) |
| Pages fetched | 24 (page size 500) |
| Wall-clock time | ~59 ms |
| Assertion bound | < 10 s (intentionally generous — a regression guard, not a benchmark) |

The 10-second bound is deliberately loose. Its purpose is to catch an
accidental O(n²) (or worse) regression — e.g. re-scanning already-processed
items per rule, or re-querying the full item list once per collection — not
to pin down a precise performance number that would make the test flaky on
slower CI hardware.

## What this validates

- **No accidental quadratic behavior** in the per-item rule-matching loop or
  in the collection-reconciliation step, at a scale an order of magnitude
  above a typical single-user DVR library.
- **Bounded memory via paging**: the service never asks for or holds more
  than one page (500 items) of raw library items in memory at a time during
  the scan phase (see `CollectionSyncService.PageSize` and the `while (true)`
  paging loop in `SyncAsync`). Only the much smaller derived data — matched
  item ID sets per rule/collection — is retained across the whole run.

## What this does NOT validate

This is an **in-memory algorithmic simulation**, not a real Emby server under
real I/O load. It does **not** tell us anything about:

- Real database/query latency of `ILibraryManager.GetItemList` against an
  actual Emby SQLite/library database with 10k+ items.
- Real `ICollectionManager.CreateCollection`/`AddToCollection`/
  `RemoveFromCollection` latency, disk I/O, or Emby-server-side locking under
  concurrent library activity.
- Actual process memory footprint under the real host (the mock's "pages"
  are cheap in-memory array slices, not real `BaseItem` hydration cost from
  the metadata provider pipeline).
- UI responsiveness or scheduled-task interaction with other Emby background
  tasks running at the same time.

In short: this test is a regression guard on the plugin's own algorithm
(pagination + per-item matching + reconciliation), not a substitute for
testing against a live Emby server with a real large library — that
verification remains open per the same caveat already noted for issues #3
and #9 in `docs/emby-api-cheatsheet.md`.
