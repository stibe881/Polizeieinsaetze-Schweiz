using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Import;

// Usage: SwissWorldBaker --lat 46.94658 --lon 7.44410 --radius 2 --out <dir> [--cache <dir>] [--osm api|overpass] [--overpass-url <url>] [--no-surface]
static class Program
{
    static async Task<int> Main(string[] args)
    {
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
