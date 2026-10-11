using System;
using System.IO;
using System.Numerics;

/// <summary>Radiance .hdr (RGBE, RLE) lat-long panorama – the same Poly Haven sky the Unity game uses.</summary>
public sealed class Hdri
{
    public readonly int W, H; readonly float[] _px;
    public const float SunU = 0.5947f;      // horizontal position of the baked sun in the file (u), elevation 48 degrees

    Hdri(int w, int h, float[] px) { W = w; H = h; _px = px; }

    public static Hdri Load(string path)
    {
        var d = File.ReadAllBytes(path); int p = 0; int w = 0, h = 0;
        while (true) { int e = Array.IndexOf(d, (byte)'\n', p); string line = System.Text.Encoding.ASCII.GetString(d, p, e - p); p = e + 1; if (line.StartsWith("-Y")) { var t = line.Split(' '); h = int.Parse(t[1]); w = int.Parse(t[3]); break; } }
        var px = new float[w * h * 3]; var row = new byte[w * 4];
        for (int y = 0; y < h; y++)
        {
            if (d[p] != 2 || d[p + 1] != 2) throw new InvalidDataException("unsupported HDR encoding"); p += 4;
            for (int c = 0; c < 4; c++) { int x = 0; while (x < w) { int n = d[p++]; if (n > 128) { n -= 128; byte v = d[p++]; for (int i = 0; i < n; i++) row[(x++) * 4 + c] = v; } else { for (int i = 0; i < n; i++) row[(x++) * 4 + c] = d[p++]; } } }
            for (int x = 0; x < w; x++) { int e = row[x * 4 + 3]; float f = e == 0 ? 0 : (float)Math.Pow(2, e - 136); px[(y * w + x) * 3] = row[x * 4] * f; px[(y * w + x) * 3 + 1] = row[x * 4 + 1] * f; px[(y * w + x) * 3 + 2] = row[x * 4 + 2] * f; }
        }
        return new Hdri(w, h, px);
    }

    /// <summary>Radiance in direction <paramref name="d"/> (Y up), rotated so the baked sun lies at the azimuth of <paramref name="sunAz"/> (radians, atan2(x, z)).</summary>
    public Vector3 Sample(Vector3 d, float sunAz)
    {
        float az = (float)Math.Atan2(d.X, d.Z); float u = 0.5f + (az - sunAz) / (2 * (float)Math.PI) + (SunU - 0.5f); u -= (float)Math.Floor(u);
        float v = 0.5f - (float)Math.Asin(Math.Max(-1, Math.Min(1, d.Y))) / (float)Math.PI;
        float fx = u * W - 0.5f, fy = Math.Max(0, Math.Min(H - 1.001f, v * H - 0.5f)); int x0 = (int)Math.Floor(fx), y0 = (int)fy; float tx = fx - x0, ty = fy - y0;
        Vector3 G(int x, int y) { x = ((x % W) + W) % W; int i = (y * W + x) * 3; return new Vector3(_px[i], _px[i + 1], _px[i + 2]); }
        return Vector3.Lerp(Vector3.Lerp(G(x0, y0), G(x0 + 1, y0), tx), Vector3.Lerp(G(x0, y0 + 1), G(x0 + 1, y0 + 1), tx), ty);
    }
}
