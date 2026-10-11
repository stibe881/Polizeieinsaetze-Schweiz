using System;

namespace SwissPoliceSim.Core.Textures
{
    /// <summary>Textures for the geometric facade details (window panes, shutters, railings, plaster).</summary>
    public static partial class ProceduralTextures
    {
        /// <summary>Plain rendered wall (no windows), tinted by the vertex colour. Fine grain, subtle large-scale mottling and faint rain streaks.</summary>
        public static Image PlasterWall(int n = 256) => Make(n, (u, v) =>
        {
            float k = Fbm(u, v, 6, 4, 201) * 0.10f + Noise(u * 140, v * 140, 140, 202) * 0.06f;
            float streak = Noise(u * 48, v * 3, 48, 203); streak = streak > 0.7f ? (streak - 0.7f) * 0.35f : 0f;
            float c = 0.90f + k - streak - 0.05f;
            return new[] { c, c * 0.995f, c * 0.98f, 1f };
        });

        /// <summary>Window opening 0..1 in both directions: white frame, glazing bars, sky-reflecting glass. variant: 0 plain, 1 curtain, 2 blind half down, 3 dark interior, 4 shop window (dark thin frame).</summary>
        public static Image WindowPane(int variant, int w = 128, int h = 192)
        {
            var im = new Image(w, h);
            bool shop = variant == 4;
            float fr = shop ? 0.045f : 0.085f;                       // frame width (fraction of width)
            float fx = fr * w, fy = fr * w;                           // pixels
            float frameR = shop ? 0.20f : 0.94f, frameG = shop ? 0.21f : 0.94f, frameB = shop ? 0.23f : 0.92f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float t = y / (float)(h - 1);                    // 0 bottom .. 1 top
                    float s = x / (float)(w - 1);
                    // glass: sky reflection + interior
                    float refl = 0.45f + 0.35f * t + 0.12f * (float)Math.Sin((s * 1.2f + t) * 7f) + 0.06f * Noise(s * 20, t * 20, 20, 211 + variant);
                    float r = 0.16f + 0.20f * refl, g = 0.24f + 0.26f * refl, b = 0.33f + 0.32f * refl;
                    if (variant == 3 || variant == 4) { r *= 0.55f; g *= 0.58f; b *= 0.62f; }
                    im.Set(x, y, r, g, b, 1f);
                }
            // curtain / blind (behind the bars)
            if (variant == 1)
            {
                for (int y = (int)fy; y < h - (int)fy; y++)
                    for (int x = (int)fx; x < w - (int)fx; x++)
                    {
                        float s = (x - fx) / (w - 2 * fx);
                        if (s < 0.30f || s > 0.74f) { float fold = 0.5f + 0.5f * (float)Math.Sin(x * 0.9f); im.Set(x, y, 0.86f + 0.1f * fold, 0.84f + 0.1f * fold, 0.78f + 0.08f * fold, 1f); }
                    }
            }
            else if (variant == 2)
            {
                int yTop = (int)(h * 0.52f);
                for (int y = yTop; y < h - (int)fy; y++)
                    for (int x = (int)fx; x < w - (int)fx; x++)
                    { float slat = (y % 6) < 4 ? 0.88f : 0.70f; im.Set(x, y, slat, slat * 0.98f, slat * 0.94f, 1f); }
            }
            // frame, transom and mullion
            im.FillRect(0, 0, w, fy, frameR, frameG, frameB);
            im.FillRect(0, h - fy, w, h, frameR, frameG, frameB);
            im.FillRect(0, 0, fx, h, frameR, frameG, frameB);
            im.FillRect(w - fx, 0, w, h, frameR, frameG, frameB);
            float bar = shop ? 0.018f * w : 0.035f * w;
            if (!shop)
            {
                im.FillRect(w * 0.5f - bar, 0, w * 0.5f + bar, h, frameR, frameG, frameB);          // casement mullion
                im.FillRect(0, h * 0.70f - bar, w, h * 0.70f + bar, frameR, frameG, frameB);       // transom
                // inner bevel shadow lines
                im.FillRect(fx, fy, w - fx, fy + 2, 0.5f, 0.5f, 0.5f, 0.35f); im.FillRect(fx, fy, fx + 2, h - fy, 0.5f, 0.5f, 0.5f, 0.35f);
            }
            else
            {
                for (int k = 1; k < 3; k++) im.FillRect(w * k / 3f - bar, 0, w * k / 3f + bar, h, frameR, frameG, frameB);
            }
            return im;
        }

        /// <summary>Five window variants side by side (u range 0.2 each).</summary>
        public static Image WindowAtlas(int w = 128, int h = 192)
        {
            var at = new Image(w * 5, h);
            for (int k = 0; k < 5; k++)
            {
                var p = WindowPane(k, w, h);
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { p.Get(x, y, out float r, out float g, out float b, out float a); at.Set(k * w + x, y, r, g, b, a); }
            }
            return at;
        }

        /// <summary>Louvred shutter ("Fensterladen"), grey-white so the vertex colour gives the paint colour.</summary>
        public static Image ShutterSlats(int w = 64, int h = 128)
        {
            var im = new Image(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float c;
                    int slat = y % 8; c = slat < 5 ? 0.96f - slat * 0.025f : (slat == 5 ? 0.55f : 0.72f);
                    float edge = (x < 3 || x > w - 4 || y < 4 || y > h - 5) ? 0.88f : 1f;
                    c *= edge * (0.97f + 0.03f * Noise(x * 0.4f, y * 0.4f, 64, 221));
                    im.Set(x, y, c, c, c, 1f);
                }
            return im;
        }

