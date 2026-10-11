using System;
using System.Collections.Generic;
using SwissPoliceSim.Characters;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Traffic;
using SwissPoliceSim.Vehicles;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.Traffic
{
    /// <summary>Pedestrian simulation (footpaths, sidewalks, crossings) with pooled visual humans inside the draw radius; cars that hit a pedestrian cause an event.</summary>
    public sealed class PedestrianManager : MonoBehaviour
    {
        public TileStreamer streamer; public Transform viewer; public float drawRadius = 150f; public int maxVisible = 70; public Func<float> HourProvider;
        public PedestrianSim Sim { get; private set; }
        public event Action<Pedestrian, float> PedestrianHit;

        sealed class PView { public Pedestrian P; public HumanVisual H; public GameObject Go; public float Height; }
        readonly Dictionary<int, PView> _views = new Dictionary<int, PView>(); readonly List<(double e, double n)> _obs = new List<(double, double)>();
        float _acc; bool _started; TrafficNetwork _net; float _nextRebuild; double _buildE, _buildN; readonly HashSet<int> _fallen = new HashSet<int>();
        public bool IsStarted => _started;

        /// <summary>Starts the simulation on the footpath network of the tiles around the player.</summary>
        public void Begin(TrafficNetwork net, TileStreamer s) { _net = net; streamer = s; Rebuild(); }

        void Rebuild()
        {
            if (viewer == null || streamer == null) return; var p = WorldOrigin.ToLv95(viewer.position); var c = TileGrid.FromLv95(p.E, p.N);
            var ways = new List<RoadWay>(); var pts = new List<PointFeature>();
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) if (streamer.TryGetTile(new TileId(c.X + dx, c.Y + dy), out var t)) { ways.AddRange(t.Content.Roads); pts.AddRange(t.Content.Points); }
            if (ways.Count == 0) return;
            var old = Sim; Sim = new PedestrianSim(ways, pts, 21) { RoadClear = IsRoadClear, Density = DensityAt }; _started = true; _buildE = p.E; _buildN = p.N;
            foreach (var kv in _views) { if (kv.Value.Go != null) Destroy(kv.Value.Go); } _views.Clear();
        }

        /// <summary>Unsignalised crossings: clear when no moving vehicle is within 22 m (the traffic simulation would otherwise have to stop for pedestrians).</summary>
        bool IsRoadClear(double e, double n)
        {
            var tm = TrafficProvider; if (tm == null || tm.Sim == null) return true;
            foreach (var v in tm.Sim.Vehicles) { double dx = v.E - e, dz = v.N - n; if (dx * dx + dz * dz < 22 * 22 && v.V > 1.5f) return false; }
            if (PlayerCarRef != null) { var pp = WorldOrigin.ToLv95(PlayerCarRef.transform.position); if ((pp.E - e) * (pp.E - e) + (pp.N - n) * (pp.N - n) < 25 * 25 && PlayerCarRef.SpeedKmh > 8f) return false; }
            return true;
        }
        public TrafficManager TrafficProvider; public PlayerCar PlayerCarRef;

        float DensityAt(double e, double n)
        {
            // crowds in dense, built-up areas, few in the countryside: land-use of the 128 raster
            var id = TileGrid.FromLv95(e, n); if (streamer == null || !streamer.TryGetTile(id, out var t) || t.Content.LandUse == null) return 0.4f;
            int x = Mathf.Clamp((int)((e - t.OriginE) / 7.8125), 0, 127), z = Mathf.Clamp((int)((n - t.OriginN) / 7.8125), 0, 127); var cls = (LandUseClass)t.Content.LandUse[z * 128 + x];
            switch (cls) { case LandUseClass.Commercial: case LandUseClass.Plaza: case LandUseClass.Urban: return 1f; case LandUseClass.Residential: return 0.7f; case LandUseClass.Park: case LandUseClass.Sports: return 0.45f; case LandUseClass.Industrial: return 0.25f; default: return 0.12f; }
        }

        void Update()
        {
            if (!_started || Sim == null || viewer == null) return; var p = WorldOrigin.ToLv95(viewer.position);
            double moved = Math.Sqrt((p.E - _buildE) * (p.E - _buildE) + (p.N - _buildN) * (p.N - _buildN)); if (moved > 650 && Time.unscaledTime > _nextRebuild) { _nextRebuild = Time.unscaledTime + 5f; Rebuild(); return; }
            _acc += Time.deltaTime; int steps = 0; while (_acc >= 0.1f && steps < 3) { _acc -= 0.1f; steps++; Sim.Step(0.1f); }
            if (_acc > 0.3f) _acc = 0;
            _obs.Clear(); _obs.Add((p.E, p.N)); if (Time.frameCount % 12 == 0) Sim.Maintain(_obs, HourProvider != null ? HourProvider() : 12f);
            UpdateViews(p);
        }

        void UpdateViews(Lv95 p)
        {
            var origin = WorldOrigin.Frame; int visible = 0;
            foreach (var ped in Sim.Peds)
            {
                double dx = ped.E - p.E, dz = ped.N - p.N; double d2 = dx * dx + dz * dz; bool near = d2 < drawRadius * drawRadius && visible < maxVisible;
                bool has = _views.TryGetValue(ped.Id, out var v);
                if (!near) { if (has) { Destroy(v.Go); _views.Remove(ped.Id); } continue; }
                visible++;
                if (!has) { v = CreateView(ped); _views[ped.Id] = v; }
                float gy = streamer.Height(ped.E, ped.N); if (float.IsNaN(gy)) gy = 0; gy += 0.12f;
                var go = v.Go.transform; var pos = new Vector3((float)(ped.E - origin.OriginE), gy, (float)(ped.N - origin.OriginN));
                if (_fallen.Contains(ped.Id)) { v.H.Pose = PoseKind.Fallen; continue; }
                go.position = pos; if (ped.DirE * ped.DirE + ped.DirN * ped.DirN > 1e-4f) go.rotation = Quaternion.Slerp(go.rotation, Quaternion.LookRotation(new Vector3(ped.DirE, 0, ped.DirN)), 1f - Mathf.Exp(-10f * Time.deltaTime));
                float speed = ped.State == PedState.Walking || ped.State == PedState.Crossing ? ped.Speed : 0f; v.H.Speed = speed;
                v.H.Pose = speed > 0.1f ? (ped.UsesPhone ? PoseKind.Phone : PoseKind.Walk) : (ped.State == PedState.Chatting ? PoseKind.Talk : PoseKind.Idle);
                if (d2 < 9 * 9 && PlayerCarRef != null && !PlayerCarRef.HasDriver == false) CheckHit(ped, v, pos);
            }
            _drop.Clear(); foreach (var kv in _views) if (!Sim.Peds.Contains(kv.Value.P)) _drop.Add(kv.Key); foreach (int id in _drop) { Destroy(_views[id].Go); _views.Remove(id); }
        }
        readonly List<int> _drop = new List<int>();

        void CheckHit(Pedestrian ped, PView v, Vector3 pos)
        {
            var car = PlayerCarRef; var cp = car.transform.position; var rel = pos - cp; rel.y = 0; float speed = car.SpeedKmh;
            if (speed < 8f || _fallen.Contains(ped.Id)) return;
            var local = car.transform.InverseTransformPoint(pos); var m = car.Model;
            if (Mathf.Abs(local.x) < m.Width * 0.5f + 0.25f && local.z > -m.Length * 0.5f && local.z < m.Length * 0.5f + 0.3f && Mathf.Abs(local.y) < 1.2f)
            {
                _fallen.Add(ped.Id); v.H.Pose = PoseKind.Fallen; PedestrianHit?.Invoke(ped, speed);
            }
        }

        PView CreateView(Pedestrian ped)
        {
            var spec = HumanSpec.FromPed(ped.Female, ped.Child, ped.Elderly, ped.Shirt, ped.Trousers, ped.Skin, ped.Hair, ped.Height, ped.Seed);
            var go = HumanFactory.Create(spec, "Ped " + ped.Id, 1, true); var hv = go.GetComponent<HumanVisual>(); hv.Height = ped.Height;
            return new PView { P = ped, H = hv, Go = go, Height = ped.Height };
        }

        public bool TryGetView(int id, out HumanVisual h) { if (_views.TryGetValue(id, out var v)) { h = v.H; return true; } h = null; return false; }

        /// <summary>Nearest visible pedestrian within a cone in front of the viewer (for "address person").</summary>
        public Pedestrian Nearest(Vector3 from, Vector3 forward, float maxDist, float maxAngleDeg)
        {
            Pedestrian best = null; float bd = maxDist; foreach (var kv in _views)
            {
                var pos = kv.Value.Go.transform.position + Vector3.up * 1f; var d = pos - from; float dist = d.magnitude; if (dist > bd) continue; if (Vector3.Angle(forward, d) > maxAngleDeg) continue; bd = dist; best = kv.Value.P;
            }
            return best;
        }
    }
}
