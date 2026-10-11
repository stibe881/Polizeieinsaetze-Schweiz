using System;
using System.Collections.Generic;
using System.IO;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Storage;

namespace SwissPoliceSim.Core.Roads
{
    /// <summary>
    /// Router that scales to the whole country: instead of keeping every road in RAM it keeps
    ///  (1) the small arterial skeleton (motorways … secondary roads, a few percent of all ways) permanently, and
    ///  (2) detailed per-tile graphs only around the origin and the destination (LRU cache, loaded from the tile source on demand).
    /// A* runs on the union of those layers; layers connect through the shared OSM node ids at way splits.
    /// Short routes (&lt; ShortRouteMeters) load every tile of the bounding corridor and are therefore exact; long routes use the skeleton
    /// for the bulk of the distance (a small, documented loss of optimality, like a car navigation system).
    /// </summary>
    public sealed class HierarchicalRouter
    {
        public RoadClass ArterialMax = RoadClass.SecondaryLink;
        public double LocalRadiusMeters = 2500, ShortRouteMeters = 9000;
        public int TileCacheCapacity = 96;
        readonly ITileSource _src;
        RoadGraph _arterial;
        readonly Dictionary<TileId, RoadGraph> _tiles = new Dictionary<TileId, RoadGraph>();
        readonly LinkedList<TileId> _lru = new LinkedList<TileId>();
        readonly object _lock = new object();
        public int TilesLoadedLastQuery { get; private set; }
        public int NodesExpandedLastQuery { get; private set; }
        public int CachedTiles { get { lock (_lock) return _tiles.Count; } }
        public bool HasArterial => _arterial != null;
        public int ArterialNodes => _arterial?.NodeCount ?? 0;

        public HierarchicalRouter(ITileSource source) { _src = source; }

        // ------------------------------------------------------------------ skeleton
        /// <summary>Scans all tiles once and collects the arterial ways. Run at bake time; the result is stored with SaveArterial.</summary>
        public static Dictionary<TileId, List<RoadWay>> CollectArterial(ITileSource src, RoadClass max, Action<int, int> progress = null)
        {
            var res = new Dictionary<TileId, List<RoadWay>>(); var tiles = new List<TileId>(src.Tiles); int i = 0;
            foreach (var t in tiles)
            {
                progress?.Invoke(i++, tiles.Count);
                if (!src.TryRead(t, out var bytes)) continue;
                var c = TileContainer.Deserialize(bytes);
                if (!c.TryGet("ROAD", out var road)) continue;
                List<RoadWay> ways; using (var ms = new MemoryStream(road)) ways = RoadTileFile.Read(ms, out _);
                var keep = ways.FindAll(w => w.IsVehicular && w.Class <= max);
                if (keep.Count > 0) res[t] = keep;
            }
            return res;
        }

        public static void SaveArterial(string path, Dictionary<TileId, List<RoadWay>> ways)
        {
            var d = new Dictionary<TileId, byte[]>();
            foreach (var kv in ways) using (var ms = new MemoryStream()) { RoadTileFile.Write(ms, kv.Key, kv.Value); d[kv.Key] = ms.ToArray(); }
            RegionPackSource.Write(path, d);
        }

        public void LoadArterial(string path)
        {
            var pack = new RegionPackSource(path); var ways = new List<RoadWay>();
            foreach (var t in pack.Tiles) if (pack.TryRead(t, out var b)) using (var ms = new MemoryStream(b)) ways.AddRange(RoadTileFile.Read(ms, out _));
            _arterial = RoadGraph.Build(ways);
        }

        public void SetArterial(Dictionary<TileId, List<RoadWay>> ways) { var all = new List<RoadWay>(); foreach (var l in ways.Values) all.AddRange(l); _arterial = RoadGraph.Build(all); }

        // ------------------------------------------------------------------ tiles
        RoadGraph GetTile(TileId t, ref int loaded)
        {
            lock (_lock)
            {
                if (_tiles.TryGetValue(t, out var g)) { _lru.Remove(t); _lru.AddFirst(t); return g; }
            }
            if (!_src.TryRead(t, out var bytes)) return null;
            var c = TileContainer.Deserialize(bytes); RoadGraph graph = new RoadGraph();
            if (c.TryGet("ROAD", out var road)) using (var ms = new MemoryStream(road)) foreach (var w in RoadTileFile.Read(ms, out _)) graph.AddWay(w);
            loaded++;
            lock (_lock)
            {
                _tiles[t] = graph; _lru.AddFirst(t);
                while (_tiles.Count > TileCacheCapacity) { var last = _lru.Last.Value; _lru.RemoveLast(); _tiles.Remove(last); }
            }
            return graph;
        }

        static IEnumerable<TileId> TilesAround(double e, double n, double r)
        {
            int x0 = (int)Math.Floor((e - r) / 1000), x1 = (int)Math.Floor((e + r) / 1000), y0 = (int)Math.Floor((n - r) / 1000), y1 = (int)Math.Floor((n + r) / 1000);
            for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) yield return new TileId(x, y);
        }

        // ------------------------------------------------------------------ query
        public RoutePath Route(double fromE, double fromN, double toE, double toN)
        {
            var r = RouteInternal(fromE, fromN, toE, toN, LocalRadiusMeters);
            return r ?? RouteInternal(fromE, fromN, toE, toN, LocalRadiusMeters * 2.5);
        }

