using System.IO;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Import;
using SwissPoliceSim.Navigation;
using SwissPoliceSim.Player;
using SwissPoliceSim.UI;
using SwissPoliceSim.Vehicle;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim
{
    /// <summary>
    /// Phase-1 entry point. Put ONE of these in an otherwise empty scene (menu: Swiss Police ▸ Create Phase-1 Scene does
    /// it for you). Builds the world services, waits for the first terrain tiles, then spawns the patrol car on the
    /// nearest real road to the Bundesplatz in Bern.
    /// </summary>
    public sealed class PilotBootstrap : MonoBehaviour
    {
        public double startLat = 46.94658, startLon = 7.44410;   // Bundesplatz Bern (WGS84)
        public string dataFolder = "SwissWorld";

        TileStreamingManager _streaming;
        FloatingOrigin _origin;
        NavigationService _nav;
        PlayerSession _session;
        Transform _probe;
        bool _spawned;
        Lv95 _start;

        void Awake()
        {
            _start = SwissCoordinateConverter.ToLv95(startLat, startLon);
            string manifestPath = Path.Combine(Application.streamingAssetsPath, dataFolder, "manifest.json");
            if (File.Exists(manifestPath))
            {
                var m = JsonUtility.FromJson<WorldManifest>(File.ReadAllText(manifestPath));
                Debug.Log($"World manifest: {m.name}, tiles X {m.minTileX}..{m.maxTileX}, Y {m.minTileY}..{m.maxTileY}");
            }
            else Debug.LogError($"No baked world data at {manifestPath}. Run 'Swiss Police ▸ Import Geodata…' or Tools/SwissWorldBaker.");

            WorldOrigin.Initialize(_start.E, _start.N);   // Unity (0,0,0) = start point, height axis = metres a.s.l.

            SetupLightingAndCamera(out var camRig);

            // A probe object drives streaming until the player entities exist.
            _probe = new GameObject("StreamingProbe").transform;
            _probe.position = new Vector3(0, 550f, 0);

            var sGo = new GameObject("TileStreaming");
            _streaming = sGo.AddComponent<TileStreamingManager>();
            _streaming.target = _probe; _streaming.dataFolder = dataFolder;

            _origin = new GameObject("FloatingOrigin").AddComponent<FloatingOrigin>();
            _origin.target = _probe;

            _nav = new GameObject("Navigation").AddComponent<NavigationService>();
            _nav.streaming = _streaming; _nav.dataFolder = dataFolder; _nav.vehicleOrPlayer = _probe;

            _session = new GameObject("PlayerSession").AddComponent<PlayerSession>();
            _session.camera = camRig;
            _session.enabled = false;

            var hud = new GameObject("HUD").AddComponent<PilotHud>();
            hud.session = _session; hud.streaming = _streaming; hud.navigation = _nav;
            hud.enabled = false;
            _hud = hud;
        }

        PilotHud _hud;

        void SetupLightingAndCamera(out CameraRig rig)
        {
            var sun = FindFirstObjectByType<Light>();
            if (sun == null || sun.type != LightType.Directional)
            {
                var l = new GameObject("Sun"); sun = l.AddComponent<Light>(); sun.type = LightType.Directional;
            }
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.intensity = 1.2f; sun.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.65f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.48f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.25f, 0.22f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 1500f; RenderSettings.fogEndDistance = 3200f;
            RenderSettings.fogColor = new Color(0.72f, 0.80f, 0.90f);

            var cam = Camera.main;
            if (cam == null)
            {
                var cg = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = cg.AddComponent<Camera>(); cg.AddComponent<AudioListener>();
            }
            cam.nearClipPlane = 0.15f; cam.farClipPlane = 3500f; cam.fieldOfView = 70f;
            cam.clearFlags = CameraClearFlags.Skybox;
            if (!cam.gameObject.TryGetComponent(out rig)) rig = cam.gameObject.AddComponent<CameraRig>();
            rig.cam = cam;
        }

        void Update()
        {
            if (_spawned || !_streaming.InitialLoadComplete || !_nav.IsReady) return;
            Spawn();
        }

        void Spawn()
        {
            _spawned = true;
            // Snap the start to the nearest real road node; face along its first outgoing edge.
            double e = _start.E, n = _start.N; Vector3 heading = Vector3.forward;
            int node = _nav.Graph.NearestNode(e, n, 300);
            if (node >= 0)
            {
                _nav.Graph.GetNodePosition(node, out e, out n);
                if (_nav.Graph.TryGetNodeHeading(node, out double de, out double dn)) heading = new Vector3((float)de, 0, (float)dn);
            }
            if (!_streaming.TryGetGroundHeight(e, n, out float ground)) ground = 540f;

            var pos = WorldOrigin.ToUnity(e, n, ground + 1.0);
            var car = PoliceVehicleFactory.Create(pos, Quaternion.LookRotation(heading, Vector3.up));
            var foot = OnFootController.Create(pos + Vector3.right * 3f);
            foot.cameraYawSource = Camera.main.transform;

            _session.vehicle = car; _session.foot = foot;
            _session.lights = car.GetComponent<VehicleLights>();
            _session.camera.CockpitAnchor = car.transform.Find("CockpitCamAnchor");
            _session.camera.HoodAnchor = car.transform.Find("HoodCamAnchor");
            _session.StartInVehicle();
            _session.camera.SnapTo(car.transform, car.transform.eulerAngles.y);
            _session.enabled = true; _hud.enabled = true;

            // from here on the active player entity drives streaming, origin shifts and navigation
            var follow = _session.gameObject.AddComponent<ActiveTargetFollower>();
            follow.session = _session; follow.streaming = _streaming; follow.origin = _origin; follow.nav = _nav;
            Destroy(_probe.gameObject);
            Debug.Log($"Spawned patrol car at LV95 {e:F0}/{n:F0}, ground {ground:F1} m");
        }

        sealed class ActiveTargetFollower : MonoBehaviour
        {
            public PlayerSession session; public TileStreamingManager streaming; public FloatingOrigin origin; public NavigationService nav;
            void LateUpdate()
            {
                var t = session.Active;
                streaming.target = t; origin.target = t; nav.vehicleOrPlayer = t;
            }
        }
    }
}
