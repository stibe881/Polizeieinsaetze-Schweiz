using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SwissPoliceSim.Core.Game;
using SwissPoliceSim.Core.Legal;
using SwissPoliceSim.Core.Npc;
using SwissPoliceSim.Core.Traffic;

namespace SwissServer.Sim
{
    /// <summary>Dispatch flow and incident scenes (ported from the former Unity IncidentController).</summary>
    public sealed partial class Game
    {
        public Incident Offered, Active; public float OfferSecondsLeft; public readonly List<SActor> Actors = new List<SActor>(); public readonly List<SProp> Props = new List<SProp>(); public bool SceneBuilt;
        double _nextGen; float _arrivalTimer; int _idCounter = 2_000_000; public bool EmsEnRoute, TowEnRoute, MissingFound; double _emsAt, _towAt, _wall; SActor _missingChild; readonly List<TrafficVehicle> _dummies = new List<TrafficVehicle>();
        double _roadDE, _roadDN = 1; public int WantedVehicleId = -1;

        static void Log(string s) { if (Environment.GetEnvironmentVariable("SPS_LOG") != null) Console.WriteLine("[game] " + s); }
        public int NextId() => ++_idCounter;

        void TickIncidents(float dt)
        {
            _wall += dt; if (Dispatch == null) return;
            if (Offered != null) { OfferSecondsLeft -= dt; if (OfferSecondsLeft <= 0) { Log("offer expired"); Radio("Zentrale: Einsatz anderweitig vergeben."); Offered.State = IncidentState.Declined; Offered = null; _nextGen = Clock.ElapsedGameSeconds + 600; } }
            else if (Active == null && Clock.ElapsedGameSeconds >= _nextGen && !Pursuit.Active && !InEncounter) Generate();
            if (Active == null) return;
            if (Active.State == IncidentState.Accepted || Active.State == IncidentState.EnRoute) { Active.State = IncidentState.EnRoute; CheckArrival(dt); }
            else if (Active.State == IncidentState.OnScene) UpdateScene();
            UpdateServices();
        }

        public Incident Generate()
        {
            var inc = Dispatch.Generate(PE, PN, (float)Clock.ElapsedGameSeconds, Hour); if (inc == null) { _nextGen = Clock.ElapsedGameSeconds + 120; return null; }
            Offered = inc; OfferSecondsLeft = 70f; Radio(inc.RadioText); Hub.Broadcast(new { t = "beep" }); return inc;
        }

        public void Accept()
        {
            Log("accept, offered=" + (Offered != null));
            if (Offered == null) return; Active = Offered; Offered = null; Active.State = IncidentState.Accepted; Active.AcceptedAt = (float)Clock.ElapsedGameSeconds; _arrivalTimer = 0;
            Radio($"{CallSign}: Verstanden, wir übernehmen. {Active.Address}, {Active.Municipality}."); SetDestination(Active.E, Active.N, Active.Address + ", " + Active.Municipality);
            if (Active.Urgency >= 3) Info("Dringlicher Einsatz: Blaulicht und Wechselklanghorn sind zulässig (Art. 100 Ziff. 4 SVG, sorgfältig fahren).");
        }

        /// <summary>Angenommenen Einsatz abbrechen (Zentrale vergibt ihn weiter). Nicht waehrend einer laufenden Kontrolle.</summary>
        public void Abort()
        {
            if (Active == null) return;
            if (InEncounter) { Warn("Zuerst die laufende Kontrolle beenden, dann den Einsatz abbrechen."); return; }
            Active.State = IncidentState.Declined; Active = null; ClearRoute();
            Radio($"{CallSign}: Wir brechen den Einsatz ab."); Radio("Zentrale: Verstanden, wir vergeben den Einsatz weiter.");
            _nextGen = Clock.ElapsedGameSeconds + 300; _clearAt = _wall + 20;
        }

        public void Decline() { if (Offered == null) return; Radio("Zentrale: Verstanden, wir vergeben den Einsatz weiter."); Offered.State = IncidentState.Declined; Offered = null; _nextGen = Clock.ElapsedGameSeconds + 400; }

        void CheckArrival(float dt)
        {
            double cx = PInVehicle ? VE : PE, cn = PInVehicle ? VN : PN; double d = Math.Sqrt((cx - Active.E) * (cx - Active.E) + (cn - Active.N) * (cn - Active.N)); float speed = PInVehicle ? PSpeedKmh : 0;
            if (d < (PInVehicle ? 70 : 40) && speed < 25) { _arrivalTimer += dt; if (_arrivalTimer > 0.5f || !PInVehicle) Arrive(); } else _arrivalTimer = 0;
        }

