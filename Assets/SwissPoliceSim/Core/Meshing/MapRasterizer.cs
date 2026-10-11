using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Terrain;

namespace SwissPoliceSim.Core.Meshing
{
    /// <summary>Navigation-style map textures: one tile (1 km) at <c>res</c> px, or a macro block (8 km) for the zoomed-out country view. RGBA, row 0 = south (Unity texture layout).</summary>
    public static class MapRasterizer
    {
        public static byte[] RenderTile(TileContent t, int res = 512)
        {
            var px = new byte[res * res * 4]; double s = res / 1000.0;     // px per metre
            double te = SwissPoliceSim.Core.Geo.TileGrid.MinE(t.Tile), tn = SwissPoliceSim.Core.Geo.TileGrid.MinN(t.Tile);
            // land cover from the 128×128 raster, lightly smoothed by bilinear lookup
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    byte cls = 0; if (t.LandUse != null && t.LandUse.Length >= 128 * 128) cls = t.LandUse[Math.Min(127, y * 128 / res) * 128 + Math.Min(127, x * 128 / res)];
                    var c = LandColour(cls); float shade = 1f;
                    if (t.Terrain != null)
                    {   // faint hillshade
                        double lx = (x + 0.5) / s, lz = (y + 0.5) / s; float h0 = t.Terrain.SampleLocal(lx - 6, lz), h1 = t.Terrain.SampleLocal(lx + 6, lz), v0 = t.Terrain.SampleLocal(lx, lz - 6), v1 = t.Terrain.SampleLocal(lx, lz + 6);
                        float slopeLight = ((h0 - h1) + (v1 - v0)) * 0.004f; shade = Math.Max(0.82f, Math.Min(1.12f, 1f + slopeLight));
                    }
                    int i = (y * res + x) * 4; px[i] = Cl(c[0] * shade); px[i + 1] = Cl(c[1] * shade); px[i + 2] = Cl(c[2] * shade); px[i + 3] = 255;
                }
            // buildings
            foreach (var b in t.Buildings) FillPolygon(px, res, b.Footprint.Outer, te, tn, s, b.Kind == BuildingKind.Industrial ? new byte[] { 210, 206, 214 } : new byte[] { 218, 210, 200 }, new byte[] { 190, 182, 172 });
            // rails
            foreach (var r in t.Rails) { if (r.Tunnel) continue; Line(px, res, r.E, r.N, te, tn, s, 1.6f, new byte[] { 120, 120, 128 }); }
            // roads: casing first, then fill, so that crossings look right
            var order = new List<RoadWay>(t.Roads); order.Sort((a, b) => b.Class.CompareTo(a.Class));        // minor roads first
            foreach (var w in order) { if (w.Tunnel) continue; var st = RoadStyle(w); Line(px, res, w.E, w.N, te, tn, s, st.width + 1.6f, st.casing); }
            foreach (var w in order) { if (w.Tunnel) continue; var st = RoadStyle(w); Line(px, res, w.E, w.N, te, tn, s, st.width, st.fill); }
            return px;
        }

