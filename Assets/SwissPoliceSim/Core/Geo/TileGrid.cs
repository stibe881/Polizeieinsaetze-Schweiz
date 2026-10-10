using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Geo
{
    /// <summary>
    /// 1 km x 1 km world cell aligned to the LV95 kilometre grid. Id = (floor(E/1000), floor(N/1000)),
    /// identical to the numbering of swisstopo tiles (e.g. swissALTI3D "2600-1199").
    /// </summary>
    public readonly struct TileId : IEquatable<TileId>
    {
        public readonly int X;
        public readonly int Y;
        public TileId(int x, int y) { X = x; Y = y; }

        public bool Equals(TileId o) => X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is TileId t && Equals(t);
        public override int GetHashCode() => unchecked(X * 73856093 ^ Y * 19349663);
        public override string ToString() => $"{X}-{Y}";
        public static bool operator ==(TileId a, TileId b) => a.Equals(b);
        public static bool operator !=(TileId a, TileId b) => !a.Equals(b);
    }

    public static class TileGrid
    {
        public const double TileSize = 1000.0;

        public static TileId FromLv95(double e, double n) =>
            new TileId((int)Math.Floor(e / TileSize), (int)Math.Floor(n / TileSize));

        public static double MinE(TileId t) => t.X * TileSize;
        public static double MinN(TileId t) => t.Y * TileSize;

        /// <summary>All tiles with Chebyshev distance &lt;= radius from centre, sorted nearest first.</summary>
        public static List<TileId> Ring(TileId centre, int radius)
        {
            var list = new List<TileId>((2 * radius + 1) * (2 * radius + 1));
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                    list.Add(new TileId(centre.X + dx, centre.Y + dy));
            list.Sort((a, b) => Dist2(a, centre).CompareTo(Dist2(b, centre)));
            return list;
        }

        public static int Chebyshev(TileId a, TileId b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        static int Dist2(TileId a, TileId b) { int dx = a.X - b.X, dy = a.Y - b.Y; return dx * dx + dy * dy; }
    }
}
