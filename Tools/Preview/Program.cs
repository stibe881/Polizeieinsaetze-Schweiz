using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Storage;
using SwissPoliceSim.Core.Terrain;
using SwissPoliceSim.Core.Textures;

/// <summary>
/// Preview renders of the generated world. Usage:
///   Preview --data Assets/StreamingAssets/SwissWorld/tiles --scene aerial|street|car --out file.png [--w 1600 --h 900 --ss 2]
/// </summary>
static class Program
{
    static double OE, ON;

    static int Main(string[] args)
    {
        string data = "Assets/StreamingAssets/SwissWorld/tiles", scene = "aerial", outPath = "preview.png"; int w = 1600, h = 900, ss = 2;
        string cam = null;
        for (int i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--data": data = args[++i]; break; case "--scene": scene = args[++i]; break; case "--out": outPath = args[++i]; break;
                case "--cam": cam = args[++i]; break; case "--w": w = int.Parse(args[++i]); break; case "--h": h = int.Parse(args[++i]); break; case "--ss": ss = int.Parse(args[++i]); break;
            }
        var centre = SwissCoordinateConverter.ToLv95(46.94658, 7.44410);   // Bundesplatz
        OE = centre.E; ON = centre.N;
        var r = new SoftRenderer(w, h, ss);
        var mats = new Mats();
        if (scene.StartsWith("car")) { CarScene(r, mats, scene); }
        else WorldScene(r, mats, data, scene, cam);
        r.SavePng(outPath);
        Console.WriteLine("wrote " + outPath);
        return 0;
    }

    sealed class Mats
    {
        public Mat Grass = new Mat { Mips = Mat.MakeMips(ProceduralTextures.Grass()), UvScale = 1f, Specular = 0 };
        public Mat Asphalt0 = new Mat { Mips = Mat.MakeMips(ProceduralTextures.Asphalt()), Specular = 0.12f, Gloss = 20 };
        public Mat Asphalt1 = new Mat { Mips = Mat.MakeMips(ProceduralTextures.Asphalt()), Specular = 0.12f, Gloss = 20 };
        public Mat Asphalt2 = new Mat { Mips = Mat.MakeMips(ProceduralTextures.Asphalt()), Specular = 0.1f, Gloss = 20 };
        public Mat Sidewalk = new Mat { Mips = Mat.MakeMips(ProceduralTextures.Pavement()), Specular = 0.05f };
        public Mat MarkWhite = new Mat { UseVertexColor = true, Base = new Vector3(0.85f), Specular = 0.05f };
        public Mat Facade0 = new Mat { Mips = Mat.MakeMips(ProceduralTextures.FacadeResidential()), Specular = 0.03f };
        public Mat Facade1 = new Mat { Mips = Mat.MakeMips(ProceduralTextures.FacadeCommercial()), Specular = 0.03f };
        public Mat Facade2 = new Mat { Mips = Mat.MakeMips(ProceduralTextures.FacadeIndustrial()), Specular = 0.03f };
        public Mat RoofPitched = new Mat { Mips = Mat.MakeMips(ProceduralTextures.RoofTiles()), Specular = 0.06f, DoubleSided = true };
        public Mat RoofFlat = new Mat { Mips = Mat.MakeMips(ProceduralTextures.FlatRoof()), Specular = 0.02f, DoubleSided = true };
        public Mat Water = new Mat { Mips = Mat.MakeMips(ProceduralTextures.Water()), Alpha = 0.82f, Specular = 0.9f, Gloss = 180, Sky = true, UseVertexColor = false, DoubleSided = true };
        public Mat Metal = new Mat { UseVertexColor = true, Specular = 0.3f, Gloss = 30 };
        public Mat Lens = new Mat { UseVertexColor = true, Emissive = new Vector3(0.35f), Specular = 0.5f };
        public Mat SignFace = new Mat { UseVertexColor = true, Specular = 0.3f, DoubleSided = true };
        public Mat Trunk = new Mat { UseVertexColor = true };
        public Mat Foliage = new Mat { UseVertexColor = true, Specular = 0.04f };
    }

