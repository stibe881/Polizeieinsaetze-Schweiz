using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Terrain;

namespace SwissPoliceSim.Core.Meshing
{
    /// <summary>
    /// Road surface, kerbs/sidewalks and lane markings (geometry, not textures) for one tile.
    /// Submeshes: 0 motorway asphalt, 1 main road asphalt, 2 local asphalt, 3 sidewalk/kerb, 4 white markings, 5 yellow/other markings.
    /// </summary>
    public static class RoadMesher
    {
        public const int Submeshes = 6;
        const float Step = 6f, AsphaltLift = 0.10f, MarkLift = 0.025f, KerbHeight = 0.13f;

        public static MeshData NewMesh() => new MeshData(Submeshes);

        public static int AsphaltSub(RoadClass c)
        {
            switch (c)
            {
                case RoadClass.Motorway: case RoadClass.MotorwayLink: case RoadClass.Trunk: case RoadClass.TrunkLink: return 0;
                case RoadClass.Primary: case RoadClass.PrimaryLink: case RoadClass.Secondary: case RoadClass.SecondaryLink: case RoadClass.Tertiary: case RoadClass.TertiaryLink: return 1;
                default: return 2;
            }
        }

        struct Pt { public float X, Y, Z, Dx, Dz, Dist; }

        /// <summary>Appends one way. <paramref name="ground"/> returns terrain height at tile-local (x,z).</summary>
        public static void AddWay(MeshData m, RoadWay w, double tileE, double tileN, Func<double, double, float> ground, int wayIndex)
        {
            if (w.PointCount < 2 || w.Class == RoadClass.Track && w.CarriageLanes() < 1) return;
            var pts = Sample(w, tileE, tileN, ground);
            if (pts.Count < 2) return;
            if (!w.IsVehicular) { PedestrianWay(m, w, pts, wayIndex); return; }

            float cw = w.CarriagewayWidth(), shoulder = w.ShoulderWidth();
            float lift = AsphaltLift + (wayIndex % 8) * 0.004f + (3 - AsphaltSub(w.Class)) * 0.012f;
            bool oneway = w.OneWay != OneWay.No;
            // oneway motorway: shoulder on the right side only (right = -normal when normal points left)
            float leftEdge = cw * 0.5f, rightEdge = -(cw * 0.5f + shoulder);
            if (w.OneWay == OneWay.Reverse) { /* geometry runs against traffic: shoulder is on the other side */ leftEdge = cw * 0.5f + shoulder; rightEdge = -cw * 0.5f; }
            if (!oneway) { leftEdge = cw * 0.5f + shoulder * 0.5f; rightEdge = -leftEdge; }

            int sub = AsphaltSub(w.Class);
            byte shade = (byte)(sub == 0 ? 78 : sub == 1 ? 92 : 108);
            Ribbon(m, sub, pts, leftEdge, rightEdge, lift, shade, shade, shade, 0.12f);

            bool urban = w.SpeedBasis == SpeedSource.LegalUrban || w.SpeedBasis == SpeedSource.Zone || (w.SpeedBasis == SpeedSource.Tagged && w.SpeedKmh <= 50);
            bool wantsSidewalk = urban && w.Class >= RoadClass.Primary && w.Class != RoadClass.Service && w.Class != RoadClass.Track
                                 && !w.Tunnel && w.Class != RoadClass.PrimaryLink && w.Class != RoadClass.SecondaryLink && w.Class != RoadClass.TertiaryLink
                                 && w.Class != RoadClass.MotorwayLink && w.Class != RoadClass.TrunkLink;
            if (wantsSidewalk && cw > 4.2f)
            {
                float sw = 1.9f;
                SidewalkStrip(m, pts, leftEdge, leftEdge + sw, lift, +1);
                SidewalkStrip(m, pts, rightEdge - sw, rightEdge, lift, -1);
            }

            if (w.Tunnel) return;
            AddMarkings(m, w, pts, cw, shoulder, oneway, lift);
        }

        static void PedestrianWay(MeshData m, RoadWay w, List<Pt> pts, int wayIndex)
        {
            float half = w.CarriagewayWidth() * 0.5f, lift = 0.07f + (wayIndex % 8) * 0.003f;
            byte r = 176, g = 172, b = 164;
            if (w.Class == RoadClass.Path) { r = 150; g = 128; b = 96; }
            else if (w.Class == RoadClass.Steps) { r = 140; g = 138; b = 132; }
            else if (w.Class == RoadClass.Cycleway) { r = 120; g = 124; b = 128; }
            Ribbon(m, 3, pts, half, -half, lift, r, g, b, 0.25f);
        }

