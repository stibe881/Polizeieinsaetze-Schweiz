# Projektstatus

## Phase 1 – Technisches Fundament

| Punkt | Stand | Nachweis |
|---|---|---|
| Unity-Projektstruktur, asmdefs, Packages | angelegt | **in Unity nicht geöffnet** |
| Architektur dokumentiert | ✔ | `docs/ARCHITECTURE.md` |
| Schweizer Koordinatensystem (WGS84⇄LV95, Kachelraster, Floating-Origin-Mathematik) | ✔ implementiert, **getestet** | NUnit, 33 Tests grün (`dotnet test Tools/Core.Tests`) |
| Geodatenquellen geprüft | teilweise | `docs/DATA_SOURCES.md` |
| GIS-Import (swisstopo STAC → XYZ → Terrain-Kacheln; OSM → Strassen-Kacheln) | ✔ **real ausgeführt** für Bern 5×5 km | 25 Gelände- und 46 Strassenkacheln im Repo |
| Realer Geländeausschnitt | ✔ | Test: Höhe am Bundesplatz im Bereich 530–552 m (Bern ≈ 540 m) |
| Reales Strassennetz Pilotraum Bern | ✔ importiert | Test: Route Bundesplatz → Wankdorf 3283 m über >1000 Ways, kachelübergreifend |
| Streaming-System | geschrieben | **in Unity nicht ausgeführt** |
| Terrain-/Strassen-Erzeugung in Unity | geschrieben | **in Unity nicht ausgeführt** |
| Spielersteuerung (zu Fuss) | geschrieben | **in Unity nicht ausgeführt** |
| Fahrbares Polizeifahrzeug (WheelCollider, Blaulicht, Horn, Blinker, 3 Kameras) | geschrieben | **in Unity nicht ausgeführt**, Fahrwerte nicht abgestimmt |
| Navigation mit Routenlinie | geschrieben | Routing getestet (Core); Darstellung nicht ausgeführt |
| PlayMode-Smoke-Test | geschrieben | **nicht ausgeführt** |

### Was heisst „nicht ausgeführt“?
In der Entwicklungsumgebung stand kein Unity-Editor zur Verfügung. Der Unity-abhängige Code wurde nur per Roslyn auf Syntaxfehler geprüft (keine gefunden; fehlende UnityEngine-Typen konnten nicht aufgelöst werden). **Es kann Kompilierfehler gegen die echten Unity-APIs geben** und das Fahrverhalten ist ungetestet. Erster Schritt bei Unity-Zugang: Projekt öffnen, Kompilierfehler beheben, `Swiss Police ▸ Create Phase-1 Scene`, Play.

### Bekannte Einschränkungen Phase 1
* Platzhalter-Grafik (Primitive); kein Gebäude, keine Vegetation, kein Wasser, keine Strassenmarkierungen.
* Strassen liegen 12–20 cm über dem Terrain (kein Terrain-Carving); Tunnel folgen der Oberfläche.
* Pilotdaten basieren auf OSM-Ständen vom 2026-10-10; Tempolimits ohne Tag sind Klassen-Defaults.
* Kein Rechtssystem, keine NPCs, keine Verkehrssimulation, kein Speichern (Phasen 2–6).
* Startposition: nächster Strassenknoten zum Bundesplatz – **keine** reale Polizeistation (Koordinaten nicht verifiziert).

## Nächste Schritte (Phase 2 – Pilotraum Bern)
1. Unity-Öffnung, Kompilierung und Fahrtest; Messung von FPS/Ladezeit/RAM mit 25 Kacheln.
2. Gebäude Bern (OSM-Grundrisse + swissBUILDINGS3D prüfen), Wasser (Aare), Vegetation.
3. Ampeln/Kreuzungen/Signalisation aus OSM; Verkehrssimulation (`TrafficManager`, `NPCVehicleAI`).
4. Erste Verkehrskontrolle: Verstoss-Erkennung → Anhalten → NPC-Dialog (Offline-Modus) → `LegalSystemManager` mit verifizierten Rechtsquellen.
5. Speichersystem-Grundgerüst.
