namespace SwissPoliceSim.Core.Geo
{
    /// <summary>Swiss national grid LV95 (EPSG:2056). E/N in metres, H = height above sea level (LN02) in metres.</summary>
    public readonly struct Lv95
    {
        public readonly double E;
        public readonly double N;
        public readonly double H;
        public Lv95(double e, double n, double h = 0) { E = e; N = n; H = h; }
        public override string ToString() => $"LV95({E:F1}, {N:F1}, {H:F1})";
    }

    /// <summary>WGS84 geographic coordinate in degrees.</summary>
    public readonly struct Wgs84
    {
        public readonly double Lat;
        public readonly double Lon;
        public Wgs84(double lat, double lon) { Lat = lat; Lon = lon; }
        public override string ToString() => $"WGS84({Lat:F6}, {Lon:F6})";
    }
}
