using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Import;

// Usage: SwissWorldBaker --lat 46.94658 --lon 7.44410 --radius 2 --out <dir> [--cache <dir>] [--no-terrain] [--no-roads]
//                        [--osm api|overpass] [--overpass-url <url>]
static class Program
{
    static async Task<int> Main(string[] args)
    {
        double lat = 46.94658, lon = 7.44410; int radius = 2;
        string outDir = "Assets/StreamingAssets/SwissWorld", cache = ".cache/swiss-import";
        bool terrain = true, roads = true; var src = OsmSourceKind.OsmApi; string overpass = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--lat": lat = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--lon": lon = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--radius": radius = int.Parse(args[++i]); break;
                case "--out": outDir = args[++i]; break;
                case "--cache": cache = args[++i]; break;
                case "--no-terrain": terrain = false; break;
                case "--no-roads": roads = false; break;
                case "--osm": src = args[++i] == "overpass" ? OsmSourceKind.Overpass : OsmSourceKind.OsmApi; break;
                case "--overpass-url": overpass = args[++i]; break;
                default: Console.Error.WriteLine("Unknown arg " + args[i]); return 2;
            }
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var imp = new SwissWorldImporter(http, Console.WriteLine) { OsmSource = src };
        if (overpass != null) imp.OverpassUrl = overpass;
        var c = SwissCoordinateConverter.ToLv95(lat, lon);
        Console.WriteLine($"Centre {c}, tile {TileGrid.FromLv95(c.E, c.N)}, radius {radius}");
        await imp.ImportAsync(c, radius, outDir, cache, terrain, roads);
        return 0;
    }
}