    // ------------------------------------------------------------------ world
    static void WorldScene(SoftRenderer r, Mats m, string data, string scene, string cam)
    {
        var src = new LooseTileSource(data);
        int cx = (int)Math.Floor(OE / 1000), cy = (int)Math.Floor(ON / 1000);
        int rad = scene == "aerial" ? 2 : 1;
        var contents = new List<TileContent>();
        for (int dx = -rad; dx <= rad; dx++)
            for (int dy = -rad; dy <= rad; dy++)
                if (src.TryRead(new TileId(cx + dx, cy + dy), out var bytes)) contents.Add(TileMeshBuilder.Build(bytes));
        Console.WriteLine($"loaded {contents.Count} tiles");

        var treeMeshes = new Dictionary<(byte, int, int), MeshData>();
        foreach (var t in contents)
        {
            var off = Matrix4x4.CreateTranslation((float)(TileGrid.MinE(t.Tile) - OE), 0, (float)(TileGrid.MinN(t.Tile) - ON));
            r.Add(TerrainMesh(t, 1), off, m.Grass);
            r.Add(t.RoadMesh, off, m.Asphalt0, m.Asphalt1, m.Asphalt2, m.Sidewalk, m.MarkWhite, m.MarkWhite);
            r.Add(t.FurnitureMesh, off, m.Metal, m.Lens, m.SignFace);
            r.Add(t.BuildingMesh, off, m.Facade0, m.Facade1, m.Facade2, m.RoofPitched, m.RoofFlat);
            if (t.WaterMesh != null) r.Add(t.WaterMesh, off, m.Water);
            int rn = 0;
            foreach (var tr in t.Trees)
            {
                double lx = tr.E - TileGrid.MinE(t.Tile), lz = tr.N - TileGrid.MinN(t.Tile);
                double wx = tr.E - OE, wz = tr.N - ON;
                if (scene == "street" && Math.Abs(wx) + Math.Abs(wz) > 500) continue;
                if (scene == "aerial" && (rn++ % 3) != 0 && Math.Abs(wx) + Math.Abs(wz) > 700) continue;    // thin out distant trees for preview speed
                int variant = (int)((tr.E * 7 + tr.N * 13)) & 3;
                bool near = Math.Abs(wx) + Math.Abs(wz) < 600;
                int lod = (Math.Abs(wx) + Math.Abs(wz) < 220) ? 0 : (near ? 1 : 2);
                if (!treeMeshes.TryGetValue((tr.Kind, variant, lod), out var tm)) treeMeshes[(tr.Kind, variant, lod)] = tm = TreeMesher.Build(tr.Kind, variant, lod);
                float gy = t.Terrain != null ? t.Terrain.SampleLocal(lx, lz) : 0;
                var mat = Matrix4x4.CreateScale(tr.Radius * 1.0f, tr.Height, tr.Radius * 1.0f) * Matrix4x4.CreateTranslation((float)wx, gy - 0.2f, (float)wz);
                r.Add(tm, mat, m.Trunk, m.Foliage);
            }
        }

        if (scene == "drive") { DriveScene(r, m, contents, cam); return; }
        if (scene == "aerial")
        {
            r.CamPos = new Vector3(-330, 150, -480); r.CamTarget = new Vector3(80, 520 - 480 + 20, 40); r.FovY = 58;
            // terrain y is metres a.s.l. so camera y must be above ~540
            r.CamPos = new Vector3(-420, 700, -520); r.CamTarget = new Vector3(60, 520, 40);
            if (cam != null) { var a = cam.Split(','); r.CamPos = new Vector3(float.Parse(a[0]), float.Parse(a[1]), float.Parse(a[2])); r.CamTarget = new Vector3(float.Parse(a[3]), float.Parse(a[4]), float.Parse(a[5])); r.FovY = float.Parse(a[6]); }
        }
        else
        {
            // --cam px,pz,lx,lz,height,fov  (local metres from the Bundesplatz; height above ground)
            var q = (cam ?? "-8,-120,-30,60,2.0,62").Split(',');
            float px = float.Parse(q[0]), pz = float.Parse(q[1]), lx = float.Parse(q[2]), lz = float.Parse(q[3]), hh = float.Parse(q[4]);
            float gp = Ground(contents, px, pz), gl = Ground(contents, lx, lz);
            r.CamPos = new Vector3(px, gp + hh, pz); r.CamTarget = new Vector3(lx, gl + hh * 0.9f, lz); r.FovY = float.Parse(q[5]);
        }
        r.Render(new Vector3(0, 520, 0), scene == "aerial" ? 1900 : 700);
    }

