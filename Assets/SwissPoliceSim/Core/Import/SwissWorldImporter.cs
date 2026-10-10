using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Terrain;

namespace SwissPoliceSim.Core.Import
{
    public enum OsmSourceKind { OsmApi, Overpass }

    /// <summary>Written next to the baked tiles so the runtime knows where the pilot area is and where data came from.</summary>
    [Serializable]
    public sealed class WorldManifest
    {
        public string name = "Pilot";
        public double centerE, centerN;
        public int terrainResolution = 257;
        public int minTileX, maxTileX, minTileY, maxTileY;
        public string createdUtc = "";
        public string attribution =
            "Höhendaten: swissALTI3D © swisstopo. Strassen: © OpenStreetMap contributors (ODbL).";

        public string ToJson() => JsonUtilityLike.ToJson(this);
    }

    /// <summary>Tiny hand-rolled serializer (Core has no JSON package); output is valid JSON readable by Unity's JsonUtility.</summary>
    static class JsonUtilityLike
    {
        public static string ToJson(WorldManifest m)
        {
            var c = CultureInfo.InvariantCulture;
            return "{\n" +
                   $"  \"name\": \"{m.name}\",\n" +
                   $"  \"centerE\": {m.centerE.ToString("F1", c)},\n" +
                   $"  \"centerN\": {m.centerN.ToString("F1", c)},\n" +
                   $"  \"terrainResolution\": {m.terrainResolution},\n" +
                   $"  \"minTileX\": {m.minTileX}, \"maxTileX\": {m.maxTileX},\n" +
                   $"  \"minTileY\": {m.minTileY}, \"maxTileY\": {m.maxTileY},\n" +
                   $"  \"createdUtc\": \"{m.createdUtc}\",\n" +
                   $"  \"attribution\": \"{m.attribution.Replace("\"", "\\\"")}\"\n" +
                   "}\n";
        }
    }

    /// <summary>
    /// Phase-1 import pipeline: swisstopo swissALTI3D (terrain) + OpenStreetMap (roads) -> baked 1 km tile files.
    /// Output layout: {outDir}/terrain/{X}-{Y}.spt, {outDir}/roads/{X}-{Y}.spr, {outDir}/manifest.json.
    /// Same code runs from the Unity editor window and from Tools/SwissWorldBaker (command line).
    /// </summary>
    public sealed class SwissWorldImporter
    {
        readonly HttpClient _http;
        readonly Action<string> _log;

        public OsmSourceKind OsmSource = OsmSourceKind.OsmApi;
        public string OverpassUrl = "https://overpass-api.de/api/interpreter";
        public int TerrainResolution = 257;
        public string HeightCellToken = "2";   // "2" = 2 m grid, "0.5" = 0.5 m grid (much larger downloads)
        public int RequestDelayMs = 1000;      // be polite to public servers

        public SwissWorldImporter(HttpClient http, Action<string> log)
        {
            _http = http; _log = log ?? (_ => { });
            if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
                _http.DefaultRequestHeaders.Add("User-Agent", "SwissPoliceSimulator-Importer/0.1 (open-source game prototype)");
        }

        public async Task ImportAsync(Lv95 center, int radiusTiles, string outDir, string cacheDir,
                                      bool terrain, bool roads, CancellationToken ct = default)
        {
            var c = TileGrid.FromLv95(center.E, center.N);
            var tiles = TileGrid.Ring(c, radiusTiles);
            Directory.CreateDirectory(Path.Combine(outDir, "terrain"));
            Directory.CreateDirectory(Path.Combine(outDir, "roads"));
            Directory.CreateDirectory(cacheDir);

            // Terrain tiles are also needed to bake road heights, so the mosaic is always loaded.
            var mosaic = await LoadMosaicAsync(tiles, cacheDir, ct);
            if (terrain) BakeTerrain(tiles, mosaic, outDir);
            if (roads) await ImportRoadsAsync(tiles, outDir, cacheDir, mosaic, ct);

            var man = new WorldManifest
            {
                centerE = center.E, centerN = center.N, terrainResolution = TerrainResolution,
                minTileX = c.X - radiusTiles, maxTileX = c.X + radiusTiles,
                minTileY = c.Y - radiusTiles, maxTileY = c.Y + radiusTiles,
                createdUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
            File.WriteAllText(Path.Combine(outDir, "manifest.json"), man.ToJson(), new UTF8Encoding(false));
            _log("Manifest written.");
        }

        async Task<HeightMosaic> LoadMosaicAsync(List<TileId> tiles, string cacheDir, CancellationToken ct)
        {
            var stac = new StacClient(_http);
            double cell = double.Parse(HeightCellToken, CultureInfo.InvariantCulture);
            var mosaic = new HeightMosaic(cell);
            int i = 0;
            foreach (var t in tiles)
            {
                ct.ThrowIfCancellationRequested();
                i++;
                string zip = Path.Combine(cacheDir, $"alti3d_{t}_{HeightCellToken}.xyz.zip");
                if (!File.Exists(zip))
                {
                    var asset = await stac.FindHeightAssetAsync(t, HeightCellToken);
                    if (asset == null) { _log($"[{i}/{tiles.Count}] {t}: no swissALTI3D asset (outside coverage?)"); continue; }
                    _log($"[{i}/{tiles.Count}] {t}: downloading {asset.ItemId}");
                    File.WriteAllBytes(zip, await _http.GetByteArrayAsync(asset.Href));
                }
                else _log($"[{i}/{tiles.Count}] {t}: cached");

                using (var za = ZipFile.OpenRead(zip))
                {
                    ZipArchiveEntry entry = null;
                    foreach (var e in za.Entries) if (e.Name.EndsWith(".xyz", StringComparison.OrdinalIgnoreCase)) entry = e;
                    if (entry == null) throw new InvalidDataException($"No .xyz in {zip}");
                    using (var s = entry.Open()) mosaic.Add(XyzHeightGridReader.Read(s, t, cell));
                }
            }
            return mosaic;
        }

        void BakeTerrain(List<TileId> tiles, HeightMosaic mosaic, string outDir)
        {
            foreach (var t in tiles)
            {
                if (!mosaic.Has(t)) continue;
                var data = TerrainTileData.Bake(mosaic, t, TerrainResolution);
                using (var f = File.Create(Path.Combine(outDir, "terrain", $"{t}.spt"))) data.Write(f);
                _log($"Terrain {t}: {data.MinH:F1}..{data.MaxH:F1} m");
            }
        }

        async Task ImportRoadsAsync(List<TileId> tiles, string outDir, string cacheDir, HeightMosaic dem, CancellationToken ct)
        {
            var seen = new HashSet<long>();
            var perTile = new Dictionary<TileId, List<RoadWay>>();
            int i = 0;
            foreach (var t in tiles)
            {
                ct.ThrowIfCancellationRequested();
                i++;
                var ways = await FetchWaysAsync(t, cacheDir);
                int added = 0;
                foreach (var w in ways)
                {
                    if (!seen.Add(w.OsmId)) continue;
                    RoadElevation.Assign(w, dem);
                    RoadTileFile.Split(w, perTile);
                    added++;
                }
                _log($"[{i}/{tiles.Count}] roads {t}: {ways.Count} ways, {added} new");
            }
            foreach (var kv in perTile)
                using (var f = File.Create(Path.Combine(outDir, "roads", $"{kv.Key}.spr")))
                    RoadTileFile.Write(f, kv.Key, kv.Value);
            _log($"Road tiles written: {perTile.Count}");
        }

        async Task<List<RoadWay>> FetchWaysAsync(TileId t, string cacheDir)
        {
            string cacheFile = Path.Combine(cacheDir, $"osm_{OsmSource}_{t}.osm.xml");
            if (File.Exists(cacheFile))
                using (var cs = File.OpenRead(cacheFile)) return OsmXmlRoadParser.Parse(cs);

            var sw = SwissCoordinateConverter.ToWgs84(new Lv95(TileGrid.MinE(t) - 20, TileGrid.MinN(t) - 20));
            var ne = SwissCoordinateConverter.ToWgs84(new Lv95(TileGrid.MinE(t) + 1020, TileGrid.MinN(t) + 1020));
            var c = CultureInfo.InvariantCulture;
            string s = sw.Lat.ToString("F6", c), w = sw.Lon.ToString("F6", c), n = ne.Lat.ToString("F6", c), e = ne.Lon.ToString("F6", c);

            HttpResponseMessage resp;
            if (OsmSource == OsmSourceKind.OsmApi)
            {
                resp = await _http.GetAsync($"https://api.openstreetmap.org/api/0.6/map?bbox={w},{s},{e},{n}");
            }
            else
            {
                string q = $"[out:xml][timeout:60];way[\"highway\"]({s},{w},{n},{e});(._;>;);out body;";
                resp = await _http.PostAsync(OverpassUrl, new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", q) }));
            }
            resp.EnsureSuccessStatusCode();
            File.WriteAllBytes(cacheFile, await resp.Content.ReadAsByteArrayAsync());
            await Task.Delay(RequestDelayMs); // politeness delay only for real network requests
            using (var stream = File.OpenRead(cacheFile))
                return OsmXmlRoadParser.Parse(stream);
        }
    }
}
