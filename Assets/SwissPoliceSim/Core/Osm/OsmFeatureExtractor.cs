using System;
using System.Collections.Generic;
using System.Globalization;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Geometry;
using SwissPoliceSim.Core.Roads;

namespace SwissPoliceSim.Core.Osm
{
    /// <summary>Turns a raw OSM dataset into typed, LV95-projected features (roads, buildings, water, land use, points, rails).</summary>
    public static class OsmFeatureExtractor
    {
        public static ExtractedFeatures Extract(OsmDataset ds)
        {
            var res = new ExtractedFeatures();
            var lv = new Dictionary<long, Vec2d>(ds.Nodes.Count);
            foreach (var n in ds.Nodes.Values)
            {
                var p = SwissCoordinateConverter.ToLv95(n.Lat, n.Lon);
                lv[n.Id] = new Vec2d(p.E, p.N);
            }

            var relationOuterWays = new HashSet<long>();   // ways that are only parts of a multipolygon relation
            foreach (var rel in ds.Relations.Values)
                if (Tag(rel.Tags, "type") == "multipolygon" || Tag(rel.Tags, "type") == "boundary")
                    foreach (var m in rel.Members) if (m.Type == 'w') relationOuterWays.Add(m.Ref);

            foreach (var w in ds.Ways.Values)
            {
                var t = w.Tags;
                if (t.Count == 0) continue;
                if (t.ContainsKey("highway")) { var r = ExtractRoad(w, lv); if (r != null) res.Roads.Add(r); }
                if (t.ContainsKey("railway")) ExtractRail(w, lv, res);
                if (t.ContainsKey("waterway")) ExtractWaterway(w, lv, res);
                if (Tag(t, "natural") == "tree_row") ExtractTreeRow(w, lv, res);

                bool closed = w.Nodes.Count >= 4 && w.Nodes[0] == w.Nodes[w.Nodes.Count - 1];
                if (!closed) continue;
                if (IsPolygonFeature(t, out _))
                {
                    var ring = RingToPoints(w.Nodes, lv);
                    if (ring == null) continue;
                    var poly = new PolygonWithHoles { Outer = ring }; poly.Normalize();
                    if (poly.Outer.Count < 3 || poly.Area() < 0.5) continue;
                    EmitPolygon(w.Id, t, poly, res);
                }
            }

            foreach (var rel in ds.Relations.Values)
            {
                if (Tag(rel.Tags, "type") != "multipolygon" || !IsPolygonFeature(rel.Tags, out _)) continue;
                foreach (var poly in AssembleMultipolygon(rel, ds, lv, res))
                    EmitPolygon(rel.Id, rel.Tags, poly, res);
            }

            foreach (var n in ds.Nodes.Values)
                if (n.Tags != null) ExtractPoint(n, lv[n.Id], res);

            return res;
        }

        static string Tag(Dictionary<string, string> t, string k) => OsmDataset.Tag(t, k);

        // ------------------------------------------------------------------ roads

