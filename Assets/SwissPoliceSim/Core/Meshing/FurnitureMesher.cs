using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Traffic;

namespace SwissPoliceSim.Core.Meshing
{
    /// <summary>
    /// Street furniture for one tile: traffic signals (pole + head with three lenses), stop / give-way signs, street lamps,
    /// bus stop shelters, plus stop lines. Submeshes: 0 painted metal / concrete / signs (vertex colours),
    /// 1 signal lenses (emissive; uv2 = lens type + phase data for the shader), 2 sign faces.
    /// </summary>
    public static class FurnitureMesher
    {
        public const int Submeshes = 3;
        public sealed class Placement
        {
            public double E, N, Dx, Dz; public long NodeId; public int Group; public float Offset; public PointKind Kind;
        }

        public static MeshData NewMesh() => new MeshData(Submeshes);

        /// <summary>Lens vertices carry (lensType, group+offset) in a parallel list so the shader can animate them; returned via <paramref name="lensInfo"/>.</summary>
        public static MeshData Build(IList<PointFeature> points, IList<RoadWay> roads, double tileE, double tileN, Func<double, double, float> ground)
        {
            var m = NewMesh();
            var byNode = new Dictionary<long, List<(RoadWay way, int idx)>>();
            foreach (var w in roads)
                for (int i = 0; i < w.PointCount; i++)
                {
                    if (!byNode.TryGetValue(w.NodeIds[i], out var l)) byNode[w.NodeIds[i]] = l = new List<(RoadWay, int)>();
                    l.Add((w, i));
                }

            foreach (var p in points)
            {
                switch (p.Kind)
                {
                    case PointKind.TrafficSignals:
                        if (byNode.TryGetValue(p.OsmId, out var ways)) Signals(m, p, ways, tileE, tileN, ground);
                        break;
                    case PointKind.StopSign: case PointKind.GiveWay:
                        if (byNode.TryGetValue(p.OsmId, out var ways2)) Signs(m, p, ways2, tileE, tileN, ground);
                        break;
                    case PointKind.StreetLamp:
                        Lamp(m, (float)(p.E - tileE), ground(p.E - tileE, p.N - tileN), (float)(p.N - tileN), 0f);
                        break;
                    case PointKind.BusStop:
                        BusStop(m, (float)(p.E - tileE), ground(p.E - tileE, p.N - tileN), (float)(p.N - tileN), p.Direction);
                        break;
                }
            }
            return m;
        }

        static void Signals(MeshData m, PointFeature p, List<(RoadWay way, int idx)> ways, double te, double tn, Func<double, double, float> ground)
        {
            float offset = SignalModel.OffsetFor(p.OsmId);
            var done = new HashSet<long>();
            foreach (var (w, i) in ways)
            {
                if (!w.IsVehicular || w.Class > RoadClass.Tertiary && w.Class != RoadClass.Residential && w.Class != RoadClass.Unclassified) continue;
                for (int side = -1; side <= 1; side += 2)
                {
                    int j = i + side;                      // neighbour node the approach comes from
                    if (j < 0 || j >= w.PointCount) continue;
                    // direction of travel when approaching the signal = from neighbour towards the node
                    double dx = w.E[i] - w.E[j], dz = w.N[i] - w.N[j], l = Math.Sqrt(dx * dx + dz * dz); if (l < 1) continue;
                    dx /= l; dz /= l;
                    if (w.OneWay == OneWay.Forward && side > 0) continue;       // oneway: only approaches in travel direction
                    if (w.OneWay == OneWay.Reverse && side < 0) continue;
                    long key = (long)(Math.Round(dx * 4) * 10 + Math.Round(dz * 4)); if (!done.Add(key)) continue;
                    int group = SignalModel.GroupOf(Math.Atan2(dx, dz));
                    float half = w.CarriagewayWidth() * 0.5f;
                    // head position: right-hand side of the approach, just before the stop line
                    double rx = dz, rz = -dx;
                    double px = w.E[i] - dx * 5.0 + rx * (half + 0.9) - te, pz = w.N[i] - dz * 5.0 + rz * (half + 0.9) - tn;
                    float gy = ground(px, pz);
                    SignalHead(m, (float)px, gy, (float)pz, (float)Math.Atan2(-dx, -dz), group, offset);
                }
            }
        }

