using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Roads
{
    public sealed class RoutePath
    {
        public List<long> NodeIds = new List<long>();
        public List<double> E = new List<double>();
        public List<double> N = new List<double>();
        public double DistanceMeters;
        public double TimeSeconds;
        public int Count => NodeIds.Count;
    }

    /// <summary>
    /// Directed road graph built from RoadWays (nodes = shared OSM node ids). Phase-1 router: free-flow travel time
    /// on speed limits (tagged or class default), one-way aware. No turn restrictions yet (needs OSM relations).
    /// </summary>
    public sealed class RoadGraph
    {
        struct Edge { public int To; public float Length; public float Seconds; }

        readonly Dictionary<long, int> _index = new Dictionary<long, int>();
        readonly List<long> _ids = new List<long>();
        readonly List<double> _e = new List<double>();
        readonly List<double> _n = new List<double>();
        readonly List<List<Edge>> _adj = new List<List<Edge>>();
        readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        const double CellSize = 100.0;
        const double MaxSpeedMs = 120.0 / 3.6;

        public int NodeCount => _ids.Count;
        public int EdgeCount { get; private set; }

        public static RoadGraph Build(IEnumerable<RoadWay> ways)
        {
            var g = new RoadGraph();
            foreach (var w in ways) g.AddWay(w);
            return g;
        }

        int GetOrAdd(long id, double e, double n)
        {
            if (_index.TryGetValue(id, out int i)) return i;
            i = _ids.Count;
            _index[id] = i; _ids.Add(id); _e.Add(e); _n.Add(n); _adj.Add(new List<Edge>(3));
            long key = CellKey(e, n);
            if (!_cells.TryGetValue(key, out var l)) _cells[key] = l = new List<int>();
            l.Add(i);
            return i;
        }

        static long CellKey(double e, double n) =>
            ((long)Math.Floor(e / CellSize) << 32) ^ (long)(uint)(int)Math.Floor(n / CellSize);

        public void AddWay(RoadWay w)
        {
            double ms = w.EffectiveSpeedKmh(out _) / 3.6;
            for (int k = 1; k < w.PointCount; k++)
            {
                int a = GetOrAdd(w.NodeIds[k - 1], w.E[k - 1], w.N[k - 1]);
                int b = GetOrAdd(w.NodeIds[k], w.E[k], w.N[k]);
                if (a == b) continue;
                double len = Dist(a, b);
                var fwd = new Edge { To = b, Length = (float)len, Seconds = (float)(len / ms) };
                var bwd = new Edge { To = a, Length = (float)len, Seconds = (float)(len / ms) };
                if (w.OneWay != OneWay.Reverse) { _adj[a].Add(fwd); EdgeCount++; }
                if (w.OneWay != OneWay.Forward) { _adj[b].Add(bwd); EdgeCount++; }
            }
        }

        double Dist(int a, int b)
        {
            double de = _e[a] - _e[b], dn = _n[a] - _n[b];
            return Math.Sqrt(de * de + dn * dn);
        }

        /// <summary>Nearest graph node within maxDistance, or -1.</summary>
        public int NearestNode(double e, double n, double maxDistance = 300)
        {
            int best = -1; double bestD = maxDistance * maxDistance;
            int reach = (int)Math.Ceiling(maxDistance / CellSize);
            long cx = (long)Math.Floor(e / CellSize), cy = (long)Math.Floor(n / CellSize);
            for (long x = cx - reach; x <= cx + reach; x++)
                for (long y = cy - reach; y <= cy + reach; y++)
                {
                    long key = (x << 32) ^ (long)(uint)(int)y;
                    if (!_cells.TryGetValue(key, out var l)) continue;
                    foreach (int i in l)
                    {
                        double de = _e[i] - e, dn = _n[i] - n, d = de * de + dn * dn;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                }
            return best;
        }

        public void GetNodePosition(int node, out double e, out double n) { e = _e[node]; n = _n[node]; }

        /// <summary>Direction (unit vector, E/N) of the first outgoing edge of a node; false for dead-end nodes without edges.</summary>
        public bool TryGetNodeHeading(int node, out double dirE, out double dirN)
        {
            dirE = dirN = 0;
            if (_adj[node].Count == 0) return false;
            int to = _adj[node][0].To;
            double de = _e[to] - _e[node], dn = _n[to] - _n[node], l = Math.Sqrt(de * de + dn * dn);
            if (l < 1e-6) return false;
            dirE = de / l; dirN = dn / l; return true;
        }

        public RoutePath FindRoute(double fromE, double fromN, double toE, double toN, double snapDistance = 300)
        {
            int s = NearestNode(fromE, fromN, snapDistance), t = NearestNode(toE, toN, snapDistance);
            if (s < 0 || t < 0) return null;
            return FindRoute(s, t);
        }

        public RoutePath FindRoute(int start, int goal)
        {
            int count = _ids.Count;
            var g = new double[count]; var prev = new int[count]; var closed = new bool[count];
            for (int i = 0; i < count; i++) { g[i] = double.PositiveInfinity; prev[i] = -1; }
            var open = new PriorityQueue();
            g[start] = 0;
            open.Push(start, H(start, goal));
            while (open.Count > 0)
            {
                int u = open.Pop();
                if (closed[u]) continue;
                if (u == goal) break;
                closed[u] = true;
                foreach (var ed in _adj[u])
                {
                    double ng = g[u] + ed.Seconds;
                    if (ng < g[ed.To]) { g[ed.To] = ng; prev[ed.To] = u; open.Push(ed.To, ng + H(ed.To, goal)); }
                }
            }
            if (double.IsPositiveInfinity(g[goal])) return null;

            var path = new RoutePath { TimeSeconds = g[goal] };
            for (int v = goal; v != -1; v = prev[v]) path.NodeIds.Add(_ids[v]);
            path.NodeIds.Reverse();
            foreach (long id in path.NodeIds) { int i = _index[id]; path.E.Add(_e[i]); path.N.Add(_n[i]); }
            for (int i = 1; i < path.Count; i++)
            {
                double de = path.E[i] - path.E[i - 1], dn = path.N[i] - path.N[i - 1];
                path.DistanceMeters += Math.Sqrt(de * de + dn * dn);
            }
            return path;
        }

        double H(int a, int b) => Dist(a, b) / MaxSpeedMs;

        sealed class PriorityQueue
        {
            readonly List<KeyValuePair<double, int>> _h = new List<KeyValuePair<double, int>>();
            public int Count => _h.Count;
            public void Push(int v, double p)
            {
                _h.Add(new KeyValuePair<double, int>(p, v));
                int i = _h.Count - 1;
                while (i > 0)
                {
                    int par = (i - 1) / 2;
                    if (_h[par].Key <= _h[i].Key) break;
                    var t = _h[par]; _h[par] = _h[i]; _h[i] = t; i = par;
                }
            }
            public int Pop()
            {
                int top = _h[0].Value;
                int last = _h.Count - 1;
                _h[0] = _h[last]; _h.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < _h.Count && _h[l].Key < _h[m].Key) m = l;
                    if (r < _h.Count && _h[r].Key < _h[m].Key) m = r;
                    if (m == i) break;
                    var t = _h[m]; _h[m] = _h[i]; _h[i] = t; i = m;
                }
                return top;
            }
        }
    }
}
