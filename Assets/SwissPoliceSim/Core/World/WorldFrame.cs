namespace SwissPoliceSim.Core.World
{
    /// <summary>Unity-free float vector so the floating-origin math can be unit-tested outside Unity.</summary>
    public readonly struct Vec3f
    {
        public readonly float X, Y, Z;
        public Vec3f(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    /// <summary>
    /// Mapping between double-precision LV95 world coordinates and float local (Unity) coordinates.
    /// Local axes: x = east, y = up (height a.s.l. minus OriginH), z = north.
    /// Shifting the origin keeps float values small (precision ~0.1 mm within 1 km, ~0.5 mm at 5 km).
    /// </summary>
    public sealed class WorldFrame
    {
        public double OriginE { get; private set; }
        public double OriginN { get; private set; }
        public double OriginH { get; private set; }

        public WorldFrame(double e, double n, double h = 0) { OriginE = e; OriginN = n; OriginH = h; }

        public Vec3f ToLocal(double e, double n, double h) =>
            new Vec3f((float)(e - OriginE), (float)(h - OriginH), (float)(n - OriginN));

        public void ToWorld(float x, float y, float z, out double e, out double n, out double h)
        {
            e = OriginE + x; n = OriginN + z; h = OriginH + y;
        }

        /// <summary>Moves the origin by (dx, dz) metres. Callers must move all scene objects by (-dx, -dz).</summary>
        public void Shift(double dx, double dz) { OriginE += dx; OriginN += dz; }

        /// <summary>True if horizontal distance of a local position from the origin exceeds the threshold.</summary>
        public static bool NeedsRecenter(float x, float z, float threshold) =>
            x * x + z * z > threshold * threshold;
    }
}