        public static RoadWay ExtractRoad(OsmWay w, Dictionary<long, Vec2d> lv)
        {
            var t = w.Tags;
            if (!RoadTags.TryParseClass(Tag(t, "highway"), out RoadClass cls)) return null;
            string acc = Tag(t, "access");
            if (acc == "no" || acc == "private") return null;
            if (Tag(t, "area") == "yes") return null;
            if (cls == RoadClass.Footway && (Tag(t, "footway") == "sidewalk" || Tag(t, "footway") == "crossing" || Tag(t, "footway") == "traffic_island")) return null;   // sidewalks are generated along roads
            if ((cls == RoadClass.Footway || cls == RoadClass.Path || cls == RoadClass.Cycleway || cls == RoadClass.Pedestrian) && (Tag(t, "indoor") != null || Tag(t, "level") != null && Tag(t, "level") != "0" && Tag(t, "bridge") == null)) return null;
            var ids = new List<long>(); var es = new List<double>(); var ns = new List<double>();
            foreach (long nid in w.Nodes)
            {
                if (!lv.TryGetValue(nid, out var p)) continue;
                ids.Add(nid); es.Add(p.X); ns.Add(p.Y);
            }
            if (ids.Count < 2) return null;

            string junction = Tag(t, "junction"), bridge = Tag(t, "bridge"), tunnel = Tag(t, "tunnel");
            int.TryParse(Tag(t, "lanes"), out int lanes);
            var road = new RoadWay
            {
                OsmId = w.Id, Class = cls,
                OneWay = RoadTags.ParseOneWay(Tag(t, "oneway"), junction, cls),
                Roundabout = junction == "roundabout",
                Bridge = !string.IsNullOrEmpty(bridge) && bridge != "no",
                Tunnel = !string.IsNullOrEmpty(tunnel) && tunnel != "no",
                MaxSpeedKmh = RoadTags.ParseMaxSpeed(Tag(t, "maxspeed")),
                Lanes = lanes, Name = Tag(t, "name") ?? "", Ref = Tag(t, "ref") ?? "",
                Motorroad = Tag(t, "motorroad") == "yes",
                ZoneSpeedKmh = RoadTags.ParseMaxSpeed(Tag(t, "zone:maxspeed")),
                NodeIds = ids.ToArray(), E = es.ToArray(), N = ns.ToArray()
            };
            string mt = Tag(t, "maxspeed:type") ?? Tag(t, "source:maxspeed");
            if (mt == "CH:urban") road.UrbanHint = 1;
            else if (mt == "CH:rural") road.UrbanHint = 0;
            string zone = Tag(t, "zone:maxspeed") ?? Tag(t, "maxspeed:zone");
            if (road.ZoneSpeedKmh == 0 && zone != null && zone.StartsWith("CH:", StringComparison.Ordinal))
                int.TryParse(zone.Substring(3), out road.ZoneSpeedKmh);
            return road;
        }

        static void ExtractRail(OsmWay w, Dictionary<long, Vec2d> lv, ExtractedFeatures res)
        {
            string r = Tag(w.Tags, "railway");
            if (r != "rail" && r != "light_rail" && r != "tram" && r != "narrow_gauge" && r != "funicular") return;
            string svc = Tag(w.Tags, "service");
            if (svc != null && (svc == "yard" || svc == "siding" || svc == "spur")) { /* keep: depot tracks are visible */ }
            var line = LinePoints(w.Nodes, lv, out var e, out var n);
            if (!line) return;
            res.Rails.Add(new RailFeature
            {
                OsmId = w.Id, E = e, N = n,
                Tunnel = Tag(w.Tags, "tunnel") != null && Tag(w.Tags, "tunnel") != "no",
                Bridge = Tag(w.Tags, "bridge") != null && Tag(w.Tags, "bridge") != "no"
            });
        }

        static void ExtractWaterway(OsmWay w, Dictionary<long, Vec2d> lv, ExtractedFeatures res)
        {
            string k = Tag(w.Tags, "waterway");
            WaterwayKind kind; float width;
            switch (k)
            {
                case "river": kind = WaterwayKind.River; width = 12f; break;
                case "canal": kind = WaterwayKind.Canal; width = 6f; break;
                case "stream": kind = WaterwayKind.Stream; width = 2f; break;
                case "ditch": kind = WaterwayKind.Ditch; width = 1.2f; break;
                default: return;
            }
            string tun = Tag(w.Tags, "tunnel"), cul = Tag(w.Tags, "culvert");
            bool tunnel = (tun != null && tun != "no") || (cul != null && cul != "no");
            if (float.TryParse(Tag(w.Tags, "width"), NumberStyles.Float, CultureInfo.InvariantCulture, out float wd) && wd > 0.3f) width = wd;
            if (!LinePoints(w.Nodes, lv, out var e, out var n)) return;
            res.Waterways.Add(new WaterwayFeature { OsmId = w.Id, Kind = kind, Width = width, Tunnel = tunnel, E = e, N = n });
        }