        static void SignalHead(MeshData m, float x, float gy, float z, float yaw, int group, float offset)
        {
            byte[] metal = { 70, 74, 78 }, dark = { 28, 28, 30 };
            MeshUtil.Cylinder(m, 0, x, gy, z, 0.065f, 0.05f, 3.4f, 8, metal[0], metal[1], metal[2], capTop: true);
            // head: housing (0.30 × 0.95 × 0.25) with 3 lenses facing the approaching traffic (yaw rotates the facing direction)
            float hx = x, hy = gy + 3.05f, hz = z;
            MeshUtil.Box(m, 0, hx, hy, hz, 0.32f, 1.0f, 0.26f, dark[0], dark[1], dark[2], yaw);
            float fx = (float)Math.Sin(yaw), fz = (float)Math.Cos(yaw);       // facing direction
            for (int k = 0; k < 3; k++)
            {
                float ly = hy + 0.32f - k * 0.32f;
                // visor + lens disc
                MeshUtil.Box(m, 0, hx + fx * 0.14f, ly + 0.10f, hz + fz * 0.14f, 0.26f, 0.025f, 0.12f, 20, 20, 22, yaw);
                AddLens(m, hx + fx * 0.131f, ly, hz + fz * 0.131f, yaw, 0.115f, k, group, offset);
            }
            // pedestrian push-button box
            MeshUtil.Box(m, 0, x, gy + 1.1f, z, 0.12f, 0.2f, 0.1f, 220, 190, 40, yaw);
        }

        static void AddLens(MeshData m, float x, float y, float z, float yaw, float r, int type, int group, float offset)
        {
            int seg = 14; float fx = (float)Math.Sin(yaw), fz = (float)Math.Cos(yaw), rx = fz, rz = -fx;
            byte[] col = type == 0 ? new byte[] { 230, 30, 30 } : type == 1 ? new byte[] { 255, 170, 20 } : new byte[] { 40, 220, 90 };
            int c = m.AddVertex(x, y, z, fx, 0, fz, 0.5f, 0.5f, col[0], col[1], col[2]); m.SetUv2(c, type, group + offset * 0.001f);   // uv2: lens type, group (integer part) + phase offset (fraction)
            int first = m.VertexCount;
            for (int i = 0; i <= seg; i++)
            {
                float a = (float)(2 * Math.PI * i / seg), ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                int vv = m.AddVertex(x + rx * ca * r, y + sa * r, z + rz * ca * r, fx, 0, fz, 0.5f + ca * 0.5f, 0.5f + sa * 0.5f, col[0], col[1], col[2]);
                m.SetUv2(vv, type, group + offset * 0.001f);
            }
            for (int i = 0; i < seg; i++) m.AddTriangle(1, c, first + i, first + i + 1);
            MeshUtil.FixWinding(m);
        }

        static void Signs(MeshData m, PointFeature p, List<(RoadWay way, int idx)> ways, double te, double tn, Func<double, double, float> ground)
        {
            foreach (var (w, i) in ways)
            {
                if (!w.IsVehicular) continue;
                for (int side = -1; side <= 1; side += 2)
                {
                    int j = i + side; if (j < 0 || j >= w.PointCount) continue;
                    if (w.OneWay == OneWay.Forward && side > 0 || w.OneWay == OneWay.Reverse && side < 0) continue;
                    double dx = w.E[i] - w.E[j], dz = w.N[i] - w.N[j], l = Math.Sqrt(dx * dx + dz * dz); if (l < 1) continue; dx /= l; dz /= l;
                    double rx = dz, rz = -dx; float half = w.CarriagewayWidth() * 0.5f;
                    double px = w.E[i] - dx * 4.0 + rx * (half + 0.8) - te, pz = w.N[i] - dz * 4.0 + rz * (half + 0.8) - tn;
                    float gy = ground(px, pz), yaw = (float)Math.Atan2(-dx, -dz);
                    MeshUtil.Cylinder(m, 0, (float)px, gy, (float)pz, 0.04f, 0.04f, 2.3f, 6, 120, 124, 128, capTop: true);
                    float fx = (float)Math.Sin(yaw), fz = (float)Math.Cos(yaw);
                    if (p.Kind == PointKind.StopSign) Octagon(m, (float)px + fx * 0.05f, gy + 2.2f, (float)pz + fz * 0.05f, yaw, 0.38f);
                    else GiveWay(m, (float)px + fx * 0.05f, gy + 2.2f, (float)pz + fz * 0.05f, yaw, 0.42f);
                }
            }
        }

