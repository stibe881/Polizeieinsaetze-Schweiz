using System; using SwissPoliceSim.Core.Meshing; using SwissPoliceSim.Core.Terrain; using SwissPoliceSim.Core.Storage; using SwissPoliceSim.Core.Geo;
class P { static void Main() {
 var src = new LooseTileSource("Assets/StreamingAssets/SwissWorld/tiles"); TileId t0 = default; foreach (var t in src.Tiles) { t0 = t; break; }
 src.TryRead(t0, out var b); var c = TileContainer.Deserialize(b); var td = TerrainTileData.Read(new System.IO.MemoryStream(c.Chunks["TERR"]));
 var m = TerrainMesher.Build(td, c.Chunks.ContainsKey("LAND") ? c.Chunks["LAND"] : null, 2);
 int n = (td.Resolution-1)/2+1; int gridTris = (n-1)*(n-1)*2; int up=0, down=0, skirtOut=0, skirtIn=0; var idx = m.Indices[0]; float cx0=500,cz0=500;
 for (int i=0;i+2<idx.Count;i+=3){ int a=idx[i],bb=idx[i+1],cc=idx[i+2];
  float ax=m.Positions[a*3],ay=m.Positions[a*3+1],az=m.Positions[a*3+2]; float ex=m.Positions[bb*3]-ax,ey=m.Positions[bb*3+1]-ay,ez=m.Positions[bb*3+2]-az, fx=m.Positions[cc*3]-ax,fy=m.Positions[cc*3+1]-ay,fz=m.Positions[cc*3+2]-az;
  float nx=ey*fz-ez*fy, ny=ez*fx-ex*fz, nz=ex*fy-ey*fx;
  if (i/3 < gridTris) { if (ny>0) up++; else down++; } else { float mx=(ax+m.Positions[bb*3]+m.Positions[cc*3])/3-cx0, mz=(az+m.Positions[bb*3+2]+m.Positions[cc*3+2])/3-cz0; if (nx*mx+nz*mz>0) skirtOut++; else skirtIn++; } }
 Console.WriteLine($"grid up {up} down {down}; skirts outward {skirtOut} inward {skirtIn}"); } }
