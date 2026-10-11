# Swiss Police Simulator – Switzerland Open World

Realistische Polizeisimulation (Unity 6, URP, Windows) auf echten Schweizer Geodaten (swisstopo, OpenStreetMap), mit Fedlex-verifiziertem Recht, 26 Kantonen/Zuständigkeiten, Verkehrssimulation, sprechenden NPCs, Polizeicomputer, Funkzentrale, Karriere/Konsequenzen und Speicherstand.

**Spielbarer Pilotraum:** Region Bern (25 km²). Das ganze Land ist technisch vorbereitet (Format, Streaming, hierarchisches Routing, Backer), muss aber noch gebacken werden (siehe `docs/DATA_SOURCES.md`).

> **Ehrlicher Status:** In der Entwicklungsumgebung stand **kein Unity** zur Verfügung. Logik (Core) ist mit 139 NUnit-Tests ausserhalb Unity getestet; der Unity-Code wurde gegen die echte UnityEngine-API kompiliert (`Tools/RuntimeCheck`), aber **nie in Unity ausgeführt**. Shader sind nicht kompiliert. Screenshots in `docs/screenshots` stammen vom **Software-Renderer** (nicht Unity). Details: [`docs/STATUS.md`](docs/STATUS.md).

## Starten
1. Unity 6 (6000.0.x) → Projektordner öffnen. Beim ersten Öffnen richtet `Swiss Police Sim ▸ Projekt einrichten` Linear-Farbraum, URP-Asset und SSAO ein.
2. Leere Szene öffnen und **Play** drücken – `GameBootstrap` baut die komplette Szene zur Laufzeit auf (Menü → Korps wählen → Neues Spiel).
3. Steuerung: `F1` im Spiel.

Wichtigste Tasten: WASD · Maus · F Fahrzeug · E ansprechen · G anhalten · R Radarpistole · B Blaulicht · Q Sirene · Tab Computer · M Karte/Navi · P Rapport · N Notizbuch · K Laufbahn · F5/F9 Schnellspeichern/-laden · Esc Pause.

## Optionales
* **KI-Dialoge:** Umgebungsvariable `ANTHROPIC_API_KEY` setzen und im Pausenmenü aktivieren. Das Modell formuliert nur die Antwort um; Aktionen und Zustand bleiben beim Offline-NPC-Modell. Der Schlüssel wird nie gespeichert.
* **Sprache:** Windows-Sprachausgabe/-erkennung (System.Speech via PowerShell), je nach installierten Sprachpaketen.

## Tests (ohne Unity)
```
dotnet test Tools/Core.Tests            # 139 Tests
dotnet build Tools/RuntimeCheck         # Unity-Code gegen echte UnityEngine-API
dotnet run --project Tools/Preview -c Release -- --scene drive --out x.png   # Software-Vorschau
```
Dokumente: [`ARCHITECTURE`](docs/ARCHITECTURE.md) · [`DATA_SOURCES`](docs/DATA_SOURCES.md) · [`LEGAL_STATUS`](docs/LEGAL_STATUS.md) · [`STATUS`](docs/STATUS.md)
