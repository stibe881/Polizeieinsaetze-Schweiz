using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.Game
{
    /// <summary>Route planning on the hierarchical router (worker thread), progress tracking along the route, turn-by-turn instructions and automatic re-routing.</summary>
    public sealed class NavigationService : MonoBehaviour
    {
        public Transform viewer;
        public RoutePath Route { get; private set; } public List<Instruction> Instructions { get; private set; } = new List<Instruction>();
        public string DestinationName { get; private set; } = ""; public double DestE, DestN; public bool Active => Route != null;
        public bool Computing { get; private set; } public string Error { get; private set; }
        public double AlongM, RemainingM, TotalM, DistToNext; public Instruction Next; public float EtaSeconds; public double OffRouteM;
        public event Action<RoutePath> RouteChanged; public event Action Arrived;
        double[] _cum; int _idx; Task<RoutePath> _task; float _offTime, _nextReroute; double _avgSpeed = 13; Vector3 _lastPos; bool _arrivedFired;

        public void SetDestination(double e, double n, string name)
        {
            DestE = e; DestN = n; DestinationName = name ?? ""; Error = null; _arrivedFired = false; StartRoute();
        }

        public void Clear() { Route = null; Instructions = new List<Instruction>(); DestinationName = ""; Next = null; RemainingM = TotalM = 0; _cum = null; _task = null; Computing = false; RouteChanged?.Invoke(null); }

        void StartRoute()
        {
            if (viewer == null || WorldData.Router == null) return; var p = WorldOrigin.ToLv95(viewer.position); double fe = p.E, fn = p.N, te = DestE, tn = DestN; var router = WorldData.Router;
            Computing = true; _task = Task.Run(() => router.Route(fe, fn, te, tn));
        }

        void Update()
        {
            if (_task != null && _task.IsCompleted)
            {
                Computing = false;
                if (_task.Status == TaskStatus.RanToCompletion && _task.Result != null) SetRoute(_task.Result);
                else { Error = "Keine Route gefunden."; Notifier.Warn("Navigation: keine Route zum Ziel gefunden."); Route = null; }
                _task = null;
            }
            if (Route == null || viewer == null) return;
            var p = WorldOrigin.ToLv95(viewer.position); Progress(p.E, p.N);
            if (Time.deltaTime > 0) { double sp = (viewer.position - _lastPos).magnitude / Time.deltaTime; if (sp < 80) _avgSpeed = Math.Max(5, _avgSpeed * 0.995 + sp * 0.005); } _lastPos = viewer.position;
            EtaSeconds = (float)(RemainingM / Math.Max(8, _avgSpeed));
            if (OffRouteM > 60) { _offTime += Time.deltaTime; if (_offTime > 3f && Time.unscaledTime > _nextReroute && !Computing) { _nextReroute = Time.unscaledTime + 8f; _offTime = 0; StartRoute(); } } else _offTime = 0;
            if (RemainingM < 30 && !_arrivedFired) { _arrivedFired = true; Arrived?.Invoke(); }
        }

        void SetRoute(RoutePath r)
        {
            Route = r; Instructions = NavigationGuide.Build(r); _cum = new double[r.Count]; for (int i = 1; i < r.Count; i++) { double de = r.E[i] - r.E[i - 1], dn = r.N[i] - r.N[i - 1]; _cum[i] = _cum[i - 1] + Math.Sqrt(de * de + dn * dn); }
            TotalM = _cum[r.Count - 1]; _idx = 0; AlongM = 0; RemainingM = TotalM; RouteChanged?.Invoke(r);
        }

        void Progress(double e, double n)
        {
            if (_cum == null) return; double best = double.MaxValue; int bi = _idx; double bt = 0;
            int end = Math.Min(Route.Count - 1, _idx + 80);
            for (int i = Math.Max(0, _idx - 2); i < end; i++)
            {
                double ax = Route.E[i], ay = Route.N[i], bx = Route.E[i + 1], by = Route.N[i + 1], dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy; if (l2 < 1e-9) continue;
                double t = Math.Max(0, Math.Min(1, ((e - ax) * dx + (n - ay) * dy) / l2)); double qx = ax + dx * t - e, qy = ay + dy * t - n, d = qx * qx + qy * qy;
                if (d < best) { best = d; bi = i; bt = t; }
            }
            OffRouteM = Math.Sqrt(best); if (OffRouteM < 40) _idx = bi;
            AlongM = _cum[bi] + (_cum[bi + 1] - _cum[bi]) * bt; RemainingM = Math.Max(0, TotalM - AlongM);
            Next = NavigationGuide.Next(Instructions, AlongM, out DistToNext);
        }
    }
}
