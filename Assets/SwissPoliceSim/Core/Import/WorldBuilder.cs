using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Geometry;
using SwissPoliceSim.Core.Osm;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Storage;
using SwissPoliceSim.Core.Terrain;

namespace SwissPoliceSim.Core.Import
{
    public sealed class BuildStats
    {
        public int Tiles, Buildings, BuildingsMeasured, Trees, RoadWays, WaterPolys, UnclosedRings;
        public long Bytes;
        public override string ToString() =>
            $"{Tiles} tiles, {RoadWays} road segments, {Buildings} buildings ({BuildingsMeasured} with measured height), {Trees} trees, {WaterPolys} water polygons, {UnclosedRings} unclosed rings, {Bytes / 1024.0 / 1024.0:F1} MB";
    }

    /// <summary>
    /// Turns raw source data (swissALTI3D, swissSURFACE3D, OpenStreetMap) of a block of tiles into <c>.sptile</c> containers.
    /// Work is done per block (all tiles of the request are held in memory); a country-wide import runs block by block with a
    /// one-tile overlap (see docs/ARCHITECTURE.md ▸ Skalierung).
    /// </summary>
    public sealed class WorldBuilder
    {
        readonly HttpClient _http; readonly Action<string> _log;
        public OsmSourceKind OsmSource = OsmSourceKind.OsmApi;
        public string OverpassUrl = "https://overpass-api.de/api/interpreter";
        public int TerrainResolution = 257;
        public int RequestDelayMs = 1000;
        public bool UseSurfaceModel = true;

        public WorldBuilder(HttpClient http, Action<string> log)
        {
            _http = http; _log = log ?? (_ => { });
            if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
                _http.DefaultRequestHeaders.Add("User-Agent", "SwissPoliceSimulator-Importer/0.2 (open-source game prototype)");
        }

