using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Util;

namespace SwissPoliceSim.Core.Import
{
    /// <summary>Client for the swisstopo STAC API (data.geo.admin.ch). Used to find the newest swissALTI3D file per tile.</summary>
    public sealed class StacClient
    {
        public const string BaseUrl = "https://data.geo.admin.ch/api/stac/v0.9";
        readonly HttpClient _http;

        public StacClient(HttpClient http) { _http = http; }

        public sealed class AssetRef { public string ItemId; public int Year; public string Href; }

        /// <summary>Finds the newest XYZ.zip asset of the given resolution (0.5 or 2 m) for a 1 km tile.</summary>
        public Task<AssetRef> FindHeightAssetAsync(TileId tile, string cellSizeToken = "2") => FindAssetAsync("ch.swisstopo.swissalti3d", tile, cellSizeToken);

        /// <summary>Newest XYZ.zip asset of any swisstopo 1 km-tile collection (swissALTI3D, swissSURFACE3D Raster, ...).</summary>
        public async Task<AssetRef> FindAssetAsync(string collection, TileId tile, string cellSizeToken)
        {
            var c = SwissCoordinateConverter.ToWgs84(new Lv95(TileGrid.MinE(tile) + 500, TileGrid.MinN(tile) + 500));
            string bbox = string.Format(CultureInfo.InvariantCulture, "{0:F6},{1:F6},{2:F6},{3:F6}",
                c.Lon - 0.0001, c.Lat - 0.0001, c.Lon + 0.0001, c.Lat + 0.0001);
            string url = $"{BaseUrl}/collections/{collection}/items?bbox={bbox}&limit=100";
            string json = await _http.GetStringAsync(url).ConfigureAwait(false);
            return PickNewest(MiniJson.Parse(json), tile, cellSizeToken);
        }

        /// <summary>Pure function (unit-tested): choose newest-year item matching the tile from a STAC FeatureCollection.</summary>
        public static AssetRef PickNewest(object featureCollection, TileId tile, string cellSizeToken)
        {
            string suffix = $"_{tile.X}-{tile.Y}";
            AssetRef best = null;
            var root = (Dictionary<string, object>)featureCollection;
            foreach (var f in (List<object>)root["features"])
            {
                var item = (Dictionary<string, object>)f;
                string id = (string)item["id"];
                if (!id.EndsWith(suffix, StringComparison.Ordinal)) continue;
                var parts = id.Split('_');
                if (parts.Length < 3 || !int.TryParse(parts[1], out int year)) continue;
                if (best != null && year <= best.Year) continue;
                foreach (var kv in (Dictionary<string, object>)item["assets"])
                {
                    string key = kv.Key;
                    if (!key.EndsWith(".xyz.zip", StringComparison.Ordinal)) continue;
                    if (key.IndexOf($"_{cellSizeToken}_2056_", StringComparison.Ordinal) < 0) continue;
                    var a = (Dictionary<string, object>)kv.Value;
                    best = new AssetRef { ItemId = id, Year = year, Href = (string)a["href"] };
                }
            }
            return best;
        }
    }
}
