# Planung: Regex-basierte Auto-Collections für Emby-Recordings

Dieses Dokument fasst die Architektur-Analyse und den Arbeitsplan zusammen.
Die Arbeitspakete sind als GitHub-Issues angelegt; das Epic [#1](https://github.com/RichieRoxx/embyAutoCollectionsNG/issues/1)
ist die Übersicht.

## Architektur

Das Plugin besteht aus drei Bausteinen:

| Baustein | Emby-Mechanismus | Zweck |
|---|---|---|
| Server-Plugin | `BasePlugin<TConfig>` + `BasePluginConfiguration` | Basis, Konfiguration, Admin-UI |
| Scheduled Task | `IScheduledTask` | periodischer Sync + manueller Trigger übers Dashboard |
| Event-Hook (optional) | `IServerEntryPoint` + `ILibraryManager`-Events | Sync nach Library-Änderungen, mit Debounce |

**Timer vs. Trigger:** Hybrid-Ansatz. Der Scheduled Task ist die primäre, robuste
Ausführungsform (MVP); ein eventbasierter Trigger mit Debounce-Fenster kommt als
Ergänzung dazu (Issue #8). Ein rein eventbasierter Ansatz ist als alleinige Lösung
fragil: Massenevents beim Library-Scan, evtl. unfertige Metadaten bei `ItemAdded`,
und Regeländerungen lösen keine Events aus.

## Verifizierte APIs

- NuGet `mediabrowser.server.core` (4.8.x), Target `netstandard2.0`
- Items abfragen: `ILibraryManager.GetItemList(InternalItemsQuery)`
- Collections: `ICollectionManager` (`MediaBrowser.Controller.Collections`)
  mit `CreateCollection` / `AddToCollection` / `RemoveFromCollection`
- Tasks: `IScheduledTask`, manuell via Dashboard oder `ITaskManager`
- Deployment: Plugin-DLL in den `plugins`-Ordner des Servers, Neustart

## Offene Unsicherheiten (Spike, Issue #3)

1. Ob Emby-Collections (BoxSets) Recordings/`.ts`-Videos aufnehmen und in der UI
   korrekt anzeigen — Collections sind primär für Movies gedacht.
   Fallback: Playlists (`IPlaylistManager`) oder Tag-basierte Filter.
2. Ob das deklarative Plugin-UI (autogeneriert aus dem Config-Model) Listen-Editing
   und Action-Buttons unterstützt — Fallback: klassische eingebettete HTML-Konfigseite.

## Arbeitspakete (Reihenfolge = Abarbeitung)

| # | Issue | Typ | Abhängig von |
|---|---|---|---|
| 1 | #2 Projektgerüst, Build & Deployment | setup | — |
| 2 | #3 Spike: Collections für Recordings verifizieren | research | #2 |
| 3 | #4 Konfigurationsmodell mit Regel-Liste | feature | #2 |
| 4 | #5 Matching-Engine + Normalisierung + Tests | feature | #4 |
| 5 | #6 Sync-Engine (idempotenter Abgleich) | feature | #3, #4, #5 |
| 6 | #7 Scheduled Task + manueller Trigger | feature | #6 |
| 7 | #8 Event-Trigger mit Debounce | enhancement | #7 |
| 8 | #9 Konfigurations-UI + Sync-Button | ui | #4, #7 |
| 9 | #10 Logging, Fehlerbehandlung, Robustheit | quality | #6, #7 |
| 10 | #11 Packaging, Doku & Release | docs | alle |

**MVP = Issues #2–#7** (Regeln per Config, periodischer + manueller Sync).

## Referenzen

- https://dev.emby.media/doc/plugins/index.html
- https://dev.emby.media/doc/plugins/dev/index.html
- https://dev.emby.media/doc/plugins/ui/index.html
- https://github.com/MediaBrowser/Emby/wiki/How-to-build-a-Server-Plugin
