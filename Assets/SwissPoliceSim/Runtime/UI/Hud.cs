using System;
using SwissPoliceSim.Core.Game;
using SwissPoliceSim.Core.Police;
using SwissPoliceSim.Game;
using SwissPoliceSim.Player;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.UI
{
    /// <summary>Head-up display: speed, legal limit sign, minimap, navigation banner, dispatch offers, notifications and context prompts.</summary>
    public static class Hud
    {
        public static bool MinimapBig;
        static GameSession S => GameSession.I;

        public static void Draw()
        {
            var s = S; if (s == null || !s.Playing) return;
            TopBar(s); Notes(); Prompts(s); IncidentPanel(s); Minimap(s); Nav(s); Speedo(s); Pursuit(s); Radar(s);
        }

        static void TopBar(GameSession s)
        {
            var r = new Rect(Gui.Width / 2 - 330, 8, 660, 36); Gui.Panel(r, new Color(0.04f, 0.05f, 0.08f, 0.82f));
            string rank = s.Career != null && s.Corps != null ? s.Career.RankName(s.Corps) : "";
            Gui.Label(new Rect(r.x + 12, r.y, 250, r.height), (s.Corps != null ? s.Corps.Name : "") + " · " + rank, Gui.Text, 15);
            Gui.Label(new Rect(r.x + 260, r.y, 150, r.height), s.Now.ToString("ddd dd.MM. HH:mm"), Gui.Text, 15, TextAnchor.MiddleCenter, true);
            string where = string.IsNullOrEmpty(s.PlaceName) ? "—" : s.PlaceName; var jur = s.Jurisdiction;
            Color jc = jur.InOwnTerritory ? Gui.Good : jur.Kind == JurisdictionKind.Unknown ? Gui.TextDim : Gui.Warn;
            Gui.Label(new Rect(r.x + 412, r.y, 240, r.height), where + (jur.InOwnTerritory || jur.Kind == JurisdictionKind.Unknown ? "" : " · fremdes Gebiet"), jc, 14, TextAnchor.MiddleRight);
        }

        static void Notes()
        {
            float y = 54;
            foreach (var n in Notifier.Active)
            {
                float age = Time.unscaledTime - n.Time, a = Mathf.Clamp01(Mathf.Min(age * 5f, (n.Duration - age) * 2f)); Color c = n.Kind == NoteKind.Bad ? Gui.Bad : n.Kind == NoteKind.Warn ? Gui.Warn : n.Kind == NoteKind.Good ? Gui.Good : n.Kind == NoteKind.Radio ? Gui.Accent : Gui.Text;
                float h = n.Text.Length > 70 ? 46 : 28; var r = new Rect(Gui.Width / 2 - 330, y, 660, h); var old = GUI.color; GUI.color = new Color(1, 1, 1, a);
                Gui.Fill(r, new Color(0.04f, 0.05f, 0.08f, 0.78f * a)); Gui.Fill(new Rect(r.x, r.y, 4, r.height), c);
                Gui.Label(new Rect(r.x + 12, r.y, r.width - 18, r.height), n.Text, c, 14, TextAnchor.MiddleLeft, false, true); GUI.color = old; y += h + 4;
            }
        }

        static void Prompts(GameSession s)
        {
            string p = s.Enforcement != null ? s.Enforcement.Prompt : "";
            if (string.IsNullOrEmpty(p)) return;
            var r = new Rect(Gui.Width / 2 - 220, Gui.Height * 0.66f, 440, 36); Gui.Panel(r, new Color(0.04f, 0.05f, 0.08f, 0.85f)); Gui.Label(r, p, Gui.Text, 16, TextAnchor.MiddleCenter, true);
        }

        static void IncidentPanel(GameSession s)
        {
            var ic = s.Incidents; if (ic == null) return;
            if (ic.Offered != null)
            {
                var o = ic.Offered; var r = new Rect(16, Gui.Height * 0.30f, 430, 190); Gui.Panel(r, Gui.Bg); Gui.Fill(new Rect(r.x, r.y, r.width, 4), o.Urgency >= 3 ? Gui.Bad : Gui.Warn);
                Gui.Label(new Rect(r.x + 14, r.y + 8, r.width - 28, 26), "Funkspruch · " + o.TypeLabel + (o.Urgency >= 3 ? " (dringend)" : ""), o.Urgency >= 3 ? Gui.Bad : Gui.Warn, 17, TextAnchor.MiddleLeft, true);
                Gui.Label(new Rect(r.x + 14, r.y + 36, r.width - 28, 90), o.RadioText, Gui.Text, 14, TextAnchor.UpperLeft, false, true);
                Gui.Label(new Rect(r.x + 14, r.y + 128, r.width - 28, 20), $"{o.Address}, {o.Municipality} · noch {Mathf.CeilToInt(ic.OfferSecondsLeft)} s", Gui.TextDim, 13);
                if (Gui.Button(new Rect(r.x + 14, r.y + 152, 190, 30), "Annehmen  [Enter]", true, Gui.Good) || GameInput.Accept()) ic.Accept();
                else if (Gui.Button(new Rect(r.x + 214, r.y + 152, 200, 30), "Ablehnen  [Rück]", true, Gui.AccentDim) || GameInput.Decline()) ic.Decline();
            }
            else if (ic.Active != null)
            {
                var a = ic.Active; var r = new Rect(16, 54, 360, 74); Gui.Panel(r, new Color(0.04f, 0.05f, 0.08f, 0.82f));
                Gui.Label(new Rect(r.x + 12, r.y + 4, r.width - 24, 24), a.TypeLabel, Gui.Warn, 15, TextAnchor.MiddleLeft, true);
                string st = a.State == IncidentState.OnScene ? "Vor Ort – [P] Rapport · Dienstfunk [T]" : a.State == IncidentState.EnRoute || a.State == IncidentState.Accepted ? "Anfahrt: " + a.Address + ", " + a.Municipality : a.State.ToString();
                Gui.Label(new Rect(r.x + 12, r.y + 28, r.width - 24, 40), st, Gui.Text, 13, TextAnchor.UpperLeft, false, true);
            }
        }

        static void Minimap(GameSession s)
        {
            float size = MinimapBig ? 360 : 220; var r = new Rect(Gui.Width - size - 16, 54, size, size);
            Gui.Panel(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), Gui.Bg);
            double mpp = MinimapBig ? 2.2 : 1.6; MapDraw.Draw(r, s.PlayerLv95.E, s.PlayerLv95.N, mpp);
            MapDraw.Area = r; MapDraw.CentreE = s.PlayerLv95.E; MapDraw.CentreN = s.PlayerLv95.N; MapDraw.MetersPerPixel = mpp;
            GUI.BeginClip(r, Vector2.zero, Vector2.zero, false); var saved = MapDraw.Area; MapDraw.Area = new Rect(0, 0, r.width, r.height);
            if (s.Nav != null && s.Nav.Route != null) { var rt = s.Nav.Route; Vector2 prev = default; bool has = false; for (int i = 0; i < rt.Count; i += Math.Max(1, rt.Count / 400)) { var p = MapDraw.ToScreen(rt.E[i], rt.N[i]); if (has && (MapDraw.InView(p, 60) || MapDraw.InView(prev, 60))) Gui.Line2D(prev, p, 4f, Gui.Accent); prev = p; has = true; } }
            var inc = s.Incidents != null ? (s.Incidents.Active ?? s.Incidents.Offered) : null; if (inc != null) { var p = MapDraw.ToScreen(inc.E, inc.N); p = ClampTo(p, MapDraw.Area); Gui.Disc(p, 7, Gui.Bad); Gui.RingAt(p, 11, Color.white); }
            var c = new Vector2(r.width / 2, r.height / 2); float yaw = s.Player != null ? s.Player.FocusTransform.eulerAngles.y : 0; Gui.RotatedTexture(new Rect(c.x - 12, c.y - 12, 24, 24), Gui.Arrow, yaw, Color.white);
            MapDraw.Area = saved; GUI.EndClip();
        }

        static Vector2 ClampTo(Vector2 p, Rect a) => new Vector2(Mathf.Clamp(p.x, 8, a.width - 8), Mathf.Clamp(p.y, 8, a.height - 8));

        static void Nav(GameSession s)
        {
            var nav = s.Nav; if (nav == null) return;
            if (nav.Computing) { var r0 = new Rect(Gui.Width / 2 - 160, Gui.Height - 150, 320, 34); Gui.Panel(r0); Gui.Label(r0, "Route wird berechnet …", Gui.Text, 15, TextAnchor.MiddleCenter); return; }
            if (!nav.Active || nav.Next == null) return;
            var r = new Rect(Gui.Width / 2 - 270, Gui.Height - 170, 540, 66); Gui.Panel(r, new Color(0.05f, 0.18f, 0.35f, 0.94f));
            Gui.Label(new Rect(r.x + 16, r.y + 6, 120, r.height - 12), nav.DistToNext >= 1000 ? (nav.DistToNext / 1000).ToString("0.0") + " km" : (Mathf.Round((float)nav.DistToNext / 10) * 10) + " m", Gui.Text, 26, TextAnchor.MiddleLeft, true);
            Gui.Label(new Rect(r.x + 140, r.y + 6, r.width - 150, 30), nav.Next.Text, Gui.Text, 17, TextAnchor.MiddleLeft, true);
            Gui.Label(new Rect(r.x + 140, r.y + 34, r.width - 150, 24), $"Ziel: {nav.DestinationName} · {(nav.RemainingM / 1000):0.0} km · {Mathf.CeilToInt(nav.EtaSeconds / 60)} min", Gui.TextDim, 13);
        }

        static void Speedo(GameSession s)
        {
            if (s.Car == null || s.Player == null) return; bool veh = s.Player.InVehicle; float v = veh ? s.Car.SpeedKmh : 0;
            var r = new Rect(Gui.Width - 270, Gui.Height - 150, 254, 130); Gui.Panel(r, new Color(0.04f, 0.05f, 0.08f, 0.85f));
            if (veh)
            {
                bool over = s.SpeedLimitKmh > 0 && v > s.SpeedLimitKmh + 2;
                Gui.Label(new Rect(r.x + 10, r.y + 6, 150, 62), Mathf.RoundToInt(v).ToString(), over ? Gui.Bad : Gui.Text, 52, TextAnchor.MiddleRight, true); Gui.Label(new Rect(r.x + 10, r.y + 64, 150, 20), "km/h", Gui.TextDim, 14, TextAnchor.MiddleRight);
                if (s.SpeedLimitKmh > 0) Gui.SpeedSign(new Vector2(r.x + 206, r.y + 48), 36, s.SpeedLimitKmh);
                Gui.Label(new Rect(r.x + 10, r.y + 88, 234, 20), string.IsNullOrEmpty(s.StreetName) ? "" : s.StreetName, Gui.TextDim, 13, TextAnchor.MiddleLeft);
                Gui.Bar(new Rect(r.x + 10, r.y + 112, 110, 8), s.Car.Fuel01, s.Car.Fuel01 < 0.15f ? Gui.Bad : Gui.Good); Gui.Bar(new Rect(r.x + 134, r.y + 112, 110, 8), 1 - s.Car.Damage01, s.Car.Damage01 > 0.6f ? Gui.Bad : Gui.Accent);
            }
            else
            {
                Gui.Label(new Rect(r.x + 10, r.y + 10, 234, 24), "Zu Fuss", Gui.Text, 18, TextAnchor.MiddleLeft, true);
                Gui.Label(new Rect(r.x + 10, r.y + 36, 234, 90), "[E] ansprechen   [F] einsteigen\n[R] Radarpistole   [G] Anhalten\n[Tab] Polizeicomputer   [M] Karte", Gui.TextDim, 13, TextAnchor.UpperLeft, false, true);
            }
        }

        static void Pursuit(GameSession s)
        {
            if (s.Pursuit == null || !s.Pursuit.IsActive) return; var r = new Rect(Gui.Width / 2 - 200, Gui.Height - 100, 400, 56); Gui.Panel(r, new Color(0.35f, 0.04f, 0.04f, 0.92f));
            Gui.Label(new Rect(r.x + 12, r.y + 4, r.width - 24, 26), "VERFOLGUNG · Verhältnismässigkeit beachten", Color.white, 16, TextAnchor.MiddleLeft, true);
            Gui.Label(new Rect(r.x + 12, r.y + 30, 190, 20), "Risiko", Gui.TextDim, 13); Gui.Bar(new Rect(r.x + 70, r.y + 36, 316, 8), Mathf.Clamp01(s.Pursuit.RiskScore), Gui.Warn);
        }

        static void Radar(GameSession s)
        {
            var e = s.Enforcement; if (e == null) return;
            if (e.RadarActive) { var c = new Vector2(Gui.Width / 2, Gui.Height / 2); Gui.RingAt(c, 18, new Color(1, 1, 1, 0.75f)); Gui.Disc(c, 2, Gui.Bad); }
            if (e.Last != null && Time.time - e.Last.Time < 25f)
            {
                var m = e.Last; var r = new Rect(Gui.Width / 2 - 150, Gui.Height * 0.12f, 300, 84); Gui.Panel(r, new Color(0.03f, 0.04f, 0.06f, 0.92f)); bool over = m.Kmh > m.Limit;
                Gui.Label(new Rect(r.x + 10, r.y + 6, 150, 50), Mathf.RoundToInt(m.Kmh).ToString(), over ? Gui.Bad : Gui.Good, 40, TextAnchor.MiddleRight, true); Gui.Label(new Rect(r.x + 162, r.y + 18, 60, 22), "km/h", Gui.TextDim, 14);
                Gui.Label(new Rect(r.x + 10, r.y + 54, r.width - 20, 24), $"{m.Plate} · Limite {m.Limit}" + (m.Fine != null && m.Fine.IsViolation ? " · " + m.Fine.Text : ""), Gui.Text, 13, TextAnchor.MiddleLeft);
            }
        }
    }
}
