using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Geometry;
using SwissPoliceSim.Core.Terrain;
using SwissPoliceSim.Core.Meshing;

namespace SwissPoliceSim.Core.Features
{
    /// <summary>Water surface level, terrain carving of river beds / lake shores and water meshes.</summary>
    public static class WaterModel
    {
        /// <summary>Water surface height at a point inside a water polygon (lowest DEM value in the surrounding water + 0.35 m).</summary>
        public static float SurfaceLevel(WaterFeature w, IHeightSource dem, double e, double n)
        {
            float min = float.MaxValue; int found = 0;
            for (double de = -12; de <= 12; de += 4)
                for (double dn = -12; dn <= 12; dn += 4)
                {
                    if (!w.Polygon.Contains(e + de, n + dn)) continue;
                    if (dem.TrySample(e + de, n + dn, out float h)) { found++; if (h < min) min = h; }
                }
            if (found == 0) { if (!dem.TrySample(e, n, out min)) return float.NaN; }
            return min + 0.35f;
        }

        /// <summary>Distance (m) from a point inside the polygon to its boundary; negative if outside.</summary>
        public static double SignedDistance(PolygonWithHoles p, double e, double n)
        {
            double d = double.MaxValue; var q = new Vec2d(e, n);
            for (int i = 0; i < p.Outer.Count; i++) d = Math.Min(d, PolygonUtil.DistToSegment(q, p.Outer[i], p.Outer[(i + 1) % p.Outer.Count]));
            foreach (var h in p.Holes) for (int i = 0; i < h.Count; i++) d = Math.Min(d, PolygonUtil.DistToSegment(q, h[i], h[(i + 1) % h.Count]));
            return p.Contains(e, n) ? d : -d;
        }

        /// <summary>
        /// Wraps a height source so water bodies are carved: inside the polygon the ground drops below the water surface
        /// (up to 1.6 m at the centre, 0.5 m/m bank slope), so the water mesh never z-fights with the terrain.
        /// </summary>
        public sealed class CarvedHeightSource : IHeightSource
        {
            readonly IHeightSource _dem; readonly List<WaterFeature> _water;
            readonly List<double[]> _bounds = new List<double[]>();
            public CarvedHeightSource(IHeightSource dem, List<WaterFeature> water)
            {
                _dem = dem; _water = water;
                foreach (var w in water) { w.Polygon.Bounds(out double a, out double b, out double c, out double d); _bounds.Add(new[] { a - 4, b - 4, c + 4, d + 4 }); }
            }

            public bool TrySample(double e, double n, out float height)
            {
                if (!_dem.TrySample(e, n, out height)) return false;
                for (int i = 0; i < _water.Count; i++)
                {
                    var b = _bounds[i];
                    if (e < b[0] || e > b[2] || n < b[1] || n > b[3]) continue;
                    var w = _water[i];
                    double d = SignedDistance(w.Polygon, e, n);
                    if (d < -2.0) continue;
                    float level = float.IsNaN(w.SurfaceHeight) ? SurfaceLevel(w, _dem, e, n) : w.SurfaceHeight;
                    if (float.IsNaN(level)) continue;
                    // depth profile: 0 at 2 m outside the shore .. full depth 3.2 m inside
                    double t = Math.Max(0, Math.Min(1, (d + 2.0) / 5.2));
                    float bed = level - 0.15f - (float)(1.5 * t);
                    if (height > bed) height = Math.Min(height, bed + (float)((1 - t) * (height - bed)));
                }
                return true;
            }
        }

        public const float GridStep = 8f;

