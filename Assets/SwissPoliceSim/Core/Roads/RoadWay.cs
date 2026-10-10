using System;
using SwissPoliceSim.Core.Geo;

namespace SwissPoliceSim.Core.Roads
{
    public enum RoadClass : byte
    {
        Motorway = 0, MotorwayLink, Trunk, TrunkLink, Primary, PrimaryLink, Secondary, SecondaryLink,
        Tertiary, TertiaryLink, Unclassified, Residential, LivingStreet, Service, Track
    }

    public enum OneWay : byte { No = 0, Forward = 1, Reverse = 2 }

    public enum SpeedSource : byte { Default = 0, Tagged = 1 }

    /// <summary>One OSM way (or a per-tile chunk of it) projected to LV95.</summary>
    public sealed class RoadWay
    {
        public long OsmId;
        public RoadClass Class;
        public OneWay OneWay;
        public bool Bridge, Tunnel, Roundabout;
        public int MaxSpeedKmh;           // 0 = not tagged
        public int Lanes;                 // 0 = unknown
        public string Name = "";
        public string Ref = "";
        public long[] NodeIds = new long[0];
        public double[] E = new double[0];
        public double[] N = new double[0];
        /// <summary>Baked road surface height (m a.s.l.) per point; empty = unknown (runtime falls back to terrain).</summary>
        public double[] H = new double[0];

        public int PointCount => E.Length;

        public double Length()
        {
            double l = 0;
            for (int i = 1; i < E.Length; i++) l += Math.Sqrt(Sq(E[i] - E[i - 1]) + Sq(N[i] - N[i - 1]));
            return l;
        }

        static double Sq(double v) => v * v;

        /// <summary>Road width in metres used for the visual mesh (simplified; real lane data is a later phase).</summary>
        public float VisualWidth()
        {
            float lanes = Lanes > 0 ? Lanes : DefaultLanes(Class);
            float laneW = Class <= RoadClass.TrunkLink ? 3.6f : 3.0f;
            if (OneWay == OneWay.No && Lanes > 0 && Lanes % 2 == 1) lanes = Lanes; // total lanes tagged
            else if (OneWay == OneWay.No && Lanes == 0) lanes = DefaultLanes(Class);
            return Math.Max(2.5f, lanes * laneW);
        }

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
