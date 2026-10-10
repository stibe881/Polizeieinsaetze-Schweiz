using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Meshing
{
    /// <summary>Engine-independent triangle mesh with submeshes (positions in metres, tile-local; y up).</summary>
    public sealed class MeshData
    {
        public readonly List<float> Positions = new List<float>();   // xyz
        public readonly List<float> Normals = new List<float>();     // xyz
        public readonly List<float> Uvs = new List<float>();         // uv
        public readonly List<byte> Colors = new List<byte>();        // rgba
        public readonly List<int>[] Indices;

        public MeshData(int submeshes) { Indices = new List<int>[submeshes]; for (int i = 0; i < submeshes; i++) Indices[i] = new List<int>(); }

        public int VertexCount => Positions.Count / 3;
        public int TriangleCount { get { int n = 0; foreach (var l in Indices) n += l.Count / 3; return n; } }

        public int AddVertex(float x, float y, float z, float nx, float ny, float nz, float u, float v, byte r, byte g, byte b, byte a = 255)
        {
            Positions.Add(x); Positions.Add(y); Positions.Add(z);
            Normals.Add(nx); Normals.Add(ny); Normals.Add(nz);
            Uvs.Add(u); Uvs.Add(v);
            Colors.Add(r); Colors.Add(g); Colors.Add(b); Colors.Add(a);
            return VertexCount - 1;
        }

        public void AddTriangle(int sub, int a, int b, int c) { var l = Indices[sub]; l.Add(a); l.Add(b); l.Add(c); }

        /// <summary>Adds a planar quad (a,b,c,d counter-clockwise seen from the front) with a flat normal.</summary>
        public void AddQuad(int sub, float[] p0, float[] p1, float[] p2, float[] p3, float u0, float v0, float u1, float v1, byte r, byte g, byte b)
        {
            float ex = p1[0] - p0[0], ey = p1[1] - p0[1], ez = p1[2] - p0[2];
            float fx = p3[0] - p0[0], fy = p3[1] - p0[1], fz = p3[2] - p0[2];
            float nx = ey * fz - ez * fy, ny = ez * fx - ex * fz, nz = ex * fy - ey * fx;
            float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz); if (l > 1e-9f) { nx /= l; ny /= l; nz /= l; }
            int a = AddVertex(p0[0], p0[1], p0[2], nx, ny, nz, u0, v0, r, g, b);
            int bb = AddVertex(p1[0], p1[1], p1[2], nx, ny, nz, u1, v0, r, g, b);
            int c = AddVertex(p2[0], p2[1], p2[2], nx, ny, nz, u1, v1, r, g, b);
            int d = AddVertex(p3[0], p3[1], p3[2], nx, ny, nz, u0, v1, r, g, b);
            AddTriangle(sub, a, bb, c); AddTriangle(sub, a, c, d);
        }

        /// <summary>Flat-shaded triangle (own vertices) with UVs from planar projection scaled by uvScale.</summary>
        public void AddFlatTriangle(int sub, float[] p0, float[] p1, float[] p2, byte r, byte g, byte b, float uvScale = 0.25f)
        {
            float ex = p1[0] - p0[0], ey = p1[1] - p0[1], ez = p1[2] - p0[2];
            float fx = p2[0] - p0[0], fy = p2[1] - p0[1], fz = p2[2] - p0[2];
            float nx = ey * fz - ez * fy, ny = ez * fx - ex * fz, nz = ex * fy - ey * fx;
            float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz); if (l > 1e-9f) { nx /= l; ny /= l; nz /= l; }
            int a = AddVertex(p0[0], p0[1], p0[2], nx, ny, nz, p0[0] * uvScale, p0[2] * uvScale, r, g, b);
            int bb = AddVertex(p1[0], p1[1], p1[2], nx, ny, nz, p1[0] * uvScale, p1[2] * uvScale, r, g, b);
            int c = AddVertex(p2[0], p2[1], p2[2], nx, ny, nz, p2[0] * uvScale, p2[2] * uvScale, r, g, b);
            AddTriangle(sub, a, bb, c);
        }

        public void Append(MeshData o)
        {
            int baseV = VertexCount;
            Positions.AddRange(o.Positions); Normals.AddRange(o.Normals); Uvs.AddRange(o.Uvs); Colors.AddRange(o.Colors);
            for (int s = 0; s < Indices.Length && s < o.Indices.Length; s++)
                foreach (int i in o.Indices[s]) Indices[s].Add(i + baseV);
        }

        /// <summary>Area-weighted smooth normals for the vertex range [from, VertexCount) using all submesh triangles that reference only that range.</summary>
        public void RecalculateNormals(int from)
        {
            int n = VertexCount;
            var acc = new float[n * 3];
            foreach (var idx in Indices)
                for (int i = 0; i + 2 < idx.Count; i += 3)
                {
                    int a = idx[i], b = idx[i + 1], c = idx[i + 2];
                    if (a < from || b < from || c < from) continue;
                    float ex = Positions[b * 3] - Positions[a * 3], ey = Positions[b * 3 + 1] - Positions[a * 3 + 1], ez = Positions[b * 3 + 2] - Positions[a * 3 + 2];
                    float fx = Positions[c * 3] - Positions[a * 3], fy = Positions[c * 3 + 1] - Positions[a * 3 + 1], fz = Positions[c * 3 + 2] - Positions[a * 3 + 2];
                    float nx = ey * fz - ez * fy, ny = ez * fx - ex * fz, nz = ex * fy - ey * fx;
                    foreach (int v in new[] { a, b, c }) { acc[v * 3] += nx; acc[v * 3 + 1] += ny; acc[v * 3 + 2] += nz; }
                }
            for (int v = from; v < n; v++)
            {
                float l = (float)Math.Sqrt(acc[v * 3] * acc[v * 3] + acc[v * 3 + 1] * acc[v * 3 + 1] + acc[v * 3 + 2] * acc[v * 3 + 2]);
                if (l < 1e-9f) continue;
                Normals[v * 3] = acc[v * 3] / l; Normals[v * 3 + 1] = acc[v * 3 + 1] / l; Normals[v * 3 + 2] = acc[v * 3 + 2] / l;
            }
        }

        public void Translate(float dx, float dy, float dz)
        {
            for (int i = 0; i < Positions.Count; i += 3) { Positions[i] += dx; Positions[i + 1] += dy; Positions[i + 2] += dz; }
        }
    }
}