        /// <summary>Grid mesh of a water polygon (cells overlapping the polygon), tile-local coordinates; submesh 0.</summary>
        public static void BuildMesh(WaterFeature w, IHeightSource dem, double tileE, double tileN, MeshData m)
        {
            w.Polygon.Bounds(out double x0, out double y0, out double x1, out double y1);
            double ox = Math.Max(x0, tileE) , oy = Math.Max(y0, tileN);
            double ex = Math.Min(x1, tileE + 1000), ey = Math.Min(y1, tileN + 1000);
            if (ex <= ox || ey <= oy) return;
            int nx = Math.Max(1, (int)Math.Ceiling((ex - ox) / GridStep)), ny = Math.Max(1, (int)Math.Ceiling((ey - oy) / GridStep));
            double sx = (ex - ox) / nx, sy = (ey - oy) / ny;
            var idx = new int[(nx + 1) * (ny + 1)];
            var used = new bool[nx * ny];
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    // include the cell if any corner / centre is inside (or within 3 m): shore overlap is hidden by the carved terrain banks
                    bool inside = false;
                    for (int k = 0; k < 5 && !inside; k++)
                    {
                        double px = ox + (i + (k == 4 ? 0.5 : k & 1)) * sx, py = oy + (j + (k == 4 ? 0.5 : (k >> 1) & 1)) * sy;
                        inside = SignedDistance(w.Polygon, px, py) > -3.0;
                    }
                    used[j * nx + i] = inside;
                }
            for (int k = 0; k < idx.Length; k++) idx[k] = -1;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    if (!used[j * nx + i]) continue;
                    for (int dj = 0; dj <= 1; dj++)
                        for (int di = 0; di <= 1; di++)
                        {
                            int vi = (j + dj) * (nx + 1) + i + di;
                            if (idx[vi] >= 0) continue;
                            double px = ox + (i + di) * sx, py = oy + (j + dj) * sy;
                            float lvl = float.IsNaN(w.SurfaceHeight) ? SurfaceLevel(w, dem, px, py) : w.SurfaceHeight;
                            if (float.IsNaN(lvl)) lvl = 0;
                            idx[vi] = m.AddVertex((float)(px - tileE), lvl, (float)(py - tileN), 0, 1, 0, (float)(px * 0.05), (float)(py * 0.05), 255, 255, 255, 255);
                        }
                    int a = idx[j * (nx + 1) + i], b = idx[j * (nx + 1) + i + 1], c = idx[(j + 1) * (nx + 1) + i + 1], d = idx[(j + 1) * (nx + 1) + i];
                    m.AddTriangle(0, a, c, b); m.AddTriangle(0, a, d, c);   // up-facing in Unity winding
                }
        }

        /// <summary>Ribbon mesh for a stream / small river line (submesh 0).</summary>
        public static void BuildWaterwayMesh(WaterwayFeature w, IHeightSource dem, double tileE, double tileN, MeshData m)
        {
            int n = w.E.Length; if (n < 2) return;
            var lvl = new float[n];
            for (int i = 0; i < n; i++) { dem.TrySample(w.E[i], w.N[i], out float h); lvl[i] = h; }
            // rivers/streams flow downhill: enforce a monotone profile (running minimum from the upstream end is unknown, so smooth)
            for (int i = 1; i < n - 1; i++) lvl[i] = (lvl[i - 1] + lvl[i] + lvl[i + 1]) / 3f;
            float half = w.Width * 0.5f;
            int baseIdx = m.VertexCount; double dist = 0;
            for (int i = 0; i < n; i++)
            {
                int pa = Math.Max(0, i - 1), pb = Math.Min(n - 1, i + 1);
                double tx = w.E[pb] - w.E[pa], tz = w.N[pb] - w.N[pa], l = Math.Sqrt(tx * tx + tz * tz); if (l < 1e-6) { tx = 1; tz = 0; l = 1; }
                tx /= l; tz /= l; double nx = -tz, nz = tx;
                if (i > 0) dist += Math.Sqrt(Math.Pow(w.E[i] - w.E[i - 1], 2) + Math.Pow(w.N[i] - w.N[i - 1], 2));
                float y = lvl[i] + 0.12f;
                m.AddVertex((float)(w.E[i] + nx * half - tileE), y, (float)(w.N[i] + nz * half - tileN), 0, 1, 0, 0, (float)(dist * 0.1), 255, 255, 255);
                m.AddVertex((float)(w.E[i] - nx * half - tileE), y, (float)(w.N[i] - nz * half - tileN), 0, 1, 0, 1, (float)(dist * 0.1), 255, 255, 255);
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = baseIdx + i * 2;
                m.AddTriangle(0, a, a + 2, a + 1); m.AddTriangle(0, a + 1, a + 2, a + 3);
            }
        }
    }
}
