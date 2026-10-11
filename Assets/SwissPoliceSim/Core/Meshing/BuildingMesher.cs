using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geometry;

namespace SwissPoliceSim.Core.Meshing
{
    /// <summary>Colour palette selection for regional recognisability (e.g. sandstone of the Bern old town).</summary>
    public sealed class BuildingStyle
    {
        public byte[][] WallPalette;     // rgb
        public byte[][] RoofPalette;     // rgb
        public static readonly BuildingStyle Swiss = new BuildingStyle
        {
            WallPalette = new[]
            {
                new byte[] { 238, 232, 220 }, new byte[] { 225, 214, 190 }, new byte[] { 232, 220, 196 }, new byte[] { 214, 200, 175 },
                new byte[] { 205, 205, 200 }, new byte[] { 236, 226, 205 }, new byte[] { 222, 190, 160 }, new byte[] { 190, 160, 130 },
                new byte[] { 245, 245, 240 }, new byte[] { 210, 218, 205 }
            },
            RoofPalette = new[]
            {
                new byte[] { 150, 70, 52 }, new byte[] { 134, 62, 48 }, new byte[] { 112, 58, 46 }, new byte[] { 88, 84, 84 },
                new byte[] { 70, 70, 74 }, new byte[] { 160, 90, 60 }
            }
        };
        /// <summary>Bern old town: grey-green Berner Sandstein, red-brown tiles.</summary>
        public static readonly BuildingStyle Sandstone = new BuildingStyle
        {
            WallPalette = new[] { new byte[] { 168, 172, 150 }, new byte[] { 160, 165, 146 }, new byte[] { 176, 178, 158 }, new byte[] { 150, 156, 140 }, new byte[] { 188, 180, 160 } },
            RoofPalette = new[] { new byte[] { 142, 66, 50 }, new byte[] { 128, 60, 46 }, new byte[] { 150, 74, 54 } }
        };
    }

    public static class BuildingMesher
    {
        public const int WallsResidential = 0, WallsCommercial = 1, WallsIndustrial = 2, RoofPitched = 3, RoofFlat = 4, PlasterWall = 5, WindowSub = 6, TrimSub = 7, ShutterSub = 8, RailingSub = 9, Submeshes = 10;
        const double Pitch = 0.9;      // rise per half-width limit for derived wings (≈ 42°)

        public static MeshData NewMesh() => new MeshData(Submeshes);

