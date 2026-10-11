using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Game;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Legal;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Police;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Storage;
using SwissPoliceSim.Core.Traffic;

namespace SwissPoliceSim.Tests
{
    static class TestPaths
    {
        public static string Find(params string[] rel)
        {
            string d = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 8 && d != null; i++, d = Path.GetDirectoryName(d)) { var p = Path.Combine(new[] { d }.Concat(rel).ToArray()); if (Directory.Exists(p) || File.Exists(p)) return p; }
            return null;
        }
        public static AdminBoundaries Admin()
        {
            var p = Find("Assets", "StreamingAssets", "SwissWorld", "admin.spj"); if (p == null) Assert.Ignore("admin.spj not baked.");
            using (var f = File.OpenRead(p)) return AdminBoundaries.Load(f);
        }
        public static ObvCatalog Obv()
        {
            var p = Find("docs", "law", "SR_314.11.txt"); if (p == null) Assert.Ignore("law texts missing.");
            return ObvCatalog.Parse(File.ReadAllText(p));
        }
    }

    public class PoliceJurisdictionTests
    {
        [Test]
        public void AllCantonalCorpsExist_WithBfsNumbersMatchingMunicipalities()
        {
            for (int k = 1; k <= 26; k++) Assert.IsNotNull(PoliceCatalog.CantonalOf(k), "canton " + k);
            var admin = TestPaths.Admin();
            foreach (var c in PoliceCatalog.All.Where(x => x.Level == CorpsLevel.Municipal))
            {
                var m = admin.Municipalities.Find(x => x.Id == c.MunicipalityBfs);
                Assert.IsNotNull(m, c.Id + " BFS " + c.MunicipalityBfs);
                Assert.AreEqual(c.CantonId, m.Canton, c.Id);
            }
        }

        [Test]
        public void KapoBern_InZurich_IsOtherCanton_NoConcordat()
        {
            var admin = TestPaths.Admin(); var bern = PoliceCatalog.ById("KAPO_BE");
            var z = SwissCoordinateConverter.ToLv95(47.3779, 8.5403);
            var st = JurisdictionManager.Evaluate(admin, bern, z.E, z.N);
            Assert.AreEqual(JurisdictionKind.OtherCanton, st.Kind); Assert.IsFalse(st.CooperationAgreement);
            var b = SwissCoordinateConverter.ToLv95(46.94658, 7.4441);
            Assert.AreEqual(JurisdictionKind.OwnTerritory, JurisdictionManager.Evaluate(admin, bern, b.E, b.N).Kind);
        }

        [Test]
        public void Concordat_AargauInBasel_Cooperation()
        {
            var admin = TestPaths.Admin(); var ag = PoliceCatalog.ById("KAPO_AG");
            var bs = SwissCoordinateConverter.ToLv95(47.5561, 7.5927);
            var st = JurisdictionManager.Evaluate(admin, ag, bs.E, bs.N);
            Assert.AreEqual(JurisdictionKind.OtherCanton, st.Kind); Assert.IsTrue(st.CooperationAgreement);
        }

        [Test]
        public void MunicipalCorps_OtherMunicipalityInOwnCanton()
        {
            var admin = TestPaths.Admin(); var stapo = PoliceCatalog.ById("STAPO_ZH");
            var w = SwissCoordinateConverter.ToLv95(47.5001, 8.7240);   // Winterthur
            Assert.AreEqual(JurisdictionKind.OtherMunicipality, JurisdictionManager.Evaluate(admin, stapo, w.E, w.N).Kind);
            var z = SwissCoordinateConverter.ToLv95(47.3779, 8.5403);
            Assert.AreEqual(JurisdictionKind.OwnTerritory, JurisdictionManager.Evaluate(admin, stapo, z.E, z.N).Kind);
        }
    }

    public class ClockWeatherTests
    {
        [Test]
        public void SummerTime_Boundaries()
        {
            Assert.IsTrue(GameClock.IsSummerTime(new DateTime(2026, 7, 1)));
            Assert.IsFalse(GameClock.IsSummerTime(new DateTime(2026, 1, 15)));
            Assert.IsFalse(GameClock.IsSummerTime(new DateTime(2026, 10, 26, 12, 0, 0)));
            Assert.IsTrue(GameClock.IsSummerTime(new DateTime(2026, 10, 24, 12, 0, 0)));
        }

        [Test]
        public void Sun_Bern_SolsticeNoon_HighWinterNoon_LowNightBelowHorizon()
        {
            SunModel.Position(new DateTime(2026, 6, 21, 13, 30, 0), 46.95, 7.44, out double ev, out double az);
            Assert.That(ev, Is.InRange(60, 67), "summer noon elevation (90-46.95+23.44 = 66.5 max)"); Assert.That(az, Is.InRange(150, 210));
            SunModel.Position(new DateTime(2026, 12, 21, 12, 30, 0), 46.95, 7.44, out ev, out az);
            Assert.That(ev, Is.InRange(16, 21), "winter noon elevation ≈ 19.6");
            SunModel.Position(new DateTime(2026, 6, 21, 2, 0, 0), 46.95, 7.44, out ev, out _);
            Assert.Less(ev, 0);
            Assert.AreEqual(0f, SunModel.Daylight(-20)); Assert.AreEqual(1f, SunModel.Daylight(30));
        }

        [Test]
        public void Weather_IsDeterministic_ColderWithAltitude_SnowAtAltitudeInWinter()
        {
            var t = new DateTime(2026, 1, 12, 9, 0, 0);
            var a = WeatherModel.Sample(2600000, 1200000, 540, t); var b = WeatherModel.Sample(2600000, 1200000, 540, t);
            Assert.AreEqual(a.TemperatureC, b.TemperatureC);
            var hi = WeatherModel.Sample(2600000, 1200000, 2500, t);
            Assert.AreEqual(a.TemperatureC - 0.0065f * (2500 - 540), hi.TemperatureC, 0.01);
            int snow = 0, rain = 0;
            for (int i = 0; i < 400; i++)
            {
                var w = WeatherModel.Sample(2600000 + i * 3000, 1200000 + i * 700, 2200, new DateTime(2026, 1, 1).AddHours(i * 5));
                if (w.IsSnow) snow++; if (w.IsRain) rain++;
                Assert.That(w.RoadFriction, Is.InRange(0.12f, 1f));
            }
            Assert.Greater(snow, 5); Assert.AreEqual(0, rain, "no rain at 2200 m in winter");
        }

        [Test]
        public void Weather_Summer_WarmerThanWinter_OnAverage()
        {
            double s = 0, w = 0; int n = 200;
            for (int i = 0; i < n; i++)
            {
                s += WeatherModel.Sample(2600000, 1200000, 540, new DateTime(2026, 7, 1).AddHours(i * 3.7)).TemperatureC;
                w += WeatherModel.Sample(2600000, 1200000, 540, new DateTime(2026, 1, 1).AddHours(i * 3.7)).TemperatureC;
            }
            Assert.Greater(s / n - w / n, 10);
        }
    }

    public class DispatchTests
    {
        static TrafficNetwork LoadPilot()
        {
            var dir = TestPaths.Find("Assets", "StreamingAssets", "SwissWorld", "tiles"); if (dir == null) Assert.Ignore("Pilot data not baked.");
            var src = new LooseTileSource(dir); var ways = new List<RoadWay>(); var pts = new List<PointFeature>();
            foreach (var t in src.Tiles) { src.TryRead(t, out var b); var c = TileMeshBuilder.Build(b, meshes: false); ways.AddRange(c.Roads); pts.AddRange(c.Points); }
            return TrafficNetwork.Build(ways, pts);
        }

        [Test]
        public void Dispatch_GeneratesIncidentsOnRealStreets_WithRadioText_AndTruth()
        {
            var net = LoadPilot(); var admin = TestPaths.Admin();
            var bp = SwissCoordinateConverter.ToLv95(46.94658, 7.4441);
            var dc = new DispatchCenter(net, admin, 5);
            var types = new HashSet<IncidentType>(); int made = 0;
            for (int i = 0; i < 150; i++)
            {
                var inc = dc.Generate(bp.E, bp.N, i * 60f, 8 + (i % 14));
                if (inc == null) continue; made++; types.Add(inc.Type);
                Assert.IsNotEmpty(inc.RadioText); Assert.IsTrue(inc.RadioText.StartsWith("Zentrale"), inc.RadioText);
                Assert.IsNotEmpty(inc.Address); Assert.IsNotEmpty(inc.Municipality);
                Assert.IsNotEmpty(inc.Scenario.Truth); if (inc.Type != IncidentType.RoadHazard) Assert.IsNotEmpty(inc.Scenario.Roles.ToArray());
                double d = Math.Sqrt((inc.E - bp.E) * (inc.E - bp.E) + (inc.N - bp.N) * (inc.N - bp.N)); Assert.LessOrEqual(d, 3700);
                if (inc.Type == IncidentType.TrafficAccidentInjured) Assert.IsTrue(inc.Scenario.NeedsAmbulance);
            }
            TestContext.WriteLine($"{made} incidents, {types.Count} types"); TestContext.WriteLine(dc.Incidents[0].RadioText);
            Assert.Greater(made, 120); Assert.GreaterOrEqual(types.Count, 6);
            var ids = dc.Incidents.Select(x => x.Id).ToList(); Assert.AreEqual(ids.Count, ids.Distinct().Count());
        }
    }

    public class CareerReportSaveTests
    {
        static LegalAssessment Bad(int sev, params string[] norms) { var a = new LegalAssessment { Headline = "Test" }; a.Fail(sev, "Test-Verstoss", norms); return a; }

        [Test]
        public void Career_LawfulWorkRaisesRating_FinesGiveNoXp()
        {
            var c = new CareerState(1); var ok = new LegalAssessment(); ok.Ok("ok");
            float l0 = c.Legality; c.ApplyAssessment(ok, 0, true, 0);
            Assert.Greater(c.Legality, l0);
            double xp = c.Xp; for (int i = 0; i < 100; i++) c.RegisterFine();
            Assert.AreEqual(xp, c.Xp); Assert.AreEqual(100, c.FinesIssued);
        }

        [Test]
        public void Career_Unlawful_EscalatesThroughLadder_AndCanEndInDismissal()
        {
            var c = new CareerState(3); var kinds = new HashSet<ConsequenceKind>();
            for (int i = 0; i < 40 && !c.Dismissed; i++)
                foreach (var q in c.ApplyAssessment(Bad(3, "CANTONAL_POLICE_ACTS"), 0.9f, true, i * 100f)) kinds.Add(q.Kind);
            TestContext.WriteLine(string.Join(", ", kinds));
            Assert.IsTrue(kinds.Contains(ConsequenceKind.CitizenComplaint)); Assert.IsTrue(kinds.Contains(ConsequenceKind.InternalInvestigation));
            Assert.IsTrue(kinds.Contains(ConsequenceKind.Disciplinary)); Assert.IsTrue(c.Dismissed);
            Assert.IsFalse(c.CanPromote);
            Assert.AreEqual(ConsequenceKind.Admonition, new CareerState(1).ApplyAssessment(Bad(1, "CANTONAL_POLICE_ACTS"), 0, false, 0)[0].Kind);
        }

        [Test]
        public void Career_Promotion_RequiresXpAndCleanRecord()
        {
            var c = new CareerState(1); c.Xp = 500; Assert.IsFalse(c.CanPromote, "average rating (70) is not enough"); c.Legality = c.Professionalism = c.DeEscalation = c.Reporting = c.Community = 80; Assert.IsTrue(c.CanPromote); Assert.IsTrue(c.TryPromote()); Assert.AreEqual(1, c.RankIndex);
            c.Xp = 5000; c.InvestigationsOpen = 1; Assert.IsFalse(c.CanPromote);
            c.InvestigationsOpen = 0; c.Legality = 40; Assert.IsFalse(c.CanPromote);
        }

        [Test]
        public void Report_Speeding_FineAmountAndProcedureChecked()
        {
            var obv = TestPaths.Obv();
            var fine = SpeedingRules.Assess(obv, 58, 50, SpeedZone.Urban);
            var good = new PoliceReport { Kind = ReportKind.Ordnungsbusse, Violation = ViolationType.Speeding, MeasuredKmh = 58, LimitKmh = 50, Zone = SpeedZone.Urban, FineChf = fine.Amount };
            var ev = ReportEvaluator.EvaluateTraffic(good, obv, ViolationType.Speeding, 58, 50, SpeedZone.Urban);
            Assert.IsTrue(ev.Correct, ev.ToString());
            var wrong = new PoliceReport { Kind = ReportKind.Ordnungsbusse, Violation = ViolationType.Speeding, MeasuredKmh = 58, LimitKmh = 50, Zone = SpeedZone.Urban, FineChf = fine.Amount + 80 };
            Assert.IsFalse(ReportEvaluator.EvaluateTraffic(wrong, obv, ViolationType.Speeding, 58, 50, SpeedZone.Urban).Correct);
            var big = new PoliceReport { Kind = ReportKind.Ordnungsbusse, Violation = ViolationType.Speeding, MeasuredKmh = 90, LimitKmh = 50, Zone = SpeedZone.Urban, FineChf = 250 };
            var e3 = ReportEvaluator.EvaluateTraffic(big, obv, ViolationType.Speeding, 90, 50, SpeedZone.Urban);
            Assert.IsFalse(e3.Correct); StringAssert.Contains("Verzeigung", e3.ToString());
        }

        [Test]
        public void Report_Accident_MissingInjuredAndWrongCulprit()
        {
            var sc = new Scenario { AtFaultRole = 1, NeedsAmbulance = true, NeedsTow = true, AtFaultViolation = ViolationType.Speeding };
            var bad = new PoliceReport { Kind = ReportKind.Unfallrapport, AtFaultRole = 0 };
            var e = ReportEvaluator.EvaluateAccident(bad, sc); Assert.IsFalse(e.Correct); Assert.IsTrue(e.Falsified);
            var good = new PoliceReport { Kind = ReportKind.Unfallrapport, AtFaultRole = 1, InjuredReported = true, AmbulanceCalled = true, TowCalled = true, Violation = ViolationType.Speeding };
            Assert.IsTrue(ReportEvaluator.EvaluateAccident(good, sc).Correct);
        }

        [Test]
        public void Save_RoundTrip_PreservesEverything_AndAtomicFileWrite()
        {
            var g = new GameSave { CorpsId = "KAPO_ZH", RankIndex = 3, Xp = 1234.5, PlayerE = 2600123.456, PlayerN = 1199876.5, PlayerH = 540.2, InVehicle = false, Fuel = 0.63f, GameSeconds = 98765.4, Seed = 77, Region = "Bern" };
            g.Incidents.Add(new GameSave.SavedIncident { Id = 4, Type = 2, State = 1, Urgency = 3, E = 2600500, N = 1200500, Address = "Länggassstrasse \"1\"\nzweite Zeile", Municipality = "Bern", Canton = "BE", RadioText = "Zentrale…", AmbulanceCalled = true });
            g.Cases.Add(new GameSave.SavedCase { Kind = "Ordnungsbusse", Summary = "Tempo 58/50", Plate = "BE 123456", Person = "Müller", FineChf = 40, Correct = true, Time = 12.5 });
            g.Consequences.Add("Ermahnung"); g.Flags.Add("tutorial_done");
            var r = GameSave.FromJson(g.ToJson());
            Assert.AreEqual(g.CorpsId, r.CorpsId); Assert.AreEqual(3, r.RankIndex); Assert.AreEqual(1234.5, r.Xp); Assert.AreEqual(2600123.456, r.PlayerE); Assert.AreEqual(1199876.5, r.PlayerN);
            Assert.IsFalse(r.InVehicle); Assert.AreEqual(0.63f, r.Fuel); Assert.AreEqual(98765.4, r.GameSeconds); Assert.AreEqual(77, r.Seed);
            Assert.AreEqual(1, r.Incidents.Count); Assert.AreEqual(g.Incidents[0].Address, r.Incidents[0].Address); Assert.IsTrue(r.Incidents[0].AmbulanceCalled);
            Assert.AreEqual("Müller", r.Cases[0].Person); Assert.AreEqual("Ermahnung", r.Consequences[0]); Assert.AreEqual("tutorial_done", r.Flags[0]);
            string path = Path.Combine(Path.GetTempPath(), "sps_save_" + Guid.NewGuid().ToString("N"), "slot1.json");
            g.SaveToFile(path); g.Xp = 9; g.SaveToFile(path);
            Assert.AreEqual(9, GameSave.LoadFromFile(path).Xp); Assert.IsTrue(File.Exists(path + ".bak"));
            File.WriteAllText(path, "{broken"); Assert.AreEqual(1234.5, GameSave.LoadFromFile(path).Xp, "falls back to backup");
            Directory.Delete(Path.GetDirectoryName(path), true);
        }

        [Test]
        public void Save_RejectsNewerVersion()
        {
            Assert.Throws<InvalidDataException>(() => GameSave.FromJson("{\"version\":999}"));
        }
    }
}

