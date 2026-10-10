using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Geo;

namespace SwissPoliceSim.Core.Features
{
    /// <summary>128 × 128 raster (7.8 m cells) of land-use classes of one tile (painted big-to-small so detail wins).</summary>
    public static class LandUseRaster
    {
        public const int Size = 128;

        public static byte[] Build(TileId tile, IList<LandUseFeature> features, IList<WaterFeature> water, IList<BuildingFeature> buildings, double fallbackAltitudeForRock = 2400)
        {
            var r = new byte[Size * Size];
            double e0 = TileGrid.MinE(tile), n0 = TileGrid.MinN(tile), cell = TileGrid.TileSize / Size;
            var list = new List<LandUseFeature>();
            foreach (var f in features)
            {
                f.Polygon.Bounds(out double x0, out double y0, out double x1, out double y1);
                if (x1 < e0 || x0 > e0 + 1000 || y1 < n0 || y0 > n0 + 1000) continue;
                list.Add(f);
            }
            list.Sort((a, b) => b.Polygon.Area().CompareTo(a.Polygon.Area()));
            foreach (var f in list) Paint(r, f.Polygon, (byte)f.Class, e0, n0, cell);
            foreach (var w in water)
            {
                w.Polygon.Bounds(out double x0, out double y0, out double x1, out double y1);
                if (x1 < e0 || x0 > e0 + 1000 || y1 < n0 || y0 > n0 + 1000) continue;
                Paint(r, w.Polygon, (byte)LandUseClass.Water, e0, n0, cell);
            }
            foreach (var b in buildings)
            {
                b.Footprint.Bounds(out double x0, out double y0, out double x1, out double y1);
                if (x1 < e0 || x0 > e0 + 1000 || y1 < n0 || y0 > n0 + 1000) continue;
                Paint(r, b.Footprint, (byte)LandUseClass.Urban, e0, n0, cell);   // ground under buildings: urban
            }
            // ground within ~10 m of a building that has no land-use class: paved/urban (streets, courtyards, plazas between houses)
            foreach (var b in buildings)
            {
                b.Footprint.Bounds(out double x0, out double y0, out double x1, out double y1);
                if (x1 + 10 < e0 || x0 - 10 > e0 + 1000 || y1 + 10 < n0 || y0 - 10 > n0 + 1000) continue;
                int c0 = Math.Max(0, (int)((x0 - 10 - e0) / cell)), c1 = Math.Min(Size - 1, (int)((x1 + 10 - e0) / cell));
                int r0 = Math.Max(0, (int)((y0 - 10 - n0) / cell)), r1 = Math.Min(Size - 1, (int)((y1 + 10 - n0) / cell));
                for (int j = r0; j <= r1; j++)
                    for (int i = c0; i <= c1; i++)
                    {
                        byte cur = r[j * Size + i];
                        if (cur != 0 && cur != (byte)LandUseClass.Grass && cur != (byte)LandUseClass.Park && cur != (byte)LandUseClass.Residential) continue;
                        double px = e0 + (i + 0.5) * cell, py = n0 + (j + 0.5) * cell;
                        if (Geometry.PolygonUtil.PointInRing(b.Footprint.Outer, px, py) || Near(b.Footprint, px, py, 9.0)) r[j * Size + i] = (byte)LandUseClass.Urban;
                    }
            }
            return r;
        }

        static bool Near(Geometry.PolygonWithHoles p, double e, double n, double d)
        {
            var q = new Geometry.Vec2d(e, n);
            for (int i = 0; i < p.Outer.Count; i++) if (Geometry.PolygonUtil.DistToSegment(q, p.Outer[i], p.Outer[(i + 1) % p.Outer.Count]) < d) return true;
            return false;
        }

        static void Paint(byte[] r, Geometry.PolygonWithHoles p, byte cls, double e0, double n0, double cell)
        {
            p.Bounds(out double x0, out double y0, out double x1, out double y1);
            int c0 = Math.Max(0, (int)((x0 - e0) / cell)), c1 = Math.Min(Size - 1, (int)((x1 - e0) / cell));
            int r0 = Math.Max(0, (int)((y0 - n0) / cell)), r1 = Math.Min(Size - 1, (int)((y1 - n0) / cell));
            for (int j = r0; j <= r1; j++)
                for (int i = c0; i <= c1; i++)
                    if (p.Contains(e0 + (i + 0.5) * cell, n0 + (j + 0.5) * cell)) r[j * Size + i] = cls;
        }
    }
}
