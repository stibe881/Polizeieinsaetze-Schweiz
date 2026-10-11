using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SwissPoliceSim.Core.Meshing;

namespace SwissServer.Net
{
    /// <summary>Binary message ids (first byte of a binary WebSocket frame). Text frames are JSON objects with a "t" field.</summary>
    public static class Msg
    {
        public const byte Tile = 1, Snapshot = 2, TileDrop = 3, Model = 5;
    }

    /// <summary>Mesh kinds inside a Tile message.</summary>
    public static class TileMeshKind { public const byte Terrain = 0, Roads = 1, BuildingsFar = 2, BuildingsDetail = 3, Water = 4, Furniture = 5, Rail = 6, MacroTerrain = 7; }

    /// <summary>Little-endian binary writer helpers for mesh payloads (layout documented in docs/PROTOCOL.md).</summary>
    public sealed class Bin
    {
        readonly MemoryStream _ms = new MemoryStream(); readonly BinaryWriter _w;
        public Bin() { _w = new BinaryWriter(_ms, Encoding.UTF8); }
        public Bin U8(int v) { _w.Write((byte)v); return this; }
        public Bin I32(int v) { _w.Write(v); return this; }
        public Bin F32(float v) { _w.Write(v); return this; }
        public Bin F64(double v) { _w.Write(v); return this; }
        public Bin U16(int v) { _w.Write((ushort)v); return this; }
        public Bin Str(string s) { var b = Encoding.UTF8.GetBytes(s ?? ""); _w.Write((ushort)b.Length); _w.Write(b); return this; }
        public Bin Bytes(byte[] b) { _w.Write(b); return this; }
        public int Length => (int)_ms.Length;
        public byte[] ToArray() { _w.Flush(); return _ms.ToArray(); }

        /// <summary>Mesh: int32 vertices, int32 submeshes, flags (bit0 = has uv2, bit1 = 32-bit indices), positions(xyz), normals(xyz), uv(xy), colour rgba bytes, [uv2], per submesh: int32 count + indices.</summary>
        public Bin Mesh(MeshData m, bool uv2 = false)
        {
            if (m == null || m.VertexCount == 0) { I32(0); I32(0); U8(0); return this; }
            int v = m.VertexCount; bool big = v > 65535; bool hasUv2 = uv2 && m.Uv2.Count == v * 2;
            I32(v); I32(m.Indices.Length); U8((hasUv2 ? 1 : 0) | (big ? 2 : 0));
            foreach (var f in m.Positions) F32(f); foreach (var f in m.Normals) F32(f); foreach (var f in m.Uvs) F32(f);
            foreach (var b in m.Colors) U8(b);
            if (hasUv2) foreach (var f in m.Uv2) F32(f);
            foreach (var idx in m.Indices) { I32(idx.Count); foreach (var i in idx) { if (big) I32(i); else U16(i); } }
            return this;
        }
    }
}