        static List<Pt> Sample(RoadWay w, double te, double tn, Func<double, double, float> ground)
        {
            var res = new List<Pt>(); bool hasH = w.H.Length == w.PointCount; double dist = 0;
            for (int i = 0; i < w.PointCount - 1; i++)
            {
                float x0 = (float)(w.E[i] - te), z0 = (float)(w.N[i] - tn), x1 = (float)(w.E[i + 1] - te), z1 = (float)(w.N[i + 1] - tn);
                float len = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
                if (len < 0.01f) continue;
                int steps = Math.Max(1, (int)Math.Ceiling(len / Step));
                for (int s = (res.Count == 0 ? 0 : 1); s <= steps; s++)
                {
                    float t = s / (float)steps;
                    float x = x0 + (x1 - x0) * t, z = z0 + (z1 - z0) * t, y;
                    bool useBaked = (w.Bridge || w.Tunnel) && hasH && !double.IsNaN(w.H[i]) && !double.IsNaN(w.H[i + 1]);
                    y = useBaked ? (float)(w.H[i] + (w.H[i + 1] - w.H[i]) * t) : ground(x, z);
                    if (res.Count > 0) dist += Math.Sqrt(Math.Pow(x - res[res.Count - 1].X, 2) + Math.Pow(z - res[res.Count - 1].Z, 2));
                    res.Add(new Pt { X = x, Y = y, Z = z, Dist = (float)dist });
                }
            }
            // tangents (smoothed)
            for (int i = 0; i < res.Count; i++)
            {
                var a = res[Math.Max(0, i - 1)]; var b = res[Math.Min(res.Count - 1, i + 1)];
                float dx = b.X - a.X, dz = b.Z - a.Z, l = (float)Math.Sqrt(dx * dx + dz * dz);
                if (l < 1e-5f) { dx = 1; dz = 0; l = 1; }
                var p = res[i]; p.Dx = dx / l; p.Dz = dz / l; res[i] = p;
            }
            return res;
        }

        // offset > 0 is to the LEFT of travel direction (normal = (-dz, dx))
        static void Ribbon(MeshData m, int sub, List<Pt> pts, float left, float right, float lift, byte r, byte g, byte b, float vScale)
        {
            int bi = m.VertexCount;
            foreach (var p in pts)
            {
                float nx = -p.Dz, nz = p.Dx;
                m.AddVertex(p.X + nx * left, p.Y + lift, p.Z + nz * left, 0, 1, 0, (p.X + nx * left) * 0.12f, (p.Z + nz * left) * 0.12f, r, g, b);
                m.AddVertex(p.X + nx * right, p.Y + lift, p.Z + nz * right, 0, 1, 0, (p.X + nx * right) * 0.12f, (p.Z + nz * right) * 0.12f, r, g, b);
            }
            for (int i = 0; i < pts.Count - 1; i++)
            {
                int a = bi + i * 2;
                m.AddTriangle(sub, a, a + 2, a + 1); m.AddTriangle(sub, a + 1, a + 2, a + 3);
            }
        }

        static void SidewalkStrip(MeshData m, List<Pt> pts, float inner, float outer, float lift, int side)
        {
            float top = lift + KerbHeight;
            int bi = m.VertexCount;
            // 4 vertices per sample: kerb bottom, kerb top (both with a normal pointing at the road), walkway road-side edge, walkway far edge (normal up)
            foreach (var p in pts)
            {
                float nx = -p.Dz, nz = p.Dx;
                float roadSide = side > 0 ? inner : outer, farSide = side > 0 ? outer : inner;
                float kx = -side * nx, kz = -side * nz;       // horizontal normal of the kerb face: towards the road
                float ux = (p.X + nx * roadSide) * 0.25f, uz = (p.Z + nz * roadSide) * 0.25f;
                m.AddVertex(p.X + nx * roadSide, p.Y + lift, p.Z + nz * roadSide, kx, 0, kz, ux, uz, 150, 148, 142);
                m.AddVertex(p.X + nx * roadSide, p.Y + top, p.Z + nz * roadSide, kx, 0, kz, ux, uz, 175, 173, 166);
                m.AddVertex(p.X + nx * roadSide, p.Y + top, p.Z + nz * roadSide, 0, 1, 0, ux, uz, 190, 188, 180);
                m.AddVertex(p.X + nx * farSide, p.Y + top, p.Z + nz * farSide, 0, 1, 0, (p.X + nx * farSide) * 0.25f, (p.Z + nz * farSide) * 0.25f, 190, 188, 180);
            }
            for (int i = 0; i < pts.Count - 1; i++)
            {
                int a = bi + i * 4, b = a + 4;
                m.AddTriangle(3, a + 2, b + 2, a + 3); m.AddTriangle(3, a + 3, b + 2, b + 3);      // walkway top (up-facing)
                if (side > 0) { m.AddTriangle(3, a, a + 1, b); m.AddTriangle(3, a + 1, b + 1, b); }  // kerb face
                else { m.AddTriangle(3, a, b, a + 1); m.AddTriangle(3, a + 1, b, b + 1); }
            }
        }

