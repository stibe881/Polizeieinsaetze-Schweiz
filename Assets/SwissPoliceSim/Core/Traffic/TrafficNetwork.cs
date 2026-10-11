using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Roads;

namespace SwissPoliceSim.Core.Traffic
{
    /// <summary>One directed stretch of road between two decision nodes (intersections, signals, limit changes).</summary>
    public sealed class TLink
    {
        public int Id;
        public int FromNode, ToNode;
        public long FromOsmNode, ToOsmNode;
        public double[] E, N;            // polyline of the lane centre line (LV95)
        public float[] Cum;              // cumulative length at each vertex
        public float[] VMax;             // curvature + limit speed profile (m/s) per vertex
        public float Length;
        public float LimitMs;            // legal speed limit (m/s)
        public int LimitKmh;
        public SpeedSource LimitBasis;
        public RoadClass Class;
        public int Lanes;                // lanes in this direction
        public float LaneWidth;
        public bool Roundabout, Bridge, Tunnel, OneWay;
        public int Priority;             // 1..10
        public string Name = "", Ref = "";
        public long WayId;
        public bool SignalAtEnd; public int SignalGroup; public float SignalOffset;
        public bool StopAtEnd, GiveWayAtEnd;
        public readonly List<int> Out = new List<int>();
        public readonly List<int> In = new List<int>();     // links that end where this one starts
        public float EndHeading;         // heading (rad from north) of the last segment
        public float[] H;                // road surface height (m a.s.l.) per vertex, null if unknown

        /// <summary>Road surface height at s along the link; returns fallback when the data has no heights.</summary>
        public float HeightAt(float s, float fallback)
        {
            if (H == null) return fallback;
            if (s <= 0) return H[0]; if (s >= Length) return H[H.Length - 1];
            int i = 1; while (i < Cum.Length - 1 && Cum[i] < s) i++;
            float seg = Cum[i] - Cum[i - 1]; float t = seg > 1e-4f ? (s - Cum[i - 1]) / seg : 0; return H[i - 1] + (H[i] - H[i - 1]) * t;
        }

        public void PointAt(float s, out double e, out double n, out float dirE, out float dirN)
        {
            if (s <= 0) s = 0; if (s >= Length) s = Length;
            int i = 1; while (i < Cum.Length - 1 && Cum[i] < s) i++;
            float seg = Cum[i] - Cum[i - 1]; float t = seg > 1e-4f ? (s - Cum[i - 1]) / seg : 0;
            e = E[i - 1] + (E[i] - E[i - 1]) * t; n = N[i - 1] + (N[i] - N[i - 1]) * t;
            float dx = (float)(E[i] - E[i - 1]), dn = (float)(N[i] - N[i - 1]), l = (float)Math.Sqrt(dx * dx + dn * dn);
            if (l < 1e-5f) { dirE = 0; dirN = 1; } else { dirE = dx / l; dirN = dn / l; }
        }

        public float SpeedLimitAt(float s)
        {
            int i = 1; while (i < Cum.Length - 1 && Cum[i] < s) i++;
            return VMax[Math.Max(0, i - 1)];
        }
    }

    /// <summary>Directed, link-based traffic graph with priorities, signals and a speed/curvature profile.</summary>
    public sealed class TrafficNetwork
    {
        public readonly List<TLink> Links = new List<TLink>();
        public readonly List<double> NodeE = new List<double>(), NodeN = new List<double>(); public readonly List<float> NodeH = new List<float>();
        readonly Dictionary<long, int> _nodeIndex = new Dictionary<long, int>();
        public readonly Dictionary<long, (int group0Dummy, float offset)> SignalNodes = new Dictionary<long, (int, float)>();
        readonly Dictionary<long, PointKind> _nodeSign = new Dictionary<long, PointKind>();
        readonly Dictionary<long, List<int>> _cell = new Dictionary<long, List<int>>();
        const double CellSize = 100;

        struct Edge { public RoadWay Way; public int A, B; public bool Rev; public int SegIdx; }

        public static TrafficNetwork Build(IEnumerable<RoadWay> ways, IEnumerable<PointFeature> points)
        {
            var net = new TrafficNetwork();
            foreach (var p in points)
            {
                if (p.Kind == PointKind.TrafficSignals) net.SignalNodes[p.OsmId] = (0, SignalModel.OffsetFor(p.OsmId));
                else if (p.Kind == PointKind.StopSign || p.Kind == PointKind.GiveWay) net._nodeSign[p.OsmId] = p.Kind;
            }
            net.BuildLinks(ways);
            return net;
        }

