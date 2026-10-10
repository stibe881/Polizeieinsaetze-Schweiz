using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SwissPoliceSim.Core.Legal
{
    public sealed class ObvEntry
    {
        public int Ziffer; public string Item = "";      // e.g. "303", "1.b"
        public string Description = ""; public int Amount;
        public string Key => Item.Length == 0 ? Ziffer.ToString() : Ziffer + "." + Item;
    }

    /// <summary>
    /// The Ordnungsbussenliste (Anhang 1 of the Ordnungsbussenverordnung, SR 314.11) parsed from the Fedlex text, so
    /// every fine amount shown in the game is the one printed in the law.
    /// </summary>
    public sealed class ObvCatalog
    {
        public readonly List<ObvEntry> Entries = new List<ObvEntry>();
        readonly Dictionary<string, ObvEntry> _byKey = new Dictionary<string, ObvEntry>();

        public static ObvCatalog Parse(string text)
        {
            var cat = new ObvCatalog();
            var lines = text.Replace("\r", "").Split('\n');
            int start = 0;
            for (int i = 0; i < lines.Length; i++) if (lines[i].TrimStart().StartsWith("Anhang 1", StringComparison.Ordinal) && i > 100) { start = i; break; }
            var zif = new Regex(@"^\s*(\d{3,4})\.\s+(.*)$");
            var num = new Regex(@"^\s*(\d{1,2})\.\s+(.*)$");
            var let = new Regex(@"^\s*([a-z])\.\s+(.*)$");
            var amt = new Regex(@"^\s*(\[tab\])?(\d{1,5})\s*$");
            int curZ = 0; string curNum = ""; ObvEntry last = null; string pendingLetterBase = "";
            for (int i = start; i < lines.Length; i++)
            {
                string l = lines[i];
                var ma = amt.Match(l);
                if (ma.Success && last != null && last.Amount == 0) { last.Amount = int.Parse(ma.Groups[2].Value, CultureInfo.InvariantCulture); continue; }
                var mz = zif.Match(l);
                if (mz.Success && int.Parse(mz.Groups[1].Value) >= 100 && int.Parse(mz.Groups[1].Value) < 10000)
                {
                    curZ = int.Parse(mz.Groups[1].Value); string rest = mz.Groups[2].Value; curNum = "";
                    var mn = num.Match(rest);
                    if (mn.Success && !Regex.IsMatch(rest, @"^\d{3,}")) { curNum = mn.Groups[1].Value; rest = mn.Groups[2].Value; }
                    last = new ObvEntry { Ziffer = curZ, Item = curNum, Description = rest }; cat.Add(last); pendingLetterBase = curNum; continue;
                }
                if (curZ == 0) continue;
                var ml = let.Match(l);
                if (ml.Success) { last = new ObvEntry { Ziffer = curZ, Item = (pendingLetterBase.Length > 0 ? pendingLetterBase + "." : "") + ml.Groups[1].Value, Description = ml.Groups[2].Value }; cat.Add(last); continue; }
                var m2 = num.Match(l);
                if (m2.Success && int.Parse(m2.Groups[1].Value) < 40 && l.Length > 12) { curNum = m2.Groups[1].Value; pendingLetterBase = curNum; last = new ObvEntry { Ziffer = curZ, Item = curNum, Description = m2.Groups[2].Value }; cat.Add(last); continue; }
            }
            cat.Entries.RemoveAll(e => e.Amount == 0 && e.Description.Length < 3);
            return cat;
        }

        void Add(ObvEntry e) { Entries.Add(e); _byKey[e.Key] = e; }

        public ObvEntry Get(string key) => _byKey.TryGetValue(key, out var e) ? e : null;
        public int Amount(string key) => Get(key)?.Amount ?? 0;
    }

    public enum SpeedZone { Urban, Rural, Motorroad, Motorway }

    /// <summary>Result of looking up a speeding fine by measured excess.</summary>
    public sealed class SpeedingFine
    {
        public bool IsViolation, IsOrdnungsbusse;
        public int ExcessKmh, Amount; public string ObvKey = ""; public string Text = "";
        public bool IsRaser;
    }

    public static class SpeedingRules
    {
        /// <summary>Measurement uncertainty deduction (game rule; the OBV refers to ASTRA values): 3 km/h up to 100 km/h, 3 % above (rounded up).</summary>
        public static int Tolerance(int measuredKmh) => measuredKmh <= 100 ? 3 : (int)Math.Ceiling(measuredKmh * 0.03);

        public static SpeedZone ZoneOf(int limitKmh, bool urban, bool motorway, bool motorroad) =>
            motorway ? SpeedZone.Motorway : motorroad ? SpeedZone.Motorroad : urban ? SpeedZone.Urban : SpeedZone.Rural;

        public static SpeedingFine Assess(ObvCatalog obv, int measuredKmh, int limitKmh, SpeedZone zone)
        {
            int excess = measuredKmh - Tolerance(measuredKmh) - limitKmh;
            var r = new SpeedingFine { ExcessKmh = excess };
            if (excess <= 0) return r;
            r.IsViolation = true;
            // SVG Art. 90 Abs. 4: besonders krasse Missachtung
            r.IsRaser = (limitKmh <= 30 && excess >= 40) || (limitKmh <= 50 && excess >= 50) || (limitKmh <= 80 && excess >= 60) || (limitKmh > 80 && excess >= 80);
            string baseKey; int[] tiers;
            switch (zone)
            {
                case SpeedZone.Urban: baseKey = "303.1."; tiers = new[] { 5, 10, 15 }; break;
                case SpeedZone.Motorway: baseKey = "303.3."; tiers = new[] { 5, 10, 15, 20, 25 }; break;
                default: baseKey = "303.2."; tiers = new[] { 5, 10, 15, 20 }; break;
            }
            for (int i = 0; i < tiers.Length; i++)
                if (excess <= tiers[i])
                {
                    string key = baseKey + (char)('a' + i); var e = obv.Get(key);
                    r.IsOrdnungsbusse = !r.IsRaser && e != null; r.Amount = e?.Amount ?? 0; r.ObvKey = key;
                    r.Text = e != null ? $"Ordnungsbusse {r.Amount} CHF (OBV Anhang 1 Ziff. {key}: {e.Description})" : "";
                    return r;
                }
            r.IsOrdnungsbusse = false;
            r.Text = "Überschreitung über dem Ordnungsbussenbereich: Anzeige im ordentlichen Strafverfahren (Art. 90 SVG)" + (r.IsRaser ? ", Raserdelikt (Art. 90 Abs. 3 und 4 SVG)" : "") + ".";
            return r;
        }

        /// <summary>
        /// Administrative consequence rule of thumb following Bundesgericht practice (NOT statute text).
        /// 0 = none / Ordnungsbusse, 1 = leichte (Verwarnung), 2 = mittelschwere (Entzug ≥ 1 Mt.), 3 = schwere (Entzug ≥ 3 Mt.), 4 = Raser (≥ 2 Jahre).
        /// </summary>
        public static int AdminSeverity(int excess, SpeedZone zone, bool raser)
        {
            if (raser) return 4;
            switch (zone)
            {
                case SpeedZone.Urban: return excess >= 25 ? 3 : excess >= 16 ? 2 : 1;
                case SpeedZone.Motorway: return excess >= 35 ? 3 : excess >= 26 ? 2 : 1;
                default: return excess >= 30 ? 3 : excess >= 21 ? 2 : 1;
            }
        }
    }
}
