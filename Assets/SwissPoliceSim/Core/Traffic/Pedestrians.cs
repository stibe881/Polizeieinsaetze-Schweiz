using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Roads;

namespace SwissPoliceSim.Core.Traffic
{
    public enum PedState : byte { Walking, Waiting, Crossing, Standing, Chatting }

    public sealed class Pedestrian
    {
        public int Id, Seed; public int Path; public float S; public sbyte Dir = 1;     // position along WalkPath, direction of travel
        public double E, N; public float DirE, DirN; public float Speed = 1.35f;
        public PedState State; public float StateTime, WaitUntil;
        public bool Female, UsesPhone, Elderly, Child; public byte Shirt, Trousers, Skin, Hair; public float Height = 1.75f;
        public bool IsCrossing => State == PedState.Crossing;
    }

    sealed class WalkPath
    {
        public double[] E, N; public float[] Cum; public float Length; public bool IsCrossing; public bool Sidewalk; public bool Signalised; public float SignalOffset; public int SignalGroup;
        public readonly List<Junction> Junctions = new List<Junction>();
        public void PointAt(float s, out double e, out double n, out float dx, out float dz)
        {
            s = Math.Max(0, Math.Min(Length, s)); int i = 0; while (i < Cum.Length - 2 && Cum[i + 1] < s) i++;
            float seg = Math.Max(1e-4f, Cum[i + 1] - Cum[i]); float t = (s - Cum[i]) / seg;
            e = E[i] + (E[i + 1] - E[i]) * t; n = N[i] + (N[i + 1] - N[i]) * t; dx = (float)((E[i + 1] - E[i]) / seg); dz = (float)((N[i + 1] - N[i]) / seg);
        }
    }

    /// <summary>A place on a sidewalk path where a crossing path starts (S along the sidewalk, target path and the end of that path).</summary>
    sealed class Junction { public float S; public int Target; public bool AtStart; }

    /// <summary>
    /// Pedestrians on the real footpath network: footways/paths from OSM plus generated sidewalks along urban roads (same rule as the road mesher),
    /// crossings at OSM crossing nodes (signalised ones obey <see cref="SignalModel"/>, others wait for a gap), spawn/despawn around observers.
    /// </summary>
    public sealed class PedestrianSim
    {
        readonly List<WalkPath> _paths = new List<WalkPath>(); readonly Random _rnd;
        public readonly List<Pedestrian> Peds = new List<Pedestrian>(); int _next = 1;
        public double Time;
        public int MaxPedestrians = 140; public float SpawnMin = 30, SpawnMax = 190, DespawnRadius = 300;
        /// <summary>Called for unsignalised crossings: true if no vehicle is close to (e,n). Default: always clear after 2.5 s.</summary>
        public Func<double, double, bool> RoadClear = (e, n) => true;
        /// <summary>Population density 0..1 at a point (e.g. from land use); default uniform.</summary>
        public Func<double, double, float> Density = (e, n) => 1f;
        readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>(); const double Cell = 100;
        static long CellKey(int cx, int cy) => ((long)cx << 32) ^ (uint)cy;
        void Index(int path)
        {
            var p = _paths[path]; var seen = new HashSet<long>();
            for (int i = 0; i < p.E.Length; i++) { long k = CellKey((int)Math.Floor(p.E[i] / Cell), (int)Math.Floor(p.N[i] / Cell)); if (seen.Add(k)) { if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new List<int>(); l.Add(path); } }
        }
        public int PathCount => _paths.Count;
        public int CrossingPathCount { get { int c = 0; foreach (var p in _paths) if (p.IsCrossing) c++; return c; } }

