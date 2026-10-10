using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Meshing
{
    /// <summary>
    /// Procedural tree meshes of unit size: total height 1, crown radius 1 (instances scale x/z by radius and y by height).
    /// Kinds: 0 broadleaf, 1 conifer. LOD 0 ≈ 350 triangles, LOD 1 ≈ 90.
    /// </summary>
    public static class TreeMesher
    {
        public const int TrunkSub = 0, FoliageSub = 1;
        public static int Variants = 4;

        public static MeshData Build(byte kind, int variant, int lod)
        {
            var m = new MeshData(2);
            var rnd = new Random(1234 + variant * 977 + kind * 31);
            if (kind == 0) Broadleaf(m, rnd, lod); else Conifer(m, rnd, lod);
            return m;
        }

        static void Broadleaf(MeshData m, Random rnd, int lod)
        {
            float trunkH = 0.32f, trunkR = 0.045f;
            MeshUtil.Cylinder(m, TrunkSub, 0, 0, 0, trunkR * 1.4f, trunkR, trunkH + 0.12f, lod == 0 ? 8 : 5, 92, 70, 52, capTop: false);
            MeshUtil.Icosphere(lod == 0 ? 3 : (lod == 1 ? 2 : 1), out var verts, out var tris);
            // per-vertex noise displacement (lumpy crown), shared by coincident vertices through position hashing
            var disp = new float[verts.Count];
            float f1 = 2.3f + (float)rnd.NextDouble(), ph = (float)rnd.NextDouble() * 10;
            for (int i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                disp[i] = 1f + 0.18f * (float)(Math.Sin(v[0] * f1 + ph) * Math.Cos(v[2] * f1 * 1.3f + ph) + 0.5 * Math.Sin(v[1] * f1 * 2.1f + ph * 2)) ;
            }
            float cy = trunkH + (1f - trunkH) * 0.5f, ry = (1f - trunkH) * 0.5f;
            int first = m.VertexCount;
            for (int i = 0; i < verts.Count; i++)
            {
                var v = verts[i]; float d = disp[i];
                float x = v[0] * d, y = v[1] * d * (ry / 0.5f > 0 ? 1 : 1), z = v[2] * d;
                float px = x, py = cy + y * ry, pz = z;
                float shade = 0.62f + 0.38f * (v[1] * 0.5f + 0.5f);               // darker underside
                float var = 0.82f + 0.30f * (float)((Math.Sin(v[0] * 13 + v[2] * 9 + ph) * Math.Cos(v[1] * 11 + ph * 0.7) + 1) * 0.5f);
                byte r = (byte)(Clamp(66 * shade * var)), g = (byte)(Clamp(112 * shade * var)), b = (byte)(Clamp(48 * shade * var));
                m.AddVertex(px, py, pz, v[0], v[1], v[2], 0, 0, r, g, b);
            }
            foreach (var t in tris) m.AddTriangle(FoliageSub, first + t[0], first + t[1], first + t[2]);
            m.RecalculateNormals(first);
        }

        static void Conifer(MeshData m, Random rnd, int lod)
        {
            float trunkH = 0.14f;
            MeshUtil.Cylinder(m, TrunkSub, 0, 0, 0, 0.05f, 0.03f, trunkH + 0.1f, lod == 0 ? 6 : 4, 84, 62, 46, capTop: false);
            int tiers = lod == 0 ? 5 : 3; int seg = lod == 0 ? 9 : 6;
            float y = trunkH;
            for (int t = 0; t < tiers; t++)
            {
                float k = t / (float)(tiers - 1 + 1e-4f);
                float baseR = Math.Max(0.28f, 1f - k * 0.78f) * (0.92f + 0.16f * (float)rnd.NextDouble());
                float h = (1f - trunkH) / tiers * 1.7f;
                float topY = Math.Min(1f, y + h);
                float shade = 0.7f + 0.3f * k;
                byte r = (byte)Clamp(34 * shade), g = (byte)Clamp(74 * shade), b = (byte)Clamp(44 * shade);
                int first = m.VertexCount;
                int apex = m.AddVertex(0, topY, 0, 0, 0.7f, 0, 0.5f, 1, r, g, b);
                for (int i = 0; i <= seg; i++)
                {
                    float a = (float)(2 * Math.PI * i / seg), ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                    float rr = baseR * (0.9f + 0.2f * (float)((Math.Sin(a * 3 + t) + 1) * 0.5f));
                    m.AddVertex(ca * rr, y, sa * rr, ca * 0.8f, 0.45f, sa * 0.8f, i / (float)seg, 0, (byte)Clamp(r * 0.8f), (byte)Clamp(g * 0.8f), (byte)Clamp(b * 0.8f));
                }
                for (int i = 0; i < seg; i++) m.AddTriangle(FoliageSub, apex, first + 1 + i + 1, first + 1 + i);
                y += (1f - trunkH) / tiers * 0.78f;
            }
        }

        static float Clamp(float v) => v < 0 ? 0 : (v > 255 ? 255 : v);
    }
}
