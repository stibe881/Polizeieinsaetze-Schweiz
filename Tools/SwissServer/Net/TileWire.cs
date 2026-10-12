using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Meshing;
using SwissServer.Sim;

namespace SwissServer.Net
{
    /// <summary>Tile message: all meshes of one 1 km tile in tile-local metres (x = east from the SW corner, y = metres a.s.l., z = north) plus the tree list.</summary>
    public static class TileWire
    {
        public static double DetailRadius = 150;
        public static byte[] Build(WorldStore w, TileId id, int quality, double detailE = double.NaN, double detailN = double.NaN)
        {
            if (!w.Source.TryRead(id, out var bytes)) return null;
            var c = TileMeshBuilder.Build(bytes, true, quality == 1, detailE, detailN, quality == 1 ? DetailRadius : 0);
            int stride = quality == 1 ? 1 : quality == 2 ? 2 : 4; MeshData terrain = null;
            if (c.Terrain != null) { int maxStride = (c.Terrain.Resolution - 1) / 32; stride = Math.Min(stride, Math.Max(1, maxStride)); terrain = TerrainMesher.Build(c.Terrain, c.LandUse, stride); }
            var b = new Bin(); b.U8(Msg.Tile).I32(id.X).I32(id.Y).U8(quality); var list = new List<(byte kind, MeshData m, bool uv2)>();
            list.Add((TileMeshKind.Terrain, terrain, false)); list.Add((TileMeshKind.Roads, c.RoadMesh, false)); list.Add((TileMeshKind.BuildingsFar, quality == 1 ? (c.BuildingMeshOutside ?? c.BuildingMesh) : c.BuildingMesh, false));
            if (quality == 1) list.Add((TileMeshKind.BuildingsDetail, c.BuildingMeshDetail, false));
            list.Add((TileMeshKind.Water, c.WaterMesh, false)); list.Add((TileMeshKind.Furniture, c.FurnitureMesh, true)); list.Add((TileMeshKind.Rail, c.RailMesh, false));
            var present = list.FindAll(x => x.m != null && x.m.VertexCount > 0); b.U8(present.Count);
            foreach (var (kind, m, uv2) in present) { b.U8(kind); b.Mesh(m, uv2); }
            // trees: local e/n, ground y, height, crown radius, kind, variant, ratio bucket
            int step = quality >= 3 ? 4 : quality == 2 ? 2 : 1; var trees = new List<SwissPoliceSim.Core.Features.TreeInstance>(); for (int i = 0; i < c.Trees.Count; i += step) trees.Add(c.Trees[i]);
            b.I32(trees.Count); double minE = TileGrid.MinE(id), minN = TileGrid.MinN(id);
            foreach (var t in trees)
            {
                float le = (float)(t.E - minE), ln = (float)(t.N - minN); float gy = c.Terrain != null ? c.Terrain.SampleLocal(le, ln) : 0f; int variant = (Math.Abs((int)(t.E * 13 + t.N * 7)) % TreeMesher.Variants);
                b.F32(le).F32(ln).F32(gy).F32(t.Height).F32(t.Radius).U8(t.Kind).U8(variant).U8(TreeMesher.RatioBucket(t.Radius, t.Height));
            }
            return b.ToArray();
        }
    }
}
