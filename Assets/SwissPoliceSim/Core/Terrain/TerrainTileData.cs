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
        public const string Magic = "SPT1";
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
            if (max - min < 0.01f) max = min + 0.01f;
            d.MinH = min; d.MaxH = max;
            float range = max - min;
            for (int i = 0; i < h.Length; i++)
                d.Q[i] = (ushort)Math.Round((h[i] - min) / range * 65535f);
            return d;
        }

        public void Write(Stream s)
        {
            using (var w = new BinaryWriter(s, System.Text.Encoding.UTF8, true))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes(Magic));
                w.Write(Tile.X); w.Write(Tile.Y); w.Write(Resolution);
                w.Write(MinH); w.Write(MaxH);
                for (int i = 0; i < Q.Length; i++) w.Write(Q[i]);
            }
        }

        public static TerrainTileData Read(Stream s)
        {
            using (var r = new BinaryReader(s, System.Text.Encoding.UTF8, true))
            {
                string magic = System.Text.Encoding.ASCII.GetString(r.ReadBytes(4));
                if (magic != Magic) throw new InvalidDataException("Not a terrain tile file");
                var d = new TerrainTileData { Tile = new TileId(r.ReadInt32(), r.ReadInt32()) };
                d.Resolution = r.ReadInt32();
                d.MinH = r.ReadSingle(); d.MaxH = r.ReadSingle();
                d.Q = new ushort[d.Resolution * d.Resolution];
                for (int i = 0; i < d.Q.Length; i++) d.Q[i] = r.ReadUInt16();
                return d;
            }
        }
    }
}
