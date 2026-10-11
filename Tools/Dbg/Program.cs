using System; using System.Linq; using SwissPoliceSim.Core.Storage; using SwissPoliceSim.Core.Meshing;
class P { static void Main() { var src=new LooseTileSource("Assets/StreamingAssets/SwissWorld/tiles"); foreach(var t in src.Tiles){src.TryRead(t,out var b); var c=TileMeshBuilder.Build(b,false); foreach(var p in c.Pois) Console.WriteLine($"{p.Kind} {p.Name}");} } }
