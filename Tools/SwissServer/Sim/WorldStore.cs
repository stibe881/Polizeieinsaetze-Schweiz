using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Import;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Storage;
using SwissPoliceSim.Core.Terrain;
using SwissPoliceSim.Core.Geometry;

namespace SwissServer.Sim
{
    /// <summary>The world data on disk (tiles, admin boundaries, POIs, router, macro terrain) plus a small cache of decoded tiles for height and road queries.</summary>
    public sealed class WorldStore
    {
        public string Root; public ITileSource Source; public AdminBoundaries Admin; public PoiIndex Pois; public HierarchicalRouter Router; public WorldManifest Manifest = new WorldManifest();
        public readonly Dictionary<long, MacroBlock> Macro = new Dictionary<long, MacroBlock>();
        readonly HashSet<TileId> _tiles = new HashSet<TileId>();
        readonly Dictionary<TileId, TileContent> _cache = new Dictionary<TileId, TileContent>(); readonly LinkedList<TileId> _lru = new LinkedList<TileId>(); readonly object _lock = new object();
        public int CacheLimit = 120;

        public static WorldStore Load(string root)
        {
            var w = new WorldStore { Root = root };
            string mf = Path.Combine(root, "manifest.json");
            if (File.Exists(mf)) { try { w.Manifest = JsonSerializer.Deserialize<WorldManifest>(File.ReadAllText(mf), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true }) ?? new WorldManifest(); } catch (Exception) { } }
            var sources = new List<ITileSource>(); string tiles = Path.Combine(root, "tiles"); if (Directory.Exists(tiles)) sources.Add(new LooseTileSource(tiles));
            foreach (var pack in Directory.Exists(root) ? Directory.GetFiles(root, "*.sprg") : new string[0]) if (!Path.GetFileName(pack).StartsWith("arterial", StringComparison.OrdinalIgnoreCase)) sources.Add(new RegionPackSource(pack));
            w.Source = new CompositeTileSource(sources.ToArray()); foreach (var t in w.Source.Tiles) w._tiles.Add(t);
            string admin = Path.Combine(root, "admin.spj"); if (File.Exists(admin)) using (var f = File.OpenRead(admin)) w.Admin = AdminBoundaries.Load(f);
            string poi = Path.Combine(root, "pois.spp"); w.Pois = File.Exists(poi) ? PoiIndex.Load(poi) : PoiIndex.Build(w.Source);
            w.Router = new HierarchicalRouter(w.Source); string art = Path.Combine(root, "arterial.sprg"); if (File.Exists(art)) w.Router.LoadArterial(art);
            string macro = Path.Combine(root, "macro"); if (Directory.Exists(macro)) foreach (var f in Directory.GetFiles(macro, "*.spm")) using (var fs = File.OpenRead(f)) { var b = MacroBlock.Read(fs); w.Macro[Key(b.BX, b.BY)] = b; }
            return w;
        }

        public static long Key(int bx, int by) => ((long)bx << 32) ^ (uint)by;
        public bool Has(TileId t) => _tiles.Contains(t);
        public int TileCount => _tiles.Count;

        /// <summary>Decoded tile (no meshes) from the cache; used for heights, road lookups and the traffic network.</summary>
        public TileContent Content(TileId id)
        {
            lock (_lock) { if (_cache.TryGetValue(id, out var c)) { _lru.Remove(id); _lru.AddFirst(id); return c; } }
            if (!_tiles.Contains(id) || !Source.TryRead(id, out var bytes)) return null;
            var content = TileMeshBuilder.Build(bytes, false);
            lock (_lock) { _cache[id] = content; _lru.AddFirst(id); while (_cache.Count > CacheLimit) { var old = _lru.Last.Value; _lru.RemoveLast(); _cache.Remove(old); } }
            return content;
        }

        public float Height(double e, double n)
        {
            var id = TileGrid.FromLv95(e, n); var c = Content(id); if (c?.Terrain == null) return float.NaN;
            return c.Terrain.SampleLocal(e - TileGrid.MinE(id), n - TileGrid.MinN(id));
        }

        public bool NearestRoad(double e, double n, double maxDist, out RoadInfo info, bool vehicularOnly = true, Func<RoadWay, double, double, bool> accept = null)
        {
            info = default; double best = maxDist * maxDist; RoadWay bw = null; int bseg = 0; var c = TileGrid.FromLv95(e, n);
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
            {
                var t = Content(new TileId(c.X + dx, c.Y + dy)); if (t == null) continue;
                foreach (var w in t.Roads)
                {
                    if (vehicularOnly && !w.IsVehicular) continue;
                    if (accept != null && !accept(w, e, n)) continue;
                    for (int i = 0; i + 1 < w.PointCount; i++)
                    {
                        double d = PolygonUtil.DistToSegment(new Vec2d(e, n), new Vec2d(w.E[i], w.N[i]), new Vec2d(w.E[i + 1], w.N[i + 1])); d *= d;
                        if (d < best) { best = d; bw = w; bseg = i; }
                    }
                }
            }
            if (bw == null) return false;
            info = new RoadInfo { Way = bw, Distance = Math.Sqrt(best), SpeedLimitKmh = bw.SpeedKmh > 0 ? bw.SpeedKmh : bw.EffectiveSpeedKmh(out _), Name = string.IsNullOrEmpty(bw.Name) ? bw.Ref : bw.Name, Basis = bw.SpeedBasis, Segment = bseg };
            info.Urban = info.Basis == SpeedSource.LegalUrban || info.Basis == SpeedSource.Zone || (info.Basis == SpeedSource.Tagged && info.SpeedLimitKmh <= 50);
            return true;
        }
    }

    public struct RoadInfo { public RoadWay Way; public double Distance; public int SpeedLimitKmh; public string Name; public SpeedSource Basis; public bool Urban; public int Segment; }
}
