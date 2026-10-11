using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geometry;

namespace SwissPoliceSim.Core.Meshing
{
    /// <summary>Finds outer-ring edges that coincide with an edge of a neighbouring building (terraced houses, blocks): they get no windows.</summary>
    public static class PartyWalls
    {
        public static Dictionary<BuildingFeature, bool[]> Compute(List<BuildingFeature> buildings, double tolerance = 0.4)
        {
            const double cell = 12;
            var grid = new Dictionary<long, List<(int b, Vec2d a, Vec2d c)>>();
            long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
            for (int bi = 0; bi < buildings.Count; bi++)
            {
                var r = buildings[bi].Footprint.Outer;
                for (int i = 0; i < r.Count; i++)
                {
                    var a = r[i]; var c = r[(i + 1) % r.Count];
                    int x0 = (int)Math.Floor(Math.Min(a.X, c.X) / cell), x1 = (int)Math.Floor(Math.Max(a.X, c.X) / cell), y0 = (int)Math.Floor(Math.Min(a.Y, c.Y) / cell), y1 = (int)Math.Floor(Math.Max(a.Y, c.Y) / cell);
                    for (int x = x0; x <= x1; x++) for (int y = y0; y <= y1; y++)
                    { if (!grid.TryGetValue(Key(x, y), out var l)) grid[Key(x, y)] = l = new List<(int, Vec2d, Vec2d)>(); l.Add((bi, a, c)); }
                }
            }
            var res = new Dictionary<BuildingFeature, bool[]>();
            for (int bi = 0; bi < buildings.Count; bi++)
            {
                var r = buildings[bi].Footprint.Outer; var flags = new bool[r.Count];
                for (int i = 0; i < r.Count; i++)
                {
                    var a = r[i]; var c = r[(i + 1) % r.Count]; var d = c - a; double len = d.Length; if (len < 0.3) continue;
                    var dir = new Vec2d(d.X / len, d.Y / len);
                    int matched = 0;
                    for (int s = 1; s <= 4; s++)
                    {
                        var p = a + d * (s / 5.0);
                        if (grid.TryGetValue(Key((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell)), out var l))
                            foreach (var e in l)
                            {
                                if (e.b == bi) continue;
                                var ed = e.c - e.a; double el = ed.Length; if (el < 0.3) continue;
                                if (Math.Abs((ed.X * dir.X + ed.Y * dir.Y) / el) < 0.97) continue;          // not parallel
                                if (PolygonUtil.DistToSegment(p, e.a, e.c) <= tolerance) { matched++; break; }
                            }
                    }
                    if (matched >= 3) { flags[i] = true; }
                }
                res[buildings[bi]] = flags;
            }
            return res;
        }
    }
}
