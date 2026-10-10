using System;
using SwissPoliceSim.Core.Terrain;

namespace SwissPoliceSim.Core.Roads
{
    /// <summary>
    /// Assigns surface heights to road points from the DEM. Bridges are linearly interpolated between their end
    /// points (so e.g. a bridge over the Aare stays level instead of following the valley floor).
    /// Tunnels currently follow the terrain surface (Phase 3: real tunnel bores + terrain holes).
    /// Must run on the WHOLE way, before it is split into tile chunks.
    /// </summary>
    public static class RoadElevation
    {
        public static void Assign(RoadWay w, IHeightSource dem)
        {
            int n = w.PointCount;
            var h = new double[n];
            for (int i = 0; i < n; i++)
                h[i] = dem.TrySample(w.E[i], w.N[i], out float v) ? v : double.NaN;

            if (w.Bridge && n >= 2 && !double.IsNaN(h[0]) && !double.IsNaN(h[n - 1]))
            {
                var cum = new double[n];
                for (int i = 1; i < n; i++)
                    cum[i] = cum[i - 1] + Math.Sqrt(Math.Pow(w.E[i] - w.E[i - 1], 2) + Math.Pow(w.N[i] - w.N[i - 1], 2));
                double total = cum[n - 1];
                if (total > 1e-6)
                    for (int i = 1; i < n - 1; i++)
                    {
                        double t = cum[i] / total;
                        double lin = h[0] + (h[n - 1] - h[0]) * t;
                        h[i] = lin;
                    }
            }
            w.H = h;
        }
    }
}
