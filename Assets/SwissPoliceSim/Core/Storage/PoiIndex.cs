using System;
using System.Collections.Generic;
using System.IO;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geo;

namespace SwissPoliceSim.Core.Storage
{
    /// <summary>Country-wide index of named points of interest (police stations, hospitals, fuel, landmarks) – small enough to stay in RAM (≈ 60 k entries).</summary>
    public sealed class PoiIndex
    {
        public readonly List<PoiFeature> All = new List<PoiFeature>();
        const double Cell = 2000; readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();

        public static PoiIndex Build(ITileSource src)
        {
            var idx = new PoiIndex();
            foreach (var t in src.Tiles)
            {
                if (!src.TryRead(t, out var bytes)) continue; var c = TileContainer.Deserialize(bytes);
                if (c.TryGet("POI", out var p)) foreach (var f in TileCodec.ReadPois(p, t)) idx.Add(f);
            }
            return idx;
        }

        public void Add(PoiFeature f) { All.Add(f); long k = Key(f.E, f.N); if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new List<int>(); l.Add(All.Count - 1); }
        static long Key(double e, double n) => ((long)Math.Floor(e / Cell) << 32) ^ (uint)(int)Math.Floor(n / Cell);

        public IEnumerable<PoiFeature> Within(double e, double n, double radius, PoiKind? kind = null)
        {
            int r = (int)Math.Ceiling(radius / Cell); long cx = (long)Math.Floor(e / Cell), cy = (long)Math.Floor(n / Cell);
            for (long x = cx - r; x <= cx + r; x++) for (long y = cy - r; y <= cy + r; y++)
                if (_grid.TryGetValue((x << 32) ^ (uint)(int)y, out var l)) foreach (int i in l) { var f = All[i]; if (kind.HasValue && f.Kind != kind.Value) continue; double d = Math.Sqrt((f.E - e) * (f.E - e) + (f.N - n) * (f.N - n)); if (d <= radius) yield return f; }
        }

        public PoiFeature Nearest(double e, double n, PoiKind kind, double maxRadius = 30000)
        {
            for (double r = 2000; r <= maxRadius; r *= 2)
            {
                PoiFeature best = null; double bd = double.MaxValue;
                foreach (var f in Within(e, n, r, kind)) { double d = (f.E - e) * (f.E - e) + (f.N - n) * (f.N - n); if (d < bd) { bd = d; best = f; } }
                if (best != null) return best;
            }
            return null;
        }

        public List<PoiFeature> Search(string text, int max = 20)
        {
            var res = new List<PoiFeature>(); if (string.IsNullOrWhiteSpace(text)) return res; text = text.Trim();
            foreach (var f in All) if (!string.IsNullOrEmpty(f.Name) && f.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) { res.Add(f); if (res.Count >= max) break; }
            return res;
        }

        public void Save(string path)
        {
            using (var fs = File.Create(path)) using (var w = new BinaryWriter(fs))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("SPP1")); w.Write(All.Count);
                foreach (var f in All) { w.Write((byte)f.Kind); w.Write(f.E); w.Write(f.N); w.Write(f.OsmId); w.Write(f.Name ?? ""); }
            }
        }

        public static PoiIndex Load(string path)
        {
            var idx = new PoiIndex();
            using (var fs = File.OpenRead(path)) using (var r = new BinaryReader(fs))
            {
                if (System.Text.Encoding.ASCII.GetString(r.ReadBytes(4)) != "SPP1") throw new InvalidDataException("not a poi index");
                int n = r.ReadInt32(); for (int i = 0; i < n; i++) idx.Add(new PoiFeature { Kind = (PoiKind)r.ReadByte(), E = r.ReadDouble(), N = r.ReadDouble(), OsmId = r.ReadInt64(), Name = r.ReadString() });
            }
            return idx;
        }
    }
}
