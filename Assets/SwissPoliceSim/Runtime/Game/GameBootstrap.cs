using System;
using System.IO;
using SwissPoliceSim.Audio;
using SwissPoliceSim.Core.Game;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Legal;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Police;
using SwissPoliceSim.Core.Traffic;
using SwissPoliceSim.Player;
using SwissPoliceSim.Rendering;
using SwissPoliceSim.Traffic;
using SwissPoliceSim.UI;
using SwissPoliceSim.Vehicles;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.Game
{
    public enum BootPhase { Loading, Menu, Starting, Playing, GameOver }

    /// <summary>Entry point: creates the whole scene at runtime (no hand-built scene needed), loads the world data and law texts, and starts or resumes a game.</summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }
        public BootPhase Phase = BootPhase.Loading; public string Error = "";
        public GameSession Session; public Camera Cam; public Transform Focus; public UiRoot Ui;
        public VoiceService Voice; public LlmService Llm;
        public PoliceCorps SelectedCorps; public int SelectedSeed = 1; public int PendingSlot = -1;
        TrafficNetwork _pedNet; double _startE, _startN; bool _placing; float _placeStart;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto() { if (FindObjectOfType<GameBootstrap>() == null) new GameObject("SwissPoliceSim").AddComponent<GameBootstrap>(); }

        void Awake()
        {
            Instance = this; DontDestroyOnLoad(gameObject); Application.targetFrameRate = 144; QualitySettings.vSyncCount = 1; Application.runInBackground = true;
            var camGo = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera"); camGo.tag = "MainCamera"; Cam = camGo.GetComponent<Camera>() ?? camGo.AddComponent<Camera>(); if (camGo.GetComponent<AudioListener>() == null) camGo.AddComponent<AudioListener>();
            Cam.depth = 0; Cam.transform.position = new Vector3(0, 30, 0); Cam.backgroundColor = new Color(0.45f, 0.58f, 0.78f);
            Focus = new GameObject("Focus").transform; Focus.SetParent(transform);
            Ui = gameObject.AddComponent<UiRoot>(); gameObject.AddComponent<MapService>();
            SelectedCorps = PoliceCatalog.ById("KAPO_BE") ?? PoliceCatalog.All[0];
            Phase = BootPhase.Loading; WorldData.LoadAsync();
        }

        void Update()
        {
            if (Phase == BootPhase.Loading) { if (WorldData.Ready) Phase = BootPhase.Menu; else if (WorldData.LoadError != null) Error = WorldData.LoadError; }
            if (Session == null) return;
            // keep one reference point for everything that streams around "the player"
            if (Session.Player != null) Focus.position = Session.Player.WorldPosition;
            if (Session.Traffic != null && Session.Traffic.Net != null)
            {
                if (_pedNet != Session.Traffic.Net && Session.Peds != null) { _pedNet = Session.Traffic.Net; Session.Peds.Begin(_pedNet, Session.Streamer); }
                if (Session.Dispatch == null || DispatchNet != Session.Traffic.Net) { Session.Dispatch = new DispatchCenter(Session.Traffic.Net, WorldData.Admin, Session.Seed); DispatchNet = Session.Traffic.Net; }
            }
            if (_placing) TryPlace();
            if (Phase == BootPhase.Playing && Session.Career != null && Session.Career.Suspended && Session.Clock.ElapsedGameSeconds >= Session.Career.SuspendedUntil) { Session.Career.Suspended = false; Notifier.Good("Die Suspendierung ist beendet."); }
        }
        TrafficNetwork DispatchNet;

        // ------------------------------------------------------------------ start / load
        public void StartNew(PoliceCorps corps, int seed) { SelectedCorps = corps; SelectedSeed = seed; PendingSlot = -1; BeginStart(); }
        public void StartFromSlot(int slot) { PendingSlot = slot; BeginStart(); }

        void BeginStart()
        {
            Teardown(); Phase = BootPhase.Starting; Error = "";
            try
            {
                GameSave save = null; if (PendingSlot > 0) save = GameSave.LoadFromFile(GameSession.SlotPath(PendingSlot));
                var corps = save != null ? (PoliceCatalog.ById(save.CorpsId) ?? SelectedCorps) : SelectedCorps; SelectedCorps = corps;
                if (save != null) { _startE = save.InVehicle ? save.VehicleE : save.PlayerE; _startN = save.InVehicle ? save.VehicleN : save.PlayerN; }
                else FindStart(corps, out _startE, out _startN);
                WorldOrigin.Initialize(Math.Floor(_startE / 500) * 500, Math.Floor(_startN / 500) * 500);
                Build(corps, save != null ? save.Seed : SelectedSeed, save);
            }
            catch (Exception ex) { Error = ex.Message; Debug.LogError(ex); Phase = BootPhase.Menu; }
        }

        void FindStart(PoliceCorps corps, out double e, out double n)
        {
            e = WorldData.Manifest != null && WorldData.Manifest.centerE > 0 ? WorldData.Manifest.centerE : 2600000; n = WorldData.Manifest != null && WorldData.Manifest.centerN > 0 ? WorldData.Manifest.centerN : 1200000;
            var pois = WorldData.Pois; if (pois == null) return; double best = double.MaxValue; Core.Features.PoiFeature pick = null;
            foreach (var p in pois.Within(e, n, 20000, Core.Features.PoiKind.Police)) { var st = WorldData.Admin != null ? JurisdictionManager.Evaluate(WorldData.Admin, corps, p.E, p.N) : null; double d = (p.E - e) * (p.E - e) + (p.N - n) * (p.N - n) - (st != null && st.InOwnTerritory ? 1e9 : 0); if (d < best) { best = d; pick = p; } }
            if (pick != null) { e = pick.E; n = pick.N; }
        }

        void Teardown()
        {
            if (Session != null) { Destroy(Session.gameObject); Session = null; }
            foreach (var n in new[] { "World", "Player" }) { var go = GameObject.Find(n); if (go != null) Destroy(go); }
            Windows.Close(); UiState.OpenWindows = 0; UiState.Apply(); Notifier.Active.Clear(); _placing = false; _pedNet = null; DispatchNet = null;
        }

        void Build(PoliceCorps corps, int seed, GameSave save)
        {
            string law = Path.Combine(Application.streamingAssetsPath, "Law");
            var worldGo = new GameObject("World"); var gameGo = new GameObject("Game"); var s = gameGo.AddComponent<GameSession>(); Session = s; s.transform.SetParent(transform);
            s.Laws = LawLibrary.LoadDirectory(law); string obvPath = Path.Combine(law, "SR_314.11.txt"); s.Obv = File.Exists(obvPath) ? ObvCatalog.Parse(File.ReadAllText(obvPath)) : new ObvCatalog();
            s.NewGame(corps, seed);
            // atmosphere & world rendering
            s.Atmos = worldGo.AddComponent<Atmosphere>(); s.Atmos.cam = Cam; s.Atmos.viewer = Focus; s.Atmos.weatherSeed = seed; s.Atmos.localTime = () => s.Now;
            s.Streamer = worldGo.AddComponent<TileStreamer>(); s.Streamer.focus = Focus;
            var trees = worldGo.AddComponent<TreeRenderer>(); trees.streamer = s.Streamer; trees.viewer = Focus; var macro = worldGo.AddComponent<MacroRing>(); macro.viewer = Focus;
            var fo = worldGo.AddComponent<FloatingOrigin>(); fo.target = Focus;
            // car & player (placed properly once the ground exists)
            var pos = WorldOrigin.ToUnity(_startE, _startN, 600); var liv = CarLivery.PatrolCar(); liv.Plate = Plate(corps);
            s.Car = PlayerCar.Create(BodyKind.Wagon, liv, pos + Vector3.right * 4f, Quaternion.identity); s.Car.Body.isKinematic = true;
            var pg = new GameObject("Player"); pg.layer = 0; pg.AddComponent<CharacterController>(); s.Player = pg.AddComponent<PlayerController>(); s.Player.Car = s.Car; s.Player.Streamer = s.Streamer; pg.transform.position = pos;
            s.Player.Init(HumanSpec.Officer(seed % 2 == 0, seed, false)); s.Player.Frozen = true;
            s.Rig = Cam.gameObject.GetComponent<CameraRig>() ?? Cam.gameObject.AddComponent<CameraRig>(); s.Rig.Cam = Cam; s.Rig.Player = s.Player; s.Player.Rig = s.Rig;
            // traffic
            s.Traffic = worldGo.AddComponent<TrafficManager>(); s.Traffic.streamer = s.Streamer; s.Traffic.viewer = Focus; s.Traffic.atmosphere = s.Atmos; s.Traffic.HourProvider = () => s.Hour; s.Traffic.PlayerCarRef = s.Car;
            s.Peds = worldGo.AddComponent<PedestrianManager>(); s.Peds.streamer = s.Streamer; s.Peds.viewer = Focus; s.Peds.HourProvider = () => s.Hour; s.Peds.TrafficProvider = s.Traffic; s.Peds.PlayerCarRef = s.Car; s.Traffic.Pedestrians = s.Peds;
            // game systems
            s.Nav = gameGo.AddComponent<NavigationService>(); s.Nav.viewer = Focus; s.Incidents = gameGo.AddComponent<IncidentController>(); s.Enforcement = gameGo.AddComponent<EnforcementController>(); s.Encounters = gameGo.AddComponent<EncounterController>(); s.Pursuit = gameGo.AddComponent<PursuitController>();
            Voice = gameGo.AddComponent<VoiceService>(); Llm = gameGo.AddComponent<LlmService>(); Voice.TtsEnabled = VoiceService.Supported; s.Encounters.Llm = Llm; Windows.Voice = Voice; Windows.Llm = Llm;
            s.GameOver += () => { Phase = BootPhase.GameOver; Windows.Close(); UiState.Open(); };
            _placing = true; _placeStart = Time.unscaledTime; _save = save;
        }
        GameSave _save;

        static string Plate(PoliceCorps c) { string ab = c.CantonId >= 0 && c.CantonId < AdminBoundaries.CantonAbbr.Length ? AdminBoundaries.CantonAbbr[c.CantonId] : "BE"; return ab + " " + (100000 + new System.Random(c.Id.GetHashCode()).Next(899999)); }

        void TryPlace()
        {
            var s = Session; if (s == null) { _placing = false; return; }
            if (!s.Streamer.IsAreaLoaded(_startE, _startN, 1)) return;
            float h = s.Streamer.Height(_startE, _startN); if (float.IsNaN(h)) return;
            _placing = false;
            if (_save != null) { s.Seed = _save.Seed; s.Player.Frozen = true; s.ApplySave(_save); }
            else
            {
                var forward = Quaternion.Euler(0, 0, 0); var carPos = WorldOrigin.ToUnity(_startE + 6, _startN, h + 0.6); s.Car.Teleport(carPos, forward); s.Car.Body.isKinematic = true;
                s.Player.Teleport(WorldOrigin.ToUnity(_startE, _startN, h + 1.0)); s.Player.Frozen = true; s.Playing = true;
            }
            s.Incidents.Init(s.Seed); Phase = BootPhase.Playing; s.Playing = true; s.Paused = false; UiState.Apply();
            Notifier.Radio($"Zentrale: {(s.Dispatch != null ? s.Dispatch.CallSign : "Patrouille")} – Dienstbeginn. Viel Erfolg.");
        }

        public void ToMenu() { Time.timeScale = 1f; Teardown(); Phase = BootPhase.Menu; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }
}
