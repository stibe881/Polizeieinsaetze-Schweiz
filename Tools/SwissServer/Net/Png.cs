using System;
using System.IO;
using System.IO.Compression;

namespace SwissServer.Net
{
    /// <summary>Minimal PNG encoder (RGBA8) for the map rasters served to the web UI.</summary>
    public static class Png
    {
        public static byte[] EncodeRgba(int w, int h, byte[] rgba)
        {
            using var ms = new MemoryStream(); ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var ihdr = new byte[13]; Be(ihdr, 0, w); Be(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 6; Chunk(ms, "IHDR", ihdr);
            using var raw = new MemoryStream(); for (int y = 0; y < h; y++) { raw.WriteByte(0); raw.Write(rgba, y * w * 4, w * 4); }
            using var z = new MemoryStream(); using (var zs = new ZLibStream(z, CompressionLevel.Fastest, true)) { raw.Position = 0; raw.CopyTo(zs); }
            Chunk(ms, "IDAT", z.ToArray()); Chunk(ms, "IEND", new byte[0]); return ms.ToArray();
        }
        static void Be(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; Be(len, 0, data.Length); s.Write(len); var t = System.Text.Encoding.ASCII.GetBytes(type); s.Write(t); s.Write(data);
            uint crc = Crc(t, data); var c = new byte[4]; Be(c, 0, (int)crc); s.Write(c);
        }
        static uint[] _tab; static uint Crc(byte[] a, byte[] b)
        {
            if (_tab == null) { _tab = new uint[256]; for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; _tab[n] = c; } }
            uint crc = 0xFFFFFFFF; foreach (var x in a) crc = _tab[(crc ^ x) & 0xFF] ^ (crc >> 8); foreach (var x in b) crc = _tab[(crc ^ x) & 0xFF] ^ (crc >> 8); return crc ^ 0xFFFFFFFF;
        }
    }
}
