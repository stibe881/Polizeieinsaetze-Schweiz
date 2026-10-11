# Architektur – Swiss Police Simulator

Ziel: die **gesamte Schweiz** (≈ 41 285 km²) im Massstab 1:1. Gebaut ist der Pilotraum Bern (5 × 5 km); die Architektur ist von Anfang an für das ganze Land ausgelegt.

## Engine & Version

* **Unity 6 LTS (6000.0.x)**, Universal Render Pipeline, C# 9, .NET Standard 2.1. `ProjectVersion.txt` nennt `6000.0.23f1` – die genaue Patch-Version ist **nicht verifiziert**, Unity Hub bietet bei Abweichung ein Update auf die installierte 6000.0-Version an.
* Package-Versionen (`Packages/manifest.json`) sind aus dem Gedächtnis gewählt und **noch nicht in Unity aufgelöst**.
* Unity-6-APIs im Code: `Rigidbody.linearVelocity` / `linearDamping`, `Object.FindFirstObjectByType`. Für Unity 2022 LTS müssten diese umbenannt werden.

## Schichten (strikte Trennung)

```
Assets/SwissPoliceSim/
  Core/      asmdef SPS.Core   (noEngineReferences)  Geodaten, Koordinaten, Kacheln, Höhenraster, Strassen, Routing, Import
  Runtime/   asmdef SPS.Runtime                      Darstellung, Streaming, Fahrzeug, Spieler, Navigation, HUD
  Editor/    asmdef SPS.Editor                       Import-Fenster, Szenen-Builder
  Tests/     EditMode (NUnit, läuft auch ausserhalb Unity), PlayMode
Tools/
  SwissWorldBaker/   Kommandozeilen-Importer (nutzt denselben Core-Code)
  Core.Tests/        dotnet-Testprojekt, kompiliert die Core- und Test-Quellen von Unity
```

`SPS.Core` hat **keine** UnityEngine-Abhängigkeit. Dadurch sind Geodaten-, Routing- und (später) Rechts-/Zuständigkeitslogik ausserhalb von Unity testbar und serverfähig. Spätere Module (je eigene asmdef): `SPS.Traffic`, `SPS.Npc`, `SPS.Police`, `SPS.Legal`, `SPS.Dialogue`, `SPS.Save`.

## Koordinatensystem

* Weltkoordinaten: **LV95 (EPSG:2056)**, `double` (E, N, Höhe ü. M. nach LN02). OSM-Daten (WGS84) werden mit den swisstopo-Näherungsformeln umgerechnet (`SwissCoordinateConverter`, Genauigkeit ~1–2 m; für cm-Genauigkeit wäre REFRAME/NTv2 nötig – für ein Spiel nicht erforderlich).
* Unity-Achsen: x = Ost, z = Nord, y = Höhe ü. M. (**y wird nie verschoben**).
* **Floating Origin** (`WorldFrame`, `WorldOrigin`, `FloatingOrigin`): Der Unity-Nullpunkt wird auf ein Vielfaches von 500 m verschoben, sobald das Ziel > 1500 m entfernt ist; dabei werden alle Root-Objekte der Szene um den Offset versetzt.

## Kachelraster, Datenformate, Streaming

