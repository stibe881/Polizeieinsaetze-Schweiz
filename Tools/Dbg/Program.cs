using System; using System.IO; using System.IO.Compression; using System.Collections.Generic; using SwissPoliceSim.Core.Storage; using SwissPoliceSim.Core.Terrain;
class P {
 static int Defl(byte[] d){ using var o=new MemoryStream(); using(var z=new DeflateStream(o,CompressionLevel.Optimal,true)) z.Write(d,0,d.Length); return (int)o.Length; }
 static void Main() { long raw=0, plain=0, pred=0, pred2=0; var src=new LooseTileSource("Assets/StreamingAssets/SwissWorld/tiles");
  foreach(var t in src.Tiles){src.TryRead(t,out var b); var c=TileContainer.Deserialize(b); c.TryGet("TERR",out var terr); var td=TerrainTileData.Read(new MemoryStream(terr)); raw+=terr.Length; plain+=Defl(terr);
   int n=td.Resolution; var res=new List<byte>(); 
   for(int r=0;r<n;r++)for(int cc=0;cc<n;cc++){ int a=cc>0?td.Q[r*n+cc-1]:0, bb=r>0?td.Q[(r-1)*n+cc]:0, d=(r>0&&cc>0)?td.Q[(r-1)*n+cc-1]:0; int p= (r==0&&cc==0)?0: (r==0?a:(cc==0?bb:Math.Max(Math.Min(a,bb),Math.Min(Math.Max(a,bb),a+bb-d)))); int e=td.Q[r*n+cc]-p; uint z=(uint)((e<<1)^(e>>31)); while(z>=128){res.Add((byte)(z|128)); z>>=7;} res.Add((byte)z);} 
   pred+=Defl(res.ToArray());
   // coarser quantisation: 12 bit effective (shift 4)
   var res2=new List<byte>(); for(int r=0;r<n;r++)for(int cc=0;cc<n;cc++){ int a=cc>0?td.Q[r*n+cc-1]>>4:0, bb=r>0?td.Q[(r-1)*n+cc]>>4:0, d=(r>0&&cc>0)?td.Q[(r-1)*n+cc-1]>>4:0; int p=(r==0&&cc==0)?0:(r==0?a:(cc==0?bb:Math.Max(Math.Min(a,bb),Math.Min(Math.Max(a,bb),a+bb-d)))); int e=(td.Q[r*n+cc]>>4)-p; uint z=(uint)((e<<1)^(e>>31)); while(z>=128){res2.Add((byte)(z|128)); z>>=7;} res2.Add((byte)z);} pred2+=Defl(res2.ToArray());
  }
  Console.WriteLine($"raw {raw} plainDeflate {plain} predictive {pred} predictive12bit {pred2}  per tile {pred/25}"); } }