        int Node(long osm, double e, double n, float h = float.NaN)
        {
            if (_nodeIndex.TryGetValue(osm, out int i)) { if (float.IsNaN(NodeH[i]) && !float.IsNaN(h)) NodeH[i] = h; return i; }
            i = NodeE.Count; _nodeIndex[osm] = i; NodeE.Add(e); NodeN.Add(n); NodeH.Add(h); return i;
        }

        public int NodeOf(long osm) => _nodeIndex.TryGetValue(osm, out int i) ? i : -1;

        static int PriorityOf(RoadClass c, bool roundabout)
        {
            if (roundabout) return 10;
            switch (c)
            {
                case RoadClass.Motorway: return 9; case RoadClass.Trunk: return 8; case RoadClass.Primary: return 7; case RoadClass.Secondary: return 6;
                case RoadClass.Tertiary: return 5; case RoadClass.Unclassified: return 4; case RoadClass.MotorwayLink: case RoadClass.TrunkLink: case RoadClass.PrimaryLink: case RoadClass.SecondaryLink: case RoadClass.TertiaryLink: return 6;
                case RoadClass.Residential: return 3; case RoadClass.Service: return 2; default: return 1;
            }
        }

        void BuildLinks(IEnumerable<RoadWay> ways)
        {
            // 1) directed edges
            var edges = new List<Edge>(); var outE = new Dictionary<int, List<int>>(); var inE = new Dictionary<int, List<int>>();
            var wayList = new List<RoadWay>();
            foreach (var w in ways)
            {
                if (!w.IsVehicular) continue;
                wayList.Add(w);
                for (int k = 0; k + 1 < w.PointCount; k++)
                {
                    bool hasH = w.H != null && w.H.Length == w.PointCount;
                    int a = Node(w.NodeIds[k], w.E[k], w.N[k], hasH ? (float)w.H[k] : float.NaN), b = Node(w.NodeIds[k + 1], w.E[k + 1], w.N[k + 1], hasH ? (float)w.H[k + 1] : float.NaN);
                    if (a == b) continue;
                    if (w.OneWay != OneWay.Reverse) Add(edges, outE, inE, new Edge { Way = w, A = a, B = b, Rev = false, SegIdx = k });
                    if (w.OneWay != OneWay.Forward) Add(edges, outE, inE, new Edge { Way = w, A = b, B = a, Rev = true, SegIdx = k });
                }
            }
            // 2) chains: maximal runs of the same way through pass-through nodes
            _osmByNode = new Dictionary<int, long>(); foreach (var kv in _nodeIndex) _osmByNode[kv.Value] = kv.Key;
            var pass = new Dictionary<int, bool>();
            bool IsPass(int node)
            {
                if (pass.TryGetValue(node, out bool r)) return r;
                r = PassThrough(node, edges, outE, inE); pass[node] = r; return r;
            }
            var used = new bool[edges.Count];
            for (int ei = 0; ei < edges.Count; ei++)
            {
                if (used[ei] || IsPass(edges[ei].A)) continue;
                BuildChain(ei, edges, outE, used, IsPass);
            }
            for (int ei = 0; ei < edges.Count; ei++) if (!used[ei]) BuildChain(ei, edges, outE, used, IsPass);   // pure cycles

            // 3) connectivity and attributes
            var startsAt = new Dictionary<int, List<int>>(); var endsAt = new Dictionary<int, List<int>>();
            foreach (var l in Links)
            {
                if (!startsAt.TryGetValue(l.FromNode, out var a)) startsAt[l.FromNode] = a = new List<int>(); a.Add(l.Id);
                if (!endsAt.TryGetValue(l.ToNode, out var b)) endsAt[l.ToNode] = b = new List<int>(); b.Add(l.Id);
            }
            foreach (var l in Links)
            {
                if (startsAt.TryGetValue(l.ToNode, out var outs))
                    foreach (int o in outs)
                    {
                        var ol = Links[o];
                        // no immediate U-turn onto the reverse link of the same way unless dead end
                        bool uturn = ol.WayId == l.WayId && ol.ToNode == l.FromNode;
                        if (uturn && outs.Count > 1) continue;
                        l.Out.Add(o); ol.In.Add(l.Id);
                    }
                long osm = l.ToOsmNode;
                if (SignalNodes.TryGetValue(osm, out var sg)) { l.SignalAtEnd = true; l.SignalOffset = sg.offset; l.SignalGroup = SignalModel.GroupOf(l.EndHeading); }
                else if (_nodeSign.TryGetValue(osm, out var sk)) { if (sk == PointKind.StopSign) l.StopAtEnd = true; else l.GiveWayAtEnd = true; }
            }
            foreach (var l in Links) { Profile(l); Index(l); }
        }

