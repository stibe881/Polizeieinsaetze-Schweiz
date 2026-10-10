using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using SwissPoliceSim.Core.Geo;

namespace SwissPoliceSim.Core.Storage
{
    /// <summary>
    /// One file per 1 km tile holding independently deflate-compressed chunks (TERR, ROAD, BLDG, WATR, TREE, LAND, POIN, RAIL).
    /// Layout: "SPW2", int X, int Y, int chunkCount, then per chunk: 4 ASCII id bytes, int rawLength, int storedLength, stored bytes.
    /// </summary>
    public sealed class TileContainer
    {
        public const string Magic = "SPW2";
        public TileId Tile;
        public readonly Dictionary<string, byte[]> Chunks = new Dictionary<string, byte[]>();

        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes(Magic)); w.Write(Tile.X); w.Write(Tile.Y); w.Write(Chunks.Count);
                foreach (var kv in Chunks)
                {
                    byte[] stored;
                    using (var o = new MemoryStream())
                    {
                        using (var d = new DeflateStream(o, CompressionLevel.Optimal, true)) d.Write(kv.Value, 0, kv.Value.Length);
                        stored = o.ToArray();
                    }
                    w.Write(Encoding.ASCII.GetBytes(kv.Key.PadRight(4).Substring(0, 4)));
                    w.Write(kv.Value.Length); w.Write(stored.Length); w.Write(stored);
                }
                w.Flush();
                return ms.ToArray();
            }
        }

        public static TileContainer Deserialize(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != Magic) throw new InvalidDataException("Not a tile container");
                var c = new TileContainer { Tile = new TileId(r.ReadInt32(), r.ReadInt32()) };
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    string id = Encoding.ASCII.GetString(r.ReadBytes(4)).TrimEnd();
                    int raw = r.ReadInt32(), stored = r.ReadInt32();
                    var comp = r.ReadBytes(stored);
                    var buf = new byte[raw];
                    using (var d = new DeflateStream(new MemoryStream(comp), CompressionMode.Decompress))
                    {
                        int off = 0; while (off < raw) { int k = d.Read(buf, off, raw - off); if (k <= 0) break; off += k; }
                    }
                    c.Chunks[id] = buf;
                }
                return c;
            }
        }

        public bool TryGet(string id, out byte[] data) => Chunks.TryGetValue(id, out data);
    }

    /// <summary>Where tile containers come from (loose files for development, region packs for distribution).</summary>
    public interface ITileSource
    {
        bool TryRead(TileId tile, out byte[] container);
        IEnumerable<TileId> Tiles { get; }
    }

    public sealed class LooseTileSource : ITileSource
    {
        readonly string _dir;
        public LooseTileSource(string dir) { _dir = dir; }
        public static string FileName(TileId t) => $"{t.X}-{t.Y}.sptile";
        public bool TryRead(TileId t, out byte[] data)
        {
            string p = Path.Combine(_dir, FileName(t));
            if (!File.Exists(p)) { data = null; return false; }
            data = File.ReadAllBytes(p); return true;
        }
        public IEnumerable<TileId> Tiles
        {
            get
            {
                if (!Directory.Exists(_dir)) yield break;
                foreach (var f in Directory.GetFiles(_dir, "*.sptile"))
                {
                    var n = Path.GetFileNameWithoutExtension(f).Split('-');
                    if (n.Length == 2 && int.TryParse(n[0], out int x) && int.TryParse(n[1], out int y)) yield return new TileId(x, y);
                }
            }
        }
    }

    /// <summary>
    /// Region pack: many tiles in one file with an index, so a country-sized world is a few hundred files
    /// (e.g. 20 × 20 km regions) that can be downloaded / mounted / Addressables-hosted individually.
    /// Layout: "SPRG", int count, then index entries (int X, int Y, long offset, int length), then the tile blobs.
    /// </summary>
    public sealed class RegionPackSource : ITileSource
    {
        public const string Magic = "SPRG";
        readonly string _path; readonly Dictionary<TileId, (long off, int len)> _index = new Dictionary<TileId, (long, int)>();

        public RegionPackSource(string path)
        {
            _path = path;
            using (var f = File.OpenRead(path))
            using (var r = new BinaryReader(f))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != Magic) throw new InvalidDataException("Not a region pack");
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++) _index[new TileId(r.ReadInt32(), r.ReadInt32())] = (r.ReadInt64(), r.ReadInt32());
            }
        }

        public bool TryRead(TileId t, out byte[] data)
        {
            if (!_index.TryGetValue(t, out var e)) { data = null; return false; }
            using (var f = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                f.Seek(e.off, SeekOrigin.Begin);
                data = new byte[e.len]; int off = 0;
                while (off < e.len) { int k = f.Read(data, off, e.len - off); if (k <= 0) throw new EndOfStreamException(); off += k; }
            }
            return true;
        }

        public IEnumerable<TileId> Tiles => _index.Keys;

        public static void Write(string path, IDictionary<TileId, byte[]> tiles)
        {
            using (var f = File.Create(path))
            using (var w = new BinaryWriter(f))
            {
                w.Write(Encoding.ASCII.GetBytes(Magic)); w.Write(tiles.Count);
                long headerSize = 8 + (long)tiles.Count * 20, off = headerSize;
                foreach (var kv in tiles) { w.Write(kv.Key.X); w.Write(kv.Key.Y); w.Write(off); w.Write(kv.Value.Length); off += kv.Value.Length; }
                foreach (var kv in tiles) w.Write(kv.Value);
            }
        }
    }

    /// <summary>Looks in several sources in order (e.g. a downloaded region pack first, loose development files second).</summary>
    public sealed class CompositeTileSource : ITileSource
    {
        readonly List<ITileSource> _s;
        public CompositeTileSource(params ITileSource[] s) { _s = new List<ITileSource>(s); }
        public bool TryRead(TileId t, out byte[] d) { foreach (var s in _s) if (s.TryRead(t, out d)) return true; d = null; return false; }
        public IEnumerable<TileId> Tiles { get { var seen = new HashSet<TileId>(); foreach (var s in _s) foreach (var t in s.Tiles) if (seen.Add(t)) yield return t; } }
    }
}
