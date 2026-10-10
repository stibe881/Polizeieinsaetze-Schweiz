using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using SwissPoliceSim.Core.Geometry;

namespace SwissPoliceSim.Core.Geo
{
    public sealed class AdminArea
    {
        public int Id;             // canton number (1..26) or BFS municipality number
        public int Canton;         // canton number of the area
        public string Name = "";
        public List<double[]> Rings = new List<double[]>();     // x,y pairs (LV95)
        public double MinE, MinN, MaxE, MaxN;
        public bool Contains(double e, double n)
        {
            if (e < MinE || e > MaxE || n < MinN || n > MaxN) return false;
            bool inside = false;     // even-odd over all rings of the area (outer rings and holes)
            foreach (var r in Rings)
                for (int i = 0, j = r.Length / 2 - 1; i < r.Length / 2; j = i++)
                {
                    double xi = r[i * 2], yi = r[i * 2 + 1], xj = r[j * 2], yj = r[j * 2 + 1];
                    if ((yi > n) != (yj > n) && e < (xj - xi) * (n - yi) / (yj - yi) + xi) inside = !inside;
                }
            return inside;
        }
        public void UpdateBounds()
        {
            MinE = MinN = double.MaxValue; MaxE = MaxN = double.MinValue;
            foreach (var r in Rings) for (int i = 0; i < r.Length; i += 2) { MinE = Math.Min(MinE, r[i]); MaxE = Math.Max(MaxE, r[i]); MinN = Math.Min(MinN, r[i + 1]); MaxN = Math.Max(MaxN, r[i + 1]); }
        }
    }

    /// <summary>
    /// Canton and municipality polygons of Switzerland (swissBOUNDARIES3D © swisstopo), simplified for gameplay.
    /// Answers "which canton / municipality is this LV95 point in?" – the basis of police jurisdiction.
    /// File "SPJ1": int cantonCount, int muniCount, then for each area: int id, int canton, string name, ushort ringCount, per ring: ushort n, int32 x[n], int32 y[n] (decimetres from 2 480 000 / 1 070 000) — whole file deflated.
    /// </summary>
    public sealed class AdminBoundaries
    {
        public readonly List<AdminArea> Cantons = new List<AdminArea>(), Municipalities = new List<AdminArea>();
        readonly Dictionary<long, List<int>> _muniGrid = new Dictionary<long, List<int>>();
        const double OriginE = 2480000, OriginN = 1070000, Grid = 5000;

        public static readonly string[] CantonNames =
        {
            "", "Zürich", "Bern", "Luzern", "Uri", "Schwyz", "Obwalden", "Nidwalden", "Glarus", "Zug", "Freiburg", "Solothurn", "Basel-Stadt", "Basel-Landschaft",
            "Schaffhausen", "Appenzell Ausserrhoden", "Appenzell Innerrhoden", "St. Gallen", "Graubünden", "Aargau", "Thurgau", "Tessin", "Waadt", "Wallis", "Neuenburg", "Genf", "Jura"
        };
        public static readonly string[] CantonAbbr =
        {
            "", "ZH", "BE", "LU", "UR", "SZ", "OW", "NW", "GL", "ZG", "FR", "SO", "BS", "BL", "SH", "AR", "AI", "SG", "GR", "AG", "TG", "TI", "VD", "VS", "NE", "GE", "JU"
        };

        public static AdminBoundaries FromShapefiles(string cantonShp, string muniShp, double simplifyMetres = 25)
        {
            var b = new AdminBoundaries();
            foreach (var r in Shapefile.Read(cantonShp))
            {
                if (!r.Attributes.TryGetValue("KANTONSNUM", out var kn) || !int.TryParse(kn, out int id)) continue;
                if (r.Attributes.TryGetValue("OBJEKTART", out var oa) && oa != "Kanton") continue;
                b.Cantons.Add(Make(r, id, id, r.Attributes["NAME"], simplifyMetres));
            }
            foreach (var r in Shapefile.Read(muniShp))
            {
                if (!r.Attributes.TryGetValue("BFS_NUMMER", out var bfs) || !int.TryParse(bfs, out int id)) continue;
                if (r.Attributes.TryGetValue("OBJEKTART", out var oa) && oa != "Gemeindegebiet") continue;
                int.TryParse(r.Attributes["KANTONSNUM"], out int kn);
                b.Municipalities.Add(Make(r, id, kn, r.Attributes["NAME"], simplifyMetres));
            }
            b.BuildIndex();
            return b;
        }

