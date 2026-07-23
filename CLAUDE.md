# CLAUDE.md — Guardrails für Coding-Agents

Diese Datei wird automatisch als Kontext geladen. Sie ist **verbindlich** für jede
Änderung in diesem Repository. Wenn eine Anweisung hier mit einer Ad-hoc-Idee
kollidiert, gewinnt diese Datei — oder du fragst nach.

## Was dieses Projekt ist

Emby-**Server-Plugin** (C#, `netstandard2.0`), das Collections automatisch anhand
konfigurierbarer Titel-Regeln (Regex/Contains) pflegt — primär für TV-Aufnahmen
vom OpenViX/Vu+ Receiver. **Lokal, ohne Cloud, ohne Internet-Metadaten.**

Die Arbeit ist in GitHub-Issues #1–#11 zerlegt (Epic = #1). Details, Architektur
und Reihenfolge stehen in [`docs/PLAN.md`](docs/PLAN.md).

## Die wichtigsten Regeln (verletze diese nie ohne Rückfrage)

1. **KEINE Fantasie-APIs.** Verwende nur Emby-Interfaces/Methoden, deren Existenz
   und Signatur belegt sind — siehe [`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md).
   Ist eine API-Stelle unklar, markiere sie **explizit als Unsicherheit** (Kommentar
   `// UNSICHER: ...` + Notiz im Issue), erfinde nichts. Lieber ein `// TODO` mit
   offener Frage als eine plausibel klingende, nicht existierende Methode.
2. **Ein Arbeitspaket nach dem anderen.** Bearbeite genau das Issue, das dran ist,
   und respektiere die im Issue genannten **Abhängigkeiten**. Nichts vorziehen, was
   auf einem noch offenen Vorgänger aufbaut.
3. **Spike #3 blockt die Sync-Engine (#6).** Der Ziel-Mechanismus (BoxSet vs.
   Playlist vs. Tag) ist erst nach dem Spike entschieden. Baue #6 **nicht**, bevor
   `docs/emby-api-cheatsheet.md` diese Entscheidung dokumentiert.
4. **Minimal robuste erste Version.** Bevorzuge die kleinste Lösung, die die
   Akzeptanzkriterien erfüllt. Keine Feature-Erweiterungen über das Issue hinaus.

## Technische Leitplanken

- **Target-Framework:** `netstandard2.0`. Keine Sprach-/BCL-Features nutzen, die dort
  nicht kompilieren (kein `net6+`-only APIs, kein nullable-context-Zwang o. Ä.).
- **Einzige Server-Abhängigkeit:** NuGet `mediabrowser.server.core` (4.8.x). Keine
  weiteren Runtime-Abhängigkeiten ins Plugin ziehen ohne Begründung im Issue.
- **Keine Netzwerkzugriffe zur Laufzeit.** Kein HTTP, keine externen Dienste, keine
  offenen Ports, keine TMDb/Online-Metadaten. Alles rein lokal.
- **Matching-Logik serverunabhängig halten.** `TitleNormalizer` und `RuleMatcher`
  (#5) dürfen **keine** Emby-Typen außer den eigenen Rule-Klassen referenzieren,
  damit sie ohne laufenden Server unit-testbar bleiben.
- **Idempotenz ist Pflicht (#6).** Zweimal ausführen ohne Datenänderung ⇒ zweiter
  Lauf macht **0 Writes**. Kein Zustand, der bei Wiederholung Duplikate erzeugt.
- **Defensiv gegenüber fehlenden Daten.** `null`/leere Titel, fehlende Metadaten,
  zwischen Query und Apply verschwundene Items: überspringen + zählen + loggen,
  niemals den ganzen Lauf abbrechen.
- **Regex sicher ausführen.** Immer mit `MatchTimeout` (~1 s) gegen katastrophales
  Backtracking; ungültige Patterns fangen, Regel als fehlerhaft markieren, restliche
  Regeln weiterlaufen lassen. Regex **einmal pro Regel** kompilieren/cachen, nicht
  pro Item.
- **Keine Feedback-Schleifen.** Der Sync schreibt Collections; ein Event-Listener (#8)
  darf durch diese eigenen Writes keinen weiteren Sync auslösen (BoxSet-Events
  filtern, Debounce).

## Definition of Done (pro Issue)

- Alle Akzeptanzkriterien des Issues erfüllt.
- Code baut ohne neue Warnungen (`dotnet build`).
- Unit-Tests grün, wo das Issue welche vorsieht (mind. #5); CI grün.
- Neue/geänderte öffentliche Verhalten sind so gebaut, dass sie den Logging-
  Anforderungen aus #10 nicht widersprechen (aussagekräftige Fehler mit Kontext).
- Kurzer Fortschritts-Kommentar/Häkchen im zugehörigen Issue.

## Git-Workflow

- Entwicklung auf dem zugewiesenen Feature-Branch. **Nicht** auf `main`/Default pushen.
- Aussagekräftige Commit-Messages (was + warum). Ein Commit pro logischer Einheit.
- **Keinen Pull Request eröffnen, außer es wird ausdrücklich verlangt.**
- Secrets/lokale Pfade/Server-Adressen gehören nicht in den Code oder in Commits.

## Befehle (Tooling-Referenz)

> Wird ergänzt, sobald das Projektgerüst (#2) steht. Erwartete Befehle:

```bash
dotnet build -c Release       # Plugin bauen (Output: Emby.AutoCollectionsNG.dll)
dotnet test                   # Unit-Tests (Matching-Engine, Config-Roundtrip)
```

**Deployment (manuell):** gebaute DLL in den `plugins`-Ordner des Emby-Servers
kopieren, Server neu starten. Pfade: Windows `%AppData%\Emby-Server\plugins`,
Linux typ. `/var/lib/emby/plugins` bzw. `/config/plugins` (Docker).

## Verifizierte Emby-API-Oberfläche

Die konkreten, belegten Interfaces/Signaturen stehen zentral in
[`docs/emby-api-cheatsheet.md`](docs/emby-api-cheatsheet.md). **Immer dort
nachschlagen**, bevor du eine Emby-API aufrufst. Ergänze die Datei, wenn du eine
weitere API verifiziert hast (mit Quelle).
