# Emby Auto Collections NG

Ein Emby-**Server-Plugin**, das Collections automatisch anhand konfigurierbarer
Titel-Regeln (Regex oder Contains) pflegt — primär für **TV-Aufnahmen** vom
OpenViX/Vu+ Receiver. Vollständig lokal: keine Cloud, keine Online-Metadaten,
keine offenen Ports.

> **Status:** In Entwicklung. Die Arbeit ist in GitHub-Issues #1–#11 zerlegt;
> aktuell steht das Fundament (Planung & Guardrails). Siehe
> [`docs/PLAN.md`](docs/PLAN.md) für den Fahrplan.

## Wozu?

Emby hat Collections nativ, aber auf Movie-/TMDb-Collections ausgelegt. Für lokale
Aufnahmen möchte man stattdessen **dynamische, regelbasierte** Collections wie
„Formel 1", „heute-show" oder „ZDF Magazin Royale", die sich automatisch aus dem
Titel der Items befüllen — ohne Items manuell zuzuordnen.

Beispiel-Dateinamen solcher Aufnahmen:

```
20260705 1742 - ORF 1 HD - Formel 1 Großer Preis von Großbritannien 2026.ts
20260719 2142 - ZDF neo HD - heute-show extra - Das Quiz.ts
20260627 0012 - ZDF HD - ZDF Magazin Royale.ts
```

## Funktionen (Zielbild)

- Collections automatisch anlegen und pflegen (erstellen, aktualisieren, optional
  leere löschen).
- Regeln mit: Collection-Name, Match-Typ (`regex`/`contains`), Pattern, optional
  Case-Sensitivity, Enabled-Flag, Library-Filter, Item-Typ-Filter.
- Matching auf den **Emby-Item-Titel** (nicht nur den Dateinamen), optional
  zusätzlich auf Dateiname/Pfad als Fallback.
- **Titel-Normalisierung** für Receiver-Aufnahmen: führendes Datum/Uhrzeit entfernen,
  Senderpräfix entfernen, Unicode/Whitespace normalisieren. Zweistufiges Matching
  (Rohtitel → normalisierter Titel).
- **Idempotenter** Abgleich: mehrfaches Ausführen erzeugt keine Duplikate.
- Ausführung **periodisch** (Scheduled Task) **und manuell** (Dashboard-Task bzw.
  UI-Button), optional zusätzlich ausgelöst durch Library-Änderungen.
- Aussagekräftiges Logging: Treffer pro Regel, Änderungen pro Collection, Fehler
  mit Kontext.

## Architektur (Kurzfassung)

Drei Bausteine des Emby-Plugin-Modells:

| Baustein | Emby-Mechanismus | Zweck |
|---|---|---|
| Server-Plugin | `BasePlugin<TConfig>` + `BasePluginConfiguration` | Basis, Konfiguration, Admin-UI |
| Scheduled Task | `IScheduledTask` | periodischer Sync + manueller Trigger |
| Event-Hook (optional) | `IServerEntryPoint` + `ILibraryManager`-Events | Sync nach Library-Änderungen (mit Debounce) |

**Timer vs. Trigger:** Hybrid. Der Scheduled Task ist die primäre, robuste
Ausführungsform; ein eventbasierter Trigger mit Debounce-Fenster ergänzt ihn für
schnellere Reaktion auf neue Aufnahmen. Begründung und Details in
[`docs/PLAN.md`](docs/PLAN.md).

## Konfigurationsmodell

Regeln werden als Liste in der Plugin-Konfiguration gepflegt (Emby persistiert diese
als XML; eine UI zum Bearbeiten folgt). Beispielregeln:

| Collection | Typ | Pattern |
|---|---|---|
| Formel 1 | regex | `(?i)\bformel\s*1\b` |
| heute-show | regex | `(?i)\bheute[- ]show\b` |
| ZDF Magazin Royale | contains | `ZDF Magazin Royale` (caseSensitive: false) |
| ZDF Satire | regex | `(?i)\b(heute[- ]show\|zdf magazin royale\|die anstalt)\b` |
| Motorsport | regex | `(?i)\b(formel\s*[1-3]\|motogp\|dtm)\b` |

## Build

> Wird konkret, sobald das Projektgerüst (Issue #2) steht.

```bash
dotnet build -c Release     # erzeugt die Plugin-DLL
dotnet test                 # Unit-Tests (Matching-Engine, Config-Roundtrip)
```

- Sprache/Runtime: C#, Target `netstandard2.0`
- Server-Abhängigkeit: NuGet `mediabrowser.server.core` (4.8.x)

## Installation

1. Plugin bauen bzw. die Release-DLL herunterladen.
2. DLL in den `plugins`-Ordner des Emby-Servers kopieren:
   - Windows: `%AppData%\Emby-Server\plugins`
   - Linux: typischerweise `/var/lib/emby/plugins`
   - Docker: `/config/plugins`
3. Emby-Server neu starten. Das Plugin erscheint im Dashboard unter *Plugins*.
4. Regeln konfigurieren und den Sync über die *Geplanten Aufgaben* (bzw. den
   UI-Button) auslösen.

## Bekannte Einschränkungen / offene Punkte

- **Collections für Recordings:** Emby-Collections (BoxSets) sind primär für Movies
  gedacht. Ob sie Aufnahmen (`.ts`) sauber aufnehmen und in der UI korrekt anzeigen,
  wird in einem Spike (Issue #3) verifiziert; Fallbacks (Playlists oder Tags) sind
  im Design vorgesehen.
- **Konfigurations-UI:** Ob das deklarative Emby-Plugin-UI editierbare Regel-Listen
  und Action-Buttons unterstützt, ist noch offen (Issue #9); Fallback ist eine
  klassische eingebettete HTML-Seite.

Der jeweils aktuelle Stand dieser Punkte steht in
[`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md).

## Für Mitwirkende / Coding-Agents

Verbindliche Leitplanken (u. a. „keine Fantasie-APIs", Idempotenz, Reihenfolge der
Arbeitspakete) stehen in [`CLAUDE.md`](CLAUDE.md). Die verifizierte Emby-API-Oberfläche
in [`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md). Fahrplan in
[`docs/PLAN.md`](docs/PLAN.md).