namespace SwissPoliceSim.Tests
{
    using SwissPoliceSim.Core.Roads;
    public class HierarchicalRouterTests
    {
        static LooseTileSource Src() { var d = TestPaths.Find("Assets", "StreamingAssets", "SwissWorld", "tiles"); if (d == null) NUnit.Framework.Assert.Ignore("pilot not baked"); return new LooseTileSource(d); }

        static RoadGraph FullGraph(LooseTileSource src)
        {
            var g = new RoadGraph();
            foreach (var t in src.Tiles) { src.TryRead(t, out var b); var c = SwissPoliceSim.Core.Storage.TileContainer.Deserialize(b); if (c.TryGet("ROAD", out var r)) using (var ms = new System.IO.MemoryStream(r)) foreach (var w in RoadTileFile.Read(ms, out _)) g.AddWay(w); }
            return g;
        }

        [NUnit.Framework.Test]
        public void ShortRoute_IsExact_AndLoadsOnlyTheCorridor()
        {
            var src = Src(); var full = FullGraph(src); var router = new HierarchicalRouter(src);
            var a = SwissCoordinateConverter.ToLv95(46.94658, 7.44410); var b = SwissCoordinateConverter.ToLv95(46.9633, 7.4649);
            var exact = full.FindRoute(a.E, a.N, b.E, b.N); var h = router.Route(a.E, a.N, b.E, b.N);
            NUnit.Framework.Assert.IsNotNull(h);
            NUnit.Framework.Assert.AreEqual(exact.TimeSeconds, h.TimeSeconds, exact.TimeSeconds * 0.02);
            TestContext.WriteLine($"exact {exact.TimeSeconds:F0}s {exact.DistanceMeters:F0}m; hier {h.TimeSeconds:F0}s; tiles {router.TilesLoadedLastQuery}");
        }

