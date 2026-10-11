using System; using System.Linq; using SwissPoliceSim.Core.Geo; using SwissPoliceSim.Core.Meshing; using SwissPoliceSim.Core.Storage; using SwissPoliceSim.Core.Geometry;
class P { static void Main() {
 var c = SwissCoordinateConverter.ToLv95(46.94658, 7.44410); var src = new LooseTileSource("Assets/StreamingAssets/SwissWorld/tiles");
 var id = new TileId((int)Math.Floor(c.E/1000),(int)Math.Floor(c.N/1000)); src.TryRead(id, out var bytes); var t = TileMeshBuilder.Build(bytes,false);
 double px=c.E-8, pz=c.N-120;
 foreach (var b in t.Buildings.OrderBy(b=>{var q=PolygonUtil.Centroid(b.Footprint.Outer); return Math.Pow(q.X-px,2)+Math.Pow(q.Y-pz,2);}).Take(4)) {
  var o=b.Footprint.Outer; double a=0; for(int i=0;i<o.Count;i++){var p=o[i];var q=o[(i+1)%o.Count];a+=p.X*q.Y-q.X*p.Y;}
  Console.WriteLine($"b {b.OsmId} kind {b.Kind} n={o.Count} holes={b.Footprint.Holes.Count} area2={a:F0} eave {b.EaveHeight} ridge {b.RidgeHeight} ground {b.GroundHeight} levels {b.Levels} part {b.IsPart} minH {b.MinHeight}");
 } } }
