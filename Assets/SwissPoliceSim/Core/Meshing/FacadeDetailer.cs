using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geometry;

namespace SwissPoliceSim.Core.Meshing
{
    /// <summary>Per-building decisions for the detailed (near) level: the same building always gets the same shutters, door and balconies.</summary>
    public sealed class DetailPlan
    {
        public bool Shutters, Balconies, Cornice, FloorBands, StoneSurrounds, Shop, Chimney, Dormers;
        public byte[] ShutterColour, TrimColour, DoorColour, FrameTint = { 255, 253, 246 };
        public int DoorEdge = -1; public int Floors; public float FloorH;

        public static DetailPlan Make(BuildingFeature b, bool oldTown, float yG, float yEave, Random rnd)
        {
            var p = new DetailPlan();
            bool res = b.Kind == BuildingKind.House || b.Kind == BuildingKind.Generic || b.Kind == BuildingKind.Apartments;
            bool big = b.Kind == BuildingKind.Apartments || b.Kind == BuildingKind.Commercial || b.Kind == BuildingKind.Public;
            double h = yEave - yG;
            int nf = b.Levels > 0 ? b.Levels : (int)Math.Max(1, Math.Round(h / 3.1));
            if (b.Kind == BuildingKind.Church || b.Kind == BuildingKind.Industrial || b.Kind == BuildingKind.Garage || b.Kind == BuildingKind.Shed) nf = 1;
            p.Floors = Math.Max(1, nf); p.FloorH = (float)(h / p.Floors);
            p.Shutters = res && b.Kind != BuildingKind.Apartments ? rnd.NextDouble() < 0.6 : (b.Kind == BuildingKind.Apartments && rnd.NextDouble() < 0.25);
            if (oldTown) p.Shutters = rnd.NextDouble() < 0.7;
            byte[][] sh = { new byte[] { 74, 104, 74 }, new byte[] { 150, 52, 44 }, new byte[] { 96, 76, 58 }, new byte[] { 120, 132, 140 }, new byte[] { 62, 86, 110 }, new byte[] { 190, 190, 180 } };
            p.ShutterColour = sh[rnd.Next(sh.Length)];
            p.TrimColour = oldTown ? new byte[] { 150, 152, 138 } : new byte[] { 214, 208, 196 };
            byte[][] doors = { new byte[] { 80, 56, 40 }, new byte[] { 54, 62, 70 }, new byte[] { 110, 40, 36 }, new byte[] { 60, 80, 64 } };
            p.DoorColour = doors[rnd.Next(doors.Length)];
            p.Balconies = p.Floors >= 2 && (b.Kind == BuildingKind.Apartments ? rnd.NextDouble() < 0.7 : (b.Kind == BuildingKind.House || b.Kind == BuildingKind.Generic) && rnd.NextDouble() < 0.12);
            p.Cornice = big || oldTown || rnd.NextDouble() < 0.25;
            p.FloorBands = (big || oldTown) && p.Floors >= 2;
            p.StoneSurrounds = oldTown || b.Kind == BuildingKind.Public || (b.Kind == BuildingKind.Apartments && rnd.NextDouble() < 0.4) || rnd.NextDouble() < 0.12;
            p.Shop = b.Kind == BuildingKind.Commercial || (oldTown && p.Floors >= 3 && rnd.NextDouble() < 0.5);
            p.Chimney = res && rnd.NextDouble() < 0.75; p.Dormers = res && p.Floors >= 2 && rnd.NextDouble() < 0.4;
            return p;
        }
    }

    /// <summary>Real facade geometry: wall strips around recessed windows with reveals, sills, shutters, stone surrounds, doors, balconies, cornices, plinth.</summary>
    public static class FacadeDetailer
    {
        public const int Plaster = BuildingMesher.PlasterWall, Window = BuildingMesher.WindowSub, Trim = BuildingMesher.TrimSub, Shutter = BuildingMesher.ShutterSub, Railing = BuildingMesher.RailingSub;