        void Arrive()
        {
            Log("arrive"); Active.State = IncidentState.OnScene; Active.ArrivedAt = (float)Clock.ElapsedGameSeconds; ClearRoute(); BuildScene(); Radio($"{CallSign}: Vor Ort.");
            float resp = Active.ArrivedAt - Active.AcceptedAt; if (resp < Active.DeadlineSeconds) Career.Xp += 10 * Active.Urgency;
        }

        // ------------------------------------------------------------------ scene
        (double dE, double dN) Side() => (-_roadDN, _roadDE);
        string Livery(Citizen c) => c.Vehicle.Plate;

        SProp AddCar(Citizen c, double e, double n, float yaw, int body = 2) { var p = new SProp { Id = NextId(), Kind = "car", Body = body, Colour = VehicleColour(c.Seed % 12), Plate = c.Vehicle.Plate ?? "BE 123456", E = e, N = n, H = HeightAt(e, n), Yaw = yaw, Hazard = true }; Props.Add(p); return p; }

        public static readonly byte[][] Palette =
        {
            new byte[] { 232, 232, 232 }, new byte[] { 24, 24, 26 }, new byte[] { 170, 172, 176 }, new byte[] { 88, 90, 96 }, new byte[] { 36, 62, 120 }, new byte[] { 150, 30, 30 },
            new byte[] { 44, 92, 60 }, new byte[] { 90, 64, 44 }, new byte[] { 205, 190, 160 }, new byte[] { 210, 120, 40 }, new byte[] { 220, 190, 50 }, new byte[] { 90, 20, 40 }
        };
        public static byte[] VehicleColour(int idx) => Palette[((idx % Palette.Length) + Palette.Length) % Palette.Length];

        SActor PlaceActor(ScenarioRole role, double e, double n, float yaw, string label, PoseKind pose, bool child = false)
        {
            var a = new SActor { Id = NextId(), Person = role.Person, Role = role, Label = label, E = e, N = n, H = HeightAt(e, n), Yaw = yaw, Pose = (byte)pose, Child = child };
            var sit = new NpcSituation { TrueViolation = ViolationType.None, NpcGuilty = role.Guilty, IsDriver = false, InVehicle = false, Place = Active.Municipality, Statement = role.Statement, TimeOfDayNight = Hour < 6 || Hour > 21 };
            a.Enc = new Encounter(EncounterKind.IncidentParty, role.Person, sit, Corps, Obv, role.Person.Seed); a.Enc.Ctx.SuspectIsDriver = false; a.Enc.Ctx.PersonDetained = false; a.Enc.Ctx.Suspicion = role.Guilty ? Suspicion.Concrete : Suspicion.None; if (role.Injured) a.Enc.Ctx.Suspicion = Suspicion.None;
            Actors.Add(a); return a;
        }

