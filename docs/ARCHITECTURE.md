# Architektur – Swiss Police Simulator

Ziel: die **gesamte Schweiz** (≈ 41 285 km²) im Massstab 1:1. Phase 1 liefert das technische Fundament und den Pilotraum Bern (5 × 5 km), die Architektur ist aber von Anfang an für das ganze Land ausgelegt.

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

## Kachelraster & Streaming

* Weltzelle = **1 km × 1 km**, ausgerichtet am LV95-Kilometergitter. `TileId = (floor(E/1000), floor(N/1000))` – identisch zur Nummerierung der swisstopo-Kacheln (z. B. `2600-1199`).
* Pro Kachel zwei Binärdateien in `StreamingAssets/SwissWorld/`: `terrain/X-Y.spt` (257² Höhenwerte, 16 Bit quantisiert, ~132 kB) und `roads/X-Y.spr` (Ways mit Knoten-IDs, Höhen, Attributen). Strassen werden an Kachelgrenzen geteilt; die Grenzknoten sind geteilt, das Netz bleibt verbunden.
* `TileStreamingManager`: Radius um das Ziel, nächste Kacheln zuerst, Dateizugriff + Geometrieberechnung (Strassenmesh, Splat-Map) auf Worker-Threads, Erzeugung der Unity-Objekte auf dem Main-Thread (max. 1 Kachel/Frame), Unload mit Hysterese.
* Terrain: ein `Terrain` pro Kachel, Nachbarn verknüpft, 3 Texturebenen (Gras/Fels/Schnee) nach Neigung und Höhe.
* Strassen: Mesh-Band pro Kachel (3 Submeshes: Autobahn/Autostrasse, Hauptstrassen, übrige), alle 8 m abgetastet; **Brücken** behalten ihre gebackene, lineare Höhe (z. B. über die Aare), Tunnel folgen vorerst der Oberfläche.
* Austauschpunkt: `TileStreamingManager.LoadTileData` ist die einzige Stelle, die Dateien liest. Ein Addressables-Provider ersetzt sie in einer späteren Phase (siehe Roadmap).

## Mehrstufiges Detailmodell (Ziel)

| Stufe | Inhalt | Status |
|---|---|---|
| C – Landschaft | Gelände (swissALTI3D), Strassen, später Wald/Wasser | Phase 1: Gelände + Strassen |
| B – Dörfer/Agglomeration | OSM-/swissBUILDINGS3D-Gebäude, Prozedural | Phase 3 |
| A – Hochdetaillierte Städte | Handmodellierte Wahrzeichen, Ampeln, Signalisation | Phase 2 (Bern) / 7 |

## Navigation

`RoadGraph` (Core): gerichteter Graph aus OSM-Knoten, Kosten = Reisezeit bei Höchstgeschwindigkeit, Einbahn-Logik, A* mit Luftlinien-Heuristik, Räumlicher Hash für „nächster Knoten“. **Bekannte Lücken:** keine Abbiegeverbote (OSM-Relationen), keine Fahrverbote nach Fahrzeugklasse, Höchstgeschwindigkeit ohne Tag = Klassen-Default (`SpeedSource.Default`, vereinfachte Spielregel, nicht die tatsächliche innerorts/ausserorts-Signalisation). Für die ganze Schweiz braucht es einen kachelweisen, hierarchischen Graphen (Autobahnebene + lokale Ebene); der aktuelle Ansatz (alles im RAM) skaliert nur auf Regionen.

## Performancebudget (Ziel, noch nicht gemessen)

* ≥ 60 FPS auf Mittelklasse-PC, 25 geladene Kacheln (5 × 5 km).
* Pro Kachel Ziel < 40 MB RAM; Kachel-Erzeugung < 8 ms Main-Thread-Anteil (heute **nicht gemessen** – Messung steht für die erste Unity-Ausführung an).

## Rechtliches / Polizeilogik (ab Phase 2/5)

Noch nicht implementiert. Vorgabe: Jede Rechtsregel wird mit Status **verifiziert / vereinfachte Spielregel / unverifizierte Annahme** und Quellenangabe (fedlex.admin.ch, kantonale Polizeigesetze) im Datensatz geführt; keine erfundenen Artikel.
