using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Geometry;
using SwissPoliceSim.Core.Terrain;

namespace SwissPoliceSim.Core.Features
{
    public struct TreeInstance
    {
        public float E, N, Height, Radius; public byte Kind;   // Kind: 0 broadleaf, 1 conifer
    }

    /// <summary>
    /// Finds individual trees as local maxima of the normalised surface model (swissSURFACE3D − swissALTI3D, 1 m grid)
    /// outside building footprints. Positions, heights and crown radii are measured, not random.
    /// </summary>
    public static class TreeDetector
    {
        const int N = SurfaceGrid.Size;

        public static List<TreeInstance> Detect(TileId tile, IHeightSource dem, SurfaceMosaic surface, IList<BuildingFeature> buildingsNearTile)
        {
            double e0 = TileGrid.MinE(tile), n0 = TileGrid.MinN(tile);
            var h = new float[N * N];
            for (int r = 0; r < N; r++)
                for (int c = 0; c < N; c++)
                {
                    double e = e0 + c + 0.5, n = n0 + r + 0.5;
                    float v = surface.NormalizedHeight(dem, e, n);
                    h[r * N + c] = float.IsNaN(v) || v < 0 ? 0 : v;
                }

            // building mask (+1.5 m so roof edges / eaves are not mistaken for trees)
            var blocked = new bool[N * N];
            foreach (var b in buildingsNearTile)
            {
                b.Footprint.Bounds(out double x0, out double y0, out double x1, out double y1);
                if (x1 < e0 - 3 || x0 > e0 + N + 3 || y1 < n0 - 3 || y0 > n0 + N + 3) continue;
                int c0 = Math.Max(0, (int)(x0 - e0 - 2)), c1 = Math.Min(N - 1, (int)(x1 - e0 + 2));
                int r0 = Math.Max(0, (int)(y0 - n0 - 2)), r1 = Math.Min(N - 1, (int)(y1 - n0 + 2));
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                    {
                        double e = e0 + c + 0.5, n = n0 + r + 0.5;
                        if (b.Footprint.Contains(e, n) || NearEdge(b.Footprint, e, n, 1.5)) blocked[r * N + c] = true;
                    }
            }

            // 3x3 box smoothing
            var hs = new float[N * N];
            for (int r = 1; r < N - 1; r++)
                for (int c = 1; c < N - 1; c++)
                {
                    float s = 0;
                    for (int dr = -1; dr <= 1; dr++) for (int dc = -1; dc <= 1; dc++) s += h[(r + dr) * N + c + dc];
                    hs[r * N + c] = s / 9f;
                }

            var list = new List<TreeInstance>();
            const int win = 2;
            for (int r = win; r < N - win; r++)
                for (int c = win; c < N - win; c++)
                {
                    int i = r * N + c;
                    float v = hs[i];
                    if (v < 2.8f || blocked[i]) continue;
                    bool max = true;
                    for (int dr = -win; dr <= win && max; dr++)
                        for (int dc = -win; dc <= win; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
                            float o = hs[(r + dr) * N + c + dc];
                            if (o > v || (o == v && (dr < 0 || (dr == 0 && dc < 0)))) { max = false; break; }
                        }
                    if (!max) continue;

                    float peak = 0;
                    for (int dr = -1; dr <= 1; dr++) for (int dc = -1; dc <= 1; dc++) peak = Math.Max(peak, h[(r + dr) * N + c + dc]);
                    if (peak > 45f) continue;   // cranes, masts

                    // crown radius: walk in 8 directions until height falls below 55 % of the peak
                    double sum = 0;
                    for (int d = 0; d < 8; d++)
                    {
                        double ang = d * Math.PI / 4; double dx = Math.Cos(ang), dy = Math.Sin(ang);
                        int k = 1;
                        for (; k < 10; k++)
                        {
                            int cc = (int)Math.Round(c + dx * k), rr = (int)Math.Round(r + dy * k);
                            if (cc < 0 || rr < 0 || cc >= N || rr >= N || h[rr * N + cc] < 0.55f * peak || blocked[rr * N + cc]) break;
                        }
                        sum += k;
                    }
                    float radius = (float)Math.Max(1.2, Math.Min(8.0, sum / 8.0));
                    list.Add(new TreeInstance
                    {
                        E = (float)(e0 + c + 0.5), N = (float)(n0 + r + 0.5), Height = peak, Radius = radius,
                        Kind = (byte)(radius / peak < 0.26f ? 1 : 0)
                    });
                }
            return list;
        }

        static bool NearEdge(PolygonWithHoles p, double e, double n, double d)
        {
            var q = new Vec2d(e, n);
            for (int i = 0; i < p.Outer.Count; i++)
                if (PolygonUtil.DistToSegment(q, p.Outer[i], p.Outer[(i + 1) % p.Outer.Count]) < d) return true;
            return false;
        }
    }
}