        struct Frame { public double AE, AN, TX, TY, NX, NY, TE, TN; public double Perim, Len; }

        sealed class Opening { public double X0, X1, Sill, Head; public int Variant; public bool Door, Balcony; public bool Shop; }

        // ------------------------------------------------------------------ emit helpers
        static float[] Pt(in Frame f, double s, double d, double y) => new[] { (float)(f.AE + f.TX * s + f.NX * d - f.TE), (float)y, (float)(f.AN + f.TY * s + f.NY * d - f.TN) };

        static void Emit(MeshData m, int sub, float[] A, float[] B, float[] C, float[] D, float uA, float vA, float uB, float vB, float uC, float vC, float uD, float vD, float hx, float hy, float hz, byte r, byte g, byte b)
        {
            float ex = B[0] - A[0], ey = B[1] - A[1], ez = B[2] - A[2], fx = C[0] - A[0], fy = C[1] - A[1], fz = C[2] - A[2];
            float nx = ey * fz - ez * fy, ny = ez * fx - ex * fz, nz = ex * fy - ey * fx;
            if (nx * hx + ny * hy + nz * hz < 0) { var t = B; B = D; D = t; float tu = uB, tv = vB; uB = uD; vB = vD; uD = tu; vD = tv; nx = -nx; ny = -ny; nz = -nz; }
            float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz); if (l < 1e-9f) return; nx /= l; ny /= l; nz /= l;
            int a = m.AddVertex(A[0], A[1], A[2], nx, ny, nz, uA, vA, r, g, b), bb = m.AddVertex(B[0], B[1], B[2], nx, ny, nz, uB, vB, r, g, b);
            int c = m.AddVertex(C[0], C[1], C[2], nx, ny, nz, uC, vC, r, g, b), d = m.AddVertex(D[0], D[1], D[2], nx, ny, nz, uD, vD, r, g, b);
            m.AddTriangle(sub, a, bb, c); m.AddTriangle(sub, a, c, d);
        }

        /// <summary>Quad in the facade plane (spanned by tangent and height) at depth d from the wall surface (positive = outward).</summary>
        static void Front(MeshData m, in Frame f, int sub, double s0, double s1, double y0, double y1, double d, byte[] c, float u0, float v0, float u1, float v1, double scale = 1)
        {
            Emit(m, sub, Pt(f, s0, d, y0), Pt(f, s1, d, y0), Pt(f, s1, d, y1), Pt(f, s0, d, y1), u0, v0, u1, v0, u1, v1, u0, v1, (float)f.NX, 0, (float)f.NY, c[0], c[1], c[2]);
        }
        static void Wall(MeshData m, in Frame f, double s0, double s1, double y0, double y1, byte[] c)
        {
            if (s1 - s0 < 0.01 || y1 - y0 < 0.01) return;
            const float k = 1f / 3.2f;
            Front(m, f, Plaster, s0, s1, y0, y1, 0, c, (float)((f.Perim + s0) * k), (float)(y0 * k), (float)((f.Perim + s1) * k), (float)(y1 * k));
        }
        /// <summary>Vertical quad perpendicular to the facade (reveal side, balcony side) at position s from depth d0 to d1.</summary>
        static void Side(MeshData m, in Frame f, int sub, double s, double d0, double d1, double y0, double y1, float hs, byte[] c, float u0 = 0, float v0 = 0, float u1 = 1, float v1 = 1)
        {
            Emit(m, sub, Pt(f, s, d0, y0), Pt(f, s, d1, y0), Pt(f, s, d1, y1), Pt(f, s, d0, y1), u0, v0, u1, v0, u1, v1, u0, v1, (float)(f.TX * hs), 0, (float)(f.TY * hs), c[0], c[1], c[2]);
        }
        /// <summary>Horizontal quad (top: hy=+1, underside: hy=-1).</summary>
        static void Flat(MeshData m, in Frame f, int sub, double s0, double s1, double y, double d0, double d1, float hy, byte[] c)
        {
            Emit(m, sub, Pt(f, s0, d0, y), Pt(f, s1, d0, y), Pt(f, s1, d1, y), Pt(f, s0, d1, y), 0, 0, 1, 0, 1, 1, 0, 1, 0, hy, 0, c[0], c[1], c[2]);
        }

