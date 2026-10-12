using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Features;

namespace SwissPoliceSim.Core.Meshing
{
    /// <summary>Railway track: ballast bed, two rails, sleepers (detail level) and catenary masts with a contact wire (standard gauge 1435 mm).</summary>
    public static class RailMesher
    {
        public const int Ballast = 0, Rails = 1, Sleepers = 2, Masts = 3, Submeshes = 4;
        public static MeshData NewMesh() => new MeshData(Submeshes);

        public static void AddRail(MeshData m, RailFeature r, double tE, double tN, Func<double, double, float> ground, bool detail, float cx = float.NaN, float cz = float.NaN, float rad = 0f)
        {
            int n = r.E.Length; if (n < 2 || r.Tunnel) return;
            var px = new float[n]; var pz = new float[n]; var py = new float[n];
            for (int i = 0; i < n; i++)
            {
                px[i] = (float)(r.E[i] - tE); pz[i] = (float)(r.N[i] - tN);
                float g = ground(px[i], pz[i]); py[i] = (r.Bridge && r.H != null && r.H.Length == n && !float.IsNaN(r.H[i]) ? r.H[i] : g) + 0.22f;
            }
            var nxs = new float[n]; var nzs = new float[n]; var tx = new float[n]; var tz = new float[n];
            for (int i = 0; i < n; i++)
            {
                float ax = 0, az = 0;
                if (i > 0) { float dx = px[i] - px[i - 1], dz = pz[i] - pz[i - 1], l = (float)Math.Sqrt(dx * dx + dz * dz); if (l > 1e-5f) { ax += dx / l; az += dz / l; } }
                if (i < n - 1) { float dx = px[i + 1] - px[i], dz = pz[i + 1] - pz[i], l = (float)Math.Sqrt(dx * dx + dz * dz); if (l > 1e-5f) { ax += dx / l; az += dz / l; } }
                float ll = (float)Math.Sqrt(ax * ax + az * az); if (ll < 1e-6f) { ax = 1; az = 0; ll = 1; }
                tx[i] = ax / ll; tz[i] = az / ll; nxs[i] = -tz[i]; nzs[i] = tx[i];       // left normal
            }
            // ballast: trapezoid profile (flat top 3.0 m, slopes to 4.2 m at the foot)
            Strip(m, Ballast, px, py, pz, nxs, nzs, -1.5f, 1.5f, 0f, 0f, 150, 146, 138, true);
            Strip(m, Ballast, px, py, pz, nxs, nzs, 1.5f, 2.1f, 0f, -0.28f, 140, 136, 128, true);
            Strip(m, Ballast, px, py, pz, nxs, nzs, -2.1f, -1.5f, -0.28f, 0f, 140, 136, 128, true);
            for (int side = -1; side <= 1; side += 2)
            {
                float o = 0.7175f * side;
                Strip(m, Rails, px, py, pz, nxs, nzs, o - 0.035f, o + 0.035f, 0.17f, 0.17f, 150, 150, 156, true);                    // railhead top
                Strip(m, Rails, px, py, pz, nxs, nzs, o + (side < 0 ? 0.035f : -0.035f), o + (side < 0 ? 0.035f : -0.035f), 0.0f, 0.17f, 100, 100, 108, false, side < 0 ? -1 : 1);
            }
            // sleepers + masts walk along the polyline
            float acc = 0, nextSleeper = 0, nextMast = 12;
            for (int i = 0; i < n - 1; i++)
            {
                float dx = px[i + 1] - px[i], dz = pz[i + 1] - pz[i], len = (float)Math.Sqrt(dx * dx + dz * dz); if (len < 1e-4f) continue;
                float ux = dx / len, uz = dz / len;
                for (float s = nextSleeper - acc; s < len; s += 0.65f)
                {
                    if (!detail) break;
                    float t = s / len, x = px[i] + dx * t, z = pz[i] + dz * t, y = py[i] + (py[i + 1] - py[i]) * t;
                    nextSleeper = acc + s + 0.65f;
                    // Schwellen nur nahe beim Spieler: ein Bahnhof hat zehntausende, das ergab 50-MB-Kacheln
                    if (rad > 0f && !float.IsNaN(cx) && ((x - cx) * (x - cx) + (z - cz) * (z - cz)) > rad * rad) continue;
                    MeshUtil.Box(m, Sleepers, x, y + 0.075f, z, 2.6f, 0.15f, 0.26f, 120, 118, 114, (float)Math.Atan2(-ux, -uz));
                    nextSleeper = acc + s + 0.65f;
                }
                for (float s = nextMast - acc; s < len; s += 62f)
                {
                    float t = s / len, x = px[i] + dx * t, z = pz[i] + dz * t, y = py[i] + (py[i + 1] - py[i]) * t; float sx = -uz * 3.4f, sz = ux * 3.4f;
                    MeshUtil.Box(m, Masts, x + sx, y + 3.2f, z + sz, 0.28f, 6.4f, 0.28f, 90, 90, 94);                                  // pole
                    MeshUtil.Box(m, Masts, x + sx * 0.5f, y + 6.0f, z + sz * 0.5f, 0.12f, 0.12f, 3.2f, 90, 90, 94, (float)Math.Atan2(sz, sx) * 0);   // cantilever (approx.)
                    nextMast = acc + s + 62f;
                }
                acc += len;
            }
            // contact wire: thin ribbon above the track
            if (detail)
            {
                var wy = new float[n]; for (int i = 0; i < n; i++) wy[i] = py[i] + 5.5f;
                Strip(m, Masts, px, wy, pz, nxs, nzs, -0.012f, 0.012f, 0f, 0f, 40, 40, 44, true);
            }
        }

