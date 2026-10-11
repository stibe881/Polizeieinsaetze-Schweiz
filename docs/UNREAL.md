# Unreal-Engine-Client

Der Client (Unreal 5.4, C++) ist eine **dünne Darstellungsschicht**: Die gesamte Spiellogik (Recht, NPCs, Verkehr, Fussgänger, Einsätze, Funk, Computer, Karriere, Routing, Speichern) läuft im C#-Server `Tools/SwissServer`, der den Kern `Assets/SwissPoliceSim/Core` hostet. Protokoll: [`PROTOCOL.md`](PROTOCOL.md).

```
SwissServer (C#, net8)  <—WebSocket—>  Unreal-Client (C++)   Kacheln, Autos, Menschen, Spielerfahrzeug, Himmel
        |                                      \__ Web-UI (HTML/JS) als transparentes Overlay (SWebBrowser): HUD, Fenster, Karte
        +—— Core: Welt, Recht, NPCs, Verkehr …
```

## Starten
1. **Server:** `dotnet run --project Tools/SwissServer -c Release` (liest `Assets/StreamingAssets/SwissWorld` und `.../Law`; Parameter `--world`, `--law`, `--port`). Test ohne Unreal: `http://127.0.0.1:47800/` öffnen – die Web-UI läuft dann als Draufsicht-Sandbox (WASD, F, B/Q, R, G, E, Tab, M …).
2. **Unreal 5.4** installieren, `Unreal/SwissPoliceSim/SwissPoliceSim.uproject` öffnen (Visual Studio 2022 mit „Game development with C++“ nötig, das Modul wird beim ersten Öffnen kompiliert).
3. **Materialien erzeugen:** Im Editor *Tools ▸ Execute Python Script …* → `Content/Python/sps_setup.py`. Das importiert die ambientCG-Texturen und baut `M_Sps_VertexColor`, `M_Sps_Pbr` (Triplanar, weltausgerichtet) und alle `MI_<Slot>`. Ohne dieses Skript rendert der Client mit dem Engine-Raster-Material.
4. **Level:** Neues leeres Level (*Basic*) anlegen, speichern (z. B. `/Game/SPS/L_Sps`), als *Game Default Map* setzen. Der GameMode (`SpsGameMode`) ist bereits in `DefaultEngine.ini` eingetragen und spawnt alles zur Laufzeit.
5. Play. Menü (Web-Overlay) → Korps → *Neues Spiel*.

Optionen: `-SpsServer=ws://host:47800/ws` (Kommandozeile), `[/Script/SwissPoliceSim.SpsSettings] ServerUrl/UiUrl` in `DefaultGame.ini`.

## Was der Client macht
`ASpsClient` verbindet, hält Modelle/Kacheln, baut `ASpsTile` (Gelände, Strassen, Gebäude, Wasser, Mobiliar, Schiene, zusammengefasste Bäume) als ProceduralMesh, spawnt `ASpsCar` / `ASpsHuman` / `ASpsProp` aus Snapshots, sendet 10×/s die Pose. `ASpsPlayerFoot` (Charakter) und `ASpsPlayerCar` (Arcade-Fahrmodell, Bodenfolge per Line-Trace, Sweep-Kollision) sind die Spielerfiguren. `ASpsAtmosphere` setzt Sonne/Himmel/Wolken/Nebel/Belichtung aus Spielzeit und Wetter (Lumen, Virtual Shadow Maps, SkyAtmosphere, Volumetric Clouds über `DefaultEngine.ini`). `ASpsController` verarbeitet Eingaben und leitet Fenster-Tasten an die Web-UI weiter.

## So wird es hübscher (der eigentliche Hebel)
Der Client zeigt standardmässig die **prozeduralen Meshes** des Servers. Für Spiel-Qualität gehören hochwertige Assets dazu:
* **Gebäude/Strassenmobiliar/Vegetation:** Quixel Megascans (Fab, kostenlos für Unreal). Die Materialien (`MI_Plaster`, `MI_Road0` …) lassen sich durch Megascans-Surfaces ersetzen – gleiche Namen unter `/Game/SPS/Materials` genügen.
* **Autos und Personen austauschen** (eingebaut, ungetestet): in `Config/DefaultGame.ini`
  ```
  [/Script/SwissPoliceSim.SpsModels]
  +Car=car:police|/Game/Vehicles/SM_PoliceWagon.SM_PoliceWagon|1.0|0|0|0|0
  +Car=car|/Game/Vehicles/SM_Sedan.SM_Sedan|1.0|0|0|0|0
  +Human=human|/Game/Characters/SK_Person.SK_Person|/Game/Characters/A_Walk.A_Walk|/Game/Characters/A_Idle.A_Idle|1.0|0|-90|0
  ```
  Auto: `Schlüssel-Präfix | Static Mesh | Skalierung | Offset X | Y | Z | Gier` (Mesh-Ursprung = Wagenmitte am Boden, +X vorne, cm). Mensch: `Präfix | Skeletal Mesh | Geh-Animation | Idle-Animation | Skalierung | Offset X | Offset Z | Gier | [Animation-Blueprint-Klasse]`. Mit Animation Blueprint (z. B. `/Game/Characters/ABP_Person.ABP_Person_C`) setzt der Client jede Frame die Variablen `Speed` (float, m/s) und `IsMoving` (bool); fehlende Variablen werden übersprungen. Der längste passende Präfix gewinnt (`car:police` vor `car`; Schlüssel siehe `PROTOCOL.md`). City Sample (Autos, Fussgänger) und MetaHuman (Figuren) lassen sich so einhängen.
* Nanite/Lumen greifen für diese Assets; die prozeduralen Meshes selbst sind weder Nanite noch LOD-optimiert.

## Weitere Funktionen des Clients
* **Gestaffelter Kachelaufbau** (ein Mesh-Abschnitt pro Aufruf, Zeitbudget 6 ms/Frame) gegen Ruckler.
* **Origin-Rebasing:** ab 5 km Entfernung vom Ursprung wird die Welt auf ganze Kilometer neu zentriert.
* **Fussgänger-Kollision:** angefahrene Personen bleiben liegen; der Server bewertet es rechtlich (`pedHit`).
* **Ampeln:** Signalfarben aus UV1 und dem 60-s-Zyklus (Material `M_Sps_Signal`).
* **Regen/Schnee:** instanzierte Streifen/Flocken um die Kamera (`ASpsWeather`), Intensität aus dem Server-Wetter. Nasse Oberflächen: globaler Parameter `Wetness` in `MPC_Sps` (Rain macht dunkler und glatter, Trocknen dauert Minuten); wirkt nur in `M_Sps_Pbr`.

## Stand / Ehrlichkeit
* **Kein Unreal war verfügbar.** Der C++-Code wurde nur gegen eine selbst geschriebene Stub-API geprüft (`Tools/UeCheck/check.sh`: Syntax/Typen). **Er ist nie gegen die echte Engine kompiliert oder gestartet worden**; mit API-Abweichungen (UE 5.4 angenommen) ist zu rechnen. `sps_setup.py` ist ebenfalls ungetestet.
* **Server und Web-UI sind getestet** (`Tools/SwissClientTest`: End-to-End-Skript, alle Prüfungen grün; Web-UI per Headless-Browser).
* Bekannte Lücken: Regen/Schnee sind einfache Instanzen (kein Niagara), Nummernschilder sind Textkomponenten, kein Spiegelbild, Spielerauto ohne Fahrwerk-Physik (Arcade-Modell).