        static byte[] Mul(byte[] c, double k) => new[] { (byte)Math.Min(255, c[0] * k), (byte)Math.Min(255, c[1] * k), (byte)Math.Min(255, c[2] * k) };

        // ------------------------------------------------------------------ walls
        public static void AddWalls(MeshData m, PolygonWithHoles footprint, List<Vec2d> ring, bool hole, double tE, double tN, float yBase, float yG, float yEave, BuildingFeature b, DetailPlan plan, bool oldTown, byte[] wall, Random rnd, bool[] partyWall)
        {
            int n = ring.Count;
            // orientation-independent: the side of the longest edge that lies outside the footprint is "outward"
            int li = 0; double lb = 0; for (int i = 0; i < n; i++) { double L0 = (ring[(i + 1) % n] - ring[i]).Length; if (L0 > lb) { lb = L0; li = i; } }
            var la = ring[li]; var ld = ring[(li + 1) % n] - la; var lt = new Vec2d(ld.X / Math.Max(1e-9, lb), ld.Y / Math.Max(1e-9, lb));
            var probe = new Vec2d(la.X + ld.X * 0.5 + lt.Y * 0.15, la.Y + ld.Y * 0.5 - lt.X * 0.15);       // right of the edge
            bool rightOut = !footprint.Contains(probe.X, probe.Y);
            // pick door edge: the longest street-ish edge of the outer ring
            int doorEdge = -1; double best = 0;
            if (!hole) for (int i = 0; i < n; i++) { double L = (ring[(i + 1) % n] - ring[i]).Length; if (L > best && L >= 3.2 && (partyWall == null || !partyWall[i])) { best = L; doorEdge = i; } }
            double perim = 0;
            for (int i = 0; i < n; i++)
            {
                var a = ring[i]; var c = ring[(i + 1) % n]; var d = c - a; double len = d.Length; if (len < 0.05) continue;
                var t = new Vec2d(d.X / len, d.Y / len); var nrm = rightOut ? new Vec2d(t.Y, -t.X) : new Vec2d(-t.Y, t.X);
                var f = new Frame { AE = a.X, AN = a.Y, TX = t.X, TY = t.Y, NX = nrm.X, NY = nrm.Y, TE = tE, TN = tN, Perim = perim, Len = len };
                BuildEdge(m, f, yBase, yG, yEave, b, plan, oldTown, wall, rnd, i == doorEdge && !hole, partyWall != null && partyWall[i]);
                perim += len;
            }
        }

