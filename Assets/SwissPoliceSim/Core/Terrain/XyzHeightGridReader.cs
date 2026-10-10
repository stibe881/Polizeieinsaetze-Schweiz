using System;
using System.Globalization;
using System.IO;
using SwissPoliceSim.Core.Geo;

namespace SwissPoliceSim.Core.Terrain
{
    /// <summary>Reads swisstopo "ASCII XYZ" height files (header "X Y Z", then "E N H" per line, LV95 + LN02).</summary>
    public static class XyzHeightGridReader
    {
        public static HeightGrid Read(Stream stream, TileId tile, double cellSize)
        {
            var grid = new HeightGrid(tile, cellSize);
            double minE = TileGrid.MinE(tile), minN = TileGrid.MinN(tile);
            using (var rd = new StreamReader(stream))
            {
                string line;
                int lineNo = 0;
                while ((line = rd.ReadLine()) != null)
                {
                    lineNo++;
                    if (line.Length == 0 || line[0] == 'X') continue;
                    if (!TryParse(line, out double e, out double n, out double h))
                        throw new FormatException($"Bad XYZ line {lineNo}: '{line}'");
                    int col = (int)Math.Floor((e - minE) / cellSize);
                    int row = (int)Math.Floor((n - minN) / cellSize);
                    if (col < 0 || row < 0 || col >= grid.Size || row >= grid.Size)
                        throw new FormatException($"Point outside tile {tile} on line {lineNo}: {e} {n}");
                    grid[col, row] = (float)h;
                }
            }
            return grid;
        }

        static bool TryParse(string line, out double e, out double n, out double h)
        {
            e = n = h = 0;
            var p = line.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3) return false;
            return double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out e)
                   && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out n)
                   && double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out h);
        }
    }
}
