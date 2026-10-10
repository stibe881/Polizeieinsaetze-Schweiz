using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geometry;

namespace SwissPoliceSim.Core.Roads
{
    /// <summary>
    /// Approximation of "innerorts" (Siedlungsgebiet within the Ortstafeln) from OSM land use and building density.
    /// 25 m raster. This is a MODEL of the signalled built-up area, not the official signage: expect errors of
    /// roughly one cell block at town entrances (see SpeedLimitValidation tests for the measured agreement).
    /// </summary>
    public sealed class BuiltUpMask
    {
        public const double Cell = 25.0;
        readonly double _minE, _minN; readonly int _w, _h; readonly bool[] _built;

        public BuiltUpMask(double minE, double minN, double maxE, double maxN, ExtractedFeatures f)
        {
            _minE = Math.Floor(minE / Cell) * Cell; _minN = Math.Floor(minN / Cell) * Cell;
            _w = (int)Math.Ceiling((maxE - _minE) / Cell) + 1; _h = (int)Math.Ceiling((maxN - _minN) / Cell) + 1;
            var raw = new bool[_w * _h];
            var count = new int[_w * _h];

            foreach (var lu in f.LandUse)
            {
                if (lu.Class != LandUseClass.Residential && lu.Class != LandUseClass.Commercial && lu.Class != LandUseClass.Industrial) continue;
                lu.Polygon.Bounds(out double x0, out double y0, out double x1, out double y1);
                int cx0 = Clamp((int)((x0 - _minE) / Cell), _w), cx1 = Clamp((int)((x1 - _minE) / Cell), _w);
                int cy0 = Clamp((int)((y0 - _minN) / Cell), _h), cy1 = Clamp((int)((y1 - _minN) / Cell), _h);
                for (int y = cy0; y <= cy1; y++)
                    for (int x = cx0; x <= cx1; x++)
                        if (lu.Polygon.Contains(_minE + (x + 0.5) * Cell, _minN + (y + 0.5) * Cell)) raw[y * _w + x] = true;
            }

            foreach (var b in f.Buildings)
            {
                if (b.Kind == BuildingKind.Shed || b.Kind == BuildingKind.Garage) continue;
                var c = PolygonUtil.Centroid(b.Footprint.Outer);
                int x = (int)((c.X - _minE) / Cell), y = (int)((c.Y - _minN) / Cell);
                if (x >= 0 && y >= 0 && x < _w && y < _h) count[y * _w + x]++;
            }
            // density: >= 5 buildings within the surrounding 5x5 cells (125 m)
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    int s = 0;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx >= 0 && yy >= 0 && xx < _w && yy < _h) s += count[yy * _w + xx];
                        }
                    if (s >= 5) raw[y * _w + x] = true;
                }
            _built = Erode(Dilate(Dilate(raw)));   // close small gaps (streets, squares): dilation 2 / erosion 1 = ~25 m margin
        }

        static int Clamp(int v, int n) => v < 0 ? 0 : (v >= n ? n - 1 : v);

        bool[] Dilate(bool[] src)
        {
            var d = new bool[src.Length];
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    if (!src[y * _w + x]) continue;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx >= 0 && yy >= 0 && xx < _w && yy < _h) d[yy * _w + xx] = true;
                        }
                }
            return d;
        }

        bool[] Erode(bool[] src)
        {
            var d = new bool[src.Length];
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    bool all = true;
                    for (int dy = -1; dy <= 1 && all; dy++)
                        for (int dx = -1; dx <= 1 && all; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx >= 0 && yy >= 0 && xx < _w && yy < _h && !src[yy * _w + xx]) all = false;
                        }
                    d[y * _w + x] = all;
                }
            return d;
        }

        public bool IsBuiltUp(double e, double n)
        {
            int x = (int)Math.Floor((e - _minE) / Cell), y = (int)Math.Floor((n - _minN) / Cell);
            return x >= 0 && y >= 0 && x < _w && y < _h && _built[y * _w + x];
        }

        public int WidthCells => _w; public int HeightCells => _h;
    }
}