        static void BuildEdge(MeshData m, in Frame f, float yBase, float yG, float yEave, BuildingFeature b, DetailPlan plan, bool oldTown, byte[] wall, Random rnd, bool door, bool party)
        {
            double len = f.Len; int nF = plan.Floors; double fh = plan.FloorH;
            var stone = plan.TrimColour; var plinthCol = new byte[] { 118, 114, 108 };
            // plinth (Sockel) and the wall below ground floor level
            Wall(m, f, 0, len, yBase, yG, wall);
            Front(m, f, Trim, 0, len, yBase, yG + 0.42, 0.035, plinthCol, 0, 0, (float)(len / 1.5), 0.5f);
            Flat(m, f, Trim, 0, len, yG + 0.42, 0, 0.035, 1, plinthCol);

            bool tooShort = len < 1.9 || party;
            bool industrial = b.Kind == BuildingKind.Industrial, garage = b.Kind == BuildingKind.Garage || b.Kind == BuildingKind.Shed, church = b.Kind == BuildingKind.Church || b.Kind == BuildingKind.Tower;
            bool commercial = b.Kind == BuildingKind.Commercial || b.Kind == BuildingKind.Public || b.Kind == BuildingKind.Station;
            double bayTarget = industrial ? 5.0 : commercial ? 3.6 : church ? 5.5 : 3.0;
            int nb = Math.Max(1, (int)Math.Round(len / bayTarget)); double bayW = len / nb;
            // door bay (nearest to the middle)
            int doorBay = door ? nb / 2 : -1;
            for (int fl = 0; fl < nF; fl++)
            {
                double y0 = yG + fl * fh, y1 = y0 + fh; var ops = new List<Opening>();
                if (!tooShort && !garage)
                    for (int k = 0; k < nb; k++)
                    {
                        double cx = (k + 0.5) * bayW; bool isDoor = fl == 0 && k == doorBay;
                        var o = new Opening { Variant = PickVariant(rnd) };
                        if (isDoor)
                        {
                            double dw = commercial ? 1.7 : 1.05; if (dw > bayW - 0.5) dw = bayW - 0.5; if (dw < 0.7) continue;
                            o.X0 = cx - dw / 2; o.X1 = cx + dw / 2; o.Sill = y0 + 0.13; o.Head = y0 + 2.15; o.Door = true;
                        }
                        else if (industrial) { double ww = Math.Min(3.2, bayW - 1.2); if (ww < 1) continue; o.X0 = cx - ww / 2; o.X1 = cx + ww / 2; o.Sill = y0 + fh * 0.62; o.Head = y0 + fh * 0.88; o.Variant = 3; }
                        else if (church) { double ww = Math.Min(1.1, bayW - 1.5); if (ww < 0.6) continue; o.X0 = cx - ww / 2; o.X1 = cx + ww / 2; o.Sill = y0 + Math.Min(3.0, fh * 0.30); o.Head = Math.Min(y0 + fh * 0.88, o.Sill + 4.2); o.Variant = 3; }
                        else if (fl == 0 && plan.Shop && (commercial || oldTown))
                        {
                            double ww = bayW - 0.55; if (ww < 1.2) continue; o.X0 = cx - ww / 2; o.X1 = cx + ww / 2; o.Sill = y0 + 0.55; o.Head = y0 + Math.Min(2.7, fh * 0.82); o.Variant = 4; o.Shop = true;
                        }
                        else
                        {
                            double ww = Math.Min(commercial ? 1.55 : 1.18, bayW - 0.95); if (ww < 0.65) continue;
                            o.X0 = cx - ww / 2; o.X1 = cx + ww / 2;
                            o.Sill = y0 + fh * (commercial ? 0.26 : 0.30); o.Head = y0 + fh * (commercial ? 0.84 : 0.80);
                            if (plan.Balconies && fl >= 1 && len >= 5.5 && k % 2 == 0 && bayW >= 2.6) { o.Balcony = true; o.Sill = y0 + 0.08; o.Head = y0 + fh * 0.84; o.Variant = 0; }
                        }
                        if (o.X0 < 0.35 || o.X1 > len - 0.35) continue;
                        ops.Add(o);
                    }
                // wall strips between and around the openings
                double prev = 0;
                foreach (var o in ops)
                {
                    Wall(m, f, prev, o.X0, y0, y1, wall);
                    if (o.Sill > y0 + 0.01) Wall(m, f, o.X0, o.X1, y0, o.Sill, wall);
                    Wall(m, f, o.X0, o.X1, o.Head, y1, wall);
                    prev = o.X1;
                }
                Wall(m, f, prev, len, y0, y1, wall);
                foreach (var o in ops) AddOpening(m, f, o, y0, plan, wall, stone, rnd, oldTown, len);
                if (plan.FloorBands && fl >= 1) Front(m, f, Trim, 0, len, y0 - 0.09, y0 + 0.09, 0.03, stone, 0, 0, (float)(len / 1.5), 0.2f);
            }
            if (garage && !tooShort && len > 3.5) AddGarageDoor(m, f, yG, wall, len);
            if (plan.Cornice && !party) AddCornice(m, f, yEave, stone, len);
            if (door == false && nF >= 1) { /* nothing */ }
        }

