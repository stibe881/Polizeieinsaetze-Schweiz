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
        try { if (Environment.GetEnvironmentVariable("NOHDRI") == null) r.SkyMap = Hdri.Load("Assets/SwissPoliceSim/Resources/Sky/sky_day.hdr"); } catch (Exception ex) { Console.WriteLine("HDRI unavailable: " + ex.Message); }
        var mats = new Mats();
        if (scene.StartsWith("car")) { CarScene(r, mats, scene); }
        else if (scene == "trees") { TreeScene(r, mats); }
        else if (scene == "people") { PeopleScene(r, mats); }
        else if (scene == "map") { MapScene(data, outPath); return 0; }
        else WorldScene(r, mats, data, scene, cam);
        r.SavePng(outPath);
        Console.WriteLine("wrote " + outPath);
        return 0;
    }

    static Mat Pbr(string set, float perM, float boost, Func<Mat> fallback, bool dbl = false)
    {
        if (Environment.GetEnvironmentVariable("NOPBR") != null) return fallback();
        try { return PbrLoader.Make(set, perM, set.StartsWith("Asphalt") ? boost * 0.8f : boost, true, dbl); } catch (Exception ex) { Console.WriteLine("PBR set " + set + " unavailable: " + ex.Message); return fallback(); }
    }

    sealed class Mats
    {
        public Mat Grass = Pbr("Grass004", 0.25f, 1.3f, () => new Mat { Mips = Mat.MakeMips(ProceduralTextures.Grass()), UvScale = 2.5f, Specular = 0 });
        public Mat Asphalt0 = Pbr("Asphalt031", 0.25f, 1.5f, () => new Mat { Mips = Mat.MakeMips(ProceduralTextures.Asphalt()), Specular = 0.12f, Gloss = 20 });
        public Mat Asphalt1 = Pbr("Asphalt004", 0.25f, 1.6f, () => new Mat { Mips = Mat.MakeMips(ProceduralTextures.Asphalt()), Specular = 0.12f, Gloss = 20 });
        public Mat Asphalt2 = Pbr("Asphalt004", 0.25f, 1.5f, () => new Mat { Mips = Mat.MakeMips(ProceduralTextures.Asphalt()), Specular = 0.1f, Gloss = 20 });
        public Mat Sidewalk = Pbr("PavingStones070", 0.5f, 1.3f, () => new Mat { Mips = Mat.MakeMips(ProceduralTextures.Pavement()), Specular = 0.05f });
        public Mat MarkWhite = new Mat { UseVertexColor = true, Base = new Vector3(0.85f), Specular = 0.05f };
        public Mat Facade0 = new Mat { Mips = Mat.MakeMips(ProceduralTextures.FacadeResidential()), Specular = 0.03f };
        public Mat Facade1 = new Mat { Mips = Mat.MakeMips(ProceduralTextures.FacadeCommercial()), Specular = 0.03f };
        public Mat Facade2 = new Mat { Mips = Mat.MakeMips(ProceduralTextures.FacadeIndustrial()), Specular = 0.03f };
        public Mat RoofPitched = Pbr("RoofingTiles014A", 0.5f, 1.5f, () => new Mat { Mips = Mat.MakeMips(ProceduralTextures.RoofTiles()), Specular = 0.06f, DoubleSided = true }, true);
        public Mat RoofFlat = Pbr("Gravel022", 0.5f, 1.4f, () => new Mat { Mips = Mat.MakeMips(ProceduralTextures.FlatRoof()), Specular = 0.02f, DoubleSided = true }, true);
        public Mat Plaster = Pbr("Plaster003", 0.4f, 1.7f, () => new Mat { Mips = Mat.MakeMips(ProceduralTextures.PlasterWall()), Specular = 0.02f });
        public Mat Window = new Mat { Mips = Mat.MakeMips(ProceduralTextures.WindowAtlas()), Specular = 0.55f, Gloss = 90, Sky = true };
        public Mat Trim = Pbr("Concrete025", 0.5f, 1.6f, () => new Mat { Mips = Mat.MakeMips(ProceduralTextures.Stone()), Specular = 0.03f, DoubleSided = true }, true);
        public Mat ShutterM = new Mat { Mips = Mat.MakeMips(ProceduralTextures.ShutterSlats()), Specular = 0.05f, DoubleSided = true };
        public Mat Railing = new Mat { Mips = Mat.MakeMips(ProceduralTextures.Railing()), CutoutAlpha = true, DoubleSided = true };
        public Mat Gravel = Pbr("Gravel022", 0.5f, 1.2f, () => new Mat { Mips = Mat.MakeMips(ProceduralTextures.Gravel()), UvScale = 1f });
        public Mat Concrete = Pbr("Concrete025", 0.5f, 1.4f, () => new Mat { Mips = Mat.MakeMips(ProceduralTextures.Stone()), UseVertexColor = true });
        public Mat Water = new Mat { Mips = Mat.MakeMips(ProceduralTextures.Water()), Alpha = 0.82f, Specular = 0.9f, Gloss = 180, Sky = true, UseVertexColor = false, DoubleSided = true };
        public Mat Metal = new Mat { UseVertexColor = true, Specular = 0.3f, Gloss = 30 };
        public Mat Lens = new Mat { UseVertexColor = true, Emissive = new Vector3(0.35f), Specular = 0.5f };
        public Mat SignFace = new Mat { UseVertexColor = true, Specular = 0.3f, DoubleSided = true };
        public Mat Trunk = new Mat { UseVertexColor = true };
        public Mat Foliage = new Mat { Mips = Mat.MakeMips(ProceduralTextures.FoliageAtlas()), UseVertexColor = true, CutoutAlpha = true, DoubleSided = true, Specular = 0.04f };
    }

    static void MapScene(string data, string outPath)
    {
        var src = new LooseTileSource(data); int cx = (int)Math.Floor(OE / 1000), cy = (int)Math.Floor(ON / 1000); int N = 5, res = 400;
        var big = new byte[N * res * N * res * 3];
        for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++)
        {
            if (!src.TryRead(new TileId(cx + dx, cy + dy), out var bytes)) continue;
            var t = TileMeshBuilder.Build(bytes, false); var px = MapRasterizer.RenderTile(t, res);
            for (int y = 0; y < res; y++) for (int x = 0; x < res; x++)
            {
                int gx = (dx + 2) * res + x, gy = (N - 1 - (dy + 2)) * res + (res - 1 - y);        // flip: image row 0 = north
                int o = (gy * N * res + gx) * 3, i = (y * res + x) * 4; big[o] = px[i]; big[o + 1] = px[i + 1]; big[o + 2] = px[i + 2];
            }
        }
        PngWriter.Write(outPath, N * res, N * res, big);
    }

    static void PeopleScene(SoftRenderer r, Mats m)
    {
        var ground = new MeshData(1);
        ground.AddQuad(0, new[] { -60f, 0, 60f }, new[] { 60f, 0, 60f }, new[] { 60f, 0, -30f }, new[] { -60f, 0, -30f }, 0, 0, 30, 30, 120, 120, 118);
        r.Add(ground, Matrix4x4.Identity, m.Sidewalk);
        var body = new Mat { UseVertexColor = true, Specular = 0.04f, Gloss = 8, DoubleSided = Environment.GetEnvironmentVariable("DBL") != null }; var leather = new Mat { UseVertexColor = true, Specular = 0.35f, Gloss = 40 };
        var patch = new Mat { Mips = Mat.MakeMips(ProceduralTextures.UniformPatch()), UseVertexColor = false, Specular = 0.2f };
        var specs = new List<HumanSpec>
        {
            HumanSpec.Officer(false, 1, true), HumanSpec.Officer(true, 2, false),
            HumanSpec.FromPed(false, false, false, 1, 0, 1, 1, 1.82f, 3), HumanSpec.FromPed(true, false, false, 2, 7, 0, 3, 1.66f, 4), HumanSpec.FromPed(false, false, true, 6, 3, 2, 4, 1.70f, 5),
            HumanSpec.FromPed(true, true, false, 7, 5, 4, 5, 1.22f, 6), HumanSpec.FromPed(false, false, false, 4, 1, 5, 6, 1.78f, 7)
        };
        float x = -2.9f; int k = 0;
        foreach (var sp in specs)
        {
            var model = HumanMesher.Build(sp, 0);
            var world = new Dictionary<string, Matrix4x4>();
            foreach (var part in model.Parts)
            {
                var local = Matrix4x4.CreateTranslation(part.Pivot[0], part.Pivot[1], part.Pivot[2]);
                if (part.Name.StartsWith("UpperArm")) local = Matrix4x4.CreateRotationZ((part.Name.EndsWith("L") ? 1 : -1) * 0.06f) * local;
                var parent = part.Parent == null ? Matrix4x4.CreateTranslation(x, 0, 6) * Matrix4x4.CreateRotationY(0.0f) : world[part.Parent];
                var wm = local * parent; world[part.Name] = wm;
                r.Add(part.Mesh, wm, body, leather, patch);
            }
            x += 1.0f; k++;
        }
        r.CamPos = new Vector3(0.0f, 1.55f, -2.2f); r.CamTarget = new Vector3(0.0f, 1.05f, 6); r.FovY = 38;
        r.CamPos = new Vector3(-2.2f, 1.5f, 10.2f); r.CamTarget = new Vector3(0.0f, 1.0f, 6); r.FovY = 32;
        if (Environment.GetEnvironmentVariable("PEOPLE_CLOSE") != null) { r.CamPos = new Vector3(-4.2f, 1.2f, 8.5f); r.CamTarget = new Vector3(-2.0f, 0.95f, 6); r.FovY = 22; r.PostFx = false; }
        r.SunDir = Vector3.Normalize(new Vector3(-0.4f, -0.6f, 0.55f)); r.SunColor = new Vector3(1.0f, 0.95f, 0.85f) * 2.6f;
        r.Render(new Vector3(0, 0, 6), 80);
    }

    static void TreeScene(SoftRenderer r, Mats m)
    {
        var ground = new MeshData(1);
        ground.AddQuad(0, new[] { -60f, 0, 60f }, new[] { 60f, 0, 60f }, new[] { 60f, 0, -30f }, new[] { -60f, 0, -30f }, 0, 0, 30, 30, 120, 160, 80);
        r.Add(ground, Matrix4x4.Identity, m.Grass);
        float x = -30;
        foreach (var (kind, lod) in new[] { ((byte)0, 0), ((byte)0, 1), ((byte)0, 2), ((byte)1, 0), ((byte)1, 1), ((byte)1, 2) })
        {
            float H = kind == 0 ? 15f : 22f, Rr = kind == 0 ? 4.5f : 3.0f; var tm = TreeMesher.Build(kind, 1, lod, TreeMesher.RatioBucket(Rr, H));
            r.Add(tm, Matrix4x4.CreateScale(H) * Matrix4x4.CreateTranslation(x, 0, 18), m.Trunk, m.Foliage);
            x += 12;
        }
        r.CamPos = new Vector3(0, 4, -30); r.CamTarget = new Vector3(0, 9, 18); r.FovY = 50;
        r.Render(new Vector3(0, 0, 0), 300);
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
                if (src.TryRead(new TileId(cx + dx, cy + dy), out var bytes)) contents.Add(TileMeshBuilder.Build(bytes, true, scene != "aerial"));
        Console.WriteLine($"loaded {contents.Count} tiles");

        var treeMeshes = new Dictionary<(byte, int, int), MeshData>();
        foreach (var t in contents)
        {
            var off = Matrix4x4.CreateTranslation((float)(TileGrid.MinE(t.Tile) - OE), 0, (float)(TileGrid.MinN(t.Tile) - ON));
            r.Add(TerrainMesh(t, 1), off, m.Grass);
            r.Add(t.RoadMesh, off, m.Asphalt0, m.Asphalt1, m.Asphalt2, m.Sidewalk, m.MarkWhite, m.MarkWhite);
            r.Add(t.FurnitureMesh, off, m.Metal, m.Lens, m.SignFace);
            r.Add(t.BuildingMeshDetail ?? t.BuildingMesh, off, m.Facade0, m.Facade1, m.Facade2, m.RoofPitched, m.RoofFlat, m.Plaster, m.Window, m.Trim, m.ShutterM, m.Railing);
            if (t.WaterMesh != null) r.Add(t.WaterMesh, off, m.Water);
            if (t.RailMesh != null) r.Add(t.RailMesh, off, m.Gravel, m.Metal, m.Concrete, m.Metal);
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
            if (cam != null && cam.StartsWith("signal"))
            {
                int want = cam.Length > 6 ? int.Parse(cam.Substring(7)) : 0, seen = 0; PointKind kind = PointKind.TrafficSignals;
                if (cam.StartsWith("lamp")) kind = PointKind.StreetLamp;
                foreach (var tc in contents) foreach (var pt in tc.Points) if (pt.Kind == PointKind.TrafficSignals && seen++ == want) { double sx = pt.E - OE, sz = pt.N - ON; cam = $"{sx - 9:F1},{sz - 9:F1},{sx:F1},{sz:F1},2.6,38"; goto found; }
                found:;
            }
            var q = (cam ?? "-8,-120,-30,60,2.0,62").Split(',');
            float px = float.Parse(q[0]), pz = float.Parse(q[1]), lx = float.Parse(q[2]), lz = float.Parse(q[3]), hh = float.Parse(q[4]);
            float gp = Ground(contents, px, pz), gl = Ground(contents, lx, lz);
            r.CamPos = new Vector3(px, gp + hh, pz); r.CamTarget = new Vector3(lx, gl + hh * 0.9f, lz); r.FovY = float.Parse(q[5]);
        }
        AddTrees(r, m, contents, scene, scene == "aerial" ? new Vector3(-420, 0, -520) : new Vector3(r.CamPos.X, 0, r.CamPos.Z));
        r.Render(new Vector3(0, 520, 0), scene == "aerial" ? 1900 : 700);
    }

    static readonly Dictionary<(byte, int, int), MeshData> TreeCache = new Dictionary<(byte, int, int), MeshData>();
    static void AddTrees(SoftRenderer r, Mats m, List<TileContent> contents, string scene, Vector3 cam)
    {
        foreach (var t in contents)
        {
            var off = Matrix4x4.CreateTranslation((float)(TileGrid.MinE(t.Tile) - OE), 0, (float)(TileGrid.MinN(t.Tile) - ON));
            int rn = 0;
            foreach (var tr in t.Trees)
            {
                double lx = tr.E - TileGrid.MinE(t.Tile), lz = tr.N - TileGrid.MinN(t.Tile);
                double wx = tr.E - OE, wz = tr.N - ON;
                double dd = Math.Sqrt((wx - cam.X) * (wx - cam.X) + (wz - cam.Z) * (wz - cam.Z));
                if (scene != "aerial" && dd > 700) continue;
                if (scene == "aerial" && (rn++ % 3) != 0 && dd > 700) continue;
                int variant = (int)((tr.E * 7 + tr.N * 13)) & 3;
                int lod = dd < 160 ? 0 : (dd < 420 ? 1 : 2);
                int rb = TreeMesher.RatioBucket(tr.Radius, tr.Height); int key = lod * 8 + rb;
                if (!TreeCache.TryGetValue((tr.Kind, variant, key), out var tm)) TreeCache[(tr.Kind, variant, key)] = tm = TreeMesher.Build(tr.Kind, variant, lod, rb);
                float gy = t.Terrain != null ? t.Terrain.SampleLocal(lx, lz) : 0;
                var mat = Matrix4x4.CreateScale(tr.Height) * Matrix4x4.CreateTranslation((float)wx, gy - 0.2f, (float)wz);
                r.Add(tm, mat, m.Trunk, m.Foliage);
            }
        }
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
        AddTrees(r, m, contents, "drive", new Vector3(cxp, 0, czp));
        r.Render(new Vector3(cxp, 540, czp), 420);
    }

    static float Ground(List<TileContent> tiles, float x, float z)
    {
        double e = OE + x, n = ON + z;
        foreach (var t in tiles)
            if (TileGrid.FromLv95(e, n) == t.Tile && t.Terrain != null) return t.Terrain.SampleLocal(e - TileGrid.MinE(t.Tile), n - TileGrid.MinN(t.Tile));
        return 540;
    }

    static MeshData TerrainMesh(TileContent t, int stride) => t.Terrain == null ? new MeshData(1) : TerrainMesher.Build(t.Terrain, t.LandUse, stride);

    // ------------------------------------------------------------------ car
    static void CarScene(SoftRenderer r, Mats m, string scene)
    {
        var liv = CarLivery.PatrolCar();
        var lineup = new List<(CarModel car, CarLivery liv, float x)>();
        if (scene == "carlineup")
        {
            float lx = -23f; int ci = 0; byte[][] cols = { new byte[] { 232, 232, 232 }, new byte[] { 36, 62, 120 }, new byte[] { 150, 30, 30 }, new byte[] { 24, 24, 26 }, new byte[] { 170, 172, 176 } };
            foreach (BodyKind bk in new[] { BodyKind.Hatchback, BodyKind.Sedan, BodyKind.Suv, BodyKind.Wagon, BodyKind.Van }) { var c = cols[ci++ % cols.Length]; var lv = CarLivery.Civilian(c[0], c[1], c[2], "ZH 4" + ci + "7821"); lineup.Add((CarMesher.Build(bk, lv), lv, lx)); lx += 6.6f; }
            lineup.Add((CarMesher.Build(BodyKind.Wagon, liv), liv, lx)); lx += 6.6f;
            var ems = new CarLivery { Body = new byte[] { 238, 238, 232 }, Stripe = new byte[] { 220, 40, 30 }, LightBar = true, Plate = "BE 144144" }; lineup.Add((CarMesher.Build(BodyKind.Van, ems), ems, lx)); lx += 6.6f;
            var tow = new CarLivery { Body = new byte[] { 230, 190, 40 }, LightBar = false, Plate = "BE 55 118" }; lineup.Add((CarMesher.Build(BodyKind.Van, tow), tow, lx));
        }
        var car = CarMesher.Build(Environment.GetEnvironmentVariable("CARKIND") != null ? (BodyKind)Enum.Parse(typeof(BodyKind), Environment.GetEnvironmentVariable("CARKIND")) : BodyKind.Wagon, liv);
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
        var tyre = new Mat { UseVertexColor = true, Specular = 0.05f, Gloss = 8 };
        var rim = new Mat { UseVertexColor = true, Specular = 0.9f, Gloss = 80, Sky = true };
        if (lineup.Count == 0) lineup.Add((car, liv, 0f));
        foreach (var (cm, lv, cx) in lineup)
        {
            var place = Matrix4x4.CreateTranslation(cx, 0, 0);
            var plateL = new Mat { Mips = Mat.MakeMips(ProceduralTextures.Plate(lv.Plate)), UseVertexColor = false, Specular = 0.3f };
            var mm = (Mat[])mats.Clone(); mm[8] = plateL;
            r.Add(cm.Body, place, mm);
            for (int i = 0; i < 4; i++)
            {
                var c = cm.WheelCentres[i];
                var mt = (c[0] < 0 ? Matrix4x4.CreateRotationY((float)Math.PI) : Matrix4x4.Identity) * Matrix4x4.CreateTranslation(c[0], c[1], c[2]) * place;
                r.Add(cm.Wheel, mt, tyre, rim);
            }
        }
        // ground plane
        var g = new MeshData(1);
        float S = scene == "carlineup" ? 90 : 60;
        g.AddVertex(-S, 0, -S, 0, 1, 0, -S * 0.15f, -S * 0.15f, 255, 255, 255); g.AddVertex(S, 0, -S, 0, 1, 0, S * 0.15f, -S * 0.15f, 255, 255, 255);
        g.AddVertex(S, 0, S, 0, 1, 0, S * 0.15f, S * 0.15f, 255, 255, 255); g.AddVertex(-S, 0, S, 0, 1, 0, -S * 0.15f, S * 0.15f, 255, 255, 255);
        g.AddTriangle(0, 0, 2, 1); g.AddTriangle(0, 0, 3, 2);
        r.Add(g, Matrix4x4.Identity, m.Asphalt1);
        r.SunDir = Vector3.Normalize(new Vector3(-0.5f, -0.6f, 0.45f)); r.SunColor = new Vector3(1.0f, 0.93f, 0.80f) * 1.5f;
        r.CamPos = new Vector3(4.6f, 1.35f, 5.6f); r.CamTarget = new Vector3(0, 0.75f, 0.1f); r.FovY = 32;
        if (scene == "carrear") { r.CamPos = new Vector3(-4.2f, 1.5f, -6.0f); r.CamTarget = new Vector3(0, 0.8f, -0.4f); }
        if (scene == "carside") { r.CamPos = new Vector3(7.5f, 0.9f, -0.2f); r.CamTarget = new Vector3(0, 0.7f, 0.0f); r.FovY = 26; }
        if (scene == "carfront") { r.CamPos = new Vector3(-2.6f, 1.2f, 6.5f); r.CamTarget = new Vector3(0, 0.75f, 0.5f); r.FovY = 30; }
        if (scene == "carlineup") { { float cx = float.Parse(Environment.GetEnvironmentVariable("LINEUPX") ?? "-1"); r.CamPos = new Vector3(cx - 9f, 1.5f, 21f); r.CamTarget = new Vector3(cx, 0.8f, 0f); r.FovY = 36; } }
        r.FogDensity = 1f / 9000f;
        r.Render(new Vector3(0, 1, 0), scene == "carlineup" ? 60 : 40);
    }
}
