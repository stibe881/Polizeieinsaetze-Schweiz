using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Geometry;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Game;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Legal;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Npc;
using SwissPoliceSim.Core.Police;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Traffic;
using SwissServer.Net;

namespace SwissServer.Sim
{
    /// <summary>One person the officer can talk to at an incident scene (the server-side counterpart of the old Unity NpcActor).</summary>
    public sealed class SActor
    {
        public int Id; public Citizen Person; public ScenarioRole Role; public Encounter Enc; public string Label = "";
        public double E, N; public float H, Yaw; public byte Pose; public bool Fleeing, Cuffed, Arrested, Escaped, Child; public float DirE, DirN; public float FleeTime;
        public bool Active => !Escaped;
    }

    /// <summary>Static vehicle or prop at a scene (accident cars, ambulance, tow truck, cargo, roadblock).</summary>
    public sealed class SProp
    {
        public int Id; public string Kind = "car"; public int Body; public byte[] Colour = { 200, 200, 200 }; public string Plate = ""; public bool Police, Hazard, Blue; public double E, N; public float H, Yaw;
    }

    public enum PoseKind : byte { Idle, Walk, Run, Talk, Point, Phone, Search, Cuffed, Fallen }

    /// <summary>The running game: all state and rules. Single-threaded – every command and tick runs on the game thread (see <see cref="Hub"/>).</summary>
    public sealed partial class Game
    {
        public readonly WorldStore W; public readonly Hub Hub;
        public GameClock Clock = new GameClock(); public PoliceCorps Corps; public CareerState Career; public readonly PoliceComputer Computer = new PoliceComputer();
        public DispatchCenter Dispatch; public LawLibrary Laws; public ObvCatalog Obv; public int Seed = 1; public bool Playing, Paused;
        public readonly List<GameSave.SavedCase> Cases = new List<GameSave.SavedCase>(); public readonly List<string> ConsequenceLog = new List<string>(); public readonly List<string> Flags = new List<string>();
        public JurisdictionStatus Jurisdiction = new JurisdictionStatus { Kind = JurisdictionKind.Unknown }; public string PlaceName = "", StreetName = ""; public int SpeedLimitKmh; public bool UrbanHere;
        // player (position is owned by the client; the server only observes it)
        public double PE, PN, PH; public float PYaw, PSpeedKmh, PFuel = 1f, PDamage; public bool PInVehicle = true, PSiren, PBlue; public double VE, VN, VH; public float VYaw;
        public Lv95 PLv95 => new Lv95(PE, PN, PH);
        // traffic
        public TrafficNetwork Net; public TrafficSimulation Traffic; public PedestrianSim Peds; Task<TrafficNetwork> _netTask; double _netE, _netN; bool _hasNet; float _netCheck; int _netTiles; TrafficVehicle _playerVeh;
        readonly Dictionary<int, Citizen> _vehicleCitizens = new Dictionary<int, Citizen>();
        public readonly ConcurrentQueue<(string kind, string text)> Outbox = new ConcurrentQueue<(string, string)>();
        public readonly List<(string kind, string text, double t)> NoteLog = new List<(string, string, double)>();

        public Game(WorldStore w, Hub hub) { W = w; Hub = hub; Computer.Resolver = ResolvePlate; }

        public DateTime Now => Clock.Now; public float Hour => Clock.HourOfDay;

        // ------------------------------------------------------------------ notes
        public void Note(string kind, string text, float seconds = 6f)
        {
            NoteLog.Add((kind, text, Clock.ElapsedGameSeconds)); if (NoteLog.Count > 400) NoteLog.RemoveAt(0);
            Hub.Broadcast(new { t = "note", kind, text, seconds });
        }
        public void Info(string t) => Note("info", t); public void Good(string t) => Note("good", t); public void Warn(string t) => Note("warn", t, 8f); public void Bad(string t) => Note("bad", t, 10f); public void Radio(string t) => Note("radio", t, 9f);

        // ------------------------------------------------------------------ law data
        public void LoadLaw(string lawDir)
        {
            Laws = LawLibrary.LoadDirectory(lawDir); string p = Path.Combine(lawDir, "SR_314.11.txt"); Obv = File.Exists(p) ? ObvCatalog.Parse(File.ReadAllText(p)) : new ObvCatalog();
        }