        static int PickVariant(Random r) { double x = r.NextDouble(); return x < 0.50 ? 0 : x < 0.72 ? 1 : x < 0.86 ? 2 : 3; }

        static void AddOpening(MeshData m, in Frame f, Opening o, double yFloor, DetailPlan plan, byte[] wall, byte[] stone, Random rnd, bool oldTown, double len)
        {
            double x0 = o.X0, x1 = o.X1, sill = o.Sill, head = o.Head; double rd = o.Shop ? 0.12 : 0.20;
            var reveal = Mul(wall, 0.90);
            // reveals
            Side(m, f, Plaster, x0, 0, -rd, sill, head, 1, reveal); Side(m, f, Plaster, x1, 0, -rd, sill, head, -1, reveal);
            Flat(m, f, Plaster, x0, x1, head, 0, -rd, -1, Mul(wall, 0.82));
            if (!o.Door) Flat(m, f, Plaster, x0, x1, sill, 0, -rd, 1, Mul(wall, 0.95));
            var tint = plan.FrameTint;
            if (o.Door)
            {
                var dc = plan.DoorColour;
                Front(m, f, Trim, x0, x1, o.Sill, head, -rd, dc, 0, 0, 1, 1);
                Front(m, f, Window, x0 + (x1 - x0) * 0.2, x1 - (x1 - x0) * 0.2, o.Sill + (head - o.Sill) * 0.52, head - 0.12, -rd + 0.01, tint, 0.002f, 0.1f, 0.198f, 0.9f);   // glazed upper door panel
                Front(m, f, Trim, x0 - 0.06, x1 + 0.06, head, head + 0.1, 0.03, stone, 0, 0, 1, 1);
                Front(m, f, Trim, x0 - 0.06, x0, o.Sill, head, 0.03, stone, 0, 0, 1, 1); Front(m, f, Trim, x1, x1 + 0.06, o.Sill, head, 0.03, stone, 0, 0, 1, 1);
                // stoop
                Flat(m, f, Trim, x0 - 0.25, x1 + 0.25, o.Sill, 0, 0.45, 1, new byte[] { 130, 126, 120 });
                Front(m, f, Trim, x0 - 0.25, x1 + 0.25, o.Sill - 0.9, o.Sill, 0.45, new byte[] { 118, 114, 108 }, 0, 0, 1, 1);
                return;
            }
            // glass + frame
            Front(m, f, Window, x0, x1, sill, head, -rd, o.Shop ? new byte[] { 255, 255, 255 } : tint, o.Variant * 0.2f + 0.002f, 0, o.Variant * 0.2f + 0.198f, 1);
            if (!o.Shop || true)
            {
                // sill (stone, projecting)
                Flat(m, f, Trim, x0 - 0.07, x1 + 0.07, sill, 0, 0.07, 1, stone);
                Front(m, f, Trim, x0 - 0.07, x1 + 0.07, sill - 0.06, sill, 0.07, stone, 0, 0, 1, 1);
            }
            if (plan.StoneSurrounds || o.Shop)
            {
                double w = o.Shop ? 0.07 : 0.11;
                Front(m, f, Trim, x0 - w, x0, sill, head + w + (o.Shop ? 0.0 : 0.05), 0.025, stone, 0, 0, 1, 1); Front(m, f, Trim, x1, x1 + w, sill, head + w + (o.Shop ? 0.0 : 0.05), 0.025, stone, 0, 0, 1, 1);
                Front(m, f, Trim, x0 - w, x1 + w, head, head + w + (o.Shop ? 0.0 : 0.05), 0.025, stone, 0, 0, 1, 1);
            }
            if (o.Shop)
            {   // awning on about half of the shop windows
                if (((int)(o.X0 * 7) & 1) == 0)
                {
                    byte[][] aw = { new byte[] { 150, 40, 40 }, new byte[] { 40, 80, 120 }, new byte[] { 70, 110, 70 }, new byte[] { 200, 170, 80 } };
                    var ac = aw[((int)(o.X0 * 13 + f.Perim) & 0xFFFF) % aw.Length];
                    var A = Pt(f, x0 - 0.1, 0.0, head + 0.35); var B = Pt(f, x1 + 0.1, 0.0, head + 0.35); var C = Pt(f, x1 + 0.1, 1.1, head + 0.05); var D = Pt(f, x0 - 0.1, 1.1, head + 0.05);
                    Emit(m, Trim, A, B, C, D, 0, 0, 1, 0, 1, 1, 0, 1, 0, 1, 0, ac[0], ac[1], ac[2]);
                    Emit(m, Trim, A, B, C, D, 0, 0, 1, 0, 1, 1, 0, 1, 0, -1, 0, (byte)(ac[0] * 0.7), (byte)(ac[1] * 0.7), (byte)(ac[2] * 0.7));
                    var C2 = Pt(f, x1 + 0.1, 1.1, head - 0.12); var D2 = Pt(f, x0 - 0.1, 1.1, head - 0.12);
                    Emit(m, Trim, D, C, C2, D2, 0, 0, 1, 0, 1, 1, 0, 1, (float)f.NX, 0, (float)f.NY, ac[0], ac[1], ac[2]);
                }
                return;
            }
            // shutters (open, flanking)
            if (plan.Shutters && !o.Balcony)
            {
                double sw = (x1 - x0) * 0.5; var sc = plan.ShutterColour;
                if (x0 - sw >= 0.08 && x1 + sw <= len - 0.08)
                {
                    Front(m, f, Shutter, x0 - sw - 0.01, x0 - 0.01, sill - 0.05, head + 0.05, 0.035, sc, 0, 0, 1, 1);
                    Front(m, f, Shutter, x1 + 0.01, x1 + sw + 0.01, sill - 0.05, head + 0.05, 0.035, sc, 0, 0, 1, 1);
                }
            }
            if (o.Balcony) AddBalcony(m, f, x0, x1, yFloor, plan, stone, rnd);
        }

