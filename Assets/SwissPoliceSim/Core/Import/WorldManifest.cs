using System;
using System.Globalization;

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
}
