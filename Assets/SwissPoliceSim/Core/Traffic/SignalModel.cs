using System;

namespace SwissPoliceSim.Core.Traffic
{
    public enum SignalState : byte { Red = 0, RedAmber = 1, Green = 2, Amber = 3 }

    /// <summary>
    /// Deterministic traffic-light timing. The SAME function drives the visible lamps (shader), the traffic AI and the
    /// violation detector, so a "red light violation" is decided on exactly what the player sees.
    /// Two conflicting approach groups (0 = roughly north/south, 1 = roughly east/west) alternate; every intersection has its own phase offset.
    /// Cycle (s): green 24 · amber 3 · all-red 3 · (other group) green 24 · amber 3 · all-red 3 = 60 s. Swiss signals show red+amber briefly before green.
    /// </summary>
    public static class SignalModel
    {
        public const float Cycle = 60f, Green = 24f, Amber = 3f, AllRed = 3f;

        public static int GroupOf(double headingRadians)
        {
            double a = Math.Abs(Math.Sin(headingRadians));   // heading measured from north (z axis): |sin| small = north/south
            return a < 0.7071 ? 0 : 1;
        }

        public static SignalState State(double timeSeconds, float offset, int group)
        {
            double t = (timeSeconds + offset + (group == 0 ? 0 : Cycle / 2)) % Cycle;
            if (t < 0) t += Cycle;
            if (t < Green - 1.5) return SignalState.Green;
            if (t < Green) return SignalState.Green;                       // (keep simple: no flashing green)
            if (t < Green + Amber) return SignalState.Amber;
            if (t < Cycle - 1.0) return SignalState.Red;
            return SignalState.RedAmber;                                   // last second before green
        }

        /// <summary>Stable per-intersection offset in [0, Cycle) from the OSM node id.</summary>
        public static float OffsetFor(long nodeId)
        {
            unchecked { uint h = (uint)(nodeId * 2654435761L); h ^= h >> 15; return (h % 6000) / 100f; }
        }

        public static bool MustStop(SignalState s) => s == SignalState.Red || s == SignalState.RedAmber;
    }
}
