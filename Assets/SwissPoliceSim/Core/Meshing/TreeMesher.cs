using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Meshing
{
    /// <summary>
    /// Procedural trees of unit size (height 1, crown radius 1; instances scale x/z by radius and y by height).
    /// Crowns are made of alpha-cut leaf-cluster cards around an opaque dark core with outward-pointing shading normals (soft, volumetric
    /// lighting; darker inside, lighter outside), on top of a real trunk with limbs. Kinds: 0 broadleaf, 1 conifer.
    /// Foliage submesh must use the FoliageAtlas texture (alpha clip, double sided).
    /// </summary>
    public static class TreeMesher
    {
        public const int TrunkSub = 0, FoliageSub = 1;
        public static int Variants = 4;

        public const int RatioBuckets = 6;
        /// <summary>Crown radius / height ratio is quantised so meshes can be shared; instances then scale uniformly by the tree height.</summary>
        public static int RatioBucket(float radius, float height) { float r = radius / Math.Max(0.5f, height); return Math.Max(0, Math.Min(RatioBuckets - 1, (int)Math.Round((r - 0.15f) / 0.07f))); }
        public static float BucketRatio(int bucket) => 0.15f + bucket * 0.07f;

        /// <summary>Unit height tree (height 1 in y, crown radius = ratio in x/z). Scale uniformly by the real height.</summary>
        public static MeshData Build(byte kind, int variant, int lod, int ratioBucket = 2)
        {
            var m = new MeshData(2);
            var rnd = new Random(1234 + variant * 977 + kind * 31);
            float ratio = BucketRatio(ratioBucket);
            if (kind == 0) Broadleaf(m, rnd, lod, ratio); else Conifer(m, rnd, lod, ratio);
            return m;
        }

        // ------------------------------------------------------------------ helpers
        static void Norm(ref float x, ref float y, ref float z) { float l = (float)Math.Sqrt(x * x + y * y + z * z); if (l < 1e-6f) { x = 0; y = 1; z = 0; return; } x /= l; y /= l; z /= l; }

        /// <summary>Camera-independent card: centre c, facing f, roll about f, size (w,h), atlas u range, shading normal s.</summary>
        static void Card(MeshData m, float cx, float cy, float cz, float fx, float fy, float fz, float roll, float w, float h, float u0, float u1, byte r, byte g, byte b, float sx, float sy, float sz)
        {
            Norm(ref fx, ref fy, ref fz); Norm(ref sx, ref sy, ref sz);
            // basis: right = normalize(cross(worldUp, f)), up = cross(f, right)
            float rx = fz, ry = 0, rz = -fx; if (Math.Abs(rx) + Math.Abs(rz) < 1e-4f) { rx = 1; rz = 0; }
            Norm(ref rx, ref ry, ref rz);
            float ux = fy * rz - fz * ry, uy = fz * rx - fx * rz, uz = fx * ry - fy * rx;
            float cr = (float)Math.Cos(roll), sr = (float)Math.Sin(roll);
            float ax = rx * cr + ux * sr, ay = ry * cr + uy * sr, az = rz * cr + uz * sr;
            float bx = -rx * sr + ux * cr, by = -ry * sr + uy * cr, bz = -rz * sr + uz * cr;
            float hw = w * 0.5f, hh = h * 0.5f;
            int a = m.AddVertex(cx - ax * hw - bx * hh, cy - ay * hw - by * hh, cz - az * hw - bz * hh, sx, sy, sz, u0, 0, r, g, b);
            int bb = m.AddVertex(cx + ax * hw - bx * hh, cy + ay * hw - by * hh, cz + az * hw - bz * hh, sx, sy, sz, u1, 0, r, g, b);
            int c = m.AddVertex(cx + ax * hw + bx * hh, cy + ay * hw + by * hh, cz + az * hw + bz * hh, sx, sy, sz, u1, 1, r, g, b);
            int d = m.AddVertex(cx - ax * hw + bx * hh, cy - ay * hw + by * hh, cz - az * hw + bz * hh, sx, sy, sz, u0, 1, r, g, b);
            // winding so that the geometric normal agrees with f
            float ex = (ax * w), ey = (ay * w), ez = (az * w), gx = bx * h, gy = by * h, gz = bz * h;
            float nx = ey * gz - ez * gy, ny = ez * gx - ex * gz, nz = ex * gy - ey * gx;
            if (nx * fx + ny * fy + nz * fz >= 0) { m.AddTriangle(FoliageSub, a, bb, c); m.AddTriangle(FoliageSub, a, c, d); }
            else { m.AddTriangle(FoliageSub, a, c, bb); m.AddTriangle(FoliageSub, a, d, c); }
        }

        /// <summary>Tapered limb between two points (open ends), used for trunk flare and branches.</summary>
        static void Limb(MeshData m, float x0, float y0, float z0, float x1, float y1, float z1, float r0, float r1, int seg, byte cr, byte cg, byte cb)
        {
            float dx = x1 - x0, dy = y1 - y0, dz = z1 - z0; float l = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz); if (l < 1e-5f) return;
            dx /= l; dy /= l; dz /= l;
            float px = Math.Abs(dy) > 0.9f ? 1 : 0, py = Math.Abs(dy) > 0.9f ? 0 : 1, pz = 0;
            float ux = dy * pz - dz * py, uy = dz * px - dx * pz, uz = dx * py - dy * px; Norm(ref ux, ref uy, ref uz);
            float vx = dy * uz - dz * uy, vy = dz * ux - dx * uz, vz = dx * uy - dy * ux;
            int first = m.VertexCount;
            for (int i = 0; i <= seg; i++)
            {
                float a = (float)(2 * Math.PI * i / seg), ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                float nx = ux * ca + vx * sa, ny = uy * ca + vy * sa, nz = uz * ca + vz * sa;
                m.AddVertex(x0 + nx * r0, y0 + ny * r0, z0 + nz * r0, nx, ny, nz, i / (float)seg, 0, cr, cg, cb);
                m.AddVertex(x1 + nx * r1, y1 + ny * r1, z1 + nz * r1, nx, ny, nz, i / (float)seg, 1, (byte)(cr * 0.9f), (byte)(cg * 0.9f), (byte)(cb * 0.9f));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = first + i * 2, b = a + 1, c = a + 2, d = a + 3;
                m.AddTriangle(TrunkSub, a, c, b); m.AddTriangle(TrunkSub, b, c, d);
            }
        }

        static byte B(float v) => (byte)(v < 0 ? 0 : (v > 255 ? 255 : v));

        // ------------------------------------------------------------------ broadleaf
        static void Broadleaf(MeshData m, Random rnd, int lod, float R)
        {
            float trunkH = 0.30f, cy = 0.63f, ry = 0.34f;
            byte tr = 84, tg = 66, tb = 52;
            float tw = 0.026f;
            Limb(m, 0, 0, 0, 0, trunkH * 0.3f, 0, tw * 1.7f, tw * 1.15f, lod == 0 ? 8 : 5, tr, tg, tb);
            Limb(m, 0, trunkH * 0.3f, 0, 0.004f, trunkH + 0.10f, 0, tw * 1.15f, tw * 0.8f, lod == 0 ? 8 : 5, tr, tg, tb);
            if (lod == 0)
            {
                int limbs = 4 + rnd.Next(3);
                for (int i = 0; i < limbs; i++)
                {
                    float a = (float)(i * 2 * Math.PI / limbs + rnd.NextDouble() * 0.6), len = R * (0.45f + 0.25f * (float)rnd.NextDouble()), up = 0.20f + 0.14f * (float)rnd.NextDouble();
                    Limb(m, 0.002f, trunkH + 0.03f, 0, (float)Math.Cos(a) * len, trunkH + 0.03f + up, (float)Math.Sin(a) * len, tw * 0.6f, tw * 0.25f, 5, tr, tg, tb);
                }
            }
            // opaque dark core
            MeshUtil.Icosphere(lod == 0 ? 2 : 1, out var verts, out var tris);
            int first = m.VertexCount;
            foreach (var v in verts) m.AddVertex(v[0] * R * 0.70f, cy + v[1] * ry * 0.70f, v[2] * R * 0.70f, v[0], v[1], v[2], 0, 0, 34, 62, 28);
            foreach (var t in tris) m.AddTriangle(TrunkSub, first + t[0], first + t[1], first + t[2]);
            m.RecalculateNormals(first);
            if (lod >= 2) return;
            int cards = lod == 0 ? 230 : 56; float size = (lod == 0 ? 0.125f : 0.2f) * (0.8f + R);
            float ph = (float)rnd.NextDouble() * 6.28f;
            for (int i = 0; i < cards; i++)
            {
                float t = (i + 0.5f) / cards, y = 1 - 2 * t; float rr = (float)Math.Sqrt(Math.Max(0, 1 - y * y)); float th = i * 2.39996f + ph;
                float nx = (float)Math.Cos(th) * rr, nz = (float)Math.Sin(th) * rr, nyv = y;
                float shell = 0.78f + 0.24f * (float)rnd.NextDouble();
                float px = nx * shell * R, py = cy + nyv * ry * shell * (nyv < 0 ? 0.75f : 1f), pz = nz * shell * R;
                if (py < trunkH + 0.08f) py = trunkH + 0.08f + 0.03f * (float)rnd.NextDouble();
                float light = 0.55f + 0.45f * (nyv * 0.5f + 0.5f); light *= 0.88f + 0.24f * (float)rnd.NextDouble();
                float tint = 0.9f + 0.2f * (float)rnd.NextDouble();
                byte r = B(255 * light * tint), g = B(255 * light), b = B(255 * light * 0.92f);
                float fx = nx + (float)(rnd.NextDouble() - 0.5) * 1.0f, fy = nyv * 0.7f + 0.35f + (float)(rnd.NextDouble() - 0.5) * 1.0f, fz = nz + (float)(rnd.NextDouble() - 0.5) * 1.0f;
                float sz = size * (0.8f + 0.5f * (float)rnd.NextDouble());
                Card(m, px, py, pz, fx, fy, fz, (float)(rnd.NextDouble() * 6.28), sz, sz, 0f, 0.5f, r, g, b, nx, nyv * 0.7f + 0.3f, nz);
            }
        }

        // ------------------------------------------------------------------ conifer
        static void Conifer(MeshData m, Random rnd, int lod, float R)
        {
            float trunkH = 0.10f;
            Limb(m, 0, 0, 0, 0, 0.97f, 0, 0.020f, 0.003f, lod == 0 ? 6 : 4, 78, 58, 44);
            int tiers = lod == 0 ? 16 : lod == 1 ? 8 : 3;
            MeshUtil.Cylinder(m, TrunkSub, 0, trunkH + 0.02f, 0, R * 0.42f, 0.0f, 0.88f, lod == 0 ? 8 : 5, 20, 40, 26, capTop: false);
            if (lod >= 2) { MeshUtil.Cylinder(m, TrunkSub, 0, trunkH, 0, R * 1.0f, 0.0f, 0.88f, 7, 36, 76, 46, capTop: false); return; }
            for (int t = 0; t < tiers; t++)
            {
                float k = t / (float)(tiers - 1);
                float y = trunkH + 0.03f + k * 0.82f;
                float len = R * (1.0f - 0.88f * k) * (0.92f + 0.16f * (float)rnd.NextDouble());
                int n = lod == 0 ? Math.Max(8, (int)(15 - 7 * k)) : Math.Max(6, (int)(11 - 5 * k));
                float ph = (float)rnd.NextDouble() * 6.28f; float width = (lod == 0 ? 0.115f : 0.17f) * (1.15f - 0.4f * k);
                for (int i = 0; i < n; i++)
                {
                    float a = ph + i * (float)(2 * Math.PI / n) + (float)(rnd.NextDouble() - 0.5) * 0.35f;
                    float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                    float droopAng = 0.70f - 0.38f * k;                       // radians below horizontal
                    float dirH = (float)Math.Cos(droopAng), dirV = -(float)Math.Sin(droopAng);
                    float cxp = ca * len * 0.5f * dirH, czp = sa * len * 0.5f * dirH, cyp = y + dirV * len * 0.5f;
                    float light = 0.55f + 0.45f * k + 0.22f * (float)rnd.NextDouble();
                    byte r = B(255 * light), g = B(255 * light), b = B(255 * light * 0.95f);
                    CardRadial(m, cxp, cyp, czp, ca * dirH, dirV, sa * dirH, len, width * (0.85f + 0.3f * (float)rnd.NextDouble()), r, g, b, ca * 0.6f, 0.8f, sa * 0.6f);
                }
            }
        }

        /// <summary>Branch card: long axis (v, trunk→tip) along (dx,dy,dz), faces up (both sides visible).</summary>
        static void CardRadial(MeshData m, float cx, float cy, float cz, float dx, float dy, float dz, float length, float width, byte r, byte g, byte b, float sx, float sy, float sz)
        {
            Norm(ref dx, ref dy, ref dz); Norm(ref sx, ref sy, ref sz);
            // across = normalize(cross(d, up))
            float ax = dy * 0 - dz * 1, ay = dz * 0 - dx * 0, az = dx * 1 - dy * 0; Norm(ref ax, ref ay, ref az);
            float hw = width * 0.5f, hl = length * 0.5f;
            float x0 = cx - dx * hl, y0 = cy - dy * hl, z0 = cz - dz * hl, x1 = cx + dx * hl, y1 = cy + dy * hl, z1 = cz + dz * hl;
            int a = m.AddVertex(x0 - ax * hw * 1.0f, y0 - ay * hw, z0 - az * hw, sx, sy, sz, 0.5f, 0, r, g, b);
            int bb = m.AddVertex(x0 + ax * hw, y0 + ay * hw, z0 + az * hw, sx, sy, sz, 1.0f, 0, r, g, b);
            int c = m.AddVertex(x1 + ax * hw, y1 + ay * hw, z1 + az * hw, sx, sy, sz, 1.0f, 1, r, g, b);
            int d = m.AddVertex(x1 - ax * hw, y1 - ay * hw, z1 - az * hw, sx, sy, sz, 0.5f, 1, r, g, b);
            m.AddTriangle(FoliageSub, a, bb, c); m.AddTriangle(FoliageSub, a, c, d);
        }
    }
}