        static void AddMarkings(MeshData m, RoadWay w, List<Pt> pts, float cw, float shoulder, bool oneway, float lift)
        {
            if (w.Class == RoadClass.Service || w.Class == RoadClass.LivingStreet || w.Class == RoadClass.Track) return;
            if (cw < 4.6f) return;
            float y = lift + MarkLift;
            int lanes = w.CarriageLanes();
            bool motorway = w.Class == RoadClass.Motorway || w.Class == RoadClass.Trunk;

            // edge lines (Randlinie): continuous, 0.15 m (motorway 0.2) just inside the carriageway edge
            float lw = motorway ? 0.20f : (w.SpeedKmh >= 70 ? 0.15f : 0.12f);
            if (w.Class != RoadClass.Residential && w.Class != RoadClass.Unclassified || w.SpeedKmh >= 60)
            {
                float rightEdge = oneway && w.OneWay == OneWay.Forward ? -cw * 0.5f : -cw * 0.5f;
                float leftEdge = cw * 0.5f;
                Strip(m, 4, pts, leftEdge - 0.25f, lw, y, 0, 0, 0, 255, 255, 255);
                Strip(m, 4, pts, rightEdge + 0.25f, lw, y, 0, 0, 0, 255, 255, 255);
            }

            if (!oneway && lanes == 2 && cw >= 5.4f && w.Class != RoadClass.Residential)
            {
                // centre line: dashed (Leitlinie 3 m / 6 m) on roads with limit >= 50
                Strip(m, 4, pts, 0, 0.12f, y, 3.0f, 6.0f, 0, 255, 255, 255);
            }
            else if (!oneway && lanes >= 4)
            {
                Strip(m, 4, pts, 0, 0.14f, y, 0, 0, 0, 255, 255, 255);        // central divider: solid
                for (int l = 1; l < lanes / 2; l++)
                {
                    float off = l * w.LaneWidth();
                    Strip(m, 4, pts, off, 0.12f, y, 3.0f, 9.0f, 0, 255, 255, 255);
                    Strip(m, 4, pts, -off, 0.12f, y, 3.0f, 9.0f, 0, 255, 255, 255);
                }
            }
            else if (oneway && lanes >= 2)
            {
                // lane dividers dashed; for oneway the carriageway spans [-cw/2 - shoulder.. +cw/2] on the traffic side; keep simple: symmetric
                for (int l = 1; l < lanes; l++)
                {
                    float off = -cw * 0.5f + l * w.LaneWidth();
                    Strip(m, 4, pts, off, 0.12f, y, 3.0f, 9.0f, 0, 255, 255, 255);
                }
            }
        }

        /// <summary>Thin strip at lateral offset (positive = left). dash/gap = 0 → continuous.</summary>
        static void Strip(MeshData m, int sub, List<Pt> pts, float offset, float width, float lift, float dash, float gap, float phase, byte r, byte g, byte b)
        {
            float hw = width * 0.5f;
            if (dash <= 0)
            {
                int bi = m.VertexCount;
                foreach (var p in pts)
                {
                    float nx = -p.Dz, nz = p.Dx;
                    m.AddVertex(p.X + nx * (offset + hw), p.Y + lift, p.Z + nz * (offset + hw), 0, 1, 0, 0, p.Dist, r, g, b);
                    m.AddVertex(p.X + nx * (offset - hw), p.Y + lift, p.Z + nz * (offset - hw), 0, 1, 0, 1, p.Dist, r, g, b);
                }
                for (int i = 0; i < pts.Count - 1; i++) { int a = bi + i * 2; m.AddTriangle(sub, a, a + 2, a + 1); m.AddTriangle(sub, a + 1, a + 2, a + 3); }
                return;
            }
            float cycle = dash + gap, total = pts[pts.Count - 1].Dist;
            for (float s = phase; s < total; s += cycle)
            {
                float s1 = Math.Min(total, s + dash);
                if (s1 - s < 0.5f) break;
                AddDash(m, sub, pts, offset, hw, lift, s, s1, r, g, b);
            }
        }