        public PedestrianSim(IList<RoadWay> ways, IList<PointFeature> points, int seed = 5)
        {
            _rnd = new Random(seed);
            var roadsForCrossings = new List<RoadWay>();
            foreach (var w in ways)
            {
                if (w.PointCount < 2) continue;
                if (w.Class >= RoadClass.Pedestrian && !w.Tunnel) AddPath(w.E, w.N, false, false);
                else if (w.IsVehicular && !w.Tunnel)
                {
                    roadsForCrossings.Add(w);
                    bool urban = w.SpeedBasis == SpeedSource.LegalUrban || w.SpeedBasis == SpeedSource.Zone || (w.SpeedBasis == SpeedSource.Tagged && w.SpeedKmh <= 50);
                    bool sw = urban && w.Class >= RoadClass.Primary && w.Class <= RoadClass.LivingStreet && w.Class != RoadClass.Service && w.Class != RoadClass.PrimaryLink && w.Class != RoadClass.SecondaryLink && w.Class != RoadClass.TertiaryLink && w.CarriagewayWidth() > 4.2f;
                    if (sw) { float off = w.VisualWidth() * 0.5f + 0.95f; var l = Offset(w, off); var r = Offset(w, -off); if (l != null) AddPath(l.Value.e, l.Value.n, true, false); if (r != null) AddPath(r.Value.e, r.Value.n, true, false); }
                }
            }
            // crossings
            var signals = new List<PointFeature>(); foreach (var p in points) if (p.Kind == PointKind.TrafficSignals) signals.Add(p);
            foreach (var p in points)
            {
                if (p.Kind != PointKind.Crossing) continue;
                RoadWay best = null; int bi = 0; double bd = 14 * 14;
                foreach (var w in roadsForCrossings) for (int i = 0; i + 1 < w.PointCount; i++)
                {
                    double d = SegDist(p.E, p.N, w.E[i], w.N[i], w.E[i + 1], w.N[i + 1]); if (d * d < bd) { bd = d * d; best = w; bi = i; }
                }
                if (best == null) continue;
                double dx = best.E[bi + 1] - best.E[bi], dz = best.N[bi + 1] - best.N[bi]; double l = Math.Sqrt(dx * dx + dz * dz); if (l < 0.5) continue; dx /= l; dz /= l;
                double nx = -dz, nz = dx, half = best.VisualWidth() * 0.5f + 0.95f;
                var ce = new[] { p.E + nx * half, p.E - nx * half }; var cn = new[] { p.N + nz * half, p.N - nz * half };
                // both ends must land on a sidewalk: snap the crossing to those exact points so pedestrians never jump
                int sp0 = NearestSidewalk(ce[0], cn[0], -1, out float s0, 3.0), sp1 = NearestSidewalk(ce[1], cn[1], -1, out float s1, 3.0);
                if (sp0 < 0 || sp1 < 0) continue;
                _paths[sp0].PointAt(s0, out ce[0], out cn[0], out _, out _); _paths[sp1].PointAt(s1, out ce[1], out cn[1], out _, out _);
                int ci = AddPath(ce, cn, false, true); if (ci < 0) continue;
                var cp = _paths[ci];
                foreach (var s in signals) { double d2 = (s.E - p.E) * (s.E - p.E) + (s.N - p.N) * (s.N - p.N); if (d2 < 30 * 30) { cp.Signalised = true; cp.SignalOffset = (float)(((long)(s.OsmId * 7) % 60 + 60) % 60); cp.SignalGroup = SignalModel.GroupOf(Math.Atan2(dx, dz)); break; } }
                _paths[sp0].Junctions.Add(new Junction { S = s0, Target = ci, AtStart = true });
                _paths[sp1].Junctions.Add(new Junction { S = s1, Target = ci, AtStart = false });
            }
        }

