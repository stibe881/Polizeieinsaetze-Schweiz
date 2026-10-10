using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Roads
{
    /// <summary>
    /// Determines the legal speed limit for every road segment.
    /// Priority: (1) explicit OSM maxspeed  (2) signed zone (Tempo-30-Zone, Begegnungszone 20)
    /// (3) Swiss general limits VRV Art. 4a: Autobahn 120, Autostrasse 100, ausserorts 80, innerorts 50
    /// (the innerorts/ausserorts decision uses <see cref="BuiltUpMask"/>; ways are split where it changes).
    /// STATUS of the legal values: from the author's knowledge of VRV Art. 4a, NOT yet checked against the current
    /// Fedlex text in this environment (see docs/LEGAL_STATUS.md).
    /// </summary>
    public static class SpeedLimitResolver
    {
        public const int Motorway = 120, Motorroad = 100, Rural = 80, Urban = 50;

        /// <summary>Resolves the limit of a way (does not split). <paramref name="urban"/> = decision for the way/segment.</summary>
        public static void Resolve(RoadWay w, bool urban)
        {
            if (!w.IsVehicular) { w.SpeedKmh = 5; w.SpeedBasis = SpeedSource.Zone; return; }
            if (w.MaxSpeedKmh > 0) { w.SpeedKmh = w.MaxSpeedKmh; w.SpeedBasis = SpeedSource.Tagged; return; }
            if (w.ZoneSpeedKmh > 0) { w.SpeedKmh = w.ZoneSpeedKmh; w.SpeedBasis = SpeedSource.Zone; return; }
            if (w.Class == RoadClass.LivingStreet) { w.SpeedKmh = 20; w.SpeedBasis = SpeedSource.Zone; return; }   // Begegnungszone
            if (w.Class == RoadClass.Motorway) { w.SpeedKmh = Motorway; w.SpeedBasis = SpeedSource.LegalMotorway; return; }
            if (w.Motorroad || w.Class == RoadClass.Trunk) { w.SpeedKmh = Motorroad; w.SpeedBasis = SpeedSource.LegalMotorroad; return; }
            if (w.UrbanHint == 1) urban = true; else if (w.UrbanHint == 0) urban = false;
            if (w.Class == RoadClass.Track && !urban) { w.SpeedKmh = 30; w.SpeedBasis = SpeedSource.Default; return; }   // Feldweg: Spielregel
            w.SpeedKmh = urban ? Urban : Rural;
            w.SpeedBasis = urban ? SpeedSource.LegalUrban : SpeedSource.LegalRural;
        }

        /// <summary>Resolves a way and splits it where the built-up state changes (runs shorter than minRun metres are merged).</summary>
        public static List<RoadWay> ResolveAndSplit(RoadWay w, BuiltUpMask mask, double minRun = 60)
        {
            var res = new List<RoadWay>();
            bool fixedLimit = !w.IsVehicular || w.MaxSpeedKmh > 0 || w.ZoneSpeedKmh > 0 || w.Class == RoadClass.LivingStreet ||
                              w.Class == RoadClass.Motorway || w.Class == RoadClass.Trunk || w.Motorroad || w.UrbanHint >= 0 || mask == null;
            if (fixedLimit)
            {
                bool u = mask != null && MajorityUrban(w, mask);
                Resolve(w, u); res.Add(w); return res;
            }
            int n = w.PointCount;
            var state = new bool[n];
            for (int i = 0; i < n; i++) state[i] = mask.IsBuiltUp(w.E[i], w.N[i]);
            // merge short runs into their neighbours
            bool changed = true;
            while (changed)
            {
                changed = false;
                int s = 0;
                while (s < n)
                {
                    int e = s; while (e + 1 < n && state[e + 1] == state[s]) e++;
                    double len = RunLength(w, s, e);
                    bool isEnd = s == 0 || e == n - 1;
                    if (len < minRun && !(s == 0 && e == n - 1))
                    {
                        bool target = s > 0 ? state[s - 1] : state[e + 1];
                        if (!isEnd || len < minRun * 0.5) { for (int k = s; k <= e; k++) state[k] = target; changed = true; }
                    }
                    s = e + 1;
                }
            }
            int start = 0;
            for (int i = 1; i < n - 1; i++)
                if (state[i] != state[start]) { res.Add(Slice(w, start, i, state[start])); start = i; }
            res.Add(Slice(w, start, n - 1, state[start]));
            return res;
        }

        static RoadWay Slice(RoadWay w, int from, int to, bool urban)
        {
            int cnt = to - from + 1;
            var c = new RoadWay
            {
                OsmId = w.OsmId, Class = w.Class, OneWay = w.OneWay, Bridge = w.Bridge, Tunnel = w.Tunnel, Roundabout = w.Roundabout,
                MaxSpeedKmh = w.MaxSpeedKmh, Lanes = w.Lanes, Name = w.Name, Ref = w.Ref, Motorroad = w.Motorroad,
                ZoneSpeedKmh = w.ZoneSpeedKmh, UrbanHint = w.UrbanHint,
                NodeIds = new long[cnt], E = new double[cnt], N = new double[cnt],
                H = w.H.Length == w.PointCount ? new double[cnt] : new double[0]
            };
            Array.Copy(w.NodeIds, from, c.NodeIds, 0, cnt); Array.Copy(w.E, from, c.E, 0, cnt); Array.Copy(w.N, from, c.N, 0, cnt);
            if (c.H.Length == cnt) Array.Copy(w.H, from, c.H, 0, cnt);
            Resolve(c, urban);
            return c;
        }

        static double RunLength(RoadWay w, int s, int e)
        {
            double l = 0;
            for (int i = s + 1; i <= e; i++) l += Math.Sqrt(Math.Pow(w.E[i] - w.E[i - 1], 2) + Math.Pow(w.N[i] - w.N[i - 1], 2));
            return l;
        }

        static bool MajorityUrban(RoadWay w, BuiltUpMask mask)
        {
            int u = 0; for (int i = 0; i < w.PointCount; i++) if (mask.IsBuiltUp(w.E[i], w.N[i])) u++;
            return u * 2 > w.PointCount;
        }
    }
}
