using System.Collections.Generic;
using SwissPoliceSim.Core.Geometry;
using SwissPoliceSim.Core.Roads;

namespace SwissPoliceSim.Core.Features
{
    public enum RoofShape : byte { Flat = 0, Gabled = 1, Hipped = 2, Pyramidal = 3, Skillion = 4, Mansard = 5, Dome = 6 }
    public enum BuildingKind : byte { Generic = 0, House = 1, Apartments = 2, Commercial = 3, Industrial = 4, Church = 5, Public = 6, Garage = 7, Shed = 8, Tower = 9, Station = 10 }

    /// <summary>One building footprint (LV95 metres) with the best available height information.</summary>
    public sealed class BuildingFeature
    {
        public long OsmId;
        public PolygonWithHoles Footprint = new PolygonWithHoles();
        public BuildingKind Kind;
        public RoofShape Roof;                 // OSM roof:shape if tagged, else derived from the surface model
        public float EaveHeight;               // metres above ground (wall height)
        public float RidgeHeight;              // total height above ground (roof top)
        public float MinHeight;                // building:part floating above ground
        public byte Levels;                    // 0 = unknown
        public bool IsPart;                    // OSM building:part (a sub-volume of a larger building)
        public bool HeightMeasured;            // true if taken from swissSURFACE3D − swissALTI3D
        public float GroundHeight = float.NaN; // terrain height at the footprint (m a.s.l.), filled when baked
        public float RidgeDirX = 1, RidgeDirY;  // unit vector along the ridge
        public int ColorSeed;                  // deterministic seed for facade/roof colour variation
        public string Name = "";
        public string Tag_Colour = "", Tag_RoofColour = "";
    }

    public sealed class WaterFeature
    {
        public long OsmId;
        public PolygonWithHoles Polygon = new PolygonWithHoles();
        public bool IsRiver;
        public float SurfaceHeight = float.NaN;  // m a.s.l., filled when baked
    }

    public enum WaterwayKind : byte { Stream = 0, Ditch = 1, River = 2, Canal = 3 }

    public sealed class WaterwayFeature
    {
        public long OsmId; public WaterwayKind Kind; public float Width; public bool Tunnel;
        public double[] E, N; public float[] H;
    }

    public enum LandUseClass : byte
    {
        Unknown = 0, Grass = 1, Forest = 2, Farmland = 3, Orchard = 4, Vineyard = 5, Residential = 6,
        Commercial = 7, Industrial = 8, Park = 9, Cemetery = 10, Rock = 11, Scree = 12, Glacier = 13, Wetland = 14,
        Sand = 15, Parking = 16, Railway = 17, Sports = 18, Water = 19, Plaza = 20, Urban = 21
    }

    public sealed class LandUseFeature
    {
        public long OsmId; public LandUseClass Class; public PolygonWithHoles Polygon = new PolygonWithHoles();
    }

    public enum PointKind : byte
    {
        Tree = 0, TrafficSignals = 1, Crossing = 2, StopSign = 3, GiveWay = 4, BusStop = 5, StreetLamp = 6, TrafficCalming = 7, RailCrossing = 8
    }

    public sealed class PointFeature
    {
        public PointKind Kind; public double E, N; public float Height = float.NaN; public float Direction; public long OsmId;
    }

    public sealed class RailFeature
    {
        public long OsmId; public double[] E, N; public float[] H; public bool Tunnel, Bridge;
    }

    /// <summary>Everything extracted from one OSM dataset, projected to LV95.</summary>
    public enum PoiKind : byte { Police = 0, Fuel = 1, Hospital = 2, FireStation = 3, Landmark = 4, Parking = 5, Station = 6 }

    /// <summary>Named point of interest (police station, hospital, fuel station, ...) for the map, dispatch and navigation.</summary>
    public sealed class PoiFeature
    {
        public PoiKind Kind; public double E, N; public string Name = ""; public long OsmId;
    }

    public sealed class ExtractedFeatures
    {
        public List<PoiFeature> Pois = new List<PoiFeature>();
        public List<RoadWay> Roads = new List<RoadWay>();
        public List<BuildingFeature> Buildings = new List<BuildingFeature>();
        public List<WaterFeature> Water = new List<WaterFeature>();
        public List<WaterwayFeature> Waterways = new List<WaterwayFeature>();
        public List<LandUseFeature> LandUse = new List<LandUseFeature>();
        public List<PointFeature> Points = new List<PointFeature>();
        public List<RailFeature> Rails = new List<RailFeature>();
        public int UnclosedRings;
    }
}