    static void DriveScene(SoftRenderer r, Mats m, List<TileContent> contents, string cam)
    {
        // pick the road closest to the Bundesplatz with a long, fairly straight stretch
        var q = (cam ?? "0,0").Split(',');
        double ox = double.Parse(q[0]), oz = double.Parse(q[1]);
        SwissPoliceSim.Core.Roads.RoadWay best = null; int bi = 0; double bd = 1e18;
        foreach (var t in contents)
            foreach (var w in t.Roads)
            {
                if (w.Class > SwissPoliceSim.Core.Roads.RoadClass.Tertiary || w.Tunnel || w.Bridge) continue;
                for (int i = 0; i + 1 < w.PointCount; i++)
                {
                    double d = Math.Pow(w.E[i] - OE - ox, 2) + Math.Pow(w.N[i] - ON - oz, 2);
                    double len = Math.Sqrt(Math.Pow(w.E[i + 1] - w.E[i], 2) + Math.Pow(w.N[i + 1] - w.N[i], 2));
                    if (len > 25 && d < bd) { bd = d; best = w; bi = i; }
                }
            }
        if (best == null) throw new Exception("no road");
        var cars = new List<(BodyKind, CarLivery, double)>
        {
            (BodyKind.Wagon, CarLivery.PatrolCar(), 14),
            (BodyKind.Hatchback, CarLivery.Civilian(190, 30, 30, "ZH 482913"), 34),
            (BodyKind.Suv, CarLivery.Civilian(30, 40, 60, "BE 305117"), 52),
            (BodyKind.Sedan, CarLivery.Civilian(200, 200, 205, "LU 77421"), 72),
        };
        double sx = best.E[bi], sz = best.N[bi]; double ddx = best.E[bi + 1] - best.E[bi], ddz = best.N[bi + 1] - best.N[bi]; double dl = Math.Sqrt(ddx * ddx + ddz * ddz); ddx /= dl; ddz /= dl;
        double nx = -ddz, nz = ddx;   // left normal; right-hand traffic: cars drive on the right → offset to the right = -n
        Func<double, (double e, double n, double dx, double dz)> At = dist =>
        {
            int i = bi; double rem = dist, e = best.E[i], n = best.N[i];
            while (i + 1 < best.PointCount)
            {
                double ex = best.E[i + 1] - best.E[i], en = best.N[i + 1] - best.N[i], el = Math.Sqrt(ex * ex + en * en), startOff = i == bi ? 0 : 0;
                if (rem <= el) { double t = rem / el; return (best.E[i] + ex * t, best.N[i] + en * t, ex / el, en / el); }
                rem -= el; i++;
            }
            int l = best.PointCount - 1; double fx = best.E[l] - best.E[l - 1], fn = best.N[l] - best.N[l - 1], fl = Math.Sqrt(fx * fx + fn * fn);
            return (best.E[l], best.N[l], fx / fl, fn / fl);
        };
        var tyre = new Mat { UseVertexColor = true, Specular = 0.05f, Gloss = 8 };
        var rim = new Mat { UseVertexColor = true, Specular = 0.9f, Gloss = 80, Sky = true };
        float lane = best.CarriagewayWidth() * 0.25f;
        foreach (var (kind, liv, dist) in cars)
        {
            var model = CarMesher.Build(kind, liv);
            var p = At(dist); double rx = -(-p.dz), rz = -(p.dx);       // right of travel = (dz, -dx)
            double e = p.e + p.dz * lane, n = p.n - p.dx * lane;
            float gx = (float)(e - OE), gz = (float)(n - ON); float gy = Ground(contents, gx, gz) + 0.10f;
            float yaw = (float)Math.Atan2(p.dx, p.dz);
            var rotT = Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(gx, gy, gz);
            var decal = Mat.MakeMips(ProceduralTextures.DecalAtlas()); var plate = Mat.MakeMips(ProceduralTextures.Plate(liv.Plate));
            var paint = new Mat { UseVertexColor = true, Specular = 0.95f, Gloss = 90, Sky = true };
            var glass = new Mat { UseVertexColor = false, Base = new Vector3(0.05f, 0.07f, 0.09f), Alpha = 0.45f, Specular = 1f, Gloss = 200, Sky = true, DoubleSided = true };
            var trim = new Mat { UseVertexColor = true, Specular = 0.2f, Gloss = 20 }; var chrome = new Mat { UseVertexColor = true, Specular = 1f, Gloss = 120, Sky = true };
            var lampF = new Mat { UseVertexColor = true, Emissive = new Vector3(0.1f), Specular = 0.9f }; var lampR = new Mat { UseVertexColor = true, Emissive = new Vector3(0.08f, 0, 0) };
            var amber = new Mat { UseVertexColor = true, Emissive = new Vector3(0.1f, 0.05f, 0) }; var blue = new Mat { UseVertexColor = true, Emissive = new Vector3(0.05f, 0.12f, 0.9f) };
            var plateM = new Mat { Mips = plate, UseVertexColor = false }; var decalM = new Mat { Mips = decal, UseVertexColor = false, CutoutAlpha = true };
            var interior = new Mat { UseVertexColor = true, DoubleSided = true };
            r.Add(model.Body, rotT, paint, glass, trim, lampF, lampR, amber, blue, blue, plateM, decalM, interior, chrome);
            for (int i = 0; i < 4; i++)
            {
                var c = model.WheelCentres[i];
                r.Add(model.Wheel, (c[0] < 0 ? Matrix4x4.CreateRotationY((float)Math.PI) : Matrix4x4.Identity) * Matrix4x4.CreateTranslation(c[0], c[1], c[2]) * rotT, tyre, rim);
            }
        }
        var start = At(0);
        float cxp = (float)(start.e - OE) - (float)start.dx * 14 + (float)start.dz * lane, czp = (float)(start.n - ON) - (float)start.dz * 14 - (float)start.dx * lane;
        float cy = Ground(contents, cxp, czp) + 1.7f;
        r.CamPos = new Vector3(cxp, cy, czp);
        var look = At(60); r.CamTarget = new Vector3((float)(look.e - OE), Ground(contents, (float)(look.e - OE), (float)(look.n - ON)) + 1.2f, (float)(look.n - ON)); r.FovY = 58;
        r.Render(new Vector3(cxp, 540, czp), 420);
    }

