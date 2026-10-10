using System;
using System.Collections.Generic;
using NUnit.Framework;
using SwissPoliceSim.Core.Geometry;

namespace SwissPoliceSim.Tests
{
    public class GeometryTests
    {
        static List<Vec2d> Rect(double x0, double y0, double x1, double y1) =>
            new List<Vec2d> { new Vec2d(x0, y0), new Vec2d(x1, y0), new Vec2d(x1, y1), new Vec2d(x0, y1) };

        [Test]
        public void Triangulate_ConcaveL_AreaMatches()
        {
            var p = new PolygonWithHoles
            {
                Outer = new List<Vec2d> { new Vec2d(0, 0), new Vec2d(10, 0), new Vec2d(10, 4), new Vec2d(4, 4), new Vec2d(4, 10), new Vec2d(0, 10) }
            };
            p.Normalize();
            var t = Triangulator.Run(p);
            Assert.AreEqual(10 * 4 + 4 * 6, t.TotalArea(), 1e-9);
            Assert.AreEqual(4 * 3, t.Triangles.Count); // n-2 = 4 triangles
        }

        [Test]
        public void Triangulate_WithHole_AreaIsOuterMinusHole()
        {
            var p = new PolygonWithHoles { Outer = Rect(0, 0, 20, 20) };
            p.Holes.Add(Rect(5, 5, 9, 9));
            p.Holes.Add(Rect(12, 8, 16, 14));
            p.Normalize();
            var t = Triangulator.Run(p);
            Assert.AreEqual(400 - 16 - 24, t.TotalArea(), 1e-6);
            Assert.AreEqual(p.Area(), t.TotalArea(), 1e-6);
        }

        [Test]
        public void Triangulate_ClockwiseInput_IsNormalised()
        {
            var o = Rect(0, 0, 3, 3); o.Reverse();
            var p = new PolygonWithHoles { Outer = o }; p.Normalize();
            Assert.AreEqual(9, Triangulator.Run(p).TotalArea(), 1e-9);
        }

        [Test]
        public void Triangulate_RandomStarPolygons_AreaMatchesShoelace()
        {
            var rnd = new Random(42);
            for (int k = 0; k < 200; k++)
            {
                int n = 5 + rnd.Next(20);
                var ring = new List<Vec2d>();
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Math.PI * i / n, r = 5 + rnd.NextDouble() * 20;
                    ring.Add(new Vec2d(r * Math.Cos(a), r * Math.Sin(a)));
                }
                var p = new PolygonWithHoles { Outer = ring }; p.Normalize();
                Assert.AreEqual(p.Area(), Triangulator.Run(p).TotalArea(), 1e-6, "polygon " + k);
            }
        }

        [Test]
        public void OrientedBox_RotatedRectangle()
        {
            double ang = 0.5, c = Math.Cos(ang), s = Math.Sin(ang);
            var r = new List<Vec2d>();
            foreach (var q in new[] { new Vec2d(-10, -3), new Vec2d(10, -3), new Vec2d(10, 3), new Vec2d(-10, 3) })
                r.Add(new Vec2d(q.X * c - q.Y * s + 100, q.X * s + q.Y * c + 50));
            PolygonUtil.OrientedBox(r, out var centre, out var u, out double hl, out double hs);
            Assert.AreEqual(100, centre.X, 1e-6); Assert.AreEqual(50, centre.Y, 1e-6);
            Assert.AreEqual(10, hl, 1e-6); Assert.AreEqual(3, hs, 1e-6);
            Assert.AreEqual(1.0, Math.Abs(u.X * c + u.Y * s), 1e-6);
        }

        [Test]
        public void PointInPolygon_AndHoles()
        {
            var p = new PolygonWithHoles { Outer = Rect(0, 0, 10, 10) };
            p.Holes.Add(Rect(4, 4, 6, 6)); p.Normalize();
            Assert.IsTrue(p.Contains(1, 1)); Assert.IsFalse(p.Contains(5, 5)); Assert.IsFalse(p.Contains(11, 1));
        }

        [Test]
        public void RingAssembler_JoinsReversedAndShuffledSegments()
        {
            var ways = new List<List<long>>
            {
                new List<long> { 3, 4, 5 }, new List<long> { 1, 2, 3 }, new List<long> { 1, 6, 5 }
            };
            var rings = RingAssembler.Assemble(ways, out int open);
            Assert.AreEqual(1, rings.Count); Assert.AreEqual(0, open);
            Assert.AreEqual(rings[0][0], rings[0][rings[0].Count - 1]);
            Assert.AreEqual(7, rings[0].Count);
        }

        [Test]
        public void RingAssembler_ReportsOpenChains()
        {
            RingAssembler.Assemble(new List<List<long>> { new List<long> { 1, 2, 3 } }, out int open);
            Assert.AreEqual(1, open);
        }

        [Test]
        public void SimplifyRing_RemovesCollinearPoints()
        {
            var r = new List<Vec2d> { new Vec2d(0, 0), new Vec2d(5, 0.01), new Vec2d(10, 0), new Vec2d(10, 10), new Vec2d(0, 10) };
            Assert.AreEqual(4, PolygonUtil.SimplifyRing(r, 0.1).Count);
        }
    }
}