        static AdminArea Make(Shapefile.Record r, int id, int canton, string name, double tol)
        {
            var a = new AdminArea { Id = id, Canton = canton, Name = name };
            foreach (var ring in r.Rings)
            {
                var pts = new List<Vec2d>(ring.Length / 2);
                for (int i = 0; i < ring.Length; i += 2) pts.Add(new Vec2d(ring[i], ring[i + 1]));
                PolygonUtil.RemoveClosingPoint(pts);
                if (pts.Count > 6) pts = PolygonUtil.SimplifyRing(pts, tol);
                if (pts.Count < 3) continue;
                var arr = new double[pts.Count * 2]; for (int i = 0; i < pts.Count; i++) { arr[i * 2] = pts[i].X; arr[i * 2 + 1] = pts[i].Y; }
                a.Rings.Add(arr);
            }
            a.UpdateBounds(); return a;
        }

        void BuildIndex()
        {
            _muniGrid.Clear();
            for (int i = 0; i < Municipalities.Count; i++)
            {
                var m = Municipalities[i];
                for (int gx = (int)Math.Floor(m.MinE / Grid); gx <= (int)Math.Floor(m.MaxE / Grid); gx++)
                    for (int gy = (int)Math.Floor(m.MinN / Grid); gy <= (int)Math.Floor(m.MaxN / Grid); gy++)
                    {
                        long k = ((long)gx << 32) ^ (uint)gy;
                        if (!_muniGrid.TryGetValue(k, out var l)) _muniGrid[k] = l = new List<int>(); l.Add(i);
                    }
            }
        }

        public AdminArea FindMunicipality(double e, double n)
        {
            if (_muniGrid.TryGetValue(((long)(int)Math.Floor(e / Grid) << 32) ^ (uint)(int)Math.Floor(n / Grid), out var l))
                foreach (int i in l) if (Municipalities[i].Contains(e, n)) return Municipalities[i];
            return null;
        }

        public AdminArea FindCanton(double e, double n)
        {
            var m = FindMunicipality(e, n);
            if (m != null) foreach (var c in Cantons) if (c.Id == m.Canton) return c;
            foreach (var c in Cantons) if (c.Contains(e, n)) return c;
            return null;
        }

        /// <summary>Distance (m) from a point to the nearest canton border (approximation: nearest vertex/edge of the canton rings, 0 if outside).</summary>
        public double DistanceToCantonBorder(double e, double n, int cantonId)
        {
            double best = double.MaxValue; var p = new Vec2d(e, n);
            foreach (var c in Cantons)
            {
                if (c.Id != cantonId) continue;
                foreach (var r in c.Rings)
                    for (int i = 0, j = r.Length / 2 - 1; i < r.Length / 2; j = i++)
                        best = Math.Min(best, PolygonUtil.DistToSegment(p, new Vec2d(r[j * 2], r[j * 2 + 1]), new Vec2d(r[i * 2], r[i * 2 + 1])));
            }
            return best;
        }

        // ---------------------------------------------------------------- storage
        public void Save(string path)
        {
            using (var fs = File.Create(path))
            using (var z = new DeflateStream(fs, CompressionLevel.Optimal))
            using (var w = new BinaryWriter(z))
            {
                w.Write(Cantons.Count); w.Write(Municipalities.Count);
                foreach (var a in Cantons) WriteArea(w, a);
                foreach (var a in Municipalities) WriteArea(w, a);
            }
        }

        static void WriteArea(BinaryWriter w, AdminArea a)
        {
            w.Write(a.Id); w.Write(a.Canton); w.Write(a.Name); w.Write((ushort)a.Rings.Count);
            foreach (var r in a.Rings)
            {
                int n = r.Length / 2; w.Write((ushort)Math.Min(n, 65535));
                for (int i = 0; i < n && i < 65535; i++) w.Write((int)Math.Round((r[i * 2] - OriginE) * 10));
                for (int i = 0; i < n && i < 65535; i++) w.Write((int)Math.Round((r[i * 2 + 1] - OriginN) * 10));
            }
        }

        public static AdminBoundaries Load(Stream s)
        {
            var b = new AdminBoundaries();
            using (var z = new DeflateStream(s, CompressionMode.Decompress))
            using (var r = new BinaryReader(z))
            {
                int nc = r.ReadInt32(), nm = r.ReadInt32();
                for (int i = 0; i < nc; i++) b.Cantons.Add(ReadArea(r));
                for (int i = 0; i < nm; i++) b.Municipalities.Add(ReadArea(r));
            }
            b.BuildIndex();
            return b;
        }

        static AdminArea ReadArea(BinaryReader r)
        {
            var a = new AdminArea { Id = r.ReadInt32(), Canton = r.ReadInt32(), Name = r.ReadString() };
            int rings = r.ReadUInt16();
            for (int k = 0; k < rings; k++)
            {
                int n = r.ReadUInt16(); var arr = new double[n * 2];
                for (int i = 0; i < n; i++) arr[i * 2] = OriginE + r.ReadInt32() / 10.0;
                for (int i = 0; i < n; i++) arr[i * 2 + 1] = OriginN + r.ReadInt32() / 10.0;
                a.Rings.Add(arr);
            }
            a.UpdateBounds(); return a;
        }
    }
}
