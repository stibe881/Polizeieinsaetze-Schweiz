# SwissServer-Protokoll

Ein WebSocket (`ws://127.0.0.1:47800/ws`). **Text-Frames** sind JSON-Objekte mit Feld `t`, **Binär-Frames** beginnen mit einer Typ-Kennung. Alle Zahlen little-endian.
Clients melden sich mit `{"t":"hello","role":"game|ui|sandbox"}` an: `game` = Unreal (bekommt Kacheln, Modelle, Snapshots), `ui` = eingebettete Web-Oberfläche, `sandbox` = Web-Oberfläche im Eigenbetrieb (bekommt Snapshots, aber keine Kacheln/Modelle).

## Koordinaten
* Server und Kerndaten: **LV95** (E, N, Höhe m ü. M.). Gitter 1 km, `TileId = (floor(E/1000), floor(N/1000))`.
* Meshes im Netz: Kachel-lokal in **Metern**, Achsen wie Unity (x = Ost, y = Höhe absolut, z = Nord), Ursprung = Südwestecke der Kachel. Der Unreal-Client wandelt in cm um (X = Nord, Y = Ost, Z = oben, zyklische Vertauschung → Windungsrichtung bleibt erhalten) und spiegelt V (1 − v).
* Gierwinkel: Grad, 0 = Nord, im Uhrzeigersinn.

## Binär (Server → Client)
| Typ | Inhalt |
|---|---|
| 1 Tile | `i32 x, i32 y, u8 quality (1 nah, 2 mittel, 3 fern), u8 n`, dann n × (`u8 kind, Mesh`), dann `i32 trees`, je Baum `f32 e, n (lokal), ground, height, radius, u8 kind, variant, bucket`. kind: 0 Terrain, 1 Strassen, 2 Gebäude (einfach), 3 Gebäude (Detail, nur im Umkreis ~230 m), 4 Wasser, 5 Mobiliar (mit uv2), 6 Schiene |
| 2 Snapshot (10 Hz) | `f64 time`; `i32 n` × Auto (`i32 id, f64 e, n, f32 h, yaw, speed(m/s), u16 flags`); `i32 n` × Fussgänger (`i32 id, f64 e, n, f32 h, yaw, u8 state, f32 speed`); `i32 n` × Szenenperson (`i32 id, f64 e, n, f32 h, yaw, u8 pose, u8 flags`) |
| 3 TileDrop | `i32 x, i32 y` (x = INT_MIN: alle verwerfen) |
| 5 Model | `str key, str metaJson, u8 n, Mesh…` — Schlüssel `car:<kind>:<farbe>`, `car:police`, `car:ems`, `car:tow`, `human:ped:<fcE>:<hemd>:<hose>:<haut>:<haar>:<cm>:<seed>`, `human:officer:m\|f:<hiVis>`, `tree:<kind>:<variant>:<lod>:<bucket>` |

**Mesh:** `i32 vertices, i32 submeshes, u8 flags (1 = uv2, 2 = 32-Bit-Indizes), f32 pos[3v], f32 normal[3v], f32 uv[2v], u8 rgba[4v], [f32 uv2[2v]]`, je Submesh `i32 count` + Indizes (u16 oder i32). Submesh-Bedeutung: Strassen 0–5 (Autobahn, Haupt, lokal, Trottoir, Markierung weiss/gelb), Gebäude 0–9 (Fassade ×3, Dachziegel, Flachdach, Putz, Fenster, Gesims, Läden, Geländer), Mobiliar 0–2, Schiene 0–3, Auto 0–11 (`CarMesher.Sub*`), Mensch 0–2, Baum 0–1.

## Text (Client → Server)
`new {corps,seed}`, `load {slot}`, `save {slot}`, `pause {on}`, `timescale {v}`, `pose {e,n,h?,yaw,speed,inVehicle,siren,blue,vE,vN,vH?,vYaw,fuel,damage}` (10 Hz), `model {key}`, `accept`, `decline`, `radar {on}`, `measure {vehicleId,dist}`, `stop {vehicleId}`, `approach {vehicleId|actorId}`, `say {text}`, `enc {op,fine}` (ops: reason, docs, breath, stepout, searchP, searchV, queryPlate, queryPerson, fine, warn, rights, cuff, arrest, report, release, end), `computer {kind,plate|first,last,birth,reason}`, `computerLog`, `route {e,n,name}`, `routeClear`, `search {text}`, `poi {e,n,r}`, `service {which: ems|tow|fire|backup}`, `report {atFault,injured,amb,tow,narrative}`, `pursuit {op: heli|roadblock|abort}`, `collision {vehicleId,rel}`, `career`, `notebook`, `skip`, `promote`, `ui {…}` (wird an alle weitergereicht: `open`, `focus`).

## Text (Server → Client)
`welcome`, `started`, `state` (2 Hz: Uhr, Ort, Tempolimit, Zuständigkeit, Sonne/Wetter, Karriere, Funk, Einsatz, Navigation, Radar, Verfolgung, Kontakt), `note`, `spawn`/`despawn` (Autos, Fussgänger), `props`, `actors`, `encounter`, `encounterEnd`, `computerResult`, `computerLog`, `searchResult`, `pois`, `route`, `measure`, `speak`, `beep`, `career`, `notebook`, `error`.

## HTTP
`/` Web-UI · `/map/tile/{x}/{y}.png` (512 px, Zeile 0 = Süden) · `/map/macro/{bx}/{by}.png` · `/api/status`.
