using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Import;
using SwissPoliceSim.Core.Terrain;
using SwissPoliceSim.Core.Util;
using SwissPoliceSim.Core.World;

namespace SwissPoliceSim.Tests
{
    public class CoordinateTests
    {
        [Test]
        public void BernObservatory_Wgs84ToLv95_IsReferencePoint()
        {
            // Old observatory Bern: LV95 2600000/1200000 = 46.9510827861 N, 7.4386324175 E (swisstopo)
            var p = SwissCoordinateConverter.ToLv95(46.9510827861, 7.4386324175);
            Assert.AreEqual(2600000.0, p.E, 1.5);
            Assert.AreEqual(1200000.0, p.N, 1.5);
        }

        [Test]
        public void TicinoReferencePoint_Lv95ToWgs84()
        {
            // swisstopo example: 2700000/1100000 = 46°02'38.87" N, 8°43'49.79" E
            var w = SwissCoordinateConverter.ToWgs84(new Lv95(2700000, 1100000));
            Assert.AreEqual(46.0 + 2 / 60.0 + 38.87 / 3600.0, w.Lat, 2e-5);
            Assert.AreEqual(8.0 + 43 / 60.0 + 49.79 / 3600.0, w.Lon, 2e-5);
        }

        [Test]
        public void RoundTrip_ErrorBelowTwoMetres_AcrossSwitzerland()
        {
            double[,] pts = { { 46.9466, 7.4441 }, { 47.3779, 8.5403 }, { 46.2044, 6.1432 }, { 46.0037, 8.9511 }, { 46.8499, 9.5329 } };
            for (int i = 0; i < pts.GetLength(0); i++)
            {
                var l = SwissCoordinateConverter.ToLv95(pts[i, 0], pts[i, 1]);
                Assert.IsTrue(SwissCoordinateConverter.IsPlausiblySwiss(l), "outside CH box: " + l);
                var w = SwissCoordinateConverter.ToWgs84(l);
                var l2 = SwissCoordinateConverter.ToLv95(w);
                Assert.AreEqual(l.E, l2.E, 2.0); Assert.AreEqual(l.N, l2.N, 2.0); // swisstopo approx. formulas: ~1-2 m at the edges
            }
        }

        [Test]
        public void ZurichIsNorthEastOfBern_ByAbout95Km()
        {
            var bern = SwissCoordinateConverter.ToLv95(46.9466, 7.4441);
            var zh = SwissCoordinateConverter.ToLv95(47.3779, 8.5403);
            double d = Math.Sqrt(Math.Pow(zh.E - bern.E, 2) + Math.Pow(zh.N - bern.N, 2));
            Assert.That(d, Is.InRange(93000, 96000)); // real distance Bern-Zürich (Luftlinie) ≈ 95 km
        }
    }

    public class TileGridTests
    {
        [Test]
        public void TileIdMatchesSwisstopoNumbering()
        {
            Assert.AreEqual(new TileId(2600, 1199), TileGrid.FromLv95(2600500, 1199999.9));
            Assert.AreEqual(new TileId(2599, 1200), TileGrid.FromLv95(2599999.9, 1200000.0));
        }

        [Test]
        public void Ring_ReturnsSquare_NearestFirst()
        {
            var r = TileGrid.Ring(new TileId(10, 10), 2);
            Assert.AreEqual(25, r.Count);
            Assert.AreEqual(new TileId(10, 10), r[0]);
        }
    }

    public class WorldFrameTests
    {
        [Test]
        public void ShiftPreservesWorldPosition()
        {
            var f = new WorldFrame(2600000, 1200000, 500);
            var a = f.ToLocal(2603500.25, 1198000.5, 620);
            f.Shift(3000, -2000);
            var b = f.ToLocal(2603500.25, 1198000.5, 620);
            Assert.AreEqual(a.X - 3000, b.X, 1e-3); Assert.AreEqual(a.Z + 2000, b.Z, 1e-3);
            f.ToWorld(b.X, b.Y, b.Z, out double e, out double n, out double h);
            Assert.AreEqual(2603500.25, e, 1e-3); Assert.AreEqual(1198000.5, n, 1e-3); Assert.AreEqual(620, h, 1e-3);
        }

        [Test]
        public void NeedsRecenter_UsesEuclideanDistance()
        {
            Assert.IsFalse(WorldFrame.NeedsRecenter(1000, 1000, 1500));
            Assert.IsTrue(WorldFrame.NeedsRecenter(1200, 1200, 1500));
        }
    }

    public class HeightTests
    {
        // Synthetic plane h = 500 + 0.1*(E-E0) + 0.2*(N-N0) sampled at cell centres, 10 m cells (100x100 per tile).
        static HeightGrid Plane(TileId t, double cell)
        {
            var g = new HeightGrid(t, cell);
            for (int r = 0; r < g.Size; r++)
                for (int c = 0; c < g.Size; c++)
                    g[c, r] = (float)(500 + 0.1 * ((t.X * 1000 + (c + 0.5) * cell) - 2600000) + 0.2 * ((t.Y * 1000 + (r + 0.5) * cell) - 1200000));
            return g;
        }

