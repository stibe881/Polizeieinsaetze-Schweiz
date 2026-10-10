using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Geometry;
using SwissPoliceSim.Core.Terrain;

namespace SwissPoliceSim.Core.Features
{
    /// <summary>
    /// Derives base height, eave height, ridge height and roof shape of a building from the measured surface model
    /// (swissSURFACE3D minus swissALTI3D). Falls back to OSM tags and type defaults where no measurement exists.
    /// </summary>
    public static class BuildingHeightEstimator
    {
        public static void Estimate(BuildingFeature b, IHeightSource dem, SurfaceMosaic surface)
        {
            var outer = b.Footprint.Outer;
            // ---- base ground: lowest terrain height along the outline
            float ground = float.MaxValue; int gs = 0;
            for (int i = 0; i < outer.Count; i++)
            {
                var a = outer[i]; var c = outer[(i + 1) % outer.Count];
                double len = (c - a).Length; int steps = Math.Max(1, (int)(len / 2.0));
                for (int s = 0; s < steps; s++)
                {
                    double t = s / (double)steps;
                    if (dem.TrySample(a.X + (c.X - a.X) * t, a.Y + (c.Y - a.Y) * t, out float h)) { gs++; if (h < ground) ground = h; }
                }
            }
            if (gs == 0) { b.GroundHeight = float.NaN; return; }
            b.GroundHeight = ground;

            // ---- roof samples (1 m cells whose centre lies inside the footprint, away from the edge)
            b.Footprint.Bounds(out double x0, out double y0, out double x1, out double y1);
            PolygonUtil.OrientedBox(outer, out var centre, out var u, out double hl, out double hs);
            var v = new Vec2d(-u.Y, u.X);
            double minDim = Math.Min(hl, hs) * 2;
            double inset = minDim < 5 ? 0.45 : 0.95;
            var vals = new List<float>(); var along = new List<double>(); var across = new List<double>();
            for (double e = Math.Floor(x0) + 0.5; e <= x1; e += 1.0)
                for (double n = Math.Floor(y0) + 0.5; n <= y1; n += 1.0)
                {
                    if (!b.Footprint.Contains(e, n)) continue;
                    if (DistToBoundary(b.Footprint, e, n) < inset) continue;
                    if (!surface.TrySample(e, n, out float s)) continue;
                    vals.Add(s);
                    var d = new Vec2d(e - centre.X, n - centre.Y);
                    along.Add(Vec2d.Dot(d, u)); across.Add(Vec2d.Dot(d, v));
                }

            float tagRidge = b.RidgeHeight;   // from OSM height / levels, may be 0
            if (vals.Count < 6)
            {
                ApplyDefaults(b, tagRidge, hl * 2, hs * 2);
                SetRidgeDir(b, u);
                return;
            }

            var sorted = new List<float>(vals); sorted.Sort();
            float p10 = Pct(sorted, 0.10f), p50 = Pct(sorted, 0.50f), p95 = Pct(sorted, 0.95f);
            float range = p95 - p10;
            b.HeightMeasured = true;
            SetRidgeDir(b, u);

            RoofShape shape = b.Roof == (RoofShape)255 ? ClassifyRoof(vals, along, across, hl, hs, range, p10, p95) : b.Roof;
            if (range < 1.2f && (b.Roof == (RoofShape)255 || b.Roof == RoofShape.Flat)) shape = RoofShape.Flat;
            b.Roof = shape;

            if (shape == RoofShape.Flat)
            {
                b.EaveHeight = Math.Max(2.4f, p50 - ground);
                b.RidgeHeight = b.EaveHeight;
            }
            else
            {
                b.EaveHeight = Math.Max(2.4f, p10 - ground);
                b.RidgeHeight = Math.Max(b.EaveHeight + 0.5f, p95 - ground);
            }
            // a ridge ray-cast from measured data can never be below the eave; clamp absurd values (cranes, antennas)
            if (b.RidgeHeight > 150f) b.RidgeHeight = 150f;
        }

        static void SetRidgeDir(BuildingFeature b, Vec2d u) { b.RidgeDirX = (float)u.X; b.RidgeDirY = (float)u.Y; }

        static float Pct(List<float> s, float p) => s[Math.Min(s.Count - 1, (int)(p * (s.Count - 1) + 0.5f))];

        static double DistToBoundary(PolygonWithHoles p, double e, double n)
        {
            double d = double.MaxValue; var q = new Vec2d(e, n);
            d = Math.Min(d, RingDist(p.Outer, q));
            foreach (var h in p.Holes) d = Math.Min(d, RingDist(h, q));
            return d;
        }

        static double RingDist(List<Vec2d> r, Vec2d q)
        {
            double d = double.MaxValue;
            for (int i = 0; i < r.Count; i++) d = Math.Min(d, PolygonUtil.DistToSegment(q, r[i], r[(i + 1) % r.Count]));
            return d;
        }

