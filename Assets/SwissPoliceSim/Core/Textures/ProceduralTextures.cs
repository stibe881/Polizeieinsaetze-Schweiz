using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Textures
{
    /// <summary>RGBA8 image (row 0 = bottom, like Unity textures).</summary>
    public sealed class Image
    {
        public readonly int W, H; public readonly byte[] Px;
        public Image(int w, int h) { W = w; H = h; Px = new byte[w * h * 4]; }
        public void Set(int x, int y, float r, float g, float b, float a = 1f)
        {
            if ((uint)x >= (uint)W || (uint)y >= (uint)H) return;
            int i = (y * W + x) * 4; Px[i] = C(r); Px[i + 1] = C(g); Px[i + 2] = C(b); Px[i + 3] = C(a);
        }
        public void Blend(int x, int y, float r, float g, float b, float a)
        {
            if ((uint)x >= (uint)W || (uint)y >= (uint)H || a <= 0) return;
            int i = (y * W + x) * 4; float ia = 1 - a;
            Px[i] = C(Px[i] / 255f * ia + r * a); Px[i + 1] = C(Px[i + 1] / 255f * ia + g * a); Px[i + 2] = C(Px[i + 2] / 255f * ia + b * a);
            Px[i + 3] = C(Math.Max(Px[i + 3] / 255f, a));
        }
        public void Get(int x, int y, out float r, out float g, out float b, out float a)
        {
            x = ((x % W) + W) % W; y = ((y % H) + H) % H; int i = (y * W + x) * 4; r = Px[i] / 255f; g = Px[i + 1] / 255f; b = Px[i + 2] / 255f; a = Px[i + 3] / 255f;
        }
        static byte C(float v) => (byte)(v <= 0 ? 0 : (v >= 1 ? 255 : (int)(v * 255f + 0.5f)));

        public void FillRect(float x0, float y0, float x1, float y1, float r, float g, float b, float a = 1f)
        {
            for (int y = (int)Math.Floor(y0); y < (int)Math.Ceiling(y1); y++) for (int x = (int)Math.Floor(x0); x < (int)Math.Ceiling(x1); x++) Blend(x, y, r, g, b, a);
        }

        /// <summary>Downsampled copy (box filter) for mip levels.</summary>
        public Image Half()
        {
            var o = new Image(Math.Max(1, W / 2), Math.Max(1, H / 2));
            for (int y = 0; y < o.H; y++)
                for (int x = 0; x < o.W; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) { Get(Math.Min(W - 1, x * 2 + dx), Math.Min(H - 1, y * 2 + dy), out float rr, out float gg, out float bb, out float aa); r += rr; g += gg; b += bb; a += aa; }
                    o.Set(x, y, r / 4, g / 4, b / 4, a / 4);
                }
            return o;
        }
    }

    /// <summary>Seamlessly tiling procedural textures shared by Unity (runtime) and the software preview renderer.</summary>
    public static partial class ProceduralTextures
    {
        // ---------------- tiling value noise
        static float Hash(int x, int y, int seed)
        {
            unchecked { int h = x * 374761393 + y * 668265263 + seed * 1274126177; h = (h ^ (h >> 13)) * 1274126177; h ^= h >> 16; return (h & 0xFFFFFF) / (float)0x1000000; }
        }

        public static float Noise(float x, float y, int period, int seed)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y); float fx = x - xi, fy = y - yi;
            float u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
            float a = Hash(Mod(xi, period), Mod(yi, period), seed), b = Hash(Mod(xi + 1, period), Mod(yi, period), seed);
            float c = Hash(Mod(xi, period), Mod(yi + 1, period), seed), d = Hash(Mod(xi + 1, period), Mod(yi + 1, period), seed);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }

        static int Mod(int a, int m) => ((a % m) + m) % m;

        public static float Fbm(float u, float v, int baseFreq, int octaves, int seed)
        {
            float s = 0, amp = 0.5f, tot = 0; int f = baseFreq;
            for (int o = 0; o < octaves; o++) { s += amp * Noise(u * f, v * f, f, seed + o * 17); tot += amp; amp *= 0.5f; f *= 2; }
            return s / tot;
        }

        static Image Make(int size, Func<float, float, float[]> f)
        {
            var im = new Image(size, size);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) { var c = f((x + 0.5f) / size, (y + 0.5f) / size); im.Set(x, y, c[0], c[1], c[2], c.Length > 3 ? c[3] : 1f); }
            return im;
        }

        // ---------------- terrain / ground
        public static Image Grass(int n = 256) => Make(n, (u, v) =>
        {
            float a = Fbm(u, v, 6, 4, 1), b = Fbm(u, v, 24, 3, 2), blades = Noise(u * 96, v * 96, 96, 3);
            float k = 0.55f * a + 0.35f * b + 0.10f * blades;
            float patch = Fbm(u, v, 3, 3, 7), clover = Noise(u * 40, v * 40, 40, 9);
            float dry = Math.Max(0, patch - 0.55f) * 1.6f;                     // sun-bleached patches
            float cr = 0.27f + 0.16f * k + 0.12f * dry + 0.03f * clover, cg = 0.36f + 0.18f * k + 0.08f * dry, cb = 0.14f + 0.06f * k + 0.02f * dry;
            float blade = (blades - 0.5f) * 0.16f;
            return new[] { cr + blade * 0.7f, cg + blade, cb + blade * 0.5f };
        });
        public static Image ForestFloor(int n = 256) => Make(n, (u, v) => { float k = Fbm(u, v, 8, 4, 5), s = Noise(u * 64, v * 64, 64, 6); return new[] { 0.17f + 0.14f * k + 0.04f * s, 0.14f + 0.10f * k, 0.08f + 0.05f * k }; });
        public static Image Field(int n = 256) => Make(n, (u, v) =>
        {
            float furrow = 0.5f + 0.5f * (float)Math.Sin(u * Math.PI * 2 * 24), k = Fbm(u, v, 5, 3, 8);
            return new[] { 0.45f + 0.12f * k - 0.05f * furrow, 0.42f + 0.10f * k - 0.04f * furrow, 0.22f + 0.06f * k };
        });
        public static Image UrbanGround(int n = 256) => Make(n, (u, v) => { float k = Fbm(u, v, 10, 4, 11); return new[] { 0.52f + 0.1f * k, 0.50f + 0.1f * k, 0.46f + 0.09f * k }; });
        public static Image Rock(int n = 256) => Make(n, (u, v) =>
        {
            float k = Fbm(u, v, 5, 5, 21), c = Fbm(u, v, 14, 3, 22); float cr = Math.Abs(Noise(u * 12, v * 12, 12, 23) - 0.5f) < 0.03f ? 0.6f : 1f; k *= 0.8f;
            return new[] { (0.40f + 0.22f * k) * cr, (0.38f + 0.20f * k) * cr, (0.35f + 0.18f * k + 0.04f * c) * cr };
        });
        public static Image Snow(int n = 256) => Make(n, (u, v) => { float k = Fbm(u, v, 6, 3, 31); return new[] { 0.90f + 0.08f * k, 0.93f + 0.06f * k, 0.97f + 0.03f * k }; });
        public static Image Gravel(int n = 256) => Make(n, (u, v) => { float k = Fbm(u, v, 20, 3, 41), s = Noise(u * 80, v * 80, 80, 42); return new[] { 0.50f + 0.15f * k + 0.1f * s, 0.47f + 0.14f * k + 0.1f * s, 0.42f + 0.12f * k + 0.08f * s }; });
        public static Image Mud(int n = 256) => Make(n, (u, v) => { float k = Fbm(u, v, 7, 4, 51); return new[] { 0.28f + 0.1f * k, 0.22f + 0.08f * k, 0.14f + 0.05f * k }; });

        public static Image Asphalt(int n = 256) => Make(n, (u, v) =>
        {
            float k = Fbm(u, v, 16, 4, 61), g = Noise(u * 128, v * 128, 128, 62), cracks = Math.Abs(Noise(u * 10, v * 10, 10, 63) - 0.5f) < 0.004f ? 0.82f : 1f;
            float c = (0.34f + 0.10f * k + 0.07f * g) * cracks;
            return new[] { c, c, c * 1.02f };
        });
        public static Image Pavement(int n = 256) => Make(n, (u, v) =>
        {
            float px = u * 4, py = v * 4; float fx = px - (float)Math.Floor(px), fy = py - (float)Math.Floor(py);
            float joint = (fx < 0.03f || fy < 0.03f) ? 0.65f : 1f; float k = Hash((int)px, (int)py, 71);
            float c = (0.56f + 0.07f * k + 0.05f * Fbm(u, v, 24, 2, 72)) * joint;
            return new[] { c, c * 0.98f, c * 0.94f };
        });
        public static Image Gravel2Rail(int n = 256) => Gravel(n);

        // ---------------- roofs
        public static Image RoofTiles(int n = 256) => Make(n, (u, v) =>
        {
            int rows = 8, cols = 8;
            float ry = v * rows, row = (float)Math.Floor(ry), fy = ry - row;
            float cx = u * cols + (((int)row & 1) == 0 ? 0 : 0.5f), col = (float)Math.Floor(cx), fx = cx - col;
            float tint = Hash((int)col, (int)row, 81);
            float shade = 0.70f + 0.30f * fy;      // lower edge of each tile row is lit
            float groove = (fx < 0.06f || fx > 0.94f) ? 0.72f : 1f;
            float c = (0.82f + 0.18f * tint) * shade * groove;
            return new[] { c, c, c };
        });
        public static Image FlatRoof(int n = 256) => Make(n, (u, v) => { float k = Fbm(u, v, 12, 4, 91), g = Noise(u * 100, v * 100, 100, 92); float c = 0.70f + 0.14f * k + 0.10f * g; return new[] { c, c, c * 0.98f }; });
        public static Image MetalRoof(int n = 256) => Make(n, (u, v) => { float seam = (float)Math.Abs(Math.Sin(u * Math.PI * 12)); float c = 0.65f + 0.2f * (seam > 0.97f ? 1 : 0) + 0.06f * Fbm(u, v, 8, 2, 95); return new[] { c, c, c * 1.04f }; });

        // ---------------- facades (module: 3.2 m wide × 3.0 m high, one window per tile)
        public static Image FacadeResidential(int n = 256) => Facade(n, 0);
        public static Image FacadeCommercial(int n = 256) => Facade(n, 1);
        public static Image FacadeIndustrial(int n = 256) => Facade(n, 2);

        static Image Facade(int n, int type)
        {
            var im = new Image(n, n);
            // plaster base
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n, v = (y + 0.5f) / n, k = Fbm(u, v, 8, 4, 100 + type) * 0.10f + Noise(u * 120, v * 120, 120, 101) * 0.04f;
                float baseC = type == 2 ? 0.66f : 0.80f;
                float c = baseC - 0.05f + k;
                if (type == 2) c *= (0.9f + 0.1f * (float)Math.Sin(u * Math.PI * 2 * 16));   // corrugated cladding
                im.Set(x, y, c, c, c * 0.985f);
            }
            float W = n, H = n;
            if (type == 0)
            {   // punched window with frame, mullions, sill, lintel
                float wx0 = 0.22f * W, wx1 = 0.78f * W, wy0 = 0.20f * H, wy1 = 0.80f * H;
                im.FillRect(wx0 - 7, wy0 - 7, wx1 + 7, wy1 + 7, 0.45f, 0.43f, 0.40f, 0.35f);          // soft reveal shadow
                im.FillRect(wx0 - 4, wy0 - 4, wx1 + 4, wy1 + 4, 0.93f, 0.93f, 0.90f);                  // frame
                Glass(im, wx0, wy0, wx1, wy1);
                im.FillRect((wx0 + wx1) / 2 - 2.5f, wy0, (wx0 + wx1) / 2 + 2.5f, wy1, 0.93f, 0.93f, 0.90f);   // vertical mullion
                im.FillRect(wx0, wy0 + 0.62f * (wy1 - wy0) - 2.5f, wx1, wy0 + 0.62f * (wy1 - wy0) + 2.5f, 0.93f, 0.93f, 0.90f);
                im.FillRect(wx0 - 8, wy0 - 9, wx1 + 8, wy0 - 4, 0.78f, 0.76f, 0.72f);                 // sill (stone)
                im.FillRect(wx0 - 5, wy1 + 4, wx1 + 5, wy1 + 8, 0.85f, 0.84f, 0.80f);
            }
            else if (type == 1)
            {   // wide glazed band between piers
                float wx0 = 0.08f * W, wx1 = 0.92f * W, wy0 = 0.18f * H, wy1 = 0.84f * H;
                im.FillRect(wx0 - 3, wy0 - 3, wx1 + 3, wy1 + 3, 0.30f, 0.30f, 0.32f);
                Glass(im, wx0, wy0, wx1, wy1);
                for (int k = 1; k < 3; k++) im.FillRect(wx0 + k * (wx1 - wx0) / 3 - 2, wy0, wx0 + k * (wx1 - wx0) / 3 + 2, wy1, 0.30f, 0.30f, 0.32f);
                im.FillRect(0, 0, W, 0.07f * H, 0.55f, 0.55f, 0.56f);                                  // floor slab band
            }
            else
            {   // industrial: ribbon of small windows high up
                Glass(im, 0.10f * W, 0.60f * H, 0.90f * W, 0.82f * H);
                for (int k = 1; k < 4; k++) im.FillRect(0.10f * W + k * 0.2f * W - 1.5f, 0.60f * H, 0.10f * W + k * 0.2f * W + 1.5f, 0.82f * H, 0.5f, 0.5f, 0.52f);
            }
            return im;
        }

        static void Glass(Image im, float x0, float y0, float x1, float y1)
        {
            for (int y = (int)y0; y < (int)y1; y++)
                for (int x = (int)x0; x < (int)x1; x++)
                {
                    float t = (y - y0) / (y1 - y0), s = (x - x0) / (x1 - x0);
                    float refl = 0.55f + 0.30f * t + 0.12f * (float)Math.Sin((s + t) * 6);         // sky reflection getting brighter to the top
                    im.Set(x, y, 0.20f + 0.16f * refl, 0.30f + 0.22f * refl, 0.40f + 0.28f * refl);
                }
        }

        // ---------------- water
        public static Image Water(int n = 256) => Make(n, (u, v) =>
        {
            float r1 = Fbm(u, v, 8, 4, 111), r2 = Fbm(u + 0.3f, v + 0.7f, 16, 3, 112);
            float k = 0.6f * r1 + 0.4f * r2;
            return new[] { 0.10f + 0.10f * k, 0.30f + 0.18f * k, 0.34f + 0.16f * k, 0.78f };
        });

        // ---------------- decals: POLIZEI + plate. Atlas 512×256: row y∈[0.5,1] = lettering (dark blue on transparent) ...
        public static Image DecalAtlas()
        {
            var im = new Image(512, 256);
            DrawText(im, "POLIZEI", 6, 134, 500, 116, 0.05f, 0.14f, 0.45f, false);   // upper half: normal reading direction
            DrawText(im, "POLIZEI", 6, 134, 500, 116, 0.05f, 0.14f, 0.45f, false);
            return im;
        }

        /// <summary>Swiss style plate (white, black letters, red/white canton shield placeholder – no real coat of arms).</summary>
        public static Image Plate(string text)
        {
            var im = new Image(256, 64);
            im.FillRect(0, 0, 256, 64, 0.97f, 0.97f, 0.97f);
            im.FillRect(0, 0, 256, 3, 0.1f, 0.1f, 0.1f); im.FillRect(0, 61, 256, 64, 0.1f, 0.1f, 0.1f); im.FillRect(0, 0, 3, 64, 0.1f, 0.1f, 0.1f); im.FillRect(253, 0, 256, 64, 0.1f, 0.1f, 0.1f);
            im.FillRect(14, 10, 38, 54, 0.78f, 0.1f, 0.1f);       // generic shield placeholder
            im.FillRect(21, 14, 31, 50, 1f, 1f, 1f); im.FillRect(14, 28, 38, 36, 1f, 1f, 1f);
            DrawText(im, text, 48, 10, 200, 44, 0.05f, 0.05f, 0.05f, true);
            return im;
        }

        // 5×7 bitmap font (A-Z, 0-9, space) — enough for lettering and plates
        static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
        {
            ['A'] = new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" }, ['B'] = new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" },
            ['C'] = new[] { "01110", "10001", "10000", "10000", "10000", "10001", "01110" }, ['D'] = new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" },
            ['E'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" }, ['F'] = new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" },
            ['G'] = new[] { "01110", "10001", "10000", "10111", "10001", "10001", "01111" }, ['H'] = new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" },
            ['I'] = new[] { "01110", "00100", "00100", "00100", "00100", "00100", "01110" }, ['J'] = new[] { "00111", "00010", "00010", "00010", "00010", "10010", "01100" },
            ['K'] = new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" }, ['L'] = new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
            ['M'] = new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" }, ['N'] = new[] { "10001", "11001", "10101", "10011", "10001", "10001", "10001" },
            ['O'] = new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" }, ['P'] = new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" },
            ['Q'] = new[] { "01110", "10001", "10001", "10001", "10101", "10010", "01101" }, ['R'] = new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
            ['S'] = new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" }, ['T'] = new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" },
            ['U'] = new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" }, ['V'] = new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" },
            ['W'] = new[] { "10001", "10001", "10001", "10101", "10101", "11011", "10001" }, ['X'] = new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" },
            ['Y'] = new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" }, ['Z'] = new[] { "11111", "00001", "00010", "00100", "01000", "10000", "11111" },
            ['0'] = new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" }, ['1'] = new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
            ['2'] = new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" }, ['3'] = new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" },
            ['4'] = new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" }, ['5'] = new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" },
            ['6'] = new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" }, ['7'] = new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
            ['8'] = new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" }, ['9'] = new[] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" },
            [' '] = new[] { "00000", "00000", "00000", "00000", "00000", "00000", "00000" }, ['-'] = new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" },
            ['.'] = new[] { "00000", "00000", "00000", "00000", "00000", "01100", "01100" }, [':'] = new[] { "00000", "01100", "01100", "00000", "01100", "01100", "00000" },
            ['/'] = new[] { "00001", "00010", "00010", "00100", "01000", "01000", "10000" }
        };

        public static void DrawText(Image im, string text, float x, float y, float w, float h, float r, float g, float b, bool bold)
        {
            text = text.ToUpperInvariant(); int n = text.Length; if (n == 0) return;
            float cw = w / n, ch = h;
            for (int i = 0; i < n; i++)
            {
                if (!Font.TryGetValue(text[i], out var glyph)) continue;
                float gx = x + i * cw, px = cw * 0.8f / 5f, py = ch / 7f;
                for (int row = 0; row < 7; row++)
                    for (int col = 0; col < 5; col++)
                        if (glyph[row][col] == '1')
                        {
                            float x0 = gx + col * px, y0 = y + (6 - row) * py;
                            im.FillRect(x0, y0, x0 + px + (bold ? 0.6f : 0.2f), y0 + py + 0.2f, r, g, b, 1f);
                        }
            }
        }
    }
}
