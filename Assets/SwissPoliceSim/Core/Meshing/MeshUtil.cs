using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Meshing
{
    /// <summary>Small geometry toolkit used by the procedural generators (cylinders, boxes, cones, spheres, lathe surfaces).</summary>
    public static class MeshUtil
    {
        /// <summary>Ersetzt nicht-endliche oder absurde Positionswerte durch den Wert des naechsten gueltigen Nachbarvertex (zuerst rueckwaerts, dann vorwaerts). Gibt die Anzahl korrigierter Werte zurueck. Ein einziges NaN kann sonst die Grafik-Engine zum Absturz bringen.</summary>
        public static int SanitizePositions(MeshData m)
        {
            var p = m.Positions; int fixedCount = 0, n = p.Count;
            for (int axis = 0; axis < 3; axis++)
            {
                for (int i = axis; i < n; i += 3)
                {
                    float v = p[i]; if (float.IsFinite(v) && Math.Abs(v) <= 1e5f) continue;
                    float rep = float.NaN;
                    for (int j = i - 3; j >= axis && float.IsNaN(rep); j -= 3) if (float.IsFinite(p[j]) && Math.Abs(p[j]) <= 1e5f) rep = p[j];
                    for (int j = i + 3; j < n && float.IsNaN(rep); j += 3) if (float.IsFinite(p[j]) && Math.Abs(p[j]) <= 1e5f) rep = p[j];
                    p[i] = float.IsNaN(rep) ? 0f : rep; fixedCount++;
                }
            }
            return fixedCount;
        }

        public static void Box(MeshData m, int sub, float cx, float cy, float cz, float sx, float sy, float sz, byte r, byte g, byte b, float yaw = 0)
        {
            float hx = sx / 2, hy = sy / 2, hz = sz / 2;
            float c = (float)Math.Cos(yaw), s = (float)Math.Sin(yaw);
            float[] P(float x, float y, float z) => new[] { cx + x * c + z * s, cy + y, cz - x * s + z * c };
            var v = new[] { P(-hx, -hy, -hz), P(hx, -hy, -hz), P(hx, hy, -hz), P(-hx, hy, -hz), P(-hx, -hy, hz), P(hx, -hy, hz), P(hx, hy, hz), P(-hx, hy, hz) };
            QuadOut(m, sub, v[5], v[4], v[7], v[6], 0, 0, 1, c, s, r, g, b);   // +z
            QuadOut(m, sub, v[0], v[1], v[2], v[3], 0, 0, -1, c, s, r, g, b);  // -z
            QuadOut(m, sub, v[1], v[5], v[6], v[2], 1, 0, 0, c, s, r, g, b);   // +x
            QuadOut(m, sub, v[4], v[0], v[3], v[7], -1, 0, 0, c, s, r, g, b);  // -x
            QuadOut(m, sub, v[3], v[2], v[6], v[7], 0, 1, 0, c, s, r, g, b);   // +y
            QuadOut(m, sub, v[4], v[5], v[1], v[0], 0, -1, 0, c, s, r, g, b);  // -y
        }

        /// <summary>Quad with an explicit outward normal given in the box's local frame (rotated by yaw); winding auto-fixed.</summary>
        static void QuadOut(MeshData m, int sub, float[] a, float[] b, float[] c, float[] d, float nx, float ny, float nz, float cos, float sin, byte r, byte g, byte bl)
        {
            float wx = nx * cos + nz * sin, wz = -nx * sin + nz * cos;
            float ex = b[0] - a[0], ey = b[1] - a[1], ez = b[2] - a[2], fx = d[0] - a[0], fy = d[1] - a[1], fz = d[2] - a[2];
            float cxn = ey * fz - ez * fy, cyn = ez * fx - ex * fz, czn = ex * fy - ey * fx;
            if (cxn * wx + cyn * ny + czn * wz < 0) { var t = b; b = d; d = t; }
            int i0 = m.AddVertex(a[0], a[1], a[2], wx, ny, wz, 0, 0, r, g, bl), i1 = m.AddVertex(b[0], b[1], b[2], wx, ny, wz, 1, 0, r, g, bl);
            int i2 = m.AddVertex(c[0], c[1], c[2], wx, ny, wz, 1, 1, r, g, bl), i3 = m.AddVertex(d[0], d[1], d[2], wx, ny, wz, 0, 1, r, g, bl);
            m.AddTriangle(sub, i0, i1, i2); m.AddTriangle(sub, i0, i2, i3);
        }

        /// <summary>Vertical (Y axis) tapered cylinder with optional caps.</summary>
        public static void Cylinder(MeshData m, int sub, float cx, float y0, float cz, float r0, float r1, float h, int seg, byte r, byte g, byte b, bool capTop = true, bool capBottom = false)
        {
            int first = m.VertexCount;
            float slope = (r0 - r1) / Math.Max(1e-4f, h);
            for (int i = 0; i <= seg; i++)
            {
                float a = (float)(2 * Math.PI * i / seg), ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                float ny = slope, l = (float)Math.Sqrt(1 + ny * ny);
                m.AddVertex(cx + ca * r0, y0, cz + sa * r0, ca / l, ny / l, sa / l, i / (float)seg, 0, r, g, b);
                m.AddVertex(cx + ca * r1, y0 + h, cz + sa * r1, ca / l, ny / l, sa / l, i / (float)seg, 1, r, g, b);
            }
            for (int i = 0; i < seg; i++)
            {
                int a = first + i * 2;
                // outward facing (clockwise from outside in Unity)
                m.AddTriangle(sub, a, a + 1, a + 2); m.AddTriangle(sub, a + 1, a + 3, a + 2);
            }
            if (capTop && r1 > 1e-4f) Disc(m, sub, cx, y0 + h, cz, r1, seg, r, g, b, true);
            if (capBottom && r0 > 1e-4f) Disc(m, sub, cx, y0, cz, r0, seg, r, g, b, false);
        }

        public static void Disc(MeshData m, int sub, float cx, float y, float cz, float rad, int seg, byte r, byte g, byte b, bool up)
        {
            int c = m.AddVertex(cx, y, cz, 0, up ? 1 : -1, 0, 0.5f, 0.5f, r, g, b);
            int first = m.VertexCount;
            for (int i = 0; i <= seg; i++)
            {
                float a = (float)(2 * Math.PI * i / seg);
                m.AddVertex(cx + (float)Math.Cos(a) * rad, y, cz + (float)Math.Sin(a) * rad, 0, up ? 1 : -1, 0, 0.5f + (float)Math.Cos(a) * 0.5f, 0.5f + (float)Math.Sin(a) * 0.5f, r, g, b);
            }
            for (int i = 0; i < seg; i++)
            {
                if (up) m.AddTriangle(sub, c, first + i + 1, first + i); else m.AddTriangle(sub, c, first + i, first + i + 1);
            }
        }

        /// <summary>Flat disc facing +Z or -Z (for sign faces, lenses).</summary>
        public static void DiscZ(MeshData m, int sub, float cx, float cy, float z, float rad, int seg, byte r, byte g, byte b, bool facePosZ)
        {
            int c = m.AddVertex(cx, cy, z, 0, 0, facePosZ ? 1 : -1, 0.5f, 0.5f, r, g, b);
            int first = m.VertexCount;
            for (int i = 0; i <= seg; i++)
            {
                float a = (float)(2 * Math.PI * i / seg);
                m.AddVertex(cx + (float)Math.Cos(a) * rad, cy + (float)Math.Sin(a) * rad, z, 0, 0, facePosZ ? 1 : -1, 0.5f + (float)Math.Cos(a) * 0.5f, 0.5f + (float)Math.Sin(a) * 0.5f, r, g, b);
            }
            for (int i = 0; i < seg; i++)
            {
                // viewed from +Z in a left-handed system: clockwise = increasing angle reversed
                if (facePosZ) m.AddTriangle(sub, c, first + i + 1, first + i); else m.AddTriangle(sub, c, first + i, first + i + 1);
            }
        }

        /// <summary>Unit icosphere (radius 1) with the given subdivision level; returns vertex positions and triangle indices.</summary>
        public static void Icosphere(int level, out List<float[]> verts, out List<int[]> tris)
        {
            float t = (1f + (float)Math.Sqrt(5)) / 2f;
            verts = new List<float[]>
            {
                N(-1, t, 0), N(1, t, 0), N(-1, -t, 0), N(1, -t, 0), N(0, -1, t), N(0, 1, t), N(0, -1, -t), N(0, 1, -t), N(t, 0, -1), N(t, 0, 1), N(-t, 0, -1), N(-t, 0, 1)
            };
            tris = new List<int[]>
            {
                new[] { 0, 11, 5 }, new[] { 0, 5, 1 }, new[] { 0, 1, 7 }, new[] { 0, 7, 10 }, new[] { 0, 10, 11 },
                new[] { 1, 5, 9 }, new[] { 5, 11, 4 }, new[] { 11, 10, 2 }, new[] { 10, 7, 6 }, new[] { 7, 1, 8 },
                new[] { 3, 9, 4 }, new[] { 3, 4, 2 }, new[] { 3, 2, 6 }, new[] { 3, 6, 8 }, new[] { 3, 8, 9 },
                new[] { 4, 9, 5 }, new[] { 2, 4, 11 }, new[] { 6, 2, 10 }, new[] { 8, 6, 7 }, new[] { 9, 8, 1 }
            };
            for (int l = 0; l < level; l++)
            {
                var cache = new Dictionary<long, int>(); var nt = new List<int[]>();
                foreach (var tr in tris)
                {
                    int a = Mid(verts, cache, tr[0], tr[1]), b = Mid(verts, cache, tr[1], tr[2]), c = Mid(verts, cache, tr[2], tr[0]);
                    nt.Add(new[] { tr[0], a, c }); nt.Add(new[] { tr[1], b, a }); nt.Add(new[] { tr[2], c, b }); nt.Add(new[] { a, b, c });
                }
                tris = nt;
            }
            // the base list is counter-clockwise seen from outside (right handed); flip for Unity front faces
            for (int i = 0; i < tris.Count; i++) { var tr = tris[i]; tris[i] = new[] { tr[0], tr[2], tr[1] }; }
        }

        static float[] N(float x, float y, float z) { float l = (float)Math.Sqrt(x * x + y * y + z * z); return new[] { x / l, y / l, z / l }; }

        static int Mid(List<float[]> v, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int i)) return i;
            var p = N(v[a][0] + v[b][0], v[a][1] + v[b][1], v[a][2] + v[b][2]);
            v.Add(p); cache[key] = v.Count - 1; return v.Count - 1;
        }

        /// <summary>Surface of revolution around the X axis (wheels): profile = list of (x, radius) from one side to the other.</summary>
        public static void LatheX(MeshData m, int sub, float cx, float cy, float cz, IList<float[]> profile, int seg, byte r, byte g, byte b, float[] colorVar = null)
        {
            int rows = profile.Count; int first = m.VertexCount;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i <= seg; i++)
                {
                    float a = (float)(2 * Math.PI * i / seg), ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                    var p = profile[j];
                    // normal from profile tangent
                    var p0 = profile[Math.Max(0, j - 1)]; var p1 = profile[Math.Min(rows - 1, j + 1)];
                    float tx = p1[0] - p0[0], tr = p1[1] - p0[1], tl = (float)Math.Sqrt(tx * tx + tr * tr); if (tl < 1e-6f) { tx = 1; tr = 0; tl = 1; }
                    float nxr = tr / tl, nr = -tx / tl;     // (axis, radial) normal components
                    m.AddVertex(cx + p[0], cy + ca * p[1], cz + sa * p[1], nxr, nr * ca, nr * sa, i / (float)seg, j / (float)(rows - 1), r, g, b);
                }
            for (int j = 0; j < rows - 1; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = first + j * (seg + 1) + i, bb = a + 1, c = a + seg + 1, d = c + 1;
                    m.AddTriangle(sub, a, c, bb); m.AddTriangle(sub, bb, c, d);
                }
        }

        /// <summary>Flips the last <paramref name="quads"/> quads (2 triangles each, appended to submesh 1) whose face normal points toward the mesh centre line (box faces built from arbitrary corner order).</summary>
        public static void FixWindingLast(MeshData m, int quads)
        {
            var idx = m.Indices[1];
            int start = idx.Count - quads * 6;
            // centre of the appended box
            float cx = 0, cy = 0, cz = 0; int n = 0;
            for (int i = start; i < idx.Count; i++) { int v = idx[i]; cx += m.Positions[v * 3]; cy += m.Positions[v * 3 + 1]; cz += m.Positions[v * 3 + 2]; n++; }
            cx /= n; cy /= n; cz /= n;
            for (int i = start; i + 2 < idx.Count; i += 3)
            {
                int a = idx[i], b = idx[i + 1], c = idx[i + 2];
                float ax = m.Positions[a * 3], ay = m.Positions[a * 3 + 1], az = m.Positions[a * 3 + 2];
                float ex = m.Positions[b * 3] - ax, ey = m.Positions[b * 3 + 1] - ay, ez = m.Positions[b * 3 + 2] - az;
                float fx = m.Positions[c * 3] - ax, fy = m.Positions[c * 3 + 1] - ay, fz = m.Positions[c * 3 + 2] - az;
                float nx = ey * fz - ez * fy, ny = ez * fx - ex * fz, nz = ex * fy - ey * fx;
                if (nx * (ax - cx) + ny * (ay - cy) + nz * (az - cz) < 0) { idx[i + 1] = c; idx[i + 2] = b; }
            }
        }

        /// <summary>Makes triangle winding consistent with the stored vertex normals (flips triangles whose face normal opposes them).</summary>
        public static void FixWinding(MeshData m)
        {
            foreach (var idx in m.Indices)
                for (int i = 0; i + 2 < idx.Count; i += 3)
                {
                    int a = idx[i], b = idx[i + 1], c = idx[i + 2];
                    float ax = m.Positions[a * 3], ay = m.Positions[a * 3 + 1], az = m.Positions[a * 3 + 2];
                    float ex = m.Positions[b * 3] - ax, ey = m.Positions[b * 3 + 1] - ay, ez = m.Positions[b * 3 + 2] - az;
                    float fx = m.Positions[c * 3] - ax, fy = m.Positions[c * 3 + 1] - ay, fz = m.Positions[c * 3 + 2] - az;
                    float nx = ey * fz - ez * fy, ny = ez * fx - ex * fz, nz = ex * fy - ey * fx;
                    float sx = m.Normals[a * 3] + m.Normals[b * 3] + m.Normals[c * 3], sy = m.Normals[a * 3 + 1] + m.Normals[b * 3 + 1] + m.Normals[c * 3 + 1], sz = m.Normals[a * 3 + 2] + m.Normals[b * 3 + 2] + m.Normals[c * 3 + 2];
                    if (nx * sx + ny * sy + nz * sz < 0) { idx[i + 1] = c; idx[i + 2] = b; }
                }
        }
    }
}
