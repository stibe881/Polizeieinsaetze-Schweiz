using System;
using System.IO;
using System.IO.Compression;

namespace SwissPoliceSim.Core.Meshing
{
    /// <summary>Compact binary form of a MeshData (used for pre-baked water surfaces). Normals are quantised to sbyte.</summary>
    public static class MeshDataIO
    {
        public static byte[] Write(MeshData m)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(m.VertexCount); w.Write(m.Indices.Length);
                    for (int i = 0; i < m.Positions.Count; i++) w.Write(m.Positions[i]);
                    for (int i = 0; i < m.Normals.Count; i++) w.Write((sbyte)Math.Max(-127, Math.Min(127, Math.Round(m.Normals[i] * 127))));
                    for (int i = 0; i < m.Uvs.Count; i++) w.Write(m.Uvs[i]);
                    w.Write(m.Colors.ToArray());
                    foreach (var idx in m.Indices) { w.Write(idx.Count); foreach (int v in idx) w.Write(v); }
                }
                return ms.ToArray();
            }
        }

        public static MeshData Read(byte[] data)
        {
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                int n = r.ReadInt32(), subs = r.ReadInt32(); var m = new MeshData(subs);
                for (int i = 0; i < n * 3; i++) m.Positions.Add(r.ReadSingle());
                for (int i = 0; i < n * 3; i++) m.Normals.Add(r.ReadSByte() / 127f);
                for (int i = 0; i < n * 2; i++) m.Uvs.Add(r.ReadSingle());
                m.Colors.AddRange(r.ReadBytes(n * 4));
                for (int s = 0; s < subs; s++) { int c = r.ReadInt32(); for (int i = 0; i < c; i++) m.Indices[s].Add(r.ReadInt32()); }
                return m;
            }
        }
    }
}