        static void Octagon(MeshData m, float x, float y, float z, float yaw, float r)
        {
            float fx = (float)Math.Sin(yaw), fz = (float)Math.Cos(yaw), rx = fz, rz = -fx;
            Fan(m, x, y, z, fx, fz, rx, rz, r, 8, Math.PI / 8, 200, 25, 30);                     // red face
            Fan(m, x + fx * 0.004f, y, z + fz * 0.004f, fx, fz, rx, rz, r * 0.86f, 8, Math.PI / 8, 235, 235, 235, outline: true, innerR: r * 0.80f);   // white rim
            // "STOP" bar placeholder: white strip
            MeshUtil.Box(m, 2, x + fx * 0.006f, y, z + fz * 0.006f, r * 1.1f, r * 0.28f, 0.004f, 245, 245, 245, yaw);
        }

        static void GiveWay(MeshData m, float x, float y, float z, float yaw, float r)
        {
            float fx = (float)Math.Sin(yaw), fz = (float)Math.Cos(yaw), rx = fz, rz = -fx;
            Fan(m, x, y - r * 0.2f, z, fx, fz, rx, rz, r, 3, -Math.PI / 2, 200, 25, 30);         // inverted red triangle
            Fan(m, x + fx * 0.004f, y - r * 0.2f, z + fz * 0.004f, fx, fz, rx, rz, r * 0.62f, 3, -Math.PI / 2, 245, 245, 245);
        }

        static void Fan(MeshData m, float x, float y, float z, float fx, float fz, float rx, float rz, float r, int n, double rot, byte cr, byte cg, byte cb, bool outline = false, float innerR = 0)
        {
            int c = m.AddVertex(x, y, z, fx, 0, fz, 0.5f, 0.5f, cr, cg, cb);
            int first = m.VertexCount;
            for (int i = 0; i <= n; i++)
            {
                double a = rot + 2 * Math.PI * i / n; float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                m.AddVertex(x + rx * ca * r, y + sa * r, z + rz * ca * r, fx, 0, fz, 0.5f + ca * 0.5f, 0.5f + sa * 0.5f, cr, cg, cb);
            }
            for (int i = 0; i < n; i++) m.AddTriangle(2, c, first + i, first + i + 1);
            MeshUtil.FixWinding(m);
        }

        static void Lamp(MeshData m, float x, float gy, float z, float yaw)
        {
            MeshUtil.Cylinder(m, 0, x, gy, z, 0.09f, 0.055f, 7.5f, 10, 78, 82, 86, capTop: true);
            MeshUtil.Box(m, 0, x + 0.7f * (float)Math.Cos(yaw), gy + 7.55f, z - 0.7f * (float)Math.Sin(yaw), 1.5f, 0.07f, 0.09f, 78, 82, 86, yaw);
            MeshUtil.Box(m, 0, x + 1.35f * (float)Math.Cos(yaw), gy + 7.45f, z - 1.35f * (float)Math.Sin(yaw), 0.5f, 0.1f, 0.22f, 60, 62, 66, yaw);
        }

        static void BusStop(MeshData m, float x, float gy, float z, float yaw)
        {
            // shelter: back wall (glass-look), roof, two posts, bench
            MeshUtil.Box(m, 0, x, gy + 2.45f, z, 3.0f, 0.1f, 1.4f, 120, 125, 130, yaw);
            MeshUtil.Box(m, 0, x - 1.4f * (float)Math.Cos(yaw), gy + 1.2f, z + 1.4f * (float)Math.Sin(yaw), 0.08f, 2.4f, 0.08f, 90, 94, 98, yaw);
            MeshUtil.Box(m, 0, x + 1.4f * (float)Math.Cos(yaw), gy + 1.2f, z - 1.4f * (float)Math.Sin(yaw), 0.08f, 2.4f, 0.08f, 90, 94, 98, yaw);
            MeshUtil.Box(m, 0, x, gy + 0.5f, z, 2.2f, 0.06f, 0.45f, 150, 110, 70, yaw);
            MeshUtil.Cylinder(m, 0, x + 2.2f * (float)Math.Cos(yaw), gy, z - 2.2f * (float)Math.Sin(yaw), 0.04f, 0.04f, 2.9f, 6, 120, 124, 128);
        }
    }
}
