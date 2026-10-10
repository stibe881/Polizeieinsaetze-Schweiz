using System.IO;
using NUnit.Framework;
using SwissPoliceSim.Core.Geo;

namespace SwissPoliceSim.Tests
{
    public class AdminBoundaryTests
    {
        static AdminBoundaries Load()
        {
            string d = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 8 && d != null; i++, d = Path.GetDirectoryName(d))
            {
                string p = Path.Combine(d, "Assets", "StreamingAssets", "SwissWorld", "admin.spj");
                if (File.Exists(p)) using (var f = File.OpenRead(p)) return AdminBoundaries.Load(f);
            }
            Assert.Ignore("admin.spj not baked."); return null;
        }

        [Test]
        public void AllTwentySixCantons_AndOver2000Municipalities()
        {
            var a = Load();
            Assert.AreEqual(26, a.Cantons.Count);
            for (int k = 1; k <= 26; k++) Assert.IsTrue(a.Cantons.Exists(c => c.Id == k), "canton " + k);
            Assert.Greater(a.Municipalities.Count, 2000);
        }

        [TestCase(46.94658, 7.44410, "BE", "Bern")]
        [TestCase(47.3779, 8.5403, "ZH", "Zürich")]
        [TestCase(47.5561, 7.5927, "BS", "Basel")]
        [TestCase(47.0516, 8.3076, "LU", "Luzern")]
        [TestCase(46.2074, 6.1556, "GE", null)]
        [TestCase(46.0037, 8.9511, "TI", "Lugano")]
        [TestCase(46.8499, 9.5329, "GR", "Chur")]
        [TestCase(47.4245, 9.3767, "SG", "St. Gallen")]
        [TestCase(46.2331, 7.3589, "VS", "Sion")]
        [TestCase(46.9930, 6.9319, "NE", "Neuchâtel")]
        public void Lookup_RealCities(double lat, double lon, string canton, string muni)
        {
            var a = Load(); var p = SwissCoordinateConverter.ToLv95(lat, lon);
            var c = a.FindCanton(p.E, p.N);
            Assert.IsNotNull(c, "no canton at " + p);
            Assert.AreEqual(canton, AdminBoundaries.CantonAbbr[c.Id]);
            var m = a.FindMunicipality(p.E, p.N);
            if (muni != null) { Assert.IsNotNull(m); StringAssert.Contains(muni, m.Name); }
        }

        [Test]
        public void CantonBorder_BernFribourg_DistanceIsSmallNearBorder()
        {
            var a = Load();
            // Kerzers (FR) is near the BE/FR/NE/VD cluster; a point deep inside Bern is far from any border
            var inner = SwissCoordinateConverter.ToLv95(46.94658, 7.44410);
            Assert.Greater(a.DistanceToCantonBorder(inner.E, inner.N, 2), 3000);
        }
    }
}
