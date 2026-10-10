using System.Collections.Generic;
using System.IO;
using SwissPoliceSim.Core.Osm;

namespace SwissPoliceSim.Core.Roads
{
    /// <summary>Convenience wrapper: parse OSM XML and return only the drivable roads (LV95). © OpenStreetMap contributors, ODbL.</summary>
    public static class OsmXmlRoadParser
    {
        public static List<RoadWay> Parse(Stream xml) => OsmFeatureExtractor.Extract(OsmDataset.Parse(xml)).Roads;
    }
}
