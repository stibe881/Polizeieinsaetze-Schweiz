using System;

namespace SwissPoliceSim.Core.Geo
{
    /// <summary>
    /// WGS84 &lt;-&gt; LV95 using swisstopo's published "approximate formulas" (accuracy ~1 m inside Switzerland).
    /// Good enough to place OSM data on swisstopo terrain; for centimetre accuracy swisstopo's REFRAME / NTv2 grid
    /// would be required (not needed for a game).
    /// </summary>
    public static class SwissCoordinateConverter
    {
        public static Lv95 ToLv95(Wgs84 p, double height = 0)
        {
            double phi = (p.Lat * 3600.0 - 169028.66) / 10000.0;
            double lam = (p.Lon * 3600.0 - 26782.5) / 10000.0;
            double e = 2600072.37
                       + 211455.93 * lam
                       - 10938.51 * lam * phi
                       - 0.36 * lam * phi * phi
                       - 44.54 * lam * lam * lam;
            double n = 1200147.07
                       + 308807.95 * phi
                       + 3745.25 * lam * lam
                       + 76.63 * phi * phi
                       - 194.56 * lam * lam * phi
                       + 119.79 * phi * phi * phi;
            return new Lv95(e, n, height);
        }

        public static Lv95 ToLv95(double lat, double lon, double height = 0) => ToLv95(new Wgs84(lat, lon), height);

        public static Wgs84 ToWgs84(Lv95 p)
        {
            double y = (p.E - 2600000.0) / 1000000.0;
            double x = (p.N - 1200000.0) / 1000000.0;
            double lam = 2.6779094
                         + 4.728982 * y
                         + 0.791484 * y * x
                         + 0.1306 * y * x * x
                         - 0.0436 * y * y * y;
            double phi = 16.9023892
                         + 3.238272 * x
                         - 0.270978 * y * y
                         - 0.002528 * x * x
                         - 0.0447 * y * y * x
                         - 0.0140 * x * x * x;
            return new Wgs84(phi * 100.0 / 36.0, lam * 100.0 / 36.0);
        }

        /// <summary>Rough bounding box of Switzerland + Liechtenstein in LV95 (sanity check only, not a border).</summary>
        public static bool IsPlausiblySwiss(Lv95 p) =>
            p.E >= 2485000 && p.E <= 2838000 && p.N >= 1075000 && p.N <= 1296000;
    }
}
