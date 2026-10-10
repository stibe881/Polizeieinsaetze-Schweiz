using System;

namespace SwissPoliceSim.Core.Game
{
    /// <summary>Game time (Swiss local time, CET/CEST handled by the caller via offset) and sun position.</summary>
    public sealed class GameClock
    {
        public DateTime Start = new DateTime(2026, 10, 12, 7, 30, 0);   // shift start
        public double ElapsedGameSeconds;
        public float TimeScale = 12f;                                   // 1 real second = 12 game seconds (a 12-minute drive ≈ 2.4 h) – adjustable
        public DateTime Now => Start.AddSeconds(ElapsedGameSeconds);
        public float HourOfDay => (float)(Now.TimeOfDay.TotalHours);
        public void Advance(float realDt) => ElapsedGameSeconds += realDt * TimeScale;

        /// <summary>Swiss summer time: last Sunday of March 01:00 UTC until last Sunday of October 01:00 UTC.</summary>
        public static bool IsSummerTime(DateTime local)
        {
            DateTime LastSunday(int y, int m) { var d = new DateTime(y, m, DateTime.DaysInMonth(y, m)); while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(-1); return d; }
            var start = LastSunday(local.Year, 3).AddHours(2); var end = LastSunday(local.Year, 10).AddHours(3);
            return local >= start && local < end;
        }
    }

    public static class SunModel
    {
        /// <summary>Solar elevation and azimuth (degrees, azimuth from north clockwise) at local civil time for lat/lon (NOAA low-precision algorithm).</summary>
        public static void Position(DateTime local, double latDeg, double lonDeg, out double elevationDeg, out double azimuthDeg)
        {
            double tzHours = GameClock.IsSummerTime(local) ? 2 : 1;
            double dayOfYear = local.DayOfYear, hour = local.TimeOfDay.TotalHours - tzHours;     // UTC hours
            double g = 2 * Math.PI / 365.0 * (dayOfYear - 1 + (hour - 12) / 24.0);
            double eqTime = 229.18 * (0.000075 + 0.001868 * Math.Cos(g) - 0.032077 * Math.Sin(g) - 0.014615 * Math.Cos(2 * g) - 0.040849 * Math.Sin(2 * g));
            double decl = 0.006918 - 0.399912 * Math.Cos(g) + 0.070257 * Math.Sin(g) - 0.006758 * Math.Cos(2 * g) + 0.000907 * Math.Sin(2 * g) - 0.002697 * Math.Cos(3 * g) + 0.00148 * Math.Sin(3 * g);
            double timeOffset = eqTime + 4 * lonDeg;                                         // minutes (UTC based)
            double tst = hour * 60 + timeOffset;                                              // true solar time (min)
            double ha = (tst / 4.0 - 180.0) * Math.PI / 180.0;
            double lat = latDeg * Math.PI / 180.0;
            double cosZ = Math.Sin(lat) * Math.Sin(decl) + Math.Cos(lat) * Math.Cos(decl) * Math.Cos(ha);
            double zen = Math.Acos(Math.Max(-1, Math.Min(1, cosZ)));
            elevationDeg = 90 - zen * 180 / Math.PI;
            double az = Math.Atan2(Math.Sin(ha), Math.Cos(ha) * Math.Sin(lat) - Math.Tan(decl) * Math.Cos(lat));
            azimuthDeg = (az * 180 / Math.PI + 180 + 360) % 360;
        }

        /// <summary>0 (night) .. 1 (full daylight) smooth factor from elevation incl. twilight.</summary>
        public static float Daylight(double elevationDeg) => (float)Math.Max(0, Math.Min(1, (elevationDeg + 6) / 18.0));
    }

    public struct WeatherSample
    {
        public float TemperatureC, PrecipitationMmH, FogDensity, WindMs, CloudCover, Visibility, RoadFriction, ThunderChance;
        public bool IsSnow, IsRain, IsSleet, Thunder;
        public override string ToString() => $"{TemperatureC:F1}°C, {(IsSnow ? "Schnee" : IsRain ? "Regen" : IsSleet ? "Schneeregen" : "trocken")} {PrecipitationMmH:F1} mm/h, Nebel {FogDensity:F2}, Wind {WindMs:F0} m/s, Grip {RoadFriction:F2}";
    }

