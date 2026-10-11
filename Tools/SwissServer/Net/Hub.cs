using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Police;
using SwissServer.Sim;

namespace SwissServer.Net
{
    /// <summary>One WebSocket peer: the Unreal client ("game") or a web UI ("ui"). Outgoing frames are queued so the game thread never blocks on the network.</summary>
    public sealed class ClientConn
    {
        public readonly int Id; public readonly WebSocket Ws; public string Role = "ui"; public string Name = "";
        readonly Channel<(byte[] data, WebSocketMessageType type)> _out = Channel.CreateUnbounded<(byte[], WebSocketMessageType)>();
        public readonly Dictionary<TileId, int> Tiles = new Dictionary<TileId, int>(); public readonly HashSet<TileId> Building = new HashSet<TileId>(); public readonly HashSet<string> Models = new HashSet<string>();
        public readonly Dictionary<TileId, (double e, double n)> DetailAt = new Dictionary<TileId, (double, double)>(); public readonly HashSet<int> Cars = new HashSet<int>(), Peds = new HashSet<int>(); public int PropsVersion = -1, ActorsVersion = -1; public bool Closed;
        public ClientConn(int id, WebSocket ws) { Id = id; Ws = ws; }
        public void SendText(string json) { if (!Closed) _out.Writer.TryWrite((Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text)); }
        public void SendBinary(byte[] data) { if (!Closed) _out.Writer.TryWrite((data, WebSocketMessageType.Binary)); }
        public async Task PumpAsync(CancellationToken ct)
        {
            try { await foreach (var (data, type) in _out.Reader.ReadAllAsync(ct)) { if (Ws.State != WebSocketState.Open) break; await Ws.SendAsync(data, type, true, ct); } } catch (Exception) { }
            Closed = true;
        }
        public void Close() { Closed = true; _out.Writer.TryComplete(); }
    }

    /// <summary>Connection hub and game loop: runs the simulation at 10 Hz on one thread, applies commands from the clients, streams tiles and entity snapshots.</summary>
    public sealed class Hub
    {
        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNamingPolicy = null, IncludeFields = true };
        readonly List<ClientConn> _clients = new List<ClientConn>(); readonly object _cl = new object(); int _nextId = 1;
        readonly ConcurrentQueue<(ClientConn c, JsonElement msg)> _cmds = new ConcurrentQueue<(ClientConn, JsonElement)>();
        public readonly WorldStore W; public Game Game; readonly string _lawDir; int _tileJobs; int _tick;

        public Hub(WorldStore w, string lawDir) { W = w; _lawDir = lawDir; Game = new Game(w, this); Game.LoadLaw(lawDir); }

        // ------------------------------------------------------------------ clients
        public ClientConn Add(WebSocket ws) { var c = new ClientConn(_nextId++, ws); lock (_cl) _clients.Add(c); return c; }
        public void Remove(ClientConn c) { lock (_cl) _clients.Remove(c); c.Close(); }
        public ClientConn[] Clients() { lock (_cl) return _clients.ToArray(); }
        public void Broadcast(object msg) { string s = JsonSerializer.Serialize(msg, Json); foreach (var c in Clients()) c.SendText(s); }
        public void Enqueue(ClientConn c, JsonElement m) => _cmds.Enqueue((c, m));

        public void OnConnected(ClientConn c)
        {
            c.SendText(JsonSerializer.Serialize(new
            {
                t = "welcome", world = W.Manifest.name, tiles = W.TileCount, centerE = W.Manifest.centerE, centerN = W.Manifest.centerN, attribution = W.Manifest.attribution,
                corps = PoliceCatalog.All.Select(x => new { id = x.Id, name = x.Name, level = x.Level.ToString(), canton = x.CantonId, lang = x.Language }).ToArray(),
                slots = Enumerable.Range(1, 5).Select(i => new { slot = i, info = Game.SlotInfo(i), exists = Game.SlotExists(i) }).ToArray(), playing = Game.Playing
            }, Json));
        }

