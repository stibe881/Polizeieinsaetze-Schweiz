using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Import;
using SwissPoliceSim.Core.Storage;
using SwissPoliceSim.Core.Terrain;
using SwissPoliceSim.Core.Roads;

// Usage: SwissWorldBaker --lat 46.94658 --lon 7.44410 --radius 2 --out <dir> [--cache <dir>] [--osm api|overpass] [--overpass-url <url>] [--no-surface]
static class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length >= 4 && args[0] == "boundaries")
        {   // SwissWorldBaker boundaries <cantons.shp> <municipalities.shp> <out.spj>
            var ab = AdminBoundaries.FromShapefiles(args[1], args[2], 25);
            ab.Save(args[3]);
            Console.WriteLine($"{ab.Cantons.Count} cantons, {ab.Municipalities.Count} municipalities → {args[3]} ({new System.IO.FileInfo(args[3]).Length / 1024} kB)");
            return 0;
        }
        if (args.Length >= 2 && args[0] == "recompress")
        {   // SwissWorldBaker recompress <tilesDir>: re-encode TERR as V2 (5 cm, predictive) and add the coarse TERC chunk
            var srcT = new LooseTileSource(args[1]); long before = 0, after = 0;
            foreach (var t in new System.Collections.Generic.List<TileId>(srcT.Tiles))
            {
                srcT.TryRead(t, out var tb); before += tb.Length; var tc = TileContainer.Deserialize(tb);
                if (tc.TryGet("TERR", out var terr))
                {
                    TerrainTileData td; using (var ms = new System.IO.MemoryStream(terr)) td = TerrainTileData.Read(ms);
                    if (td.Resolution > 129 && td.Q != null && false) { }
                    // re-quantise to the 5 cm grid
                    var nd = new TerrainTileData { Tile = td.Tile, Resolution = td.Resolution, Q = new ushort[td.Q.Length] };
                    float min = float.MaxValue; for (int i = 0; i < td.Q.Length; i++) { float h = td.MinH + td.Q[i] / 65535f * (td.MaxH - td.MinH); if (h < min) min = h; }
                    nd.MinH = (float)(Math.Floor(min / TerrainTileData.Step) * TerrainTileData.Step); nd.MaxH = nd.MinH + 65535 * TerrainTileData.Step;
                    for (int i = 0; i < td.Q.Length; i++) { float h = td.MinH + td.Q[i] / 65535f * (td.MaxH - td.MinH); nd.Q[i] = (ushort)Math.Round((h - nd.MinH) / TerrainTileData.Step); }
                    tc.Chunks["TERR"] = nd.ToBytes(); tc.Chunks["TERC"] = nd.Downsample((nd.Resolution - 1) / 64).ToBytes();
                }
                var nb = tc.Serialize(); after += nb.Length; System.IO.File.WriteAllBytes(System.IO.Path.Combine(args[1], LooseTileSource.FileName(t)), nb);
            }
            Console.WriteLine($"recompressed: {before / 1024} kB → {after / 1024} kB");
            return 0;
        }
        if (args.Length >= 3 && args[0] == "macro") { int n = MacroTerrainBuilder.Build(new LooseTileSource(args[1]), args[2], Console.WriteLine); Console.WriteLine(n + " macro blocks"); return 0; }
        if (args.Length >= 3 && args[0] == "arterial")
        {
            var srcA = new LooseTileSource(args[1]); var ways = HierarchicalRouter.CollectArterial(srcA, RoadClass.SecondaryLink, (i, n) => { if (i % 500 == 0) Console.WriteLine($"scan {i}/{n}"); });
            HierarchicalRouter.SaveArterial(args[2], ways); Console.WriteLine($"arterial network → {args[2]} ({new System.IO.FileInfo(args[2]).Length / 1024} kB)"); return 0;
        }
        if (args.Length >= 3 && args[0] == "pack")
        {   // region pack for distribution (one file instead of thousands of tiles)
            var srcP = new LooseTileSource(args[1]); var d = new System.Collections.Generic.Dictionary<TileId, byte[]>();
            foreach (var t in srcP.Tiles) { srcP.TryRead(t, out var pb); d[t] = pb; }
            RegionPackSource.Write(args[2], d); Console.WriteLine($"{d.Count} tiles → {args[2]}"); return 0;
        }
        double lat = 46.94658, lon = 7.44410; int radius = 2;
        string outDir = "Assets/StreamingAssets/SwissWorld", cache = ".cache/swiss-import";
        var src = OsmSourceKind.OsmApi; string overpass = null; bool surface = true;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--lat": lat = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--lon": lon = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--radius": radius = int.Parse(args[++i]); break;
                case "--out": outDir = args[++i]; break;
                case "--cache": cache = args[++i]; break;
                case "--no-surface": surface = false; break;
                case "--osm": src = args[++i] == "overpass" ? OsmSourceKind.Overpass : OsmSourceKind.OsmApi; break;
                case "--overpass-url": overpass = args[++i]; break;
                default: Console.Error.WriteLine("Unknown arg " + args[i]); return 2;
            }
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var b = new WorldBuilder(http, Console.WriteLine) { OsmSource = src, UseSurfaceModel = surface };
        if (overpass != null) b.OverpassUrl = overpass;
        var c = SwissCoordinateConverter.ToLv95(lat, lon);
        Console.WriteLine($"Centre {c}, tile {TileGrid.FromLv95(c.E, c.N)}, radius {radius}");
        await b.BuildAsync(c, radius, outDir, cache);
        return 0;
    }
}