        // ------------------------------------------------------------------ new game / save / load
        public void NewGame(string corpsId, int seed, double? startE = null, double? startN = null)
        {
            Corps = PoliceCatalog.ById(corpsId) ?? PoliceCatalog.ById("KAPO_BE") ?? PoliceCatalog.All[0]; Seed = seed; Career = new CareerState(seed) { CorpsId = Corps.Id }; Cases.Clear(); ConsequenceLog.Clear(); Flags.Clear();
            Clock = new GameClock { Start = new DateTime(2026, 10, 12, 9, 0, 0), TimeScale = 12f };
            ResetWorldState();
            double e, n; if (startE.HasValue && startN.HasValue) { e = startE.Value; n = startN.Value; } else FindStart(Corps, out e, out n);
            PE = VE = e; PN = VN = n; VYaw = 0;
            // Der Polizeiposten (POI) liegt im Gebaeudegrundriss: Auto auf die naechste befahrbare Strasse, Beamter auf den Gehsteig daneben (sonst startet man im Hof ohne Ausgang)
            if (!(startE.HasValue && startN.HasValue)) SnapStartToRoad(e, n);
            else VE = e + 6;
            PH = W.Height(PE, PN); VH = W.Height(VE, VN); if (double.IsNaN(PH)) PH = 540; if (double.IsNaN(VH)) VH = PH; PInVehicle = false; PFuel = 1; PDamage = 0;
            Playing = true; Paused = false; _nextGen = Clock.ElapsedGameSeconds + 150 + _rnd.Next(150);
            Hub.Broadcast(new { t = "started", e = PE, n = PN, h = PH, vE = VE, vN = VN, vYaw = VYaw, corps = Corps.Name, seed });
            Radio($"Zentrale: {CallSign} – Dienstbeginn. Viel Erfolg.");
        }

        public string CallSign => Dispatch != null ? Dispatch.CallSign : "Patrouille 12";

        /// <summary>Setzt Fahrzeug und Spieler in der Naehe von (e, n) auf eine befahrbare Strasse: Auto auf der Strassenachse (in Fahrtrichtung), Spieler 2.5 m seitlich Richtung Gebaeude.</summary>
        void SnapStartToRoad(double e, double n)
        {
            VE = e + 6; VN = n; VYaw = 0;
            // Bevorzugt eine normale Strasse (nicht Betriebs-/Waldweg, kein Tunnel/Bruecke) mit Abstand zu Gleisen; sonst die naechste befahrbare
            bool NearRail(double pe, double pn, double r)
            {
                var c0 = TileGrid.FromLv95(pe, pn);
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) { var t = W.Content(new TileId(c0.X + dx, c0.Y + dy)); if (t?.Rails == null) continue; foreach (var rl in t.Rails) for (int k = 0; k + 1 < rl.E.Length; k++) if (PolygonUtil.DistToSegment(new Vec2d(pe, pn), new Vec2d(rl.E[k], rl.N[k]), new Vec2d(rl.E[k + 1], rl.N[k + 1])) < r) return true; }
                return false;
            }
            if (!W.NearestRoad(e, n, 300, out var ri, true, (rw, qe, qn) => rw.Class <= RoadClass.LivingStreet && !rw.Tunnel && !rw.Bridge && !NearRail(rw.E[0], rw.N[0], 18) ) || ri.Way == null)
                if (!W.NearestRoad(e, n, 150, out ri) || ri.Way == null) return;
            var w = ri.Way; int i = Math.Max(0, Math.Min(ri.Segment, w.PointCount - 2));
            double ax = w.E[i], ay = w.N[i], bx = w.E[i + 1], by = w.N[i + 1], dx = bx - ax, dy = by - ay, len = Math.Sqrt(dx * dx + dy * dy); if (len < 0.5) return;
            double t = Math.Max(0, Math.Min(1, ((e - ax) * dx + (n - ay) * dy) / (len * len))), fx = ax + t * dx, fy = ay + t * dy, ux = dx / len, uy = dy / len;
            VE = fx; VN = fy; VYaw = (float)(Math.Atan2(ux, uy) * 180.0 / Math.PI); if (VYaw < 0) VYaw += 360f;   // Peilung von Nord im Uhrzeigersinn
            double sx = e - fx, sy = n - fy, sl = Math.Sqrt(sx * sx + sy * sy); if (sl < 0.5) { sx = -uy; sy = ux; sl = 1; }   // zur Seite des Gebaeudes (Gehsteig)
            double off = Math.Min(2.5, sl); PE = fx + sx / sl * off - ux * 3.0; PN = fy + sy / sl * off - uy * 3.0;
        }

