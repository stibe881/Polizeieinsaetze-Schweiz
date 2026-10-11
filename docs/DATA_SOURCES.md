# Geodatenquellen, Verfügbarkeit und Lizenzen

Stand der Prüfung: 2026-10-10. Was **tatsächlich geprüft** wurde, ist markiert; Lizenzaussagen sind **keine Rechtsberatung** und müssen vor einer Veröffentlichung nochmals gegen die offiziellen Bedingungen geprüft werden.

| Quelle | Verwendung | Verfügbarkeit (geprüft) | Lizenz / Pflichten |
|---|---|---|---|
| **swissALTI3D** (swisstopo) | Gelände | ✔ STAC-API `data.geo.admin.ch/api/stac/v0.9` erreichbar; 1-km-Kacheln, 0.5 m und 2 m, GeoTIFF und ASCII-XYZ, LV95/LN02; Abdeckung ganze Schweiz + FL. Genutzt: 2 m XYZ (ca. 1 MB/Kachel), Ausgabe 2019. | Die STAC-Collection weist `license: proprietary` aus. swisstopo gibt seine Geodaten als Open Government Data frei (Quellenangabe nötig). **Wortlaut der Nutzungsbedingungen in dieser Sitzung nicht abrufbar (URL lieferte 404) → vor Release prüfen.** Quellenangabe: „swissALTI3D © swisstopo“. |
| **OpenStreetMap** | Strassennetz, Attribute | ✔ OSM-API 0.6 (`/map`) erreichbar und verwendet (25 Anfragen mit Cache, 1 s Pause). Overpass/Geofabrik waren aus der Entwicklungs-Sandbox **nicht** erreichbar. | ODbL: Quellenangabe „© OpenStreetMap contributors“, Share-Alike für abgeleitete *Datenbanken*. Die gebackenen `.spr`-Dateien sind eine abgeleitete Datenbank → bei Weitergabe unter ODbL stellen. Für grosse Gebiete **nicht** die OSM-API, sondern Overpass-Instanz/Planet-Extrakt (Geofabrik) verwenden. |
| **swissTLM3D** | genauere Strassen/Brücken/Tunnel, Gewässer, Wald | ✔ STAC-Collection `ch.swisstopo.swisstlm3d` vorhanden (Ausgabe 2020-03 gesehen); Dateigrösse/Format für Landesumfang noch nicht geprüft. | Wie swisstopo OGD, siehe oben. Vorgesehen für Phase 3. |
| **swissBUILDINGS3D** | Gebäude (3D) | nicht verwendet | Gebäude stammen aus OSM-Grundrissen (Höhe aus swissSURFACE3D abzüglich Gelände bzw. `building:levels`). |
| **swissIMAGE / Swiss Map Raster** | Texturen, Kartenansicht | nicht geprüft | Phase 3/4 – Lizenz und Texturgrösse für Echtzeit noch zu prüfen. |
| Kantonale Daten (Polizeistationen, Signalisation) | Stationen, Tempolimits | nicht geprüft | Phase 4 – je Kanton unterschiedlich. |

## Quellenangaben im Spiel

Das HUD zeigt „swisstopo swissALTI3D, © OpenStreetMap contributors“. Vor einem Release gehört ein vollständiger Credits-Bildschirm dazu.

## Reproduzierbarkeit

`Tools/SwissWorldBaker` und das Editor-Fenster *Swiss Police ▸ Import Geodata…* erzeugen die Kacheln. Downloads landen in `.cache/` bzw. `Library/SwissImportCache` (nicht versioniert). Die im Repo liegenden Pilotdaten (`Assets/StreamingAssets/SwissWorld`, ~4.4 MB) wurden mit

```
dotnet run --project Tools/SwissWorldBaker -c Release -- --lat 46.94658 --lon 7.44410 --radius 2 --out Assets/StreamingAssets/SwissWorld
```

erzeugt. Für grössere Datenmengen Git LFS oder externen Download verwenden.

## Hochrechnung auf die ganze Schweiz (Schätzung aus den gemessenen Pilotdaten)

Der Pilotraum (25 Kacheln, Bern) belegt **6.7 MB** inkl. Gebäude, Wasser, Bäume, Strassen und Fernterrain-Blöcke (≈ 270 kB je Kachel, davon Gelände ≈ 60–70 kB statt früher 132 kB).
Terrain-Format SPT2 (5-cm-Stufen, Median-Edge-Detector-Vorhersage, Zig-Zag-Varints) und ein Grobraster-Chunk (TERC) ersetzen die frühere 16-Bit-Rohspeicherung.

| Grösse | Schätzung (≈ 41 300 Kacheln) |
|---|---|
| Gelände | ≈ 2.5–3 GB (statt 5.5 GB) |
| Gelände + Gebäude + Strassen + Wasser (urban hoch, alpin niedrig) | ≈ 8–12 GB gesamt, **geschätzt, nicht gemessen** |
| Fernterrain (8-km-Blöcke `.spm`) | < 150 MB |
| Routing-Skelett (`arterial.sprg`) | < 100 MB |

**Routing nicht mehr komplett im RAM:** `HierarchicalRouter` hält nur ein Skelett (Autobahnen/Hauptstrassen, `arterial.sprg`) und lädt Kachelgraphen nur entlang der Route nach (LRU).
**Datenhaltung:** Kacheln in Regionspaketen `.sprg` (ein Index, Direktzugriff), nicht je Datei in Git. Für das Landesbacken: Overpass-/Geofabrik-Extrakt statt OSM-API (siehe unten, in der Entwicklungsumgebung nicht erreichbar).

## Landesweit backen (vom Nutzer auszuführen)
```
dotnet run --project Tools/SwissWorldBaker -c Release -- --help
```
Modi: Kachelbau je Region, Makro-Blöcke, Arterial-Skelett, Regionspakete, Verwaltungsgrenzen (`admin.spj`). swisstopo-Daten (swissALTI3D, swissSURFACE3D, swissBOUNDARIES3D) sind über die STAC-API erreichbar; Lizenzbedingungen vor Veröffentlichung nochmals prüfen.