        static void AddBalcony(MeshData m, in Frame f, double x0, double x1, double yFloor, DetailPlan plan, byte[] stone, Random rnd)
        {
            double mid = (x0 + x1) / 2, hw = Math.Max((x1 - x0) / 2 + 0.55, 1.1), depth = 1.25, th = 0.16;
            double s0 = mid - hw, s1 = mid + hw; var slab = new byte[] { 196, 192, 184 }; var rail = new byte[] { 255, 255, 255 }; var railTint = new byte[] { 70, 72, 76 };
            Flat(m, f, Trim, s0, s1, yFloor, 0, depth, 1, new byte[] { 160, 156, 150 });
            Flat(m, f, Trim, s0, s1, yFloor - th, 0, depth, -1, Mul(slab, 0.8f));
            Front(m, f, Trim, s0, s1, yFloor - th, yFloor, depth, slab, 0, 0, 1, 1);
            Side(m, f, Trim, s0, 0, depth, yFloor - th, yFloor, -1, slab); Side(m, f, Trim, s1, 0, depth, yFloor - th, yFloor, 1, slab);
            // railing (alpha-cut, visible from both sides)
            double rh = 1.0; float rep = (float)((s1 - s0) / 1.0);
            Front(m, f, Railing, s0, s1, yFloor, yFloor + rh, depth - 0.03, railTint, 0, 0, rep, 1);
            Emit(m, Railing, Pt(f, s0, depth - 0.03, yFloor), Pt(f, s1, depth - 0.03, yFloor), Pt(f, s1, depth - 0.03, yFloor + rh), Pt(f, s0, depth - 0.03, yFloor + rh), 0, 0, rep, 0, rep, 1, 0, 1, (float)-f.NX, 0, (float)-f.NY, railTint[0], railTint[1], railTint[2]);
            foreach (double s in new[] { s0 + 0.02, s1 - 0.02 })
            {
                float hs = s < mid ? 1f : -1f;
                Side(m, f, Railing, s, 0, depth - 0.03, yFloor, yFloor + rh, hs, railTint, 0, 0, 1.2f, 1);
                Side(m, f, Railing, s, 0, depth - 0.03, yFloor, yFloor + rh, -hs, railTint, 0, 0, 1.2f, 1);
            }
        }

