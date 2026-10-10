using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Player;
using SwissPoliceSim.Util;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.Navigation
{
    /// <summary>
    /// Road graph + routing over the real OSM network, plus a world-space route line.
    /// Phase 1 loads the whole pilot area graph (a few thousand ways). For all of Switzerland this must become a
    /// hierarchical / tiled graph (see docs/ARCHITECTURE.md, "Navigation").
    /// </summary>
    public sealed class NavigationService : MonoBehaviour
    {
        public struct Destination { public string Name; public double Lat, Lon; }

        // Approximate public landmark coordinates (WGS84), snapped to the nearest road node at runtime.
        public static readonly Destination[] PilotDestinations =
        {
            new Destination { Name = "Bahnhof Bern",        Lat = 46.9490, Lon = 7.4393 },
            new Destination { Name = "Zytglogge",           Lat = 46.9480, Lon = 7.4474 },
            new Destination { Name = "Stadion Wankdorf",    Lat = 46.9633, Lon = 7.4649 },
            new Destination { Name = "Bundesplatz",         Lat = 46.9466, Lon = 7.4441 },
        };

        public TileStreamingManager streaming;
        public string dataFolder = "SwissWorld";
        public Transform vehicleOrPlayer;

        public RoadGraph Graph { get; private set; }
        public bool IsReady => Graph != null;
        public RoutePath Route { get; private set; }
        public string DestinationName { get; private set; } = "";
        public double RemainingMeters { get; private set; }
        int _destIndex = -1;
        Task<RoadGraph> _loading;
        Task<RoutePath> _routing;
        LineRenderer _line;
        float _nextLineUpdate;

        void Start()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, dataFolder, "roads");
            _loading = Task.Run(() =>
            {
                var ways = new List<RoadWay>();
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.spr"))
                        using (var s = File.OpenRead(f)) ways.AddRange(RoadTileFile.Read(s, out _));
                return RoadGraph.Build(ways);
            });

            var go = new GameObject("RouteLine");
            _line = go.AddComponent<LineRenderer>();
            _line.widthMultiplier = 3f; _line.positionCount = 0; _line.numCornerVertices = 3;
            _line.material = MaterialUtil.Create(new Color(1f, 0.85f, 0.1f), 0f);
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.alignment = LineAlignment.View;
        }

        void Update()
        {
            if (_loading != null && _loading.IsCompleted)
            {
                if (_loading.IsFaulted) Debug.LogError("Road graph load failed: " + _loading.Exception?.GetBaseException());
                else Graph = _loading.Result;
                _loading = null;
            }
            if (_routing != null && _routing.IsCompleted)
            {
                Route = _routing.IsFaulted ? null : _routing.Result;
                _routing = null;
                if (Route == null) Debug.LogWarning("No route found to " + DestinationName);
            }
            if (!IsReady || vehicleOrPlayer == null) return;

            if (GameInput.NextDestination()) NextDestination();
            if (GameInput.ClearRoute()) { Route = null; DestinationName = ""; _line.positionCount = 0; }

            if (Time.time >= _nextLineUpdate) { _nextLineUpdate = Time.time + 0.25f; UpdateLine(); }
        }

        public void NextDestination()
        {
            _destIndex = (_destIndex + 1) % PilotDestinations.Length;
            var d = PilotDestinations[_destIndex];
            SetDestination(SwissCoordinateConverter.ToLv95(d.Lat, d.Lon), d.Name);
        }

        public void SetDestination(Lv95 target, string name)
        {
            if (!IsReady || _routing != null) return;
            DestinationName = name;
            var from = WorldOrigin.ToLv95(vehicleOrPlayer.position);
            var graph = Graph;
            _routing = Task.Run(() => graph.FindRoute(from.E, from.N, target.E, target.N));
        }

        void UpdateLine()
        {
            if (Route == null || Route.Count < 2 || streaming == null) { _line.positionCount = 0; RemainingMeters = 0; return; }
            var me = WorldOrigin.ToLv95(vehicleOrPlayer.position);

            int nearest = 0; double best = double.MaxValue;
            for (int i = 0; i < Route.Count; i++)
            {
                double d = Sq(Route.E[i] - me.E) + Sq(Route.N[i] - me.N);
                if (d < best) { best = d; nearest = i; }
            }
            double rem = 0;
            for (int i = nearest + 1; i < Route.Count; i++) rem += Math.Sqrt(Sq(Route.E[i] - Route.E[i - 1]) + Sq(Route.N[i] - Route.N[i - 1]));
            RemainingMeters = rem;
            if (rem < 25) { Route = null; _line.positionCount = 0; return; }

            var pts = new List<Vector3>();
            for (int i = nearest; i < Route.Count; i++)
            {
                if (Sq(Route.E[i] - me.E) + Sq(Route.N[i] - me.N) > 2500.0 * 2500.0) break;   // only draw what is streamed in
                if (!streaming.TryGetGroundHeight(Route.E[i], Route.N[i], out float h)) break;
                pts.Add(WorldOrigin.ToUnity(Route.E[i], Route.N[i], h + 0.6));
            }
            _line.positionCount = pts.Count;
            _line.SetPositions(pts.ToArray());
        }

        static double Sq(double v) => v * v;
    }
}