        RoutePath RouteInternal(double fe, double fn, double te, double tn, double radius)
        {
            int loaded = 0; var layers = new List<RoadGraph>(); var wanted = new HashSet<TileId>();
            foreach (var t in TilesAround(fe, fn, radius)) wanted.Add(t);
            foreach (var t in TilesAround(te, tn, radius)) wanted.Add(t);
            double straight = Math.Sqrt((fe - te) * (fe - te) + (fn - tn) * (fn - tn));
            if (straight < ShortRouteMeters || _arterial == null)
            {
                double m = Math.Max(1500, straight * 0.25);
                int x0 = (int)Math.Floor((Math.Min(fe, te) - m) / 1000), x1 = (int)Math.Floor((Math.Max(fe, te) + m) / 1000), y0 = (int)Math.Floor((Math.Min(fn, tn) - m) / 1000), y1 = (int)Math.Floor((Math.Max(fn, tn) + m) / 1000);
                for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++) wanted.Add(new TileId(x, y));
            }
            if (_arterial != null && straight >= ShortRouteMeters) layers.Add(_arterial);
            foreach (var t in wanted) { var g = GetTile(t, ref loaded); if (g != null && g.NodeCount > 0) layers.Add(g); }
            TilesLoadedLastQuery = loaded;
            if (layers.Count == 0) return null;
            if (!Snap(layers, fe, fn, out long s) || !Snap(layers, te, tn, out long g2)) return null;
            return AStar(layers, s, g2);
        }

        static bool Snap(List<RoadGraph> layers, double e, double n, out long id)
        {
            id = 0; double best = 400 * 400; bool found = false;
            foreach (var g in layers)
            {
                int i = g.NearestNode(e, n, 400); if (i < 0) continue;
                g.GetNodePosition(i, out double ne, out double nn); double d = (ne - e) * (ne - e) + (nn - n) * (nn - n);
                // prefer nodes that have outgoing edges
                if (g.OutCount(i) == 0) d += 1e6;
                if (d < best || !found && d < 1e9) { best = d; id = g.IdAt(i); found = true; }
            }
            return found;
        }

        RoutePath AStar(List<RoadGraph> layers, long start, long goal)
        {
            const double Vmax = 130 / 3.6;
            double ge = 0, gn = 0; bool haveGoal = false;
            foreach (var g in layers) if (g.TryGetIndex(goal, out int gi)) { g.GetNodePosition(gi, out ge, out gn); haveGoal = true; break; }
            if (!haveGoal) return null;
            var gs = new Dictionary<long, double>(); var prev = new Dictionary<long, long>(); var closed = new HashSet<long>();
            var open = new Heap(); gs[start] = 0; open.Push(start, 0); int expanded = 0;
            while (open.Count > 0)
            {
                long u = open.Pop(); if (!closed.Add(u)) continue; expanded++;
                if (u == goal) break;
                double gu = gs[u];
                foreach (var g in layers)
                {
                    if (!g.TryGetIndex(u, out int ui)) continue;
                    int cnt = g.OutCount(ui);
                    for (int k = 0; k < cnt; k++)
                    {
                        g.OutEdge(ui, k, out int to, out _, out float sec); long vid = g.IdAt(to); if (closed.Contains(vid)) continue;
                        double ng = gu + sec;
                        if (!gs.TryGetValue(vid, out double old) || ng < old)
                        {
                            gs[vid] = ng; prev[vid] = u; g.GetNodePosition(to, out double ve, out double vn);
                            open.Push(vid, ng + Math.Sqrt((ve - ge) * (ve - ge) + (vn - gn) * (vn - gn)) / Vmax);
                        }
                    }
                }
            }
            NodesExpandedLastQuery = expanded;
            if (!gs.ContainsKey(goal)) return null;
            var path = new RoutePath { TimeSeconds = gs[goal] };
            for (long v = goal; ; v = prev[v]) { path.NodeIds.Add(v); if (v == start) break; }
            path.NodeIds.Reverse();
            foreach (long id in path.NodeIds)
                foreach (var g in layers) if (g.TryGetIndex(id, out int i)) { g.GetNodePosition(i, out double e, out double n); path.E.Add(e); path.N.Add(n); break; }
            for (int i = 1; i < path.Count; i++) { double de = path.E[i] - path.E[i - 1], dn = path.N[i] - path.N[i - 1]; path.DistanceMeters += Math.Sqrt(de * de + dn * dn); }
            return path;
        }

        sealed class Heap
        {
            readonly List<KeyValuePair<double, long>> _h = new List<KeyValuePair<double, long>>();
            public int Count => _h.Count;
            public void Push(long v, double p)
            {
                _h.Add(new KeyValuePair<double, long>(p, v)); int i = _h.Count - 1;
                while (i > 0) { int par = (i - 1) / 2; if (_h[par].Key <= _h[i].Key) break; var t = _h[par]; _h[par] = _h[i]; _h[i] = t; i = par; }
            }
            public long Pop()
            {
                long top = _h[0].Value; int last = _h.Count - 1; _h[0] = _h[last]; _h.RemoveAt(last); int i = 0;
                while (true) { int l = 2 * i + 1, r = l + 1, m = i; if (l < _h.Count && _h[l].Key < _h[m].Key) m = l; if (r < _h.Count && _h[r].Key < _h[m].Key) m = r; if (m == i) break; var t = _h[m]; _h[m] = _h[i]; _h[i] = t; i = m; }
                return top;
            }
        }
    }
}
