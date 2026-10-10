using System;
using SwissPoliceSim.Core.Geo;

namespace SwissPoliceSim.Core.Roads
{
    public enum RoadClass : byte
    {
        Motorway = 0, MotorwayLink, Trunk, TrunkLink, Primary, PrimaryLink, Secondary, SecondaryLink,
        Tertiary, TertiaryLink, Unclassified, Residential, LivingStreet, Service, Track,
        // non-vehicular (pedestrians / cyclists only)
        Pedestrian, Footway, Cycleway, Path, Steps
    }

    public enum OneWay : byte { No = 0, Forward = 1, Reverse = 2 }

    /// <summary>How <see cref="RoadWay.SpeedKmh"/> was determined. Everything except Tagged/Zone is derived from Swiss general limits.</summary>
    public enum SpeedSource : byte
    {
        Default = 0,        // no information, class based guess (should be rare)
        Tagged = 1,         // explicit OSM maxspeed
        Zone = 2,           // Tempo-30-Zone / Begegnungszone (zone:maxspeed / living_street)
        LegalUrban = 3,     // general limit innerorts (50)
        LegalRural = 4,     // general limit ausserorts (80)
        LegalMotorroad = 5, // Autostrasse (100)
        LegalMotorway = 6   // Autobahn (120)
    }

    /// <summary>One OSM way (or a per-tile chunk of it) projected to LV95.</summary>
    public sealed class RoadWay
    {
        public long OsmId;
        public RoadClass Class;
        public OneWay OneWay;
        public bool Bridge, Tunnel, Roundabout;
        public int MaxSpeedKmh;           // 0 = not tagged
        public int Lanes;                 // 0 = unknown
        public bool Motorroad;            // OSM motorroad=yes (Autostrasse)
        public int ZoneSpeedKmh;          // zone:maxspeed (e.g. 30), 0 = none
        public sbyte UrbanHint = -1;      // -1 unknown, 0 rural, 1 urban (from maxspeed:type / source:maxspeed)
        public int SpeedKmh;              // resolved limit (see SpeedLimitResolver); 0 = not yet resolved
        public SpeedSource SpeedBasis;
        public string Name = "";
        public string Ref = "";
        public long[] NodeIds = new long[0];
        public double[] E = new double[0];
        public double[] N = new double[0];
        /// <summary>Baked road surface height (m a.s.l.) per point; empty = unknown (runtime falls back to terrain).</summary>
        public double[] H = new double[0];

        public int PointCount => E.Length;

        /// <summary>True for ways cars may use (everything except pedestrian/cycle/path/steps).</summary>
        public bool IsVehicular => Class <= RoadClass.Track;

        public double Length()
        {
            double l = 0;
            for (int i = 1; i < E.Length; i++) l += Math.Sqrt(Sq(E[i] - E[i - 1]) + Sq(N[i] - N[i - 1]));
            return l;
        }

        static double Sq(double v) => v * v;

        /// <summary>Number of lanes of THIS way's carriageway (one direction for one-way ways, both directions otherwise).</summary>
        public int CarriageLanes()
        {
            if (!IsVehicular) return 1;
            if (Lanes > 0) return Lanes;
            bool oneway = OneWay != OneWay.No;
            switch (Class)
            {
                case RoadClass.Motorway: return oneway ? 2 : 4;
                case RoadClass.Trunk: return oneway ? 2 : 2;
                default: return oneway ? 1 : 2;
            }
        }

        public float LaneWidth()
        {
            switch (Class)
            {
                case RoadClass.Pedestrian: return 4.5f;
                case RoadClass.Footway: return 2.0f;
                case RoadClass.Cycleway: return 2.2f;
                case RoadClass.Path: return 1.2f;
                case RoadClass.Steps: return 1.6f;
                case RoadClass.Motorway: case RoadClass.MotorwayLink: return 3.75f;
                case RoadClass.Trunk: case RoadClass.TrunkLink: case RoadClass.Primary: return 3.5f;
                case RoadClass.Secondary: case RoadClass.Tertiary: return 3.2f;
                case RoadClass.Service: case RoadClass.Track: return 2.6f;
                default: return 2.9f;
            }
        }

        /// <summary>Asphalt width of the carriageway in metres.</summary>
        public float CarriagewayWidth() => IsVehicular ? Math.Max(2.5f, CarriageLanes() * LaneWidth() * (Class == RoadClass.Track ? 1.1f : 1f)) : LaneWidth();

        /// <summary>Hard shoulder (Pannenstreifen) width on each carriageway side (motorway: right side only).</summary>
        public float ShoulderWidth() => Class == RoadClass.Motorway ? 2.8f : (Class == RoadClass.Trunk ? 1.2f : 0f);

        /// <summary>Total width used for visual mesh / collision (asphalt incl. shoulder).</summary>
        public float VisualWidth() => CarriagewayWidth() + ShoulderWidth();

