using System;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Geometry;
using SwissPoliceSim.Core.Roads;
using UnityEngine;

namespace SwissPoliceSim.World
{
    public struct RoadInfo { public RoadWay Way; public double Distance; public int SpeedLimitKmh; public string Name; public SpeedSource Basis; public bool Urban; public int Segment; }

    /// <summary>Spatial queries on the loaded tiles: nearest road, legal speed limit and street name at a position (used by HUD, radar and incident logic).</summary>
    public static class WorldQuery
    {
        public static bool NearestRoad(TileStreamer streamer, double e, double n, double maxDist, out RoadInfo info, bool vehicularOnly = true)
        {
            info = default; if (streamer == null) return false; double best = maxDist * maxDist; RoadWay bw = null; int bseg = 0;
            var c = TileGrid.FromLv95(e, n);
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
            {
                if (!streamer.TryGetTile(new TileId(c.X + dx, c.Y + dy), out var t)) continue;
                double lx0 = TileGrid.MinE(t.Id) - maxDist, lx1 = TileGrid.MinE(t.Id) + 1000 + maxDist, ly0 = TileGrid.MinN(t.Id) - maxDist, ly1 = TileGrid.MinN(t.Id) + 1000 + maxDist; if (e < lx0 || e > lx1 || n < ly0 || n > ly1) continue;
                foreach (var w in t.Content.Roads)
                {
                    if (vehicularOnly && !w.IsVehicular) continue;
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

        /// <summary>Canton / municipality names for a Unity position.</summary>
        public static string PlaceName(Vector3 unityPos)
        {
            if (WorldData.Admin == null) return ""; var p = WorldOrigin.ToLv95(unityPos); var m = WorldData.Admin.FindMunicipality(p.E, p.N); var c = WorldData.Admin.FindCanton(p.E, p.N);
            if (m == null) return c != null ? c.Name : ""; return m.Name + (c != null ? " (" + AdminBoundaries.CantonAbbr[c.Id] + ")" : "");
        }
    }
}