        void FindStart(PoliceCorps corps, out double e, out double n)
        {
            e = W.Manifest.centerE > 0 ? W.Manifest.centerE : 2600000; n = W.Manifest.centerN > 0 ? W.Manifest.centerN : 1200000;
            if (W.Pois == null) return; double best = double.MaxValue; PoiFeature pick = null;
            foreach (var p in W.Pois.Within(e, n, 20000, PoiKind.Police)) { var st = W.Admin != null ? JurisdictionManager.Evaluate(W.Admin, corps, p.E, p.N) : null; double d = (p.E - e) * (p.E - e) + (p.N - n) * (p.N - n) - (st != null && st.InOwnTerritory ? 1e9 : 0); if (d < best) { best = d; pick = p; } }
            if (pick != null) { e = pick.E; n = pick.N; }
        }

        void ResetWorldState()
        {
            Traffic = null; Net = null; Peds = null; _hasNet = false; _netTask = null; _vehicleCitizens.Clear(); Dispatch = null; ClearScene(); Offered = null; Active = null; Encounter = null; EncActor = null; EncVehicle = null; StoppedVehicle = null; RadarActive = false; LastMeasure = null;
            Pursuit = new PursuitManager(); PursuitTarget = null; Nav = new Nav(); _rnd = new Random(Seed * 31 + 5);
        }

        public static string SaveDir = Path.Combine(AppContext.BaseDirectory, "Saves");
        public static string SlotPath(int slot) => Path.Combine(SaveDir, $"slot{slot}.json");
        public static bool SlotExists(int slot) => File.Exists(SlotPath(slot));
        public static string SlotInfo(int slot)
        {
            try { if (!SlotExists(slot)) return "leer"; var g = GameSave.LoadFromFile(SlotPath(slot)); var t = DateTime.Parse(g.StartIso).AddSeconds(g.GameSeconds); return $"{PoliceCatalog.ById(g.CorpsId)?.Name ?? g.CorpsId} · Rang {g.RankIndex + 1} · {t:dd.MM. HH:mm}"; } catch (Exception) { return "beschädigt"; }
        }

        public GameSave BuildSave()
        {
            var g = new GameSave { CorpsId = Corps.Id, RankIndex = Career.RankIndex, Xp = Career.Xp, GameSeconds = Clock.ElapsedGameSeconds, TimeScale = Clock.TimeScale, StartIso = Clock.Start.ToString("s"), Seed = Seed, Region = PlaceName };
            g.PlayerE = PE; g.PlayerN = PN; g.PlayerH = PH; g.PlayerYaw = PYaw; g.InVehicle = PInVehicle; g.VehicleE = VE; g.VehicleN = VN; g.VehicleH = VH; g.VehicleYaw = VYaw; g.Fuel = PFuel; g.VehicleDamage = PDamage;
            g.Legality = Career.Legality; g.Professionalism = Career.Professionalism; g.DeEscalation = Career.DeEscalation; g.Reporting = Career.Reporting; g.Community = Career.Community;
            g.ComplaintsOpen = Career.ComplaintsOpen; g.ComplaintsTotal = Career.ComplaintsTotal; g.InvestigationsOpen = Career.InvestigationsOpen; g.DisciplinaryCount = Career.DisciplinaryCount; g.FinesIssued = Career.FinesIssued; g.ArrestsMade = Career.ArrestsMade; g.Suspended = Career.Suspended; g.Dismissed = Career.Dismissed;
            foreach (var c in Cases) g.Cases.Add(c); foreach (var s in ConsequenceLog) g.Consequences.Add(s); foreach (var f in Flags) g.Flags.Add(f);
            if (Active != null && Active.State != IncidentState.Resolved) g.Incidents.Add(new GameSave.SavedIncident { Id = Active.Id, Type = (int)Active.Type, State = (int)Active.State, Urgency = Active.Urgency, E = Active.E, N = Active.N, Address = Active.Address, Municipality = Active.Municipality, Canton = Active.Canton, RadioText = Active.RadioText, AmbulanceCalled = Active.AmbulanceCalled, FireCalled = Active.FireCalled });
            return g;
        }