        static double SegDist(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy; double t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / l2));
            double qx = ax + dx * t - px, qy = ay + dy * t - py; return Math.Sqrt(qx * qx + qy * qy);
        }

        int NearestSidewalk(double e, double n, int exclude, out float s, double maxDist)
        {
            int best = -1; s = 0; double bd = maxDist * maxDist;
            for (int k = 0; k < _paths.Count; k++)
            {
                var p = _paths[k]; if (!p.Sidewalk || k == exclude) continue;
                for (int i = 0; i + 1 < p.E.Length; i++)
                {
                    double dx = p.E[i + 1] - p.E[i], dy = p.N[i + 1] - p.N[i], l2 = dx * dx + dy * dy; if (l2 < 1e-9) continue;
                    double t = Math.Max(0, Math.Min(1, ((e - p.E[i]) * dx + (n - p.N[i]) * dy) / l2)); double qx = p.E[i] + dx * t - e, qy = p.N[i] + dy * t - n, d = qx * qx + qy * qy;
                    if (d < bd) { bd = d; best = k; s = p.Cum[i] + (float)(t * Math.Sqrt(l2)); }
                }
            }
            return best;
        }

        static (double[] e, double[] n)? Offset(RoadWay w, float d)
        {
            int c = w.PointCount; var e = new double[c]; var n = new double[c];
            for (int i = 0; i < c; i++)
            {
                double ax = 0, ay = 0;
                if (i > 0) { double dx = w.E[i] - w.E[i - 1], dy = w.N[i] - w.N[i - 1], l = Math.Sqrt(dx * dx + dy * dy); if (l > 1e-6) { ax += -dy / l; ay += dx / l; } }
                if (i < c - 1) { double dx = w.E[i + 1] - w.E[i], dy = w.N[i + 1] - w.N[i], l = Math.Sqrt(dx * dx + dy * dy); if (l > 1e-6) { ax += -dy / l; ay += dx / l; } }
                double ll = Math.Sqrt(ax * ax + ay * ay); if (ll < 1e-6) return null; e[i] = w.E[i] + ax / ll * d; n[i] = w.N[i] + ay / ll * d;
            }
            return (e, n);
        }

        int AddPath(double[] e, double[] n, bool sidewalk, bool crossing)
        {
            var p = new WalkPath { E = (double[])e.Clone(), N = (double[])n.Clone(), Sidewalk = sidewalk, IsCrossing = crossing }; p.Cum = new float[e.Length];
            for (int i = 1; i < e.Length; i++) p.Cum[i] = p.Cum[i - 1] + (float)Math.Sqrt((e[i] - e[i - 1]) * (e[i] - e[i - 1]) + (n[i] - n[i - 1]) * (n[i] - n[i - 1]));
            p.Length = p.Cum[e.Length - 1]; if (p.Length < (crossing ? 2f : 6f)) return -1;
            _paths.Add(p); Index(_paths.Count - 1); return _paths.Count - 1;
        }

        // ------------------------------------------------------------------ simulation
        public void Maintain(IList<(double e, double n)> observers, float hourOfDay)
        {
            for (int i = Peds.Count - 1; i >= 0; i--)
            {
                var p = Peds[i]; double dmin = double.MaxValue; foreach (var o in observers) dmin = Math.Min(dmin, Math.Sqrt((p.E - o.e) * (p.E - o.e) + (p.N - o.n) * (p.N - o.n)));
                if (dmin > DespawnRadius && p.State != PedState.Crossing) Peds.RemoveAt(i);
            }
            float hourFactor = hourOfDay < 5.5f ? 0.08f : hourOfDay < 7 ? 0.35f : hourOfDay < 9 ? 0.9f : hourOfDay < 11.5f ? 0.65f : hourOfDay < 14 ? 1.0f : hourOfDay < 17 ? 0.75f : hourOfDay < 19 ? 1.0f : hourOfDay < 22 ? 0.5f : 0.15f;
            if (observers.Count == 0 || _paths.Count == 0) return;
            for (int attempt = 0; attempt < 24 && Peds.Count < MaxPedestrians * hourFactor; attempt++)
            {
                var o = observers[_rnd.Next(observers.Count)];
                int cx = (int)Math.Floor(o.e / Cell) + _rnd.Next(-2, 3), cy = (int)Math.Floor(o.n / Cell) + _rnd.Next(-2, 3);
                if (!_grid.TryGetValue(CellKey(cx, cy), out var cand) || cand.Count == 0) continue;
                int k = cand[_rnd.Next(cand.Count)]; var path = _paths[k]; if (path.IsCrossing) continue;
                float s = (float)_rnd.NextDouble() * path.Length; path.PointAt(s, out double e, out double n, out _, out _);
                double d = Math.Sqrt((e - o.e) * (e - o.e) + (n - o.n) * (n - o.n)); if (d < SpawnMin || d > SpawnMax) continue;
                if (_rnd.NextDouble() > Density(e, n)) continue;
                SpawnAt(k, s);
            }
        }

        public Pedestrian SpawnAt(int path, float s)
        {
            var r = _rnd; var p = new Pedestrian { Id = _next++, Seed = r.Next(), Path = path, S = s, Dir = (sbyte)(r.Next(2) == 0 ? 1 : -1) };
            double x = r.NextDouble(); p.Child = x < 0.07; p.Elderly = !p.Child && x < 0.22; p.Female = r.Next(2) == 0;
            p.Height = p.Child ? 1.1f + (float)r.NextDouble() * 0.3f : (p.Female ? 1.58f : 1.70f) + (float)r.NextDouble() * 0.2f;
            p.Speed = p.Child ? 1.15f : p.Elderly ? 0.95f + (float)r.NextDouble() * 0.2f : 1.25f + (float)r.NextDouble() * 0.35f;
            p.UsesPhone = !p.Child && !p.Elderly && r.NextDouble() < 0.18; if (p.UsesPhone) p.Speed *= 0.85f;
            p.Shirt = (byte)r.Next(12); p.Trousers = (byte)r.Next(8); p.Skin = (byte)r.Next(6); p.Hair = (byte)r.Next(7);
            _paths[path].PointAt(s, out p.E, out p.N, out p.DirE, out p.DirN); if (p.Dir < 0) { p.DirE = -p.DirE; p.DirN = -p.DirN; }
            if (r.NextDouble() < 0.06) { p.State = PedState.Standing; p.WaitUntil = (float)(Time + 5 + r.NextDouble() * 40); }
            Peds.Add(p); return p;
        }

        public void Step(float dt)
        {
            Time += dt;
            foreach (var p in Peds)
            {
                var path = _paths[p.Path]; p.StateTime += dt;
                switch (p.State)
                {
                    case PedState.Standing: case PedState.Chatting: if (Time >= p.WaitUntil) { p.State = PedState.Walking; p.StateTime = 0; } continue;
                    case PedState.Waiting:
                    {
                        bool go; if (path.Signalised) go = SignalModel.State(Time, path.SignalOffset, path.SignalGroup) == SignalState.Red && p.StateTime > 1f; else go = p.StateTime > 2.5f && RoadClear(p.E, p.N);
                        if (go || p.StateTime > 70f) { p.State = PedState.Crossing; p.StateTime = 0; } else continue; break;
                    }
                }
                float move = p.Speed * dt * (p.State == PedState.Crossing ? 1.15f : 1f);
                float prev = p.S; p.S += move * p.Dir;
                // junctions passed this step → maybe turn onto a crossing
                if (p.State == PedState.Walking && path.Junctions.Count > 0)
                    foreach (var j in path.Junctions)
                    {
                        if ((prev - j.S) * (p.S - j.S) <= 0 && _rnd.NextDouble() < 0.28)
                        {
                            var t = _paths[j.Target]; p.Path = j.Target; p.S = j.AtStart ? 0 : t.Length; p.Dir = (sbyte)(j.AtStart ? 1 : -1); p.State = PedState.Waiting; p.StateTime = 0;
                            t.PointAt(p.S, out p.E, out p.N, out _, out _); path = t; break;
                        }
                    }
                if (p.State == PedState.Waiting) continue;
                if (p.S >= path.Length || p.S <= 0)
                {
                    if (path.IsCrossing)
                    {   // arrive on the opposite sidewalk: continue on the sidewalk path that has this crossing as a junction
                        int from = p.Path; bool atEnd = p.S >= path.Length; int sw = -1; float sws = 0;
                        for (int k = 0; k < _paths.Count && sw < 0; k++) foreach (var j in _paths[k].Junctions) if (j.Target == from && j.AtStart == !atEnd) { sw = k; sws = j.S; break; }
                        if (sw >= 0) { p.Path = sw; p.S = sws; p.Dir = (sbyte)(_rnd.Next(2) == 0 ? 1 : -1); p.State = PedState.Walking; p.StateTime = 0; path = _paths[sw]; }
                        else { p.S = Math.Max(0, Math.Min(path.Length, p.S)); p.Dir = (sbyte)-p.Dir; p.State = PedState.Walking; }
                    }
                    else
                    {
                        if (_rnd.NextDouble() < 0.5) { p.Dir = (sbyte)-p.Dir; p.S = Math.Max(0, Math.Min(path.Length, p.S)); } else { p.S = Math.Max(0, Math.Min(path.Length, p.S)); p.State = PedState.Standing; p.WaitUntil = (float)(Time + 3 + _rnd.NextDouble() * 15); p.Dir = (sbyte)-p.Dir; }
                    }
                }
                path.PointAt(p.S, out p.E, out p.N, out float dx, out float dz); p.DirE = dx * p.Dir; p.DirN = dz * p.Dir;
                if (p.State == PedState.Crossing && p.StateTime > 90f) p.State = PedState.Walking;
            }
        }

        /// <summary>Pedestrians that are on the carriageway (traffic must give way).</summary>
        public IEnumerable<Pedestrian> Crossing() { foreach (var p in Peds) if (p.State == PedState.Crossing) yield return p; }
    }
}
