using System;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Terrain;

namespace SwissPoliceSim.Core.Meshing
{
    /// <summary>
    /// Terrain surface of one tile as a MeshData (tile-local metres, y = m a.s.l.). Vertex colour = tint of the land-use class;
    /// Uv2 = (rock weight, paved weight) – the remainder is grass/soil. The terrain shader blends its three detail layers with these weights and adds snow by altitude.
    /// A skirt hides cracks between tiles of different level of detail.
    /// </summary>
    public static class TerrainMesher
    {
        public const float SkirtDepth = 5f;
        /// <summary>Das Terrain liegt 0.3 m unter dem Hoehenraster, damit Strassen/Trottoirs (nur 0.1 m darueber, aber auf der Mittellinienhoehe) nie vom Gelaendedreieck verdeckt werden.</summary>
        public const float RoadClearance = 0.3f;

        struct Style { public byte R, G, B; public float Rock, Paved; public Style(byte r, byte g, byte b, float rock, float paved) { R = r; G = g; B = b; Rock = rock; Paved = paved; } }

        static Style StyleOf(byte cls)
        {
            switch ((LandUseClass)cls)
            {
                case LandUseClass.Forest: return new Style(72, 100, 56, 0, 0);
                case LandUseClass.Farmland: return new Style(150, 135, 85, 0, 0);
                case LandUseClass.Orchard: return new Style(100, 135, 70, 0, 0);
                case LandUseClass.Vineyard: return new Style(115, 130, 70, 0, 0);
                case LandUseClass.Residential: return new Style(105, 125, 80, 0, 0.05f);
                case LandUseClass.Commercial: return new Style(135, 133, 128, 0, 0.55f);
                case LandUseClass.Industrial: return new Style(125, 124, 120, 0, 0.65f);
                case LandUseClass.Park: case LandUseClass.Sports: return new Style(190, 215, 150, 0, 0);
                case LandUseClass.Cemetery: return new Style(95, 125, 75, 0, 0.1f);
                case LandUseClass.Rock: return new Style(135, 130, 122, 1f, 0);
                case LandUseClass.Scree: return new Style(150, 144, 134, 1f, 0);
                case LandUseClass.Glacier: return new Style(235, 242, 250, 0.2f, 0);
                case LandUseClass.Wetland: return new Style(85, 110, 70, 0, 0);
                case LandUseClass.Sand: return new Style(185, 168, 120, 0, 0.2f);
                case LandUseClass.Parking: case LandUseClass.Plaza: return new Style(115, 114, 110, 0, 1f);
                case LandUseClass.Railway: return new Style(105, 100, 92, 0.3f, 0.3f);
                case LandUseClass.Urban: return new Style(120, 118, 113, 0, 0.8f);
                case LandUseClass.Water: return new Style(70, 64, 50, 0, 0);
                default: return new Style(100, 130, 72, 0, 0);
            }
        }

        /// <param name="landUse">128×128 class raster (row 0 = south) or null</param>
        public static MeshData Build(TerrainTileData d, byte[] landUse, int stride = 1)
        {
            int res = d.Resolution, n = (res - 1) / stride + 1; float sp = d.Spacing * stride;
            // gröbere Raster weichen am Hang stärker von der fein abgetasteten Strasse ab: je Stufe tiefer legen, sonst verdeckt das Gelände die Strassen (flackernde Strassen)
            float clearance = RoadClearance + 0.6f * (stride - 1);
            var m = new MeshData(1); int total = n * n;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int c = Math.Min(res - 1, i * stride), r = Math.Min(res - 1, j * stride);
                    float x = c * d.Spacing, z = r * d.Spacing, y = d.Height(c, r) - clearance;
                    float hx = d.Height(Math.Min(res - 1, c + 1), r) - d.Height(Math.Max(0, c - 1), r), hz = d.Height(c, Math.Min(res - 1, r + 1)) - d.Height(c, Math.Max(0, r - 1));
                    float nx = -hx, nz = -hz, ny = 2 * d.Spacing, l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    byte cls = 0;
                    if (landUse != null && landUse.Length >= 128 * 128) cls = landUse[Math.Min(127, (int)(z / 7.8125f)) * 128 + Math.Min(127, (int)(x / 7.8125f))];
                    var st = StyleOf(cls);
                    float slope = 1f - ny / l; float rock = Math.Max(st.Rock, Math.Min(1f, Math.Max(0f, (slope - 0.20f) * 3.2f)));
                    // soft noise so that tint boundaries of the 7.8 m raster do not look like a grid
                    float v = 0.92f + 0.16f * (float)(Math.Sin(x * 0.173 + z * 0.091) * Math.Cos(z * 0.137 - x * 0.057) * 0.5 + 0.5);
                    int vi = m.AddVertex(x, y, z, nx / l, ny / l, nz / l, (float)(d.Tile.X * 1000.0 + x) * 0.1f, (float)(d.Tile.Y * 1000.0 + z) * 0.1f,
                        (byte)Math.Min(255, st.R * v), (byte)Math.Min(255, st.G * v), (byte)Math.Min(255, st.B * v));
                    m.SetUv2(vi, rock, Math.Min(1f - rock, st.Paved));
                }
            for (int j = 0; j < n - 1; j++)
                for (int i = 0; i < n - 1; i++)
                {
                    int a = j * n + i, b = a + 1, c = a + n, e = c + 1;
                    m.AddTriangle(0, a, c, b); m.AddTriangle(0, b, c, e);
                }
            // skirts along the four borders (facing outwards)
            AddSkirt(m, n, 0, true, false); AddSkirt(m, n, 0, false, false); AddSkirt(m, n, n - 1, true, true); AddSkirt(m, n, n - 1, false, true);
            return m;
        }

