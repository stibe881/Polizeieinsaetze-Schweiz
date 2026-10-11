# Swiss Police Simulator – Switzerland Open World

Realistische Polizeisimulation (Unity 6, URP, Windows) auf echten Schweizer Geodaten (swisstopo, OpenStreetMap), mit Fedlex-verifiziertem Recht, 26 Kantonen/Zuständigkeiten, Verkehrssimulation, sprechenden NPCs, Polizeicomputer, Funkzentrale, Karriere/Konsequenzen und Speicherstand.

**Spielbarer Pilotraum:** Region Bern (25 km²). Das ganze Land ist technisch vorbereitet (Format, Streaming, hierarchisches Routing, Backer), muss aber noch gebacken werden (siehe `docs/DATA_SOURCES.md`).

> **Ehrlicher Status:** Es stand weder Unity noch Unreal zur Verfügung. **Getestet:** Spiellogik (139 NUnit-Tests), Server und Web-UI (`Tools/SwissClientTest`, Headless-Browser). **Nicht getestet:** der Unreal-C++-Client (nur gegen eine Stub-API syntaxgeprüft) und der ältere Unity-Client (gegen UnityEngine kompiliert, nie ausgeführt). Screenshots in `docs/screenshots` stammen vom **Software-Renderer** bzw. aus der Web-UI, nicht aus einer Engine. Details: [`docs/STATUS.md`](docs/STATUS.md).

## Starten
Das Spiel besteht aus **zwei Teilen**: einem C#-Server (Spiellogik, Weltdaten) und einem Darstellungs-Client.

**Server (läuft sofort, ohne Engine):** `./start_server.sh` bzw. `start_server.bat` → `http://127.0.0.1:47800/` im Browser öffnen: Menü, HUD, Polizeicomputer, Karte/Navigation, Kontaktfenster, Einsätze und Rapporte als **Draufsicht-Sandbox** (WASD, F, B/Q, R, G, E, Tab, M …). Damit lässt sich die komplette Spiellogik spielen und testen.

**Unreal-Client (empfohlen für das Aussehen):** `Unreal/SwissPoliceSim` mit Unreal Engine 5.4 öffnen, Materialien per `Content/Python/sps_setup.py` erzeugen, leeres Level als Default Map setzen, Play – Anleitung in [`docs/UNREAL.md`](docs/UNREAL.md). **Ungetestet** (keine Engine verfügbar), siehe Status.

**Unity-Variante (veraltet):** `Assets/SwissPoliceSim/Runtime` ist ein früherer vollständiger Unity-Client; er wird nicht mehr weiterentwickelt.

Steuerung: `F1` im Spiel. Wichtigste Tasten: WASD · Maus · F Fahrzeug · E ansprechen · G anhalten · R Radarpistole · B Blaulicht · Q Sirene · Tab Computer · M Karte/Navi · P Rapport · N Notizbuch · K Laufbahn · F5/F9 Schnellspeichern/-laden · Esc Pause.

## Optionales
* **KI-Dialoge:** Umgebungsvariable `ANTHROPIC_API_KEY` setzen und im Pausenmenü aktivieren. Das Modell formuliert nur die Antwort um; Aktionen und Zustand bleiben beim Offline-NPC-Modell. Der Schlüssel wird nie gespeichert.
* **Sprache:** Windows-Sprachausgabe/-erkennung (System.Speech via PowerShell), je nach installierten Sprachpaketen.

## Tests (ohne Unity)
```
dotnet test Tools/Core.Tests            # 139 Tests
dotnet run --project Tools/SwissClientTest   # End-to-End gegen den laufenden Server
sh Tools/UeCheck/check.sh               # Unreal-C++ gegen Stub-API (Syntax/Typen)
dotnet build Tools/RuntimeCheck         # Unity-Code gegen echte UnityEngine-API
dotnet run --project Tools/Preview -c Release -- --scene drive --out x.png   # Software-Vorschau
```
Grafik-Quellen: [`docs/CREDITS.md`](docs/CREDITS.md). Eigene Fahrzeugmodelle: Prefab nach `Assets/SwissPoliceSim/Resources/Models/Vehicles/` (siehe `docs/STATUS.md`).

Dokumente: [`ARCHITECTURE`](docs/ARCHITECTURE.md) · [`DATA_SOURCES`](docs/DATA_SOURCES.md) · [`LEGAL_STATUS`](docs/LEGAL_STATUS.md) · [`STATUS`](docs/STATUS.md)