        /// <summary>Appends one building (tile-local coordinates, y = m a.s.l.). Returns false if it has no valid ground/height.</summary>
        public static bool Build(BuildingFeature b, double tileE, double tileN, MeshData m, BuildingStyle style, bool detail = false, bool oldTown = false, bool[] partyWall = null)
        {
            if (float.IsNaN(b.GroundHeight) || b.Footprint.Outer.Count < 3) return false;
            var rnd = new Random(b.ColorSeed);
            float eaveH = b.EaveHeight, ridgeH = b.RidgeHeight;
            // measured heights can include overhanging trees / neighbours: plausibility limits per building kind
            if (b.Kind == BuildingKind.Garage || b.Kind == BuildingKind.Shed) { eaveH = Math.Min(eaveH, 4.2f); ridgeH = Math.Min(ridgeH, eaveH + 2.0f); }
            else if (b.Kind == BuildingKind.House || b.Kind == BuildingKind.Generic) { ridgeH = Math.Min(ridgeH, eaveH + 6.5f); }
            float yBase = b.GroundHeight - 0.5f, yEave = b.GroundHeight + eaveH, yRidge = b.GroundHeight + ridgeH;
            var wall = Pick(style.WallPalette, rnd, b.Tag_Colour);
            var roof = Pick(style.RoofPalette, rnd, b.Tag_RoofColour);
            if (b.Kind == BuildingKind.Industrial || b.Kind == BuildingKind.Shed) wall = Tint(new byte[] { 190, 192, 192 }, rnd, 12);
            int wallSub = b.Kind == BuildingKind.Commercial || b.Kind == BuildingKind.Public || b.Kind == BuildingKind.Station || b.Kind == BuildingKind.Apartments ? WallsCommercial
                        : b.Kind == BuildingKind.Industrial || b.Kind == BuildingKind.Shed || b.Kind == BuildingKind.Garage ? WallsIndustrial : WallsResidential;

            // ---- walls
            DetailPlan plan = null;
            if (detail)
            {
                plan = DetailPlan.Make(b, oldTown, b.GroundHeight, yEave, new Random(b.ColorSeed * 31 + 7));
                FacadeDetailer.AddWalls(m, b.Footprint, b.Footprint.Outer, false, tileE, tileN, yBase, b.GroundHeight, yEave, b, plan, oldTown, wall, rnd, partyWall);
                foreach (var h in b.Footprint.Holes) FacadeDetailer.AddWalls(m, b.Footprint, h, true, tileE, tileN, yBase, b.GroundHeight, yEave, b, plan, oldTown, wall, rnd, null);
                wallSub = PlasterWall;
            }
            else
            {
                AddWalls(m, b.Footprint.Outer, tileE, tileN, yBase, yEave, wallSub, wall);
                foreach (var h in b.Footprint.Holes) AddWalls(m, h, tileE, tileN, yBase, yEave, wallSub, wall);
            }

            // ---- roof
            var shape = b.Roof == (RoofShape)255 ? RoofShape.Gabled : b.Roof;
            double rise = yRidge - yEave;
            if (shape == RoofShape.Flat || rise < 0.6 || shape == RoofShape.Dome) { AddFlatCap(m, b.Footprint, tileE, tileN, yEave, roof, flat: true); return true; }

            PolygonUtil.OrientedBox(b.Footprint.Outer, out var centre, out var u, out double hl, out double hs);
            double coverage = b.Footprint.Area() / Math.Max(1e-6, 4 * hl * hs);
            if (coverage >= 0.9 && b.Footprint.Holes.Count == 0)
            {
                AddPitchedRect(m, centre, u, hl, hs, tileE, tileN, yEave, Math.Min(rise, hs * 1.25), shape, 0.35, roof, wall, wallSub);
                if (plan != null && shape == RoofShape.Gabled) RoofDetail.Add(m, plan, centre, u, hl, hs, tileE, tileN, yEave, Math.Min(rise, hs * 1.25), roof, wall, new Random(b.ColorSeed * 17 + 3));
            }
            else
            {
                var rects = RectCover.Compute(b.Footprint, centre, u);
                if (rects.Count == 0) { AddFlatCap(m, b.Footprint, tileE, tileN, yEave, roof, flat: false); return true; }
                // safety cap under the wings so no gap is ever visible from above
                AddFlatCap(m, b.Footprint, tileE, tileN, yEave - 0.05f, roof, flat: false);
                for (int i = 0; i < rects.Count; i++)
                {
                    var r = rects[i];
                    double wingRise = Math.Min(rise, r.HalfShort * Pitch);
                    AddPitchedRect(m, r.Centre, r.AxisLong, r.HalfLong, r.HalfShort, tileE, tileN, yEave, wingRise, shape == RoofShape.Skillion ? RoofShape.Gabled : shape, 0.0, roof, wall, wallSub);
                }
            }
            return true;
        }

        // ------------------------------------------------------------------ walls