        void BuildScene()
        {
            ClearScene(); var inc = Active; SceneBuilt = true; double h = HeightAt(inc.E, inc.N); float halfRoad = 3.2f; _roadDE = 0; _roadDN = 1;
            if (W.NearestRoad(inc.E, inc.N, 40, out var ri)) { var w = ri.Way; double dx = w.E[ri.Segment + 1] - w.E[ri.Segment], dz = w.N[ri.Segment + 1] - w.N[ri.Segment]; double l = Math.Sqrt(dx * dx + dz * dz); if (l > 0.1) { _roadDE = dx / l; _roadDN = dz / l; } halfRoad = w.CarriagewayWidth() * 0.5f; }
            var (sE, sN) = Side(); float yaw = (float)(Math.Atan2(_roadDE, _roadDN) * 180 / Math.PI); var sc = inc.Scenario;
            double At(double along, double side, out double nn) { nn = inc.N + _roadDN * along + sN * side; return inc.E + _roadDE * along + sE * side; }
            switch (inc.Type)
            {
                case IncidentType.TrafficAccident: case IncidentType.TrafficAccidentInjured:
                {
                    var c0 = sc.Roles[0].Person; var c1 = sc.Roles[Math.Min(1, sc.Roles.Count - 1)].Person; double e, n;
                    e = At(-2.4, halfRoad * 0.5 * -1, out n); AddCar(c0, e, n, yaw + 8f); e = At(3.1, halfRoad * 0.3 * -1, out n); AddCar(c1, e, n, yaw - 24f);
                    for (int i = 0; i < sc.Roles.Count && i < 2; i++) { e = At(i * 2.2 - 1, halfRoad + 1.7, out n); PlaceActor(sc.Roles[i], e, n, yaw + (i == 0 ? 180 : 160), sc.Roles[i].Role, sc.Roles[i].Injured ? PoseKind.Idle : PoseKind.Talk); }
                    for (int i = 0; i < 7; i++) { e = At((_rnd.NextDouble() - 0.5) * 6, (_rnd.NextDouble() - 0.5) * 6, out n); Props.Add(new SProp { Id = NextId(), Kind = "debris", E = e, N = n, H = HeightAt(e, n), Yaw = (float)(_rnd.NextDouble() * 360) }); }
                    break;
                }
                case IncidentType.Burglary: { double e, n; e = At(0, halfRoad + 3, out n); PlaceActor(sc.Roles[0], e, n, yaw + 90, "Geschädigter", PoseKind.Talk); e = At(55, halfRoad + 3, out n); PlaceActor(sc.Roles[1], e, n, yaw, "Mutmasslicher Täter", PoseKind.Idle); break; }
                case IncidentType.Shoplifting: { double e, n; e = At(0, halfRoad + 2.5, out n); PlaceActor(sc.Roles[0], e, n, yaw + 180, "Filialleiter", PoseKind.Talk); e = At(1.4, halfRoad + 2.5, out n); PlaceActor(sc.Roles[1], e, n, yaw, "Verdächtige Person", PoseKind.Idle); break; }
                case IncidentType.Dispute: { double e, n; e = At(0, halfRoad + 2, out n); PlaceActor(sc.Roles[0], e, n, yaw + 90, "Beteiligter 1", PoseKind.Talk); e = At(1.6, halfRoad + 2, out n); PlaceActor(sc.Roles[1], e, n, yaw - 90, "Beteiligter 2", PoseKind.Talk); break; }
                case IncidentType.NoiseComplaint: { double e, n; e = At(0, halfRoad + 5, out n); PlaceActor(sc.Roles[0], e, n, yaw + 90, "Gastgeber", PoseKind.Talk); if (sc.Roles.Count > 1) { e = At(-6, halfRoad + 3, out n); PlaceActor(sc.Roles[1], e, n, yaw, "Anrufer", PoseKind.Idle); } break; }
                case IncidentType.AssistMotorist: { double e, n; e = At(0, halfRoad * 0.6 * -1, out n); AddCar(sc.Roles[0].Person, e, n, yaw, 1); e = At(2, halfRoad + 1.8, out n); PlaceActor(sc.Roles[0], e, n, yaw + 90, "Pannenfahrer", PoseKind.Phone); break; }
                case IncidentType.SuspiciousPerson: case IncidentType.DrunkPerson: { double e, n; e = At(0, halfRoad + 3, out n); PlaceActor(sc.Roles[0], e, n, yaw, inc.Type == IncidentType.DrunkPerson ? "Betrunkene Person" : "Verdächtige Person", PoseKind.Idle); break; }
                case IncidentType.MissingPerson: { double e, n; e = At(0, halfRoad + 3, out n); PlaceActor(sc.Roles[0], e, n, yaw + 90, "Elternteil", PoseKind.Talk); SpawnMissingChild(); break; }
                case IncidentType.RoadHazard:
                {
                    for (int i = 0; i < 6; i++) { double e = At((_rnd.NextDouble() - 0.5) * 4, (_rnd.NextDouble() - 0.5) * halfRoad, out double n); Props.Add(new SProp { Id = NextId(), Kind = "cargo", E = e, N = n, H = HeightAt(e, n), Yaw = (float)(_rnd.NextDouble() * 360) }); }
                    var d = new TrafficVehicle { Id = -100 - inc.Id, External = true, Length = 3f, Width = 2f, V = 0 }; Traffic?.UpdateExternal(d, inc.E, inc.N, (float)_roadDE, (float)_roadDN, 0); _dummies.Add(d); break;
                }
                case IncidentType.WantedVehicle: SpawnWantedVehicle(); break;
            }
            Info($"Einsatzort: {inc.TypeLabel}. Ansprechen mit [E]. Rapport mit [P].");
        }