        public async Task<BuildStats> BuildAsync(Lv95 center, int radius, string outDir, string cacheDir, CancellationToken ct = default)
        {
            var stats = new BuildStats();
            var c = TileGrid.FromLv95(center.E, center.N);
            var tiles = TileGrid.Ring(c, radius);
            var tileSet = new HashSet<TileId>(tiles);
            string tilesDir = Path.Combine(outDir, "tiles");
            Directory.CreateDirectory(tilesDir); Directory.CreateDirectory(cacheDir);

            _log("== swissALTI3D (terrain)");
            var dem = await LoadDemAsync(tiles, cacheDir, ct);
            SurfaceMosaic surf = new SurfaceMosaic();
            if (UseSurfaceModel) { _log("== swissSURFACE3D (roofs, trees)"); surf = await LoadSurfaceAsync(tiles, cacheDir, ct); }
            _log("== OpenStreetMap");
            var ds = await LoadOsmAsync(tiles, cacheDir, ct);
            _log($"OSM: {ds.Nodes.Count} nodes, {ds.Ways.Count} ways, {ds.Relations.Count} relations");
            var feat = OsmFeatureExtractor.Extract(ds);
            stats.UnclosedRings = feat.UnclosedRings;

            double minE = (c.X - radius) * 1000.0, minN = (c.Y - radius) * 1000.0, maxE = (c.X + radius + 1) * 1000.0, maxN = (c.Y + radius + 1) * 1000.0;
            _log("== analysis");
            var mask = new BuiltUpMask(minE - 500, minN - 500, maxE + 500, maxN + 500, feat);

            // roads: heights on the full way, speed limits (splitting at built-up borders), then tile split
            var perTileRoads = new Dictionary<TileId, List<RoadWay>>();
            foreach (var w in feat.Roads)
            {
                RoadElevation.Assign(w, dem);
                foreach (var part in SpeedLimitResolver.ResolveAndSplit(w, mask)) { RoadTileFile.Split(part, perTileRoads); stats.RoadWays++; }
            }

            // terrain used for the tiles: water carved into the ground and the ground flattened across roads
            var carved = new WaterModel.CarvedHeightSource(new RoadCorridorHeightSource(dem, AllParts(perTileRoads)), feat.Water);

            // buildings
            var perTileB = new Dictionary<TileId, List<BuildingFeature>>();
            var deduped = BuildingDeduper.Run(feat.Buildings);
            foreach (var b in deduped)
            {
                var cen = PolygonUtil.Centroid(b.Footprint.Outer);
                var t = TileGrid.FromLv95(cen.X, cen.Y);
                if (!tileSet.Contains(t)) continue;
                BuildingHeightEstimator.Estimate(b, dem, surf);
                if (float.IsNaN(b.GroundHeight)) continue;
                if (!perTileB.TryGetValue(t, out var l)) perTileB[t] = l = new List<BuildingFeature>();
                l.Add(b); stats.Buildings++; if (b.HeightMeasured) stats.BuildingsMeasured++;
            }
            _log($"buildings: {stats.Buildings} ({stats.BuildingsMeasured} measured)");

            // rails with heights
            foreach (var r in feat.Rails)
            {
                r.H = new float[r.E.Length];
                for (int i = 0; i < r.E.Length; i++) { dem.TrySample(r.E[i], r.N[i], out float h); r.H[i] = h; }
            }

            int done = 0;
            foreach (var t in tiles)
            {
                ct.ThrowIfCancellationRequested();
                done++;
                var cont = new TileContainer { Tile = t };
                double te = TileGrid.MinE(t), tn = TileGrid.MinN(t);

                if (dem.Has(t))
                {
                    // adaptive resolution: tiles without roads or buildings (alpine, forest) need only 7.8 m spacing; settled tiles get 3.9 m
                    bool settled = (perTileRoads.TryGetValue(t, out var rr0) && rr0.Count > 0) || (perTileB.TryGetValue(t, out var bb0) && bb0.Count > 0);
                    int res = settled ? TerrainResolution : (TerrainResolution - 1) / 2 + 1;
                    var td = TerrainTileData.Bake(carved, t, res);
                    cont.Chunks["TERR"] = td.ToBytes();
                    cont.Chunks["TERC"] = td.Downsample((res - 1) / 64).ToBytes();       // 65×65 coarse level for distant tiles
                }
                perTileRoads.TryGetValue(t, out var roads);
                using (var ms = new MemoryStream()) { RoadTileFile.Write(ms, t, roads ?? new List<RoadWay>()); cont.Chunks["ROAD"] = ms.ToArray(); }

                perTileB.TryGetValue(t, out var bl);
                cont.Chunks["BLDG"] = TileCodec.WriteBuildings(bl ?? new List<BuildingFeature>(), t);

                var wat = new List<WaterFeature>(); var wws = new List<WaterwayFeature>();
                foreach (var w in feat.Water)
                {
                    w.Polygon.Bounds(out double x0, out double y0, out double x1, out double y1);
                    if (x1 < te - 10 || x0 > te + 1010 || y1 < tn - 10 || y0 > tn + 1010) continue;
                    var clipped = new WaterFeature { OsmId = w.OsmId, IsRiver = w.IsRiver, SurfaceHeight = w.SurfaceHeight };
                    clipped.Polygon.Outer = PolygonClip.ClipToRect(w.Polygon.Outer, te - 12, tn - 12, te + 1012, tn + 1012);
                    if (clipped.Polygon.Outer.Count < 3) continue;
                    foreach (var h in w.Polygon.Holes) clipped.Polygon.Holes.Add(h);
                    clipped.Polygon.Normalize();
                    if (clipped.Polygon.Area() < 4) continue;
                    wat.Add(clipped); stats.WaterPolys++;
                }
                foreach (var q in feat.Waterways)
                {
                    if (q.Tunnel) continue;
                    var mid = TileGrid.FromLv95(q.E[q.E.Length / 2], q.N[q.N.Length / 2]);
                    if (mid == t) wws.Add(q);
                }
                cont.Chunks["WATR"] = TileCodec.WriteWater(wat, wws, t);
                var wmesh = new Meshing.MeshData(1);
                foreach (var wp in wat) WaterModel.BuildMesh(wp, dem, te, tn, wmesh);
                foreach (var wq in wws) WaterModel.BuildWaterwayMesh(wq, dem, te, tn, wmesh);
                cont.Chunks["WMSH"] = Meshing.MeshDataIO.Write(wmesh);

                var nearB = new List<BuildingFeature>();
                foreach (var kv in perTileB) if (TileGrid.Chebyshev(kv.Key, t) <= 1) nearB.AddRange(kv.Value);
                var trees = surf.Has(t) ? TreeDetector.Detect(t, dem, surf, nearB) : new List<TreeInstance>();
                stats.Trees += trees.Count;
                cont.Chunks["TREE"] = TileCodec.WriteTrees(trees, t);

                var lu = new List<LandUseFeature>(); foreach (var f in feat.LandUse) lu.Add(f);
                cont.Chunks["LAND"] = LandUseRaster.Build(t, lu, wat, bl ?? new List<BuildingFeature>());

                var pts = new List<PointFeature>();
                foreach (var p in feat.Points) if (p.Kind != PointKind.Tree && TileGrid.FromLv95(p.E, p.N) == t) pts.Add(p);
                cont.Chunks["POIN"] = TileCodec.WritePoints(pts, t);

                var rails = new List<RailFeature>();
                foreach (var r in feat.Rails) if (TileGrid.FromLv95(r.E[r.E.Length / 2], r.N[r.N.Length / 2]) == t) rails.Add(r);
                cont.Chunks["RAIL"] = TileCodec.WriteRails(rails, t);

                var bytes = cont.Serialize();
                File.WriteAllBytes(Path.Combine(tilesDir, LooseTileSource.FileName(t)), bytes);
                stats.Bytes += bytes.Length; stats.Tiles++;
                _log($"[{done}/{tiles.Count}] tile {t}: {bytes.Length / 1024} kB (roads {(roads?.Count ?? 0)}, buildings {(bl?.Count ?? 0)}, trees {trees.Count}, water {wat.Count})");
            }

            var man = new WorldManifest
            {
                centerE = center.E, centerN = center.N, terrainResolution = TerrainResolution,
                minTileX = c.X - radius, maxTileX = c.X + radius, minTileY = c.Y - radius, maxTileY = c.Y + radius,
                createdUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
            File.WriteAllText(Path.Combine(outDir, "manifest.json"), man.ToJson(), new UTF8Encoding(false));
            _log("done: " + stats);
            return stats;
        }

        static IEnumerable<RoadWay> AllParts(Dictionary<TileId, List<RoadWay>> d) { foreach (var kv in d) foreach (var w in kv.Value) yield return w; }

        // ---------------------------------------------------------------- sources

        async Task<HeightMosaic> LoadDemAsync(List<TileId> tiles, string cache, CancellationToken ct)
        {
            var stac = new StacClient(_http); var mosaic = new HeightMosaic(2.0); int i = 0;
            foreach (var t in tiles)
            {
                ct.ThrowIfCancellationRequested(); i++;
                string zip = Path.Combine(cache, $"alti3d_{t}_2.xyz.zip");
                if (!File.Exists(zip))
                {
                    var a = await stac.FindAssetAsync("ch.swisstopo.swissalti3d", t, "2");
                    if (a == null) { _log($"[{i}/{tiles.Count}] {t}: no swissALTI3D"); continue; }
                    _log($"[{i}/{tiles.Count}] {t}: downloading {a.ItemId}");
                    File.WriteAllBytes(zip, await _http.GetByteArrayAsync(a.Href));
                }
                using (var za = ZipFile.OpenRead(zip))
                    foreach (var e in za.Entries)
                        if (e.Name.EndsWith(".xyz", StringComparison.OrdinalIgnoreCase))
                            using (var s = e.Open()) mosaic.Add(XyzHeightGridReader.Read(s, t, 2.0));
            }
            return mosaic;
        }

        async Task<SurfaceMosaic> LoadSurfaceAsync(List<TileId> tiles, string cache, CancellationToken ct)
        {
            var stac = new StacClient(_http); var m = new SurfaceMosaic(); int i = 0;
            foreach (var t in tiles)
            {
                ct.ThrowIfCancellationRequested(); i++;
                string zip = Path.Combine(cache, $"surface3d_{t}_0.5.xyz.zip");
                if (!File.Exists(zip))
                {
                    var a = await stac.FindAssetAsync("ch.swisstopo.swisssurface3d-raster", t, "0.5");
                    if (a == null) { _log($"[{i}/{tiles.Count}] {t}: no swissSURFACE3D"); continue; }
                    _log($"[{i}/{tiles.Count}] {t}: downloading {a.ItemId} (~18 MB)");
                    File.WriteAllBytes(zip, await _http.GetByteArrayAsync(a.Href));
                }
                using (var za = ZipFile.OpenRead(zip))
                    foreach (var e in za.Entries)
                        if (e.Name.EndsWith(".xyz", StringComparison.OrdinalIgnoreCase))
                            using (var s = e.Open()) m.Add(SurfaceGrid.ReadXyz(s, t));
                _log($"[{i}/{tiles.Count}] {t}: surface parsed");
            }
            return m;
        }

        async Task<OsmDataset> LoadOsmAsync(List<TileId> tiles, string cache, CancellationToken ct)
        {
            var ds = new OsmDataset(); int i = 0;
            foreach (var t in tiles)
            {
                ct.ThrowIfCancellationRequested(); i++;
                string file = Path.Combine(cache, $"osm_{OsmSource}_{t}.osm.xml");
                if (!File.Exists(file))
                {
                    var sw = SwissCoordinateConverter.ToWgs84(new Lv95(TileGrid.MinE(t) - 20, TileGrid.MinN(t) - 20));
                    var ne = SwissCoordinateConverter.ToWgs84(new Lv95(TileGrid.MinE(t) + 1020, TileGrid.MinN(t) + 1020));
                    var ci = CultureInfo.InvariantCulture;
                    string s = sw.Lat.ToString("F6", ci), w = sw.Lon.ToString("F6", ci), n = ne.Lat.ToString("F6", ci), e = ne.Lon.ToString("F6", ci);
                    HttpResponseMessage resp;
                    if (OsmSource == OsmSourceKind.OsmApi) resp = await _http.GetAsync($"https://api.openstreetmap.org/api/0.6/map?bbox={w},{s},{e},{n}", ct);
                    else
                    {
                        string q = $"[out:xml][timeout:120];(way({s},{w},{n},{e});node(w);relation({s},{w},{n},{e}););out body;>;out skel qt;";
                        resp = await _http.PostAsync(OverpassUrl, new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("data", q) }), ct);
                    }
                    resp.EnsureSuccessStatusCode();
                    File.WriteAllBytes(file, await resp.Content.ReadAsByteArrayAsync());
                    _log($"[{i}/{tiles.Count}] {t}: OSM downloaded");
                    await Task.Delay(RequestDelayMs, ct);
                }
                using (var fs = File.OpenRead(file)) ds.Add(fs);
            }
            return ds;
        }
    }
}