        /// <summary>Horizon terrain of an 8 km macro block (129×129 samples), block-local coordinates, lowered by <paramref name="drop"/> m so the real tiles always cover it.</summary>
        public static MeshData BuildMacro(MacroBlock b, float drop = 10f)
        {
            int n = MacroBlock.Samples; float sp = (float)b.Spacing; var m = new MeshData(1);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float x = i * sp, z = j * sp, y = b.Height(i, j) - drop;
                    float hx = b.Height(Math.Min(n - 1, i + 1), j) - b.Height(Math.Max(0, i - 1), j), hz = b.Height(i, Math.Min(n - 1, j + 1)) - b.Height(i, Math.Max(0, j - 1));
                    float nx = -hx, nz = -hz, ny = 2 * sp, l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    byte cls = b.Land[Math.Min(127, (int)(z / 62.5f)) * 128 + Math.Min(127, (int)(x / 62.5f))];
                    var st = StyleOf(cls); float slope = 1f - ny / l; float rock = Math.Max(st.Rock, Math.Min(1f, Math.Max(0f, (slope - 0.22f) * 3f)));
                    int vi = m.AddVertex(x, y, z, nx / l, ny / l, nz / l, (float)(b.MinE + x) * 0.1f, (float)(b.MinN + z) * 0.1f, st.R, st.G, st.B);
                    m.SetUv2(vi, rock, Math.Min(1f - rock, st.Paved));
                }
            for (int j = 0; j < n - 1; j++) for (int i = 0; i < n - 1; i++) { int a = j * n + i, bb = a + 1, c = a + n, e = c + 1; m.AddTriangle(0, a, c, bb); m.AddTriangle(0, bb, c, e); }
            return m;
        }

        static void AddSkirt(MeshData m, int n, int fixedIndex, bool alongX, bool far)
        {
            int first = m.VertexCount;
            for (int k = 0; k < n; k++)
            {
                int vi = alongX ? fixedIndex * n + k : k * n + fixedIndex;
                float x = m.Positions[vi * 3], y = m.Positions[vi * 3 + 1], z = m.Positions[vi * 3 + 2];
                float nx = 0, nz = 0; if (alongX) nz = far ? 1 : -1; else nx = far ? 1 : -1;
                byte r = m.Colors[vi * 4], g = m.Colors[vi * 4 + 1], b = m.Colors[vi * 4 + 2];
                m.AddVertex(x, y, z, nx, 0, nz, m.Uvs[vi * 2], m.Uvs[vi * 2 + 1], r, g, b);
                m.AddVertex(x, y - SkirtDepth, z, nx, 0, nz, m.Uvs[vi * 2], m.Uvs[vi * 2 + 1], r, g, b);
            }
            for (int k = 0; k < n - 1; k++)
            {
                int a = first + k * 2, b = a + 2, c = a + 1, e = a + 3;       // a,b top; c,e bottom
                bool flip = alongX ? far : !far;
                if (!flip) { m.AddTriangle(0, a, b, c); m.AddTriangle(0, b, e, c); } else { m.AddTriangle(0, a, c, b); m.AddTriangle(0, b, c, e); }
            }
        }
    }
}