        void SpawnMissingChild()
        {
            var c = CitizenFactory.Create(Active.Id * 13, Active.Canton.Length > 0 ? Active.Canton : "BE", Active.Municipality, "", 1, 1); c.FirstName = new[] { "Mia", "Lena", "Noah", "Luca", "Elin" }[Active.Id % 5]; c.Age = 8;
            double ang = _rnd.NextDouble() * Math.PI * 2, dist = 60 + _rnd.NextDouble() * 70; double e = Active.E + Math.Cos(ang) * dist, n = Active.N + Math.Sin(ang) * dist;
            var a = new SActor { Id = NextId(), Person = c, Label = "Vermisstes Kind", E = e, N = n, H = HeightAt(e, n), Pose = (byte)PoseKind.Idle, Child = true };
            a.Enc = new Encounter(EncounterKind.IncidentParty, c, new NpcSituation { Place = Active.Municipality, Statement = "Ich habe mich verlaufen … ich will zu meiner Mama." }, Corps, Obv, c.Seed); Actors.Add(a); _missingChild = a;
        }

        void SpawnWantedVehicle()
        {
            var sim = Traffic; if (sim == null) return; int link = sim.Net.Nearest(PE + _roadDE * 180, PN + _roadDN * 180, 120, out float s); if (link < 0) link = sim.Net.Nearest(PE, PN, 200, out s); if (link < 0) return;
            var v = sim.SpawnAt(link, s, 1.05f); string plate = ExtractPlate(Active.RadioText); if (!string.IsNullOrEmpty(plate)) v.Plate = plate; v.Driver.Wanted = true; v.Driver.Nervousness = 0.9f; v.Driver.Aggression = 0.8f; v.ColorIndex = ColorFromText(Active.RadioText);
            Computer.WantedPlates.Add(v.Plate); WantedVehicleId = v.Id; Radio("Fahndung: Fahrzeug im Raum gesichtet – Kontrollschild " + v.Plate + ".");
        }
        static string ExtractPlate(string text) { var m = Regex.Match(text ?? "", @"\b([A-Z]{2})\s?(\d{3,6})\b"); return m.Success ? m.Groups[1].Value + " " + m.Groups[2].Value : null; }
        static int ColorFromText(string t) { string[] names = { "weiss", "schwarz", "silber", "grau", "blau", "rot", "grün", "braun", "beige", "orange", "gelb", "bordeaux" }; t = (t ?? "").ToLowerInvariant(); for (int i = 0; i < names.Length; i++) if (t.Contains(names[i])) return i; return 1; }

        void UpdateScene()
        {
            foreach (var a in Actors)
            {
                if (a.Fleeing || a.Cuffed || a.Escaped) continue; double d = Math.Sqrt((a.E - PE) * (a.E - PE) + (a.N - PN) * (a.N - PN));
                if (a.Role != null && a.Role.Guilty && a.Role.Behaviour == Temperament.Aggressive && d < 7 && !a.Arrested && Active.Type == IncidentType.Burglary) { FleeActor(a); Warn(a.Label + " flüchtet!"); }
                if (a == _missingChild && d < 8 && !MissingFound) { MissingFound = true; Good("Das vermisste Kind wurde gefunden und ist wohlauf. Eltern informieren und Rapport schreiben."); a.Pose = (byte)PoseKind.Talk; }
            }
        }

        // ------------------------------------------------------------------ services
        public void CallService(string which)
        {
            if (Active == null) return;
            switch (which)
            {
                case "ems": if (Active.AmbulanceCalled) return; Active.AmbulanceCalled = true; EmsEnRoute = true; _emsAt = _wall + 55; Radio("Zentrale: Rettungsdienst 144 ist aufgeboten, ETA ca. 1 Minute."); break;
                case "tow": if (TowEnRoute || Props.Any(p => p.Kind == "tow")) return; TowEnRoute = true; _towAt = _wall + 80; Radio("Zentrale: Abschleppdienst ist avisiert."); break;
                case "fire": if (Active.FireCalled) return; Active.FireCalled = true; Radio("Zentrale: Feuerwehr (118) ist avisiert."); break;
                case "backup": if (Active.BackupCalled) return; Active.BackupCalled = true; Radio("Zentrale: Verstärkung ist unterwegs."); break;
            }
        }

