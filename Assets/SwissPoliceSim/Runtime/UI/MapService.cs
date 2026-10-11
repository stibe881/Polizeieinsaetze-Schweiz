using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Terrain;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.UI
{
    /// <summary>Map textures on demand: 1 km tiles (rasterised roads/buildings/water, worker thread) and 8 km macro blocks for the zoomed-out country view. LRU cached.</summary>
    public sealed class MapService : MonoBehaviour
    {
        public static MapService Instance { get; private set; }
        public int tileRes = 256; public int macroRes = 256; public int maxTextures = 140;
        readonly Dictionary<long, Texture2D> _tex = new Dictionary<long, Texture2D>(); readonly LinkedList<long> _lru = new LinkedList<long>(); readonly HashSet<long> _pending = new HashSet<long>();
        readonly ConcurrentQueue<(long key, byte[] px, int res)> _done = new ConcurrentQueue<(long, byte[], int)>();
        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; foreach (var t in _tex.Values) if (t != null) Destroy(t); }

        static long TileKey(TileId t) => ((long)t.X << 32) ^ (uint)t.Y; static long MacroKey(int bx, int by) => (1L << 62) ^ ((long)bx << 32) ^ (uint)by;

        void Update()
        {
            int n = 0;
            while (n < 4 && _done.TryDequeue(out var d))
            {
                n++; _pending.Remove(d.key); var t = new Texture2D(d.res, d.res, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 2 }; t.SetPixelData(d.px, 0); t.Apply(true, false);
                _tex[d.key] = t; _lru.AddLast(d.key); while (_tex.Count > maxTextures) { long old = _lru.First.Value; _lru.RemoveFirst(); if (_tex.TryGetValue(old, out var ot)) { Destroy(ot); _tex.Remove(old); } }
            }
        }

        public Texture2D Tile(TileId id)
        {
            long k = TileKey(id); if (_tex.TryGetValue(k, out var t)) return t; if (_pending.Count >= 6 || _pending.Contains(k) || !WorldData.Ready || !WorldData.HasTile(id)) return null;
            _pending.Add(k); int res = tileRes; Task.Run(() =>
            {
                try { if (!WorldData.Source.TryRead(id, out var b)) return; var c = TileMeshBuilder.Build(b, false); _done.Enqueue((k, MapRasterizer.RenderTile(c, res), res)); } catch (Exception) { }
            });
            return null;
        }

        public Texture2D Macro(int bx, int by)
        {
            long k = MacroKey(bx, by); if (_tex.TryGetValue(k, out var t)) return t; var block = WorldData.MacroAt(bx, by); if (block == null || _pending.Count >= 6 || _pending.Contains(k)) return null;
            _pending.Add(k); int res = macroRes; Task.Run(() => { try { _done.Enqueue((k, MapRasterizer.RenderMacro(block, res), res)); } catch (Exception) { } }); return null;
        }
    }

    /// <summary>Draws the map into a GUI rectangle (north up) and converts world coordinates to screen positions for overlays.</summary>
    public static class MapDraw
    {
        public static double CentreE, CentreN, MetersPerPixel = 2; public static Rect Area;

        public static Vector2 ToScreen(double e, double n) => new Vector2((float)(Area.center.x + (e - CentreE) / MetersPerPixel), (float)(Area.center.y - (n - CentreN) / MetersPerPixel));
        public static void ToWorld(Vector2 s, out double e, out double n) { e = CentreE + (s.x - Area.center.x) * MetersPerPixel; n = CentreN - (s.y - Area.center.y) * MetersPerPixel; }

        public static void Draw(Rect area, double e, double n, double mpp)
        {
            Area = area; CentreE = e; CentreN = n; MetersPerPixel = mpp; var svc = MapService.Instance; GUI.BeginClip(area); var local = new Rect(0, 0, area.width, area.height); var oldArea = Area; Area = local;
            Gui.Fill(local, new Color(0.93f, 0.92f, 0.90f));
            if (svc != null)
            {
                bool macro = mpp > 7.0;
                double halfW = area.width * 0.5 * mpp, halfH = area.height * 0.5 * mpp;
                if (macro)
                {
                    int bx0 = (int)Math.Floor((e - halfW) / MacroBlock.BlockSize), bx1 = (int)Math.Floor((e + halfW) / MacroBlock.BlockSize), by0 = (int)Math.Floor((n - halfH) / MacroBlock.BlockSize), by1 = (int)Math.Floor((n + halfH) / MacroBlock.BlockSize);
                    for (int bx = bx0; bx <= bx1; bx++) for (int by = by0; by <= by1; by++) { var t = svc.Macro(bx, by); if (t == null) continue; var p0 = ToScreen(bx * MacroBlock.BlockSize, (by + 1) * MacroBlock.BlockSize); float s = (float)(MacroBlock.BlockSize / mpp); GUI.DrawTexture(new Rect(p0.x, p0.y, s, s), t, ScaleMode.StretchToFill, false); }
                }
                else
                {
                    int tx0 = (int)Math.Floor((e - halfW) / 1000), tx1 = (int)Math.Floor((e + halfW) / 1000), ty0 = (int)Math.Floor((n - halfH) / 1000), ty1 = (int)Math.Floor((n + halfH) / 1000);
                    for (int tx = tx0; tx <= tx1; tx++) for (int ty = ty0; ty <= ty1; ty++) { var t = svc.Tile(new TileId(tx, ty)); if (t == null) continue; var p0 = ToScreen(tx * 1000.0, (ty + 1) * 1000.0); float s = (float)(1000.0 / mpp); GUI.DrawTexture(new Rect(p0.x, p0.y, s + 0.5f, s + 0.5f), t, ScaleMode.StretchToFill, false); }
                }
            }
            GUI.EndClip();
            Area = oldArea;
        }

        /// <summary>Overlay helper: screen position in the (unclipped) GUI space.</summary>
        public static bool InView(Vector2 p, float margin = 0) => p.x > Area.x - margin && p.x < Area.xMax + margin && p.y > Area.y - margin && p.y < Area.yMax + margin;
    }
}
