using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Import;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Storage;
using SwissPoliceSim.Core.Terrain;
using UnityEngine;

namespace SwissPoliceSim.World
{
    /// <summary>
    /// The static data of the world that stays resident: manifest, tile source (loose tiles and/or region packs), cantons and municipalities, the POI index,
    /// the hierarchical router (arterial skeleton + lazy tile graphs) and the macro terrain blocks for the horizon ring.
    /// </summary>
    public static class WorldData
    {
        public static WorldManifest Manifest { get; private set; }
        public static ITileSource Source { get; private set; }
        public static AdminBoundaries Admin { get; private set; }
        public static PoiIndex Pois { get; private set; }
        public static HierarchicalRouter Router { get; private set; }
        public static readonly Dictionary<long, MacroBlock> Macro = new Dictionary<long, MacroBlock>();
        public static string Root { get; private set; }
        public static bool Ready { get; private set; }
        public static string Status { get; private set; } = "";
        public static string LoadError { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { Manifest = null; Source = null; Admin = null; Pois = null; Router = null; Macro.Clear(); Ready = false; Status = ""; LoadError = null; }

        public static string DefaultRoot => Path.Combine(Application.streamingAssetsPath, "SwissWorld");
        static long MacroKey(int bx, int by) => ((long)bx << 32) ^ (uint)by;
        public static MacroBlock MacroAt(int bx, int by) => Macro.TryGetValue(MacroKey(bx, by), out var b) ? b : null;

        /// <summary>Loads everything on a worker thread. Await from a coroutine by polling <see cref="Ready"/>.</summary>
        public static Task LoadAsync(string root = null)
        {
            root = root ?? DefaultRoot; Root = root; Ready = false; LoadError = null;
            return Task.Run(() =>
            {
                try
                {
                    Status = "Weltdaten lesen …";
                    string mf = Path.Combine(root, "manifest.json");
                    Manifest = File.Exists(mf) ? JsonUtility.FromJson<WorldManifest>(File.ReadAllText(mf)) : new WorldManifest();
                    var sources = new List<ITileSource>();
                    string tiles = Path.Combine(root, "tiles"); if (Directory.Exists(tiles)) sources.Add(new LooseTileSource(tiles));
                    foreach (var pack in Directory.Exists(root) ? Directory.GetFiles(root, "*.sprg") : new string[0])
                        if (!Path.GetFileName(pack).StartsWith("arterial", StringComparison.OrdinalIgnoreCase)) sources.Add(new RegionPackSource(pack));
                    Source = new CompositeTileSource(sources.ToArray());
                    Status = "Kantons- und Gemeindegrenzen …";
                    string admin = Path.Combine(root, "admin.spj"); if (File.Exists(admin)) using (var f = File.OpenRead(admin)) Admin = AdminBoundaries.Load(f);
                    Status = "Orte und Polizeistationen …";
                    string poi = Path.Combine(root, "pois.spp");
                    Pois = File.Exists(poi) ? PoiIndex.Load(poi) : PoiIndex.Build(Source);
                    Status = "Routing …";
                    Router = new HierarchicalRouter(Source);
                    string art = Path.Combine(root, "arterial.sprg"); if (File.Exists(art)) Router.LoadArterial(art);
                    Status = "Fernsicht …";
                    string macro = Path.Combine(root, "macro");
                    if (Directory.Exists(macro)) foreach (var f in Directory.GetFiles(macro, "*.spm")) using (var fs = File.OpenRead(f)) { var b = MacroBlock.Read(fs); Macro[MacroKey(b.BX, b.BY)] = b; }
                    Status = "Bereit"; Ready = true;
                }
                catch (Exception ex) { LoadError = ex.Message; Status = "Fehler: " + ex.Message; Debug.LogError("WorldData.LoadAsync failed: " + ex); }
            });
        }

        public static bool HasTile(TileId t) { return Source != null && TileExists(t); }
        static readonly HashSet<TileId> _tileSet = new HashSet<TileId>();
        static bool TileExists(TileId t)
        {
            if (_tileSet.Count == 0 && Source != null) lock (_tileSet) if (_tileSet.Count == 0) foreach (var x in Source.Tiles) _tileSet.Add(x);
            return _tileSet.Contains(t);
        }
    }
}
