using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Geometry;

namespace SwissPoliceSim.Core.Storage
{
    /// <summary>Binary (de)serialisation of the per-tile feature chunks. Coordinates are stored as float metres relative to the tile's SW corner.</summary>
    public static class TileCodec
    {
        static BinaryWriter W(MemoryStream ms) => new BinaryWriter(ms, Encoding.UTF8, true);
        static BinaryReader R(byte[] d) => new BinaryReader(new MemoryStream(d), Encoding.UTF8, false);

        // ---------------------------------------------------------------- buildings
        public static byte[] WriteBuildings(IList<BuildingFeature> list, TileId t)
        {
            double oe = TileGrid.MinE(t), on = TileGrid.MinN(t);
            using (var ms = new MemoryStream())
            {
                using (var w = W(ms))
                {
                    w.Write(list.Count);
                    foreach (var b in list)
                    {
                        w.Write(b.OsmId); w.Write((byte)b.Kind); w.Write((byte)b.Roof); w.Write(b.Levels);
                        w.Write((byte)((b.HeightMeasured ? 1 : 0) | (b.IsPart ? 2 : 0)));
                        w.Write(b.GroundHeight); w.Write(b.EaveHeight); w.Write(b.RidgeHeight);
                        w.Write(b.RidgeDirX); w.Write(b.RidgeDirY); w.Write(b.ColorSeed);
                        w.Write(b.Name); w.Write(b.Tag_Colour); w.Write(b.Tag_RoofColour);
                        WriteRing(w, b.Footprint.Outer, oe, on);
                        w.Write((byte)b.Footprint.Holes.Count);
                        foreach (var h in b.Footprint.Holes) WriteRing(w, h, oe, on);
                    }
                }
                return ms.ToArray();
            }
        }

        static void WriteRing(BinaryWriter w, List<Vec2d> ring, double oe, double on)
        {
            w.Write((ushort)ring.Count);
            foreach (var p in ring) { w.Write((float)(p.X - oe)); w.Write((float)(p.Y - on)); }
        }

        static List<Vec2d> ReadRing(BinaryReader r, double oe, double on)
        {
            int n = r.ReadUInt16(); var l = new List<Vec2d>(n);
            for (int i = 0; i < n; i++) l.Add(new Vec2d(oe + r.ReadSingle(), on + r.ReadSingle()));
            return l;
        }

        public static List<BuildingFeature> ReadBuildings(byte[] data, TileId t)
        {
            double oe = TileGrid.MinE(t), on = TileGrid.MinN(t);
            using (var r = R(data))
            {
                int n = r.ReadInt32(); var list = new List<BuildingFeature>(n);
                for (int i = 0; i < n; i++)
                {
                    var b = new BuildingFeature { OsmId = r.ReadInt64(), Kind = (BuildingKind)r.ReadByte(), Roof = (RoofShape)r.ReadByte(), Levels = r.ReadByte() };
                    byte f = r.ReadByte(); b.HeightMeasured = (f & 1) != 0; b.IsPart = (f & 2) != 0;
                    b.GroundHeight = r.ReadSingle(); b.EaveHeight = r.ReadSingle(); b.RidgeHeight = r.ReadSingle();
                    b.RidgeDirX = r.ReadSingle(); b.RidgeDirY = r.ReadSingle(); b.ColorSeed = r.ReadInt32();
                    b.Name = r.ReadString(); b.Tag_Colour = r.ReadString(); b.Tag_RoofColour = r.ReadString();
                    b.Footprint.Outer = ReadRing(r, oe, on);
                    int holes = r.ReadByte();
                    for (int h = 0; h < holes; h++) b.Footprint.Holes.Add(ReadRing(r, oe, on));
                    list.Add(b);
                }
                return list;
            }
        }

        // ---------------------------------------------------------------- water
        public static byte[] WriteWater(IList<WaterFeature> water, IList<WaterwayFeature> ways, TileId t)
        {
            double oe = TileGrid.MinE(t), on = TileGrid.MinN(t);
            using (var ms = new MemoryStream())
            {
                using (var w = W(ms))
                {
                    w.Write(water.Count);
                    foreach (var p in water)
                    {
                        w.Write(p.OsmId); w.Write(p.IsRiver); w.Write(p.SurfaceHeight);
                        WriteRing(w, p.Polygon.Outer, oe, on);
                        w.Write((byte)p.Polygon.Holes.Count);
                        foreach (var h in p.Polygon.Holes) WriteRing(w, h, oe, on);
                    }
                    w.Write(ways.Count);
                    foreach (var q in ways)
                    {
                        w.Write(q.OsmId); w.Write((byte)q.Kind); w.Write(q.Width); w.Write(q.Tunnel);
                        w.Write((ushort)q.E.Length);
                        for (int i = 0; i < q.E.Length; i++) { w.Write((float)(q.E[i] - oe)); w.Write((float)(q.N[i] - on)); }
                    }
                }
                return ms.ToArray();
            }
        }

