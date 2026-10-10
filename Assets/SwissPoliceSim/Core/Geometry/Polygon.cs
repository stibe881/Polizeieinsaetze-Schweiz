using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Geometry
{
    public struct Vec2d
    {
        public double X, Y;
        public Vec2d(double x, double y) { X = x; Y = y; }
        public static Vec2d operator +(Vec2d a, Vec2d b) => new Vec2d(a.X + b.X, a.Y + b.Y);
        public static Vec2d operator -(Vec2d a, Vec2d b) => new Vec2d(a.X - b.X, a.Y - b.Y);
        public static Vec2d operator *(Vec2d a, double s) => new Vec2d(a.X * s, a.Y * s);
        public double Length => Math.Sqrt(X * X + Y * Y);
        public static double Dot(Vec2d a, Vec2d b) => a.X * b.X + a.Y * b.Y;
        public static double Cross(Vec2d a, Vec2d b) => a.X * b.Y - a.Y * b.X;
        public Vec2d Normalized() { double l = Length; return l < 1e-12 ? new Vec2d(0, 0) : new Vec2d(X / l, Y / l); }
    }

    /// <summary>A polygon with optional holes. Outer ring is stored counter-clockwise, holes clockwise (after Normalize()).</summary>
    public sealed class PolygonWithHoles
    {
        public List<Vec2d> Outer = new List<Vec2d>();
        public List<List<Vec2d>> Holes = new List<List<Vec2d>>();

        public double Area()
        {
            double a = Math.Abs(PolygonUtil.SignedArea(Outer));
            foreach (var h in Holes) a -= Math.Abs(PolygonUtil.SignedArea(h));
            return a;
        }

        public void Normalize()
        {
            PolygonUtil.RemoveClosingPoint(Outer);
            if (PolygonUtil.SignedArea(Outer) < 0) Outer.Reverse();
            foreach (var h in Holes)
            {
                PolygonUtil.RemoveClosingPoint(h);
                if (PolygonUtil.SignedArea(h) > 0) h.Reverse();
            }
        }

        public void Bounds(out double minX, out double minY, out double maxX, out double maxY)
        {
            minX = minY = double.MaxValue; maxX = maxY = double.MinValue;
            foreach (var p in Outer)
            {
                if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            }
        }

        public bool Contains(double x, double y)
        {
            if (!PolygonUtil.PointInRing(Outer, x, y)) return false;
            foreach (var h in Holes) if (PolygonUtil.PointInRing(h, x, y)) return false;
            return true;
        }
    }

    public static class PolygonUtil
    {
        /// <summary>Positive for counter-clockwise rings.</summary>
        public static double SignedArea(IList<Vec2d> r)
        {
            double a = 0;
            for (int i = 0, n = r.Count; i < n; i++)
            {
                var p = r[i]; var q = r[(i + 1) % n];
                a += p.X * q.Y - q.X * p.Y;
            }
            return a * 0.5;
        }

        public static void RemoveClosingPoint(List<Vec2d> r)
        {
            if (r.Count > 1 && Math.Abs(r[0].X - r[r.Count - 1].X) < 1e-9 && Math.Abs(r[0].Y - r[r.Count - 1].Y) < 1e-9)
                r.RemoveAt(r.Count - 1);
        }

        public static bool PointInRing(IList<Vec2d> r, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
            {
                if ((r[i].Y > y) != (r[j].Y > y) &&
                    x < (r[j].X - r[i].X) * (y - r[i].Y) / (r[j].Y - r[i].Y) + r[i].X)
                    inside = !inside;
            }
            return inside;
        }

        public static Vec2d Centroid(IList<Vec2d> r)
        {
            double a = 0, cx = 0, cy = 0;
            for (int i = 0, n = r.Count; i < n; i++)
            {
                var p = r[i]; var q = r[(i + 1) % n];
                double c = p.X * q.Y - q.X * p.Y;
                a += c; cx += (p.X + q.X) * c; cy += (p.Y + q.Y) * c;
            }
            if (Math.Abs(a) < 1e-9) return r.Count > 0 ? r[0] : new Vec2d();
            return new Vec2d(cx / (3 * a), cy / (3 * a));
        }

        /// <summary>
        /// Minimum-area oriented bounding rectangle. Returns centre, unit axis u along the LONG side,
        /// and half extents along u (long) and its normal (short).
        /// </summary>
        public static void OrientedBox(IList<Vec2d> r, out Vec2d centre, out Vec2d u, out double halfLong, out double halfShort)
        {
            double bestArea = double.MaxValue;
            centre = new Vec2d(); u = new Vec2d(1, 0); halfLong = halfShort = 0;
            for (int i = 0, n = r.Count; i < n; i++)
            {
                var d = (r[(i + 1) % n] - r[i]).Normalized();
                if (d.Length < 0.5) continue;
                var nrm = new Vec2d(-d.Y, d.X);
                double minA = double.MaxValue, maxA = double.MinValue, minB = double.MaxValue, maxB = double.MinValue;
                foreach (var p in r)
                {
                    double a = Vec2d.Dot(p, d), b = Vec2d.Dot(p, nrm);
                    if (a < minA) minA = a; if (a > maxA) maxA = a;
                    if (b < minB) minB = b; if (b > maxB) maxB = b;
                }
                double area = (maxA - minA) * (maxB - minB);
                if (area < bestArea)
                {
                    bestArea = area;
                    double ca = (minA + maxA) * 0.5, cb = (minB + maxB) * 0.5;
                    double ea = (maxA - minA) * 0.5, eb = (maxB - minB) * 0.5;
                    centre = d * ca + nrm * cb;
                    if (ea >= eb) { u = d; halfLong = ea; halfShort = eb; }
                    else { u = nrm; halfLong = eb; halfShort = ea; }
                }
            }
        }

        /// <summary>Douglas-Peucker for closed rings (keeps at least 3 points).</summary>
        public static List<Vec2d> SimplifyRing(List<Vec2d> ring, double tolerance)
        {
            if (ring.Count <= 4) return new List<Vec2d>(ring);
            // split at the two farthest-apart indices (0 and farthest) for stability
            int far = 0; double fd = -1;
            for (int i = 1; i < ring.Count; i++) { double d = (ring[i] - ring[0]).Length; if (d > fd) { fd = d; far = i; } }
            var a = new List<Vec2d>(); var b = new List<Vec2d>();
            for (int i = 0; i <= far; i++) a.Add(ring[i]);
            for (int i = far; i < ring.Count; i++) b.Add(ring[i]); b.Add(ring[0]);
            var sa = DouglasPeucker(a, tolerance); var sb = DouglasPeucker(b, tolerance);
            var res = new List<Vec2d>(sa);
            for (int i = 1; i < sb.Count - 1; i++) res.Add(sb[i]);
            return res.Count >= 3 ? res : new List<Vec2d>(ring);
        }

        static List<Vec2d> DouglasPeucker(List<Vec2d> pts, double tol)
        {
            if (pts.Count < 3) return new List<Vec2d>(pts);
            var keep = new bool[pts.Count]; keep[0] = keep[pts.Count - 1] = true;
            var stack = new Stack<(int, int)>(); stack.Push((0, pts.Count - 1));
            while (stack.Count > 0)
            {
                var (s, e) = stack.Pop();
                double max = 0; int idx = -1;
                for (int i = s + 1; i < e; i++)
                {
                    double d = DistToSegment(pts[i], pts[s], pts[e]);
                    if (d > max) { max = d; idx = i; }
                }
                if (idx >= 0 && max > tol) { keep[idx] = true; stack.Push((s, idx)); stack.Push((idx, e)); }
            }
            var res = new List<Vec2d>();
            for (int i = 0; i < pts.Count; i++) if (keep[i]) res.Add(pts[i]);
            return res;
        }

        public static double DistToSegment(Vec2d p, Vec2d a, Vec2d b)
        {
            var ab = b - a; double l2 = Vec2d.Dot(ab, ab);
            if (l2 < 1e-12) return (p - a).Length;
            double t = Math.Max(0, Math.Min(1, Vec2d.Dot(p - a, ab) / l2));
            return (p - (a + ab * t)).Length;
        }
    }
}
