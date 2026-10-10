using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using SwissPoliceSim.Core.Geo;

namespace SwissPoliceSim.Core.Roads
{
    /// <summary>
    /// Parses OSM XML (OSM API 0.6 "map" call or Overpass [out:xml] with node recursion) into drivable RoadWays
    /// projected to LV95. Data: © OpenStreetMap contributors, ODbL.
    /// </summary>
    public static class OsmXmlRoadParser
    {
        public static List<RoadWay> Parse(Stream xml)
        {
            var nodes = new Dictionary<long, Wgs84>();
            var ways = new List<RoadWay>();
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (var r = XmlReader.Create(xml, settings))
            {
                while (r.Read())
                {
                    if (r.NodeType != XmlNodeType.Element) continue;
                    if (r.Name == "node")
                    {
                        long id = long.Parse(r.GetAttribute("id"), CultureInfo.InvariantCulture);
                        string lat = r.GetAttribute("lat"), lon = r.GetAttribute("lon");
                        if (lat != null && lon != null)
                            nodes[id] = new Wgs84(double.Parse(lat, CultureInfo.InvariantCulture),
                                                  double.Parse(lon, CultureInfo.InvariantCulture));
                    }
                    else if (r.Name == "way")
                    {
                        var w = ReadWay(r, nodes);
                        if (w != null) ways.Add(w);
                    }
                }
            }
            return ways;
        }

        static RoadWay ReadWay(XmlReader r, Dictionary<long, Wgs84> nodes)
        {
            long id = long.Parse(r.GetAttribute("id"), CultureInfo.InvariantCulture);
            var refs = new List<long>();
            var tags = new Dictionary<string, string>();
            if (!r.IsEmptyElement)
            {
                int depth = r.Depth;
                while (r.Read() && r.Depth > depth)
                {
                    if (r.NodeType != XmlNodeType.Element) continue;
                    if (r.Name == "nd") refs.Add(long.Parse(r.GetAttribute("ref"), CultureInfo.InvariantCulture));
                    else if (r.Name == "tag") tags[r.GetAttribute("k")] = r.GetAttribute("v");
                }
            }
            if (!tags.TryGetValue("highway", out string hw) || !RoadTags.TryParseClass(hw, out RoadClass cls)) return null;
            if (tags.TryGetValue("access", out string acc) && (acc == "no" || acc == "private")) return null;
            if (tags.TryGetValue("area", out string area) && area == "yes") return null;

            var ids = new List<long>(); var es = new List<double>(); var ns = new List<double>();
            foreach (long nid in refs)
            {
                if (!nodes.TryGetValue(nid, out Wgs84 g)) continue; // node outside download area
                var p = SwissCoordinateConverter.ToLv95(g);
                ids.Add(nid); es.Add(p.E); ns.Add(p.N);
            }
            if (ids.Count < 2) return null;

            tags.TryGetValue("junction", out string junction);
            tags.TryGetValue("oneway", out string oneway);
            tags.TryGetValue("maxspeed", out string maxspeed);
            tags.TryGetValue("lanes", out string lanesStr);
            tags.TryGetValue("name", out string name);
            tags.TryGetValue("ref", out string rf);
            tags.TryGetValue("bridge", out string bridge);
            tags.TryGetValue("tunnel", out string tunnel);
            int.TryParse(lanesStr, out int lanes);

            return new RoadWay
            {
                OsmId = id, Class = cls,
                OneWay = RoadTags.ParseOneWay(oneway, junction, cls),
                Roundabout = junction == "roundabout",
                Bridge = !string.IsNullOrEmpty(bridge) && bridge != "no",
                Tunnel = !string.IsNullOrEmpty(tunnel) && tunnel != "no",
                MaxSpeedKmh = RoadTags.ParseMaxSpeed(maxspeed),
                Lanes = lanes, Name = name ?? "", Ref = rf ?? "",
                NodeIds = ids.ToArray(), E = es.ToArray(), N = ns.ToArray()
            };
        }
    }
}
