using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Roads;

namespace SwissPoliceSim.Tests
{
    public class RoadTests
    {
        const string Osm = @"<?xml version='1.0'?><osm version='0.6'>
<node id='1' lat='46.9466' lon='7.4441'/><node id='2' lat='46.9470' lon='7.4441'/><node id='3' lat='46.9474' lon='7.4441'/>
<node id='4' lat='46.9470' lon='7.4448'/>
<way id='10'><nd ref='1'/><nd ref='2'/><nd ref='3'/><tag k='highway' v='primary'/><tag k='maxspeed' v='50'/><tag k='name' v='Bundesgasse'/><tag k='lanes' v='2'/></way>
<way id='11'><nd ref='2'/><nd ref='4'/><tag k='highway' v='residential'/><tag k='oneway' v='yes'/></way>
<way id='12'><nd ref='1'/><nd ref='4'/><tag k='highway' v='footway'/></way>
<way id='13'><nd ref='1'/><nd ref='3'/><tag k='highway' v='service'/><tag k='access' v='private'/></way>
<way id='14'><nd ref='1'/><nd ref='99'/><tag k='highway' v='tertiary'/></way>
</osm>";

        static List<RoadWay> Parse() => OsmXmlRoadParser.Parse(new MemoryStream(Encoding.UTF8.GetBytes(Osm)));

        [Test]
        public void OsmParser_KeepsOnlyDrivablePublicWays_WithTags()
        {
            var ways = Parse();
            Assert.AreEqual(2, ways.Count); // footway, private service and way with <2 resolvable nodes dropped
            var p = ways[0];
            Assert.AreEqual(RoadClass.Primary, p.Class);
            Assert.AreEqual(50, p.MaxSpeedKmh);
            Assert.AreEqual("Bundesgasse", p.Name);
            Assert.AreEqual(OneWay.No, p.OneWay);
            Assert.AreEqual(OneWay.Forward, ways[1].OneWay);
            Assert.AreEqual(3, p.PointCount);
            Assert.That(p.Length(), Is.InRange(85, 95)); // 0.0008° lat ≈ 89 m
        }

        [TestCase("50", 50)] [TestCase("80 km/h", 80)] [TestCase("CH:urban", 50)] [TestCase("CH:motorway", 120)]
        [TestCase("none", 0)] [TestCase(null, 0)] [TestCase("30 mph", 48)]
        public void MaxSpeedParsing(string tag, int expected) => Assert.AreEqual(expected, RoadTags.ParseMaxSpeed(tag));

        [Test]
        public void DefaultSpeedIsFlaggedAsDefault()
        {
            var w = new RoadWay { Class = RoadClass.Motorway };
            Assert.AreEqual(120, w.EffectiveSpeedKmh(out var src));
            Assert.AreEqual(SpeedSource.Default, src);
        }

        [Test]
        public void Split_AtTileBorder_SharesBorderNode_AndRoundTrips()
        {
            var w = new RoadWay
            {
                OsmId = 7, Class = RoadClass.Secondary, Name = "Test", Ref = "A9", MaxSpeedKmh = 80, Bridge = true,
                NodeIds = new long[] { 1, 2, 3 },
                E = new[] { 2600800.0, 2601100.0, 2601300.0 }, N = new[] { 1199500.0, 1199500.0, 1199500.0 },
                H = new[] { 540.0, 541.0, 542.5 }
            };
            var map = new Dictionary<TileId, List<RoadWay>>();
            RoadTileFile.Split(w, map);
            Assert.AreEqual(2, map.Count);
            var left = map[new TileId(2600, 1199)][0]; var right = map[new TileId(2601, 1199)][0];
            Assert.AreEqual(new long[] { 1, 2 }, left.NodeIds);
            Assert.AreEqual(new long[] { 2, 3 }, right.NodeIds); // node 2 shared => network stays connected

            var ms = new MemoryStream();
            RoadTileFile.Write(ms, new TileId(2601, 1199), map[new TileId(2601, 1199)]);
            ms.Position = 0;
            var back = RoadTileFile.Read(ms, out var tile);
            Assert.AreEqual(new TileId(2601, 1199), tile);
            Assert.AreEqual(1, back.Count);
            Assert.AreEqual("A9", back[0].Ref); Assert.IsTrue(back[0].Bridge); Assert.AreEqual(80, back[0].MaxSpeedKmh);
            Assert.AreEqual(2601100.0, back[0].E[0], 1e-3); Assert.AreEqual(2601300.0, back[0].E[1], 1e-3);
            Assert.AreEqual(541.0, back[0].H[0], 1e-3); Assert.AreEqual(542.5, back[0].H[1], 1e-3);
        }