        static void ExtractTreeRow(OsmWay w, Dictionary<long, Vec2d> lv, ExtractedFeatures res)
        {
            if (!LinePoints(w.Nodes, lv, out var e, out var n)) return;
            double carry = 0;
            for (int i = 1; i < e.Length; i++)
            {
                double dx = e[i] - e[i - 1], dn = n[i] - n[i - 1], len = Math.Sqrt(dx * dx + dn * dn);
                double pos = carry;
                while (pos < len)
                {
                    double f = pos / len;
                    res.Points.Add(new PointFeature { Kind = PointKind.Tree, E = e[i - 1] + dx * f, N = n[i - 1] + dn * f, OsmId = w.Id });
                    pos += 8.0;
                }
                carry = pos - len;
            }
        }

        static bool LinePoints(List<long> nodes, Dictionary<long, Vec2d> lv, out double[] e, out double[] n)
        {
            var el = new List<double>(); var nl = new List<double>();
            foreach (long id in nodes) if (lv.TryGetValue(id, out var p)) { el.Add(p.X); nl.Add(p.Y); }
            e = el.ToArray(); n = nl.ToArray();
            return e.Length >= 2;
        }

        // ------------------------------------------------------------------ points

        static void ExtractPoint(OsmNode node, Vec2d p, ExtractedFeatures res)
        {
            var t = node.Tags;
            PointKind? kind = null;
            string hw = Tag(t, "highway");
            if (Tag(t, "natural") == "tree") kind = PointKind.Tree;
            else if (hw == "traffic_signals") kind = PointKind.TrafficSignals;
            else if (hw == "crossing" || Tag(t, "crossing") != null && hw == null && Tag(t, "railway") == null) kind = PointKind.Crossing;
            else if (hw == "stop") kind = PointKind.StopSign;
            else if (hw == "give_way") kind = PointKind.GiveWay;
            else if (hw == "bus_stop" || Tag(t, "public_transport") == "platform" && Tag(t, "bus") == "yes") kind = PointKind.BusStop;
            else if (hw == "street_lamp") kind = PointKind.StreetLamp;
            else if (Tag(t, "traffic_calming") != null) kind = PointKind.TrafficCalming;
            else if (Tag(t, "railway") == "level_crossing") kind = PointKind.RailCrossing;
            if (kind == null) return;
            float h = float.NaN;
            float.TryParse(Tag(t, "height"), NumberStyles.Float, CultureInfo.InvariantCulture, out h);
            float dir = 0; float.TryParse(Tag(t, "direction"), NumberStyles.Float, CultureInfo.InvariantCulture, out dir);
            res.Points.Add(new PointFeature { Kind = kind.Value, E = p.X, N = p.Y, Height = h, Direction = dir, OsmId = node.Id });
        }

        // ------------------------------------------------------------------ polygons

        static List<Vec2d> RingToPoints(List<long> ids, Dictionary<long, Vec2d> lv)
        {
            var pts = new List<Vec2d>(ids.Count);
            foreach (long id in ids)
            {
                if (!lv.TryGetValue(id, out var p)) return null;   // ring leaves the downloaded area
                pts.Add(p);
            }
            return pts;
        }

        static IEnumerable<PolygonWithHoles> AssembleMultipolygon(OsmRelation rel, OsmDataset ds, Dictionary<long, Vec2d> lv, ExtractedFeatures res)
        {
            var outers = new List<List<long>>(); var inners = new List<List<long>>();
            foreach (var m in rel.Members)
            {
                if (m.Type != 'w' || !ds.Ways.TryGetValue(m.Ref, out var way)) continue;
                if (m.Role == "inner") inners.Add(way.Nodes); else if (m.Role == "outer" || m.Role == "") outers.Add(way.Nodes);
            }
            var outerRings = RingAssembler.Assemble(outers, out int u1);
            var innerRings = RingAssembler.Assemble(inners, out int u2);
            res.UnclosedRings += u1 + u2;
            var inner = new List<List<Vec2d>>();
            foreach (var r in innerRings) { var p = RingToPoints(r, lv); if (p != null) inner.Add(p); }
            foreach (var r in outerRings)
            {
                var pts = RingToPoints(r, lv);
                if (pts == null) { res.UnclosedRings++; continue; }
                var poly = new PolygonWithHoles { Outer = pts };
                poly.Normalize();
                foreach (var h in inner)
                    if (h.Count > 2 && PolygonUtil.PointInRing(poly.Outer, h[0].X, h[0].Y)) poly.Holes.Add(new List<Vec2d>(h));
                poly.Normalize();
                if (poly.Area() > 0.5) yield return poly;
            }
        }