        [Test]
        public void Mosaic_BilinearIsExactOnPlane_AcrossTileBorder()
        {
            var m = new HeightMosaic(10);
            m.Add(Plane(new TileId(2600, 1200), 10)); m.Add(Plane(new TileId(2601, 1200), 10));
            foreach (double e in new[] { 2600995.0, 2601000.0, 2601004.9 })
            {
                Assert.IsTrue(m.TrySample(e, 1200500, out float h));
                Assert.AreEqual(500 + 0.1 * (e - 2600000) + 0.2 * 500, h, 1e-2, "E=" + e);
            }
        }

        [Test]
        public void Mosaic_MissingNeighbour_DoesNotThrow_AndReturnsFalseWhenEmpty()
        {
            var m = new HeightMosaic(10);
            m.Add(Plane(new TileId(2600, 1200), 10));
            Assert.IsTrue(m.TrySample(2600999.9, 1200500, out _)); // border cell, neighbour tile absent
            Assert.IsFalse(m.TrySample(2650000, 1200500, out _));
        }

        [Test]
        public void XyzReader_ParsesSwisstopoFormat_CellCentres()
        {
            string xyz = "X Y Z\n2600001 1199999 553.67\n2600003 1199999 553.65\n2600001 1199997 550.00\n";
            var g = XyzHeightGridReader.Read(new MemoryStream(Encoding.ASCII.GetBytes(xyz)), new TileId(2600, 1199), 2);
            Assert.AreEqual(500, g.Size);
            Assert.AreEqual(553.67f, g[0, 499], 1e-3f);
            Assert.AreEqual(553.65f, g[1, 499], 1e-3f);
            Assert.AreEqual(550.00f, g[0, 498], 1e-3f);
        }

        [Test]
        public void XyzReader_RejectsPointOutsideTile()
        {
            var s = new MemoryStream(Encoding.ASCII.GetBytes("X Y Z\n2601500 1199999 1\n"));
            Assert.Throws<FormatException>(() => XyzHeightGridReader.Read(s, new TileId(2600, 1199), 2));
        }

        [Test]
        public void TerrainTile_BakeAndSerialise_RoundTripWithinQuantisation()
        {
            var m = new HeightMosaic(10);
            var t = new TileId(2600, 1200);
            m.Add(Plane(t, 10));
            var d = TerrainTileData.Bake(m, t, 129);
            var ms = new MemoryStream(); d.Write(ms); ms.Position = 0;
            var d2 = TerrainTileData.Read(ms);
            Assert.AreEqual(129, d2.Resolution);
            Assert.AreEqual(d.MinH, d2.MinH); Assert.AreEqual(d.MaxH, d2.MaxH);
            // plane gradient: at tile-local (400, 300) expect 500+40+60 = 600 (+ tolerance for quantisation)
            Assert.AreEqual(600f, d2.SampleLocal(400, 300), 0.05f);
            Assert.AreEqual(TerrainTileData.Step, (d.MaxH - d.MinH) / 65535f, 1e-6f);   // fixed 5 cm vertical quantisation
        }
    }

    public class JsonStacTests
    {
        [Test]
        public void MiniJson_ParsesNestedStructures()
        {
            var o = (System.Collections.Generic.Dictionary<string, object>)MiniJson.Parse(
                "{\"a\":[1,2.5,{\"b\":\"x\\\"y\\u00e4\"}],\"c\":true,\"d\":null}");
            var arr = (System.Collections.Generic.List<object>)o["a"];
            Assert.AreEqual(2.5, (double)arr[1]);
            Assert.AreEqual("x\"yä", ((System.Collections.Generic.Dictionary<string, object>)arr[2])["b"]);
            Assert.AreEqual(true, o["c"]); Assert.IsNull(o["d"]);
        }

        [Test]
        public void Stac_PicksNewestYear_AndRequestedResolution()
        {
            string json = "{\"features\":[" +
                "{\"id\":\"swissalti3d_2019_2600-1199\",\"assets\":{\"swissalti3d_2019_2600-1199_2_2056_5728.xyz.zip\":{\"href\":\"http://old\"}}}," +
                "{\"id\":\"swissalti3d_2022_2600-1199\",\"assets\":{" +
                "\"swissalti3d_2022_2600-1199_0.5_2056_5728.xyz.zip\":{\"href\":\"http://half\"}," +
                "\"swissalti3d_2022_2600-1199_2_2056_5728.xyz.zip\":{\"href\":\"http://new2m\"}," +
                "\"swissalti3d_2022_2600-1199_2_2056_5728.tif\":{\"href\":\"http://tif\"}}}," +
                "{\"id\":\"swissalti3d_2023_2601-1199\",\"assets\":{\"x_2_2056_.xyz.zip\":{\"href\":\"http://other\"}}}]}";
            var a = StacClient.PickNewest(MiniJson.Parse(json), new TileId(2600, 1199), "2");
            Assert.AreEqual("http://new2m", a.Href);
            Assert.AreEqual(2022, a.Year);
            Assert.IsNull(StacClient.PickNewest(MiniJson.Parse(json), new TileId(9, 9), "2"));
        }
    }

