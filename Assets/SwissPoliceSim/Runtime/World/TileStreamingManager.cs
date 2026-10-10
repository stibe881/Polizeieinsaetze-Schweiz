using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Terrain;
using SwissPoliceSim.Util;
using UnityEngine;

namespace SwissPoliceSim.World
{
    /// <summary>
    /// Streams 1 km world cells (terrain + roads) around the target. File IO and geometry generation run on worker
    /// threads; Unity objects are created on the main thread (at most <see cref="maxCreatesPerFrame"/> per frame).
    /// Tile data comes from StreamingAssets/&lt;dataFolder&gt;/{terrain,roads}/{X}-{Y}.{spt,spr}, produced by SwissWorldImporter.
    /// Phase 1 reads loose files; an Addressables provider can replace <see cref="LoadTileData"/> later.
    /// </summary>
    public sealed class TileStreamingManager : MonoBehaviour
    {
        public Transform target;
        public string dataFolder = "SwissWorld";
        [Min(1)] public int terrainRadius = 2;
        public int unloadMargin = 1;
        public int maxCreatesPerFrame = 1;
        public int maxConcurrentLoads = 4;
        public Material terrainMaterial;

        sealed class LoadedData
        {
            public TileId Id; public bool Missing;
            public TerrainTileData Terrain; public float[,,] Splat;
            public List<RoadWay> Ways; public RoadMeshData RoadMesh;
        }

        sealed class Tile
        {
            public GameObject Root; public TerrainData TerrainData; public Mesh RoadMesh; public Terrain Terrain;
            public TerrainTileData Data; public List<RoadWay> Ways;
        }

        readonly Dictionary<TileId, Tile> _tiles = new Dictionary<TileId, Tile>();
        readonly Dictionary<TileId, Task<LoadedData>> _pending = new Dictionary<TileId, Task<LoadedData>>();
        readonly HashSet<TileId> _missing = new HashSet<TileId>();
        readonly List<TileId> _desired = new List<TileId>();
        readonly List<TileId> _scratch = new List<TileId>();
        TileId _centre = new TileId(int.MinValue, int.MinValue);
        Transform _worldRoot;
        TerrainLayer[] _layers;
        Material[] _roadMaterials;

        public bool InitialLoadComplete { get; private set; }
        public int LoadedTileCount => _tiles.Count;
        public int PendingCount => _pending.Count;
        public TileId CurrentTile => _centre;
        public event Action<TileId> TileLoaded;
        public event Action<TileId> TileUnloaded;

        string Root => Path.Combine(Application.streamingAssetsPath, dataFolder);

        void Awake()
        {
            _worldRoot = new GameObject("WorldTiles").transform;
            _layers = new[]
            {
                MakeLayer(new Color(0.30f, 0.45f, 0.22f), 12f),  // grass
                MakeLayer(new Color(0.42f, 0.40f, 0.37f), 20f),  // rock
                MakeLayer(new Color(0.92f, 0.94f, 0.97f), 20f),  // snow
            };
            _roadMaterials = new[]
            {
                MaterialUtil.Create(new Color(0.16f, 0.16f, 0.17f), 0.25f),
                MaterialUtil.Create(new Color(0.22f, 0.22f, 0.23f), 0.2f),
                MaterialUtil.Create(new Color(0.34f, 0.34f, 0.33f), 0.1f),
            };
            if (terrainMaterial == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Terrain/Lit");
                if (sh != null) terrainMaterial = new Material(sh);
            }
        }

        static TerrainLayer MakeLayer(Color c, float tile)
        {
            return new TerrainLayer { diffuseTexture = MaterialUtil.SolidTexture(c), tileSize = new Vector2(tile, tile) };
        }

        void OnDestroy()
        {
            foreach (var t in _tiles.Values) Release(t);
            _tiles.Clear();
        }

        void Update()
        {
            if (target == null) return;
            var lv = WorldOrigin.ToLv95(target.position);
            var c = TileGrid.FromLv95(lv.E, lv.N);
            if (c != _centre)
            {
                _centre = c;
                _desired.Clear();
                _desired.AddRange(TileGrid.Ring(c, terrainRadius));
            }

            // 1) start background loads (nearest first); path is resolved here because Application.* is main-thread only
            string root = Root;
            foreach (var id in _desired)
            {
                if (_pending.Count >= maxConcurrentLoads) break;
                if (_tiles.ContainsKey(id) || _pending.ContainsKey(id) || _missing.Contains(id)) continue;
                _pending[id] = Task.Run(() => LoadTileData(id, root));
            }

            // 2) create finished tiles on the main thread
            int created = 0;
            _scratch.Clear();
            foreach (var kv in _pending) if (kv.Value.IsCompleted) _scratch.Add(kv.Key);
            foreach (var id in _scratch)
            {
                if (created >= maxCreatesPerFrame) break;
                var task = _pending[id];
                _pending.Remove(id);
                if (task.IsFaulted) { Debug.LogError($"Tile {id} failed to load: {task.Exception?.GetBaseException()}"); _missing.Add(id); continue; }
                var data = task.Result;
                if (data.Missing) { _missing.Add(id); continue; }
                if (TileGrid.Chebyshev(id, _centre) > terrainRadius + unloadMargin) continue; // player moved on
                CreateTile(data);
                created++;
            }

            // 3) unload far tiles
            _scratch.Clear();
            foreach (var id in _tiles.Keys)
                if (TileGrid.Chebyshev(id, _centre) > terrainRadius + unloadMargin) _scratch.Add(id);
            foreach (var id in _scratch) Unload(id);

            if (!InitialLoadComplete && _pending.Count == 0)
            {
                bool allThere = true;
                foreach (var id in _desired) if (TileGrid.Chebyshev(id, _centre) <= 1 && !_tiles.ContainsKey(id) && !_missing.Contains(id)) allThere = false;
                InitialLoadComplete = allThere && _tiles.Count > 0;
            }
        }

