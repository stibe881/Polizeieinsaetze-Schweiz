# Swiss Police Simulator – Switzerland Open World

Realistischer Polizeisimulator in Unity, Endziel: die **gesamte Schweiz** im Massstab 1:1 aus echten Geodaten (swisstopo, OpenStreetMap).

**Stand:** Phase 1 (technisches Fundament, Pilotraum Bern). Details und ehrlicher Teststatus: [`docs/STATUS.md`](docs/STATUS.md). Architektur: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md). Datenquellen/Lizenzen: [`docs/DATA_SOURCES.md`](docs/DATA_SOURCES.md).

## Starten (Unity)
1. Unity Hub → Projekt öffnen (Unity 6 LTS, 6000.0.x). Kompilierfehler melden/beheben (der Code wurde noch nie in Unity kompiliert).
2. *Project Settings ▸ Player ▸ Active Input Handling*: **Input System Package (New)** oder *Both*.
3. Menü **Swiss Police ▸ Create Phase-1 Scene** (legt Szene + URP-Asset an), Play drücken.
4. Steuerung: WASD, Leertaste Handbremse, F ein-/aussteigen, C Kamera (Verfolger/Cockpit/Haube), L Licht, B Blaulicht, Q Horn, Z/X/H Blinker/Warnblinker, N Navi-Ziel, M Route löschen, Esc Maus freigeben.

Die Pilotdaten (Bern 5×5 km) liegen bereits unter `Assets/StreamingAssets/SwissWorld`. Neue Gebiete: **Swiss Police ▸ Import Geodata…** oder
`dotnet run --project Tools/SwissWorldBaker -c Release -- --lat <breite> --lon <länge> --radius <n> --out Assets/StreamingAssets/SwissWorld`.

## Tests (ohne Unity)
```
dotnet test Tools/Core.Tests
```
