using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Navigation;
using SwissPoliceSim.Player;
using SwissPoliceSim.Vehicle;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.UI
{
    /// <summary>Phase-1 debug/driving HUD (IMGUI): speed, real-world coordinates, streaming state, navigation, controls.</summary>
    public sealed class PilotHud : MonoBehaviour
    {
        public PlayerSession session;
        public TileStreamingManager streaming;
        public NavigationService navigation;
        float _fps;
        GUIStyle _style, _big;

        void Update() { _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f); }

        void OnGUI()
        {
            if (session == null) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
                _big = new GUIStyle(_style) { fontSize = 34, fontStyle = FontStyle.Bold };
            }
            var p = WorldOrigin.ToLv95(session.Active.position);
            var w = SwissCoordinateConverter.ToWgs84(p);
            var tile = TileGrid.FromLv95(p.E, p.N);

            GUI.Box(new Rect(10, 10, 400, 150), GUIContent.none);
            GUI.Label(new Rect(20, 14, 380, 22), "Swiss Police Simulator – Phase 1 (Pilot Bern)", _style);
            GUI.Label(new Rect(20, 36, 380, 22), $"LV95  E {p.E:F0}  N {p.N:F0}  H {p.H:F0} m", _style);
            GUI.Label(new Rect(20, 56, 380, 22), $"WGS84 {w.Lat:F5}, {w.Lon:F5}   Kachel {tile}", _style);
            GUI.Label(new Rect(20, 76, 380, 22), $"Kacheln geladen {streaming.LoadedTileCount}  (ausstehend {streaming.PendingCount})   {_fps:F0} FPS", _style);
            if (navigation != null && navigation.IsReady)
            {
                string nav = string.IsNullOrEmpty(navigation.DestinationName) ? "kein Ziel (N = nächstes Ziel)"
                    : navigation.Route == null ? $"{navigation.DestinationName}: Route wird berechnet…"
                    : $"→ {navigation.DestinationName}: {navigation.RemainingMeters / 1000.0:F2} km";
                GUI.Label(new Rect(20, 96, 380, 22), "Navi: " + nav, _style);
            }
            else GUI.Label(new Rect(20, 96, 380, 22), "Navi: Strassennetz wird geladen…", _style);
            GUI.Label(new Rect(20, 118, 380, 36), "Daten: swisstopo swissALTI3D, © OpenStreetMap contributors", _style);

            if (session.InVehicle)
            {
                GUI.Label(new Rect(Screen.width - 230, Screen.height - 70, 220, 50), $"{Mathf.Abs(session.vehicle.ForwardSpeedKmh):F0} km/h", _big);
                var l = session.lights;
                string st = $"{(l.HeadlightsOn ? "Licht " : "")}{(l.BlueOn ? "Blaulicht " : "")}{(l.SirenOn ? "Horn " : "")}{(l.HazardOn ? "Warnblinker" : "")}";
                GUI.Label(new Rect(Screen.width - 330, Screen.height - 95, 320, 22), st, _style);
            }

            GUI.Label(new Rect(10, Screen.height - 46, Screen.width - 20, 40),
                "WASD fahren/gehen · Leertaste Handbremse · F ein-/aussteigen · C Kamera · L Licht · B Blaulicht · Q Horn · Z/X/H Blinker · N Navi-Ziel · M Route löschen · Esc Maus", _style);
        }
    }
}