        // ---------------- worker thread ----------------

        static LoadedData LoadTileData(TileId id, string root)
        {
            var r = new LoadedData { Id = id };
            string tf = Path.Combine(root, "terrain", id + ".spt");
            if (!File.Exists(tf)) { r.Missing = true; return r; }
            using (var s = File.OpenRead(tf)) r.Terrain = TerrainTileData.Read(s);
            r.Splat = TerrainSplat.Compute(r.Terrain, 128);

            string rf = Path.Combine(root, "roads", id + ".spr");
            if (File.Exists(rf))
                using (var s = File.OpenRead(rf)) r.Ways = RoadTileFile.Read(s, out _);
            else r.Ways = new List<RoadWay>();
            r.RoadMesh = RoadMeshBuilder.Build(r.Ways, r.Terrain, TileGrid.MinE(id), TileGrid.MinN(id));
            return r;
        }

        // ---------------- main thread ----------------

        void CreateTile(LoadedData d)
        {
            var tile = new Tile { Data = d.Terrain, Ways = d.Ways };
            var root = new GameObject($"Tile {d.Id}");
            root.transform.SetParent(_worldRoot, false);
            root.transform.position = WorldOrigin.ToUnity(TileGrid.MinE(d.Id), TileGrid.MinN(d.Id), 0);
            tile.Root = root;

            // terrain
            var t = d.Terrain;
            var td = new TerrainData { heightmapResolution = t.Resolution };
            td.size = new Vector3(1000f, Mathf.Max(0.1f, t.MaxH - t.MinH), 1000f);
            var h = new float[t.Resolution, t.Resolution];
            for (int r = 0; r < t.Resolution; r++)
                for (int c = 0; c < t.Resolution; c++)
                    h[r, c] = t.Q[r * t.Resolution + c] / 65535f;
            td.SetHeights(0, 0, h);
            td.terrainLayers = _layers;
            td.alphamapResolution = 128;
            td.SetAlphamaps(0, 0, d.Splat);
            tile.TerrainData = td;

            var tgo = Terrain.CreateTerrainGameObject(td);
            tgo.name = "Terrain";
            tgo.transform.SetParent(root.transform, false);
            tgo.transform.localPosition = new Vector3(0, t.MinH, 0);
            tile.Terrain = tgo.GetComponent<Terrain>();
            tile.Terrain.heightmapPixelError = 2f;
            tile.Terrain.drawInstanced = true;
            if (terrainMaterial != null) tile.Terrain.materialTemplate = terrainMaterial;

            // roads
            if (d.RoadMesh.Vertices.Count > 0)
            {
                var go = new GameObject("Roads");
                go.transform.SetParent(root.transform, false);
                var mesh = d.RoadMesh.ToMesh();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = _roadMaterials;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                tile.RoadMesh = mesh;
            }

            _tiles[d.Id] = tile;
            LinkNeighbours(d.Id);
            TileLoaded?.Invoke(d.Id);
        }

        Terrain TerrainAt(int x, int y) => _tiles.TryGetValue(new TileId(x, y), out var t) ? t.Terrain : null;

        void LinkNeighbours(TileId id)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (!_tiles.TryGetValue(new TileId(id.X + dx, id.Y + dy), out var t)) continue;
                    int x = id.X + dx, y = id.Y + dy;
                    t.Terrain.SetNeighbors(TerrainAt(x - 1, y), TerrainAt(x, y + 1), TerrainAt(x + 1, y), TerrainAt(x, y - 1));
                }
        }

        void Unload(TileId id)
        {
            if (!_tiles.TryGetValue(id, out var t)) return;
            _tiles.Remove(id);
            Release(t);
            TileUnloaded?.Invoke(id);
        }

        static void Release(Tile t)
        {
            if (t.RoadMesh != null) Destroy(t.RoadMesh);
            if (t.TerrainData != null) Destroy(t.TerrainData);
            if (t.Root != null) Destroy(t.Root);
        }

        // ---------------- queries ----------------

        /// <summary>Terrain height (m a.s.l.) at an LV95 position if its tile is loaded.</summary>
        public bool TryGetGroundHeight(double e, double n, out float height)
        {
            var id = TileGrid.FromLv95(e, n);
            if (_tiles.TryGetValue(id, out var t))
            {
                height = t.Data.SampleLocal(e - TileGrid.MinE(id), n - TileGrid.MinN(id));
                return true;
            }
            height = 0; return false;
        }

        public void ForceRefresh() { _centre = new TileId(int.MinValue, int.MinValue); }
    }
}
