using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using SwissPoliceSim.Common;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Terrain;
using SwissPoliceSim.Rendering;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SwissPoliceSim.World
{
    /// <summary>One streamed 1 km tile: its content (kept for gameplay queries) and the Unity objects.</summary>
    public sealed class LoadedTile
    {
        public TileId Id; public TileContent Content; public GameObject Root; public int Quality = 99;     // 1 = full detail, 2 = mid, 3 = far
        public Mesh Terrain, Roads, BuildingsFar, BuildingsDetail, Water, Furniture, Rail;
        public GameObject TerrainGo, RoadGo, BuildingGo, BuildingDetailGo, WaterGo, FurnitureGo, RailGo;
        public bool Busy; public bool CollidersRequested, CollidersReady;
        public double OriginE, OriginN;
    }

    /// <summary>
    /// Streams tiles around the focus: worker threads read + decode + mesh the tile (all in the Unity-free Core), the main thread creates Unity meshes within a frame budget.
    /// Quality ring by tile distance d: d≤1 full (3.9 m terrain, detailed facades, colliders), d=2 mid (7.8 m terrain, simple buildings), d≥3 far (15.6 m terrain). Tiles beyond Keep are unloaded.
    /// </summary>
    public sealed class TileStreamer : MonoBehaviour
    {
        public Transform focus;
        public int detailRing = 1, midRing = 2, farRing = 3, keepExtra = 1;
        public float frameBudgetMs = 3.5f; public int maxParallelJobs = 2;
        public bool buildColliders = true;

        public readonly Dictionary<TileId, LoadedTile> Tiles = new Dictionary<TileId, LoadedTile>();
        readonly ConcurrentQueue<TileBuild> _done = new ConcurrentQueue<TileBuild>();
        readonly Queue<(LoadedTile tile, Mesh mesh, GameObject go, int id)> _bakeDone = new Queue<(LoadedTile, Mesh, GameObject, int)>();
        readonly ConcurrentQueue<(LoadedTile, Mesh, GameObject, int)> _baked = new ConcurrentQueue<(LoadedTile, Mesh, GameObject, int)>();
        int _jobs; TileId _focusTile; bool _focusKnown;
        public event Action<LoadedTile> TileLoaded, TileUnloaded;
        public int TilesBuilt { get; private set; }
        public double LastBuildMs { get; private set; }
        public static TileStreamer Instance { get; private set; }

        sealed class TileBuild
        {
            public TileId Id; public TileContent Content; public int Quality; public UnityMeshArrays Terrain, Roads, BuildingsFar, BuildingsDetail, Water, Furniture, Rail; public double Ms; public string Error;
        }

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------------ queries
        public bool TryGetTile(TileId id, out LoadedTile t) => Tiles.TryGetValue(id, out t) && t.Content != null;

        /// <summary>Terrain height (m a.s.l.) at LV95 coordinates from the loaded tiles, NaN if the tile is not loaded.</summary>
        public float Height(double e, double n)
        {
            if (!Tiles.TryGetValue(TileGrid.FromLv95(e, n), out var t) || t.Content?.Terrain == null) return float.NaN;
            return t.Content.Terrain.SampleLocal(e - t.OriginE, n - t.OriginN);
        }

        public bool IsAreaLoaded(double e, double n, int ring = 1)
        {
            var c = TileGrid.FromLv95(e, n);
            for (int dx = -ring; dx <= ring; dx++) for (int dy = -ring; dy <= ring; dy++)
            {
                var id = new TileId(c.X + dx, c.Y + dy);
                if (WorldData.HasTile(id) && (!Tiles.TryGetValue(id, out var t) || t.Content == null || t.Quality > 2 || !t.CollidersReady && buildColliders)) return false;
            }
            return true;
        }

        public IEnumerable<LoadedTile> Loaded() { foreach (var t in Tiles.Values) if (t.Content != null) yield return t; }

        // ------------------------------------------------------------------ update
        void Update()
        {
            if (!WorldData.Ready || focus == null) return;
            var p = WorldOrigin.ToLv95(focus.position); var ft = TileGrid.FromLv95(p.E, p.N);
            if (!_focusKnown || !ft.Equals(_focusTile)) { _focusTile = ft; _focusKnown = true; }
            Schedule(ft, p);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < frameBudgetMs)
            {
                if (_baked.TryDequeue(out var b)) { ApplyCollider(b); continue; }
                if (_done.TryDequeue(out var r)) { Apply(r); continue; }
                break;
            }
            Unload(ft);
        }

        static int Dist(TileId a, TileId b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        int QualityFor(int d) => d <= detailRing ? 1 : d <= midRing ? 2 : 3;

        void Schedule(TileId ft, Lv95 p)
        {
            if (_jobs >= maxParallelJobs) return;
            // candidate tiles ordered by distance (spiral)
            LoadedTile bestTile = null; TileId bestId = default; int bestScore = int.MaxValue; int bestQ = 0;
            for (int dx = -farRing; dx <= farRing; dx++)
                for (int dy = -farRing; dy <= farRing; dy++)
                {
                    var id = new TileId(ft.X + dx, ft.Y + dy); if (!WorldData.HasTile(id)) continue;
                    int d = Math.Max(Math.Abs(dx), Math.Abs(dy)); int q = QualityFor(d);
                    Tiles.TryGetValue(id, out var lt);
                    if (lt != null && (lt.Busy || lt.Quality <= q)) continue;           // already as good or better / in flight
                    int score = d * 10 + (lt == null ? 0 : 5);
                    if (score < bestScore) { bestScore = score; bestId = id; bestTile = lt; bestQ = q; }
                }
            if (bestScore == int.MaxValue) return;
            if (bestTile == null) { bestTile = new LoadedTile { Id = bestId, OriginE = TileGrid.MinE(bestId), OriginN = TileGrid.MinN(bestId) }; Tiles[bestId] = bestTile; }
            bestTile.Busy = true; _jobs++;
            var id2 = bestId; int q2 = bestQ;
            Task.Run(() =>
            {
                var res = new TileBuild { Id = id2, Quality = q2 }; var sw = Stopwatch.StartNew();
                try
                {
                    if (!WorldData.Source.TryRead(id2, out var bytes)) throw new Exception("tile missing");
                    var c = TileMeshBuilder.Build(bytes, true, q2 == 1);
                    res.Content = c;
                    int stride = q2 == 1 ? 1 : q2 == 2 ? 2 : 4;
                    if (c.Terrain != null) { int maxStride = (c.Terrain.Resolution - 1) / 32; stride = Math.Min(stride, Math.Max(1, maxStride)); res.Terrain = UnityMeshArrays.From(TerrainMesher.Build(c.Terrain, c.LandUse, stride), true); }
                    res.Roads = UnityMeshArrays.From(c.RoadMesh);
                    res.BuildingsFar = UnityMeshArrays.From(c.BuildingMesh);
                    if (q2 == 1) res.BuildingsDetail = UnityMeshArrays.From(c.BuildingMeshDetail);
                    res.Water = UnityMeshArrays.From(c.WaterMesh);
                    res.Furniture = UnityMeshArrays.From(c.FurnitureMesh, true);
                    res.Rail = UnityMeshArrays.From(c.RailMesh);
                }
                catch (Exception ex) { res.Error = ex.Message; }
                res.Ms = sw.Elapsed.TotalMilliseconds; _done.Enqueue(res);
            });
        }

        // ------------------------------------------------------------------ main thread: build Unity objects
        void Apply(TileBuild r)
        {
            _jobs = Math.Max(0, _jobs - 1);
            if (!Tiles.TryGetValue(r.Id, out var t)) return;
            t.Busy = false;
            if (r.Error != null) { Debug.LogWarning($"Tile {r.Id.X}-{r.Id.Y}: {r.Error}"); Tiles.Remove(r.Id); return; }
            // tiles that left the keep range while the job was running are dropped
            if (_focusKnown && Dist(r.Id, _focusTile) > farRing + keepExtra) { Tiles.Remove(r.Id); return; }
            bool first = t.Root == null;
            if (first)
            {
                t.Root = new GameObject($"Tile {r.Id.X}-{r.Id.Y}");
                t.Root.transform.position = WorldOrigin.ToUnity(t.OriginE, t.OriginN, 0);
            }
            else
            {   // quality upgrade: replace the meshes
                DestroyMeshes(t);
            }
            t.Content = r.Content; t.Quality = r.Quality; LastBuildMs = r.Ms; TilesBuilt++;

            t.Terrain = r.Terrain?.Build($"terrain {r.Id.X}-{r.Id.Y}"); t.TerrainGo = Part(t, "Terrain", t.Terrain, MaterialLibrary.Terrain(), UnityEngine.Rendering.ShadowCastingMode.On);
            t.Roads = r.Roads?.Build("roads"); t.RoadGo = Part(t, "Roads", t.Roads, MaterialLibrary.Roads(), UnityEngine.Rendering.ShadowCastingMode.Off);
            t.BuildingsFar = r.BuildingsFar?.Build("buildings"); t.BuildingGo = Part(t, "Buildings", t.BuildingsFar, MaterialLibrary.Buildings(), UnityEngine.Rendering.ShadowCastingMode.On);
            if (r.BuildingsDetail != null) { t.BuildingsDetail = r.BuildingsDetail.Build("buildings detail"); t.BuildingDetailGo = Part(t, "BuildingsDetail", t.BuildingsDetail, MaterialLibrary.Buildings(), UnityEngine.Rendering.ShadowCastingMode.On); if (t.BuildingGo != null) t.BuildingGo.SetActive(false); }
            t.Water = r.Water?.Build("water"); t.WaterGo = Part(t, "Water", t.Water, new[] { MaterialLibrary.Water() }, UnityEngine.Rendering.ShadowCastingMode.Off);
            t.Furniture = r.Furniture?.Build("furniture"); t.FurnitureGo = Part(t, "Furniture", t.Furniture, MaterialLibrary.Furniture(), UnityEngine.Rendering.ShadowCastingMode.On);
            t.Rail = r.Rail?.Build("rail"); t.RailGo = Part(t, "Rail", t.Rail, MaterialLibrary.Rails(), UnityEngine.Rendering.ShadowCastingMode.On);

            if (buildColliders && r.Quality == 1) RequestColliders(t);
            TileLoaded?.Invoke(t);
        }

        GameObject Part(LoadedTile t, string name, Mesh mesh, Material[] mats, UnityEngine.Rendering.ShadowCastingMode shadows)
        {
            if (mesh == null) return null;
            var go = new GameObject(name); go.transform.SetParent(t.Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); var arr = new Material[mesh.subMeshCount]; for (int i = 0; i < arr.Length; i++) arr[i] = mats[Math.Min(i, mats.Length - 1)];
            mr.sharedMaterials = arr; mr.shadowCastingMode = shadows; mr.receiveShadows = true;
            return go;
        }

        void RequestColliders(LoadedTile t)
        {
            if (t.CollidersRequested) return; t.CollidersRequested = true; int pending = 0;
            void Req(Mesh m, GameObject go) { if (m == null || go == null) return; pending++; int id = m.GetInstanceID(); var mesh = m; Task.Run(() => { try { Physics.BakeMesh(id, false); } catch (Exception) { } _baked.Enqueue((t, mesh, go, id)); }); }
            Req(t.Terrain, t.TerrainGo); Req(t.Roads, t.RoadGo); Req(t.BuildingsFar, t.BuildingGo != null ? t.BuildingGo : null);
            if (t.BuildingGo != null && t.BuildingDetailGo != null) { /* the far building mesh carries the collider (cheaper); the detail mesh is visual only */ }
            if (pending == 0) t.CollidersReady = true;
        }

        void ApplyCollider((LoadedTile tile, Mesh mesh, GameObject go, int id) b)
        {
            if (b.go == null || b.mesh == null || b.tile.Root == null) return;
            var col = b.go.AddComponent<MeshCollider>(); col.sharedMesh = b.mesh;
            if (b.tile.TerrainGo != null && b.tile.TerrainGo.GetComponent<MeshCollider>() != null && b.tile.RoadGo != null && b.tile.RoadGo.GetComponent<MeshCollider>() != null) b.tile.CollidersReady = true;
        }

        void DestroyMeshes(LoadedTile t)
        {
            foreach (var go in new[] { t.TerrainGo, t.RoadGo, t.BuildingGo, t.BuildingDetailGo, t.WaterGo, t.FurnitureGo, t.RailGo }) if (go != null) Destroy(go);
            foreach (var m in new[] { t.Terrain, t.Roads, t.BuildingsFar, t.BuildingsDetail, t.Water, t.Furniture, t.Rail }) if (m != null) Destroy(m);
            t.Terrain = t.Roads = t.BuildingsFar = t.BuildingsDetail = t.Water = t.Furniture = t.Rail = null; t.CollidersRequested = t.CollidersReady = false;
        }

        List<TileId> _remove = new List<TileId>();
        void Unload(TileId ft)
        {
            _remove.Clear();
            foreach (var kv in Tiles) { if (!kv.Value.Busy && Dist(kv.Key, ft) > farRing + keepExtra) _remove.Add(kv.Key); }
            foreach (var id in _remove)
            {
                var t = Tiles[id]; TileUnloaded?.Invoke(t); DestroyMeshes(t); if (t.Root != null) Destroy(t.Root); Tiles.Remove(id);
            }
            // fall back to the cheap building mesh outside the detail range (the detail meshes are heavy)
            foreach (var t in Tiles.Values)
            {
                if (t.BuildingDetailGo == null) continue; int d = Dist(t.Id, ft);
                if (d >= midRing + 1) { Destroy(t.BuildingDetailGo); if (t.BuildingsDetail != null) Destroy(t.BuildingsDetail); t.BuildingDetailGo = null; t.BuildingsDetail = null; if (t.BuildingGo != null) t.BuildingGo.SetActive(true); t.Quality = Math.Max(t.Quality, 2); }
            }
        }
    }
}