        // ------------------------------------------------------------------ game loop
        public async Task RunAsync(CancellationToken ct)
        {
            var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100)); var last = DateTime.UtcNow;
            while (await timer.WaitForNextTickAsync(ct))
            {
                var now = DateTime.UtcNow; float dt = (float)Math.Min(0.25, (now - last).TotalSeconds); last = now;
                try { Step(dt); } catch (Exception ex) { Console.WriteLine("tick error: " + ex); }
            }
        }

        public void Step(float dt)
        {
            while (_cmds.TryDequeue(out var cm)) { try { Handle(cm.c, cm.msg); } catch (Exception ex) { Console.WriteLine("command error: " + ex.Message); cm.c.SendText(JsonSerializer.Serialize(new { t = "error", message = ex.Message })); } }
            Game.Tick(dt); Game.TickScenery(); _tick++;
            if (!Game.Playing) return;
            foreach (var c in Clients()) if (c.Role == "game") { StreamTiles(c); SendEntities(c); }
            if (_tick % 5 == 0) { string st = JsonSerializer.Serialize(Snapshotter.State(Game), Json); foreach (var c in Clients()) c.SendText(st); }
        }

        // ------------------------------------------------------------------ commands
        void Handle(ClientConn c, JsonElement m)
        {
            string t = m.GetProperty("t").GetString(); var g = Game;
            switch (t)
            {
                case "hello": c.Role = m.TryGetProperty("role", out var r) ? r.GetString() : "ui"; c.Name = m.TryGetProperty("name", out var nm) ? nm.GetString() : ""; OnConnected(c); if (g.Playing) { c.SendText(JsonSerializer.Serialize(new { t = "started", e = g.PE, n = g.PN, h = g.PH, vE = g.VE, vN = g.VN, vYaw = g.VYaw, corps = g.Corps.Name, seed = g.Seed, resumed = true }, Json)); } break;
                case "new": g.NewGame(m.GetProperty("corps").GetString(), m.TryGetProperty("seed", out var sd) ? sd.GetInt32() : 1, m.TryGetProperty("e", out var se) ? se.GetDouble() : (double?)null, m.TryGetProperty("n", out var sn) ? sn.GetDouble() : (double?)null); ResetClients(); break;
                case "load": if (g.Load(m.GetProperty("slot").GetInt32())) ResetClients(); break;
                case "save": if (g.Playing) g.Save(m.GetProperty("slot").GetInt32()); break;
                case "pause": g.Paused = m.GetProperty("on").GetBoolean(); break;
                case "timescale": g.Clock.TimeScale = (float)Math.Max(1, Math.Min(60, m.GetProperty("v").GetDouble())); break;
                case "pose":
                    g.SetPose(m.GetProperty("e").GetDouble(), m.GetProperty("n").GetDouble(), m.GetProperty("h").GetDouble(), (float)m.GetProperty("yaw").GetDouble(), (float)m.GetProperty("speed").GetDouble(), m.GetProperty("inVehicle").GetBoolean(),
                        m.TryGetProperty("siren", out var si) && si.GetBoolean(), m.TryGetProperty("blue", out var bl) && bl.GetBoolean(), m.GetProperty("vE").GetDouble(), m.GetProperty("vN").GetDouble(), m.GetProperty("vH").GetDouble(), (float)m.GetProperty("vYaw").GetDouble(),
                        m.TryGetProperty("fuel", out var fu) ? (float)fu.GetDouble() : 1f, m.TryGetProperty("damage", out var da) ? (float)da.GetDouble() : 0f); break;
                case "model": SendModel(c, m.GetProperty("key").GetString()); break;
                case "accept": g.Accept(); break; case "decline": g.Decline(); break;
                case "radar": g.Radar(m.GetProperty("on").GetBoolean()); break;
                case "measure": g.Measure(m.GetProperty("vehicleId").GetInt32(), m.TryGetProperty("dist", out var di) ? (float)di.GetDouble() : 50f); break;
                case "stop": g.SignalStop(m.TryGetProperty("vehicleId", out var vi) ? vi.GetInt32() : 0); break;
                case "approach": g.Approach(m.TryGetProperty("vehicleId", out var av) ? av.GetInt32() : 0, m.TryGetProperty("actorId", out var aa) ? aa.GetInt32() : 0); break;
                case "say": g.Say(m.GetProperty("text").GetString()); break;
                case "enc": g.EncounterAction(m.GetProperty("op").GetString(), m.TryGetProperty("fine", out var fi) ? fi.GetInt32() : 0, 0); break;
                case "computer": g.ComputerQuery(m.GetProperty("kind").GetString(), Str(m, "plate"), Str(m, "first"), Str(m, "last"), Str(m, "birth"), m.TryGetProperty("reason", out var re) ? re.GetInt32() : 0); break;
                case "computerLog": c.SendText(JsonSerializer.Serialize(new { t = "computerLog", items = g.ComputerLog() }, Json)); break;
                case "route": g.SetDestination(m.GetProperty("e").GetDouble(), m.GetProperty("n").GetDouble(), Str(m, "name")); break;
                case "routeClear": g.ClearRoute(); break;
                case "search": c.SendText(JsonSerializer.Serialize(new { t = "searchResult", items = Snapshotter.Search(W, Str(m, "text")) }, Json)); break;
                case "poi": c.SendText(JsonSerializer.Serialize(new { t = "pois", items = Snapshotter.PoisNear(W, m.GetProperty("e").GetDouble(), m.GetProperty("n").GetDouble(), m.GetProperty("r").GetDouble()) }, Json)); break;
                case "service": g.CallService(m.GetProperty("which").GetString()); break;
                case "report": g.SubmitReport(m.TryGetProperty("atFault", out var af) ? af.GetInt32() : -1, m.TryGetProperty("injured", out var ij) && ij.GetBoolean(), m.TryGetProperty("amb", out var am) && am.GetBoolean(), m.TryGetProperty("tow", out var to) && to.GetBoolean(), Str(m, "narrative")); break;
                case "pursuit": g.PursuitAction(m.GetProperty("op").GetString()); break;
                case "collision": g.PlayerCollision(m.GetProperty("vehicleId").GetInt32(), (float)m.GetProperty("rel").GetDouble()); break;
                case "career": c.SendText(JsonSerializer.Serialize(Snapshotter.Career(g), Json)); break;
                case "notebook": c.SendText(JsonSerializer.Serialize(new { t = "notebook", cases = g.Cases, log = g.NoteLog.TakeLast(80).Select(x => x.text).ToArray() }, Json)); break;
                case "skip": if (g.Career != null && g.Career.Suspended) { g.Clock.ElapsedGameSeconds = g.Career.SuspendedUntil + 1; g.Career.Suspended = false; g.Career.ComplaintsOpen = 0; g.Career.InvestigationsOpen = Math.Max(0, g.Career.InvestigationsOpen - 1); g.Warn("Die Suspendierung ist abgelaufen. Sie sind wieder im Dienst."); } break;
                case "promote": if (g.Career != null && g.Career.TryPromote()) g.Good("Befördert zum Rang: " + g.Career.RankName(g.Corps)); break;
                case "ping": c.SendText("{\"t\":\"pong\"}"); break;
            }
        }
        static string Str(JsonElement m, string k) => m.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";

        void ResetClients() { foreach (var c in Clients()) { c.Tiles.Clear(); c.Cars.Clear(); c.Peds.Clear(); c.PropsVersion = c.ActorsVersion = -1; c.SendBinary(new Bin().U8(Msg.TileDrop).I32(int.MinValue).I32(0).ToArray()); } }

        // ------------------------------------------------------------------ tiles
        public int DetailRing = 1, MidRing = 2, FarRing = 3; public int MaxTileJobs = Math.Max(2, Environment.ProcessorCount - 1);
        void StreamTiles(ClientConn c) { for (int k = 0; k < MaxTileJobs; k++) if (!StreamOneTile(c)) break; }

        bool Stale(ClientConn c, TileId id) { if (!c.DetailAt.TryGetValue(id, out var d)) return true; double dx = d.e - Game.PE, dn = d.n - Game.PN; return dx * dx + dn * dn > 120 * 120; }

        bool StreamOneTile(ClientConn c)
        {
            var ft = TileGrid.FromLv95(Game.PE, Game.PN);
            // drop what left the keep range
            foreach (var kv in c.Tiles.ToArray()) if (Math.Max(Math.Abs(kv.Key.X - ft.X), Math.Abs(kv.Key.Y - ft.Y)) > FarRing + 1) { c.Tiles.Remove(kv.Key); c.SendBinary(new Bin().U8(Msg.TileDrop).I32(kv.Key.X).I32(kv.Key.Y).ToArray()); }
            if (_tileJobs >= MaxTileJobs) return false;
            TileId best = default; int bestScore = int.MaxValue, bestQ = 0;
            for (int dx = -FarRing; dx <= FarRing; dx++) for (int dy = -FarRing; dy <= FarRing; dy++)
            {
                var id = new TileId(ft.X + dx, ft.Y + dy); if (!W.Has(id) || c.Building.Contains(id)) continue; int d = Math.Max(Math.Abs(dx), Math.Abs(dy)); int q = d <= DetailRing ? 1 : d <= MidRing ? 2 : 3;
                if (c.Tiles.TryGetValue(id, out var have) && have <= q && !(q == 1 && Stale(c, id))) continue; int score = d * 10 + (c.Tiles.ContainsKey(id) ? 5 : 0); if (score < bestScore) { bestScore = score; best = id; bestQ = q; }
            }
            if (bestScore == int.MaxValue) return false;
            c.Building.Add(best); Interlocked.Increment(ref _tileJobs); var id2 = best; int q2 = bestQ; double de = Game.PE, dn2 = Game.PN;
            Task.Run(() =>
            {
                try { var sw = System.Diagnostics.Stopwatch.StartNew(); var payload = TileWire.Build(W, id2, q2, de, dn2); if (Environment.GetEnvironmentVariable("SPS_LOG_TILES") != null) Console.WriteLine($"tile {id2.X}-{id2.Y} q{q2}: {sw.ElapsedMilliseconds} ms, {(payload?.Length ?? 0) / 1024} kB"); if (payload != null) { c.SendBinary(payload); lock (c.Tiles) { c.Tiles[id2] = q2; c.DetailAt[id2] = (de, dn2); } } }
                catch (Exception ex) { Console.WriteLine($"tile {id2.X}-{id2.Y}: {ex.Message}"); }
                finally { lock (c.Building) c.Building.Remove(id2); Interlocked.Decrement(ref _tileJobs); }
            });
            return true;
        }

        // ------------------------------------------------------------------ models
        public void SendModel(ClientConn c, string key)
        {
            if (!c.Models.Add(key)) return; var data = ModelWire.Build(key); if (data == null) { c.Models.Remove(key); return; } c.SendBinary(data);
        }

        // ------------------------------------------------------------------ entities
        void SendEntities(ClientConn c)
        {
            var g = Game; var spawns = new List<object>(); var gone = new List<int>(); var goneP = new List<int>();
            if (g.Traffic != null)
            {
                var now = new HashSet<int>();
                foreach (var v in g.Traffic.Vehicles)
                {
                    now.Add(v.Id); if (c.Cars.Add(v.Id)) { string key = $"car:{Math.Max(0, Math.Min(4, v.BodyKind))}:{((v.ColorIndex % 12) + 12) % 12}"; SendModel(c, key); spawns.Add(new { t = "spawn", k = "car", id = v.Id, model = key, plate = v.Plate, body = v.BodyKind, color = Game.VehicleColour(v.ColorIndex) }); }
                }
                foreach (int id in c.Cars.ToArray()) if (!now.Contains(id)) { c.Cars.Remove(id); gone.Add(id); }
            }
            if (g.Peds != null)
            {
                var now = new HashSet<int>();
                foreach (var p in g.Peds.Peds)
                {
                    now.Add(p.Id); if (c.Peds.Add(p.Id)) { string key = ModelWire.PedKey(p); SendModel(c, key); spawns.Add(new { t = "spawn", k = "ped", id = p.Id, model = key }); }
                }
                foreach (int id in c.Peds.ToArray()) if (!now.Contains(id)) { c.Peds.Remove(id); goneP.Add(id); }
            }
            foreach (var s in spawns) c.SendText(JsonSerializer.Serialize(s, Json));
            if (gone.Count > 0) c.SendText(JsonSerializer.Serialize(new { t = "despawn", k = "car", ids = gone }, Json)); if (goneP.Count > 0) c.SendText(JsonSerializer.Serialize(new { t = "despawn", k = "ped", ids = goneP }, Json));
            // scene props and actors are small and change rarely: resend the full list when their version changes
            int pv = Snapshotter.PropsVersion(g); if (pv != c.PropsVersion) { c.PropsVersion = pv; foreach (var p in g.Props.Where(p => p.Kind == "car" || p.Kind == "ems" || p.Kind == "tow")) SendModel(c, Snapshotter.PropModel(p)); c.SendText(JsonSerializer.Serialize(new { t = "props", items = g.Props.Select(p => new { id = p.Id, kind = p.Kind, model = Snapshotter.PropModel(p), plate = p.Plate, hazard = p.Hazard, blue = p.Blue, e = p.E, n = p.N, h = p.H, yaw = p.Yaw }).ToArray() }, Json)); }
            int av = Snapshotter.ActorsVersion(g); if (av != c.ActorsVersion) { c.ActorsVersion = av; var items = g.Actors.Select(a => { string key = ModelWire.ActorKey(a); SendModel(c, key); return new { id = a.Id, label = a.Label, model = key, child = a.Child }; }).ToArray(); c.SendText(JsonSerializer.Serialize(new { t = "actors", items }, Json)); }
            c.SendBinary(Snapshotter.Snapshot(g));
        }
    }
}