        static RoofShape ClassifyRoof(List<float> vals, List<double> along, List<double> across, double hl, double hs, float range, float p10, float p95)
        {
            if (range < 1.2f) return RoofShape.Flat;
            // mean height in 3 bins across the short axis and 3 bins along the long axis
            double[] sa = new double[3]; int[] na = new int[3]; double[] sl = new double[3]; int[] nl = new int[3];
            for (int i = 0; i < vals.Count; i++)
            {
                int ba = (int)Math.Min(2, Math.Max(0, (across[i] / (hs * 2) + 0.5) * 3));
                int bl = (int)Math.Min(2, Math.Max(0, (along[i] / (hl * 2) + 0.5) * 3));
                sa[ba] += vals[i]; na[ba]++; sl[bl] += vals[i]; nl[bl]++;
            }
            if (na[0] == 0 || na[1] == 0 || na[2] == 0) return RoofShape.Gabled;
            double a0 = sa[0] / na[0], a1 = sa[1] / na[1], a2 = sa[2] / na[2];
            double sides = (a0 + a2) * 0.5;
            bool peak = a1 - sides > 0.35 * range;
            bool slope = Math.Abs(a0 - a2) > 0.45 * range && !peak;
            if (slope) return RoofShape.Skillion;
            if (!peak) return range > 2.5f ? RoofShape.Hipped : RoofShape.Flat;
            bool squareish = hl / Math.Max(0.1, hs) < 1.25;
            if (squareish) return RoofShape.Pyramidal;
            if (nl[0] > 0 && nl[1] > 0 && nl[2] > 0)
            {
                double l0 = sl[0] / nl[0], l1 = sl[1] / nl[1], l2 = sl[2] / nl[2];
                if (l1 - (l0 + l2) * 0.5 > 0.35 * range) return RoofShape.Hipped;
            }
            return RoofShape.Gabled;
        }

        /// <summary>Type based fallback when neither a measurement nor a complete OSM height exists.</summary>
        static void ApplyDefaults(BuildingFeature b, float tagRidge, double length, double width)
        {
            float eave, ridge; RoofShape roof;
            switch (b.Kind)
            {
                case BuildingKind.House: eave = 5.6f; ridge = 9.2f; roof = RoofShape.Gabled; break;
                case BuildingKind.Apartments: eave = 12f; ridge = 12.4f; roof = RoofShape.Flat; break;
                case BuildingKind.Commercial: eave = 9f; ridge = 9.4f; roof = RoofShape.Flat; break;
                case BuildingKind.Industrial: eave = 8f; ridge = 8.4f; roof = RoofShape.Flat; break;
                case BuildingKind.Church: eave = 12f; ridge = 22f; roof = RoofShape.Gabled; break;
                case BuildingKind.Public: eave = 10f; ridge = 14f; roof = RoofShape.Hipped; break;
                case BuildingKind.Garage: eave = 2.7f; ridge = 2.9f; roof = RoofShape.Flat; break;
                case BuildingKind.Shed: eave = 3f; ridge = 4.6f; roof = RoofShape.Gabled; break;
                case BuildingKind.Tower: eave = 25f; ridge = 25f; roof = RoofShape.Flat; break;
                default: eave = 6f; ridge = 9f; roof = RoofShape.Gabled; break;
            }
            if (b.Levels > 0) { eave = b.Levels * 3.0f; ridge = eave + (roof == RoofShape.Flat ? 0.3f : Math.Min(5f, (float)width * 0.35f)); }
            if (tagRidge > 0.5f) { ridge = tagRidge; if (eave > ridge) eave = ridge; if (roof != RoofShape.Flat) eave = Math.Min(eave, Math.Max(2.4f, ridge - Math.Min(5f, (float)width * 0.35f))); }
            if (b.Roof != (RoofShape)255) roof = b.Roof; 
            if (roof == RoofShape.Flat) ridge = eave;
            b.EaveHeight = eave; b.RidgeHeight = ridge; b.Roof = roof;
        }
    }

    /// <summary>Removes building outlines that are fully covered by building:part polygons and exact duplicates.</summary>
    public static class BuildingDeduper
    {
        public static List<BuildingFeature> Run(List<BuildingFeature> all)
        {
            var parts = new List<BuildingFeature>(); var outlines = new List<BuildingFeature>();
            foreach (var b in all) (b.IsPart ? parts : outlines).Add(b);
            if (parts.Count == 0) return all;

            var grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < outlines.Count; i++)
            {
                outlines[i].Footprint.Bounds(out double x0, out double y0, out double x1, out double y1);
                for (int gx = (int)(x0 / 100); gx <= (int)(x1 / 100); gx++)
                    for (int gy = (int)(y0 / 100); gy <= (int)(y1 / 100); gy++)
                    {
                        long k = ((long)gx << 32) ^ (uint)gy;
                        if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                        l.Add(i);
                    }
            }
            var covered = new double[outlines.Count];
            foreach (var p in parts)
            {
                var c = PolygonUtil.Centroid(p.Footprint.Outer);
                long k = ((long)(int)(c.X / 100) << 32) ^ (uint)(int)(c.Y / 100);
                if (!grid.TryGetValue(k, out var cand)) continue;
                foreach (int i in cand)
                    if (outlines[i].Footprint.Contains(c.X, c.Y)) { covered[i] += p.Footprint.Area(); break; }
            }
            var res = new List<BuildingFeature>(parts);
            for (int i = 0; i < outlines.Count; i++)
                if (covered[i] < 0.8 * outlines[i].Footprint.Area()) res.Add(outlines[i]);
            return res;
        }
    }
}
