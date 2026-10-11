using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Storage;

namespace SwissPoliceSim.Core.Terrain
{
    /// <summary>
    /// Far-view terrain: blocks of 8×8 tiles (8 km) with 129×129 height samples (62.5 m) and a 128×128 land-use raster. Built from the coarse
    /// TERC / LAND chunks of the tiles, ≈ 25 KB per 64 km² → the whole country needs < 15 MB and is kept resident for the horizon ring.
    /// </summary>
    public sealed class MacroBlock
    {
        public const int BlockTiles = 8, Samples = 129, LandRes = 128;
        public const double BlockSize = BlockTiles * 1000.0;
        public int BX, BY;                       // block index = floor(tileX/8), floor(tileY/8)
        public TerrainTileData Heights;          // Resolution 129, Tile unused
        public byte[] Land;                      // 128×128 land-use classes, row 0 = south
        public bool[] Present;                   // 8×8 tiles that exist (row-major, y*8+x)

        public double MinE => BX * BlockSize; public double MinN => BY * BlockSize;
        public double Spacing => BlockSize / (Samples - 1);

        public static (int, int) BlockOf(TileId t) => ((int)Math.Floor(t.X / (double)BlockTiles), (int)Math.Floor(t.Y / (double)BlockTiles));
        public static string FileName(int bx, int by) => $"{bx}_{by}.spm";

        public float Height(int col, int row) => Heights.Height(col, row);

        public void Write(Stream s)
        {
            using (var w = new BinaryWriter(s, System.Text.Encoding.UTF8, true))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("SPM1")); w.Write(BX); w.Write(BY);
                ulong mask = 0; for (int i = 0; i < 64; i++) if (Present[i]) mask |= 1UL << i; w.Write(mask);
                var h = Heights.ToBytes(); w.Write(h.Length); w.Write(h); w.Write(Land);
            }
        }

        public static MacroBlock Read(Stream s)
        {
            using (var r = new BinaryReader(s, System.Text.Encoding.UTF8, true))
            {
                if (System.Text.Encoding.ASCII.GetString(r.ReadBytes(4)) != "SPM1") throw new InvalidDataException("not a macro block");
                var b = new MacroBlock { BX = r.ReadInt32(), BY = r.ReadInt32(), Present = new bool[64] };
                ulong mask = r.ReadUInt64(); for (int i = 0; i < 64; i++) b.Present[i] = (mask >> i & 1) != 0;
                int n = r.ReadInt32(); using (var ms = new MemoryStream(r.ReadBytes(n))) b.Heights = TerrainTileData.Read(ms);
                b.Land = r.ReadBytes(LandRes * LandRes); return b;
            }
        }

        /// <summary>Bilinear height at LV95 coordinates inside this block.</summary>
        public float Sample(double e, double n)
        {
            double fx = Math.Min(Math.Max((e - MinE) / Spacing, 0), Samples - 1.0001), fz = Math.Min(Math.Max((n - MinN) / Spacing, 0), Samples - 1.0001);
            int c = (int)fx, r = (int)fz; double tx = fx - c, tz = fz - r;
            double h0 = Height(c, r) + (Height(c + 1, r) - Height(c, r)) * tx, h1 = Height(c, r + 1) + (Height(c + 1, r + 1) - Height(c, r + 1)) * tx;
            return (float)(h0 + (h1 - h0) * tz);
        }
    }

    public static class MacroTerrainBuilder
    {
        /// <summary>Builds all macro blocks that contain at least one tile and writes them to outDir. Returns the number of blocks.</summary>
        public static int Build(ITileSource src, string outDir, Action<string> log = null)
        {
            Directory.CreateDirectory(outDir);
            var blocks = new Dictionary<(int, int), List<TileId>>();
            foreach (var t in src.Tiles) { var k = MacroBlock.BlockOf(t); if (!blocks.TryGetValue(k, out var l)) blocks[k] = l = new List<TileId>(); l.Add(t); }
            foreach (var kv in blocks)
            {
                var b = BuildBlock(src, kv.Key.Item1, kv.Key.Item2, kv.Value);
                string path = Path.Combine(outDir, MacroBlock.FileName(b.BX, b.BY));
                using (var fs = File.Create(path)) b.Write(fs);
                log?.Invoke($"macro {b.BX},{b.BY}: {kv.Value.Count} tiles, {new FileInfo(path).Length / 1024} KB");
            }
            return blocks.Count;
        }

        public static MacroBlock BuildBlock(ITileSource src, int bx, int by, List<TileId> tiles)
        {
            const int S = MacroBlock.Samples, T = MacroBlock.BlockTiles;
            var h = new float[S * S]; var have = new bool[S * S]; var land = new byte[MacroBlock.LandRes * MacroBlock.LandRes]; var present = new bool[64];
            float min = float.MaxValue;
            foreach (var t in tiles)
            {
                if (!src.TryRead(t, out var bytes)) continue;
                var c = TileContainer.Deserialize(bytes); int tx = t.X - bx * T, ty = t.Y - by * T; present[ty * T + tx] = true;
                if (c.TryGet("TERC", out var terc))
                {
                    TerrainTileData td; using (var ms = new MemoryStream(terc)) td = TerrainTileData.Read(ms);
                    int step = (td.Resolution - 1) / 16;                    // 65 → 4: 16 macro samples per tile
                    for (int r = 0; r <= 16; r++) for (int cc = 0; cc <= 16; cc++)
                    {
                        int gx = tx * 16 + cc, gy = ty * 16 + r; if (gx >= S || gy >= S) continue;
                        float v = td.Height(Math.Min(td.Resolution - 1, cc * step), Math.Min(td.Resolution - 1, r * step)); h[gy * S + gx] = v; have[gy * S + gx] = true; if (v < min) min = v;
                    }
                }
                if (c.TryGet("LAND", out var lb) && lb.Length >= 128 * 128)
                    for (int r = 0; r < 16; r++) for (int cc = 0; cc < 16; cc++) land[(ty * 16 + r) * MacroBlock.LandRes + tx * 16 + cc] = lb[(r * 8 + 4) * 128 + cc * 8 + 4];
            }
            if (min == float.MaxValue) min = 0;
            for (int i = 0; i < h.Length; i++) if (!have[i]) h[i] = min - 5;     // missing tiles: flat low plain (never visible when the real tile streams in)
            float lo = float.MaxValue, hi = float.MinValue; foreach (var v in h) { if (v < lo) lo = v; if (v > hi) hi = v; }
            var td2 = new TerrainTileData { Tile = new TileId(bx, by), Resolution = S, Q = new ushort[S * S] };
            td2.MinH = (float)(Math.Floor(lo / TerrainTileData.Step) * TerrainTileData.Step); td2.MaxH = td2.MinH + 65535 * TerrainTileData.Step;
            if (hi - lo > 65534 * TerrainTileData.Step) throw new InvalidOperationException("macro block relief too large");
            for (int i = 0; i < h.Length; i++) td2.Q[i] = (ushort)Math.Round((h[i] - td2.MinH) / TerrainTileData.Step);
            return new MacroBlock { BX = bx, BY = by, Heights = td2, Land = land, Present = present };
        }
    }
}
