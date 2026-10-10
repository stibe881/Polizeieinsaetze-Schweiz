using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geometry;
using SwissPoliceSim.Core.Osm;
using SwissPoliceSim.Core.Roads;

namespace SwissPoliceSim.Tests
{
    public class SpeedLimitTests
    {
        static RoadWay Way(RoadClass c, int maxspeed = 0, bool motorroad = false, int zone = 0)
            => new RoadWay { Class = c, MaxSpeedKmh = maxspeed, Motorroad = motorroad, ZoneSpeedKmh = zone,
                NodeIds = new long[] { 1, 2 }, E = new[] { 0.0, 100 }, N = new[] { 0.0, 0 } };

        [Test]
        public void Rules_Priority_And_LegalDefaults()
        {
            var w = Way(RoadClass.Residential, maxspeed: 30); SpeedLimitResolver.Resolve(w, urban: false);
            Assert.AreEqual(30, w.SpeedKmh); Assert.AreEqual(SpeedSource.Tagged, w.SpeedBasis);

            w = Way(RoadClass.Residential, zone: 30); SpeedLimitResolver.Resolve(w, true);
            Assert.AreEqual(30, w.SpeedKmh); Assert.AreEqual(SpeedSource.Zone, w.SpeedBasis);

            w = Way(RoadClass.LivingStreet); SpeedLimitResolver.Resolve(w, true);
            Assert.AreEqual(20, w.SpeedKmh);

            w = Way(RoadClass.Motorway); SpeedLimitResolver.Resolve(w, false);
            Assert.AreEqual(120, w.SpeedKmh); Assert.AreEqual(SpeedSource.LegalMotorway, w.SpeedBasis);

            w = Way(RoadClass.Primary, motorroad: true); SpeedLimitResolver.Resolve(w, false);
            Assert.AreEqual(100, w.SpeedKmh);

            w = Way(RoadClass.Secondary); SpeedLimitResolver.Resolve(w, true);
            Assert.AreEqual(50, w.SpeedKmh); Assert.AreEqual(SpeedSource.LegalUrban, w.SpeedBasis);
            SpeedLimitResolver.Resolve(w, false);
            Assert.AreEqual(80, w.SpeedKmh); Assert.AreEqual(SpeedSource.LegalRural, w.SpeedBasis);
        }

        [Test]
        public void Split_WhereRoadEntersBuiltUpArea()
        {
            var f = new ExtractedFeatures();
            var lu = new LandUseFeature { Class = LandUseClass.Residential };
            lu.Polygon.Outer = new List<Vec2d> { new Vec2d(300, -200), new Vec2d(1000, -200), new Vec2d(1000, 200), new Vec2d(300, 200) };
            lu.Polygon.Normalize(); f.LandUse.Add(lu);
            var mask = new BuiltUpMask(-100, -300, 1100, 300, f);
            var w = new RoadWay { Class = RoadClass.Secondary, NodeIds = new long[7], E = new double[7], N = new double[7] };
            for (int i = 0; i < 7; i++) { w.NodeIds[i] = i + 1; w.E[i] = i * 100.0; }
            var parts = SpeedLimitResolver.ResolveAndSplit(w, mask);
            Assert.AreEqual(2, parts.Count);
            Assert.AreEqual(80, parts[0].SpeedKmh); Assert.AreEqual(50, parts[1].SpeedKmh);
            Assert.AreEqual(parts[0].NodeIds.Last(), parts[1].NodeIds.First());
        }

        static string Dir()
        {
            string d = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 8 && d != null; i++, d = Path.GetDirectoryName(d))
            {
                string p = Path.Combine(d, ".cache", "swiss-import");
                if (Directory.Exists(p)) return p;
            }
            return null;
        }

        [Test]
        public void RealBern_BuiltUpModel_ReproducesTaggedLimits()
        {
            string dir = Dir();
            if (dir == null) Assert.Ignore("OSM cache not present (run Tools/SwissWorldBaker).");
            var ds = new OsmDataset();
            foreach (var f in Directory.GetFiles(dir, "osm_OsmApi_*.osm.xml")) using (var s = File.OpenRead(f)) ds.Add(s);
            var feat = OsmFeatureExtractor.Extract(ds);
            var mask = new BuiltUpMask(2597000, 1196000, 2604000, 1203000, feat);

            int total = 0, agree = 0, u = 0, ua = 0, r80 = 0, ra = 0; double lenTotal = 0, lenAgree = 0;
            foreach (var r in feat.Roads)
            {
                if (r.MaxSpeedKmh != 50 && r.MaxSpeedKmh != 80) continue;
                if (r.Class == RoadClass.Motorway || r.Class == RoadClass.Trunk || r.Motorroad) continue;
                var probe = new RoadWay { Class = r.Class, NodeIds = r.NodeIds, E = r.E, N = r.N };
                var parts = SpeedLimitResolver.ResolveAndSplit(probe, mask);
                foreach (var p in parts) { double len = p.Length(); lenTotal += len; if (p.SpeedKmh == r.MaxSpeedKmh) lenAgree += len; else TestContext.WriteLine($"MISS {r.Class} tag={r.MaxSpeedKmh} got={p.SpeedKmh} len={len:F0} E={p.E[0]:F0} N={p.N[0]:F0} {r.Name}"); }
                total++;
                bool ok = parts.All(p => p.SpeedKmh == r.MaxSpeedKmh);
                if (ok) agree++;
                if (r.MaxSpeedKmh == 50) { u++; if (ok) ua++; } else { r80++; if (ok) ra++; }
            }
            TestContext.WriteLine($"tagged 50/80 ways: {total}; fully agreeing {agree} ({100.0 * agree / total:F1}%); by length {100 * lenAgree / lenTotal:F1}%; 50: {ua}/{u}; 80: {ra}/{r80}");
            Assert.Greater(total, 100);
            Assert.Greater(lenAgree / lenTotal, 0.85);
        }
    }
}
