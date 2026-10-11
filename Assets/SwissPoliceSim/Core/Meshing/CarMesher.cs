using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Meshing
{
    public enum BodyKind { Wagon, Hatchback, Sedan, Suv, Van }

    public struct CarLivery
    {
        public byte[] Body;          // paint rgb
        public byte[] Stripe;        // belt stripe rgb (null = none)
        public bool LightBar;        // blue roof light bar + blue grille/rear LEDs
        public bool PoliceDecals;    // POLIZEI lettering
        public string Plate;         // text on the plates
        public static CarLivery PatrolCar() => new CarLivery { Body = new byte[] { 242, 244, 246 }, Stripe = new byte[] { 255, 112, 14 }, LightBar = true, PoliceDecals = true, Plate = "BE 123456" };
        public static CarLivery Civilian(byte r, byte g, byte b, string plate) => new CarLivery { Body = new[] { r, g, b }, Plate = plate };
    }

    /// <summary>Output of the car generator. Wheels are separate meshes centred on their axle so they can spin and steer.</summary>
    public sealed class CarModel
    {
        public MeshData Body;           // submeshes: see CarMesher.Sub*
        public MeshData Wheel;          // 2 submeshes: 0 tyre, 1 rim + brake (one wheel, axis = x, centred; mirror for the left side via yaw 180°)
        public float[][] WheelCentres;  // FL, FR, RL, RR local positions (x right, y up, z forward)
        public float WheelRadius, WheelWidth;
        public float Length, Width, Height;
        public float[] DriverEye;       // cockpit camera anchor
        public float[] HoodCam;
        public float[][] IndicatorsLeft, IndicatorsRight; // positions for light sources
        public float[][] HeadlightPos, BlueLightPos;
    }

    public static class CarMesher
    {
        public const int SubPaint = 0, SubGlass = 1, SubTrim = 2, SubLampFront = 3, SubLampRear = 4, SubIndicator = 5, SubBlueL = 6, SubBlueR = 7,
                         SubPlate = 8, SubDecal = 9, SubInterior = 10, SubChrome = 11, Submeshes = 12;

        sealed class Profile
        {
            public float[][] Top, Half;      // (z, value) key points
            public float Belt, WindShieldBase, RoofFront, RoofRear, RearGlassEnd, Clearance, Length, WheelBase, FrontAxle, Track, WheelR;
            public float[] SideWin0, SideWin1, SideWin2;   // z-ranges (hi, lo) of side windows
            public float GreenhouseNarrow;
        }

        static Profile For(BodyKind k)
        {
            var p = ForRaw(k);
            // plan-view taper: the nose and tail are narrower than the body so the corners read as rounded
            int n = p.Half.Length;
            p.Half[n - 1][1] *= 0.90f; p.Half[0][1] *= 0.90f;
            return p;
        }

        static float Smooth(float t) { t = t < 0 ? 0 : (t > 1 ? 1 : t); return t * t * (3 - 2 * t); }

        static Profile ForRaw(BodyKind k)
        {
            switch (k)
            {
                case BodyKind.Hatchback: return new Profile
                {
                    Length = 4.05f, WheelBase = 2.55f, FrontAxle = 1.28f, Track = 1.52f, WheelR = 0.31f, Clearance = 0.15f, Belt = 0.95f, GreenhouseNarrow = 0.80f,
                    Top = new[] { P(-2.03f, 0.95f), P(-1.95f, 1.12f), P(-1.80f, 1.40f), P(-1.5f, 1.46f), P(-0.4f, 1.47f), P(0.0f, 1.44f), P(0.78f, 0.97f), P(1.1f, 0.94f), P(1.7f, 0.90f), P(1.95f, 0.84f), P(2.03f, 0.72f) },
                    Half = new[] { P(-2.03f, 0.80f), P(-1.85f, 0.86f), P(-1.5f, 0.89f), P(1.4f, 0.89f), P(1.8f, 0.86f), P(2.03f, 0.76f) },
                    SideWin0 = new[] { 0.60f, -0.28f }, SideWin1 = new[] { -0.40f, -1.20f }, SideWin2 = new[] { -1.30f, -1.62f }, RoofFront = 0.0f, WindShieldBase = 0.78f, RoofRear = -1.6f, RearGlassEnd = -1.95f
                };
                case BodyKind.Sedan: return new Profile
                {
                    Length = 4.75f, WheelBase = 2.80f, FrontAxle = 1.45f, Track = 1.55f, WheelR = 0.33f, Clearance = 0.15f, Belt = 0.98f, GreenhouseNarrow = 0.80f,
                    Top = new[] { P(-2.375f, 0.92f), P(-2.2f, 1.0f), P(-1.9f, 1.02f), P(-1.5f, 1.12f), P(-1.1f, 1.40f), P(-0.7f, 1.455f), P(-0.0f, 1.46f), P(0.3f, 1.42f), P(0.95f, 1.0f), P(1.3f, 0.99f), P(2.0f, 0.93f), P(2.3f, 0.86f), P(2.375f, 0.74f) },
                    Half = new[] { P(-2.375f, 0.82f), P(-2.2f, 0.90f), P(-1.9f, 0.92f), P(1.7f, 0.92f), P(2.1f, 0.88f), P(2.375f, 0.78f) },
                    SideWin0 = new[] { 0.62f, -0.25f }, SideWin1 = new[] { -0.38f, -1.15f }, SideWin2 = new[] { -1.22f, -1.4f }, RoofFront = 0.0f, WindShieldBase = 0.95f, RoofRear = -0.75f, RearGlassEnd = -1.5f
                };
                case BodyKind.Suv: return new Profile
                {
                    Length = 4.65f, WheelBase = 2.78f, FrontAxle = 1.42f, Track = 1.62f, WheelR = 0.38f, Clearance = 0.21f, Belt = 1.12f, GreenhouseNarrow = 0.84f,
                    Top = new[] { P(-2.32f, 1.12f), P(-2.25f, 1.45f), P(-2.1f, 1.72f), P(-1.5f, 1.77f), P(-0.3f, 1.78f), P(0.15f, 1.74f), P(0.95f, 1.18f), P(1.3f, 1.14f), P(2.0f, 1.07f), P(2.32f, 0.90f) },
                    Half = new[] { P(-2.32f, 0.88f), P(-2.2f, 0.94f), P(-1.9f, 0.96f), P(1.7f, 0.96f), P(2.1f, 0.92f), P(2.32f, 0.84f) },
                    SideWin0 = new[] { 0.72f, -0.30f }, SideWin1 = new[] { -0.42f, -1.20f }, SideWin2 = new[] { -1.28f, -1.95f }, RoofFront = 0.15f, WindShieldBase = 0.95f, RoofRear = -2.05f, RearGlassEnd = -2.2f
                };
                case BodyKind.Van: return new Profile
                {
                    Length = 5.2f, WheelBase = 3.2f, FrontAxle = 1.62f, Track = 1.70f, WheelR = 0.34f, Clearance = 0.20f, Belt = 1.25f, GreenhouseNarrow = 0.9f,
                    Top = new[] { P(-2.6f, 2.0f), P(-2.5f, 2.35f), P(-2.4f, 2.45f), P(0.6f, 2.45f), P(1.0f, 2.35f), P(1.5f, 1.55f), P(1.8f, 1.35f), P(2.3f, 1.12f), P(2.6f, 0.82f) },
                    Half = new[] { P(-2.6f, 0.98f), P(-2.3f, 1.0f), P(1.5f, 1.0f), P(2.2f, 0.96f), P(2.6f, 0.85f) },
                    SideWin0 = new[] { 1.05f, 0.25f }, SideWin1 = new[] { -9f, -9f }, SideWin2 = new[] { -9f, -9f }, RoofFront = 0.9f, WindShieldBase = 1.5f, RoofRear = -2.4f, RearGlassEnd = -2.4f
                };
                default: return new Profile   // Wagon (Octavia Combi / Passat Variant class) — the patrol car
                {
                    Length = 4.70f, WheelBase = 2.69f, FrontAxle = 1.40f, Track = 1.54f, WheelR = 0.33f, Clearance = 0.16f, Belt = 0.98f, GreenhouseNarrow = 0.82f,
                    Top = new[] { P(-2.35f, 0.98f), P(-2.30f, 1.00f), P(-2.24f, 1.06f), P(-2.08f, 1.38f), P(-1.95f, 1.455f), P(-1.0f, 1.47f), P(0.0f, 1.46f), P(0.25f, 1.42f), P(0.80f, 1.0f), P(1.1f, 1.0f), P(1.9f, 0.95f), P(2.25f, 0.88f), P(2.35f, 0.76f) },
                    Half = new[] { P(-2.35f, 0.84f), P(-2.2f, 0.90f), P(-1.9f, 0.915f), P(1.7f, 0.915f), P(2.1f, 0.88f), P(2.35f, 0.78f) },
                    SideWin0 = new[] { 0.66f, -0.22f }, SideWin1 = new[] { -0.36f, -1.40f }, SideWin2 = new[] { -1.52f, -1.98f }, RoofFront = 0.0f, WindShieldBase = 0.80f, RoofRear = -2.0f, RearGlassEnd = -2.26f
                };
            }
        }

        static float[] P(float a, float b) => new[] { a, b };

        static float Eval(float[][] k, float z)
        {
            // key points are sorted by z ascending in the arrays above for Half, but Top lists ascending too
            if (z <= k[0][0]) return k[0][1];
            for (int i = 0; i < k.Length - 1; i++)
                if (z <= k[i + 1][0])
                {
                    float t = (z - k[i][0]) / Math.Max(1e-5f, k[i + 1][0] - k[i][0]);
                    t = t * t * (3 - 2 * t) * 0.65f + t * 0.35f;     // soft shoulders, still monotone
                    return k[i][1] + (k[i + 1][1] - k[i][1]) * t;
                }
            return k[k.Length - 1][1];
        }

        struct RP { public float X, Y; public byte R, G, B; public byte Kind; }  // Kind: 0 paint, 1 trim, 2 floor

        public static CarModel Build(BodyKind kind, CarLivery liv)
        {
            var pr = For(kind);
            var model = new CarModel { Body = new MeshData(Submeshes), Wheel = new MeshData(2), WheelRadius = pr.WheelR, WheelWidth = 0.215f, Length = pr.Length };
            var m = model.Body;
            float zFront = pr.Top[pr.Top.Length - 1][0], zRear = pr.Top[0][0];
            float maxW = 0; foreach (var h in pr.Half) maxW = Math.Max(maxW, h[1]);
            model.Width = maxW * 2; float maxT = 0; foreach (var t in pr.Top) maxT = Math.Max(maxT, t[1]); model.Height = maxT;

            // wheel positions
            float fa = pr.FrontAxle, ra = pr.FrontAxle - pr.WheelBase, tx = pr.Track * 0.5f;
            model.WheelCentres = new[] { new[] { -tx, pr.WheelR, fa }, new[] { tx, pr.WheelR, fa }, new[] { -tx, pr.WheelR, ra }, new[] { tx, pr.WheelR, ra } };
            float archR = pr.WheelR + 0.075f;

            // ---------------- stations
            var zs = new SortedSet<float>();
            for (float z = zRear; z <= zFront + 1e-4f; z += 0.1f) zs.Add((float)Math.Round(z, 3));
            foreach (var w in new[] { pr.SideWin0, pr.SideWin1, pr.SideWin2 }) { zs.Add(w[0]); zs.Add(w[1]); }
            for (float z = zRear + 0.04f; z < zRear + 0.5f; z += 0.04f) zs.Add((float)Math.Round(z, 3));
            foreach (var z in new[] { pr.RoofFront, pr.WindShieldBase, pr.RoofRear, pr.RearGlassEnd, zFront, zRear })
                if (z > zRear && z < zFront) zs.Add(z);
            foreach (var wz in new[] { fa, ra })
                for (float d = -archR; d <= archR + 1e-4f; d += archR / 4) { float z = wz + d; if (z > zRear && z < zFront) zs.Add((float)Math.Round(z, 3)); }
            var stations = new List<float>(zs);

            int firstBodyVertex = m.VertexCount;
            var rings = new List<int[]>();       // vertex indices per station
            var ringCount = 0;
            byte[] body = liv.Body ?? new byte[] { 240, 240, 240 };
            foreach (float z in stations)
            {
                var half = HalfRing(pr, z, fa, ra, archR, body, liv.Stripe, zFront, zRear);
                ringCount = half.Count * 2 - 2;     // full loop: right half (incl. both centre points) + mirrored left (excluding centre points)
                var idx = new int[ringCount + 1];
                int k = 0;
                for (int i = 0; i < half.Count; i++) idx[k++] = m.AddVertex(half[i].X, half[i].Y, z, 0, 0, 0, 0, 0, half[i].R, half[i].G, half[i].B);
                for (int i = half.Count - 2; i >= 1; i--) idx[k++] = m.AddVertex(-half[i].X, half[i].Y, z, 0, 0, 0, 0, 0, half[i].R, half[i].G, half[i].B);
                idx[ringCount] = idx[0];
                rings.Add(idx);
            }
            // triangles: classify by centroid
            var triSpecs = new List<(int a, int b, int c)>();
            for (int s = 0; s < rings.Count - 1; s++)
                for (int i = 0; i < ringCount; i++)
                {
                    int a = rings[s][i], b = rings[s][i + 1], c = rings[s + 1][i + 1], d = rings[s + 1][i];
                    // winding: ring runs bottom->right side->top->left (clockwise seen from the rear when z grows forward); outward normals required
                    triSpecs.Add((a, b, d)); triSpecs.Add((b, c, d));
                }
            foreach (var t in triSpecs)
            {
                int sub = Classify(m, t.a, t.b, t.c, pr, body);
                m.AddTriangle(sub, t.a, t.b, t.c);
            }
            m.RecalculateNormals(firstBodyVertex);
            MeshUtil.FixWinding(m);
            // end caps (fan from centre) are added after smoothing so their flat normals survive
            Cap(m, rings[rings.Count - 1], ringCount, true); Cap(m, rings[0], ringCount, false);

            Interior(m, kind, pr, model);
            Details(m, kind, pr, model, liv, fa, ra, tx, zFront, zRear);
            BuildWheel(model.Wheel, pr.WheelR);

            model.DriverEye = new[] { -0.36f, pr.Belt + 0.30f, pr.RoofFront - 0.35f };
            model.HoodCam = new[] { 0f, pr.Top[pr.Top.Length - 4][1] + 0.9f, pr.WindShieldBase + 0.2f };
            model.IndicatorsLeft = new[] { new[] { -0.74f, 0.74f, zFront - 0.05f }, new[] { -0.80f, 0.86f, zRear + 0.02f } };
            model.IndicatorsRight = new[] { new[] { 0.74f, 0.74f, zFront - 0.05f }, new[] { 0.80f, 0.86f, zRear + 0.02f } };
            model.HeadlightPos = new[] { new[] { -0.62f, 0.72f, zFront }, new[] { 0.62f, 0.72f, zFront } };
            model.BlueLightPos = new[] { new[] { -0.3f, maxT + 0.12f, pr.RoofFront - 0.5f }, new[] { 0.3f, maxT + 0.12f, pr.RoofFront - 0.5f } };
            return model;
        }

        static List<RP> HalfRing(Profile pr, float z, float fa, float ra, float archR, byte[] body, byte[] stripe, float zFront, float zRear)
        {
            float T = Eval(pr.Top, z), W = Eval(pr.Half, z);
            float yb = pr.Clearance + 0.24f * Smooth((z - (zFront - 0.42f)) / 0.42f) + 0.17f * Smooth(((zRear + 0.36f) - z) / 0.36f);   // bumper sweeps up towards the tips (approach / departure angle)
            // arch lift for the side bottom edge
            float lift = 0;
            foreach (var wz in new[] { fa, ra })
            {
                float dz = z - wz;
                if (Math.Abs(dz) < archR) lift = Math.Max(lift, pr.WheelR + (float)Math.Sqrt(archR * archR - dz * dz) - 0.02f);
            }
            float ySide = Math.Max(yb + 0.06f, lift);
            float belt = Math.Min(pr.Belt, T - 0.05f);
            bool greenhouse = T - belt > 0.12f;
            float wg = W * pr.GreenhouseNarrow;
            var white = body; var dark = new byte[] { 38, 38, 40 }; var floor = new byte[] { 24, 24, 26 };
            byte[] orange = stripe ?? body;
            var l = new List<RP>();
            void Add(float x, float y, byte[] c, byte kind = 0) { l.Add(new RP { X = x, Y = y, R = c[0], G = c[1], B = c[2], Kind = kind }); }

            Add(0, yb, floor, 2);
            Add(W * 0.78f, yb + 0.005f, floor, 2);
            Add(W * 0.93f, ySide, dark, 1);
            float yRock = Math.Min(Math.Max(ySide + 0.10f, 0.36f), T - 0.12f);
            Add(W * 0.985f, yRock, dark, 1);
            float yS0 = Math.Min(0.47f, T - 0.10f), yS1 = Math.Min(0.63f, T - 0.06f);
            if (yS0 < yRock + 0.02f) yS0 = yRock + 0.02f; if (yS1 < yS0 + 0.04f) yS1 = yS0 + 0.04f;
            Add(W * 0.995f, yRock, white);
            if (stripe != null) { Add(W * 1.0f, yS0, white); Add(W * 1.0f, yS0, orange); Add(W * 1.004f, yS1, orange); Add(W * 1.004f, yS1, white); }
            else { Add(W * 1.0f, yS0, white); Add(W * 1.004f, yS1, white); }
            float yTop = Math.Min(belt, T - 0.05f);
            if (yTop < yS1 + 0.02f) yTop = yS1 + 0.02f;
            Add(W * 1.0f, yTop, white);
            if (greenhouse)
            {
                float gy1 = yTop + 0.45f * (T - yTop);
                Add(Lerp(W, wg, 0.45f), gy1, white);
                Add(wg, T - 0.06f, white);
                Add(wg * 0.62f, T - 0.008f, white);
                Add(0, T, white);
            }
            else
            {
                Add(W * 0.975f, Math.Max(yTop + 0.01f, T - 0.045f), white);
                Add(W * 0.80f, Math.Max(yTop + 0.015f, T - 0.012f), white);
                Add(W * 0.42f, T, white);
                Add(0, T, white);
            }
            // wheel arch: every side point below the arch edge is tucked up/inwards onto the arch edge (dark wheel-house roof)
            if (lift > yb + 0.08f)
                for (int i = 0; i < l.Count; i++)
                {
                    var r = l[i];
                    if (r.Kind == 2 || r.Y >= lift) continue;
                    r.Y = lift; r.X = W * 0.93f; r.R = 28; r.G = 28; r.B = 30; r.Kind = 1; l[i] = r;
                }
            // bumpers: lower front/rear black
            float zf = zFront, zr = zRear;
            if (z > zf - 0.35f || z < zr + 0.35f)
                for (int i = 0; i < l.Count; i++)
                    if (l[i].Y < 0.52f && l[i].Kind == 0) { var r = l[i]; r.R = 30; r.G = 30; r.B = 32; l[i] = r; }
            // enforce monotone y
            for (int i = 1; i < l.Count; i++) { var r = l[i]; if (r.Y < l[i - 1].Y) { r.Y = l[i - 1].Y; l[i] = r; } }
            return l;
        }

        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        static int Classify(MeshData m, int a, int b, int c, Profile pr, byte[] body)
        {
            float cx = (m.Positions[a * 3] + m.Positions[b * 3] + m.Positions[c * 3]) / 3, cy = (m.Positions[a * 3 + 1] + m.Positions[b * 3 + 1] + m.Positions[c * 3 + 1]) / 3, cz = (m.Positions[a * 3 + 2] + m.Positions[b * 3 + 2] + m.Positions[c * 3 + 2]) / 3;
            // underside + rocker → trim
            if (m.Colors[a * 4] == 24 && m.Colors[b * 4] == 24 && m.Colors[c * 4] == 24) return SubTrim;
            if (m.Colors[a * 4] == 38 && m.Colors[b * 4] == 38 && m.Colors[c * 4] == 38) return SubTrim;
            if (m.Colors[a * 4] == 30 && m.Colors[b * 4] == 30 && m.Colors[c * 4] == 30) return SubTrim;
            // glass
            float yBelt = pr.Belt + 0.025f;
            if (cy > yBelt)
            {
                float ax = Math.Abs(cx);
                if (ax > 0.55f)
                {
                    if (In(cz, pr.SideWin0) || In(cz, pr.SideWin1) || In(cz, pr.SideWin2)) return SubGlass;
                }
                else
                {
                    if (cz > pr.RoofFront + 0.04f && cz < pr.WindShieldBase - 0.04f && cy < (pr.Belt > 1.2f ? 3.0f : 1.9f)) return SubGlass;      // windshield
                    if (cz < pr.RoofRear - 0.02f && cz > pr.RearGlassEnd) return SubGlass;                              // rear window
                }
                if (ax > 0.55f && ax <= (pr.Belt > 1.2f ? 0.92f : 0.75f) && cz > pr.RoofFront + 0.04f && cz < pr.WindShieldBase - 0.05f) return SubGlass;
            }
            return SubPaint;
        }

        static bool In(float z, float[] r) => z < r[0] && z > r[1];

        /// <summary>Closes an end of the loft with horizontal colour bands (paint / black bumper / stripe) instead of a gradient fan.</summary>
        static void Cap(MeshData m, int[] ring, int count, bool front)
        {
            int half = count / 2 + 1;      // number of points of the right half incl. both centre points
            float nz = front ? 1 : -1;
            int V(int v, byte[] col) => m.AddVertex(m.Positions[v * 3], m.Positions[v * 3 + 1], m.Positions[v * 3 + 2], 0, 0, nz, 0, 0, col[0], col[1], col[2]);
            for (int i = 0; i < half - 1; i++)
            {
                int r0 = ring[i], r1 = ring[i + 1];
                int l0 = i == 0 ? ring[0] : ring[count - i], l1 = (i + 1 == half - 1) ? ring[half - 1] : ring[count - (i + 1)];
                float dy = m.Positions[r1 * 3 + 1] - m.Positions[r0 * 3 + 1];
                if (dy < 1e-4f && Math.Abs(m.Positions[r1 * 3] - m.Positions[r0 * 3]) < 1e-4f) continue;
                var col = new[] { m.Colors[r0 * 4], m.Colors[r0 * 4 + 1], m.Colors[r0 * 4 + 2] };
                int a = V(l0, col), b = V(r0, col), c = V(r1, col), d = V(l1, col);
                m.AddTriangle(SubPaint, a, b, c); m.AddTriangle(SubPaint, a, c, d);
            }
        }

        // ------------------------------------------------------------------ interior
        static void Interior(MeshData m, BodyKind kind, Profile pr, CarModel model)
        {
            byte[] dark = { 40, 40, 44 }, seat = { 52, 54, 60 }, dash = { 30, 30, 34 };
            float yb = pr.Clearance + 0.12f, eyeZ = pr.RoofFront - 0.35f;
            // floor
            float fz0 = pr.Top[0][0] + 0.25f, fz1 = pr.WindShieldBase - 0.1f;
            MeshUtil.Box(m, SubInterior, 0, yb, (fz0 + fz1) / 2, 1.5f, 0.04f, fz1 - fz0, 30, 30, 34);
            // dashboard + centre console
            float dz = pr.WindShieldBase - 0.15f;
            MeshUtil.Box(m, SubInterior, 0, pr.Belt - 0.12f, dz, 1.56f, 0.24f, 0.36f, dash[0], dash[1], dash[2]);
            MeshUtil.Box(m, SubInterior, 0, yb + 0.20f, dz - 0.55f, 0.26f, 0.36f, 0.9f, 36, 36, 40);
            // instrument hood
            MeshUtil.Box(m, SubInterior, -0.36f, pr.Belt + 0.03f, dz - 0.05f, 0.40f, 0.12f, 0.22f, 22, 22, 24);
            // steering wheel
            SteeringWheel(m, -0.36f, pr.Belt - 0.04f, dz - 0.24f);
            // front seats + rear bench
            for (int s = -1; s <= 1; s += 2)
            {
                float sx = s * 0.36f, sz = eyeZ - 0.55f;
                MeshUtil.Box(m, SubInterior, sx, yb + 0.20f, sz, 0.50f, 0.14f, 0.52f, seat[0], seat[1], seat[2]);
                MeshUtil.Box(m, SubInterior, sx, yb + 0.55f, sz - 0.24f, 0.48f, 0.62f, 0.12f, seat[0], seat[1], seat[2]);
                MeshUtil.Box(m, SubInterior, sx, yb + 0.92f, sz - 0.27f, 0.30f, 0.16f, 0.08f, seat[0], seat[1], seat[2]);   // headrest
            }
            float rz = (pr.RoofRear + pr.SideWin1[1]) / 2 + 0.3f;
            if (kind != BodyKind.Van)
            {
                MeshUtil.Box(m, SubInterior, 0, yb + 0.20f, rz, 1.38f, 0.14f, 0.5f, seat[0], seat[1], seat[2]);
                MeshUtil.Box(m, SubInterior, 0, yb + 0.55f, rz - 0.24f, 1.38f, 0.6f, 0.12f, seat[0], seat[1], seat[2]);
            }
            // mirror
            MeshUtil.Box(m, SubInterior, 0, pr.Belt + 0.33f, pr.WindShieldBase - 0.55f, 0.22f, 0.07f, 0.04f, 30, 30, 32);
            // inner shell (so the cabin looks closed from inside): reversed, slightly shrunk copy of upper body triangles
            int baseCount = m.Indices[SubPaint].Count;
            foreach (int sub in new[] { SubPaint, SubGlass })
            {
                var src = m.Indices[sub]; var map = new Dictionary<int, int>();
                for (int i = 0; i + 2 < src.Count; i += 3)
                {
                    int a = src[i], b = src[i + 1], c = src[i + 2];
                    float y = (m.Positions[a * 3 + 1] + m.Positions[b * 3 + 1] + m.Positions[c * 3 + 1]) / 3;
                    if (y < pr.Belt - 0.25f) continue;
                    int na = Dup(m, map, a, 0.96f), nb = Dup(m, map, b, 0.96f), nc = Dup(m, map, c, 0.96f);
                    m.AddTriangle(SubInterior, na, nc, nb);
                }
            }
        }

        static int Dup(MeshData m, Dictionary<int, int> map, int v, float shrink)
        {
            if (map.TryGetValue(v, out int n)) return n;
            n = m.AddVertex(m.Positions[v * 3] * shrink, m.Positions[v * 3 + 1] - 0.03f, m.Positions[v * 3 + 2], -m.Normals[v * 3], -m.Normals[v * 3 + 1], -m.Normals[v * 3 + 2], 0, 0, 58, 60, 64);
            map[v] = n; return n;
        }

        static void SteeringWheel(MeshData m, float cx, float cy, float cz)
        {
            // torus lying tilted towards the driver
            int seg = 20, ring = 6; float R = 0.19f, r = 0.018f; float tilt = 0.55f;
            int first = m.VertexCount;
            for (int i = 0; i <= seg; i++)
                for (int j = 0; j <= ring; j++)
                {
                    float a = (float)(2 * Math.PI * i / seg), b = (float)(2 * Math.PI * j / ring);
                    float x = (R + r * (float)Math.Cos(b)) * (float)Math.Cos(a), yy = (R + r * (float)Math.Cos(b)) * (float)Math.Sin(a), zz = r * (float)Math.Sin(b);
                    float yr = yy * (float)Math.Cos(tilt) - zz * (float)Math.Sin(tilt), zr = yy * (float)Math.Sin(tilt) + zz * (float)Math.Cos(tilt);
                    m.AddVertex(cx + x, cy + yr, cz + zr, (float)Math.Cos(a), 0, 0, 0, 0, 24, 24, 26);
                }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < ring; j++)
                {
                    int a = first + i * (ring + 1) + j, bb = a + 1, c = a + ring + 1, d = c + 1;
                    m.AddTriangle(SubInterior, a, c, bb); m.AddTriangle(SubInterior, bb, c, d);
                    m.AddTriangle(SubInterior, a, bb, c); m.AddTriangle(SubInterior, bb, d, c);
                }
        }

        // ------------------------------------------------------------------ exterior details
        static void Details(MeshData m, BodyKind kind, Profile pr, CarModel model, CarLivery liv, float fa, float ra, float tx, float zf, float zr)
        {
            float halfF = Eval(pr.Half, zf - 0.02f), halfR = Eval(pr.Half, zr + 0.02f);
            // ---- front: grille, intake, headlamp clusters, fog lamps, plate
            float tipHalf = Eval(pr.Half, zf), innerHalf = Eval(pr.Half, zf - 0.32f);
            float taper = (float)Math.Atan2(innerHalf - tipHalf, 0.32f);
            MeshUtil.Box(m, SubTrim, 0, 0.665f, zf + 0.001f, 0.50f, 0.13f, 0.03f, 16, 16, 18);                  // upper grille
            MeshUtil.Box(m, SubChrome, 0, 0.735f, zf + 0.006f, 0.52f, 0.012f, 0.03f, 150, 152, 156);
            MeshUtil.Box(m, SubChrome, 0, 0.595f, zf + 0.006f, 0.52f, 0.010f, 0.03f, 150, 152, 156);
            for (int g = -2; g <= 2; g++) MeshUtil.Box(m, SubChrome, g * 0.10f, 0.665f, zf + 0.012f, 0.006f, 0.11f, 0.02f, 120, 122, 126);
            MeshUtil.Box(m, SubTrim, 0, 0.475f, zf + 0.001f, 0.84f, 0.11f, 0.03f, 12, 12, 14);                  // lower intake
            for (int g = 0; g < 4; g++) MeshUtil.Box(m, SubTrim, 0, 0.44f + g * 0.03f, zf + 0.010f, 0.80f, 0.008f, 0.03f, 34, 34, 38);
            for (int s = -1; s <= 1; s += 2)
            {
                // headlamp: flat part on the nose (beside the grille) + corner part following the tapered fender
                MeshUtil.Box(m, SubTrim, s * 0.46f, 0.700f, zf + 0.001f, 0.34f, 0.115f, 0.03f, 22, 22, 26);
                MeshUtil.Box(m, SubLampFront, s * 0.46f, 0.704f, zf + 0.009f, 0.30f, 0.085f, 0.02f, 238, 240, 248);
                MeshUtil.Box(m, SubLampFront, s * 0.46f, 0.648f, zf + 0.010f, 0.28f, 0.012f, 0.02f, 250, 252, 255);
                float x1 = tipHalf - 0.02f, z1 = zf, z2 = zf - 0.30f, x2 = Eval(pr.Half, z2) - 0.03f;
                float cx = s * (x1 + x2) * 0.5f, cz = (z1 + z2) * 0.5f, len = (float)Math.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1));
                float yaw = s * (float)Math.Atan2(-(z2 - z1), x2 - x1);
                float nx = (z2 - z1) * s, nz = -(x2 - x1);   // outward normal offset direction (approx.)
                MeshUtil.Box(m, SubTrim, cx, 0.700f, cz + 0.012f, len, 0.115f, 0.03f, 22, 22, 26, yaw);
                MeshUtil.Box(m, SubLampFront, cx, 0.704f, cz + 0.020f, len - 0.03f, 0.085f, 0.02f, 238, 240, 248, yaw);
                MeshUtil.Box(m, SubIndicator, s * (x2 + 0.02f), 0.70f, z2 - 0.02f, 0.05f, 0.06f, 0.10f, 255, 160, 20);
                MeshUtil.Box(m, SubTrim, s * 0.60f, 0.47f, zf + 0.001f, 0.18f, 0.08f, 0.03f, 18, 18, 20);                  // fog lamp surround
                MeshUtil.Box(m, SubLampFront, s * 0.60f, 0.47f, zf + 0.009f, 0.11f, 0.045f, 0.02f, 225, 228, 235);
            }
            Plate(m, 0, 0.545f, zf + 0.026f, 0.30f, 0.08f, true, liv.Plate ?? "BE 000000");
            // ---- rear: tail lights, plate, bumper strip
            for (int s = -1; s <= 1; s += 2)
            {
                MeshUtil.Box(m, SubLampRear, s * 0.70f, 0.90f, zr + 0.03f, 0.30f, 0.20f, 0.06f, 190, 20, 24);
                MeshUtil.Box(m, SubIndicator, s * 0.80f, 0.74f, zr + 0.04f, 0.14f, 0.07f, 0.06f, 255, 160, 20);
            }
            MeshUtil.Box(m, SubTrim, 0, 0.52f, zr + 0.02f, 1.2f, 0.07f, 0.04f, 22, 22, 24);
            Plate(m, 0, 0.88f, zr + 0.015f, 0.30f, 0.08f, false, liv.Plate ?? "BE 000000");
            // ---- mirrors
            float mz = pr.WindShieldBase - 0.12f;
            for (int s = -1; s <= 1; s += 2)
            {
                float mx = s * (Eval(pr.Half, mz) + 0.10f);
                MeshUtil.Box(m, SubTrim, s * (Eval(pr.Half, mz) + 0.02f), pr.Belt + 0.01f, mz, 0.08f, 0.03f, 0.08f, 24, 24, 26);
                MeshUtil.Box(m, SubPaint, mx, pr.Belt + 0.07f, mz - 0.03f, 0.13f, 0.11f, 0.20f, liv.Body[0], liv.Body[1], liv.Body[2], s * 0.12f);
                MeshUtil.Box(m, SubIndicator, mx + s * 0.001f, pr.Belt + 0.07f, mz + 0.07f, 0.12f, 0.03f, 0.02f, 255, 160, 20);
            }
            // ---- door handles & seams
            foreach (float dz in new[] { 0.1f, -0.75f })
                for (int s = -1; s <= 1; s += 2)
                    MeshUtil.Box(m, SubChrome, s * (Eval(pr.Half, dz) + 0.012f), pr.Belt - 0.06f, dz, 0.02f, 0.025f, 0.16f, 110, 112, 116);
            // ---- wheel liners (dark discs behind the tyres)
            foreach (var wc in model.WheelCentres)
            {
                float side = Math.Sign(wc[0]);
                float xi = wc[0] - side * 0.11f;
                MeshUtil.Box(m, SubTrim, xi, wc[1] + 0.12f, wc[2], 0.02f, 0.62f, 0.84f, 20, 20, 22);
            }
            // ---- wipers, roof antenna
            MeshUtil.Cylinder(m, SubTrim, 0.15f, pr.Top[4][1], pr.RoofRear + 0.3f, 0.008f, 0.004f, 0.22f, 5, 24, 24, 26);

            // ---- light bar, flashers, decals
            if (liv.LightBar)
            {
                float roofY = pr.Top[4][1];
                float bz = pr.RoofFront - 0.55f;
                MeshUtil.Box(m, SubTrim, 0, roofY + 0.015f, bz, 1.18f, 0.03f, 0.30f, 20, 20, 24);
                MeshUtil.Box(m, SubBlueL, -0.30f, roofY + 0.075f, bz, 0.54f, 0.09f, 0.26f, 20, 70, 255);
                MeshUtil.Box(m, SubBlueR, 0.30f, roofY + 0.075f, bz, 0.54f, 0.09f, 0.26f, 20, 70, 255);
                MeshUtil.Box(m, SubTrim, 0, roofY + 0.125f, bz, 1.16f, 0.01f, 0.28f, 18, 18, 22);
                // front grille flashers, rear window flashers
                for (int s = -1; s <= 1; s += 2)
                {
                    MeshUtil.Box(m, s < 0 ? SubBlueL : SubBlueR, s * 0.30f, 0.615f, zf + 0.02f, 0.14f, 0.025f, 0.02f, 20, 70, 255);
                    MeshUtil.Box(m, s < 0 ? SubBlueL : SubBlueR, s * 0.50f, pr.Top[0][1] - 0.02f, zr - 0.005f, 0.22f, 0.03f, 0.02f, 20, 70, 255);
                }
            }
            if (liv.PoliceDecals) Decals(m, pr, liv, zf, zr);
        }

        static void Plate(MeshData m, float cx, float cy, float z, float w, float h, bool front, string text)
        {
            float nz = front ? 1 : -1;
            int a = m.AddVertex(cx - w / 2, cy - h / 2, z, 0, 0, nz, front ? 0 : 1, 0, 255, 255, 255), b = m.AddVertex(cx + w / 2, cy - h / 2, z, 0, 0, nz, front ? 1 : 0, 0, 255, 255, 255);
            int c = m.AddVertex(cx + w / 2, cy + h / 2, z, 0, 0, nz, front ? 1 : 0, 1, 255, 255, 255), d = m.AddVertex(cx - w / 2, cy + h / 2, z, 0, 0, nz, front ? 0 : 1, 1, 255, 255, 255);
            if (front) { m.AddTriangle(SubPlate, a, c, b); m.AddTriangle(SubPlate, a, d, c); } else { m.AddTriangle(SubPlate, a, b, c); m.AddTriangle(SubPlate, a, c, d); }
        }

        /// <summary>UV rectangles inside DecalAtlas: POLIZEI text occupies (0..1, 0.5..1), plate (0..0.5, 0..0.5) etc.</summary>
        static void Decals(MeshData m, Profile pr, CarLivery liv, float zf, float zr)
        {
            // POLIZEI on both doors (above the stripe), reads from front to back
            float z0 = pr.SideWin0[0] - 0.05f, z1 = -0.95f, y0 = pr.Belt - 0.31f, y1 = pr.Belt - 0.08f;
            for (int s = -1; s <= 1; s += 2)
            {
                float x0 = s * (Eval(pr.Half, z0) * 1.006f + 0.004f), x1 = s * (Eval(pr.Half, z1) * 1.006f + 0.004f);
                int a = m.AddVertex(x0, y0, z0, s, 0, 0, s > 0 ? 0 : 1, 0.5f, 255, 255, 255), b = m.AddVertex(x1, y0, z1, s, 0, 0, s > 0 ? 1 : 0, 0.5f, 255, 255, 255);
                int c = m.AddVertex(x1, y1, z1, s, 0, 0, s > 0 ? 1 : 0, 1, 255, 255, 255), d = m.AddVertex(x0, y1, z0, s, 0, 0, s > 0 ? 0 : 1, 1, 255, 255, 255);
                if (s > 0) { m.AddTriangle(SubDecal, a, b, c); m.AddTriangle(SubDecal, a, c, d); } else { m.AddTriangle(SubDecal, a, c, b); m.AddTriangle(SubDecal, a, d, c); }
            }
            // POLIZEI on the tailgate (mirrored in the atlas row so it reads correctly from behind)
            float zz = zr - 0.004f, xr = 0.6f, yy0 = 0.58f, yy1 = 0.74f;
            int p = m.AddVertex(xr, yy0, zz, 0, 0, -1, 0, 0.5f, 255, 255, 255), q = m.AddVertex(-xr, yy0, zz, 0, 0, -1, 1, 0.5f, 255, 255, 255);
            int r = m.AddVertex(-xr, yy1, zz, 0, 0, -1, 1, 1, 255, 255, 255), t = m.AddVertex(xr, yy1, zz, 0, 0, -1, 0, 1, 255, 255, 255);
            m.AddTriangle(SubDecal, p, q, r); m.AddTriangle(SubDecal, p, r, t);
        }

        // ------------------------------------------------------------------ wheel
        static void BuildWheel(MeshData w, float R)
        {
            float half = 0.1075f, rimR = R - 0.115f;     // 225/45 R17-ish
            var tyre = new List<float[]>
            {
                new[] { -half * 0.98f, rimR + 0.012f }, new[] { -half, rimR + 0.05f }, new[] { -half * 1.02f, rimR + 0.09f }, new[] { -half * 0.98f, R - 0.045f },
                new[] { -half * 0.80f, R - 0.012f }, new[] { -half * 0.45f, R }, new[] { 0f, R + 0.002f }, new[] { half * 0.45f, R }, new[] { half * 0.80f, R - 0.012f },
                new[] { half * 0.98f, R - 0.045f }, new[] { half * 1.02f, rimR + 0.09f }, new[] { half, rimR + 0.05f }, new[] { half * 0.98f, rimR + 0.012f }
            };
            MeshUtil.LatheX(w, 0, 0, 0, 0, tyre, 36, 22, 22, 24);
            // rim: barrel + dished face
            var rim = new List<float[]>
            {
                new[] { -half * 0.95f, rimR + 0.012f }, new[] { -half * 0.95f, rimR - 0.01f }, new[] { -half * 0.55f, rimR - 0.02f }, new[] { half * 0.55f, rimR - 0.02f }, new[] { half * 0.95f, rimR - 0.01f }, new[] { half * 0.95f, rimR + 0.012f }
            };
            MeshUtil.LatheX(w, 1, 0, 0, 0, rim, 36, 170, 172, 176);
            // face disc (outer side +x for the right wheel; mirrored by yaw for the left)
            var face = new List<float[]> { new[] { half * 0.90f, rimR }, new[] { half * 0.70f, rimR * 0.72f }, new[] { half * 0.62f, rimR * 0.30f }, new[] { half * 0.70f, rimR * 0.12f }, new[] { half * 0.74f, 0.0f } };
            MeshUtil.LatheX(w, 1, 0, 0, 0, face, 36, 52, 54, 58);
            // 5 double spokes
            for (int s = 0; s < 5; s++)
            {
                float a = (float)(2 * Math.PI * s / 5);
                for (int k = -1; k <= 1; k += 2)
                {
                    float ao = a + k * 0.11f;
                    float len = rimR * 0.78f, cy = (float)Math.Cos(ao) * (rimR * 0.5f + 0.01f), cz = (float)Math.Sin(ao) * (rimR * 0.5f + 0.01f);
                    SpokeBox(w, half * 0.82f, cy, cz, ao, len, 0.034f, 0.026f);
                }
            }
            // brake disc + caliper (visible between spokes)
            MeshUtil.LatheX(w, 1, 0, 0, 0, new List<float[]> { new[] { half * 0.30f, 0.07f }, new[] { half * 0.30f, 0.152f }, new[] { half * 0.38f, 0.152f }, new[] { half * 0.38f, 0.07f } }, 28, 96, 98, 102);
            MeshUtil.Box(w, 1, half * 0.52f, 0.10f, 0.0f, 0.05f, 0.09f, 0.16f, 200, 40, 36, 0);
            // hub cap
            MeshUtil.LatheX(w, 1, 0, 0, 0, new List<float[]> { new[] { half * 0.78f, 0.0f }, new[] { half * 0.80f, 0.030f }, new[] { half * 0.74f, 0.045f }, new[] { half * 0.70f, 0.0f } }, 16, 150, 152, 158);
        }

        static void SpokeBox(MeshData w, float x, float cy, float cz, float ang, float len, float width, float depth)
        {
            // box oriented radially in the y-z plane
            float ca = (float)Math.Cos(ang), sa = (float)Math.Sin(ang);
            float[] Pt(float lx, float ly, float lz) => new[] { x + lx, cy + (ly * ca - lz * sa), cz + (ly * sa + lz * ca) };
            float hx = depth / 2, hy = len / 2, hz = width / 2;
            var v = new[] { Pt(-hx, -hy, -hz), Pt(hx, -hy, -hz), Pt(hx, hy, -hz), Pt(-hx, hy, -hz), Pt(-hx, -hy, hz), Pt(hx, -hy, hz), Pt(hx, hy, hz), Pt(-hx, hy, hz) };
            int[][] faces = { new[] { 4, 5, 6, 7 }, new[] { 1, 0, 3, 2 }, new[] { 5, 1, 2, 6 }, new[] { 0, 4, 7, 3 }, new[] { 3, 7, 6, 2 }, new[] { 0, 1, 5, 4 } };
            foreach (var f in faces)
            {
                var a = v[f[0]]; var b = v[f[1]]; var c = v[f[2]]; var d = v[f[3]];
                w.AddQuad(1, a, b, c, d, 0, 0, 1, 1, 178, 180, 186);
            }
            MeshUtil.FixWindingLast(w, 6);
        }
    }
}
