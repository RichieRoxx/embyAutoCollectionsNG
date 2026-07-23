# Emby-API-Cheatsheet (verifizierte Oberfläche)

Zweck: **Fantasie-APIs verhindern.** Hier stehen nur Interfaces/Methoden, deren
Existenz belegt ist. Signaturen können je nach `mediabrowser.server.core`-Version
minimal abweichen — beim Implementieren gegen die tatsächlich referenzierte
NuGet-Version prüfen und Abweichungen hier korrigieren.

Status-Legende: ✅ belegt · ⚠️ plausibel, aber gegen SDK zu verifizieren · ❓ offen (Spike)

## Plugin-Basis

| Element | Status | Notiz |
|---|---|---|
| `BasePlugin<TConfig>` | ✅ | Basisklasse des Plugins; Ctor `(IApplicationPaths, IXmlSerializer)`; `Id`-GUID überschreiben |
| `BasePluginConfiguration` | ✅ | Basisklasse der Konfiguration; wird als XML im `config`-Ordner persistiert |
| `IServerEntryPoint` | ✅ | Einstiegspunkt für Initialisierung/Event-Hooks; `Run()` + `Dispose()` |
| Dependency Injection | ✅ | Services über Konstruktor injizieren (z. B. `ILibraryManager`, `ILogger`) |

Quellen: dev.emby.media/doc/plugins/dev, MediaBrowser/Emby Wiki „How to build a Server Plugin".

## Items abfragen

| Element | Status | Notiz |
|---|---|---|
| `ILibraryManager` | ✅ | zentraler Zugriff auf die Library |
| `ILibraryManager.GetItemList(InternalItemsQuery)` | ✅ | liefert Items; Filter über die Query |
| `InternalItemsQuery` | ✅ | Filter-Objekt; u. a. `IncludeItemTypes`, `Limit`, `StartIndex`, `Recursive`, `DtoOptions` |
| Paging über `Limit`/`StartIndex` | ⚠️ | für große Libraries chunked abfragen; Property-Namen gegen SDK prüfen |

`InternalItemsQuery`-Felder je nach Version leicht unterschiedlich — vor Nutzung
im Objekt-Browser / SDK verifizieren. Quelle: `Emby.Server.Implementations/Library/LibraryManager.cs`.

## Collections

| Element | Status | Notiz |
|---|---|---|
| `ICollectionManager` (`MediaBrowser.Controller.Collections`) | ✅ | Manager für BoxSets/Collections |
| `CreateCollection(...)` | ✅ | erstellt Collection; exakte Options-/Parameterform gegen SDK prüfen |
| `AddToCollection(collectionId, itemIds)` | ✅ | Items hinzufügen; genaue Typen (Guid vs. string/long) gegen SDK prüfen |
| `RemoveFromCollection(collectionId, itemIds)` | ✅ | Items entfernen |
| Events `ItemsAddedToCollection` / `ItemsRemovedToCollection` / `CollectionCreated` | ⚠️ | existieren im CollectionManager; für Vermeidung von Sync-Schleifen relevant |
| BoxSets abfragen | ⚠️ | via `InternalItemsQuery` mit `IncludeItemTypes = ["BoxSet"]`; Kinder/Mitglieder gegen SDK prüfen |

**❓ OFFENER SPIKE (#3):** Ob `ICollectionManager` Recordings/`.ts`-Videos
(Item-Typ je nach Library `Movie`/`Video`/`Episode`) aufnimmt und die Emby-UI diese
Collection korrekt anzeigt, ist **nicht bestätigt**. Ergebnis + finale Signaturen
hier eintragen. Fallbacks: `IPlaylistManager` (Playlists) oder Tag-basiert.

Quellen: Emby-Community „Get/Create Collections Plugin Service", Jellyfin
`CollectionManager.cs` (API-verwandt, aber **nicht** 1:1 identisch — nicht blind übernehmen).

## Scheduled Tasks

| Element | Status | Notiz |
|---|---|---|
| `IScheduledTask` | ✅ | `Name`/`Description`/`Category`, `Execute(...)`, `GetDefaultTriggers()` |
| Automatic Type Discovery | ✅ | Task-Klasse public im Plugin-Assembly ⇒ wird gefunden |
| `ITaskManager` | ✅ | Task programmatisch triggern (für UI-Button #9 und Event-Trigger #8) |
| Progress/CancellationToken in `Execute` | ⚠️ | Signatur gegen SDK prüfen (i. d. R. `IProgress<double>` + `CancellationToken`) |

Quelle: dev.emby.media/reference `ITaskManager`, Emby „Scheduled Tasks".

## Konfigurations-UI

| Weg | Status | Notiz |
|---|---|---|
| Deklaratives Plugin-UI (autogeneriert aus Config-Model) | ❓ | Attribute wie `[DisplayName]`, `[Description]`; **unklar**, ob editierbare Listen + Action-Buttons + dynamische Dropdowns unterstützt werden (#9) |
| Klassische eingebettete HTML-Konfigseite | ⚠️ | Fallback: `IHasWebPages`/embedded resource + JS `ApiClient`-Pattern; gegen Beispiel-Plugins verifizieren |

Quelle: dev.emby.media/doc/plugins/ui.

## Logging

| Element | Status | Notiz |
|---|---|---|
| `ILogger` (Emby-SDK) | ✅ | via DI; Info = Zahlen/Zusammenfassung, Debug = Item-Details |

---

**Pflege dieser Datei:** Wer eine ⚠️/❓-Zeile verifiziert, ändert den Status auf ✅
und ergänzt die konkrete Signatur samt Quelle. Nichts hier eintragen, das nicht
belegt ist.
