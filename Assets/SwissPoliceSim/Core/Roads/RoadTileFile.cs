using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SwissPoliceSim.Core.Geo;

namespace SwissPoliceSim.Core.Roads
{
    /// <summary>
    /// Splits ways at tile borders and (de)serialises one tile's roads.
    /// A segment belongs to the tile containing its midpoint; consecutive segments in the same tile form one chunk,
    /// chunks share their border node so the network stays connected.
    /// File: "SPR2", int X, int Y, int wayCount, per way: long id, byte class, byte oneway, byte flags, short maxspeed,
    /// byte lanes, string name, string ref, int n, long[n] nodeIds, float[n] x, float[n] z (metres from tile SW corner), float[n] h (m a.s.l.; NaN if unknown).
    /// </summary>
    public static class RoadTileFile
    {
        public const string Magic = "SPR2";

        public static void Split(RoadWay way, Dictionary<TileId, List<RoadWay>> into)
        {
            int start = 0;
            TileId cur = SegTile(way, 0);
            for (int i = 1; i < way.PointCount - 1; i++)
            {
                var t = SegTile(way, i);
                if (t != cur) { Emit(way, start, i, cur, into); start = i; cur = t; }
            }
            Emit(way, start, way.PointCount - 1, cur, into);
        }

        static TileId SegTile(RoadWay w, int i) =>
            TileGrid.FromLv95((w.E[i] + w.E[i + 1]) * 0.5, (w.N[i] + w.N[i + 1]) * 0.5);

        static void Emit(RoadWay w, int from, int toInclusive, TileId t, Dictionary<TileId, List<RoadWay>> into)
        {
            int n = toInclusive - from + 1;
            var c = new RoadWay
            {
                OsmId = w.OsmId, Class = w.Class, OneWay = w.OneWay, Bridge = w.Bridge, Tunnel = w.Tunnel,
                Roundabout = w.Roundabout, MaxSpeedKmh = w.MaxSpeedKmh, Lanes = w.Lanes, Name = w.Name, Ref = w.Ref,
                NodeIds = new long[n], E = new double[n], N = new double[n],
                H = w.H.Length == w.NodeIds.Length ? new double[n] : new double[0]
            };
            Array.Copy(w.NodeIds, from, c.NodeIds, 0, n);
            Array.Copy(w.E, from, c.E, 0, n);
            Array.Copy(w.N, from, c.N, 0, n);
            if (c.H.Length == n) Array.Copy(w.H, from, c.H, 0, n);
            if (!into.TryGetValue(t, out var list)) into[t] = list = new List<RoadWay>();
            list.Add(c);
        }

        public static void Write(Stream s, TileId tile, IList<RoadWay> ways)
        {
            double oe = TileGrid.MinE(tile), on = TileGrid.MinN(tile);
            using (var w = new BinaryWriter(s, Encoding.UTF8, true))
            {
                w.Write(Encoding.ASCII.GetBytes(Magic));
                w.Write(tile.X); w.Write(tile.Y); w.Write(ways.Count);
                foreach (var way in ways)
                {
                    w.Write(way.OsmId); w.Write((byte)way.Class); w.Write((byte)way.OneWay);
                    w.Write((byte)((way.Bridge ? 1 : 0) | (way.Tunnel ? 2 : 0) | (way.Roundabout ? 4 : 0)));
                    w.Write((short)way.MaxSpeedKmh); w.Write((byte)way.Lanes);
                    w.Write(way.Name); w.Write(way.Ref);
                    int n = way.PointCount;
                    w.Write(n);
                    for (int i = 0; i < n; i++) w.Write(way.NodeIds[i]);
                    for (int i = 0; i < n; i++) w.Write((float)(way.E[i] - oe));
                    for (int i = 0; i < n; i++) w.Write((float)(way.N[i] - on));
                    bool hasH = way.H.Length == n;
                    for (int i = 0; i < n; i++) w.Write(hasH ? (float)way.H[i] : float.NaN);
                }
            }
        }

        public static List<RoadWay> Read(Stream s, out TileId tile)
        {
            using (var r = new BinaryReader(s, Encoding.UTF8, true))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != Magic) throw new InvalidDataException("Not a road tile file");
                tile = new TileId(r.ReadInt32(), r.ReadInt32());
                double oe = TileGrid.MinE(tile), on = TileGrid.MinN(tile);
                int count = r.ReadInt32();
                var list = new List<RoadWay>(count);
                for (int k = 0; k < count; k++)
                {
                    var way = new RoadWay { OsmId = r.ReadInt64(), Class = (RoadClass)r.ReadByte(), OneWay = (OneWay)r.ReadByte() };
                    byte f = r.ReadByte();
                    way.Bridge = (f & 1) != 0; way.Tunnel = (f & 2) != 0; way.Roundabout = (f & 4) != 0;
                    way.MaxSpeedKmh = r.ReadInt16(); way.Lanes = r.ReadByte();
                    way.Name = r.ReadString(); way.Ref = r.ReadString();
                    int n = r.ReadInt32();
                    way.NodeIds = new long[n]; way.E = new double[n]; way.N = new double[n];
                    for (int i = 0; i < n; i++) way.NodeIds[i] = r.ReadInt64();
                    for (int i = 0; i < n; i++) way.E[i] = oe + r.ReadSingle();
                    for (int i = 0; i < n; i++) way.N[i] = on + r.ReadSingle();
                    way.H = new double[n];
                    for (int i = 0; i < n; i++) way.H[i] = r.ReadSingle();
                    list.Add(way);
                }
                return list;
            }
        }
    }
}
