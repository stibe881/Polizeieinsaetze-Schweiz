using System;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.World;
using UnityEngine;

namespace SwissPoliceSim.World
{
    /// <summary>
    /// Single source of truth for "where is Unity (0,0,0) in Switzerland". World positions are double precision LV95;
    /// Unity positions are floats relative to a movable origin (see FloatingOrigin). Unity axes: x = east, y = up
    /// (metres above sea level, never shifted), z = north.
    /// </summary>
    public static class WorldOrigin
    {
        public static WorldFrame Frame { get; private set; } = new WorldFrame(2600000, 1200000);

        /// <summary>Raised after the origin moved; argument = offset (metres) that was SUBTRACTED from all object positions.</summary>
        public static event Action<Vector3> Shifted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Shifted = null; Frame = new WorldFrame(2600000, 1200000); }

        public static void Initialize(double e, double n) { Frame = new WorldFrame(e, n); }

        public static Vector3 ToUnity(double e, double n, double h)
        {
            var v = Frame.ToLocal(e, n, h);
            return new Vector3(v.X, v.Y, v.Z);
        }

        public static Vector3 ToUnity(Lv95 p) => ToUnity(p.E, p.N, p.H);

        public static Lv95 ToLv95(Vector3 unity)
        {
            Frame.ToWorld(unity.x, unity.y, unity.z, out double e, out double n, out double h);
            return new Lv95(e, n, h);
        }

        /// <summary>Moves the origin by a horizontal offset. Callers are responsible for moving scene objects by -offset.</summary>
        public static void ShiftOrigin(Vector3 offset)
        {
            Frame.Shift(offset.x, offset.z);
            Shifted?.Invoke(new Vector3(offset.x, 0, offset.z));
        }
    }
}
