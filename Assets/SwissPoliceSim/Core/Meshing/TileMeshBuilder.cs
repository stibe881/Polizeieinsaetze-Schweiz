using System;
using System.Collections.Generic;
using System.IO;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Geometry;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Storage;
using SwissPoliceSim.Core.Terrain;

namespace SwissPoliceSim.Core.Meshing
{
    /// <summary>Everything the renderer needs for one tile, generated from the container (engine independent, thread safe).</summary>
    public sealed class TileContent
    {
        public TileId Tile;
        public TerrainTileData Terrain;
        public List<RoadWay> Roads = new List<RoadWay>();
        public byte[] LandUse;
        public List<TreeInstance> Trees = new List<TreeInstance>();
        public List<PointFeature> Points = new List<PointFeature>();
        public List<RailFeature> Rails = new List<RailFeature>();
        public List<BuildingFeature> Buildings = new List<BuildingFeature>();
        public MeshData RoadMesh, BuildingMesh, WaterMesh, FurnitureMesh;
    }

    public static class TileMeshBuilder
    {
        /// <summary>Bern old town centre (Zytglogge) – buildings inside use the sandstone palette. Radius in metres.</summary>
        static readonly Lv95 AltstadtCentre = SwissCoordinateConverter.ToLv95(46.9480, 7.4474);
        const double AltstadtRadius = 480;

        public static TileContent Build(byte[] containerBytes, bool meshes = true)
        {
            var c = TileContainer.Deserialize(containerBytes);
            var t = new TileContent { Tile = c.Tile };
            double te = TileGrid.MinE(c.Tile), tn = TileGrid.MinN(c.Tile);
            if (c.TryGet("TERR", out var terr)) using (var ms = new MemoryStream(terr)) t.Terrain = TerrainTileData.Read(ms);
            if (c.TryGet("ROAD", out var road)) using (var ms = new MemoryStream(road)) t.Roads = RoadTileFile.Read(ms, out _);
            if (c.TryGet("LAND", out var land)) t.LandUse = land;
            if (c.TryGet("TREE", out var tree)) t.Trees = TileCodec.ReadTrees(tree, c.Tile);
            if (c.TryGet("POIN", out var poin)) t.Points = TileCodec.ReadPoints(poin, c.Tile);
            if (c.TryGet("RAIL", out var rail)) t.Rails = TileCodec.ReadRails(rail, c.Tile);
            if (c.TryGet("BLDG", out var bl)) t.Buildings = TileCodec.ReadBuildings(bl, c.Tile);
            if (c.TryGet("WMSH", out var wm)) t.WaterMesh = MeshDataIO.Read(wm);
            if (!meshes) return t;

            Func<double, double, float> ground = (x, z) => t.Terrain != null ? t.Terrain.SampleLocal(x, z) : 0f;

            var rm = RoadMesher.NewMesh(); int wi = 0;
            foreach (var w in t.Roads) RoadMesher.AddWay(rm, w, te, tn, ground, wi++);
            foreach (var p in t.Points)
                if (p.Kind == PointKind.Crossing)
                {
                    RoadWay best = null; double bd = 30 * 30;
                    foreach (var w in t.Roads)
                    {
                        if (w.Class > RoadClass.Tertiary || w.Tunnel) continue;
                        for (int i = 0; i < w.PointCount - 1; i++)
                        {
                            double d = PolygonUtil.DistToSegment(new Vec2d(p.E, p.N), new Vec2d(w.E[i], w.N[i]), new Vec2d(w.E[i + 1], w.N[i + 1]));
                            if (d * d < bd) { bd = d * d; best = w; }
                        }
                    }
                    if (best != null) RoadMesher.AddCrossing(rm, p.E, p.N, te, tn, best, ground);
                }
            t.RoadMesh = rm;
            t.FurnitureMesh = FurnitureMesher.Build(t.Points, t.Roads, te, tn, ground);

            var bm = BuildingMesher.NewMesh();
            foreach (var b in t.Buildings)
            {
                var cen = PolygonUtil.Centroid(b.Footprint.Outer);
                bool old = Math.Sqrt(Math.Pow(cen.X - AltstadtCentre.E, 2) + Math.Pow(cen.Y - AltstadtCentre.N, 2)) < AltstadtRadius;
                BuildingMesher.Build(b, te, tn, bm, old ? BuildingStyle.Sandstone : BuildingStyle.Swiss);
            }
            t.BuildingMesh = bm;
            return t;
        }
    }
}