        static void AddCornice(MeshData m, in Frame f, double yEave, byte[] stone, double len)
        {
            const double proj = 0.16;
            Front(m, f, Trim, 0, len, yEave - 0.32, yEave, proj, stone, 0, 0, (float)(len / 1.5), 0.3f);
            Flat(m, f, Trim, 0, len, yEave - 0.32, 0.0, proj, -1, Mul(stone, 0.75f));
            Flat(m, f, Trim, 0, len, yEave, 0.0, proj, 1, Mul(stone, 0.9f));
        }

        static void AddGarageDoor(MeshData m, in Frame f, double yG, byte[] wall, double len)
        {
            double w = Math.Min(2.8, len - 1.0); double x0 = len / 2 - w / 2, x1 = len / 2 + w / 2;
            Front(m, f, Shutter, x0, x1, yG + 0.05, yG + 2.1, 0.03, new byte[] { 205, 205, 200 }, 0, 0, 1, 1);
            Front(m, f, Trim, x0 - 0.08, x0, yG + 0.05, yG + 2.2, 0.04, new byte[] { 120, 118, 112 }, 0, 0, 1, 1); Front(m, f, Trim, x1, x1 + 0.08, yG + 0.05, yG + 2.2, 0.04, new byte[] { 120, 118, 112 }, 0, 0, 1, 1);
            Front(m, f, Trim, x0 - 0.08, x1 + 0.08, yG + 2.1, yG + 2.2, 0.04, new byte[] { 120, 118, 112 }, 0, 0, 1, 1);
        }

        // ------------------------------------------------------------------ roof furniture
        /// <summary>Axis-aligned (in the rect frame) box without a bottom face, colour per face group.</summary>
        public static void AddBox(MeshData m, int sub, Func<double, double, double, float[]> P, double a0, double a1, double b0, double b1, double y0, double y1, double ux, double uz, double vx, double vz, byte[] col, byte[] topCol)
        {
            var c00 = P(a0, b0, y0); var c10 = P(a1, b0, y0); var c11 = P(a1, b1, y0); var c01 = P(a0, b1, y0);
            var t00 = P(a0, b0, y1); var t10 = P(a1, b0, y1); var t11 = P(a1, b1, y1); var t01 = P(a0, b1, y1);
            Emit(m, sub, c00, c10, t10, t00, 0, 0, 1, 0, 1, 1, 0, 1, (float)-vx, 0, (float)-vz, col[0], col[1], col[2]);
            Emit(m, sub, c10, c11, t11, t10, 0, 0, 1, 0, 1, 1, 0, 1, (float)ux, 0, (float)uz, col[0], col[1], col[2]);
            Emit(m, sub, c11, c01, t01, t11, 0, 0, 1, 0, 1, 1, 0, 1, (float)vx, 0, (float)vz, col[0], col[1], col[2]);
            Emit(m, sub, c01, c00, t00, t01, 0, 0, 1, 0, 1, 1, 0, 1, (float)-ux, 0, (float)-uz, col[0], col[1], col[2]);
            Emit(m, sub, t00, t10, t11, t01, 0, 0, 1, 0, 1, 1, 0, 1, 0, 1, 0, topCol[0], topCol[1], topCol[2]);
        }

        public static void EmitPublic(MeshData m, int sub, float[] A, float[] B, float[] C, float[] D, float hx, float hy, float hz, byte[] c, float u = 1, float v = 1)
            => Emit(m, sub, A, B, C, D, 0, 0, u, 0, u, v, 0, v, hx, hy, hz, c[0], c[1], c[2]);
    }
}