        /// <summary>Ribbon between lateral offsets o0..o1 (m, left positive); heights relative lift y0 at o0 and y1 at o1; up = normal up (face up) or sideways wall.</summary>
        static void Strip(MeshData m, int sub, float[] px, float[] py, float[] pz, float[] nx, float[] nz, float o0, float o1, float y0, float y1, byte r, byte g, byte b, bool up, int side = 0)
        {
            int n = px.Length; int first = m.VertexCount; float run = 0;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) { float dx = px[i] - px[i - 1], dz = pz[i] - pz[i - 1]; run += (float)Math.Sqrt(dx * dx + dz * dz); }
                float ax = px[i] + nx[i] * o0, az = pz[i] + nz[i] * o0, bx = px[i] + nx[i] * o1, bz = pz[i] + nz[i] * o1;
                float nyv = up ? 1f : 0f; float nnx = up ? 0 : nx[i] * side, nnz = up ? 0 : nz[i] * side;
                if (up && Math.Abs(y1 - y0) > 0.01f) { float k = (y1 - y0) / Math.Max(1e-3f, o1 - o0); float l = (float)Math.Sqrt(1 + k * k); nyv = 1 / l; nnx = -k / l * -0 + 0; }
                m.AddVertex(ax, py[i] + y0, az, nnx, nyv, nnz, 0, run * 0.5f, r, g, b);
                m.AddVertex(bx, py[i] + y1, bz, nnx, nyv, nnz, 1, run * 0.5f, r, g, b);
            }
            for (int i = 0; i < n - 1; i++)
            {
                int a = first + i * 2, b2 = a + 1, c = a + 2, d = a + 3;
                if (up) { m.AddTriangle(sub, a, c, b2); m.AddTriangle(sub, b2, c, d); }          // left strip seen from above: orientation fixed below
                else { m.AddTriangle(sub, a, b2, c); m.AddTriangle(sub, b2, d, c); }
            }
            // make sure all triangles of this strip face the desired side
            FixStrip(m, sub, first, up, side);
        }

        static void FixStrip(MeshData m, int sub, int firstVertex, bool up, int side)
        {
            var idx = m.Indices[sub];
            for (int i = 0; i + 2 < idx.Count; i += 3)
            {
                if (idx[i] < firstVertex) continue;
                int a = idx[i], b = idx[i + 1], c = idx[i + 2];
                float ex = m.Positions[b * 3] - m.Positions[a * 3], ey = m.Positions[b * 3 + 1] - m.Positions[a * 3 + 1], ez = m.Positions[b * 3 + 2] - m.Positions[a * 3 + 2];
                float fx = m.Positions[c * 3] - m.Positions[a * 3], fy = m.Positions[c * 3 + 1] - m.Positions[a * 3 + 1], fz = m.Positions[c * 3 + 2] - m.Positions[a * 3 + 2];
                float ny = ez * fx - ex * fz, nxx = ey * fz - ez * fy, nzz = ex * fy - ey * fx;
                bool ok = up ? ny > 0 : (nxx * m.Normals[a * 3] + nzz * m.Normals[a * 3 + 2]) > 0;
                if (!ok) { idx[i + 1] = c; idx[i + 2] = b; }
            }
        }
    }
}