    static float Ground(List<TileContent> tiles, float x, float z)
    {
        double e = OE + x, n = ON + z;
        foreach (var t in tiles)
            if (TileGrid.FromLv95(e, n) == t.Tile && t.Terrain != null) return t.Terrain.SampleLocal(e - TileGrid.MinE(t.Tile), n - TileGrid.MinN(t.Tile));
        return 540;
    }

    static MeshData TerrainMesh(TileContent t, int stride)
    {
        var d = t.Terrain; var m = new MeshData(1); if (d == null) return m;
        int res = d.Resolution, n = (res - 1) / stride + 1; float sp = d.Spacing * stride;
        var tint = new Dictionary<byte, byte[]>
        {
            [0] = new byte[] { 190, 205, 150 }, [(byte)LandUseClass.Grass] = new byte[] { 185, 205, 140 }, [(byte)LandUseClass.Forest] = new byte[] { 110, 140, 90 },
            [(byte)LandUseClass.Farmland] = new byte[] { 190, 170, 110 }, [(byte)LandUseClass.Orchard] = new byte[] { 130, 165, 95 }, [(byte)LandUseClass.Vineyard] = new byte[] { 150, 160, 90 },
            [(byte)LandUseClass.Residential] = new byte[] { 160, 165, 130 }, [(byte)LandUseClass.Commercial] = new byte[] { 150, 148, 140 }, [(byte)LandUseClass.Industrial] = new byte[] { 140, 138, 132 },
            [(byte)LandUseClass.Park] = new byte[] { 175, 205, 140 }, [(byte)LandUseClass.Cemetery] = new byte[] { 120, 160, 100 }, [(byte)LandUseClass.Rock] = new byte[] { 150, 145, 135 },
            [(byte)LandUseClass.Scree] = new byte[] { 160, 155, 145 }, [(byte)LandUseClass.Water] = new byte[] { 70, 60, 45 }, [(byte)LandUseClass.Parking] = new byte[] { 130, 130, 128 },
            [(byte)LandUseClass.Railway] = new byte[] { 120, 115, 105 }, [(byte)LandUseClass.Plaza] = new byte[] { 165, 160, 150 }, [(byte)LandUseClass.Urban] = new byte[] { 150, 146, 138 }, [(byte)LandUseClass.Sports] = new byte[] { 110, 165, 85 }, [(byte)LandUseClass.Wetland] = new byte[] { 110, 140, 90 }
        };
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int c = Math.Min(res - 1, i * stride), r = Math.Min(res - 1, j * stride);
                float x = c * d.Spacing, z = r * d.Spacing, y = d.Height(c, r);
                float hx = d.Height(Math.Min(res - 1, c + 1), r) - d.Height(Math.Max(0, c - 1), r), hz = d.Height(c, Math.Min(res - 1, r + 1)) - d.Height(c, Math.Max(0, r - 1));
                float nx = -hx, nz = -hz, ny = 2 * d.Spacing, l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                byte cls = 0;
                if (t.LandUse != null) cls = t.LandUse[Math.Min(127, (int)(z / 7.8125f)) * 128 + Math.Min(127, (int)(x / 7.8125f))];
                var col = tint.TryGetValue(cls, out var cc) ? cc : tint[0];
                float slope = 1f - ny / l; float rock = Math.Min(1, Math.Max(0, (slope - 0.18f) * 4));
                byte rr = (byte)(col[0] * (1 - rock) + 130 * rock), gg = (byte)(col[1] * (1 - rock) + 125 * rock), bb = (byte)(col[2] * (1 - rock) + 115 * rock);
                m.AddVertex(x, y, z, nx / l, ny / l, nz / l, (t.Tile.X * 1000 + x) * 0.1f, (t.Tile.Y * 1000 + z) * 0.1f, rr, gg, bb);
            }
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, dd = c + 1;
                m.AddTriangle(0, a, c, b); m.AddTriangle(0, b, c, dd);
            }
        return m;
    }

    // ------------------------------------------------------------------ car
    static void CarScene(SoftRenderer r, Mats m, string scene)
    {
        var liv = CarLivery.PatrolCar();
        var car = CarMesher.Build(BodyKind.Wagon, liv);
        var decal = Mat.MakeMips(ProceduralTextures.DecalAtlas());
        var plate = Mat.MakeMips(ProceduralTextures.Plate(liv.Plate));
        var paint = new Mat { UseVertexColor = true, Specular = 0.95f, Gloss = 90, Sky = true };
        var glass = new Mat { UseVertexColor = false, Base = new Vector3(0.05f, 0.07f, 0.09f), Alpha = 0.45f, Specular = 1f, Gloss = 200, Sky = true, DoubleSided = true };
        var trim = new Mat { UseVertexColor = true, Specular = 0.2f, Gloss = 20 };
        var chrome = new Mat { UseVertexColor = true, Specular = 1f, Gloss = 120, Sky = true };
        var lampF = new Mat { UseVertexColor = true, Emissive = new Vector3(0.12f), Specular = 0.9f, Gloss = 90, Sky = true };
        var lampR = new Mat { UseVertexColor = true, Emissive = new Vector3(0.10f, 0, 0), Specular = 0.6f, Gloss = 60 };
        var amber = new Mat { UseVertexColor = true, Emissive = new Vector3(0.12f, 0.06f, 0), Specular = 0.6f };
        var blue = new Mat { UseVertexColor = true, Emissive = new Vector3(0.05f, 0.12f, 0.9f), Specular = 0.5f };
        var plateM = new Mat { Mips = plate, UseVertexColor = false, Specular = 0.3f };
        var decalM = new Mat { Mips = decal, UseVertexColor = false, CutoutAlpha = true, Specular = 0.5f, Gloss = 40 };
        var interior = new Mat { UseVertexColor = true, DoubleSided = true, Specular = 0.05f };
        var mats = new[] { paint, glass, trim, lampF, lampR, amber, blue, blue, plateM, decalM, interior, chrome };
        r.Add(car.Body, Matrix4x4.Identity, mats);

        var tyre = new Mat { UseVertexColor = true, Specular = 0.05f, Gloss = 8 };
        var rim = new Mat { UseVertexColor = true, Specular = 0.9f, Gloss = 80, Sky = true };
        for (int i = 0; i < 4; i++)
        {
            var c = car.WheelCentres[i];
            var mt = (c[0] < 0 ? Matrix4x4.CreateRotationY((float)Math.PI) : Matrix4x4.Identity) * Matrix4x4.CreateTranslation(c[0], c[1], c[2]);
            r.Add(car.Wheel, mt, tyre, rim);
        }
        // ground plane
        var g = new MeshData(1);
        float S = 60;
        g.AddVertex(-S, 0, -S, 0, 1, 0, -S * 0.15f, -S * 0.15f, 255, 255, 255); g.AddVertex(S, 0, -S, 0, 1, 0, S * 0.15f, -S * 0.15f, 255, 255, 255);
        g.AddVertex(S, 0, S, 0, 1, 0, S * 0.15f, S * 0.15f, 255, 255, 255); g.AddVertex(-S, 0, S, 0, 1, 0, -S * 0.15f, S * 0.15f, 255, 255, 255);
        g.AddTriangle(0, 0, 2, 1); g.AddTriangle(0, 0, 3, 2);
        r.Add(g, Matrix4x4.Identity, m.Asphalt1);
        r.SunDir = Vector3.Normalize(new Vector3(-0.5f, -0.6f, 0.45f)); r.SunColor = new Vector3(1.0f, 0.93f, 0.80f) * 1.5f;
        r.CamPos = new Vector3(4.6f, 1.35f, 5.6f); r.CamTarget = new Vector3(0, 0.75f, 0.1f); r.FovY = 32;
        if (scene == "carrear") { r.CamPos = new Vector3(-4.2f, 1.5f, -6.0f); r.CamTarget = new Vector3(0, 0.8f, -0.4f); }
        if (scene == "carside") { r.CamPos = new Vector3(7.5f, 0.9f, -0.2f); r.CamTarget = new Vector3(0, 0.7f, 0.0f); r.FovY = 26; }
        if (scene == "carfront") { r.CamPos = new Vector3(-2.6f, 1.2f, 6.5f); r.CamTarget = new Vector3(0, 0.75f, 0.5f); r.FovY = 30; }
        r.FogDensity = 1f / 9000f;
        r.Render(new Vector3(0, 1, 0), 40);
    }
}