        public void Save(int slot)
        {
            try { Directory.CreateDirectory(SaveDir); BuildSave().SaveToFile(SlotPath(slot)); Good($"Spielstand {slot} gespeichert."); } catch (Exception ex) { Bad("Speichern fehlgeschlagen: " + ex.Message); }
        }

        public bool Load(int slot)
        {
            try
            {
                var g = GameSave.LoadFromFile(SlotPath(slot)); Corps = PoliceCatalog.ById(g.CorpsId) ?? PoliceCatalog.ById("KAPO_BE"); Seed = g.Seed;
                Clock = new GameClock { Start = DateTime.Parse(g.StartIso), ElapsedGameSeconds = g.GameSeconds, TimeScale = g.TimeScale };
                Career = new CareerState(g.Seed) { CorpsId = g.CorpsId, RankIndex = g.RankIndex, Xp = g.Xp, Legality = g.Legality, Professionalism = g.Professionalism, DeEscalation = g.DeEscalation, Reporting = g.Reporting, Community = g.Community, ComplaintsOpen = g.ComplaintsOpen, ComplaintsTotal = g.ComplaintsTotal, InvestigationsOpen = g.InvestigationsOpen, DisciplinaryCount = g.DisciplinaryCount, FinesIssued = g.FinesIssued, ArrestsMade = g.ArrestsMade, Suspended = g.Suspended, Dismissed = g.Dismissed };
                Cases.Clear(); Cases.AddRange(g.Cases); ConsequenceLog.Clear(); ConsequenceLog.AddRange(g.Consequences); Flags.Clear(); Flags.AddRange(g.Flags);
                ResetWorldState(); PE = g.PlayerE; PN = g.PlayerN; PH = g.PlayerH; PYaw = g.PlayerYaw; PInVehicle = g.InVehicle; VE = g.VehicleE; VN = g.VehicleN; VH = g.VehicleH; VYaw = g.VehicleYaw; PFuel = g.Fuel; PDamage = g.VehicleDamage;
                Playing = true; Paused = false; _nextGen = Clock.ElapsedGameSeconds + 200;
                if (g.Incidents.Count > 0) { var s = g.Incidents[0]; Active = new Incident { Id = s.Id, Type = (IncidentType)s.Type, E = s.E, N = s.N, Address = s.Address, Municipality = s.Municipality, Canton = s.Canton, Urgency = s.Urgency, RadioText = s.RadioText, State = IncidentState.Accepted }; SetDestination(Active.E, Active.N, Active.Address + ", " + Active.Municipality); }
                Hub.Broadcast(new { t = "started", e = PE, n = PN, h = PH, vE = VE, vN = VN, vYaw = VYaw, corps = Corps.Name, seed = Seed, loaded = true, inVehicle = PInVehicle, pYaw = PYaw });
                Good($"Spielstand {slot} geladen."); return true;
            }
            catch (Exception ex) { Bad("Laden fehlgeschlagen: " + ex.Message); return false; }
        }

        // ------------------------------------------------------------------ judging
        public List<Consequence> Judge(LegalAssessment a, float npcAnger = 0.2f, bool documented = true)
        {
            if (a == null || Career == null) return new List<Consequence>();
            var list = Career.ApplyAssessment(a, npcAnger, documented, (float)Clock.ElapsedGameSeconds);
            if (!a.Lawful) Warn("Rechtswidrig: " + (a.Reasons.Count > 0 ? a.Reasons[0] : a.Headline));
            foreach (var c in list) { ConsequenceLog.Add(Clock.Now.ToString("dd.MM. HH:mm") + "  " + c.Text); Bad(c.Text); }
            return list;
        }

        public void AddCase(string kind, string summary, string plate, string person, int fine, bool correct)
        {
            Cases.Add(new GameSave.SavedCase { Kind = kind, Summary = summary, Plate = plate ?? "", Person = person ?? "", FineChf = fine, Correct = correct, Time = Clock.ElapsedGameSeconds }); if (Cases.Count > 200) Cases.RemoveAt(0);
        }

