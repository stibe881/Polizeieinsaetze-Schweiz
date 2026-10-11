using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Roads
{
    public enum TurnKind { Start, Straight, SlightLeft, Left, SharpLeft, SlightRight, Right, SharpRight, UTurn, EnterRoundabout, ExitRoundabout, Arrive }

    public sealed class Instruction
    {
        public TurnKind Kind; public double DistanceFromStart, E, N; public string Street = "", Text = ""; public int RoundaboutExit;
    }

    /// <summary>Turn-by-turn guidance (German wording) from a route: turns are detected by the bearing change at junction nodes, same-street continuations are merged.</summary>
    public static class NavigationGuide
    {
        public static List<Instruction> Build(RoutePath p)
        {
            var list = new List<Instruction>(); if (p == null || p.Count < 2) return list;
            var cum = new double[p.Count]; for (int i = 1; i < p.Count; i++) cum[i] = cum[i - 1] + Dist(p, i - 1, i);
            string first = p.SegmentNames.Count > 1 ? p.SegmentNames[1] : "";
            list.Add(new Instruction { Kind = TurnKind.Start, DistanceFromStart = 0, E = p.E[0], N = p.N[0], Street = first, Text = string.IsNullOrEmpty(first) ? "Fahrt beginnen" : "Fahrt beginnen auf " + first });
            bool inRb = false; int rbExit = 0;
            for (int i = 1; i < p.Count - 1; i++)
            {
                bool rbIn = p.SegmentRoundabout[i], rbOut = p.SegmentRoundabout[i + 1];
                if (!rbIn && rbOut) { inRb = true; rbExit = 0; list.Add(new Instruction { Kind = TurnKind.EnterRoundabout, DistanceFromStart = cum[i], E = p.E[i], N = p.N[i], Street = p.SegmentNames[i + 1], Text = "In den Kreisel einfahren" }); continue; }
                if (rbIn && rbOut) { if (HasExit(p, i)) rbExit++; continue; }
                if (rbIn && !rbOut)
                {
                    rbExit++; string st = p.SegmentNames[i + 1]; var last = list.Count > 0 ? list[list.Count - 1] : null;
                    if (last != null && last.Kind == TurnKind.EnterRoundabout) { last.RoundaboutExit = rbExit; last.Text = $"Im Kreisel die {rbExit}. Ausfahrt nehmen" + (string.IsNullOrEmpty(st) ? "" : " Richtung " + st); }
                    inRb = false; continue;
                }
                double a1 = Bearing(p, i - 1, i), a2 = Bearing(p, i, i + 1); double d = Norm(a2 - a1);
                string sNow = p.SegmentNames[i], sNext = p.SegmentNames[i + 1]; bool sameStreet = !string.IsNullOrEmpty(sNow) && sNow == sNext;
                if (Math.Abs(d) < 28 || (sameStreet && Math.Abs(d) < 45)) continue;
                TurnKind k = Math.Abs(d) > 150 ? TurnKind.UTurn : d > 110 ? TurnKind.SharpRight : d > 60 ? TurnKind.Right : d > 28 ? TurnKind.SlightRight : d < -110 ? TurnKind.SharpLeft : d < -60 ? TurnKind.Left : TurnKind.SlightLeft;
                if (list.Count > 0 && cum[i] - list[list.Count - 1].DistanceFromStart < 12 && list[list.Count - 1].Kind != TurnKind.Start) continue;     // merge junction clusters
                string verb = k == TurnKind.UTurn ? "Wenden" : (k == TurnKind.SharpRight ? "Scharf rechts abbiegen" : k == TurnKind.Right ? "Rechts abbiegen" : k == TurnKind.SlightRight ? "Leicht rechts halten" : k == TurnKind.SharpLeft ? "Scharf links abbiegen" : k == TurnKind.Left ? "Links abbiegen" : "Leicht links halten");
                list.Add(new Instruction { Kind = k, DistanceFromStart = cum[i], E = p.E[i], N = p.N[i], Street = sNext, Text = verb + (string.IsNullOrEmpty(sNext) ? "" : " auf " + sNext) });
            }
            list.Add(new Instruction { Kind = TurnKind.Arrive, DistanceFromStart = cum[p.Count - 1], E = p.E[p.Count - 1], N = p.N[p.Count - 1], Text = "Sie haben Ihr Ziel erreicht" });
            return list;
        }

        static bool HasExit(RoutePath p, int i) => false;       // roundabout side exits are not known to the path; only the taken exit is counted

        static double Dist(RoutePath p, int a, int b) { double de = p.E[b] - p.E[a], dn = p.N[b] - p.N[a]; return Math.Sqrt(de * de + dn * dn); }
        static double Bearing(RoutePath p, int a, int b) => Math.Atan2(p.E[b] - p.E[a], p.N[b] - p.N[a]) * 180 / Math.PI;
        static double Norm(double d) { while (d > 180) d -= 360; while (d < -180) d += 360; return d; }

        /// <summary>Next instruction after the given distance along the route, with the distance to it.</summary>
        public static Instruction Next(List<Instruction> list, double distanceAlong, out double distanceToIt)
        {
            foreach (var ins in list) if (ins.DistanceFromStart > distanceAlong + 3) { distanceToIt = ins.DistanceFromStart - distanceAlong; return ins; }
            distanceToIt = 0; return list.Count > 0 ? list[list.Count - 1] : null;
        }

        public static string DistanceText(double m)
        {
            if (m >= 1000) return (m / 1000).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " km";
            double r = m >= 100 ? Math.Round(m / 50) * 50 : Math.Round(m / 10) * 10;
            return r.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " m";
        }
    }
}
