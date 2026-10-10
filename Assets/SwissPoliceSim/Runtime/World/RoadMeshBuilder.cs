using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Terrain;
using UnityEngine;

namespace SwissPoliceSim.World
{
    /// <summary>Plain geometry buffers. Built on a worker thread (no UnityEngine object access), turned into a Mesh on the main thread.</summary>
    public sealed class RoadMeshData
    {
        public const int Groups = 3; // 0 = Autobahn/Autostrasse, 1 = Hauptstrassen, 2 = übrige
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int>[] Triangles = { new List<int>(), new List<int>(), new List<int>() };

        public Mesh ToMesh()
        {
            var m = new Mesh { name = "RoadTile", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(Vertices); m.SetUVs(0, Uvs);
            m.subMeshCount = Groups;
            for (int g = 0; g < Groups; g++) m.SetTriangles(Triangles[g], g);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }

    public static class RoadMeshBuilder
    {
        const float Subdivision = 8f;     // metres between sample points so the road follows the terrain
        const float Lift = 0.12f;         // above terrain surface

        public static int Group(RoadClass c)
        {
            switch (c)
            {
                case RoadClass.Motorway: case RoadClass.MotorwayLink: case RoadClass.Trunk: case RoadClass.TrunkLink: return 0;
                case RoadClass.Primary: case RoadClass.PrimaryLink: case RoadClass.Secondary: case RoadClass.SecondaryLink:
                case RoadClass.Tertiary: case RoadClass.TertiaryLink: return 1;
                default: return 2;
            }
        }

        /// <summary>Builds the road ribbon for one tile in tile-local coordinates (x east, z north, y metres a.s.l.).</summary>
        public static RoadMeshData Build(IList<RoadWay> ways, TerrainTileData terrain, double tileMinE, double tileMinN)
        {
            var data = new RoadMeshData();
            int index = 0;
            foreach (var w in ways)
            {
                index++;
                if (w.PointCount < 2) continue;
                AddWay(data, w, terrain, tileMinE, tileMinN, index);
            }
            return data;
        }

        static void AddWay(RoadMeshData d, RoadWay w, TerrainTileData terrain, double oe, double on, int wayIndex)
        {
            var pts = new List<Vector3>();
            bool hasBaked = w.H.Length == w.PointCount;
            for (int i = 0; i < w.PointCount - 1; i++)
            {
                float x0 = (float)(w.E[i] - oe), z0 = (float)(w.N[i] - on);
                float x1 = (float)(w.E[i + 1] - oe), z1 = (float)(w.N[i + 1] - on);
                float len = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / Subdivision));
                for (int s = (i == 0 ? 0 : 1); s <= steps; s++)
                {
                    float t = s / (float)steps;
                    float x = Mathf.Lerp(x0, x1, t), z = Mathf.Lerp(z0, z1, t);
                    float y;
                    if (w.Bridge && hasBaked && !double.IsNaN(w.H[i]) && !double.IsNaN(w.H[i + 1]))
                        y = (float)(w.H[i] + (w.H[i + 1] - w.H[i]) * t);
                    else
                        y = terrain != null ? terrain.SampleLocal(x, z) : (hasBaked && !double.IsNaN(w.H[i]) ? (float)w.H[i] : 0f);
                    pts.Add(new Vector3(x, y, z));
                }
            }
            if (pts.Count < 2) return;

            // Slight per-class / per-way height offset avoids z-fighting where roads overlap.
            float classLift = Lift + (3 - Group(w.Class)) * 0.015f + (wayIndex % 8) * 0.003f;
            float half = w.VisualWidth() * 0.5f;
            int g = Group(w.Class);
            int baseIndex = d.Vertices.Count;
            float dist = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 prev = pts[Mathf.Max(0, i - 1)], next = pts[Mathf.Min(pts.Count - 1, i + 1)];
                var tan = new Vector3(next.x - prev.x, 0, next.z - prev.z);
                if (tan.sqrMagnitude < 1e-6f) tan = Vector3.forward;
                tan.Normalize();
                var nrm = new Vector3(-tan.z, 0, tan.x);
                if (i > 0) dist += Vector3.Distance(pts[i - 1], pts[i]);
                var p = pts[i]; p.y += classLift;
                d.Vertices.Add(p + nrm * half); d.Uvs.Add(new Vector2(0, dist / 6f));
                d.Vertices.Add(p - nrm * half); d.Uvs.Add(new Vector2(1, dist / 6f));
            }
            for (int i = 0; i < pts.Count - 1; i++)
            {
                int a = baseIndex + i * 2;
                // wound so the surface faces up (+y) for a ribbon with "left" = +normal
                d.Triangles[g].Add(a); d.Triangles[g].Add(a + 2); d.Triangles[g].Add(a + 1);
                d.Triangles[g].Add(a + 1); d.Triangles[g].Add(a + 2); d.Triangles[g].Add(a + 3);
            }
        }
    }
}