    /// <summary>
    /// Deterministic regional weather: smooth space-time noise fields give fronts that move across the country (rain in Bern while the
    /// Bernese Oberland has snow), temperature follows season, daytime and altitude (−0.65 K / 100 m). Not a forecast of real weather.
    /// </summary>
    public static class WeatherModel
    {
        public static WeatherSample Sample(double e, double n, double altitude, DateTime local, int seed = 1)
        {
            double tDays = (local - new DateTime(2026, 1, 1)).TotalDays, tH = tDays * 24;
            double kmE = e / 1000.0, kmN = n / 1000.0;
            // front pattern drifting from west to east at ~25 km/h
            double drift = tH * 25.0;
            double front = Noise3((kmE - drift) / 90.0, kmN / 70.0, tH / 40.0, seed);
            double cloud = Clamp01(0.25 + 1.2 * (front - 0.30));
            double precipMm = front > 0.58 ? (front - 0.58) / 0.42 * 9.0 : 0.0;
            precipMm *= 0.6 + 0.8 * Noise3(kmE / 15.0, kmN / 15.0, tH / 3.0, seed + 5);    // convective cells
            // season & day
            double doy = local.DayOfYear, season = -Math.Cos((doy - 15) / 365.0 * 2 * Math.PI);           // −1 in mid January, +1 in mid July
            double hour = local.TimeOfDay.TotalHours; double diurnal = -Math.Cos((hour - 3.5) / 24.0 * 2 * Math.PI);   // minimum ~03:30, max ~15:30
            double temp = 9.5 + season * 9.5 + diurnal * (4.5 - cloud * 2.0) - (altitude - 540) * 0.0065 + (Noise3(kmE / 120.0, kmN / 120.0, tH / 70.0, seed + 9) - 0.5) * 6.0;
            var w = new WeatherSample { TemperatureC = (float)temp, PrecipitationMmH = (float)precipMm, CloudCover = (float)cloud };
            if (precipMm > 0.05)
            {
                if (temp <= 0.5) w.IsSnow = true; else if (temp < 2.5) w.IsSleet = true; else w.IsRain = true;
            }
            double wind = 1.5 + 6 * Math.Max(0, front - 0.4) + 3 * Noise3(kmE / 20.0, kmN / 20.0, tH / 6.0, seed + 11);
            w.WindMs = (float)wind;
            // fog: cold, humid, calm basins at night / early morning, mostly autumn and winter, below ~900 m
            double humidity = 0.4 + 0.4 * (1 - Math.Abs(diurnal)) + 0.2 * Noise3(kmE / 40.0, kmN / 40.0, tH / 30.0, seed + 21);
            double fogBase = (season < 0.3 ? 1.0 : 0.25) * (altitude < 900 ? 1.0 : 0.0) * (hour < 10 || hour > 20 ? 1.0 : 0.15) * Math.Max(0, humidity - 0.62) * (wind < 3 ? 1 : 0.2);
            w.FogDensity = (float)Math.Min(1.0, fogBase * 3.5);
            w.Thunder = temp > 17 && precipMm > 4.5 && Noise3(kmE / 8.0, kmN / 8.0, tH / 1.5, seed + 31) > 0.62;
            w.ThunderChance = w.Thunder ? 1f : 0f;
            // visibility (m) and grip
            double vis = 20000 / (1 + w.FogDensity * 60 + precipMm * 1.2); w.Visibility = (float)Math.Max(30, vis);
            double fr = 1.0;
            if (w.IsRain) fr = 0.75 - Math.Min(0.15, precipMm * 0.03); else if (w.IsSleet) fr = 0.45; else if (w.IsSnow) fr = 0.32;
            else if (temp < 0.0 && w.FogDensity > 0.2) fr = 0.2;    // freezing fog → black ice
            w.RoadFriction = (float)Math.Max(0.12, fr);
            return w;
        }

        static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        static double Hash(int x, int y, int z, int seed)
        {
            unchecked { int h = x * 374761393 + y * 668265263 + z * 1442695041 + seed * 1274126177; h = (h ^ (h >> 13)) * 1274126177; h ^= h >> 16; return (h & 0xFFFFFF) / (double)0x1000000; }
        }

        public static double Noise3(double x, double y, double z, int seed)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z); double fx = x - xi, fy = y - yi, fz = z - zi;
            double u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy), w = fz * fz * (3 - 2 * fz);
            double L(double a, double b, double t) => a + (b - a) * t;
            return L(L(L(Hash(xi, yi, zi, seed), Hash(xi + 1, yi, zi, seed), u), L(Hash(xi, yi + 1, zi, seed), Hash(xi + 1, yi + 1, zi, seed), u), v),
                     L(L(Hash(xi, yi, zi + 1, seed), Hash(xi + 1, yi, zi + 1, seed), u), L(Hash(xi, yi + 1, zi + 1, seed), Hash(xi + 1, yi + 1, zi + 1, seed), u), v), w);
        }
    }
}