        static void AddWalls(MeshData m, List<Vec2d> ring, double tE, double tN, float y0, float y1, int sub, byte[] col)
        {
            double perimeter = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i]; var b = ring[(i + 1) % ring.Count];
                double len = (b - a).Length;
                if (len < 0.05) continue;
                // build quad p1b -> p0b -> p0t -> p1t so the normal faces outward (see MeshData.AddQuad)
                var A = new[] { (float)(b.X - tE), y0, (float)(b.Y - tN) };
                var B = new[] { (float)(a.X - tE), y0, (float)(a.Y - tN) };
                var C = new[] { (float)(a.X - tE), y1, (float)(a.Y - tN) };
                var D = new[] { (float)(b.X - tE), y1, (float)(b.Y - tN) };
                float u0 = (float)(perimeter / 3.2), u1 = (float)((perimeter + len) / 3.2);
                // texture runs left->right as seen from outside: from a to b means u decreases with our reversed order
                m.AddQuad(sub, A, B, C, D, u1, y0 / 3.0f, u0, y1 / 3.0f, col[0], col[1], col[2]);
                perimeter += len;
            }
        }

        // ------------------------------------------------------------------ roofs

        static void AddFlatCap(MeshData m, PolygonWithHoles poly, double tE, double tN, float y, byte[] col, bool flat)
        {
            var tri = Triangulator.Run(poly);
            int sub = flat ? RoofFlat : RoofPitched;
            byte[] c = flat ? new byte[] { 120, 118, 114 } : col;
            int baseIdx = m.VertexCount;
            foreach (var p in tri.Vertices)
                m.AddVertex((float)(p.X - tE), y, (float)(p.Y - tN), 0, 1, 0, (float)(p.X * 0.2), (float)(p.Y * 0.2), c[0], c[1], c[2]);
            for (int i = 0; i < tri.Triangles.Count; i += 3)
                m.AddTriangle(sub, baseIdx + tri.Triangles[i], baseIdx + tri.Triangles[i + 2], baseIdx + tri.Triangles[i + 1]);   // reversed: CCW in (E,N) faces down in Unity
        }

        static float[] P(Vec2d c, Vec2d u, Vec2d v, double a, double b, double y, double tE, double tN)
        {
            var p = c + u * a + v * b;
            return new[] { (float)(p.X - tE), (float)y, (float)(p.Y - tN) };
        }

        static void AddPitchedRect(MeshData m, Vec2d c, Vec2d u, double L, double W, double tE, double tN,
                                   double yEave, double rise, RoofShape shape, double overhang, byte[] roof, byte[] wall, int wallSub)
        {
            var v = new Vec2d(-u.Y, u.X);
            double ov = overhang, Le = L + (ov > 0 ? 0.15 : 0), We = W + ov;
            double slope = rise / Math.Max(0.1, W);
            double yE = yEave - ov * slope;            // eave edge drops with the overhang
            double yR = yEave + rise;
            const float uvs = 0.6f;

            if (shape == RoofShape.Pyramidal || (shape == RoofShape.Hipped && L - W < 0.4))
            {
                var apex = P(c, u, v, 0, 0, yR, tE, tN);
                var c0 = P(c, u, v, -Le, -We, yE, tE, tN); var c1 = P(c, u, v, Le, -We, yE, tE, tN);
                var c2 = P(c, u, v, Le, We, yE, tE, tN); var c3 = P(c, u, v, -Le, We, yE, tE, tN);
                Tri(m, c0, c1, apex, roof, uvs); Tri(m, c1, c2, apex, roof, uvs); Tri(m, c2, c3, apex, roof, uvs); Tri(m, c3, c0, apex, roof, uvs);
                return;
            }
            if (shape == RoofShape.Skillion)
            {
                var lo0 = P(c, u, v, -Le, -We, yEave - ov * slope * 0.5, tE, tN); var lo1 = P(c, u, v, Le, -We, yEave - ov * slope * 0.5, tE, tN);
                var hi1 = P(c, u, v, Le, W, yR, tE, tN); var hi0 = P(c, u, v, -Le, W, yR, tE, tN);
                Quad(m, lo0, lo1, hi1, hi0, roof, uvs);
                // infill walls at both ends + back wall above eave
                for (int s = -1; s <= 1; s += 2)
                {
                    var e0 = P(c, u, v, s * L, -W, yEave, tE, tN); var e1 = P(c, u, v, s * L, W, yEave, tE, tN); var e2 = P(c, u, v, s * L, W, yR, tE, tN);
                    TriDir(m, wallSub, e0, e1, e2, wall, u.X * s, u.Y * s);
                }
                var b0 = P(c, u, v, -L, W, yEave, tE, tN); var b1 = P(c, u, v, L, W, yEave, tE, tN); var b2 = P(c, u, v, L, W, yR, tE, tN); var b3 = P(c, u, v, -L, W, yR, tE, tN);
                QuadDir(m, wallSub, b0, b1, b2, b3, wall, v.X, v.Y);
                return;
            }

            if (shape == RoofShape.Hipped)
            {
                double ridgeHalf = L - W;
                var r0 = P(c, u, v, -ridgeHalf, 0, yR, tE, tN); var r1 = P(c, u, v, ridgeHalf, 0, yR, tE, tN);
                var c0 = P(c, u, v, -Le, -We, yE, tE, tN); var c1 = P(c, u, v, Le, -We, yE, tE, tN);
                var c2 = P(c, u, v, Le, We, yE, tE, tN); var c3 = P(c, u, v, -Le, We, yE, tE, tN);
                Quad(m, c0, c1, r1, r0, roof, uvs);         // side -v
                Quad(m, c2, c3, r0, r1, roof, uvs);         // side +v
                Tri(m, c1, c2, r1, roof, uvs);              // end +u
                Tri(m, c3, c0, r0, roof, uvs);              // end -u
                return;
            }

            // gabled
            {
                var r0 = P(c, u, v, -Le, 0, yR, tE, tN); var r1 = P(c, u, v, Le, 0, yR, tE, tN);
                var c0 = P(c, u, v, -Le, -We, yE, tE, tN); var c1 = P(c, u, v, Le, -We, yE, tE, tN);
                var c2 = P(c, u, v, Le, We, yE, tE, tN); var c3 = P(c, u, v, -Le, We, yE, tE, tN);
                Quad(m, c0, c1, r1, r0, roof, uvs);
                Quad(m, c2, c3, r0, r1, roof, uvs);
                for (int s = -1; s <= 1; s += 2)
                {
                    var e0 = P(c, u, v, s * L, -W, yEave, tE, tN); var e1 = P(c, u, v, s * L, W, yEave, tE, tN); var apex = P(c, u, v, s * L, 0, yR, tE, tN);
                    TriDir(m, wallSub, e0, e1, apex, wall, u.X * s, u.Y * s);
                }
            }
        }

        // roof faces: orient normal upwards
        static void Quad(MeshData m, float[] a, float[] b, float[] c, float[] d, byte[] col, float uvs)
        {
            float ny = Ny(a, b, d);
            if (ny < 0) { var t = b; b = d; d = t; }
            m.AddFlatTriangle(RoofPitched, a, b, c, col[0], col[1], col[2], uvs);
            m.AddFlatTriangle(RoofPitched, a, c, d, col[0], col[1], col[2], uvs);
        }
        static void Tri(MeshData m, float[] a, float[] b, float[] c, byte[] col, float uvs)
        {
            float ny = Ny(a, b, c);
            if (ny < 0) { var t = b; b = c; c = t; }
            m.AddFlatTriangle(RoofPitched, a, b, c, col[0], col[1], col[2], uvs);
        }
        static float Ny(float[] a, float[] b, float[] c) => Cross(a, b, c);
        static float Cross(float[] a, float[] b, float[] c)
        {
            // y component of cross(b-a, c-a)
            return (b[2] - a[2]) * (c[0] - a[0]) - (b[0] - a[0]) * (c[2] - a[2]);
        }

        // wall faces: orient normal along (dx,dz)
        static void TriDir(MeshData m, int sub, float[] a, float[] b, float[] c, byte[] col, double dx, double dz)
        {
            float nx = (b[1] - a[1]) * (c[2] - a[2]) - (b[2] - a[2]) * (c[1] - a[1]);
            float nz = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
            if (nx * dx + nz * dz < 0) { var t = b; b = c; c = t; }
            m.AddFlatTriangle(sub, a, b, c, col[0], col[1], col[2], 1f / 3.2f);
        }
        static void QuadDir(MeshData m, int sub, float[] a, float[] b, float[] c, float[] d, byte[] col, double dx, double dz)
        {
            TriDir(m, sub, a, b, c, col, dx, dz); TriDir(m, sub, a, c, d, col, dx, dz);
        }

        // ------------------------------------------------------------------ colours

        static byte[] Pick(byte[][] palette, Random rnd, string tagColour)
        {
            if (TryParseColour(tagColour, out var c)) return c;
            return Tint(palette[rnd.Next(palette.Length)], rnd, 8);
        }

        static byte[] Tint(byte[] c, Random rnd, int jitter)
        {
            int j = rnd.Next(-jitter, jitter + 1);
            return new[] { Clamp(c[0] + j), Clamp(c[1] + j), Clamp(c[2] + j) };
        }

        static byte Clamp(int v) => (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));

        public static bool TryParseColour(string s, out byte[] rgb)
        {
            rgb = null;
            if (string.IsNullOrEmpty(s)) return false;
            if (s[0] == '#' && s.Length == 7)
            {
                try { rgb = new[] { Convert.ToByte(s.Substring(1, 2), 16), Convert.ToByte(s.Substring(3, 2), 16), Convert.ToByte(s.Substring(5, 2), 16) }; return true; }
                catch { return false; }
            }
            switch (s.ToLowerInvariant())
            {
                case "white": rgb = new byte[] { 240, 240, 236 }; return true;
                case "grey": case "gray": rgb = new byte[] { 160, 160, 160 }; return true;
                case "red": rgb = new byte[] { 160, 60, 50 }; return true;
                case "brown": rgb = new byte[] { 130, 95, 70 }; return true;
                case "beige": rgb = new byte[] { 220, 205, 175 }; return true;
                case "yellow": rgb = new byte[] { 225, 205, 130 }; return true;
                case "black": rgb = new byte[] { 50, 50, 52 }; return true;
                case "green": rgb = new byte[] { 120, 150, 110 }; return true;
                case "blue": rgb = new byte[] { 110, 130, 170 }; return true;
                case "orange": rgb = new byte[] { 210, 140, 80 }; return true;
                case "cream": rgb = new byte[] { 238, 228, 200 }; return true;
            }
            return false;
        }
    }
}
