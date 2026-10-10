using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace SwissPoliceSim.Core.Osm
{
    public sealed class OsmNode { public long Id; public double Lat, Lon; public Dictionary<string, string> Tags; }
    public sealed class OsmWay { public long Id; public List<long> Nodes = new List<long>(); public Dictionary<string, string> Tags = new Dictionary<string, string>(); }
    public sealed class OsmMember { public char Type; public long Ref; public string Role = ""; }
    public sealed class OsmRelation { public long Id; public List<OsmMember> Members = new List<OsmMember>(); public Dictionary<string, string> Tags = new Dictionary<string, string>(); }

    /// <summary>In-memory OSM extract (nodes, ways, relations). Several downloads can be merged. © OpenStreetMap contributors, ODbL.</summary>
    public sealed class OsmDataset
    {
        public readonly Dictionary<long, OsmNode> Nodes = new Dictionary<long, OsmNode>();
        public readonly Dictionary<long, OsmWay> Ways = new Dictionary<long, OsmWay>();
        public readonly Dictionary<long, OsmRelation> Relations = new Dictionary<long, OsmRelation>();

        public static OsmDataset Parse(Stream xml) { var d = new OsmDataset(); d.Add(xml); return d; }

        public void Add(Stream xml)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using (var r = XmlReader.Create(xml, settings))
            {
                var inv = CultureInfo.InvariantCulture;
                while (r.Read())
                {
                    if (r.NodeType != XmlNodeType.Element) continue;
                    switch (r.Name)
                    {
                        case "node":
                        {
                            var n = new OsmNode { Id = long.Parse(r.GetAttribute("id"), inv) };
                            string lat = r.GetAttribute("lat"), lon = r.GetAttribute("lon");
                            if (lat == null || lon == null) { SkipChildren(r); break; }
                            n.Lat = double.Parse(lat, inv); n.Lon = double.Parse(lon, inv);
                            if (!r.IsEmptyElement) n.Tags = ReadChildren(r, null, null);
                            Nodes[n.Id] = n;
                            break;
                        }
                        case "way":
                        {
                            var w = new OsmWay { Id = long.Parse(r.GetAttribute("id"), inv) };
                            if (!r.IsEmptyElement) w.Tags = ReadChildren(r, w.Nodes, null) ?? w.Tags;
                            Ways[w.Id] = w;
                            break;
                        }
                        case "relation":
                        {
                            var rel = new OsmRelation { Id = long.Parse(r.GetAttribute("id"), inv) };
                            if (!r.IsEmptyElement) rel.Tags = ReadChildren(r, null, rel.Members) ?? rel.Tags;
                            Relations[rel.Id] = rel;
                            break;
                        }
                    }
                }
            }
        }

        static void SkipChildren(XmlReader r) { if (!r.IsEmptyElement) r.Skip(); }

        /// <summary>Reads &lt;tag&gt;/&lt;nd&gt;/&lt;member&gt; children of the current element; returns the tags (or null if none).</summary>
        static Dictionary<string, string> ReadChildren(XmlReader r, List<long> nodes, List<OsmMember> members)
        {
            Dictionary<string, string> tags = null;
            int depth = r.Depth;
            var inv = CultureInfo.InvariantCulture;
            while (r.Read() && r.Depth > depth)
            {
                if (r.NodeType != XmlNodeType.Element) continue;
                if (r.Name == "tag") { if (tags == null) tags = new Dictionary<string, string>(); tags[r.GetAttribute("k")] = r.GetAttribute("v"); }
                else if (r.Name == "nd" && nodes != null) nodes.Add(long.Parse(r.GetAttribute("ref"), inv));
                else if (r.Name == "member" && members != null)
                    members.Add(new OsmMember { Type = r.GetAttribute("type")[0], Ref = long.Parse(r.GetAttribute("ref"), inv), Role = r.GetAttribute("role") ?? "" });
            }
            return tags;
        }

        public static string Tag(Dictionary<string, string> t, string key) =>
            t != null && t.TryGetValue(key, out var v) ? v : null;
    }
}
