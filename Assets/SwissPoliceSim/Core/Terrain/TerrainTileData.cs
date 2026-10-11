using System;
using System.IO;
using SwissPoliceSim.Core.Geo;

namespace SwissPoliceSim.Core.Terrain
{
    /// <summary>
    /// Baked terrain of one 1 km tile: (Resolution x Resolution) samples incl. both tile borders
    /// (spacing = 1000/(Resolution-1) m). Heights quantised to 16 bit between MinH and MaxH.
    /// Row 0 = south, column 0 = west; matches Unity's TerrainData.SetHeights(float[z, x]).
    /// File: magic "SPT1", int X, int Y, int res, float minH, float maxH, ushort[res*res].
    /// </summary>
    public sealed class TerrainTileData
    {
        public const string Magic = "SPT1", MagicV2 = "SPT2";
        /// <summary>Vertical quantisation of baked tiles: 5 cm (range up to 3276 m per tile).</summary>
        public const float Step = 0.05f;
        public TileId Tile;
        public int Resolution;
        public float MinH, MaxH;
        public ushort[] Q;

        public float Spacing => (float)(TileGrid.TileSize / (Resolution - 1));

        public float Height(int col, int row)
        {
            float range = MaxH - MinH;
            return MinH + Q[row * Resolution + col] / 65535f * range;
        }

        /// <summary>Bilinear height at tile-local metres (x east, z north).</summary>
        public float SampleLocal(double x, double z)
        {
            double fx = Math.Min(Math.Max(x / Spacing, 0), Resolution - 1.0001);
            double fz = Math.Min(Math.Max(z / Spacing, 0), Resolution - 1.0001);
            int c = (int)fx, r = (int)fz;
            double tx = fx - c, tz = fz - r;
            double h0 = Height(c, r) + (Height(c + 1, r) - Height(c, r)) * tx;
            double h1 = Height(c, r + 1) + (Height(c + 1, r + 1) - Height(c, r + 1)) * tx;
            return (float)(h0 + (h1 - h0) * tz);
        }

        public static TerrainTileData Bake(IHeightSource src, TileId tile, int resolution = 257)
        {
            var d = new TerrainTileData { Tile = tile, Resolution = resolution, Q = new ushort[resolution * resolution] };
            var h = new float[resolution * resolution];
            double step = TileGrid.TileSize / (resolution - 1);
            double e0 = TileGrid.MinE(tile), n0 = TileGrid.MinN(tile);
            float min = float.MaxValue, max = float.MinValue;
            for (int r = 0; r < resolution; r++)
                for (int c = 0; c < resolution; c++)
                {
                    if (!src.TrySample(e0 + c * step, n0 + r * step, out float v))
                        throw new InvalidOperationException($"No height data for tile {tile} at col {c}, row {r}");
                    h[r * resolution + c] = v;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
            if (max - min > 65534 * Step) throw new InvalidOperationException($"relief of tile {tile} exceeds {65534 * Step} m");
            d.MinH = (float)(Math.Floor(min / Step) * Step); d.MaxH = d.MinH + 65535 * Step;
            for (int i = 0; i < h.Length; i++)
                d.Q[i] = (ushort)Math.Round((h[i] - d.MinH) / Step);
            return d;
        }

        /// <summary>Keeps every factor-th sample (factor must divide Resolution-1): 257 → 65 with factor 4.</summary>
        public TerrainTileData Downsample(int factor)
        {
            int n = (Resolution - 1) / factor + 1; var o = new TerrainTileData { Tile = Tile, Resolution = n, MinH = MinH, MaxH = MaxH, Q = new ushort[n * n] };
            for (int r = 0; r < n; r++) for (int c = 0; c < n; c++) o.Q[r * n + c] = Q[(r * factor) * Resolution + c * factor];
            return o;
        }

        /// <summary>V2 file: header + predictive (median edge detector) residuals, zig-zag varints. Deflated again by the tile container.</summary>
        public void Write(Stream s)
        {
            using (var w = new BinaryWriter(s, System.Text.Encoding.UTF8, true))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes(MagicV2));
                w.Write(Tile.X); w.Write(Tile.Y); w.Write(Resolution); w.Write(MinH); w.Write(MaxH);
                int n = Resolution;
                for (int r = 0; r < n; r++)
                    for (int c = 0; c < n; c++)
                    {
                        int e = Q[r * n + c] - Predict(Q, n, c, r);
                        uint z = (uint)((e << 1) ^ (e >> 31));
                        while (z >= 128) { w.Write((byte)(z | 128)); z >>= 7; }
                        w.Write((byte)z);
                    }
            }
        }

        static int Predict(ushort[] q, int n, int c, int r)
        {
            if (r == 0 && c == 0) return 0;
            int a = c > 0 ? q[r * n + c - 1] : 0, b = r > 0 ? q[(r - 1) * n + c] : 0;
            if (r == 0) return a; if (c == 0) return b;
            int d = q[(r - 1) * n + c - 1];
            return Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), a + b - d));
        }

        public byte[] ToBytes() { using (var ms = new MemoryStream()) { Write(ms); return ms.ToArray(); } }

        public static TerrainTileData Read(Stream s)
        {
            using (var r = new BinaryReader(s, System.Text.Encoding.UTF8, true))
            {
                string magic = System.Text.Encoding.ASCII.GetString(r.ReadBytes(4));
                if (magic != Magic && magic != MagicV2) throw new InvalidDataException("Not a terrain tile file");
                var d = new TerrainTileData { Tile = new TileId(r.ReadInt32(), r.ReadInt32()) };
                d.Resolution = r.ReadInt32();
                d.MinH = r.ReadSingle(); d.MaxH = r.ReadSingle();
                d.Q = new ushort[d.Resolution * d.Resolution];
                if (magic == Magic) { for (int i = 0; i < d.Q.Length; i++) d.Q[i] = r.ReadUInt16(); return d; }
                int n = d.Resolution;
                for (int row = 0; row < n; row++)
                    for (int c = 0; c < n; c++)
                    {
                        uint z = 0; int shift = 0; byte b;
                        do { b = r.ReadByte(); z |= (uint)(b & 127) << shift; shift += 7; } while ((b & 128) != 0);
                        int e = (int)(z >> 1) ^ -(int)(z & 1);
                        d.Q[row * n + c] = (ushort)(Predict(d.Q, n, c, row) + e);
                    }
                return d;
            }
        }
    }
}