        enum PolyKind { None, Building, Water, LandUse }

        static bool IsPolygonFeature(Dictionary<string, string> t, out PolyKind kind)
        {
            kind = PolyKind.None;
            string b = Tag(t, "building");
            if (b != null && b != "no") { kind = PolyKind.Building; return true; }
            if (Tag(t, "building:part") != null && Tag(t, "building:part") != "no") { kind = PolyKind.Building; return true; }
            string nat = Tag(t, "natural"), water = Tag(t, "water"), lu = Tag(t, "landuse"), ww = Tag(t, "waterway");
            if (nat == "water" || ww == "riverbank" || lu == "reservoir" || lu == "basin" || nat == "bay") { kind = PolyKind.Water; return true; }
            if (LandUseOf(t) != LandUseClass.Unknown) { kind = PolyKind.LandUse; return true; }
            return false;
        }

        static void EmitPolygon(long id, Dictionary<string, string> t, PolygonWithHoles poly, ExtractedFeatures res)
        {
            IsPolygonFeature(t, out var kind);
            switch (kind)
            {
                case PolyKind.Building: res.Buildings.Add(MakeBuilding(id, t, poly)); break;
                case PolyKind.Water:
                    res.Water.Add(new WaterFeature { OsmId = id, Polygon = poly, IsRiver = Tag(t, "water") == "river" || Tag(t, "waterway") == "riverbank" || Tag(t, "water") == "canal" });
                    break;
                case PolyKind.LandUse:
                    res.LandUse.Add(new LandUseFeature { OsmId = id, Class = LandUseOf(t), Polygon = poly });
                    break;
            }
        }

        public static LandUseClass LandUseOf(Dictionary<string, string> t)
        {
            string lu = Tag(t, "landuse"), nat = Tag(t, "natural"), leis = Tag(t, "leisure"), am = Tag(t, "amenity");
            switch (lu)
            {
                case "forest": return LandUseClass.Forest;
                case "farmland": case "farmyard": case "allotments": case "greenhouse_horticulture": case "plant_nursery": return LandUseClass.Farmland;
                case "meadow": case "grass": case "village_green": case "greenfield": case "flowerbed": return LandUseClass.Grass;
                case "orchard": return LandUseClass.Orchard;
                case "vineyard": return LandUseClass.Vineyard;
                case "residential": return LandUseClass.Residential;
                case "commercial": case "retail": case "education": case "institutional": return LandUseClass.Commercial;
                case "industrial": case "construction": case "quarry": case "landfill": return LandUseClass.Industrial;
                case "cemetery": return LandUseClass.Cemetery;
                case "recreation_ground": return LandUseClass.Park;
                case "railway": return LandUseClass.Railway;
            }
            switch (nat)
            {
                case "wood": return LandUseClass.Forest;
                case "grassland": case "heath": case "scrub": case "fell": return LandUseClass.Grass;
                case "scree": case "shingle": return LandUseClass.Scree;
                case "bare_rock": case "cliff": return LandUseClass.Rock;
                case "glacier": return LandUseClass.Glacier;
                case "wetland": return LandUseClass.Wetland;
                case "sand": case "beach": return LandUseClass.Sand;
            }
            if (Tag(t, "highway") == "pedestrian" && Tag(t, "area") == "yes" || Tag(t, "place") == "square" || Tag(t, "amenity") == "marketplace" || Tag(t, "highway") == "footway" && Tag(t, "area") == "yes") return LandUseClass.Plaza;
            if (leis == "park" || leis == "garden" || leis == "playground") return LandUseClass.Park;
            if (leis == "pitch" || leis == "stadium" || leis == "sports_centre" || leis == "golf_course") return LandUseClass.Sports;
            if (am == "parking") return LandUseClass.Parking;
            return LandUseClass.Unknown;
        }