        // ------------------------------------------------------------------ people behind vehicles
        public Citizen CitizenForVehicle(TrafficVehicle v)
        {
            if (_vehicleCitizens.TryGetValue(v.Id, out var c)) return c;
            string plate = string.IsNullOrEmpty(v.Plate) ? "BE 000000" : v.Plate; string canton = plate.Length >= 2 ? plate.Substring(0, 2) : "BE"; var d = v.Driver;
            c = CitizenFactory.Create(v.Id * 7919 + Seed, canton, PlaceName ?? "", plate, Math.Max(0, Math.Min(4, v.BodyKind)), v.ColorIndex); c.Honesty = Math.Max(0.1f, Math.Min(1f, d.Honesty));
            if (!string.IsNullOrEmpty(d.FirstName)) c.FirstName = d.FirstName; if (!string.IsNullOrEmpty(d.Name)) c.LastName = d.Name.Contains(" ") ? d.Name.Substring(d.Name.LastIndexOf(' ') + 1) : d.Name; if (!string.IsNullOrEmpty(d.Birthdate)) c.Birthdate = d.Birthdate;
            if (!string.IsNullOrEmpty(d.Address)) c.Address = d.Address; if (!string.IsNullOrEmpty(d.Occupation)) c.Occupation = d.Occupation; if (d.Age > 0) c.Age = d.Age;
            CitizenFactory.ApplyDriverTruth(c, d.Impaired, d.BloodAlcohol, d.NoLicense, d.LicenseSuspended, d.UninsuredOrExpired, d.VehicleDefect, d.Wanted, v.Id + Seed);
            _vehicleCitizens[v.Id] = c; Computer.Register(c); return c;
        }

        Citizen ResolvePlate(string plate)
        {
            if (Traffic == null) return null; string np = plate.Replace(" ", "").ToUpperInvariant();
            foreach (var v in Traffic.Vehicles) if (!string.IsNullOrEmpty(v.Plate) && v.Plate.Replace(" ", "").ToUpperInvariant() == np) return CitizenForVehicle(v);
            return null;
        }

        // ------------------------------------------------------------------ pose from the client
        public void SetPose(double e, double n, double h, float yaw, float speedKmh, bool inVehicle, bool siren, bool blue, double vE, double vN, double vH, float vYaw, float fuel, float damage)
        {
            PE = e; PN = n; PH = h; PYaw = yaw; PSpeedKmh = speedKmh; PInVehicle = inVehicle; PSiren = siren; PBlue = blue; VE = vE; VN = vN; VH = vH; VYaw = vYaw; PFuel = fuel; PDamage = damage;
        }

        // ------------------------------------------------------------------ tick
        float _jurTimer, _roadTimer, _stateTimer; Random _rnd = new Random(1);

        public void Tick(float dt)
        {
            if (!Playing || Paused || Career == null) return;
            Clock.Advance(dt); Computer.Now = Clock.ElapsedGameSeconds;
            _jurTimer -= dt; if (_jurTimer <= 0) { _jurTimer = 1f; UpdateJurisdiction(); }
            _roadTimer -= dt; if (_roadTimer <= 0) { _roadTimer = 0.35f; UpdateRoad(); }
            TickTraffic(dt); TickIncidents(dt); TickEncounterAndEnforcement(dt); TickNav(dt); TickPursuit(dt);
            if (Career.Suspended && Clock.ElapsedGameSeconds >= Career.SuspendedUntil) { Career.Suspended = false; Good("Die Suspendierung ist beendet."); }
        }

        void UpdateJurisdiction()
        {
            if (W.Admin == null || Corps == null) return;
            Jurisdiction = JurisdictionManager.Evaluate(W.Admin, Corps, PE, PN); PlaceName = Jurisdiction.MunicipalityName;
        }

        void UpdateRoad()
        {
            if (W.NearestRoad(PE, PN, 18, out var ri)) { SpeedLimitKmh = ri.SpeedLimitKmh; StreetName = ri.Name; UrbanHere = ri.Urban; } else { SpeedLimitKmh = 0; StreetName = ""; }
        }

