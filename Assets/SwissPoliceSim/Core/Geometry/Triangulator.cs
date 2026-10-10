using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Geometry
{
    /// <summary>
    /// Ear-clipping triangulation of a polygon with holes (holes are merged into the outer ring with bridge edges,
    /// David Eberly's method). Input must be normalised (outer CCW, holes CW). Output: indices into <see cref="Vertices"/>.
    /// </summary>
    public sealed class Triangulator
    {
        public readonly List<Vec2d> Vertices = new List<Vec2d>();
        public readonly List<int> Triangles = new List<int>();

        public static Triangulator Run(PolygonWithHoles poly)
        {
            var t = new Triangulator();
            t.Build(poly);
            return t;
        }

        void Build(PolygonWithHoles poly)
        {
            // working ring as list of vertex indices; vertices may be duplicated by bridges
            foreach (var p in poly.Outer) Vertices.Add(p);
            var ring = new List<int>();
            for (int i = 0; i < poly.Outer.Count; i++) ring.Add(i);

            // holes: process by descending max X
            var holes = new List<(List<int> idx, int rightmost)>();
            foreach (var h in poly.Holes)
            {
                if (h.Count < 3) continue;
                var idx = new List<int>(); int rm = 0;
                for (int i = 0; i < h.Count; i++)
                {
                    idx.Add(Vertices.Count); Vertices.Add(h[i]);
                    if (h[i].X > h[rm].X) rm = i;
                }
                holes.Add((idx, rm));
            }
            holes.Sort((a, b) => Vertices[b.idx[b.rightmost]].X.CompareTo(Vertices[a.idx[a.rightmost]].X));
            foreach (var (idx, rm) in holes) ring = MergeHole(ring, idx, rm);

            EarClip(ring);
        }

        List<int> MergeHole(List<int> ring, List<int> hole, int rm)
        {
            var m = Vertices[hole[rm]];
            // find the closest ring edge intersected by the ray from m towards +X
            double bestX = double.MaxValue; int bestEdge = -1; Vec2d bestPt = new Vec2d();
            for (int i = 0; i < ring.Count; i++)
            {
                var a = Vertices[ring[i]]; var b = Vertices[ring[(i + 1) % ring.Count]];
                if ((a.Y > m.Y) == (b.Y > m.Y) && a.Y != m.Y && b.Y != m.Y) continue;
                if (a.Y == b.Y) continue;
                double t = (m.Y - a.Y) / (b.Y - a.Y);
                if (t < 0 || t > 1) continue;
                double x = a.X + t * (b.X - a.X);
                if (x >= m.X && x < bestX) { bestX = x; bestEdge = i; bestPt = new Vec2d(x, m.Y); }
            }
            if (bestEdge < 0) { bestEdge = 0; }
            // candidate: endpoint of the edge with max X; then check reflex vertices inside triangle (m, bestPt, endpoint)
            int ia = bestEdge, ib = (bestEdge + 1) % ring.Count;
            int cand = Vertices[ring[ia]].X > Vertices[ring[ib]].X ? ia : ib;
            var P = Vertices[ring[cand]];
            double bestAngle = double.MaxValue; int final = cand;
            for (int i = 0; i < ring.Count; i++)
            {
                if (i == cand) continue;
                var v = Vertices[ring[i]];
                if (v.X >= m.X && PointInTriangle(v, m, bestPt, P))
                {
                    double ang = Math.Abs(Math.Atan2(v.Y - m.Y, v.X - m.X));
                    if (ang < bestAngle || (ang == bestAngle && v.X < Vertices[ring[final]].X)) { bestAngle = ang; final = i; }
                }
            }
            // splice: ring[..final], hole starting at rm (full loop), back to hole[rm], ring[final..]
            var res = new List<int>(ring.Count + hole.Count + 2);
            for (int i = 0; i <= final; i++) res.Add(ring[i]);
            for (int k = 0; k <= hole.Count; k++) res.Add(hole[(rm + k) % hole.Count]);
            // duplicate the ring vertex we bridge from/back to
            res.Add(AddCopy(ring[final]));
            for (int i = final + 1; i < ring.Count; i++) res.Add(ring[i]);
            return res;
        }

        int AddCopy(int vi) { Vertices.Add(Vertices[vi]); return Vertices.Count - 1; }

        static bool PointInTriangle(Vec2d p, Vec2d a, Vec2d b, Vec2d c)
        {
            double d1 = Vec2d.Cross(b - a, p - a), d2 = Vec2d.Cross(c - b, p - b), d3 = Vec2d.Cross(a - c, p - c);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        void EarClip(List<int> ring)
        {
            var idx = new List<int>(ring);
            int guard = idx.Count * idx.Count + 10;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                int n = idx.Count;
                for (int i = 0; i < n; i++)
                {
                    int ip = idx[(i + n - 1) % n], ic = idx[i], inx = idx[(i + 1) % n];
                    var a = Vertices[ip]; var b = Vertices[ic]; var c = Vertices[inx];
                    double cr = Vec2d.Cross(b - a, c - b);
                    if (cr <= 1e-12) continue;   // reflex or degenerate
                    bool ear = true;
                    for (int j = 0; j < n && ear; j++)
                    {
                        int ij = idx[j];
                        if (ij == ip || ij == ic || ij == inx) continue;
                        var p = Vertices[ij];
                        if (SamePoint(p, a) || SamePoint(p, b) || SamePoint(p, c)) continue;
                        if (PointInTriangleStrict(p, a, b, c)) ear = false;
                    }
                    if (!ear) continue;
                    Triangles.Add(ip); Triangles.Add(ic); Triangles.Add(inx);
                    idx.RemoveAt(i); clipped = true; break;
                }
                if (!clipped)
                {
                    // degenerate input: drop a collinear vertex to make progress
                    int rm = 0; double mn = double.MaxValue;
                    for (int i = 0; i < idx.Count; i++)
                    {
                        var a = Vertices[idx[(i + idx.Count - 1) % idx.Count]]; var b = Vertices[idx[i]]; var c = Vertices[idx[(i + 1) % idx.Count]];
                        double cr = Math.Abs(Vec2d.Cross(b - a, c - b));
                        if (cr < mn) { mn = cr; rm = i; }
                    }
                    idx.RemoveAt(rm);
                }
            }
            if (idx.Count == 3) { Triangles.Add(idx[0]); Triangles.Add(idx[1]); Triangles.Add(idx[2]); }
        }

        static bool SamePoint(Vec2d a, Vec2d b) => Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;

        static bool PointInTriangleStrict(Vec2d p, Vec2d a, Vec2d b, Vec2d c)
        {
            double d1 = Vec2d.Cross(b - a, p - a), d2 = Vec2d.Cross(c - b, p - b), d3 = Vec2d.Cross(a - c, p - c);
            return d1 > 1e-12 && d2 > 1e-12 && d3 > 1e-12;
        }

        public double TotalArea()
        {
            double a = 0;
            for (int i = 0; i < Triangles.Count; i += 3)
                a += Math.Abs(Vec2d.Cross(Vertices[Triangles[i + 1]] - Vertices[Triangles[i]], Vertices[Triangles[i + 2]] - Vertices[Triangles[i]])) * 0.5;
            return a;
        }
    }
}
