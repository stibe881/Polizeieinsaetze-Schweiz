using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Terrain;

namespace SwissPoliceSim.Core.Roads
{
    /// <summary>
    /// Terrain wrapper that flattens the ground across each road: inside the road width the ground takes the road's own
    /// (baked) height, blending back to the natural terrain over a few metres (cuts and embankments). Bridges and tunnels are skipped.
    /// Guarantees the road mesh never sinks into the heightmap and removes the cross-slope that would make cars tilt.
    /// </summary>
    public sealed class RoadCorridorHeightSource : IHeightSource
    {
        struct Seg { public double Ax, Ay, Bx, By, Ha, Hb, Half; }
        readonly IHeightSource _inner; readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();
        readonly List<Seg> _segs = new List<Seg>();
        const double Cell = 32, Blend = 4.5;

        public RoadCorridorHeightSource(IHeightSource inner, IEnumerable<RoadWay> roads)
        {
            _inner = inner;
            foreach (var w in roads)
            {
                if (w.Bridge || w.Tunnel || w.H.Length != w.PointCount || w.Class == RoadClass.Track) continue;
                double half = w.VisualWidth() * 0.5 + (w.Class >= RoadClass.Primary && w.Class != RoadClass.Service ? 0.3 : 0.0);
                for (int i = 0; i < w.PointCount - 1; i++)
                {
                    if (double.IsNaN(w.H[i]) || double.IsNaN(w.H[i + 1])) continue;
                    int id = _segs.Count;
                    _segs.Add(new Seg { Ax = w.E[i], Ay = w.N[i], Bx = w.E[i + 1], By = w.N[i + 1], Ha = w.H[i], Hb = w.H[i + 1], Half = half });
                    int x0 = (int)Math.Floor((Math.Min(w.E[i], w.E[i + 1]) - half - Blend) / Cell), x1 = (int)Math.Floor((Math.Max(w.E[i], w.E[i + 1]) + half + Blend) / Cell);
                    int y0 = (int)Math.Floor((Math.Min(w.N[i], w.N[i + 1]) - half - Blend) / Cell), y1 = (int)Math.Floor((Math.Max(w.N[i], w.N[i + 1]) + half + Blend) / Cell);
                    for (int gx = x0; gx <= x1; gx++)
                        for (int gy = y0; gy <= y1; gy++)
                        {
                            long k = ((long)gx << 32) ^ (uint)gy;
                            if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new List<int>();
                            l.Add(id);
                        }
                }
            }
        }

        public bool TrySample(double e, double n, out float height)
        {
            if (!_inner.TrySample(e, n, out height)) return false;
            long k = ((long)(int)Math.Floor(e / Cell) << 32) ^ (uint)(int)Math.Floor(n / Cell);
            if (!_grid.TryGetValue(k, out var list)) return true;
            double bestW = 0, bestH = 0;
            foreach (int id in list)
            {
                var s = _segs[id];
                double dx = s.Bx - s.Ax, dy = s.By - s.Ay, l2 = dx * dx + dy * dy; if (l2 < 1e-9) continue;
                double t = Math.Max(0, Math.Min(1, ((e - s.Ax) * dx + (n - s.Ay) * dy) / l2));
                double px = s.Ax + dx * t - e, py = s.Ay + dy * t - n, d = Math.Sqrt(px * px + py * py);
                if (d > s.Half + Blend) continue;
                double w = d <= s.Half + 0.4 ? 1.0 : 1.0 - (d - s.Half - 0.4) / (Blend - 0.4);
                w = w * w * (3 - 2 * w);
                if (w > bestW) { bestW = w; bestH = s.Ha + (s.Hb - s.Ha) * t - 0.10; }   // 0.10 = asphalt lift, so the surface sits exactly on the road height
            }
            if (bestW > 0) height = (float)(height + (bestH - height) * bestW);
            return true;
        }
    }
}