        public static void ReadWater(byte[] data, TileId t, out List<WaterFeature> water, out List<WaterwayFeature> ways)
        {
            double oe = TileGrid.MinE(t), on = TileGrid.MinN(t);
            water = new List<WaterFeature>(); ways = new List<WaterwayFeature>();
            using (var r = R(data))
            {
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    var p = new WaterFeature { OsmId = r.ReadInt64(), IsRiver = r.ReadBoolean(), SurfaceHeight = r.ReadSingle() };
                    p.Polygon.Outer = ReadRing(r, oe, on);
                    int holes = r.ReadByte(); for (int h = 0; h < holes; h++) p.Polygon.Holes.Add(ReadRing(r, oe, on));
                    water.Add(p);
                }
                n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    var q = new WaterwayFeature { OsmId = r.ReadInt64(), Kind = (WaterwayKind)r.ReadByte(), Width = r.ReadSingle(), Tunnel = r.ReadBoolean() };
                    int m = r.ReadUInt16(); q.E = new double[m]; q.N = new double[m];
                    for (int k = 0; k < m; k++) { q.E[k] = oe + r.ReadSingle(); q.N[k] = on + r.ReadSingle(); }
                    ways.Add(q);
                }
            }
        }

        // ---------------------------------------------------------------- trees
        public static byte[] WriteTrees(IList<TreeInstance> trees, TileId t)
        {
            double oe = TileGrid.MinE(t), on = TileGrid.MinN(t);
            using (var ms = new MemoryStream())
            {
                using (var w = W(ms))
                {
                    w.Write(trees.Count);
                    foreach (var x in trees)
                    {
                        w.Write((ushort)Math.Round((x.E - oe) * 20)); w.Write((ushort)Math.Round((x.N - on) * 20));   // 5 cm, range ±3 km
                        w.Write((byte)Math.Min(255, Math.Round(x.Height * 4))); w.Write((byte)Math.Min(255, Math.Round(x.Radius * 20))); w.Write(x.Kind);
                    }
                }
                return ms.ToArray();
            }
        }

        public static List<TreeInstance> ReadTrees(byte[] data, TileId t)
        {
            double oe = TileGrid.MinE(t), on = TileGrid.MinN(t);
            using (var r = R(data))
            {
                int n = r.ReadInt32(); var l = new List<TreeInstance>(n);
                for (int i = 0; i < n; i++)
                    l.Add(new TreeInstance { E = (float)(oe + r.ReadUInt16() / 20.0), N = (float)(on + r.ReadUInt16() / 20.0), Height = r.ReadByte() / 4f, Radius = r.ReadByte() / 20f, Kind = r.ReadByte() });
                return l;
            }
        }

        // ---------------------------------------------------------------- points & rails
        public static byte[] WritePoints(IList<PointFeature> pts, TileId t)
        {
            double oe = TileGrid.MinE(t), on = TileGrid.MinN(t);
            using (var ms = new MemoryStream())
            {
                using (var w = W(ms))
                {
                    w.Write(pts.Count);
                    foreach (var p in pts) { w.Write((byte)p.Kind); w.Write((float)(p.E - oe)); w.Write((float)(p.N - on)); w.Write(p.Direction); w.Write(p.Height); w.Write(p.OsmId); }
                }
                return ms.ToArray();
            }
        }

        public static List<PointFeature> ReadPoints(byte[] data, TileId t)
        {
            double oe = TileGrid.MinE(t), on = TileGrid.MinN(t);
            using (var r = R(data))
            {
                int n = r.ReadInt32(); var l = new List<PointFeature>(n);
                for (int i = 0; i < n; i++) l.Add(new PointFeature { Kind = (PointKind)r.ReadByte(), E = oe + r.ReadSingle(), N = on + r.ReadSingle(), Direction = r.ReadSingle(), Height = r.ReadSingle(), OsmId = r.ReadInt64() });
                return l;
            }
        }

        public static byte[] WriteRails(IList<RailFeature> rails, TileId t)
        {
            double oe = TileGrid.MinE(t), on = TileGrid.MinN(t);
            using (var ms = new MemoryStream())
            {
                using (var w = W(ms))
                {
                    w.Write(rails.Count);
                    foreach (var q in rails)
                    {
                        w.Write(q.OsmId); w.Write(q.Tunnel); w.Write(q.Bridge); w.Write((ushort)q.E.Length);
                        for (int i = 0; i < q.E.Length; i++) { w.Write((float)(q.E[i] - oe)); w.Write((float)(q.N[i] - on)); w.Write(q.H != null && q.H.Length == q.E.Length ? q.H[i] : float.NaN); }
                    }
                }
                return ms.ToArray();
            }
        }

        public static List<RailFeature> ReadRails(byte[] data, TileId t)
        {
            double oe = TileGrid.MinE(t), on = TileGrid.MinN(t);
            using (var r = R(data))
            {
                int n = r.ReadInt32(); var l = new List<RailFeature>(n);
                for (int i = 0; i < n; i++)
                {
                    var q = new RailFeature { OsmId = r.ReadInt64(), Tunnel = r.ReadBoolean(), Bridge = r.ReadBoolean() };
                    int m = r.ReadUInt16(); q.E = new double[m]; q.N = new double[m]; q.H = new float[m];
                    for (int k = 0; k < m; k++) { q.E[k] = oe + r.ReadSingle(); q.N[k] = on + r.ReadSingle(); q.H[k] = r.ReadSingle(); }
                    l.Add(q);
                }
                return l;
            }
        }
    }
}
