using System;
using System.Collections.Generic;
using System.IO;
using SwissPoliceSim.Core.Game;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Legal;
using SwissPoliceSim.Core.Police;
using SwissPoliceSim.Core.Npc;
using SwissPoliceSim.Player;
using SwissPoliceSim.Rendering;
using SwissPoliceSim.Traffic;
using SwissPoliceSim.Vehicles;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.Game
{
    /// <summary>The state of the running game: clock, corps, career, police computer, dispatch and handles to all scene systems. Everything else reads it via <see cref="I"/>.</summary>
    public sealed class GameSession : MonoBehaviour
    {
        public static GameSession I { get; private set; }
        public GameClock Clock = new GameClock(); public PoliceCorps Corps; public CareerState Career; public PoliceComputer Computer = new PoliceComputer();
        public DispatchCenter Dispatch; public LawLibrary Laws; public ObvCatalog Obv; public int Seed = 1;
        public PlayerController Player; public PlayerCar Car; public TileStreamer Streamer; public TrafficManager Traffic; public PedestrianManager Peds; public Atmosphere Atmos; public CameraRig Rig;
        public NavigationService Nav; public IncidentController Incidents; public EnforcementController Enforcement; public EncounterController Encounters; public PursuitController Pursuit;
        public bool Paused, Playing; public string RegionName = "";
        public readonly List<GameSave.SavedCase> Cases = new List<GameSave.SavedCase>(); public readonly List<string> ConsequenceLog = new List<string>(); public readonly List<string> Flags = new List<string>();
        public JurisdictionStatus Jurisdiction = new JurisdictionStatus { Kind = JurisdictionKind.Unknown }; public string PlaceName = ""; public string StreetName = ""; public int SpeedLimitKmh; public bool UrbanHere;
        public Lv95 PlayerLv95; public Vector3 PlayerPos; public event Action<List<Consequence>> ConsequencesApplied; public event Action GameOver;
        float _nextJur, _nextRoad;

        readonly Dictionary<int, Citizen> _vehicleCitizens = new Dictionary<int, Citizen>();

        void Awake() { I = this; Computer.Resolver = ResolvePlate; }

        /// <summary>The person behind a simulated vehicle: built from the simulation's driver flags so the computer, the dialogue and the breath test all agree.</summary>
        public Citizen CitizenForVehicle(SwissPoliceSim.Core.Traffic.TrafficVehicle v)
        {
            if (_vehicleCitizens.TryGetValue(v.Id, out var c)) return c;
            string plate = string.IsNullOrEmpty(v.Plate) ? "BE 000000" : v.Plate; string canton = plate.Length >= 2 ? plate.Substring(0, 2) : "BE"; var d = v.Driver;
            c = CitizenFactory.Create(v.Id * 7919 + Seed, canton, PlaceName ?? "", plate, Mathf.Clamp(v.BodyKind, 0, 4), v.ColorIndex); c.Honesty = Mathf.Clamp(d.Honesty, 0.1f, 1f);
            if (!string.IsNullOrEmpty(d.FirstName)) { c.FirstName = d.FirstName; } if (!string.IsNullOrEmpty(d.Name)) c.LastName = d.Name.Contains(" ") ? d.Name.Substring(d.Name.LastIndexOf(' ') + 1) : d.Name; if (!string.IsNullOrEmpty(d.Birthdate)) c.Birthdate = d.Birthdate; if (!string.IsNullOrEmpty(d.Address)) c.Address = d.Address;
            CitizenFactory.ApplyDriverTruth(c, d.Impaired, d.BloodAlcohol, d.NoLicense, d.LicenseSuspended, d.UninsuredOrExpired, d.VehicleDefect, d.Wanted, v.Id + Seed);
            _vehicleCitizens[v.Id] = c; Computer.Register(c); return c;
        }

        Citizen ResolvePlate(string plate)
        {
            if (Traffic == null || Traffic.Sim == null) return null; string np = plate.Replace(" ", "").ToUpperInvariant();
            foreach (var v in Traffic.Sim.Vehicles) if (!string.IsNullOrEmpty(v.Plate) && v.Plate.Replace(" ", "").ToUpperInvariant() == np) return CitizenForVehicle(v);
            return null;
        }
        void OnDestroy() { if (I == this) I = null; }

        public DateTime Now => Clock.Now;
        public float Hour => Clock.HourOfDay;

        void Update()
        {
            if (!Playing || Paused || Player == null) return;
            Clock.Advance(Time.deltaTime); Computer.Now = Clock.ElapsedGameSeconds;
            PlayerPos = Player.WorldPosition; PlayerLv95 = WorldOrigin.ToLv95(PlayerPos);
            if (Time.unscaledTime >= _nextJur) { _nextJur = Time.unscaledTime + 1f; UpdateJurisdiction(); }
            if (Time.unscaledTime >= _nextRoad) { _nextRoad = Time.unscaledTime + 0.35f; UpdateRoad(); }
            Notifier.Tick();
            if (Career != null && Career.Dismissed && Playing) { Playing = false; GameOver?.Invoke(); }
        }

        void UpdateJurisdiction()
        {
            if (WorldData.Admin == null || Corps == null) return;
            Jurisdiction = JurisdictionManager.Evaluate(WorldData.Admin, Corps, PlayerLv95.E, PlayerLv95.N); PlaceName = Jurisdiction.MunicipalityName;
        }

        void UpdateRoad()
        {
            if (Streamer == null) return;
            if (WorldQuery.NearestRoad(Streamer, PlayerLv95.E, PlayerLv95.N, 18, out var ri)) { SpeedLimitKmh = ri.SpeedLimitKmh; StreetName = ri.Name; UrbanHere = ri.Urban; } else { SpeedLimitKmh = 0; StreetName = ""; }
        }

        /// <summary>Applies the legal assessment of an action to the career; shows the legal basis and the consequences. Returns the consequences.</summary>
        public List<Consequence> Judge(LegalAssessment a, float npcAnger = 0.2f, bool documented = true)
        {
            if (a == null || Career == null) return new List<Consequence>();
            var list = Career.ApplyAssessment(a, npcAnger, documented, (float)Clock.ElapsedGameSeconds);
            if (!a.Lawful) Notifier.Warn("Rechtswidrig: " + (a.Reasons.Count > 0 ? a.Reasons[0] : a.Headline));
            foreach (var c in list) { ConsequenceLog.Add(Clock.Now.ToString("dd.MM. HH:mm") + "  " + c.Text); Notifier.Bad(c.Text); }
            if (list.Count > 0) ConsequencesApplied?.Invoke(list);
            return list;
        }

        public void AddCase(string kind, string summary, string plate, string person, int fine, bool correct)
        {
            Cases.Add(new GameSave.SavedCase { Kind = kind, Summary = summary, Plate = plate ?? "", Person = person ?? "", FineChf = fine, Correct = correct, Time = Clock.ElapsedGameSeconds }); if (Cases.Count > 200) Cases.RemoveAt(0);
        }

        // ------------------------------------------------------------------ save / load
        public static string SaveDir => Path.Combine(Application.persistentDataPath, "Saves");
        public static string SlotPath(int slot) => Path.Combine(SaveDir, $"slot{slot}.json");
        public static bool SlotExists(int slot) => File.Exists(SlotPath(slot));
        public static string SlotInfo(int slot)
        {
            try { if (!SlotExists(slot)) return "leer"; var g = GameSave.LoadFromFile(SlotPath(slot)); var t = DateTime.Parse(g.StartIso).AddSeconds(g.GameSeconds); return $"{PoliceCatalog.ById(g.CorpsId)?.Name ?? g.CorpsId} · Rang {g.RankIndex + 1} · {t:dd.MM.yyyy HH:mm} · {g.Region}"; } catch { return "beschädigt"; }
        }

        public GameSave BuildSave()
        {
            var g = new GameSave { CorpsId = Corps.Id, RankIndex = Career.RankIndex, Xp = Career.Xp, GameSeconds = Clock.ElapsedGameSeconds, TimeScale = Clock.TimeScale, StartIso = Clock.Start.ToString("s"), Seed = Seed, Region = PlaceName };
            var p = WorldOrigin.ToLv95(Player.transform.position); g.PlayerE = p.E; g.PlayerN = p.N; g.PlayerH = p.H; g.PlayerYaw = Player.transform.eulerAngles.y; g.InVehicle = Player.InVehicle;
            var cp = WorldOrigin.ToLv95(Car.transform.position); g.VehicleE = cp.E; g.VehicleN = cp.N; g.VehicleH = cp.H; g.VehicleYaw = Car.transform.eulerAngles.y; g.Fuel = Car.Fuel01; g.VehicleDamage = Car.Damage01;
            g.Legality = Career.Legality; g.Professionalism = Career.Professionalism; g.DeEscalation = Career.DeEscalation; g.Reporting = Career.Reporting; g.Community = Career.Community;
            g.ComplaintsOpen = Career.ComplaintsOpen; g.ComplaintsTotal = Career.ComplaintsTotal; g.InvestigationsOpen = Career.InvestigationsOpen; g.DisciplinaryCount = Career.DisciplinaryCount; g.FinesIssued = Career.FinesIssued; g.ArrestsMade = Career.ArrestsMade; g.Suspended = Career.Suspended; g.Dismissed = Career.Dismissed;
            foreach (var c in Cases) g.Cases.Add(c); foreach (var s in ConsequenceLog) g.Consequences.Add(s); foreach (var f in Flags) g.Flags.Add(f);
            var inc = Incidents != null ? Incidents.Active : null; if (inc != null && inc.State != IncidentState.Resolved) g.Incidents.Add(new GameSave.SavedIncident { Id = inc.Id, Type = (int)inc.Type, State = (int)inc.State, Urgency = inc.Urgency, E = inc.E, N = inc.N, Address = inc.Address, Municipality = inc.Municipality, Canton = inc.Canton, RadioText = inc.RadioText, AmbulanceCalled = inc.AmbulanceCalled, FireCalled = inc.FireCalled });
            return g;
        }

        public bool Save(int slot)
        {
            try { BuildSave().SaveToFile(SlotPath(slot)); Notifier.Good($"Spielstand {slot} gespeichert."); return true; }
            catch (Exception ex) { Notifier.Bad("Speichern fehlgeschlagen: " + ex.Message); return false; }
        }

        /// <summary>Applies a save to the running session (the world around the new position streams in; the player is frozen until the ground is ready).</summary>
        public bool Load(int slot)
        {
            try
            {
                var g = GameSave.LoadFromFile(SlotPath(slot)); ApplySave(g); Notifier.Good($"Spielstand {slot} geladen."); return true;
            }
            catch (Exception ex) { Notifier.Bad("Laden fehlgeschlagen: " + ex.Message); return false; }
        }

        public void ApplySave(GameSave g)
        {
            Corps = PoliceCatalog.ById(g.CorpsId) ?? PoliceCatalog.ById("KAPO_BE"); Seed = g.Seed;
            Clock.Start = DateTime.Parse(g.StartIso); Clock.ElapsedGameSeconds = g.GameSeconds; Clock.TimeScale = g.TimeScale;
            Career = new CareerState(g.Seed) { CorpsId = g.CorpsId, RankIndex = g.RankIndex, Xp = g.Xp, Legality = g.Legality, Professionalism = g.Professionalism, DeEscalation = g.DeEscalation, Reporting = g.Reporting, Community = g.Community, ComplaintsOpen = g.ComplaintsOpen, ComplaintsTotal = g.ComplaintsTotal, InvestigationsOpen = g.InvestigationsOpen, DisciplinaryCount = g.DisciplinaryCount, FinesIssued = g.FinesIssued, ArrestsMade = g.ArrestsMade, Suspended = g.Suspended, Dismissed = g.Dismissed };
            Cases.Clear(); Cases.AddRange(g.Cases); ConsequenceLog.Clear(); ConsequenceLog.AddRange(g.Consequences); Flags.Clear(); Flags.AddRange(g.Flags);
            Player.Frozen = true;
            var carPos = WorldOrigin.ToUnity(g.VehicleE, g.VehicleN, g.VehicleH + 1.0); Car.Teleport(carPos, Quaternion.Euler(0, g.VehicleYaw, 0)); Car.Fuel01 = g.Fuel; Car.Damage01 = g.VehicleDamage; Car.Body.isKinematic = true;
            if (g.InVehicle) { if (!Player.InVehicle) Player.PlaceInVehicle(Car); } else { var pp = WorldOrigin.ToUnity(g.PlayerE, g.PlayerN, g.PlayerH + 1.0); Player.Teleport(pp); }
            Incidents?.ResetFromSave(g.Incidents.Count > 0 ? g.Incidents[0] : null);
            Playing = true;
        }

        public void NewGame(PoliceCorps corps, int seed)
        {
            Corps = corps; Seed = seed; Career = new CareerState(seed) { CorpsId = corps.Id }; Cases.Clear(); ConsequenceLog.Clear(); Flags.Clear(); Clock = new GameClock { Start = new DateTime(2026, 10, 12, 7, 30, 0), TimeScale = 12f };
        }
    }
}
