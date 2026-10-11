using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SwissPoliceSim.Characters;
using SwissPoliceSim.Common;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Traffic;
using SwissPoliceSim.Rendering;
using SwissPoliceSim.Vehicles;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.Traffic
{
    /// <summary>
    /// Bridges the Unity-free <see cref="TrafficSimulation"/> to the scene: builds the link network from the loaded tiles on a worker thread (rebuilt when the player has driven far),
    /// steps the simulation at 10 Hz with interpolated rendering, pools one visual per simulated vehicle, shows a seated driver for near cars,
    /// and turns collisions with the player's car into crashes.
    /// </summary>
    public sealed class TrafficManager : MonoBehaviour
    {
        public TileStreamer streamer; public Transform viewer; public Atmosphere atmosphere; public int maxVehicles = 140; public bool enabledSim = true;
        public TrafficSimulation Sim { get; private set; } public TrafficNetwork Net { get; private set; }
        public PedestrianManager Pedestrians;
        public event Action<TrafficEvent> Violation; public event Action<TrafficVehicle, float> PlayerCollision;
        public int ViewCount => _views.Count;

        sealed class View
        {
            public TrafficVehicle V; public VehicleVisual Vis; public Rigidbody Rb; public BoxCollider Col; public double PrevE, PrevN, CurE, CurN; public float PrevYaw, CurYaw, Height, PrevH; public HumanVisual Driver; public bool Physics;
            public float Dist; public float Odometer;
        }

        readonly Dictionary<int, View> _views = new Dictionary<int, View>(); readonly List<View> _pool = new List<View>();
        TrafficVehicle _player; Task<TrafficNetwork> _building; double _buildE, _buildN; bool _hasBuild; float _acc, _stepDt = 0.1f, _lastBuildCheck; int _lastTilesBuilt = -1;
        public double PlayerSpeedMs; Vector3 _lastPlayerPos; double _simE, _simN;

        void Start() { _player = new TrafficVehicle { Id = -1, Length = 4.7f, Width = 1.9f, External = true }; }

        void Update()
        {
            if (streamer == null || viewer == null || !WorldData.Ready) return;
            var p = WorldOrigin.ToLv95(viewer.position);
            PollNetworkBuild(p);
            if (Sim == null || !enabledSim) return;
            _acc += Time.deltaTime; int steps = 0;
            while (_acc >= _stepDt && steps < 3) { _acc -= _stepDt; steps++; StepSim(p); }
            if (_acc > _stepDt * 3) _acc = 0;
            UpdateViews((float)(_acc / _stepDt), p);
        }

        // ------------------------------------------------------------------ network
        void PollNetworkBuild(Lv95 p)
        {
            if (_building != null)
            {
                if (!_building.IsCompleted) return;
                if (_building.Status == TaskStatus.RanToCompletion && _building.Result != null)
                {
                    Net = _building.Result; if (Sim == null) { Sim = new TrafficSimulation(Net, new TrafficParams { MaxVehicles = maxVehicles, Seed = 11 }); Sim.Violation += e => Violation?.Invoke(e); Sim.Crash += OnSimCrash; Sim.Despawned += OnDespawned; if (Pedestrians != null) Pedestrians.Begin(Net, streamer); }
                    else Sim.Rebase(Net);
                    _hasBuild = true;
                }
                _building = null;
            }
            if (Time.unscaledTime - _lastBuildCheck < 1.5f) return; _lastBuildCheck = Time.unscaledTime;
            double moved = Math.Sqrt((p.E - _buildE) * (p.E - _buildE) + (p.N - _buildN) * (p.N - _buildN));
            bool need = !_hasBuild ? streamer.IsAreaLoaded(p.E, p.N, 1) : moved > 1100 || (streamer.TilesBuilt != _lastTilesBuilt && moved > 300);
            if (!need) return;
            // snapshot ways/points of the loaded 5x5 around the player on the main thread, build on a worker
            var ways = new List<RoadWay>(); var pts = new List<PointFeature>(); var c = TileGrid.FromLv95(p.E, p.N);
            for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++) if (streamer.TryGetTile(new TileId(c.X + dx, c.Y + dy), out var t)) { ways.AddRange(t.Content.Roads); pts.AddRange(t.Content.Points); }
            if (ways.Count == 0) return;
            _buildE = p.E; _buildN = p.N; _lastTilesBuilt = streamer.TilesBuilt; _building = Task.Run(() => TrafficNetwork.Build(ways, pts));
        }

        // ------------------------------------------------------------------ simulation
        void StepSim(Lv95 p)
        {
            var obs = _obs; obs.Clear(); obs.Add((p.E, p.N));
            float hour = atmosphere != null ? 0f : 12f; hour = HourProvider != null ? HourProvider() : 12f;
            Sim.Maintain(obs, hour);
            var pc = PlayerCarRef;
            if (pc != null) { var f = pc.transform.forward; var v = RbCompat.GetVelocity(pc.Body); Sim.UpdateExternal(_player, p.E, p.N, f.x, f.z, v.magnitude); PlayerSpeedMs = v.magnitude; }
            Sim.SirenActive = pc != null && pc.Siren; if (pc != null) { Sim.SirenE = p.E; Sim.SirenN = p.N; Sim.SirenDirE = pc.transform.forward.x; Sim.SirenDirN = pc.transform.forward.z; }
            Sim.Step(_stepDt);
        }
        readonly List<(double e, double n)> _obs = new List<(double, double)>();
        public Func<float> HourProvider; public PlayerCar PlayerCarRef;

        void OnDespawned(TrafficVehicle v) { Release(v.Id); }

        void OnSimCrash(TrafficVehicle a, TrafficVehicle b)
        {
            foreach (var v in new[] { a, b }) if (_views.TryGetValue(v.Id, out var vw)) { MakeDynamic(vw); }
            CrashHappened?.Invoke(a, b);
        }
        public event Action<TrafficVehicle, TrafficVehicle> CrashHappened;

        // ------------------------------------------------------------------ views
        View Acquire(TrafficVehicle v)
        {
            View vw = null; if (_pool.Count > 0) { vw = _pool[_pool.Count - 1]; _pool.RemoveAt(_pool.Count - 1); }
            string plate = string.IsNullOrEmpty(v.Plate) ? "BE 000000" : v.Plate; var kind = (BodyKind)Mathf.Clamp(v.BodyKind, 0, 4);
            var vis = VehicleFactory.Create(kind, VehicleFactory.CivilianLivery(v.ColorIndex, plate), false, "Traffic " + v.Id);
            vw = new View { V = v, Vis = vis };
            var rb = vis.gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.mass = 1500f; rb.interpolation = RigidbodyInterpolation.None; vw.Rb = rb;
            var col = vis.gameObject.AddComponent<BoxCollider>(); col.size = new Vector3(vis.Model.Width * 0.95f, 1.15f, vis.Model.Length * 0.97f); col.center = new Vector3(0, 0.75f, 0); PhysCompat.MakeSlippery(col); vw.Col = col;
            var relay = vis.gameObject.AddComponent<TrafficCollisionRelay>(); relay.Owner = this; relay.VehicleId = v.Id;
            _views[v.Id] = vw; return vw;
        }

        void Release(int id)
        {
            if (!_views.TryGetValue(id, out var vw)) return; _views.Remove(id);
            if (vw.Driver != null) Destroy(vw.Driver.gameObject);
            if (vw.Vis != null) Destroy(vw.Vis.gameObject);
        }

        void MakeDynamic(View vw) { if (vw.Rb != null) { vw.Rb.isKinematic = false; vw.Physics = true; vw.Rb.collisionDetectionMode = CollisionDetectionMode.Discrete; } }

        public void ReportPlayerCollision(int vehicleId, float relativeSpeed)
        {
            if (!_views.TryGetValue(vehicleId, out var vw) || vw.V == null) return;
            if (relativeSpeed > 2.5f)
            {
                vw.V.Mode = DriverMode.Crashed; vw.V.Hazard = true; vw.V.V = 0; vw.V.TimeInMode = 0; MakeDynamic(vw); PlayerCollision?.Invoke(vw.V, relativeSpeed);
            }
        }

        void UpdateViews(float alpha, Lv95 p)
        {
            var origin = WorldOrigin.Frame; bool night = atmosphere != null && atmosphere.Daylight < 0.45f;
            // create views for vehicles within visual range, remove far ones
            foreach (var v in Sim.Vehicles)
            {
                double dx = v.E - p.E, dz = v.N - p.N; double d = Math.Sqrt(dx * dx + dz * dz);
                bool has = _views.TryGetValue(v.Id, out var vw);
                if (!has) { if (d < 380 && _views.Count < maxVehicles + 20) { vw = Acquire(v); vw.PrevE = vw.CurE = v.E; vw.PrevN = vw.CurN = v.N; vw.PrevYaw = vw.CurYaw = Yaw(v); } else continue; }
                vw.Dist = (float)d;
                if (d > 520) { Release(v.Id); continue; }
                if (vw.Physics) { SyncFromPhysics(vw, v); continue; }
                // new sim state arrived?
                if (vw.CurE != v.E || vw.CurN != v.N) { vw.PrevE = vw.CurE; vw.PrevN = vw.CurN; vw.PrevYaw = vw.CurYaw; vw.PrevH = vw.Height; vw.CurE = v.E; vw.CurN = v.N; vw.CurYaw = Yaw(v); vw.Height = HeightOf(v); }
                double e = vw.PrevE + (vw.CurE - vw.PrevE) * alpha, n = vw.PrevN + (vw.CurN - vw.PrevN) * alpha; float yaw = Mathf.LerpAngle(vw.PrevYaw, vw.CurYaw, alpha), h = vw.PrevH + (vw.Height - vw.PrevH) * alpha;
                if (float.IsNaN(h)) h = vw.Height;
                var pos = new Vector3((float)(e - origin.OriginE), h, (float)(n - origin.OriginN));
                float steer = Mathf.DeltaAngle(vw.PrevYaw, vw.CurYaw) * 3f;
                vw.Vis.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
                vw.Odometer += v.V * Time.deltaTime; vw.Vis.SetWheels(Mathf.Clamp(steer, -28f, 28f), v.V * Time.deltaTime);
                var s = vw.Vis; s.Brake = v.BrakeLights || v.V < 0.3f && v.Mode != DriverMode.Parked; s.Headlights = night; s.IndLeft = v.Indicator < 0; s.IndRight = v.Indicator > 0; s.Hazard = v.Hazard;
                // near cars show their driver
                bool wantDriver = d < 38 && v.Mode != DriverMode.Crashed;
                if (wantDriver && vw.Driver == null) vw.Driver = CreateDriver(vw);
                else if (!wantDriver && vw.Driver != null) { Destroy(vw.Driver.gameObject); vw.Driver = null; }
                if (vw.Col != null) vw.Col.enabled = d < 90;
            }
            // views without a vehicle (removed by the sim)
            _gone.Clear(); foreach (var kv in _views) if (!Sim.Vehicles.Contains(kv.Value.V) && !kv.Value.Physics) _gone.Add(kv.Key);
            foreach (int id in _gone) Release(id);
        }
        readonly List<int> _gone = new List<int>();

        static float Yaw(TrafficVehicle v) => Mathf.Atan2(v.DirE, v.DirN) * Mathf.Rad2Deg;

        float HeightOf(TrafficVehicle v)
        {
            var l = Net.Links[Mathf.Clamp(v.Link, 0, Net.Links.Count - 1)]; float g = streamer.Height(v.E, v.N);
            float h = l.HeightAt(v.S, g); if (float.IsNaN(h)) h = 0; return h + 0.02f;
        }

        void SyncFromPhysics(View vw, TrafficVehicle v)
        {
            var t = vw.Vis.transform; var l = WorldOrigin.ToLv95(t.position); v.E = l.E; v.N = l.N; v.V = RbCompat.GetVelocity(vw.Rb).magnitude;
            v.Hazard = true; vw.Vis.Hazard = true; vw.Vis.Brake = true;
        }

        HumanVisual CreateDriver(View vw)
        {
            var d = vw.V.Driver; var spec = HumanSpec.FromPed(false, false, false, (byte)(vw.V.Id % 12), (byte)(vw.V.Id % 8), (byte)((vw.V.Id / 3) % 6), (byte)((vw.V.Id / 5) % 7), 1.75f, vw.V.Id * 17);
            spec.Female = d.FirstName != null && d.FirstName.Length > 0 && vw.V.Id % 3 == 0; spec.ShortSleeves = false; spec.Backpack = false;
            var go = HumanFactory.Create(spec, "Driver", 1, false); go.transform.SetParent(vw.Vis.transform, false); var m = vw.Vis.Model;
            go.transform.localPosition = new Vector3(-0.36f, 0.32f, m.DriverEye[2] - 0.22f); var hv = go.GetComponent<HumanVisual>(); hv.Pose = PoseKind.Seated; return hv;
        }

        public TrafficVehicle FindVehicle(int id) => Sim?.Find(id);

        public bool TryGetVisual(int id, out VehicleVisual vis) { if (_views.TryGetValue(id, out var v)) { vis = v.Vis; return true; } vis = null; return false; }
    }

    /// <summary>On the traffic car's GameObject: reports collisions with the player's car back to the manager.</summary>
    public sealed class TrafficCollisionRelay : MonoBehaviour
    {
        public TrafficManager Owner; public int VehicleId;
        void OnCollisionEnter(Collision c) { if (c.rigidbody != null && c.rigidbody.GetComponent<PlayerCar>() != null) Owner.ReportPlayerCollision(VehicleId, c.relativeVelocity.magnitude); }
    }
}