        Dictionary<int, long> _osmByNode;
        long OsmOfFast(int node) => _osmByNode[node];

        bool PassThrough(int node, List<Edge> edges, Dictionary<int, List<int>> outE, Dictionary<int, List<int>> inE)
        {
            int ins = inE.TryGetValue(node, out var li) ? li.Count : 0, outs = outE.TryGetValue(node, out var lo) ? lo.Count : 0;
            long osm = _osmByNode[node];
            if (SignalNodes.ContainsKey(osm) || _nodeSign.ContainsKey(osm)) return false;
            if (ins == 1 && outs == 1)
            {
                var a = edges[inE[node][0]]; var b = edges[outE[node][0]];
                return a.Way == b.Way || (a.Way.SpeedKmh == b.Way.SpeedKmh && a.Way.Class == b.Way.Class && a.Way.Bridge == b.Way.Bridge && a.Way.Tunnel == b.Way.Tunnel && a.Way.Roundabout == b.Way.Roundabout && a.Way.Name == b.Way.Name);
            }
            if (ins == 2 && outs == 2)
            {
                RoadWay w = edges[inE[node][0]].Way;
                foreach (int x in inE[node]) if (edges[x].Way != w) return false;
                foreach (int x in outE[node]) if (edges[x].Way != w) return false;
                // same way both directions → a plain two-way pass-through unless the way touches itself
                return true;
            }
            return false;
        }

        static void Add(List<Edge> edges, Dictionary<int, List<int>> outE, Dictionary<int, List<int>> inE, Edge e)
        {
            int id = edges.Count; edges.Add(e);
            if (!outE.TryGetValue(e.A, out var o)) outE[e.A] = o = new List<int>(); o.Add(id);
            if (!inE.TryGetValue(e.B, out var i)) inE[e.B] = i = new List<int>(); i.Add(id);
        }

        void BuildChain(int start, List<Edge> edges, Dictionary<int, List<int>> outE, bool[] used, Func<int, bool> isPass)
        {
            var seq = new List<int>(); int cur = start;
            while (true)
            {
                used[cur] = true; seq.Add(cur);
                var e = edges[cur];
                if (!isPass(e.B)) break;
                int next = -1;
                foreach (int o in outE[e.B]) if (!used[o] && edges[o].B != e.A && edges[o].Way == e.Way) { next = o; break; }
                if (next < 0) foreach (int o in outE[e.B]) if (!used[o] && edges[o].B != e.A) { next = o; break; }
                if (next < 0) break;
                cur = next;
            }
            var first = edges[seq[0]]; var way = first.Way; var last = edges[seq[seq.Count - 1]];
            var el = new List<double>(); var nl = new List<double>();
            el.Add(NodeE[first.A]); nl.Add(NodeN[first.A]);
            foreach (int ei in seq) { el.Add(NodeE[edges[ei].B]); nl.Add(NodeN[edges[ei].B]); }
            int lanes = way.OneWay != OneWay.No ? Math.Max(1, way.CarriageLanes()) : Math.Max(1, way.CarriageLanes() / 2);
            var l = new TLink
            {
                Id = Links.Count, FromNode = first.A, ToNode = last.B, FromOsmNode = OsmOfFast(first.A), ToOsmNode = OsmOfFast(last.B),
                E = el.ToArray(), N = nl.ToArray(), Class = way.Class, Lanes = lanes, LaneWidth = way.LaneWidth(), Roundabout = way.Roundabout,
                Bridge = way.Bridge, Tunnel = way.Tunnel, OneWay = way.OneWay != OneWay.No, Priority = PriorityOf(way.Class, way.Roundabout),
                Name = way.Name, Ref = way.Ref, WayId = way.OsmId, LimitKmh = Math.Max(10, way.SpeedKmh), LimitBasis = way.SpeedBasis
            };
            l.LimitMs = l.LimitKmh / 3.6f;
            {   // heights: node heights along the chain, gaps filled from the nearest known value
                var hl = new List<float> { NodeH[first.A] }; foreach (int ei in seq) hl.Add(NodeH[edges[ei].B]);
                bool any = false; foreach (var v in hl) if (!float.IsNaN(v)) { any = true; break; }
                if (any)
                {
                    float last2 = float.NaN; for (int i = 0; i < hl.Count; i++) { if (!float.IsNaN(hl[i])) last2 = hl[i]; else if (!float.IsNaN(last2)) hl[i] = last2; }
                    last2 = float.NaN; for (int i = hl.Count - 1; i >= 0; i--) { if (!float.IsNaN(hl[i])) last2 = hl[i]; else hl[i] = last2; }
                    l.H = hl.ToArray();
                }
            }
            l.Cum = new float[l.E.Length];
            for (int i = 1; i < l.E.Length; i++) l.Cum[i] = l.Cum[i - 1] + (float)Math.Sqrt(Math.Pow(l.E[i] - l.E[i - 1], 2) + Math.Pow(l.N[i] - l.N[i - 1], 2));
            l.Length = l.Cum[l.Cum.Length - 1];
            if (l.Length < 0.5f) { return; }
            int nn = l.E.Length; l.EndHeading = (float)Math.Atan2(l.E[nn - 1] - l.E[nn - 2], l.N[nn - 1] - l.N[nn - 2]);
            Links.Add(l);
        }

