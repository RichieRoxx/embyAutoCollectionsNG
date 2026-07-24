# Spike notes: collections for recordings (issue #3)

## Question

Can `ICollectionManager` (BoxSets) host TV recordings (`.ts` files resolved
as `Movie`/`Video`/`Episode` items, depending on library content type), and
does the Emby web UI display such a collection the same way it displays a
movie collection? If not, what's the practical fallback?

## What was verified, and how

No live Emby server was available in the environment this spike was
performed in — there was no way to install/run the actual Emby Server
binary, point it at a library, and observe its dashboard. Rather than
guessing or fabricating a "spike result" (which the project's guardrails
explicitly forbid — see `CLAUDE.md`), the verification that *was* possible
was done via .NET reflection against the real SDK assemblies restored from
NuGet (`mediabrowser.server.core` 4.9.1.90). That is ground truth for the
compiled API surface; it says nothing about server runtime behavior or web
UI rendering. Both halves of the picture are recorded below, clearly
separated.

### Confirmed via reflection (static/compile-time evidence)

- `ICollectionManager.AddToCollection(long collectionId, long[] itemIds)`
  takes plain item IDs. There is **no item-type parameter or generic
  constraint** anywhere in the signature — the interface itself does not
  distinguish a `Movie` ID from a `Video` or `Episode` ID.
- `BoxSet` (the class behind a "collection") extends `Folder` — the same
  base class that a regular media folder, a season, or a playlist extends.
  It is architecturally a container of `BaseItem`s, not a movie-specific
  type.
- `Movie` implements `ISupportsBoxSetGrouping`, an interface `Video`,
  `Episode`, and other item types do **not** implement. Reflection shows
  this interface has no members visible beyond a marker — it appears to
  relate to Emby's automatic franchise-grouping behavior (grouping movie
  sequels via provider IDs), which is a *different, automatic* feature from
  the manual `AddToCollection` call this plugin uses. There is nothing in
  the `ICollectionManager` interface itself that gates `AddToCollection` on
  this marker interface.
- Full signatures are in [`docs/emby-api-cheatsheet.md`](emby-api-cheatsheet.md).

**Conclusion from static evidence alone:** nothing in the compiled API
prevents adding recording items (however they're typed) to a `BoxSet`.

### NOT verified — genuinely open, needs a live server

- Whether the Emby **web UI** renders a BoxSet containing non-`Movie` items
  (e.g. `Video`/`Episode`-typed recordings) with the same collection view,
  poster layout, and navigation as a movie collection, or whether it
  degrades/hides such items.
- Whether Live TV/DVR recordings specifically (vs. a `.ts` file manually
  placed in a "Movies" or "Home Videos" library and resolved as a plain
  `Video`) are indexed and exposed differently by Emby's library scanner —
  this affects which `IncludeItemTypes` values actually show up for a given
  user's library setup, which in turn affects the sync engine's item query
  and the `ItemTypeFilter` values documented in `docs/example-config.md`.
- Any undocumented runtime validation inside the server's `ICollectionManager`
  implementation (not visible in the public interface) that might reject or
  silently ignore certain item types.

## Decision

**Primary target: `ICollectionManager` / `BoxSet`.** The static evidence is
strong enough to build on, and it's the only option that satisfies the
original requirement of "real Emby collections" (visible as a Collection in
the library, not a Playlist, not a smart-filter workaround). The sync engine
(#6) is built against this.

**Fallback, if live testing later shows recordings don't render well as a
BoxSet:** `IPlaylistManager` (`Playlist`, also a `Folder` subtype,
confirmed to exist with `CreatePlaylist`/`AddToPlaylist`/`RemoveFromPlaylist`
— see cheat sheet). The sync engine's design should keep the "diff the
desired item-ID set against the current item-ID set and apply add/remove"
logic decoupled from *which* container type it writes to, specifically so
this fallback is a small change rather than a rewrite, if needed.

## Action needed from a human with a real Emby server

Before or shortly after #6 ships, please verify on an actual Emby instance
with a few sample recordings:

1. Create a rule that matches a recording, let the sync run, and confirm the
   resulting BoxSet shows up and displays sensibly in the web UI.
2. Note which `IncludeItemTypes` value(s) your recordings actually get
   assigned (check via the API or a debug log) so `docs/example-config.md`'s
   `ItemTypeFilter` guidance can be corrected if needed.

If either check fails, switch the sync engine's target to
`IPlaylistManager` per the fallback above and update this file.
