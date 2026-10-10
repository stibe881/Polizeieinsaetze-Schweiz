using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Geometry
{
    public static class PolygonClip
    {
        /// <summary>Sutherland–Hodgman clip of a ring against an axis-aligned rectangle (may create degenerate edges for concave input; fine for rendering).</summary>
        public static List<Vec2d> ClipToRect(List<Vec2d> ring, double x0, double y0, double x1, double y1)
        {
            var r = ring;
            r = ClipEdge(r, p => p.X >= x0, (a, b) => Intersect(a, b, x0, true));
            r = ClipEdge(r, p => p.X <= x1, (a, b) => Intersect(a, b, x1, true));
            r = ClipEdge(r, p => p.Y >= y0, (a, b) => Intersect(a, b, y0, false));
            r = ClipEdge(r, p => p.Y <= y1, (a, b) => Intersect(a, b, y1, false));
            return r;
        }

        static Vec2d Intersect(Vec2d a, Vec2d b, double v, bool vertical)
        {
            double t = vertical ? (v - a.X) / (b.X - a.X) : (v - a.Y) / (b.Y - a.Y);
            return new Vec2d(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
        }

        static List<Vec2d> ClipEdge(List<Vec2d> input, Func<Vec2d, bool> inside, Func<Vec2d, Vec2d, Vec2d> isect)
        {
            var o = new List<Vec2d>();
            if (input.Count == 0) return o;
            var s = input[input.Count - 1];
            foreach (var e in input)
            {
                bool ei = inside(e), si = inside(s);
                if (ei) { if (!si) o.Add(isect(s, e)); o.Add(e); }
                else if (si) o.Add(isect(s, e));
                s = e;
            }
            return o;
        }
    }
}