        /// <summary>8 km macro block → texture (res×res). Land-use colours with hillshading from the 62.5 m heights.</summary>
        public static byte[] RenderMacro(MacroBlock b, int res = 512)
        {
            var px = new byte[res * res * 4];
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    double e = b.MinE + (x + 0.5) / res * MacroBlock.BlockSize, n = b.MinN + (y + 0.5) / res * MacroBlock.BlockSize;
                    byte cls = b.Land[Math.Min(127, y * 128 / res) * 128 + Math.Min(127, x * 128 / res)];
                    float h = b.Sample(e, n), h1 = b.Sample(e + 60, n), h2 = b.Sample(e, n + 60), h3 = b.Sample(e - 60, n), h4 = b.Sample(e, n - 60);
                    float shade = Math.Max(0.55f, Math.Min(1.25f, 1f + ((h3 - h1) + (h2 - h4)) * 0.006f));
                    var c = LandColour(cls); if (h > 2600) { c = new byte[] { 238, 242, 248 }; }
                    else if (h > 2000 && cls != (byte)LandUseClass.Water) { float k = (h - 2000) / 600f; c = new[] { (byte)(c[0] + (240 - c[0]) * k * 0.5f), (byte)(c[1] + (242 - c[1]) * k * 0.5f), (byte)(c[2] + (246 - c[2]) * k * 0.5f) }; }
                    int i = (y * res + x) * 4; px[i] = Cl(c[0] * shade); px[i + 1] = Cl(c[1] * shade); px[i + 2] = Cl(c[2] * shade); px[i + 3] = 255;
                }
            return px;
        }

        public static byte[] LandColour(byte cls)
        {
            switch ((LandUseClass)cls)
            {
                case LandUseClass.Forest: return new byte[] { 198, 220, 186 }; case LandUseClass.Farmland: return new byte[] { 238, 232, 200 }; case LandUseClass.Orchard: case LandUseClass.Vineyard: return new byte[] { 222, 232, 190 };
                case LandUseClass.Park: case LandUseClass.Sports: case LandUseClass.Cemetery: return new byte[] { 206, 230, 190 }; case LandUseClass.Grass: case LandUseClass.Wetland: return new byte[] { 224, 236, 205 };
                case LandUseClass.Residential: return new byte[] { 238, 234, 228 }; case LandUseClass.Commercial: return new byte[] { 236, 226, 222 }; case LandUseClass.Industrial: return new byte[] { 230, 226, 234 };
                case LandUseClass.Rock: case LandUseClass.Scree: return new byte[] { 214, 210, 204 }; case LandUseClass.Glacier: return new byte[] { 240, 246, 252 }; case LandUseClass.Sand: return new byte[] { 240, 232, 205 };
                case LandUseClass.Water: return new byte[] { 160, 196, 232 }; case LandUseClass.Parking: case LandUseClass.Plaza: case LandUseClass.Urban: return new byte[] { 236, 232, 226 }; case LandUseClass.Railway: return new byte[] { 226, 224, 222 };
                default: return new byte[] { 238, 238, 232 };
            }
        }

        static (float width, byte[] fill, byte[] casing) RoadStyle(RoadWay w)
        {
            switch (w.Class)
            {
                case RoadClass.Motorway: case RoadClass.MotorwayLink: return (6.5f, new byte[] { 246, 168, 108 }, new byte[] { 200, 120, 70 });
                case RoadClass.Trunk: case RoadClass.TrunkLink: return (5.5f, new byte[] { 250, 196, 120 }, new byte[] { 196, 150, 80 });
                case RoadClass.Primary: case RoadClass.PrimaryLink: return (5.0f, new byte[] { 252, 222, 130 }, new byte[] { 190, 165, 100 });
                case RoadClass.Secondary: case RoadClass.SecondaryLink: return (4.2f, new byte[] { 255, 240, 170 }, new byte[] { 186, 176, 120 });
                case RoadClass.Tertiary: case RoadClass.TertiaryLink: return (3.4f, new byte[] { 255, 255, 255 }, new byte[] { 175, 175, 175 });
                case RoadClass.Residential: case RoadClass.Unclassified: case RoadClass.LivingStreet: return (2.6f, new byte[] { 255, 255, 255 }, new byte[] { 190, 190, 190 });
                case RoadClass.Service: case RoadClass.Track: return (1.6f, new byte[] { 250, 250, 250 }, new byte[] { 205, 205, 205 });
                default: return (1.2f, new byte[] { 232, 190, 180 }, new byte[] { 232, 190, 180 });     // foot/cycle paths
            }
        }

        static byte Cl(float v) => (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));

        static void Line(byte[] px, int res, double[] e, double[] n, double te, double tn, double s, float widthPx, byte[] col)
        {
            for (int i = 0; i + 1 < e.Length; i++)
            {
                double x0 = (e[i] - te) * s, y0 = (n[i] - tn) * s, x1 = (e[i + 1] - te) * s, y1 = (n[i + 1] - tn) * s;
                if ((x0 < -10 && x1 < -10) || (y0 < -10 && y1 < -10) || (x0 > res + 10 && x1 > res + 10) || (y0 > res + 10 && y1 > res + 10)) continue;
                double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy); int steps = Math.Max(1, (int)(len / 0.5));
                float rad = widthPx * 0.5f;
                for (int k = 0; k <= steps; k++) { double t = k / (double)steps; Disc(px, res, x0 + dx * t, y0 + dy * t, rad, col); }
            }
        }

        static void Disc(byte[] px, int res, double cx, double cy, float r, byte[] col)
        {
            int x0 = (int)Math.Floor(cx - r - 1), x1 = (int)Math.Ceiling(cx + r + 1), y0 = (int)Math.Floor(cy - r - 1), y1 = (int)Math.Ceiling(cy + r + 1);
            for (int y = Math.Max(0, y0); y <= Math.Min(res - 1, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(res - 1, x1); x++)
                {
                    double d = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy)); float a = (float)Math.Max(0, Math.Min(1, r + 0.5 - d)); if (a <= 0) continue;
                    int i = (y * res + x) * 4; px[i] = (byte)(px[i] + (col[0] - px[i]) * a); px[i + 1] = (byte)(px[i + 1] + (col[1] - px[i + 1]) * a); px[i + 2] = (byte)(px[i + 2] + (col[2] - px[i + 2]) * a);
                }
        }

        static void FillPolygon(byte[] px, int res, List<SwissPoliceSim.Core.Geometry.Vec2d> ring, double te, double tn, double s, byte[] fill, byte[] edge)
        {
            if (ring.Count < 3) return; double minY = double.MaxValue, maxY = double.MinValue;
            var xs = new double[ring.Count]; var ys = new double[ring.Count];
            for (int i = 0; i < ring.Count; i++) { xs[i] = (ring[i].X - te) * s; ys[i] = (ring[i].Y - tn) * s; minY = Math.Min(minY, ys[i]); maxY = Math.Max(maxY, ys[i]); }
            if (maxY < 0 || minY > res) return;
            var cross = new List<double>();
            for (int y = Math.Max(0, (int)Math.Floor(minY)); y <= Math.Min(res - 1, (int)Math.Ceiling(maxY)); y++)
            {
                cross.Clear(); double sy = y + 0.5;
                for (int i = 0; i < ring.Count; i++) { int j = (i + 1) % ring.Count; double ya = ys[i], yb = ys[j]; if ((ya <= sy && yb > sy) || (yb <= sy && ya > sy)) cross.Add(xs[i] + (sy - ya) / (yb - ya) * (xs[j] - xs[i])); }
                cross.Sort();
                for (int k = 0; k + 1 < cross.Count; k += 2)
                    for (int x = Math.Max(0, (int)Math.Ceiling(cross[k] - 0.5)); x <= Math.Min(res - 1, (int)Math.Floor(cross[k + 1] - 0.5)); x++)
                    { int i = (y * res + x) * 4; bool border = x <= cross[k] + 0.5 || x >= cross[k + 1] - 1.5; px[i] = border ? edge[0] : fill[0]; px[i + 1] = border ? edge[1] : fill[1]; px[i + 2] = border ? edge[2] : fill[2]; }
            }
        }
    }
}
