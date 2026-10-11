# Projektstatus

Stand: Ende Phase 6 der Umsetzung. **Kein Unity war verfügbar** – siehe „Was heisst geprüft?“.

## Phasen und Nachweise

| Phase | Inhalt | Nachweis |
|---|---|---|
| 1 Fundament | LV95/Kachelraster/Floating Origin, GIS-Import, Bern-Pilot, fahrbares Auto | Core-Tests |
| 2 Welt | OSM-Gebäude/Wasser/Landnutzung/Signale/POIs, swissSURFACE3D-Bäume, Tempolimits aus OSM-Tags + Orts-/Klassenregeln, Tile-Container, Prozedural-Meshing (Fassaden mit Fenstern/Läden/Balkonen/Gesimsen/Dachaufbauten, Laubbäume, Gewässer, Schienen) | Core-Tests, Software-Vorschau |
| 3 Verkehr & Fussgänger | IDM-Verkehr mit Ampeln/Vortritt, Fussgänger auf Trottoirs/Fussgängerstreifen | Core-Tests |
| 4 Recht & NPCs | Fedlex-Rechtssystem, Bussenkatalog aus OBV-Text, NLU + NPC-Psychologie (offline), Zuständigkeit aus Gemeindegrenzen | Core-Tests (139) |
| 5 Spiellogik | Korps/Rang, Karriere & Konsequenzen (bis Entlassung), Funkzentrale/Einsätze (12 Typen), Polizeicomputer mit Protokoll, Verfolgung, Rapportbewertung, Speichern | Core-Tests |
| 6 Unity-Laufzeit & UI | Streaming, Shader, Wetter/Tageszeit, Fahrzeug, Spieler, Kontrollen, Funk, Karte/Navigation, Menü, HUD, Fenster, Sprache/LLM, Editor-Setup | Kompilierung gegen UnityEngine (`Tools/RuntimeCheck`) – **nicht ausgeführt** |

## Was heisst geprüft?
* **Core (ohne Unity): getestet.** `dotnet test Tools/Core.Tests` → 139 grün.
* **Runtime (Unity-Code): kompiliert** gegen die echte UnityEngine-API (Unity-2021-Referenz; Unity 6 vorgesehen), aber **nie gestartet**. Verhalten, Fahrphysik, Performance (FPS/RAM) sind **unbekannt**.
* **Shader** (`Resources/Shaders/*.shader`): nicht kompiliert. Fehlt ein Shader, fällt `MaterialLibrary` auf URP/Lit zurück (`UsingFallback`).
* **Screenshots** sind Software-Renderer-Vorschauen (Tools/Preview) – **nicht Unity**; Beleuchtung/Post-Processing in Unity weichen ab.
* **Daten:** nur der Pilotraum Bern (25 Kacheln, 6.7 MB) liegt vor. Landesweites Backen war hier nicht möglich (Overpass/Geofabrik nicht erreichbar, Datenmenge).

## Bekannte Einschränkungen
* Grafik ist prozedural (Low-Poly mit Texturen/Details), **nicht** auf dem Niveau handmodellierter Assets. Ein Austausch durch eigene Prefabs ist noch **nicht** implementiert (geplant: Prefab-Ersatzmechanismus je Fahrzeug-/Figurentyp).
* Fahrzeugtypen: Kombi, Fliessheck, Limousine, SUV, Lieferwagen; keine LKW/Busse.
* Kantonale Polizeigesetze nicht verifiziert (`docs/LEGAL_STATUS.md`).
* Fussgänger reagieren nicht auf Verkehrsmeldungen der TrafficSimulation (nur Kollisionsprüfung).
* Rapporte: Unfall wird gegen die verdeckte Wahrheit bewertet; übrige Einsatztypen nutzen eine vereinfachte Bewertung.
* LLM-Anbindung nur formulierend; nicht getestet gegen die reale API.

## Erster Schritt mit Unity
Projekt öffnen → Kompilier-/Shaderfehler beheben → `Swiss Police Sim ▸ Projekt einrichten` → leere Szene → Play → PlayMode-Test `BootstrapSmokeTest` ausführen.