        static int DefaultLanes(RoadClass c)
        {
            switch (c)
            {
                case RoadClass.Motorway: return 4;
                case RoadClass.Trunk: case RoadClass.Primary: case RoadClass.Secondary: return 2;
                case RoadClass.MotorwayLink: case RoadClass.TrunkLink: case RoadClass.PrimaryLink:
                case RoadClass.SecondaryLink: case RoadClass.TertiaryLink: return 1;
                case RoadClass.Track: return 1;
                default: return 2;
            }
        }

        /// <summary>
        /// Speed limit in km/h. Tagged values win. Otherwise SIMPLIFIED game defaults per class
        /// (Swiss general limits: 120 Autobahn, 100 Autostrasse, 80 ausserorts, 50 innerorts - but "innerorts"
        /// is not known without settlement data, so Phase 1 uses class based guesses; flagged via SpeedSource.Default).
        /// </summary>
        public int EffectiveSpeedKmh(out SpeedSource source)
        {
            if (SpeedKmh > 0) { source = SpeedBasis; return SpeedKmh; }
            if (MaxSpeedKmh > 0) { source = SpeedSource.Tagged; return MaxSpeedKmh; }
            source = SpeedSource.Default;
            switch (Class)
            {
                case RoadClass.Motorway: return 120;
                case RoadClass.Trunk: return 100;
                case RoadClass.MotorwayLink: case RoadClass.TrunkLink: return 60;
                case RoadClass.Primary: case RoadClass.Secondary: return 80;
                case RoadClass.Tertiary: case RoadClass.Unclassified: return 60;
                case RoadClass.Residential: return 50;
                case RoadClass.LivingStreet: return 20;
                case RoadClass.Service: return 30;
                case RoadClass.Track: return 30;
                case RoadClass.Pedestrian: case RoadClass.Footway: case RoadClass.Cycleway: case RoadClass.Path: case RoadClass.Steps: return 5;
                default: return 50;
            }
        }
    }

    public static class RoadTags
    {
        public static bool TryParseClass(string highway, out RoadClass c)
        {
            switch (highway)
            {
                case "motorway": c = RoadClass.Motorway; return true;
                case "motorway_link": c = RoadClass.MotorwayLink; return true;
                case "trunk": c = RoadClass.Trunk; return true;
                case "trunk_link": c = RoadClass.TrunkLink; return true;
                case "primary": c = RoadClass.Primary; return true;
                case "primary_link": c = RoadClass.PrimaryLink; return true;
                case "secondary": c = RoadClass.Secondary; return true;
                case "secondary_link": c = RoadClass.SecondaryLink; return true;
                case "tertiary": c = RoadClass.Tertiary; return true;
                case "tertiary_link": c = RoadClass.TertiaryLink; return true;
                case "unclassified": c = RoadClass.Unclassified; return true;
                case "residential": c = RoadClass.Residential; return true;
                case "living_street": c = RoadClass.LivingStreet; return true;
                case "service": c = RoadClass.Service; return true;
                case "track": c = RoadClass.Track; return true;
                case "pedestrian": c = RoadClass.Pedestrian; return true;
                case "footway": c = RoadClass.Footway; return true;
                case "cycleway": c = RoadClass.Cycleway; return true;
                case "path": c = RoadClass.Path; return true;
                case "steps": c = RoadClass.Steps; return true;
                default: c = RoadClass.Unclassified; return false;
            }
        }

        /// <summary>Parses OSM maxspeed ("50", "50 km/h", "CH:urban", "CH:rural", "CH:motorway"...). 0 = unknown/none.</summary>
        public static int ParseMaxSpeed(string v)
        {
            if (string.IsNullOrEmpty(v)) return 0;
            switch (v)
            {
                case "CH:urban": return 50;
                case "CH:rural": return 80;
                case "CH:trunk": return 100;
                case "CH:motorway": return 120;
            }
            int i = 0;
            while (i < v.Length && char.IsDigit(v[i])) i++;
            if (i == 0) return 0;
            int kmh = int.Parse(v.Substring(0, i));
            if (v.IndexOf("mph", StringComparison.OrdinalIgnoreCase) >= 0) kmh = (int)Math.Round(kmh * 1.609344);
            return kmh;
        }

        public static OneWay ParseOneWay(string oneway, string junction, RoadClass cls)
        {
            if (oneway == "yes" || oneway == "true" || oneway == "1") return OneWay.Forward;
            if (oneway == "-1" || oneway == "reverse") return OneWay.Reverse;
            if (oneway == "no") return OneWay.No;
            if (junction == "roundabout" || junction == "circular") return OneWay.Forward;
            if (cls == RoadClass.Motorway || cls == RoadClass.MotorwayLink) return OneWay.Forward;
            return OneWay.No;
        }
    }
}