        [NUnit.Framework.Test]
        public void LongRoute_UsesArterialSkeleton_WithSmallDetour_AndBoundedMemory()
        {
            var src = Src(); var full = FullGraph(src);
            var router = new HierarchicalRouter(src) { LocalRadiusMeters = 700, ShortRouteMeters = 1500, TileCacheCapacity = 8 };
            var ways = HierarchicalRouter.CollectArterial(src, RoadClass.SecondaryLink); router.SetArterial(ways);
            int total = 0; foreach (var l in ways.Values) total += l.Count;
            TestContext.WriteLine($"arterial ways {total}, nodes {router.ArterialNodes}");
            var rnd = new System.Random(3); var all = new List<TileId>(src.Tiles); int ok = 0; double worst = 1;
            for (int i = 0; i < 25; i++)
            {
                var ta = all[rnd.Next(all.Count)]; var tb = all[rnd.Next(all.Count)];
                double ae = TileGrid.MinE(ta) + rnd.Next(1000), an = TileGrid.MinN(ta) + rnd.Next(1000), be = TileGrid.MinE(tb) + rnd.Next(1000), bn = TileGrid.MinN(tb) + rnd.Next(1000);
                var exact = full.FindRoute(ae, an, be, bn); if (exact == null) continue;
                var h = router.Route(ae, an, be, bn); if (h == null) continue;
                ok++; worst = Math.Max(worst, h.TimeSeconds / exact.TimeSeconds);
                NUnit.Framework.Assert.LessOrEqual(router.CachedTiles, 8);
                NUnit.Framework.Assert.GreaterOrEqual(h.TimeSeconds, exact.TimeSeconds * 0.999, "hierarchical route can never beat the optimum");
            }
            TestContext.WriteLine($"{ok} routes compared, worst ratio {worst:F2}");
            NUnit.Framework.Assert.Greater(ok, 12); NUnit.Framework.Assert.LessOrEqual(worst, 1.6);
        }