        static BuildingFeature MakeBuilding(long id, Dictionary<string, string> t, PolygonWithHoles poly)
        {
            var b = new BuildingFeature { OsmId = id, Footprint = poly, Name = Tag(t, "name") ?? "" };
            string bt = Tag(t, "building") ?? "yes";
            b.Kind = KindOf(bt);
            if (Tag(t, "amenity") == "place_of_worship") b.Kind = BuildingKind.Church;
            if (Tag(t, "railway") == "station" || Tag(t, "public_transport") == "station") b.Kind = BuildingKind.Station;
            b.Roof = RoofOf(Tag(t, "roof:shape"));
            var inv = CultureInfo.InvariantCulture;
            if (byte.TryParse(Tag(t, "building:levels"), NumberStyles.Integer, inv, out byte lv)) b.Levels = lv;
            float h = ParseMeters(Tag(t, "height"));
            float mh = ParseMeters(Tag(t, "min_height"));
            if (!float.IsNaN(mh)) b.MinHeight = mh;
            if (!float.IsNaN(h)) b.RidgeHeight = h;
            else if (b.Levels > 0) b.RidgeHeight = b.Levels * 3.1f + (b.Roof == RoofShape.Flat ? 0.4f : 2.6f);
            b.IsPart = Tag(t, "building:part") != null && Tag(t, "building") == null;
            b.Tag_Colour = Tag(t, "building:colour") ?? ""; b.Tag_RoofColour = Tag(t, "roof:colour") ?? "";
            b.ColorSeed = unchecked((int)(id * 2654435761L));
            return b;
        }

        public static float ParseMeters(string s)
        {
            if (string.IsNullOrEmpty(s)) return float.NaN;
            int i = 0; while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == ',')) i++;
            if (i == 0) return float.NaN;
            return float.TryParse(s.Substring(0, i).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : float.NaN;
        }

        static BuildingKind KindOf(string b)
        {
            switch (b)
            {
                case "house": case "detached": case "semidetached_house": case "terrace": case "residential": case "bungalow": case "farm": case "cabin": case "chalet": return BuildingKind.House;
                case "apartments": case "dormitory": return BuildingKind.Apartments;
                case "commercial": case "retail": case "office": case "supermarket": case "hotel": case "kiosk": return BuildingKind.Commercial;
                case "industrial": case "warehouse": case "factory": case "manufacture": return BuildingKind.Industrial;
                case "church": case "cathedral": case "chapel": case "mosque": case "synagogue": case "temple": return BuildingKind.Church;
                case "public": case "civic": case "government": case "hospital": case "school": case "university": case "college": case "kindergarten": case "fire_station": return BuildingKind.Public;
                case "garage": case "garages": case "carport": case "parking": return BuildingKind.Garage;
                case "shed": case "hut": case "roof": case "barn": case "farm_auxiliary": case "greenhouse": case "stable": case "service": return BuildingKind.Shed;
                case "train_station": return BuildingKind.Station;
                case "tower": return BuildingKind.Tower;
                default: return BuildingKind.Generic;
            }
        }

        static RoofShape RoofOf(string s)
        {
            switch (s)
            {
                case "gabled": return RoofShape.Gabled;
                case "hipped": case "half-hipped": return RoofShape.Hipped;
                case "pyramidal": return RoofShape.Pyramidal;
                case "skillion": case "lean_to": return RoofShape.Skillion;
                case "mansard": case "gambrel": return RoofShape.Mansard;
                case "dome": case "round": case "onion": return RoofShape.Dome;
                case "flat": return RoofShape.Flat;
                default: return (RoofShape)255;   // unknown, resolved later from the surface model
            }
        }
    }
}