        /// <summary>Per-vertex maximum speed from curvature (lateral acceleration ≤ 2.6 m/s²), smoothed with a braking pass (2.2 m/s²).</summary>
        static void Profile(TLink l)
        {
            int n = l.E.Length; l.VMax = new float[n];
            for (int i = 0; i < n; i++)
            {
                float v = 60f;     // geometric cap only; the legal limit is applied separately so speeders can exceed it
                if (i > 0 && i < n - 1)
                {
                    double ax = l.E[i] - l.E[i - 1], ay = l.N[i] - l.N[i - 1], bx = l.E[i + 1] - l.E[i], by = l.N[i + 1] - l.N[i];
                    double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
                    if (la > 0.5 && lb > 0.5)
                    {
                        double cos = (ax * bx + ay * by) / (la * lb); cos = Math.Max(-1, Math.Min(1, cos));
                        double ang = Math.Acos(cos), arc = (la + lb) * 0.5;
                        if (ang > 0.05) { double radius = arc / ang; v = Math.Min(v, (float)Math.Sqrt(2.6 * Math.Max(4, radius))); }
                    }
                }
                l.VMax[i] = Math.Max(v, 3f);
            }
            for (int i = n - 2; i >= 0; i--) { float ds = l.Cum[i + 1] - l.Cum[i]; l.VMax[i] = Math.Min(l.VMax[i], (float)Math.Sqrt(l.VMax[i + 1] * l.VMax[i + 1] + 2 * 2.2f * ds)); }
            for (int i = 1; i < n; i++) { float ds = l.Cum[i] - l.Cum[i - 1]; l.VMax[i] = Math.Min(l.VMax[i], (float)Math.Sqrt(l.VMax[i - 1] * l.VMax[i - 1] + 2 * 2.0f * ds)); }
        }

        void Index(TLink l)
        {
            for (int i = 0; i < l.E.Length; i++)
            {
                long k = ((long)Math.Floor(l.E[i] / CellSize) << 32) ^ (uint)(int)Math.Floor(l.N[i] / CellSize);
                if (!_cell.TryGetValue(k, out var list)) _cell[k] = list = new List<int>();
                if (!list.Contains(l.Id)) list.Add(l.Id);
            }
        }

        /// <summary>Nearest link point within maxDist; returns link id or -1 and the arc-length position.</summary>
        public int Nearest(double e, double n, double maxDist, out float s)
        {
            s = 0; int best = -1; double bd = maxDist * maxDist; int reach = (int)Math.Ceiling(maxDist / CellSize);
            long cx = (long)Math.Floor(e / CellSize), cy = (long)Math.Floor(n / CellSize);
            for (long x = cx - reach; x <= cx + reach; x++)
                for (long y = cy - reach; y <= cy + reach; y++)
                {
                    long k = (x << 32) ^ (uint)(int)y;
                    if (!_cell.TryGetValue(k, out var list)) continue;
                    foreach (int id in list)
                    {
                        var l = Links[id];
                        for (int i = 0; i + 1 < l.E.Length; i++)
                        {
                            double dx = l.E[i + 1] - l.E[i], dy = l.N[i + 1] - l.N[i], l2 = dx * dx + dy * dy; if (l2 < 1e-9) continue;
                            double t = Math.Max(0, Math.Min(1, ((e - l.E[i]) * dx + (n - l.N[i]) * dy) / l2));
                            double px = l.E[i] + dx * t - e, py = l.N[i] + dy * t - n, d = px * px + py * py;
                            if (d < bd) { bd = d; best = id; s = l.Cum[i] + (float)(t * Math.Sqrt(l2)); }
                        }
                    }
                }
            return best;
        }
    }
}