        [NUnit.Framework.Test]
        public void ArterialPack_RoundTrips()
        {
            var src = Src(); var ways = HierarchicalRouter.CollectArterial(src, RoadClass.SecondaryLink);
            string p = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "art_" + Guid.NewGuid().ToString("N") + ".sprg");
            HierarchicalRouter.SaveArterial(p, ways); var r = new HierarchicalRouter(src); r.LoadArterial(p);
            var r2 = new HierarchicalRouter(src); r2.SetArterial(ways);
            NUnit.Framework.Assert.AreEqual(r2.ArterialNodes, r.ArterialNodes); System.IO.File.Delete(p);
        }
    }
}

namespace SwissPoliceSim.Tests
{
    using SwissPoliceSim.Core.Terrain;
    public class ScalingStorageTests
    {
        [NUnit.Framework.Test]
        public void TerrainV2_RoundTripsExactly_AndIsSmallerThanRawQ16()
        {
            var rnd = new System.Random(4); int n = 257; var td = new TerrainTileData { Tile = new TileId(2600, 1199), Resolution = n, Q = new ushort[n * n], MinH = 500, MaxH = 500 + 65535 * TerrainTileData.Step };
            for (int r = 0; r < n; r++) for (int c = 0; c < n; c++) td.Q[r * n + c] = (ushort)(2000 + 600 * Math.Sin(c * 0.03) + 500 * Math.Cos(r * 0.02) + rnd.Next(3));
            var b = td.ToBytes(); var back = TerrainTileData.Read(new System.IO.MemoryStream(b));
            NUnit.Framework.Assert.AreEqual(td.Q, back.Q); NUnit.Framework.Assert.AreEqual(td.MinH, back.MinH);
            NUnit.Framework.Assert.Less(b.Length, n * n * 2 * 0.7, "predictive coding must beat raw ushorts");
            var coarse = td.Downsample(4); NUnit.Framework.Assert.AreEqual(65, coarse.Resolution); NUnit.Framework.Assert.AreEqual(td.Q[4 * n + 8], coarse.Q[1 * 65 + 2]);
        }