        // ------------------------------------------------------------------ traffic & pedestrians
        void TickTraffic(float dt)
        {
            var c = TileGrid.FromLv95(PE, PN);
            if (_netTask != null && _netTask.IsCompleted)
            {
                if (_netTask.Status == TaskStatus.RanToCompletion && _netTask.Result != null)
                {
                    Net = _netTask.Result;
                    if (Traffic == null) { Traffic = new TrafficSimulation(Net, new TrafficParams { MaxVehicles = 140, Seed = 11 }); Traffic.Crash += OnTrafficCrash; _playerVeh = new TrafficVehicle { Id = -1, Length = 4.7f, Width = 1.9f, External = true }; }
                    else Traffic.Rebase(Net);
                    Dispatch = new DispatchCenter(Net, W.Admin, Seed); Peds = new PedestrianSim(_netWays, _netPts, Seed + 5); _hasNet = true;
                }
                _netTask = null;
            }
            _netCheck -= dt;
            if (_netTask == null && _netCheck <= 0)
            {
                _netCheck = 1.5f; double moved = Math.Sqrt((PE - _netE) * (PE - _netE) + (PN - _netN) * (PN - _netN));
                bool near = W.Content(c) != null;
                if (near && (!_hasNet || moved > 1100))
                {
                    var ways = new List<RoadWay>(); var pts = new List<PointFeature>();
                    for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++) { var t = W.Content(new TileId(c.X + dx, c.Y + dy)); if (t != null) { ways.AddRange(t.Roads); pts.AddRange(t.Points); } }
                    if (ways.Count > 0) { _netE = PE; _netN = PN; _netWays = ways; _netPts = pts; _netTask = Task.Run(() => TrafficNetwork.Build(ways, pts)); }
                }
            }
            if (Traffic == null) return;
            var obs = new List<(double e, double n)> { (PE, PN) };
            Traffic.Maintain(obs, Hour);
            double cx = PInVehicle ? VE : PE, cn = PInVehicle ? VN : PN; float yawRad = (PInVehicle ? VYaw : PYaw) * (float)Math.PI / 180f;
            Traffic.UpdateExternal(_playerVeh, cx, cn, (float)Math.Sin(yawRad), (float)Math.Cos(yawRad), PInVehicle ? PSpeedKmh / 3.6f : 0f);
            Traffic.SirenActive = PInVehicle && PSiren; Traffic.SirenE = cx; Traffic.SirenN = cn; Traffic.SirenDirE = (float)Math.Sin(yawRad); Traffic.SirenDirN = (float)Math.Cos(yawRad);
            Traffic.Step(dt);
            if (Peds != null) { Peds.Maintain(obs, Hour); Peds.Step(dt); }
        }
        List<RoadWay> _netWays; List<PointFeature> _netPts;

        void OnTrafficCrash(TrafficVehicle a, TrafficVehicle b) { }

        /// <summary>The client reports a collision between the player's car and a traffic car (physics runs in the client).</summary>
        public void PlayerCollision(int vehicleId, float relSpeedMs)
        {
            var v = Traffic?.Find(vehicleId); if (v == null) return; if (relSpeedMs > 3f) { v.Mode = DriverMode.Crashed; v.V = 0; Warn("Kollision mit einem Fahrzeug (" + v.Plate + ")."); }
        }

        /// <summary>The client reports that the player's car hit a pedestrian (or a scene person). The person stays down; the officer answers for it.</summary>
        public void PedHit(int id, float relSpeedMs, bool actor)
        {
            if (relSpeedMs < 1.5f) return;
            if (!actor && Peds != null) { var p = Peds.Peds.Find(x => x.Id == id); if (p != null) { p.State = PedState.Standing; p.Speed = 0; p.WaitUntil = (float)(Peds.Time + 180); } }
            if (relSpeedMs > 4f) { Bad("Eine Person wurde angefahren. Rettungsdienst aufbieten und Unfallstelle sichern."); Judge(new LegalAssessment { Lawful = false, Severity = 3, Headline = "Fussgänger angefahren" }, 0.3f); }
            else Warn("Kontakt mit einer Person. Prüfen Sie, ob sie verletzt ist.");
        }

        // ------------------------------------------------------------------ helpers
        public float HeightAt(double e, double n) { float h = W.Height(e, n); return float.IsNaN(h) ? (float)PH : h; }
    }

    public sealed class Nav
    {
        public RoutePath Route; public List<Instruction> Instructions = new List<Instruction>(); public string DestinationName = ""; public double DestE, DestN; public bool Computing; public string Error;
        public double AlongM, RemainingM, TotalM, DistToNext; public Instruction Next; public float EtaSeconds; public double OffRouteM; public double[] Cum; public int Idx; public Task<RoutePath> Task; public float OffTime, NextReroute; public double AvgSpeed = 13; public bool ArrivedFired;
    }
}
