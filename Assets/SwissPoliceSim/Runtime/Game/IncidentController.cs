using System;
using System.Collections.Generic;
using SwissPoliceSim.Characters;
using SwissPoliceSim.Core.Game;
using SwissPoliceSim.Core.Legal;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Npc;
using SwissPoliceSim.Core.Traffic;
using SwissPoliceSim.Vehicles;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.Game
{
    /// <summary>
    /// Dispatch flow: the dispatch centre offers incidents on the real road network (radio text with the real street and municipality), the player accepts, drives there (navigation),
    /// the scene is built (accident vehicles, parties, witnesses, injured, ambulance/tow on request) and the incident ends with a report that is checked against the hidden truth.
    /// </summary>
    public sealed class IncidentController : MonoBehaviour
    {
        public Incident Offered, Active; public float OfferSecondsLeft; public readonly List<NpcActor> Actors = new List<NpcActor>(); public bool SceneBuilt; public bool AutoGenerate = true;
        public event Action<Incident> IncidentOffered, IncidentAccepted, IncidentArrived, IncidentCompleted;
        readonly List<GameObject> _props = new List<GameObject>(); double _nextGenGame; float _arrivalTimer, _emsTimer; GameObject _beacon; Vector3 _scenePos; Vector3 _roadDir = Vector3.forward;
        readonly List<TrafficVehicle> _dummies = new List<TrafficVehicle>(); public bool EmsEnRoute, TowEnRoute; float _emsArrive, _towArrive; GameObject _ems, _tow; public bool MissingFound;
        System.Random _rnd = new System.Random(7);

        GameSession S => GameSession.I;

        public void Init(int seed) { _rnd = new System.Random(seed * 31 + 5); _nextGenGame = S.Clock.ElapsedGameSeconds + 150 + _rnd.Next(150); }

        void Update()
        {
            if (S == null || !S.Playing || S.Paused || S.Dispatch == null) return;
            double now = S.Clock.ElapsedGameSeconds;
            if (Offered != null) { OfferSecondsLeft -= Time.deltaTime; if (OfferSecondsLeft <= 0) { Notifier.Radio("Zentrale: Einsatz anderweitig vergeben."); Offered.State = IncidentState.Declined; Offered = null; _nextGenGame = now + 600; } }
            else if (Active == null && AutoGenerate && now >= _nextGenGame && S.Player != null && !S.Player.Frozen && !S.Pursuit.IsActive && !S.Encounters.InEncounter) Generate();
            if (Active == null) return;
            if (Active.State == IncidentState.Accepted || Active.State == IncidentState.EnRoute) { Active.State = IncidentState.EnRoute; CheckArrival(); }
            else if (Active.State == IncidentState.OnScene) UpdateScene();
            UpdateBeacon(); UpdateServices();
        }

        // ------------------------------------------------------------------ offering
        public Incident Generate(IncidentType? force = null)
        {
            var p = S.PlayerLv95; var inc = S.Dispatch.Generate(p.E, p.N, (float)S.Clock.ElapsedGameSeconds, S.Hour); if (inc == null) { _nextGenGame = S.Clock.ElapsedGameSeconds + 120; return null; }
            Offered = inc; OfferSecondsLeft = 70f; IncidentOffered?.Invoke(inc); Notifier.Radio(inc.RadioText); Audio.ProceduralAudio.RadioBeep();
            return inc;
        }

        public void Accept()
        {
            if (Offered == null) return; Active = Offered; Offered = null; Active.State = IncidentState.Accepted; Active.AcceptedAt = (float)S.Clock.ElapsedGameSeconds; _arrivalTimer = 0;
            Notifier.Radio($"{S.Dispatch.CallSign}: Verstanden, wir übernehmen. {Active.Address}, {Active.Municipality}."); S.Nav.SetDestination(Active.E, Active.N, Active.Address + ", " + Active.Municipality);
            if (Active.Urgency >= 3) Notifier.Push("Dringlicher Einsatz: Blaulicht und Wechselklanghorn sind zulässig (Art. 100 Ziff. 4 SVG, sorgfältig fahren).", NoteKind.Info, 9f);
            IncidentAccepted?.Invoke(Active);
        }

        public void Decline() { if (Offered == null) return; Notifier.Radio("Zentrale: Verstanden, wir vergeben den Einsatz weiter."); Offered.State = IncidentState.Declined; Offered = null; _nextGenGame = S.Clock.ElapsedGameSeconds + 400; }

        public void ResetFromSave(GameSave.SavedIncident saved)
        {
            ClearScene(); Offered = null; Active = null; if (saved == null) { _nextGenGame = S.Clock.ElapsedGameSeconds + 200; return; }
            var inc = new Incident { Id = saved.Id, Type = (IncidentType)saved.Type, E = saved.E, N = saved.N, Address = saved.Address, Municipality = saved.Municipality, Canton = saved.Canton, Urgency = saved.Urgency, RadioText = saved.RadioText, State = IncidentState.Accepted };
            Active = inc; S.Nav.SetDestination(inc.E, inc.N, inc.Address + ", " + inc.Municipality);
        }

        void CheckArrival()
        {
            var p = S.PlayerLv95; double d = Math.Sqrt((p.E - Active.E) * (p.E - Active.E) + (p.N - Active.N) * (p.N - Active.N)); bool inCar = S.Player.InVehicle; float speed = inCar ? S.Car.SpeedKmh : 0;
            if (d < (inCar ? 70 : 40) && speed < 25) { _arrivalTimer += Time.deltaTime; if (_arrivalTimer > 0.5f || !inCar) Arrive(); } else _arrivalTimer = 0;
        }

        void Arrive()
        {
            Active.State = IncidentState.OnScene; Active.ArrivedAt = (float)S.Clock.ElapsedGameSeconds; S.Nav.Clear(); BuildScene(); Notifier.Radio($"{S.Dispatch.CallSign}: Vor Ort."); IncidentArrived?.Invoke(Active);
            float resp = Active.ArrivedAt - Active.AcceptedAt; if (resp < Active.DeadlineSeconds) S.Career.Xp += 10 * (Active.Urgency);
        }

        // ------------------------------------------------------------------ scene
        void BuildScene()
        {
            ClearScene(); var inc = Active; SceneBuilt = true;
            float h = S.Streamer.Height(inc.E, inc.N); if (float.IsNaN(h)) h = S.PlayerPos.y;
            if (WorldQuery.NearestRoad(S.Streamer, inc.E, inc.N, 40, out var ri)) { var w = ri.Way; double dx = w.E[ri.Segment + 1] - w.E[ri.Segment], dz = w.N[ri.Segment + 1] - w.N[ri.Segment]; double l = Math.Sqrt(dx * dx + dz * dz); if (l > 0.1) _roadDir = new Vector3((float)(dx / l), 0, (float)(dz / l)); }
            _scenePos = WorldOrigin.ToUnity(inc.E, inc.N, h + 0.05); var right = Vector3.Cross(Vector3.up, _roadDir) * -1f; float yaw = Mathf.Atan2(_roadDir.x, _roadDir.z) * Mathf.Rad2Deg; float halfRoad = ri.Way != null ? ri.Way.CarriagewayWidth() * 0.5f : 3.2f;
            var sc = inc.Scenario;
            switch (inc.Type)
            {
                case IncidentType.TrafficAccident: case IncidentType.TrafficAccidentInjured:
                {
                    var a = Prop(Veh(0), _scenePos - _roadDir * 2.4f - right * (halfRoad * 0.5f), yaw + 8f, 0); var b = Prop(Veh(1), _scenePos + _roadDir * 3.1f - right * (halfRoad * 0.3f), yaw - 24f, 1);
                    for (int i = 0; i < sc.Roles.Count && i < 2; i++) { var pos = _scenePos + right * (halfRoad + 1.7f) + _roadDir * (i * 2.2f - 1f); PlaceActor(sc.Roles[i], pos, yaw + (i == 0 ? 180 : 160), sc.Roles[i].Role, sc.Roles[i].Injured ? PoseKind.Idle : PoseKind.Talk); }
                    AddWreckDebris(_scenePos); break;
                }
                case IncidentType.Burglary: PlaceActor(sc.Roles[0], _scenePos + right * (halfRoad + 3f), yaw + 90, "Geschädigter", PoseKind.Talk); PlaceActor(sc.Roles[1], _scenePos + right * (halfRoad + 3f) + _roadDir * 55f, yaw, "Mutmasslicher Täter", PoseKind.Idle, hiding: true); break;
                case IncidentType.Shoplifting: PlaceActor(sc.Roles[0], _scenePos + right * (halfRoad + 2.5f), yaw + 180, "Filialleiter", PoseKind.Talk); PlaceActor(sc.Roles[1], _scenePos + right * (halfRoad + 2.5f) + _roadDir * 1.4f, yaw, "Verdächtige Person", PoseKind.Idle); break;
                case IncidentType.Dispute: PlaceActor(sc.Roles[0], _scenePos + right * (halfRoad + 2f), yaw + 90, "Beteiligter 1", PoseKind.Talk); PlaceActor(sc.Roles[1], _scenePos + right * (halfRoad + 2f) + _roadDir * 1.6f, yaw - 90, "Beteiligter 2", PoseKind.Talk); break;
                case IncidentType.NoiseComplaint: PlaceActor(sc.Roles[0], _scenePos + right * (halfRoad + 5f), yaw + 90, "Gastgeber", PoseKind.Talk); if (sc.Roles.Count > 1) PlaceActor(sc.Roles[1], _scenePos + right * (halfRoad + 3f) - _roadDir * 6f, yaw, "Anrufer", PoseKind.Idle); break;
                case IncidentType.AssistMotorist: { var c = Prop(VehicleFactory.CivilianLivery(_rnd.Next(12), PlateOf(sc.Roles[0])), _scenePos + right * (halfRoad * 0.6f), yaw, 0, BodyKind.Hatchback); PlaceActor(sc.Roles[0], _scenePos + right * (halfRoad + 1.8f) + _roadDir * 2f, yaw + 90, "Pannenfahrer", PoseKind.Phone); break; }
                case IncidentType.SuspiciousPerson: case IncidentType.DrunkPerson: PlaceActor(sc.Roles[0], _scenePos + right * (halfRoad + 3f), yaw, inc.Type == IncidentType.DrunkPerson ? "Betrunkene Person" : "Verdächtige Person", PoseKind.Idle); break;
                case IncidentType.MissingPerson: PlaceActor(sc.Roles[0], _scenePos + right * (halfRoad + 3f), yaw + 90, "Elternteil", PoseKind.Talk); SpawnMissingChild(); break;
                case IncidentType.RoadHazard: SpawnCargo(_scenePos, yaw, halfRoad); break;
                case IncidentType.WantedVehicle: SpawnWantedVehicle(); break;
            }
            Notifier.Push($"Einsatzort: {inc.TypeLabel}. Ansprechen mit [E]. Rapport mit [P].", NoteKind.Info, 9f);
        }

        void SpawnMissingChild()
        {
            var c = CitizenFactory.Create(Active.Id * 13, Active.Canton.Length > 0 ? Active.Canton : "BE", Active.Municipality, "", 1, 1); c.FirstName = new[] { "Mia", "Lena", "Noah", "Luca", "Elin" }[Active.Id % 5]; c.Age = 8;
            var ang = (_rnd.NextDouble() * 360) * Mathf.Deg2Rad; var pos = _scenePos + new Vector3((float)Math.Cos(ang), 0, (float)Math.Sin(ang)) * (60f + (float)_rnd.NextDouble() * 70f);
            if (Physics.Raycast(pos + Vector3.up * 200f, Vector3.down, out var hit, 400f)) pos = hit.point;
            var spec = HumanSpec.FromPed(true, true, false, 7, 3, 0, 2, 1.28f, c.Seed); var go = HumanFactory.Create(spec, "Vermisstes Kind", 0, true); go.transform.position = pos; var a = go.AddComponent<NpcActor>(); a.Person = c; a.Label = "Vermisstes Kind"; a.Visual = go.GetComponent<HumanVisual>(); a.Visual.Pose = PoseKind.Idle;
            a.Enc = new Encounter(EncounterKind.IncidentParty, c, new NpcSituation { Place = Active.Municipality, Statement = "Ich habe mich verlaufen … ich will zu meiner Mama." }, S.Corps, S.Obv, c.Seed); Actors.Add(a); MissingChild = a;
        }
        public NpcActor MissingChild;

        void SpawnCargo(Vector3 pos, float yaw, float halfRoad)
        {
            var rnd = new System.Random(Active.Id);
            for (int i = 0; i < 6; i++)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = "Ladung"; g.transform.position = pos + Quaternion.Euler(0, yaw, 0) * new Vector3((float)(rnd.NextDouble() - 0.5) * halfRoad, 0.3f, (float)(rnd.NextDouble() - 0.5) * 4f); g.transform.localScale = new Vector3(1.2f, 0.6f, 0.8f); g.transform.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 90, 0);
                var mr = g.GetComponent<MeshRenderer>(); mr.sharedMaterial = SwissPoliceSim.Rendering.MaterialLibrary.Make("pallet", null, new Color(0.72f, 0.55f, 0.35f), 0.1f, 0f, false); _props.Add(g);
            }
            // the AI sees the obstacle as a stopped vehicle
            var d = new TrafficVehicle { Id = -100 - Active.Id, External = true, Length = 3f, Width = 2f, V = 0 }; S.Traffic.Sim?.UpdateExternal(d, Active.E, Active.N, _roadDir.x, _roadDir.z, 0); _dummies.Add(d);
        }

        void SpawnWantedVehicle()
        {
            var sim = S.Traffic.Sim; if (sim == null) return; var p = S.PlayerLv95; int link = sim.Net.Nearest(p.E + _roadDir.x * 180, p.N + _roadDir.z * 180, 120, out float s); if (link < 0) link = sim.Net.Nearest(p.E, p.N, 200, out s); if (link < 0) return;
            var v = sim.SpawnAt(link, s, 1.05f); string plate = ExtractPlate(Active.RadioText); if (!string.IsNullOrEmpty(plate)) v.Plate = plate; v.Driver.Wanted = true; v.Driver.Nervousness = 0.9f; v.Driver.Aggression = 0.8f; v.ColorIndex = ColorFromText(Active.RadioText);
            S.Computer.WantedPlates.Add(v.Plate); WantedVehicleId = v.Id; Notifier.Radio("Fahndung: Fahrzeug im Raum gesichtet – Kontrollschild " + v.Plate + ".");
        }
        public int WantedVehicleId = -1;
        static string ExtractPlate(string text) { var m = System.Text.RegularExpressions.Regex.Match(text ?? "", @"\b([A-Z]{2})\s?(\d{3,6})\b"); return m.Success ? m.Groups[1].Value + " " + m.Groups[2].Value : null; }
        static int ColorFromText(string t) { string[] names = { "weiss", "schwarz", "silber", "grau", "blau", "rot", "grün", "braun", "beige", "orange", "gelb", "bordeaux" }; t = (t ?? "").ToLowerInvariant(); for (int i = 0; i < names.Length; i++) if (t.Contains(names[i])) return i; return 1; }

        CarLivery Veh(int role) { var c = Active.Scenario.Roles[Math.Min(role, Active.Scenario.Roles.Count - 1)].Person; return VehicleFactory.CivilianLivery(c.Seed % 12, c.Vehicle.Plate); }
        static string PlateOf(ScenarioRole r) => r.Person != null && !string.IsNullOrEmpty(r.Person.Vehicle?.Plate) ? r.Person.Vehicle.Plate : "BE 123456";

        GameObject Prop(CarLivery liv, Vector3 pos, float yaw, int role, BodyKind kind = BodyKind.Sedan)
        {
            if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out var hit, 12f)) pos.y = hit.point.y + 0.02f;
            var vv = VehicleFactory.Create(kind, liv, false, "Unfallfahrzeug " + role); vv.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0)); vv.Hazard = true;
            var rb = vv.gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true; var col = vv.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(vv.Model.Width, 1.2f, vv.Model.Length); col.center = new Vector3(0, 0.75f, 0);
            _props.Add(vv.gameObject); return vv.gameObject;
        }

        void AddWreckDebris(Vector3 pos)
        {
            for (int i = 0; i < 7; i++)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = "Trümmer"; Destroy(g.GetComponent<Collider>()); g.transform.localScale = new Vector3(0.12f + (float)_rnd.NextDouble() * 0.2f, 0.03f, 0.1f + (float)_rnd.NextDouble() * 0.25f);
                g.transform.position = pos + new Vector3((float)(_rnd.NextDouble() - 0.5) * 6f, 0.03f, (float)(_rnd.NextDouble() - 0.5) * 6f); g.transform.rotation = Quaternion.Euler(0, (float)_rnd.NextDouble() * 360f, 0);
                g.GetComponent<MeshRenderer>().sharedMaterial = SwissPoliceSim.Rendering.MaterialLibrary.Make("debris", null, new Color(0.15f, 0.15f, 0.17f), 0.4f, 0.3f, false); _props.Add(g);
            }
        }

        NpcActor PlaceActor(ScenarioRole role, Vector3 pos, float yaw, string label, PoseKind pose, bool hiding = false)
        {
            if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out var hit, 12f)) pos.y = hit.point.y;
            var a = NpcActor.Spawn(role.Person, pos, yaw, label, role); a.Visual.Pose = pose; a.LookAt = S.Player.FocusTransform;
            var sit = new NpcSituation { TrueViolation = ViolationType.None, NpcGuilty = role.Guilty, IsDriver = false, InVehicle = false, Place = Active.Municipality, Statement = role.Statement, TimeOfDayNight = S.Hour < 6 || S.Hour > 21 };
            a.Enc = new Encounter(EncounterKind.IncidentParty, role.Person, sit, S.Corps, S.Obv, role.Person.Seed); a.Enc.Ctx.SuspectIsDriver = false; a.Enc.Ctx.PersonDetained = false;
            a.Enc.Ctx.Suspicion = role.Guilty ? Suspicion.Concrete : Suspicion.None; a.Role = role; if (role.Injured) a.Enc.Ctx.Suspicion = Suspicion.None;
            Actors.Add(a); return a;
        }

        void UpdateScene()
        {
            if (Active == null) return;
            // suspects that were not found yet may try to run when the officer comes close
            foreach (var a in Actors)
            {
                if (a == null || a.Fleeing || a.Cuffed || a.Escaped) continue;
                float d = Vector3.Distance(a.transform.position, S.PlayerPos);
                if (a.Role != null && a.Role.Guilty && a.Role.Behaviour == Temperament.Aggressive && d < 7f && !a.Arrested && Active.Type == IncidentType.Burglary) { a.Flee(S.PlayerPos); Notifier.Warn(a.Label + " flüchtet!"); }
                if (a == MissingChild && d < 8f && !MissingFound) { MissingFound = true; Notifier.Good("Das vermisste Kind wurde gefunden und ist wohlauf. Eltern informieren und Rapport schreiben."); a.Visual.Pose = PoseKind.Talk; }
            }
        }

        void UpdateBeacon()
        {
            bool want = Active != null && (Active.State == IncidentState.EnRoute || Active.State == IncidentState.Accepted);
            if (want && _beacon == null) { _beacon = new GameObject("IncidentBeacon"); var lr = _beacon.AddComponent<LineRenderer>(); lr.positionCount = 2; lr.startWidth = lr.endWidth = 0.9f; lr.material = new Material(Shader.Find("Sprites/Default")); lr.startColor = new Color(0.2f, 0.5f, 1f, 0.55f); lr.endColor = new Color(0.2f, 0.5f, 1f, 0.0f); lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
            if (!want && _beacon != null) { Destroy(_beacon); _beacon = null; }
            if (_beacon != null) { var p = WorldOrigin.ToUnity(Active.E, Active.N, S.Streamer.Height(Active.E, Active.N) is float hh && !float.IsNaN(hh) ? hh : S.PlayerPos.y); var lr = _beacon.GetComponent<LineRenderer>(); lr.SetPosition(0, p); lr.SetPosition(1, p + Vector3.up * 120f); }
        }

        // ------------------------------------------------------------------ services
        public void CallAmbulance() { if (Active == null || Active.AmbulanceCalled) return; Active.AmbulanceCalled = true; EmsEnRoute = true; _emsArrive = Time.time + 55f; Notifier.Radio("Zentrale: Rettungsdienst 144 ist aufgeboten, ETA ca. 1 Minute."); }
        public void CallTow() { if (Active == null || TowEnRoute || _tow != null) return; TowEnRoute = true; _towArrive = Time.time + 80f; Notifier.Radio("Zentrale: Abschleppdienst ist avisiert."); }
        public void CallFire() { if (Active == null || Active.FireCalled) return; Active.FireCalled = true; Notifier.Radio("Zentrale: Feuerwehr (118) ist avisiert."); }
        public void CallBackup() { if (Active == null || Active.BackupCalled) return; Active.BackupCalled = true; Notifier.Radio("Zentrale: Verstärkung ist unterwegs."); }

        void UpdateServices()
        {
            if (Active == null || !SceneBuilt) return;
            if (EmsEnRoute && Time.time >= _emsArrive && _ems == null) { EmsEnRoute = false; _ems = SpawnService(new CarLivery { Body = new byte[] { 238, 238, 232 }, Stripe = new byte[] { 220, 40, 30 }, LightBar = true, Plate = "BE 144144" }, BodyKind.Van, 6.5f); Notifier.Radio("Rettungsdienst ist vor Ort und übernimmt die Betreuung der Verletzten."); foreach (var a in Actors) if (a.Role != null && a.Role.Injured) a.Visual.Pose = PoseKind.Idle; }
            if (TowEnRoute && Time.time >= _towArrive && _tow == null) { TowEnRoute = false; _tow = SpawnService(new CarLivery { Body = new byte[] { 230, 190, 40 }, LightBar = false, Plate = "BE 55 118" }, BodyKind.Van, -7f); Notifier.Radio("Abschleppdienst ist vor Ort."); }
        }

        GameObject SpawnService(CarLivery liv, BodyKind kind, float along)
        {
            var right = Vector3.Cross(Vector3.up, _roadDir) * -1f; var pos = _scenePos + _roadDir * along + right * 3.2f; float yaw = Mathf.Atan2(_roadDir.x, _roadDir.z) * Mathf.Rad2Deg;
            var go = Prop(liv, pos, yaw, 9, kind); var vv = go.GetComponent<VehicleVisual>(); vv.Blue = liv.LightBar; vv.Hazard = true; return go;
        }

        // ------------------------------------------------------------------ completion
        public void Complete(float score01, bool correct)
        {
            if (Active == null) return; Active.State = IncidentState.Resolved; Active.ResolvedAt = (float)S.Clock.ElapsedGameSeconds; S.Career.IncidentCompleted(score01, correct, (float)S.Clock.ElapsedGameSeconds);
            var done = Active; Active = null; Notifier.Radio($"{S.Dispatch.CallSign}: Einsatz abgeschlossen."); IncidentCompleted?.Invoke(done); _nextGenGame = S.Clock.ElapsedGameSeconds + 420 + _rnd.Next(500); Invoke(nameof(ClearScene), 45f);
        }

        public void ClearScene()
        {
            foreach (var a in Actors) if (a != null) Destroy(a.gameObject); Actors.Clear(); foreach (var p in _props) if (p != null) Destroy(p); _props.Clear(); _ems = _tow = null; SceneBuilt = false; EmsEnRoute = TowEnRoute = false; MissingChild = null; MissingFound = false; WantedVehicleId = -1;
            foreach (var d in _dummies) { S.Traffic.Sim?.Externals.Remove(d); } _dummies.Clear(); if (_beacon != null) { Destroy(_beacon); _beacon = null; }
        }

        public NpcActor NearestActor(Vector3 from, float maxDist)
        {
            NpcActor best = null; float bd = maxDist; foreach (var a in Actors) { if (a == null || !a.gameObject.activeInHierarchy) continue; float d = Vector3.Distance(from, a.transform.position); if (d < bd) { bd = d; best = a; } } return best;
        }
    }
}