        [NUnit.Framework.Test]
        public void PilotTiles_HaveCoarseChunk_AndMacroBlocksCoverThem()
        {
            var d = TestPaths.Find("Assets", "StreamingAssets", "SwissWorld", "tiles"); if (d == null) NUnit.Framework.Assert.Ignore("pilot not baked");
            var src = new LooseTileSource(d); int checkedTiles = 0;
            foreach (var t in src.Tiles)
            {
                src.TryRead(t, out var bytes); var c = SwissPoliceSim.Core.Storage.TileContainer.Deserialize(bytes);
                NUnit.Framework.Assert.IsTrue(c.TryGet("TERC", out var terc)); var coarse = TerrainTileData.Read(new System.IO.MemoryStream(terc)); NUnit.Framework.Assert.AreEqual(65, coarse.Resolution);
                var fine = TerrainTileData.Read(new System.IO.MemoryStream(c.Chunks["TERR"]));
                NUnit.Framework.Assert.AreEqual(fine.SampleLocal(500, 500), coarse.SampleLocal(500, 500), 3.0, "coarse level must approximate the fine terrain"); checkedTiles++;
            }
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "macro_" + Guid.NewGuid().ToString("N"));
            int blocks = MacroTerrainBuilder.Build(src, tmp);
            NUnit.Framework.Assert.GreaterOrEqual(blocks, 1);
            var anyTile = new List<TileId>(src.Tiles)[0]; var (bx, by) = MacroBlock.BlockOf(anyTile);
            MacroBlock mb; using (var fs = System.IO.File.OpenRead(System.IO.Path.Combine(tmp, MacroBlock.FileName(bx, by)))) mb = MacroBlock.Read(fs);
            double e = TileGrid.MinE(anyTile) + 500, n = TileGrid.MinN(anyTile) + 500; src.TryRead(anyTile, out var ab);
            var fineT = TerrainTileData.Read(new System.IO.MemoryStream(SwissPoliceSim.Core.Storage.TileContainer.Deserialize(ab).Chunks["TERR"]));
            NUnit.Framework.Assert.AreEqual(fineT.SampleLocal(500, 500), mb.Sample(e, n), 6.0);
            NUnit.Framework.Assert.IsTrue(mb.Present[(anyTile.Y - by * 8) * 8 + (anyTile.X - bx * 8)]);
            System.IO.Directory.Delete(tmp, true);
        }
    }
}
