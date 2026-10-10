using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Import;
using UnityEditor;
using UnityEngine;

namespace SwissPoliceSim.Editor
{
    /// <summary>Editor front-end for SwissWorldImporter (the same code the command-line baker runs).</summary>
    public sealed class SwissGeoDataImporterWindow : EditorWindow
    {
        double _lat = 46.94658, _lon = 7.44410;
        int _radius = 2;
                int _osmSource;
        string _overpassUrl = "https://overpass-api.de/api/interpreter";
        string _log = "";
        Vector2 _scroll;
        CancellationTokenSource _cts;
        readonly ConcurrentQueue<string> _lines = new ConcurrentQueue<string>();
        Task _task;

        [MenuItem("Swiss Police/Import Geodata…")]
        static void Open() => GetWindow<SwissGeoDataImporterWindow>("Swiss Geodata");

        void OnEnable() { EditorApplication.update += Pump; }
        void OnDisable() { EditorApplication.update -= Pump; _cts?.Cancel(); }

        void Pump()
        {
            bool changed = false;
            while (_lines.TryDequeue(out var l)) { _log += l + "\n"; changed = true; }
            if (_task != null && _task.IsCompleted)
            {
                if (_task.IsFaulted) _log += "FEHLER: " + _task.Exception?.GetBaseException().Message + "\n";
                else if (!_task.IsCanceled) { _log += "Fertig.\n"; AssetDatabase.Refresh(); }
                _task = null; changed = true;
            }
            if (changed) Repaint();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Lädt swissALTI3D, swissSURFACE3D (swisstopo) und OpenStreetMap und speichert 1-km-Kacheln in StreamingAssets/SwissWorld/tiles.\n" +
                "Quellenangaben sind Pflicht (swisstopo OGD, OSM ODbL) – siehe docs/DATA_SOURCES.md.", MessageType.Info);
            _lat = EditorGUILayout.DoubleField("Zentrum Breite (WGS84)", _lat);
            _lon = EditorGUILayout.DoubleField("Zentrum Länge (WGS84)", _lon);
            _radius = EditorGUILayout.IntSlider("Radius (Kacheln à 1 km)", _radius, 0, 6);
            int n = (2 * _radius + 1) * (2 * _radius + 1);
            EditorGUILayout.LabelField($"= {n} Kacheln ({n} km²)");
            _osmSource = EditorGUILayout.Popup("OSM-Quelle", _osmSource, new[] { "OSM-API (nur kleine Gebiete)", "Overpass (empfohlen)" });
            if (_osmSource == 1) _overpassUrl = EditorGUILayout.TextField("Overpass-URL", _overpassUrl);

            using (new EditorGUI.DisabledScope(_task != null))
                if (GUILayout.Button("Importieren")) Run();
            if (_task != null && GUILayout.Button("Abbrechen")) _cts.Cancel();

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(220));
            EditorGUILayout.TextArea(_log, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void Run()
        {
            _log = ""; _cts = new CancellationTokenSource();
            var center = SwissCoordinateConverter.ToLv95(_lat, _lon);
            string outDir = Path.Combine(Application.streamingAssetsPath, "SwissWorld");
            string cache = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "SwissImportCache");
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var imp = new WorldBuilder(http, s => _lines.Enqueue(s))
            {
                OsmSource = _osmSource == 1 ? OsmSourceKind.Overpass : OsmSourceKind.OsmApi, OverpassUrl = _overpassUrl
            };
            int radius = _radius; var token = _cts.Token;
            _task = Task.Run(() => imp.BuildAsync(center, radius, outDir, cache, token), token);
        }
    }
}
