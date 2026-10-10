using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Geo;

namespace SwissPoliceSim.Core.Terrain
{
    /// <summary>Any source that can answer "terrain height (m a.s.l.) at LV95 E/N".</summary>
    public interface IHeightSource
    {
        bool TrySample(double e, double n, out float height);
    }

    /// <summary>
    /// Raster of one 1 km tile. Values are located at CELL CENTRES (swissALTI3D XYZ convention:
    /// a 2 m cell starting at E=2600000 is stored as E=2600001). Row 0 = southern row.
    /// </summary>
    public sealed class HeightGrid
    {
        public readonly TileId Tile;
        public readonly double CellSize;
        public readonly int Size;          // cells per side
        public readonly float[] Values;    // [row * Size + col]

        public HeightGrid(TileId tile, double cellSize)
        {
            Tile = tile;
            CellSize = cellSize;
            Size = (int)Math.Round(TileGrid.TileSize / cellSize);
            Values = new float[Size * Size];
            for (int i = 0; i < Values.Length; i++) Values[i] = float.NaN;
        }

        public float this[int col, int row]
        {
            get => Values[row * Size + col];
            set => Values[row * Size + col] = value;
        }

        public int MissingCells()
        {
            int n = 0;
            for (int i = 0; i < Values.Length; i++) if (float.IsNaN(Values[i])) n++;
            return n;
        }
    }

    /// <summary>
    /// Set of equally-sized HeightGrids on the same cell lattice; bilinear sampling across tile borders.
    /// Missing neighbours are tolerated (nearest available corner values are used) so the outermost tiles of an
    /// import area do not produce holes.
    /// </summary>
    public sealed class HeightMosaic : IHeightSource
    {
        readonly Dictionary<TileId, HeightGrid> _grids = new Dictionary<TileId, HeightGrid>();
        readonly double _cell;

        public HeightMosaic(double cellSize) { _cell = cellSize; }

        public int Count => _grids.Count;
        public bool Has(TileId t) => _grids.ContainsKey(t);

        public void Add(HeightGrid g)
        {
            if (Math.Abs(g.CellSize - _cell) > 1e-9) throw new ArgumentException("Cell size mismatch");
            _grids[g.Tile] = g;
        }

        bool TryCell(long gx, long gy, out float v)
        {
            double e = (gx + 0.5) * _cell, n = (gy + 0.5) * _cell;
            var t = TileGrid.FromLv95(e, n);
            if (_grids.TryGetValue(t, out var g))
            {
                int col = (int)(gx - (long)Math.Round(TileGrid.MinE(t) / _cell));
                int row = (int)(gy - (long)Math.Round(TileGrid.MinN(t) / _cell));
                v = g[col, row];
                return !float.IsNaN(v);
            }
            v = float.NaN;
            return false;
        }

        public bool TrySample(double e, double n, out float height)
        {
            double fx = e / _cell - 0.5, fy = n / _cell - 0.5;
            long x0 = (long)Math.Floor(fx), y0 = (long)Math.Floor(fy);
            double tx = fx - x0, ty = fy - y0;

            bool a = TryCell(x0, y0, out float v00), b = TryCell(x0 + 1, y0, out float v10);
            bool c = TryCell(x0, y0 + 1, out float v01), d = TryCell(x0 + 1, y0 + 1, out float v11);
            int have = (a ? 1 : 0) + (b ? 1 : 0) + (c ? 1 : 0) + (d ? 1 : 0);
            if (have == 0) { height = float.NaN; return false; }
            if (have < 4)
            {
                float sum = (a ? v00 : 0) + (b ? v10 : 0) + (c ? v01 : 0) + (d ? v11 : 0);
                float avg = sum / have;
                if (!a) v00 = avg; if (!b) v10 = avg; if (!c) v01 = avg; if (!d) v11 = avg;
            }
            double h0 = v00 + (v10 - v00) * tx;
            double h1 = v01 + (v11 - v01) * tx;
            height = (float)(h0 + (h1 - h0) * ty);
            return true;
        }
    }
}