    public class RealBernDataTests
    {
        static string Dir()
        {
            string d = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 8 && d != null; i++, d = Path.GetDirectoryName(d))
            {
                string p = Path.Combine(d, "Assets", "StreamingAssets", "SwissWorld", "tiles");
                if (Directory.Exists(p)) return p;
            }
            return null;
        }

        [Test]
        public void BundesplatzTerrain_IsAbout540Metres()
        {
            string dir = Dir(); if (dir == null) Assert.Ignore("Pilot data not baked (run Tools/SwissWorldBaker).");
            var src = new SwissPoliceSim.Core.Storage.LooseTileSource(dir);
            Assert.IsTrue(src.TryRead(new TileId(2600, 1199), out var bytes));
            var tile = SwissPoliceSim.Core.Meshing.TileMeshBuilder.Build(bytes, meshes: false);
            var bp = SwissCoordinateConverter.ToLv95(46.94658, 7.44410);
            Assert.AreEqual(new TileId(2600, 1199), TileGrid.FromLv95(bp.E, bp.N));
            float h = tile.Terrain.SampleLocal(bp.E - TileGrid.MinE(tile.Tile), bp.N - TileGrid.MinN(tile.Tile));
            Assert.That(h, Is.InRange(530f, 552f), "Bern lies at ~540 m a.s.l.");
        }

        [Test]
        public void RealRoadNetwork_RoutesBundesplatzToWankdorfStadium()
        {
            string dir = Dir(); if (dir == null) Assert.Ignore("Pilot data not baked.");
            var src = new SwissPoliceSim.Core.Storage.LooseTileSource(dir);
            var ways = new System.Collections.Generic.List<SwissPoliceSim.Core.Roads.RoadWay>();
            foreach (var t in src.Tiles) { src.TryRead(t, out var b); ways.AddRange(SwissPoliceSim.Core.Meshing.TileMeshBuilder.Build(b, meshes: false).Roads); }
            Assert.Greater(ways.Count, 1000);
            var g = SwissPoliceSim.Core.Roads.RoadGraph.Build(ways);
            var a = SwissCoordinateConverter.ToLv95(46.94658, 7.44410);
            var bb = SwissCoordinateConverter.ToLv95(46.96330, 7.46490);
            var r = g.FindRoute(a.E, a.N, bb.E, bb.N);
            Assert.IsNotNull(r, "no route: network not connected across tiles?");
            double straight = Math.Sqrt(Math.Pow(a.E - bb.E, 2) + Math.Pow(a.N - bb.N, 2));
            TestContext.WriteLine($"straight {straight:F0} m, route {r.DistanceMeters:F0} m, {r.TimeSeconds / 60:F1} min, nodes {r.Count}");
            Assert.That(r.DistanceMeters, Is.InRange(straight, straight * 2.0));
        }

        [Test]
        public void RealBern_DataIsRich_BuildingsTreesWater()
        {
            string dir = Dir(); if (dir == null) Assert.Ignore("Pilot data not baked.");
            var src = new SwissPoliceSim.Core.Storage.LooseTileSource(dir);
            int buildings = 0, measured = 0, trees = 0, water = 0, crossings = 0, signals = 0;
            foreach (var t in src.Tiles)
            {
                src.TryRead(t, out var b); var c = SwissPoliceSim.Core.Meshing.TileMeshBuilder.Build(b, meshes: false);
                buildings += c.Buildings.Count; foreach (var x in c.Buildings) if (x.HeightMeasured) measured++;
                trees += c.Trees.Count; if (c.WaterMesh != null) water += c.WaterMesh.VertexCount > 0 ? 1 : 0;
                foreach (var p in c.Points) { if (p.Kind == SwissPoliceSim.Core.Features.PointKind.Crossing) crossings++; if (p.Kind == SwissPoliceSim.Core.Features.PointKind.TrafficSignals) signals++; }
            }
            TestContext.WriteLine($"buildings {buildings} (measured {measured}), trees {trees}, tiles with water {water}, crossings {crossings}, signals {signals}");
            Assert.Greater(buildings, 10000); Assert.Greater(measured * 1.0 / buildings, 0.9);
            Assert.Greater(trees, 50000); Assert.Greater(water, 3); Assert.Greater(crossings, 100); Assert.Greater(signals, 20);
        }
    }
}
