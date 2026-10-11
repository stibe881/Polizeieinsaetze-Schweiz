using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Meshing
{
    public sealed class HumanSpec
    {
        public float Height = 1.75f, Build = 1f; public bool Female, Child, Elderly, Police, HiVis, Cap, Backpack;
        public byte[] Shirt = { 90, 110, 150 }, Trousers = { 50, 55, 70 }, Skin = { 224, 178, 150 }, Hair = { 60, 42, 30 }, Shoes = { 30, 30, 32 };
        public int HairStyle;          // 0 short, 1 long, 2 bald, 3 tied back, 4 curly
        public bool ShortSleeves;

        public static readonly byte[][] SkinTones = { new byte[] { 238, 200, 175 }, new byte[] { 224, 178, 150 }, new byte[] { 205, 155, 120 }, new byte[] { 160, 112, 82 }, new byte[] { 120, 80, 58 }, new byte[] { 90, 60, 44 } };
        public static readonly byte[][] HairTones = { new byte[] { 36, 26, 20 }, new byte[] { 70, 48, 32 }, new byte[] { 120, 84, 48 }, new byte[] { 190, 160, 100 }, new byte[] { 150, 150, 150 }, new byte[] { 130, 55, 35 }, new byte[] { 18, 18, 18 } };
        public static readonly byte[][] ShirtTones =
        {
            new byte[] { 228, 228, 224 }, new byte[] { 52, 92, 150 }, new byte[] { 160, 50, 46 }, new byte[] { 70, 120, 84 }, new byte[] { 40, 40, 44 }, new byte[] { 200, 170, 80 },
            new byte[] { 120, 120, 128 }, new byte[] { 214, 120, 60 }, new byte[] { 150, 90, 130 }, new byte[] { 90, 150, 170 }, new byte[] { 180, 160, 130 }, new byte[] { 40, 60, 90 }
        };
        public static readonly byte[][] TrouserTones = { new byte[] { 40, 44, 60 }, new byte[] { 34, 34, 36 }, new byte[] { 76, 90, 120 }, new byte[] { 110, 98, 80 }, new byte[] { 60, 70, 64 }, new byte[] { 130, 130, 135 }, new byte[] { 90, 60, 44 }, new byte[] { 54, 70, 110 } };

        /// <summary>Appearance from the compact indices carried by the pedestrian simulation.</summary>
        public static HumanSpec FromPed(bool female, bool child, bool elderly, byte shirt, byte trousers, byte skin, byte hair, float height, int seed)
        {
            var r = new Random(seed);
            return new HumanSpec
            {
                Female = female, Child = child, Elderly = elderly, Height = height, Build = child ? 1f : 0.88f + (float)r.NextDouble() * 0.3f,
                Shirt = ShirtTones[shirt % ShirtTones.Length], Trousers = TrouserTones[trousers % TrouserTones.Length], Skin = SkinTones[skin % SkinTones.Length], Hair = HairTones[hair % HairTones.Length],
                HairStyle = elderly && r.NextDouble() < 0.3 ? 2 : female ? 1 + r.Next(3) : r.Next(5), ShortSleeves = r.NextDouble() < 0.3, Backpack = !elderly && r.NextDouble() < 0.18,
                Shoes = r.NextDouble() < 0.5 ? new byte[] { 30, 30, 32 } : new byte[] { 200, 200, 200 }
            };
        }

        public static HumanSpec Officer(bool female, int seed, bool hiVis)
        {
            var r = new Random(seed);
            return new HumanSpec { Female = female, Police = true, Cap = true, HiVis = hiVis, Height = female ? 1.68f : 1.80f, Build = 1.0f + (float)r.NextDouble() * 0.12f, Shirt = new byte[] { 34, 44, 74 }, Trousers = new byte[] { 28, 34, 56 }, Skin = SkinTones[r.Next(3)], Hair = HairTones[r.Next(4)], HairStyle = female ? 3 : 0, Shoes = new byte[] { 20, 20, 22 } };
        }
    }

    public sealed class HumanPart { public string Name, Parent; public float[] Pivot; public MeshData Mesh; }

    public sealed class HumanModel
    {
        public readonly List<HumanPart> Parts = new List<HumanPart>(); public float Height, HipHeight;
        public HumanPart Get(string name) { foreach (var p in Parts) if (p.Name == name) return p; return null; }
    }

    /// <summary>
    /// Articulated low-poly human: 11 parts hanging from joint pivots (pelvis/torso, head, 2×upper arm, 2×forearm+hand, 2×thigh, 2×shin+foot),
    /// lofted from elliptical rings with outward normals, face details, hair/cap, optional police uniform with belt, radio, hi-vis vest.
    /// Submeshes: 0 body (cloth / skin, matte), 1 leather &amp; plastics (shoes, belt, glossy), 2 back patch (POLIZEI texture).
    /// </summary>
    public static class HumanMesher
    {
        public const int SubBody = 0, SubLeather = 1, SubPatch = 2, Submeshes = 3;

        struct Ring { public float T, C1, C2, R1, R2; public byte R, G, B; public Ring(float t, float c1, float c2, float r1, float r2, byte[] c) { T = t; C1 = c1; C2 = c2; R1 = r1; R2 = r2; R = c[0]; G = c[1]; B = c[2]; } }

        public static HumanModel Build(HumanSpec sp, int lod = 0)
        {
            var model = new HumanModel { Height = sp.Height };
            float s = sp.Height / 1.75f; float child = sp.Child ? 1f : 0f;
            // children: shorter limbs relative to head/torso
            float legK = sp.Child ? 0.84f : 1f, headK = sp.Child ? 1.28f : 1f;
            int seg = lod == 0 ? 12 : 7;
            float b = sp.Build * (sp.Female ? 0.93f : 1f), stoop = sp.Elderly ? 0.07f : 0f;
            float hipY = 0.92f * legK;
            model.HipHeight = hipY * s;
            var skin = sp.Skin; var shirt = sp.Police ? sp.Shirt : sp.Shirt; var trou = sp.Trousers;
            byte[] sleeve = sp.Police || !sp.ShortSleeves ? shirt : skin;

            // ---------------------------------------------------------------- pelvis + torso
            var torso = new MeshData(Submeshes); var tr = new List<Ring>();
            float hipW = (sp.Female ? 0.185f : 0.165f) * b, waistW = (sp.Female ? 0.140f : 0.155f) * b, chestW = (sp.Female ? 0.165f : 0.188f) * b, shW = 0.195f * b;
            tr.Add(new Ring(-0.085f, 0, 0, hipW * 0.80f, 0.100f * b, trou)); tr.Add(new Ring(-0.02f, 0, 0, hipW, 0.112f * b, trou)); tr.Add(new Ring(0.045f, 0, 0, hipW * 0.97f, 0.108f * b, trou));
            tr.Add(new Ring(0.06f, 0, 0, hipW * 0.96f, 0.106f * b, shirt));
            tr.Add(new Ring(0.15f, 0, 0.004f, waistW, 0.100f * b, shirt)); tr.Add(new Ring(0.28f, 0, 0.008f, chestW * 0.96f, 0.110f * b, shirt));
            tr.Add(new Ring(0.39f, 0, 0.010f - stoop * 0.3f, chestW, 0.118f * b + (sp.Female ? 0.01f : 0), shirt)); tr.Add(new Ring(0.445f, 0, 0.004f, shW * 0.98f, 0.104f * b, shirt)); tr.Add(new Ring(0.48f, 0, 0.002f, shW * 0.84f, 0.092f * b, shirt));
            tr.Add(new Ring(0.515f, 0, 0, shW * 0.50f, 0.070f * b, shirt)); tr.Add(new Ring(0.535f, 0, 0, 0.060f, 0.058f, skin)); tr.Add(new Ring(0.56f, 0, 0, 0.052f, 0.052f, skin));
            Loft(torso, SubBody, tr, seg, 1, true, true);
            if (sp.Police || sp.HiVis)
            {   // duty belt + holster + radio
                var belt = new List<Ring> { new Ring(0.035f, 0, 0, hipW * 1.0f + 0.004f, 0.112f * b + 0.006f, new byte[] { 20, 20, 22 }), new Ring(0.095f, 0, 0, hipW * 0.98f + 0.004f, 0.108f * b + 0.006f, new byte[] { 20, 20, 22 }) };
                Loft(torso, SubLeather, belt, seg, 1, true, true);
                MeshUtil.Box(torso, SubLeather, 0.15f * b + 0.04f, 0.04f, 0.02f, 0.05f, 0.17f, 0.10f, 24, 24, 26);                  // holster
                MeshUtil.Box(torso, SubLeather, -0.15f * b - 0.03f, 0.075f, 0.03f, 0.04f, 0.10f, 0.07f, 28, 28, 30);                 // magazine / cuff case
                MeshUtil.Box(torso, SubLeather, -0.17f * b, 0.43f, 0.07f, 0.045f, 0.095f, 0.03f, 25, 25, 28);                        // shoulder radio
            }
            if (sp.HiVis)
            {
                byte[] yellow = { 240, 224, 30 }, silver = { 210, 214, 220 };
                float k = 1.045f; var vest = new List<Ring>
                {
                    new Ring(0.10f, 0, 0, waistW * k, 0.100f * b * k + 0.004f, yellow), new Ring(0.17f, 0, 0.004f, waistW * k, 0.102f * b * k, silver), new Ring(0.20f, 0, 0.005f, waistW * k, 0.103f * b * k, silver), new Ring(0.215f, 0, 0.006f, waistW * k, 0.103f * b * k, yellow),
                    new Ring(0.30f, 0, 0.008f, chestW * k, 0.112f * b * k, yellow), new Ring(0.345f, 0, 0.009f, chestW * k, 0.114f * b * k, silver), new Ring(0.375f, 0, 0.010f, chestW * k, 0.116f * b * k, silver), new Ring(0.39f, 0, 0.010f, chestW * k, 0.117f * b * k, yellow),
                    new Ring(0.46f, 0, 0.004f, shW * 0.86f, 0.100f * b * k, yellow)
                };
                Loft(torso, SubBody, vest, seg, 1, false, false);
            }
            if (sp.Police)
            {   // POLIZEI back patch
                float pz = -(0.118f * b * 1.0f + 0.012f); float w = 0.30f * b, h = 0.075f, y0 = 0.30f;
                int a = torso.AddVertex(-w / 2, y0, pz, 0, 0, -1, 1, 0, 255, 255, 255), bb = torso.AddVertex(w / 2, y0, pz, 0, 0, -1, 0, 0, 255, 255, 255), c = torso.AddVertex(w / 2, y0 + h, pz, 0, 0, -1, 0, 1, 255, 255, 255), d = torso.AddVertex(-w / 2, y0 + h, pz, 0, 0, -1, 1, 1, 255, 255, 255);
                torso.AddTriangle(SubPatch, a, c, bb); torso.AddTriangle(SubPatch, a, d, c);
            }
            if (sp.Backpack && !sp.Police)
            {
                byte[] bc = { (byte)(40 + (sp.Shirt[0] / 3)), (byte)(40 + (sp.Shirt[1] / 4)), 60 };
                MeshUtil.Box(torso, SubBody, 0, 0.30f, -0.17f * b, 0.28f * b, 0.34f, 0.12f, bc[0], bc[1], bc[2]);
            }
            Fix(torso); Add(model, "Pelvis", null, 0, hipY * s, 0, torso, s);

            // ---------------------------------------------------------------- head & neck
            var head = new MeshData(Submeshes); float hk = headK * (sp.Female ? 0.96f : 1f);
            float hr = 0.082f * hk, hd = 0.095f * hk, hh = 0.112f * hk; float cy = 0.115f;
            var hrings = new List<Ring>();
            for (int i = 0; i <= 8; i++)
            {
                float t = -1 + 2 * i / 8f; float sq = (float)Math.Sqrt(Math.Max(0.0, 1 - t * t * 0.985f)); float jaw = t < -0.15f ? 0.80f + 0.20f * (t + 1f) / 0.85f : 1f;
                byte[] col = skin; hrings.Add(new Ring(cy + t * hh, 0, t < -0.3f ? 0.006f : 0.0f, hr * sq * jaw, hd * sq * (t < -0.3f ? 0.92f : 1f), col));
            }
            Loft(head, SubBody, hrings, lod == 0 ? 14 : 8, 1, true, true);
            MeshUtil.Cylinder(head, SubBody, 0, -0.045f, -0.008f, 0.049f, 0.047f, 0.10f, 8, skin[0], skin[1], skin[2], false);        // neck
            if (lod == 0)
            {
                MeshUtil.Box(head, SubBody, 0, cy - 0.012f * hk, hd * 0.97f, 0.026f * hk, 0.044f * hk, 0.03f, (byte)(skin[0] * 0.96f), (byte)(skin[1] * 0.92f), (byte)(skin[2] * 0.9f));   // nose
                for (int e = -1; e <= 1; e += 2)
                {
                    MeshUtil.Box(head, SubLeather, e * 0.034f * hk, cy + 0.020f * hk, hd * 0.93f, 0.020f * hk, 0.011f * hk, 0.008f, 30, 26, 24);                    // eye
                    MeshUtil.Box(head, SubBody, e * 0.034f * hk, cy + 0.041f * hk, hd * 0.925f, 0.030f * hk, 0.006f, 0.008f, sp.Hair[0], sp.Hair[1], sp.Hair[2]);   // brow
                    MeshUtil.Box(head, SubBody, e * hr * 0.99f, cy - 0.004f * hk, -0.004f, 0.010f, 0.036f * hk, 0.026f * hk, (byte)(skin[0] * 0.95f), (byte)(skin[1] * 0.9f), (byte)(skin[2] * 0.9f)); // ear
                }
                MeshUtil.Box(head, SubLeather, 0, cy - 0.052f * hk, hd * 0.90f, 0.040f * hk, 0.006f, 0.008f, 150, 70, 70);                                    // mouth
            }
            // hair / cap
            if (sp.Cap)
            {
                byte[] cc = sp.Police ? new byte[] { 24, 32, 58 } : new byte[] { 60, 60, 66 };
                var cap = new List<Ring> { new Ring(cy + 0.01f * hk, 0, 0, hr * 1.10f, hd * 1.08f, cc), new Ring(cy + 0.075f * hk, 0, 0, hr * 1.12f, hd * 1.10f, cc), new Ring(cy + 0.115f * hk, 0, 0, hr * 0.95f, hd * 0.98f, cc), new Ring(cy + 0.135f * hk, 0, 0, hr * 0.5f, hd * 0.5f, cc) };
                Loft(head, SubBody, cap, 12, 1, true, true);
                MeshUtil.Box(head, SubLeather, 0, cy + 0.045f * hk, hd * 1.12f, hr * 1.7f, 0.012f, 0.07f, 16, 16, 20);                                       // visor
                MeshUtil.Box(head, SubBody, 0, cy + 0.060f * hk, hd * 1.085f + 0.01f, 0.030f, 0.026f, 0.006f, 190, 190, 200);                                 // badge
                if (sp.HairStyle == 3 || sp.Female) MeshUtil.Box(head, SubBody, 0, cy - 0.02f * hk, -hd * 1.05f, 0.05f, 0.09f, 0.045f, sp.Hair[0], sp.Hair[1], sp.Hair[2]);   // bun under the cap
            }
            else if (sp.HairStyle != 2)
            {
                float lift = sp.HairStyle == 4 ? 1.12f : 1.045f; var hair = new List<Ring>();
                float yLow = sp.HairStyle == 1 ? cy - 0.10f * hk : sp.HairStyle == 3 ? cy - 0.01f * hk : cy + 0.02f * hk;
                hair.Add(new Ring(yLow, 0, -0.012f * hk, hr * lift * 0.90f, hd * lift * 0.92f, sp.Hair)); hair.Add(new Ring(cy + 0.04f * hk, 0, -0.004f, hr * lift, hd * lift, sp.Hair));
                hair.Add(new Ring(cy + 0.085f * hk, 0, 0, hr * lift * 0.88f, hd * lift * 0.92f, sp.Hair)); hair.Add(new Ring(cy + 0.112f * hk, 0, 0.0f, hr * lift * 0.45f, hd * lift * 0.5f, sp.Hair)); hair.Add(new Ring(cy + 0.12f * hk, 0, 0, 0.004f, 0.004f, sp.Hair));
                Loft(head, SubBody, hair, lod == 0 ? 14 : 8, 1, true, false);
                if (sp.HairStyle == 3) MeshUtil.Box(head, SubBody, 0, cy + 0.03f * hk, -hd * 1.15f, 0.045f, 0.075f, 0.06f, sp.Hair[0], sp.Hair[1], sp.Hair[2]);
            }
            Fix(head); Add(model, "Head", "Pelvis", 0, 0.575f, 0.003f + stoop * 0.5f, head, s);

            // ---------------------------------------------------------------- arms (shoulder pivots)
            float armK = legK;
            for (int side = -1; side <= 1; side += 2)
            {
                float sx = side * (shW * 0.86f + 0.012f);
                var upper = new MeshData(Submeshes); float ul = 0.295f * armK, fl = 0.265f * armK;
                var ur = new List<Ring> { new Ring(0.04f, 0, 0, 0.052f * b, 0.050f * b, sleeve), new Ring(0, 0, 0, 0.050f * b, 0.048f * b, sleeve), new Ring(-ul * 0.5f, 0, 0, 0.044f * b, 0.043f * b, sleeve), new Ring(-ul, 0, 0, 0.038f * b, 0.038f * b, sleeve) };
                Loft(upper, SubBody, ur, seg, 1, false, false); Fix(upper); Add(model, side < 0 ? "UpperArmL" : "UpperArmR", "Pelvis", sx, 0.48f, 0, upper, s);
                var fore = new MeshData(Submeshes);
                byte[] cuff = sp.Police ? shirt : skin; byte[] fc = sp.ShortSleeves && !sp.Police ? skin : shirt;
                var fr = new List<Ring> { new Ring(0.0f, 0, 0, 0.038f * b, 0.038f * b, fc), new Ring(-fl * 0.5f, 0, 0, 0.034f * b, 0.033f * b, fc), new Ring(-fl * 0.93f, 0, 0, 0.029f * b, 0.027f * b, fc), new Ring(-fl, 0, 0, 0.027f, 0.025f, cuff) };
                Loft(fore, SubBody, fr, seg, 1, false, false);
                var hand = new List<Ring> { new Ring(-fl, 0, 0, 0.030f, 0.014f, skin), new Ring(-fl - 0.045f, 0, 0.002f, 0.040f, 0.017f, skin), new Ring(-fl - 0.09f, 0, 0.004f, 0.032f, 0.013f, skin), new Ring(-fl - 0.105f, 0, 0.004f, 0.018f, 0.009f, skin) };
                Loft(fore, SubBody, hand, 8, 1, false, true);
                MeshUtil.Box(fore, SubBody, side * -0.002f - side * 0.02f, -fl - 0.04f, 0.02f, 0.016f, 0.05f, 0.016f, skin[0], skin[1], skin[2]);   // thumb
                Fix(fore); Add(model, side < 0 ? "ForearmL" : "ForearmR", side < 0 ? "UpperArmL" : "UpperArmR", 0, -ul, 0, fore, s);
            }

            // ---------------------------------------------------------------- legs (hip pivots)
            for (int side = -1; side <= 1; side += 2)
            {
                float hx = side * hipW * 0.52f; float tl = 0.435f * legK, sl = 0.42f * legK;
                var thigh = new MeshData(Submeshes);
                var th = new List<Ring> { new Ring(0.06f, 0, 0, 0.088f * b, 0.092f * b, trou), new Ring(-tl * 0.3f, 0, 0, 0.082f * b, 0.084f * b, trou), new Ring(-tl * 0.7f, 0, 0, 0.065f * b, 0.066f * b, trou), new Ring(-tl, 0, 0.002f, 0.056f * b, 0.058f * b, trou) };
                Loft(thigh, SubBody, th, seg, 1, false, false); Fix(thigh); Add(model, side < 0 ? "ThighL" : "ThighR", "Pelvis", hx, 0.0f, 0, thigh, s);
                var shin = new MeshData(Submeshes);
                var sh = new List<Ring> { new Ring(0.0f, 0, 0, 0.056f * b, 0.058f * b, trou), new Ring(-sl * 0.25f, 0, -0.008f, 0.056f * b, 0.062f * b, trou), new Ring(-sl * 0.75f, 0, 0, 0.040f * b, 0.040f * b, trou), new Ring(-sl, 0, 0, 0.036f, 0.038f, trou) };
                Loft(shin, SubBody, sh, seg, 1, false, true);
                // shoe: loft along Z (heel → toe)
                float fy = -sl; var sc = sp.Shoes; var shoe = new List<Ring>
                {
                    new Ring(-0.075f, 0, fy - 0.040f, 0.036f, 0.035f, sc), new Ring(-0.04f, 0, fy - 0.040f, 0.043f, 0.043f, sc), new Ring(0.04f, 0, fy - 0.048f, 0.048f, 0.040f, sc), new Ring(0.13f, 0, fy - 0.055f, 0.047f, 0.030f, sc), new Ring(0.20f, 0, fy - 0.062f, 0.036f, 0.020f, sc), new Ring(0.225f, 0, fy - 0.066f, 0.020f, 0.012f, sc)
                };
                LoftZ(shin, SubLeather, shoe, lod == 0 ? 10 : 6);
                MeshUtil.Box(shin, SubLeather, 0, fy - 0.092f, 0.07f, 0.092f, 0.018f, 0.30f, 14, 14, 16);       // sole
                Fix(shin); Add(model, side < 0 ? "ShinL" : "ShinR", side < 0 ? "ThighL" : "ThighR", 0, -tl, 0, shin, s);
            }
            return model;
        }

        static void Add(HumanModel m, string name, string parent, float px, float py, float pz, MeshData mesh, float scale)
        {
            if (Math.Abs(scale - 1f) > 1e-4f) { for (int i = 0; i < mesh.Positions.Count; i++) mesh.Positions[i] *= scale; }
            m.Parts.Add(new HumanPart { Name = name, Parent = parent, Pivot = new[] { px * scale, py * scale, pz * scale }, Mesh = mesh });
        }

        static void Fix(MeshData m) { MeshUtil.FixWinding(m); }

        /// <summary>Rings stacked along Y (T = height); each ring is an ellipse (R1 along x, R2 along z) centred at (C1, C2). Normals point outwards.</summary>
        static void Loft(MeshData m, int sub, List<Ring> rings, int seg, int dummy, bool capStart, bool capEnd)
        {
            int first = m.VertexCount; int n = rings.Count;
            for (int r = 0; r < n; r++)
            {
                var g = rings[r];
                // slope for normal tilt
                float dr = 0; int r0 = Math.Max(0, r - 1), r1 = Math.Min(n - 1, r + 1);
                if (r1 != r0) { float dT = rings[r1].T - rings[r0].T; if (Math.Abs(dT) < 1e-5f) dT = dT < 0 ? -1e-5f : 1e-5f; dr = (rings[r1].R1 - rings[r0].R1) / dT; }
                for (int i = 0; i <= seg; i++)
                {
                    float a = (float)(2 * Math.PI * i / seg), ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                    float nx = ca / Math.Max(1e-3f, g.R1) * g.R1, nz = sa / Math.Max(1e-3f, g.R2) * g.R1, ny = -dr; float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz); if (l < 1e-6f) l = 1;
                    m.AddVertex(g.C1 + ca * g.R1, g.T, g.C2 + sa * g.R2, nx / l, ny / l, nz / l, i / (float)seg, r / (float)Math.Max(1, n - 1), g.R, g.G, g.B);
                }
            }
            int stride = seg + 1;
            for (int r = 0; r < n - 1; r++)
                for (int i = 0; i < seg; i++)
                {
                    int a = first + r * stride + i, b = a + 1, c = a + stride, d = c + 1;
                    m.AddTriangle(sub, a, c, b); m.AddTriangle(sub, b, c, d);
                }
            if (capStart) Cap(m, sub, rings[0], seg, -1); if (capEnd) Cap(m, sub, rings[n - 1], seg, 1);
        }

        static void Cap(MeshData m, int sub, Ring g, int seg, int dir)
        {
            int c = m.AddVertex(g.C1, g.T, g.C2, 0, dir, 0, 0.5f, 0.5f, g.R, g.G, g.B); int first = m.VertexCount;
            for (int i = 0; i <= seg; i++) { float a = (float)(2 * Math.PI * i / seg); m.AddVertex(g.C1 + (float)Math.Cos(a) * g.R1, g.T, g.C2 + (float)Math.Sin(a) * g.R2, 0, dir, 0, 0.5f, 0.5f, g.R, g.G, g.B); }
            for (int i = 0; i < seg; i++) m.AddTriangle(sub, c, first + i, first + i + 1);
        }

        /// <summary>Rings stacked along Z (T = z): ellipse in the x/y plane, centre (C1, C2 = y).</summary>
        static void LoftZ(MeshData m, int sub, List<Ring> rings, int seg)
        {
            int first = m.VertexCount; int n = rings.Count;
            for (int r = 0; r < n; r++)
            {
                var g = rings[r];
                for (int i = 0; i <= seg; i++)
                {
                    float a = (float)(2 * Math.PI * i / seg), ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                    float nx = ca / Math.Max(1e-3f, g.R1), ny = sa / Math.Max(1e-3f, g.R2); float l = (float)Math.Sqrt(nx * nx + ny * ny); if (l < 1e-6f) l = 1;
                    m.AddVertex(g.C1 + ca * g.R1, g.C2 + sa * g.R2, g.T, nx / l, ny / l, 0, i / (float)seg, r / (float)Math.Max(1, n - 1), g.R, g.G, g.B);
                }
            }
            int stride = seg + 1;
            for (int r = 0; r < n - 1; r++)
                for (int i = 0; i < seg; i++) { int a = first + r * stride + i, b = a + 1, c = a + stride, d = c + 1; m.AddTriangle(sub, a, c, b); m.AddTriangle(sub, b, c, d); }
            // toe cap
            var last = rings[n - 1]; int cv = m.AddVertex(last.C1, last.C2, last.T + 0.004f, 0, 0, 1, 0.5f, 0.5f, last.R, last.G, last.B);
            for (int i = 0; i < seg; i++) m.AddTriangle(sub, cv, first + (n - 1) * stride + i, first + (n - 1) * stride + i + 1);
            var h0 = rings[0]; int cb = m.AddVertex(h0.C1, h0.C2, h0.T - 0.004f, 0, 0, -1, 0.5f, 0.5f, h0.R, h0.G, h0.B);
            for (int i = 0; i < seg; i++) m.AddTriangle(sub, cb, first + i + 1, first + i);
        }
    }
}