        void UpdateServices()
        {
            if (Active == null || !SceneBuilt) return; var (sE, sN) = Side();
            if (EmsEnRoute && _wall >= _emsAt) { EmsEnRoute = false; AddService("ems", 6.5, new byte[] { 238, 238, 232 }, true, "BE 144144"); Radio("Rettungsdienst ist vor Ort und übernimmt die Betreuung."); }
            if (TowEnRoute && _wall >= _towAt) { TowEnRoute = false; AddService("tow", -7, new byte[] { 230, 190, 40 }, false, "BE 55 118"); Radio("Abschleppdienst ist vor Ort."); }
        }

        void AddService(string kind, double along, byte[] colour, bool blue, string plate)
        {
            var (sE, sN) = Side(); double e = Active.E + _roadDE * along + sE * 3.2, n = Active.N + _roadDN * along + sN * 3.2;
            Props.Add(new SProp { Id = NextId(), Kind = kind, Body = 4, Colour = colour, Plate = plate, Police = blue, Blue = blue, Hazard = true, E = e, N = n, H = HeightAt(e, n), Yaw = (float)(Math.Atan2(_roadDE, _roadDN) * 180 / Math.PI) });
        }

        // ------------------------------------------------------------------ completion & reports
        public void Complete(float score01, bool correct)
        {
            if (Active == null) return; Active.State = IncidentState.Resolved; Active.ResolvedAt = (float)Clock.ElapsedGameSeconds; Career.IncidentCompleted(score01, correct, (float)Clock.ElapsedGameSeconds);
            Active = null; Radio($"{CallSign}: Einsatz abgeschlossen."); _nextGen = Clock.ElapsedGameSeconds + 420 + _rnd.Next(500); _clearAt = _wall + 45;
        }
        double _clearAt = -1;

        public void ClearScene()
        {
            Actors.Clear(); Props.Clear(); EmsEnRoute = TowEnRoute = false; SceneBuilt = false; _missingChild = null; MissingFound = false; WantedVehicleId = -1; foreach (var d in _dummies) Traffic?.Externals.Remove(d); _dummies.Clear();
        }

        public void SubmitReport(int atFault, bool injured, bool amb, bool tow, string narrative)
        {
            var inc = Active; if (inc == null || inc.State != IncidentState.OnScene) return;
            var rep = new PoliceReport { Kind = inc.Scenario.AtFaultRole >= 0 || inc.Type == IncidentType.TrafficAccident || inc.Type == IncidentType.TrafficAccidentInjured ? ReportKind.Unfallrapport : ReportKind.Einsatzrapport, IncidentId = inc.Id, Narrative = narrative ?? "", AtFaultRole = atFault, InjuredReported = injured, TowCalled = tow || TowEnRoute || Props.Any(p => p.Kind == "tow"), AmbulanceCalled = amb || inc.AmbulanceCalled, CreatedAt = (float)Clock.ElapsedGameSeconds };
            ReportEvaluation ev;
            if (rep.Kind == ReportKind.Unfallrapport) ev = ReportEvaluator.EvaluateAccident(rep, inc.Scenario);
            else { ev = new ReportEvaluation { Correct = true, Score = 80 }; if (inc.Scenario.NeedsAmbulance && !rep.AmbulanceCalled) { ev.Correct = false; ev.Score = 40; ev.Findings.Add("Rettungsdienst wäre nötig gewesen."); } if (inc.Scenario.NeedsTow && !rep.TowCalled) { ev.Score -= 10; ev.Findings.Add("Abschleppdienst wäre nötig gewesen."); } if (ev.Findings.Count == 0) ev.Findings.Add("Rapport vollständig."); }
            foreach (var f in ev.Findings) Note(ev.Correct ? "info" : "warn", f, 9f);
            AddCase(rep.Kind == ReportKind.Unfallrapport ? "Unfall" : "Einsatz", inc.TypeLabel + " – " + inc.Address, "", "", 0, ev.Correct);
            if (ev.Falsified) Career.ApplyAssessment(new LegalAssessment { Lawful = false, Severity = 3, Headline = "Falsche Angaben im Rapport" }, 0.1f, true, (float)Clock.ElapsedGameSeconds);
            Complete(Math.Max(0, Math.Min(1, ev.Score / 100f)), ev.Correct);
        }

        public void TickScenery() { if (_clearAt > 0 && _wall >= _clearAt && Active == null) { _clearAt = -1; ClearScene(); } }
    }
}
