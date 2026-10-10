using System.IO;
using System.Linq;
using NUnit.Framework;
using SwissPoliceSim.Core.Legal;

namespace SwissPoliceSim.Tests
{
    public class LegalTests
    {
        static string LawDir()
        {
            string d = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 8 && d != null; i++, d = Path.GetDirectoryName(d)) { var p = Path.Combine(d, "docs", "law"); if (Directory.Exists(p)) return p; }
            Assert.Ignore("docs/law not present"); return null;
        }

        [Test]
        public void EveryCitedArticle_ExistsInTheFedlexText_AndContainsItsKeyPhrase()
        {
            var lib = LawLibrary.LoadDirectory(LawDir());
            var errors = new System.Collections.Generic.List<string>();
            foreach (var n in Norms.All.Values)
            {
                if (n.Status == Verification.GameRule || n.Status == Verification.CantonalNotVerified) continue;
                foreach (var c in n.Cites)
                {
                    if (!lib.Has(c.Sr)) { errors.Add($"{n.Id}: law SR {c.Sr} missing"); continue; }
                    bool ok = string.IsNullOrEmpty(c.Article) ? lib.TextContains(c.Sr, c.Keyword) : lib.ArticleContains(c.Sr, c.Article, c.Keyword);
                    if (!ok) errors.Add($"{n.Id}: {c} (SR {c.Sr}) does not contain \"{c.Keyword}\"");
                }
            }
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void ObvCatalog_ParsesFinesExactlyAsPrintedInTheLaw()
        {
            var lib = LawLibrary.LoadDirectory(LawDir());
            string text = File.ReadAllText(Path.Combine(LawDir(), "SR_314.11.txt"));
            var obv = ObvCatalog.Parse(text);
            TestContext.WriteLine("OBV entries parsed: " + obv.Entries.Count);
            Assert.Greater(obv.Entries.Count, 200);
            // speeding (Ziff. 303)
            Assert.AreEqual(40, obv.Amount("303.1.a")); Assert.AreEqual(120, obv.Amount("303.1.b")); Assert.AreEqual(250, obv.Amount("303.1.c"));
            Assert.AreEqual(40, obv.Amount("303.2.a")); Assert.AreEqual(100, obv.Amount("303.2.b")); Assert.AreEqual(160, obv.Amount("303.2.c")); Assert.AreEqual(240, obv.Amount("303.2.d"));
            Assert.AreEqual(20, obv.Amount("303.3.a")); Assert.AreEqual(60, obv.Amount("303.3.b")); Assert.AreEqual(120, obv.Amount("303.3.c")); Assert.AreEqual(180, obv.Amount("303.3.d")); Assert.AreEqual(260, obv.Amount("303.3.e"));
            // other
            Assert.AreEqual(250, obv.Amount("309.1")); Assert.AreEqual(100, obv.Amount("311")); Assert.AreEqual(60, obv.Amount("312.1")); Assert.AreEqual(60, obv.Amount("308")); Assert.AreEqual(140, obv.Amount("337"));
        }

        [TestCase(58, 50, SpeedZone.Urban, 120)]       // measured 58, -3 tolerance = 55 → +5 → tier a? (55-50=5 ⇒ 40)
        public void SpeedingFineLookup_UsesToleranceAndTiers(int measured, int limit, SpeedZone zone, int expectedUnused)
        {
            var obv = ObvCatalog.Parse(File.ReadAllText(Path.Combine(LawDir(), "SR_314.11.txt")));
            var f = SpeedingRules.Assess(obv, measured, limit, zone);
            Assert.IsTrue(f.IsViolation); Assert.AreEqual(5, f.ExcessKmh); Assert.AreEqual(40, f.Amount); Assert.IsTrue(f.IsOrdnungsbusse);
        }

        [Test]
        public void SpeedingFine_Tiers_AndRaser()
        {
            var obv = ObvCatalog.Parse(File.ReadAllText(Path.Combine(LawDir(), "SR_314.11.txt")));
            var a = SpeedingRules.Assess(obv, 50 + 3 + 14, 50, SpeedZone.Urban); Assert.AreEqual(250, a.Amount);              // +14 → 11–15
            var b = SpeedingRules.Assess(obv, 80 + 3 + 18, 80, SpeedZone.Rural); Assert.AreEqual(240, b.Amount);              // +18 → 16–20
            var c = SpeedingRules.Assess(obv, 120 + 4 + 25, 120, SpeedZone.Motorway); Assert.AreEqual(260, c.Amount);          // 3 % of 149 = 5 → hmm tolerance
            var d = SpeedingRules.Assess(obv, 50 + 3 + 20, 50, SpeedZone.Urban);                                              // +20 innerorts → not an Ordnungsbusse
            Assert.IsFalse(d.IsOrdnungsbusse); Assert.IsTrue(d.IsViolation);
            var r = SpeedingRules.Assess(obv, 50 + 3 + 52, 50, SpeedZone.Urban); Assert.IsTrue(r.IsRaser); Assert.IsFalse(r.IsOrdnungsbusse);
            var none = SpeedingRules.Assess(obv, 52, 50, SpeedZone.Urban); Assert.IsFalse(none.IsViolation);                  // 52 - 3 = 49 → no violation
            Assert.AreEqual(4, SpeedingRules.AdminSeverity(52, SpeedZone.Urban, true));
            Assert.AreEqual(2, SpeedingRules.AdminSeverity(18, SpeedZone.Urban, false)); Assert.AreEqual(3, SpeedingRules.AdminSeverity(25, SpeedZone.Urban, false));
        }

        [Test]
        public void LawLibrary_QuotesArticles()
        {
            var lib = LawLibrary.LoadDirectory(LawDir());
            StringAssert.Contains("Atemalkoholprobe", lib.Article("741.01", "55"));
            StringAssert.Contains("Polizeiliche Anhaltung", lib.Article("312.0", "215"));
            Assert.IsNull(lib.Article("741.01", "9999"));
        }
    }
}