* Weltzelle = **1 km × 1 km**, `TileId = (floor(E/1000), floor(N/1000))` (swisstopo-Nummerierung).
* **Kachel-Container `.sptile`** mit Chunks: `TERR` (Gelände, SPT2: 5-cm-Stufen, Median-Edge-Detector-Vorhersage, Zig-Zag-Varints), `TERC` (Grobgelände), `ROAD`, `BLDG`, `WATR`/`WMSH`, `TREE`, `LAND`, `POIN`/`POI`, `RAIL`.
* **Makro-Blöcke `.spm`** (8 km) für die Fernsicht, **Regionspakete `.sprg`** (Index + Direktzugriff), `admin.spj` (Kantons-/Gemeindegrenzen), `arterial.sprg` (Routing-Skelett).
* `TileStreamer`: Ringe (Detail/Mittel/Fern) um den Fokus, Datei-/Meshberechnung (`TileMeshBuilder`) auf Worker-Threads mit Frame-Budget, Erzeugung der Unity-Meshes auf dem Main-Thread, Colliders nur nahe am Spieler, LRU-Entladen.
* `TreeRenderer`: Instancing in 3 LODs (Laubkarten), `MacroRing`: Fernterrain aus Makro-Blöcken.
* Fensterrender: eigene URP-Shader (`SPS/Lit`, `SPS/Terrain`, `SPS/Water`) mit Vertexfarbe (sRGB→linear), Schatten-/Tiefenpässe aus URP/Lit.

## Skalierung (Gelände ≈ 5.5 GB → Format-Optimierung, Routing nicht im RAM)

* Gelände: SPT2 + Grobchunk, geschätzt ≈ 2.5–3 GB landesweit (Details `docs/DATA_SOURCES.md`).
* Routing: `HierarchicalRouter` – Skelett aus Hauptstrassen im RAM, Kachelgraphen lazy nur entlang der Route (LRU); Namens-/Abbiegeanweisungen aus `NavigationGuide`.
* Verkehrsnetz (`TrafficNetwork`) wird auf einem 5×5-Kachel-Fenster um den Spieler aufgebaut und mitgeführt (`Rebase`).

## Navigation

`RoadGraph` (Core): gerichteter Graph aus OSM-Knoten, Kosten = Reisezeit bei Höchstgeschwindigkeit, Einbahn-Logik, A* mit Luftlinien-Heuristik, Räumlicher Hash für „nächster Knoten“. **Bekannte Lücken:** keine Abbiegeverbote (OSM-Relationen), keine Fahrverbote nach Fahrzeugklasse, Höchstgeschwindigkeit ohne Tag = Klassen-Default (`SpeedSource.Default`, vereinfachte Spielregel, nicht die tatsächliche innerorts/ausserorts-Signalisation). Für die ganze Schweiz braucht es einen kachelweisen, hierarchischen Graphen (Autobahnebene + lokale Ebene); der aktuelle Ansatz (alles im RAM) skaliert nur auf Regionen.

## Performancebudget (Ziel, noch nicht gemessen)

* ≥ 60 FPS auf Mittelklasse-PC, 25 geladene Kacheln (5 × 5 km).
* Pro Kachel Ziel < 40 MB RAM; Kachel-Erzeugung < 8 ms Main-Thread-Anteil (heute **nicht gemessen** – Messung steht für die erste Unity-Ausführung an).

## Spielschichten (Core, ohne Unity)

`Legal` (Normen mit Verifikationsstatus, Regelwerk `LegalSystem`, `ObvCatalog` aus Gesetzestext) · `Npc` (NLU, `NpcMind`, `Citizen`) · `Game` (`Encounter`, `Career`, `Dispatch`, `Reports`, `SaveGame`, `Pursuit`, `GameClock`) · `Police` (Korps, Zuständigkeit, `PoliceComputer`) · `Traffic` (IDM, Ampeln, Fussgänger) · `Roads` (Graph, Router, Navigation) · `Meshing`/`Textures` (prozedurale Geometrie, in Unity wie im Software-Renderer identisch genutzt).

## Unity-Laufzeit (`Runtime/`)

`GameBootstrap` baut die Szene programmatisch (kein handgebautes Scene-Asset nötig) → `GameSession` (Uhr, Korps, Karriere, Computer) mit Controllern für Einsätze, Kontrollen/Radar, Begegnungen, Verfolgung; UI per IMGUI (`Gui`, `Hud`, `Windows`, `UiRoot`); Eingabe über die klassische Input-Klasse (kein Input-System-Package). Alles Unity-Abhängige ist von `Core` getrennt.

## Rechtliches
Siehe `docs/LEGAL_STATUS.md`.
