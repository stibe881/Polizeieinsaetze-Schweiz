using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SwissPoliceSim.Core.Geo
{
    /// <summary>Minimal ESRI Shapefile (polygon types 5/15/25) + dBASE reader for swissBOUNDARIES3D (XY only, Z ignored).</summary>
    public static class Shapefile
    {
        public sealed class Record
        {
            public List<double[]> Rings = new List<double[]>();     // each ring: x0,y0,x1,y1,...
            public Dictionary<string, string> Attributes = new Dictionary<string, string>();
        }

        public static List<Record> Read(string shpPath)
        {
            var list = new List<Record>();
            using (var fs = File.OpenRead(shpPath))
            using (var r = new BinaryReader(fs))
            {
                r.ReadBytes(24); fs.Seek(100, SeekOrigin.Begin);
                while (fs.Position + 8 <= fs.Length)
                {
                    int recNo = ReadBe(r), contentWords = ReadBe(r); long end = fs.Position + contentWords * 2L;
                    int type = r.ReadInt32();
                    var rec = new Record();
                    if (type == 5 || type == 15 || type == 25)
                    {
                        r.ReadBytes(32);
                        int parts = r.ReadInt32(), pts = r.ReadInt32();
                        var partStart = new int[parts + 1];
                        for (int i = 0; i < parts; i++) partStart[i] = r.ReadInt32();
                        partStart[parts] = pts;
                        var xy = new double[pts * 2];
                        for (int i = 0; i < pts; i++) { xy[i * 2] = r.ReadDouble(); xy[i * 2 + 1] = r.ReadDouble(); }
                        for (int p = 0; p < parts; p++)
                        {
                            int a = partStart[p], b = partStart[p + 1]; var ring = new double[(b - a) * 2];
                            Array.Copy(xy, a * 2, ring, 0, ring.Length); rec.Rings.Add(ring);
                        }
                    }
                    fs.Seek(end, SeekOrigin.Begin);
                    list.Add(rec);
                }
            }
            string dbf = Path.ChangeExtension(shpPath, ".dbf");
            if (File.Exists(dbf)) ReadDbf(dbf, list);
            return list;
        }

        static int ReadBe(BinaryReader r) { var b = r.ReadBytes(4); return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]; }

        static void ReadDbf(string path, List<Record> recs)
        {
            var d = File.ReadAllBytes(path);
            int n = BitConverter.ToInt32(d, 4); int hl = BitConverter.ToUInt16(d, 8), rl = BitConverter.ToUInt16(d, 10);
            var fields = new List<(string name, int len)>(); int o = 32;
            while (d[o] != 0x0D) { string name = Encoding.ASCII.GetString(d, o, 11).TrimEnd('\0'); fields.Add((name, d[o + 16])); o += 32; }
            for (int i = 0; i < n && i < recs.Count; i++)
            {
                int p = hl + i * rl + 1;
                foreach (var (name, len) in fields) { recs[i].Attributes[name] = Encoding.UTF8.GetString(d, p, len).Trim(); p += len; }
            }
        }
    }
}