        /// <summary>Balcony railing: vertical bars between rails, alpha-cut.</summary>
        public static Image Railing(int w = 128, int h = 64)
        {
            var im = new Image(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool rail = y > h - 7 || y < 5;
                    bool bar = (x % 10) < 3;
                    if (rail || bar) im.Set(x, y, 0.22f, 0.23f, 0.25f, 1f); else im.Set(x, y, 0, 0, 0, 0f);
                }
            return im;
        }

        /// <summary>Roof tile / slate / brick chimney etc. use vertex colour only; this is a neutral fine-grain stone for trim.</summary>
        public static Image Stone(int n = 128) => Make(n, (u, v) =>
        {
            float k = Fbm(u, v, 8, 3, 231) * 0.15f + Noise(u * 90, v * 90, 90, 232) * 0.08f; float c = 0.80f + k - 0.05f;
            return new[] { c, c, c * 0.98f, 1f };
        });

        // ---------------- vegetation cards
        static float Rnd(ref uint st) { st = st * 1664525u + 1013904223u; return ((st >> 8) & 0xFFFFFF) / (float)0x1000000; }

        static void Leaf(Image im, float cx, float cy, float ang, float len, float wid, float r, float g, float b)
        {
            float ca = (float)Math.Cos(ang), sa = (float)Math.Sin(ang);
            int R = (int)(len + 2);
            for (int y = (int)(cy - R); y <= (int)(cy + R); y++)
                for (int x = (int)(cx - R); x <= (int)(cx + R); x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float a = dx * ca + dy * sa, b2 = -dx * sa + dy * ca;          // along / across
                    float t = a / len; if (t < -1 || t > 1) continue;
                    float half = wid * (1 - t * t) * (t < 0 ? 0.9f : 1f);
                    if (Math.Abs(b2) > half) continue;
                    float vein = Math.Abs(b2) < 0.55f ? 0.78f : 1f;
                    float side = 1f - 0.18f * Math.Abs(b2) / Math.Max(0.5f, half);
                    float k = vein * side;
                    if ((uint)x < (uint)im.W && (uint)y < (uint)im.H) im.Set(x, y, r * k, g * k, b * k, 1f);
                }
        }

        /// <summary>Soft cluster of broadleaf leaves (alpha-cut). Back leaves are darker than front leaves, which gives depth.</summary>
        public static Image LeafCluster(int n = 128, int seed = 1)
        {
            var im = new Image(n, n); uint st = (uint)(seed * 7919 + 13);
            int count = 95;
            for (int i = 0; i < count; i++)
            {
                float depth = i / (float)count;
                float rr = (float)Math.Sqrt(Rnd(ref st)) * 0.40f * n, th = Rnd(ref st) * 6.2832f;
                float cx = n * 0.5f + (float)Math.Cos(th) * rr, cy = n * 0.5f + (float)Math.Sin(th) * rr * 0.92f;
                float tone = 0.50f + 0.55f * depth + 0.10f * (Rnd(ref st) - 0.5f);
                float yel = Rnd(ref st) * 0.25f;
                Leaf(im, cx, cy, Rnd(ref st) * 6.2832f, 7.5f + 3.5f * Rnd(ref st), 3.4f + 1.2f * Rnd(ref st), (0.20f + yel) * tone * 1.15f, (0.46f + yel * 0.5f) * tone * 1.15f, 0.12f * tone * 1.15f);
            }
            return im;
        }

        /// <summary>Spruce / fir branch card: v runs from the trunk (0) to the tip (1); needle mass narrows towards the tip.</summary>
        public static Image NeedleBranch(int n = 128, int seed = 2)
        {
            var im = new Image(n, n);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = y / (float)(n - 1), u = x / (float)(n - 1) - 0.5f;
                    float half = 0.46f * (1f - 0.80f * v) + 0.03f;
                    float jag = 0.78f + 0.40f * Noise(v * 14, u * 20 + 3, 64, 241 + seed) + 0.10f * (float)Math.Sin(v * 60 + Math.Abs(u) * 40);
                    float edge = Math.Abs(u) / (half * jag);
                    if (edge > 1f) continue;
                    // needles radiate forward from the central twig
                    float stripe = (float)Math.Sin((v * 1.0f - Math.Abs(u) * 0.9f) * 95f) * 0.5f + 0.5f;
                    float tone = 0.62f + 0.38f * stripe - 0.25f * edge * edge + 0.1f * Noise(u * 40, v * 40, 40, 251);
                    if (Math.Abs(u) < 0.016f) { im.Set(x, y, 0.22f, 0.15f, 0.09f, 1f); continue; }       // twig
                    im.Set(x, y, 0.13f * tone * 1.5f, 0.30f * tone * 1.5f, 0.15f * tone * 1.5f, 1f);
                }
            return im;
        }

        /// <summary>Broadleaf cluster (u 0..0.5) and needle branch (u 0.5..1) in one atlas.</summary>
        public static Image FoliageAtlas(int n = 128)
        {
            var a = LeafCluster(n); var b = NeedleBranch(n); var at = new Image(n * 2, n);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            { a.Get(x, y, out float r, out float g, out float bl, out float al); at.Set(x, y, r, g, bl, al); b.Get(x, y, out r, out g, out bl, out al); at.Set(n + x, y, r, g, bl, al); }
            return at;
        }

        /// <summary>Reflective "POLIZEI" patch for the back of uniforms: white letters on dark blue.</summary>
        public static Image UniformPatch(int w = 256, int h = 64)
        {
            var im = new Image(w, h); im.FillRect(0, 0, w, h, 0.10f, 0.14f, 0.26f, 1f);
            DrawText(im, "POLIZEI", 6, 8, w - 12, h - 16, 0.96f, 0.96f, 0.98f, false);
            return im;
        }
    }
}
