using System;
using SwissPoliceSim.Core.Geometry;

namespace SwissPoliceSim.Core.Meshing
{
    /// <summary>Chimneys and dormers on gabled roofs (near level of detail).</summary>
    public static class RoofDetail
    {
        public static void Add(MeshData m, DetailPlan plan, Vec2d c, Vec2d u, double L, double W, double tE, double tN, double yEave, double rise, byte[] roof, byte[] wall, Random rnd)
        {
            var v = new Vec2d(-u.Y, u.X); double yR = yEave + rise, slope = rise / Math.Max(0.1, W);
            float[] P(double a, double b, double y) { var p = c + u * a + v * b; return new[] { (float)(p.X - tE), (float)y, (float)(p.Y - tN) }; }
            var brick = new byte[] { 150, 92, 74 }; var cap = new byte[] { 110, 108, 104 };

            if (plan.Chimney && L > 3.0)
            {
                double a = (rnd.NextDouble() < 0.5 ? -1 : 1) * L * (0.35 + 0.3 * rnd.NextDouble()); double b = (rnd.NextDouble() - 0.5) * W * 0.3;
                double s = 0.30;
                FacadeDetailer.AddBox(m, BuildingMesher.TrimSub, (x, z, y) => P(x, z, y), a - s, a + s, b - s, b + s, yR - 0.9 - Math.Abs(b) * slope, yR + 0.95, u.X, u.Y, v.X, v.Y, brick, cap);
                FacadeDetailer.AddBox(m, BuildingMesher.TrimSub, (x, z, y) => P(x, z, y), a - s - 0.07, a + s + 0.07, b - s - 0.07, b + s + 0.07, yR + 0.95, yR + 1.07, u.X, u.Y, v.X, v.Y, cap, cap);
            }

            if (plan.Dormers && W > 3.4 && L * 2 > 8)
            {
                int count = Math.Min(3, (int)(L * 2 / 4.8)); if (count < 1) return;
                double step = L * 2 / (count + 1);
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 1; k <= count; k++)
                    {
                        if (rnd.NextDouble() < 0.25) continue;
                        double a = -L + step * k, vD = side * W * 0.50;
                        AddDormer(m, plan, P, a, vD, side, yR, slope, u, v, roof, wall);
                    }
            }
        }

        static void AddDormer(MeshData m, DetailPlan plan, Func<double, double, double, float[]> P, double a, double vD, int side, double yR, double slope, Vec2d u, Vec2d v, byte[] roof, byte[] wall)
        {
            double dw = 1.35, hw = dw / 2, wallH = 1.35;
            double yb = yR - slope * Math.Abs(vD), yt = yb + wallH, ya = yt + hw * 0.85;
            double vm = side * (yR - ya) / slope, vt = side * (yR - yt) / slope;      // where ridge / eave level meet the main roof
            if (Math.Abs(vm) < 0.25) return;
            double vF = vD;
            double vo = vF + side * 0.0;
            // front wall + window
            var Fbl = P(a - hw, vF, yb); var Fbr = P(a + hw, vF, yb); var Ftr = P(a + hw, vF, yt); var Ftl = P(a - hw, vF, yt);
            FacadeDetailer.EmitPublic(m, BuildingMesher.PlasterWall, Fbl, Fbr, Ftr, Ftl, (float)(v.X * side), 0, (float)(v.Y * side), wall, 1, 1);
            var tint = plan.FrameTint;
            double iw = 0.14;
            FacadeDetailer.EmitPublic(m, BuildingMesher.WindowSub, P(a - hw + iw, vF + side * 0.02, yb + 0.12), P(a + hw - iw, vF + side * 0.02, yb + 0.12), P(a + hw - iw, vF + side * 0.02, yt - 0.14), P(a - hw + iw, vF + side * 0.02, yt - 0.14), (float)(v.X * side), 0, (float)(v.Y * side), tint, 0.2f, 1);
            // front gable triangle
            FacadeDetailer.EmitPublic(m, BuildingMesher.PlasterWall, Ftl, Ftr, P(a, vF, ya), P(a, vF, ya), (float)(v.X * side), 0, (float)(v.Y * side), wall, 1, 1);
            // cheeks
            foreach (int s in new[] { -1, 1 })
            {
                double x = a + s * hw;
                var q0 = P(x, vF, yb); var q1 = P(x, vF, yt); var q2 = P(x, vt, yt);
                FacadeDetailer.EmitPublic(m, BuildingMesher.PlasterWall, q0, q1, q2, q2, (float)(u.X * s), 0, (float)(u.Y * s), wall, 1, 1);
            }
            // roof planes (front overhang 0.18)
            double vFo = vF + side * 0.18;
            foreach (int s in new[] { -1, 1 })
            {
                double x = a + s * hw;
                var e0 = P(x + s * 0.12, vFo, yt - 0.06); var e1 = P(x + s * 0.12, vt, yt - 0.06 + 0.0); var r1 = P(a, vm, ya); var r0 = P(a, vFo, ya + 0.0);
                FacadeDetailer.EmitPublic(m, BuildingMesher.RoofPitched, e0, e1, r1, r0, 0, 1, 0, roof, 1, 1);
            }
        }
    }
}