        static void AddDash(MeshData m, int sub, List<Pt> pts, float offset, float hw, float lift, float s0, float s1, byte r, byte g, byte b)
        {
            int bi = m.VertexCount; int count = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                if (p.Dist < s0 - Step && i + 1 < pts.Count && pts[i + 1].Dist < s0) continue;
                if (p.Dist > s1 + Step) break;
                float d = Math.Max(s0, Math.Min(s1, p.Dist));
                // interpolate on the segment to hit s0 / s1 exactly
                float x = p.X, y = p.Y, z = p.Z, dx = p.Dx, dz = p.Dz;
                if (p.Dist < s0 && i + 1 < pts.Count) { var q = pts[i + 1]; float t = (s0 - p.Dist) / Math.Max(1e-5f, q.Dist - p.Dist); x += (q.X - p.X) * t; y += (q.Y - p.Y) * t; z += (q.Z - p.Z) * t; dx = q.Dx; dz = q.Dz; }
                else if (p.Dist > s1 && i > 0) { var q = pts[i - 1]; float t = (s1 - q.Dist) / Math.Max(1e-5f, p.Dist - q.Dist); x = q.X + (p.X - q.X) * t; y = q.Y + (p.Y - q.Y) * t; z = q.Z + (p.Z - q.Z) * t; dx = q.Dx; dz = q.Dz; }
                float nx = -dz, nz = dx;
                m.AddVertex(x + nx * (offset + hw), y + lift, z + nz * (offset + hw), 0, 1, 0, 0, d, r, g, b);
                m.AddVertex(x + nx * (offset - hw), y + lift, z + nz * (offset - hw), 0, 1, 0, 1, d, r, g, b);
                count++;
            }
            for (int i = 0; i < count - 1; i++) { int a = bi + i * 2; m.AddTriangle(sub, a, a + 2, a + 1); m.AddTriangle(sub, a + 1, a + 2, a + 3); }
        }

        /// <summary>Zebra crossing (Fussgängerstreifen): white bars across the carriageway at a point on a road.</summary>
        public static void AddCrossing(MeshData m, double e, double n, double tileE, double tileN, RoadWay way, Func<double, double, float> ground)
        {
            // find nearest segment of the way
            int best = -1; double bd = double.MaxValue; double bt = 0;
            for (int i = 0; i < way.PointCount - 1; i++)
            {
                double ax = way.E[i], ay = way.N[i], bx = way.E[i + 1], by = way.N[i + 1], dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
                if (l2 < 1e-9) continue;
                double t = Math.Max(0, Math.Min(1, ((e - ax) * dx + (n - ay) * dy) / l2));
                double px = ax + dx * t, py = ay + dy * t, d = (px - e) * (px - e) + (py - n) * (py - n);
                if (d < bd) { bd = d; best = i; bt = t; }
            }
            if (best < 0 || bd > 25) return;
            double sx = way.E[best + 1] - way.E[best], sy = way.N[best + 1] - way.N[best], sl = Math.Sqrt(sx * sx + sy * sy);
            float tx = (float)(sx / sl), tz = (float)(sy / sl), nx = -tz, nz = tx;
            float cx = (float)(way.E[best] + sx * bt - tileE), cz = (float)(way.N[best] + sy * bt - tileN);
            float halfRoad = way.CarriagewayWidth() * 0.5f;
            int bars = (int)(way.CarriagewayWidth() / 0.9f);
            float y0 = ground(cx, cz) + AsphaltLift + 0.03f;
            for (int k = 0; k < bars; k++)
            {
                float off = -halfRoad + 0.45f + k * 0.9f;
                float bx = cx + nx * off, bz = cz + nz * off, hw = 0.25f, hl = 1.6f;
                int a = m.VertexCount;
                m.AddVertex(bx - tx * hl + nx * hw, y0, bz - tz * hl + nz * hw, 0, 1, 0, 0, 0, 255, 255, 255);
                m.AddVertex(bx + tx * hl + nx * hw, y0, bz + tz * hl + nz * hw, 0, 1, 0, 0, 1, 255, 255, 255);
                m.AddVertex(bx + tx * hl - nx * hw, y0, bz + tz * hl - nz * hw, 0, 1, 0, 1, 1, 255, 255, 255);
                m.AddVertex(bx - tx * hl - nx * hw, y0, bz - tz * hl - nz * hw, 0, 1, 0, 1, 0, 255, 255, 255);
                m.AddTriangle(4, a, a + 1, a + 2); m.AddTriangle(4, a, a + 2, a + 3);
            }
        }
    }
}
