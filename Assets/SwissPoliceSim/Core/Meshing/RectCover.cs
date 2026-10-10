using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Geometry;

namespace SwissPoliceSim.Core.Meshing
{
    public struct Rect2
    {
        public Vec2d Centre; public Vec2d AxisLong; public double HalfLong, HalfShort;
    }

    /// <summary>
    /// Covers a (mostly rectilinear) footprint with overlapping rectangles in the frame of its oriented bounding box.
    /// Each rectangle later gets its own gabled/hipped roof; where wings meet, the intersecting roof planes form the
    /// valley automatically (z-buffer), which approximates real L/T/U shaped Swiss houses without a straight skeleton.
    /// </summary>
    public static class RectCover
    {
        public static List<Rect2> Compute(PolygonWithHoles poly, Vec2d origin, Vec2d u, double cell = 0.5, double minArea = 5.0, double minShort = 1.8, double targetCoverage = 0.93)
        {
            var v = new Vec2d(-u.Y, u.X);
            // grid bounds in (a,b) frame
            double minA = double.MaxValue, maxA = double.MinValue, minB = double.MaxValue, maxB = double.MinValue;
            foreach (var p in poly.Outer)
            {
                var d = p - origin; double a = Vec2d.Dot(d, u), b = Vec2d.Dot(d, v);
                if (a < minA) minA = a; if (a > maxA) maxA = a; if (b < minB) minB = b; if (b > maxB) maxB = b;
            }
            int w = (int)Math.Ceiling((maxA - minA) / cell), h = (int)Math.Ceiling((maxB - minB) / cell);
            if (w < 1 || h < 1 || w * h > 400 * 400) return new List<Rect2>();
            var inside = new bool[w * h]; int insideCount = 0;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    double a = minA + (i + 0.5) * cell, b = minB + (j + 0.5) * cell;
                    var pt = origin + u * a + v * b;
                    if (poly.Contains(pt.X, pt.Y)) { inside[j * w + i] = true; insideCount++; }
                }
            if (insideCount == 0) return new List<Rect2>();

            var remain = (bool[])inside.Clone(); int remainCount = insideCount;
            var res = new List<Rect2>();
            while (res.Count < 12)
            {
                if (!LargestRect(remain, w, h, out int x0, out int y0, out int x1, out int y1)) break;
                int rw = x1 - x0 + 1, rh = y1 - y0 + 1;
                double area = rw * rh * cell * cell;
                if (area < minArea || Math.Min(rw, rh) * cell < minShort) break;
                // grow into already covered / inside cells (wing overlap)
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    if (x0 > 0 && ColInside(inside, w, h, x0 - 1, y0, y1)) { x0--; grew = true; }
                    if (x1 < w - 1 && ColInside(inside, w, h, x1 + 1, y0, y1)) { x1++; grew = true; }
                    if (y0 > 0 && RowInside(inside, w, h, y0 - 1, x0, x1)) { y0--; grew = true; }
                    if (y1 < h - 1 && RowInside(inside, w, h, y1 + 1, x0, x1)) { y1++; grew = true; }
                }
                for (int j = y0; j <= y1; j++)
                    for (int i = x0; i <= x1; i++)
                        if (remain[j * w + i]) { remain[j * w + i] = false; remainCount--; }
                double ca = minA + (x0 + x1 + 1) * 0.5 * cell, cb = minB + (y0 + y1 + 1) * 0.5 * cell;
                double ea = (x1 - x0 + 1) * cell * 0.5, eb = (y1 - y0 + 1) * cell * 0.5;
                var r = new Rect2 { Centre = origin + u * ca + v * cb };
                if (ea >= eb) { r.AxisLong = u; r.HalfLong = ea; r.HalfShort = eb; }
                else { r.AxisLong = v; r.HalfLong = eb; r.HalfShort = ea; }
                res.Add(r);
                if (remainCount <= (1 - targetCoverage) * insideCount) break;
            }
            return res;
        }

        static bool ColInside(bool[] g, int w, int h, int x, int y0, int y1) { for (int j = y0; j <= y1; j++) if (!g[j * w + x]) return false; return true; }
        static bool RowInside(bool[] g, int w, int h, int y, int x0, int x1) { for (int i = x0; i <= x1; i++) if (!g[y * w + i]) return false; return true; }

        /// <summary>Largest all-true axis-aligned rectangle (histogram stack method).</summary>
        static bool LargestRect(bool[] g, int w, int h, out int bx0, out int by0, out int bx1, out int by1)
        {
            bx0 = by0 = bx1 = by1 = 0; int best = 0;
            var heights = new int[w];
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++) heights[i] = g[j * w + i] ? heights[i] + 1 : 0;
                var stack = new Stack<int>();
                for (int i = 0; i <= w; i++)
                {
                    int cur = i == w ? 0 : heights[i];
                    while (stack.Count > 0 && heights[stack.Peek()] >= cur)
                    {
                        int hh = heights[stack.Pop()];
                        int left = stack.Count == 0 ? 0 : stack.Peek() + 1;
                        int width = i - left, area = hh * width;
                        if (area > best) { best = area; bx0 = left; bx1 = i - 1; by1 = j; by0 = j - hh + 1; }
                    }
                    stack.Push(i);
                }
            }
            return best > 0;
        }
    }
}