        sealed class ValleyDem : SwissPoliceSim.Core.Terrain.IHeightSource
        {
            // flat 540 m plateau with a 40 m deep valley between E=100..200
            public bool TrySample(double e, double n, out float h) { h = (e > 100 && e < 200) ? 500f : 540f; return true; }
        }

        [Test]
        public void Elevation_BridgeStaysLevel_RoadFollowsTerrain()
        {
            var bridge = new RoadWay { Bridge = true, NodeIds = new long[] { 1, 2, 3, 4 }, E = new[] { 50.0, 120, 180, 250 }, N = new double[4] };
            RoadElevation.Assign(bridge, new ValleyDem());
            Assert.AreEqual(540, bridge.H[1], 1e-6); Assert.AreEqual(540, bridge.H[2], 1e-6);
            var road = new RoadWay { NodeIds = new long[] { 1, 2, 3, 4 }, E = new[] { 50.0, 120, 180, 250 }, N = new double[4] };
            RoadElevation.Assign(road, new ValleyDem());
            Assert.AreEqual(500, road.H[1], 1e-6);
        }

        // 3x3 grid, 100 m spacing; node id = 10*row+col. Horizontal middle row is a fast primary road, others residential.
        static RoadGraph Grid(bool oneWayFastRoad = false)
        {
            var ways = new List<RoadWay>();
            for (int r = 0; r < 3; r++)
            {
                var cls = r == 1 ? RoadClass.Primary : RoadClass.Residential;
                ways.Add(new RoadWay
                {
                    OsmId = 100 + r, Class = cls, MaxSpeedKmh = r == 1 ? 80 : 30,
                    OneWay = r == 1 && oneWayFastRoad ? OneWay.Forward : OneWay.No,
                    NodeIds = new long[] { 10 * r, 10 * r + 1, 10 * r + 2 },
                    E = new[] { 0.0, 100, 200 }, N = new[] { r * 100.0, r * 100.0, r * 100.0 }
                });
            }
            for (int c = 0; c < 3; c++)
                ways.Add(new RoadWay
                {
                    OsmId = 200 + c, Class = RoadClass.Residential, MaxSpeedKmh = 30,
                    NodeIds = new long[] { c, 10 + c, 20 + c }, E = new[] { c * 100.0, c * 100.0, c * 100.0 }, N = new[] { 0.0, 100, 200 }
                });
            return RoadGraph.Build(ways);
        }

        [Test]
        public void Router_PrefersFasterRoad_NotShortestOnly()
        {
            var g = Grid();
            var p = g.FindRoute(0, 100, 200, 100); // along the 80 km/h road
            Assert.IsNotNull(p);
            Assert.AreEqual(200, p.DistanceMeters, 1e-6);
            Assert.AreEqual(200 / (80 / 3.6), p.TimeSeconds, 1e-3);
        }

        [Test]
        public void Router_DetourToFastRoad_WhenFaster()
        {
            var g = Grid();
            // (0,0)->(200,200): any monotone path is 400 m. Via the fast middle row gets 100 m at 80 km/h => faster than all-30.
            var p = g.FindRoute(0, 0, 200, 200);
            double all30 = 400 / (30 / 3.6);
            Assert.Less(p.TimeSeconds, all30);
            Assert.AreEqual(400, p.DistanceMeters, 1e-6);
        }

        [Test]
        public void Router_RespectsOneWay()
        {
            var g = Grid(oneWayFastRoad: true);
            var back = g.FindRoute(200, 100, 0, 100); // against the one-way: must not use the fast road
            Assert.IsNotNull(back);
            Assert.Greater(back.TimeSeconds, 200 / (80 / 3.6) + 1);
        }

        [Test]
        public void NodeLookup_AndHeading()
        {
            var g = Grid();
            int n = g.NearestNode(3, 2);                  // node 0 at (0,0)
            g.GetNodePosition(n, out double e, out double nn);
            Assert.AreEqual(0, e); Assert.AreEqual(0, nn);
            Assert.IsTrue(g.TryGetNodeHeading(n, out double de, out double dn));
            Assert.AreEqual(1.0, Math.Sqrt(de * de + dn * dn), 1e-9);
        }

        [Test]
        public void Router_ReturnsNull_WhenNoNodeNearOrDisconnected()
        {
            var g = Grid();
            Assert.IsNull(g.FindRoute(0, 0, 50000, 50000));
            var a = new RoadWay { OsmId = 1, NodeIds = new long[] { 1, 2 }, E = new[] { 0.0, 100 }, N = new[] { 0.0, 0 } };
            var b = new RoadWay { OsmId = 2, NodeIds = new long[] { 3, 4 }, E = new[] { 1000.0, 1100 }, N = new[] { 0.0, 0 } };
            Assert.IsNull(RoadGraph.Build(new[] { a, b }).FindRoute(0, 0, 1100, 0, 300));
        }
    }
}
