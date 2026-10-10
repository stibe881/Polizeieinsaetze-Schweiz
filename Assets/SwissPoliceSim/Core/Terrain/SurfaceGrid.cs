using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SwissPoliceSim.Core.Geo;

namespace SwissPoliceSim.Core.Terrain
{
    /// <summary>
    /// Digital surface model (swissSURFACE3D Raster: roofs, tree crowns, ground) of a tile, reduced to 1 m cells by
    /// taking the MAXIMUM of the finer input cells (keeps ridges and treetops). Heights in m a.s.l., row 0 = south.
    /// </summary>
    public sealed class SurfaceGrid
    {
        public readonly TileId Tile;
        public const double Cell = 1.0;
        public const int Size = 1000;
        public readonly float[] Values = new float[Size * Size];

        public SurfaceGrid(TileId t) { Tile = t; for (int i = 0; i < Values.Length; i++) Values[i] = float.NaN; }

        public float At(int col, int row) => Values[row * Size + col];

        /// <summary>Streams a swissSURFACE3D XYZ file (0.5 m or 2 m grid) into a 1 m max-grid.</summary>
        public static SurfaceGrid ReadXyz(Stream stream, TileId tile)
        {
            var g = new SurfaceGrid(tile);
            double e0 = TileGrid.MinE(tile), n0 = TileGrid.MinN(tile);
            using (var rd = new StreamReader(stream, System.Text.Encoding.ASCII, false, 1 << 16))
            {
                string line;
                while ((line = rd.ReadLine()) != null)
                {
                    if (line.Length < 5 || line[0] == 'X') continue;
                    int a = line.IndexOf(' '); if (a < 0) continue;
                    int b = line.IndexOf(' ', a + 1); if (b < 0) continue;
                    if (!double.TryParse(line.AsSpan(0, a), NumberStyles.Float, CultureInfo.InvariantCulture, out double e)) continue;
                    if (!double.TryParse(line.AsSpan(a + 1, b - a - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) continue;
                    if (!float.TryParse(line.AsSpan(b + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float h)) continue;
                    int col = (int)Math.Floor(e - e0), row = (int)Math.Floor(n - n0);
                    if ((uint)col >= Size || (uint)row >= Size) continue;
                    int idx = row * Size + col;
                    float cur = g.Values[idx];
                    if (float.IsNaN(cur) || h > cur) g.Values[idx] = h;
                }
            }
            return g;
        }
    }

    /// <summary>Set of surface grids; samples with nearest-cell lookup (1 m).</summary>
    public sealed class SurfaceMosaic
    {
        readonly Dictionary<TileId, SurfaceGrid> _g = new Dictionary<TileId, SurfaceGrid>();
        public void Add(SurfaceGrid g) { _g[g.Tile] = g; }
        public bool Has(TileId t) => _g.ContainsKey(t);
        public int Count => _g.Count;

        public bool TrySample(double e, double n, out float h)
        {
            var t = TileGrid.FromLv95(e, n);
            if (_g.TryGetValue(t, out var g))
            {
                int col = (int)Math.Floor(e - TileGrid.MinE(t)), row = (int)Math.Floor(n - TileGrid.MinN(t));
                if ((uint)col < SurfaceGrid.Size && (uint)row < SurfaceGrid.Size)
                {
                    h = g.At(col, row);
                    return !float.IsNaN(h);
                }
            }
            h = float.NaN; return false;
        }

        /// <summary>Height of the object above ground (surface − terrain) at a point, or NaN.</summary>
        public float NormalizedHeight(IHeightSource dem, double e, double n)
        {
            if (!TrySample(e, n, out float s) || !dem.TrySample(e, n, out float t)) return float.NaN;
            return s - t;
        }
    }
}
