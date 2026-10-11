using System;
using System.Collections.Generic;
using System.Linq;
using SwissPoliceSim.Core.Game;
using SwissPoliceSim.Core.Features;
using SwissPoliceSim.Core.Legal;
using SwissPoliceSim.Core.Npc;
using SwissPoliceSim.Core.Police;
using SwissPoliceSim.Game;
using SwissPoliceSim.Player;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.UI
{
    public enum Win { None, Encounter, Computer, Map, Report, Pause, Help, Career, Notebook }

    /// <summary>All modal windows. Opening one releases the cursor and suspends gameplay input (UiState).</summary>
    public static class Windows
    {
        public static Win Current;
        static GameSession S => GameSession.I;
        public static VoiceService Voice; public static LlmService Llm;

        public static void Open(Win w) { if (Current == Win.None) UiState.Open(); Current = w; GameInput.TextInputActive = false; GUI.FocusControl(null); }
        public static void Close() { if (Current != Win.None) { Current = Win.None; UiState.Close(); GameInput.TextInputActive = false; GUI.FocusControl(null); } }
        public static void Toggle(Win w) { if (Current == w) Close(); else if (Current == Win.None) Open(w); }

        public static void Draw()
        {
            if (S == null || !S.Playing && Current != Win.Pause) return;
            switch (Current)
            {
                case Win.Encounter: EncounterWin(); break; case Win.Computer: ComputerWin(); break; case Win.Map: MapWin(); break; case Win.Report: ReportWin(); break;
                case Win.Pause: PauseWin(); break; case Win.Help: HelpWin(); break; case Win.Career: CareerWin(); break; case Win.Notebook: NotebookWin(); break;
            }
            TrackTextFocus();
        }

        static void TrackTextFocus() { string f = GUI.GetNameOfFocusedControl(); GameInput.TextInputActive = !string.IsNullOrEmpty(f) && f.StartsWith("txt_"); }

        // ============================================================ Encounter
        static Vector2 _tscroll; static string _say = ""; static int _lastLines; static int _fine = 0; static int _fineFor = -1;
        static readonly string[] Quick =
        {
            "Guten Tag. Allgemeine Verkehrskontrolle. Ihre Papiere bitte.", "Wissen Sie, weshalb ich Sie angehalten habe?", "Haben Sie heute Alkohol getrunken?", "Bitte steigen Sie aus dem Fahrzeug.",
            "Ich muss Sie wegen der Geschwindigkeit büssen.", "Danke für Ihre Kooperation. Gute Fahrt.", "Beruhigen Sie sich bitte, ich erkläre Ihnen alles.", "Was ist passiert?"
        };

        static void EncounterWin()
        {
            var ec = S.Encounters; var e = ec.Current; if (e == null) { Close(); return; }
            var r = Gui.Centered(1060, 660); Gui.Window(r, (e.Kind == EncounterKind.TrafficStop ? "Verkehrskontrolle" : "Personenkontrolle") + " – " + e.Person.FormalAddress(e.Language));
            // transcript
            var tr = new Rect(r.x + 14, r.y + 50, 600, 360); Gui.Panel(tr, Gui.Bg2); float lh = 0; var content = new Rect(0, 0, tr.width - 26, 4000);
            float y = 6; foreach (var l in e.Transcript) { float h = Gui.Wrap.CalcHeight(new GUIContent(l.Text), tr.width - 60) + 6; y += h; } content.height = Mathf.Max(tr.height, y + 8); lh = y;
            if (e.Transcript.Count != _lastLines) { _lastLines = e.Transcript.Count; _tscroll.y = 1e6f; }
            _tscroll = GUI.BeginScrollView(tr, _tscroll, content); y = 6;
            foreach (var l in e.Transcript)
            {
                float h = Gui.Wrap.CalcHeight(new GUIContent(l.Text), tr.width - 60) + 6; Color c = l.IsWarning ? Gui.Warn : l.IsSystem ? Gui.TextDim : l.IsOfficer ? Gui.Accent : Gui.Text;
                string who = l.IsSystem ? "" : (l.IsOfficer ? "Sie: " : e.Person.FirstName + ": "); Gui.Label(new Rect(10, y, tr.width - 40, h), who + l.Text, c, 15, TextAnchor.UpperLeft, !l.IsSystem && !l.IsOfficer ? false : false, true); y += h;
            }
            GUI.EndScrollView();
            // input
            GUI.SetNextControlName("txt_say"); _say = Gui.TextField(new Rect(tr.x, tr.yMax + 8, 470, 32), _say, "txt_say");
            bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "txt_say";
            if ((Gui.Button(new Rect(tr.x + 478, tr.yMax + 8, 60, 32), "Sagen") || enter) && !string.IsNullOrWhiteSpace(_say)) { ec.Say(_say.Trim()); _say = ""; if (enter) Event.current.Use(); }
            if (Voice != null && Voice.SttEnabled) { if (Gui.Button(new Rect(tr.x + 544, tr.yMax + 8, 56, 32), Voice.Listening ? "■" : "Mic", true, Voice.Listening ? Gui.Bad : Gui.AccentDim)) { if (Voice.Listening) Voice.StopListening(); else Voice.StartListening(e.Language); } if (!string.IsNullOrEmpty(Voice.LastHeard)) { _say = Voice.LastHeard; Voice.LastHeard = ""; } }
            // quick phrases
            float qx = tr.x, qy = tr.yMax + 48; Gui.Label(new Rect(qx, qy, 200, 18), "Schnellsätze", Gui.TextDim, 12);
            for (int i = 0; i < Quick.Length; i++) { var qr = new Rect(qx + (i % 2) * 302, qy + 20 + (i / 2) * 30, 298, 27); if (Gui.ButtonDim(qr, Quick[i].Length > 44 ? Quick[i].Substring(0, 42) + "…" : Quick[i])) ec.Say(Quick[i]); }
            // person card
            var pr = new Rect(r.x + 630, r.y + 50, 416, 190); Gui.Panel(pr, Gui.Bg2); var p = e.Person;
            Gui.Label(new Rect(pr.x + 12, pr.y + 6, pr.width - 24, 22), "Ausweis / Papiere", Gui.Accent, 14, TextAnchor.MiddleLeft, true);
            if (e.DocumentsChecked || e.LicenseShown)
            {
                string[] rows = { "Name: " + p.FullName, "Geb.: " + p.Birthdate + " · " + p.Nationality, "Wohnort: " + p.Address + ", " + p.Municipality, "Führerausweis Kat. " + p.LicenseCategory + " · gültig bis " + p.LicenseExpires, "Fahrzeug: " + p.Vehicle.Make + " " + p.Vehicle.Model + " · " + p.Vehicle.Plate, "Halter laut Ausweis: " + (string.IsNullOrEmpty(p.Vehicle.HolderName) ? p.FullName : p.Vehicle.HolderName) };
                for (int i = 0; i < rows.Length; i++) Gui.Label(new Rect(pr.x + 12, pr.y + 32 + i * 24, pr.width - 24, 22), rows[i], Gui.Text, 13);
            }
            else Gui.Label(new Rect(pr.x + 12, pr.y + 40, pr.width - 24, 100), "Noch keine Papiere kontrolliert.", Gui.TextDim, 14, TextAnchor.UpperLeft, false, true);
            // mood
            Gui.Label(new Rect(pr.x + 12, pr.yMax - 28, 70, 20), "Stimmung", Gui.TextDim, 12); Gui.Bar(new Rect(pr.x + 80, pr.yMax - 22, 320, 8), e.Mind.Anger, e.Mind.Anger > 0.6f ? Gui.Bad : Gui.Warn);
            // actions
            float ax = r.x + 630, ay = r.y + 252; float bw = 204, bh = 30, g = 6; int k = 0;
            Func<string, bool, bool> B = (t, en) => { var br = new Rect(ax + (k % 2) * (bw + 8), ay + (k / 2) * (bh + g), bw, bh); k++; return Gui.Button(br, t, en); };
            if (B("Grund erklären", true)) ec.Reason(); if (B("Papiere verlangen", true)) ec.Docs();
            if (B("Atemalkoholtest", true)) ec.Breath(); if (B("Aussteigen lassen", e.Kind == EncounterKind.TrafficStop)) ec.StepOut();
            if (B("Person durchsuchen", true)) ec.SearchPerson(); if (B("Fahrzeug durchsuchen", e.Kind == EncounterKind.TrafficStop)) ec.SearchVehicle();
            if (B("Computer: Kennzeichen", true)) ec.QueryPlate(); if (B("Computer: Person", true)) ec.QueryPerson();
            if (B("Rechte eröffnen", true)) ec.Rights(); if (B("Verwarnen", true)) ec.Warn();
            if (B("Fesseln", true)) ec.Cuff(); if (B("Festnehmen", true)) ec.Arrest();
            if (B("Verzeigen (Rapport)", true)) ec.Report(); if (B("Gehen lassen", true)) { ec.Release(); }
            // fine
            if (_fineFor != e.Person.Seed) { _fineFor = e.Person.Seed; _fine = ec.ExpectedFine(); }
            float fy = ay + 7 * (bh + g) + 8; Gui.Label(new Rect(ax, fy, 110, 30), "Busse CHF", Gui.TextDim, 14); if (Gui.ButtonDim(new Rect(ax + 92, fy, 30, 30), "−")) _fine = Mathf.Max(0, _fine - 10); Gui.Label(new Rect(ax + 124, fy, 54, 30), _fine.ToString(), Gui.Text, 16, TextAnchor.MiddleCenter, true); if (Gui.ButtonDim(new Rect(ax + 180, fy, 30, 30), "+")) _fine += 10;
            if (Gui.Button(new Rect(ax + 216, fy, 190, 30), "Ordnungsbusse ausstellen", _fine > 0, Gui.Good)) ec.Fine(_fine);
            if (Gui.Button(new Rect(r.xMax - 224, r.yMax - 44, 210, 32), "Kontakt beenden", true, Gui.Orange)) { ec.End(); Close(); }
            if (Gui.Button(new Rect(r.x + 14, r.yMax - 44, 150, 32), "Fenster ausblenden")) { ec.WindowRequested = false; Close(); }
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape && string.IsNullOrEmpty(GUI.GetNameOfFocusedControl())) { ec.WindowRequested = false; Close(); }
        }

        // ============================================================ Computer
        static int _ctab; static string _plate = "", _fn = "", _ln = "", _bd = ""; static int _reason; static QueryResult _qres; static Vector2 _cscroll;
        static readonly string[] Reasons = { "Verkehrskontrolle / Verdacht auf Verkehrsdelikt", "Verdacht auf Straftat", "Fahndung / Vermisstensuche", "Unfallaufnahme", "Aus privatem Interesse (kein Dienstzweck)" };
        static void ComputerWin()
        {
            var r = Gui.Centered(900, 620); Gui.Window(r, "Polizeicomputer – Abfragen sind protokolliert (Zweckbindung)");
            string[] tabs = { "Kennzeichen", "Person", "Protokoll" }; for (int i = 0; i < 3; i++) if (Gui.Button(new Rect(r.x + 14 + i * 150, r.y + 48, 144, 30), tabs[i], true, i == _ctab ? Gui.Accent : Gui.AccentDim)) _ctab = i;
            if (_ctab < 2)
            {
                float y = r.y + 92;
                if (_ctab == 0) { Gui.Label(new Rect(r.x + 14, y, 120, 30), "Kennzeichen", Gui.TextDim, 14); GUI.SetNextControlName("txt_plate"); _plate = Gui.TextField(new Rect(r.x + 140, y, 220, 30), _plate, "txt_plate"); }
                else { Gui.Label(new Rect(r.x + 14, y, 80, 30), "Vorname", Gui.TextDim, 14); _fn = Gui.TextField(new Rect(r.x + 90, y, 180, 30), _fn, "txt_fn"); Gui.Label(new Rect(r.x + 280, y, 80, 30), "Nachname", Gui.TextDim, 14); _ln = Gui.TextField(new Rect(r.x + 360, y, 200, 30), _ln, "txt_ln"); Gui.Label(new Rect(r.x + 570, y, 90, 30), "Geb. (opt.)", Gui.TextDim, 14); _bd = Gui.TextField(new Rect(r.x + 660, y, 150, 30), _bd, "txt_bd"); }
                y += 40; Gui.Label(new Rect(r.x + 14, y, 200, 22), "Abfragegrund (rechtlich relevant)", Gui.TextDim, 13);
                for (int i = 0; i < Reasons.Length; i++) if (Gui.Button(new Rect(r.x + 14 + (i % 2) * 436, y + 24 + (i / 2) * 34, 430, 30), Reasons[i], true, i == _reason ? Gui.Accent : Gui.AccentDim)) _reason = i;
                y += 24 + 3 * 34 + 6;
                if (Gui.Button(new Rect(r.x + 14, y, 200, 34), "Abfrage ausführen", true, Gui.Good))
                {
                    string reason = _reason == Reasons.Length - 1 ? "" : Reasons[_reason]; var ctx = new LegalContext { Suspicion = _reason == 1 ? Suspicion.Concrete : _reason < 4 ? Suspicion.Routine : Suspicion.None };
                    _qres = _ctab == 0 ? S.Computer.QueryPlate(_plate, reason, ctx) : S.Computer.QueryPerson(_fn, _ln, _bd, reason, ctx);
                    if (_qres.Assessment != null) S.Judge(_qres.Assessment);
                    if (_qres.Found && _qres.Person != null && (_qres.Flags & QueryFlags.Wanted) != 0) Notifier.Warn("Treffer: Person zur Fahndung ausgeschrieben.");
                }
                var rr = new Rect(r.x + 14, y + 44, r.width - 28, r.yMax - y - 60); Gui.Panel(rr, Gui.Bg2);
                if (_qres != null) { var sb = new System.Text.StringBuilder(); sb.AppendLine(_qres.Title); sb.AppendLine(); foreach (var l in _qres.Lines) sb.AppendLine(l); if (_qres.Assessment != null && !_qres.Assessment.Lawful) { sb.AppendLine(); sb.AppendLine("RECHTLICH PROBLEMATISCH: " + string.Join("; ", _qres.Assessment.Reasons)); }
                    GUI.Label(new Rect(rr.x + 10, rr.y + 8, rr.width - 20, rr.height - 16), sb.ToString(), Gui.Mono); }
            }
            else
            {
                var rr = new Rect(r.x + 14, r.y + 90, r.width - 28, r.height - 150); Gui.Panel(rr, Gui.Bg2); var list = S.Computer.Log; var content = new Rect(0, 0, rr.width - 22, Mathf.Max(rr.height, list.Count * 26 + 10)); _cscroll = GUI.BeginScrollView(rr, _cscroll, content);
                for (int i = 0; i < list.Count; i++) { var q = list[list.Count - 1 - i]; Gui.Label(new Rect(8, 6 + i * 26, content.width - 10, 24), $"{TimeSpan.FromSeconds(q.GameSeconds):hh\\:mm\\:ss}  {q.Kind}: {q.Subject} · {(string.IsNullOrEmpty(q.Reason) ? "OHNE Zweck" : q.Reason)}", q.Lawful ? Gui.Text : Gui.Bad, 13); }
                GUI.EndScrollView();
            }
            if (Gui.Button(new Rect(r.xMax - 164, r.yMax - 44, 150, 32), "Schliessen")) Close();
        }

        // ============================================================ Map
        static double _mE, _mN; static double _mpp = 6; static bool _mInit, _drag; static string _search = ""; static List<PoiFeature> _hits = new List<PoiFeature>(); static Vector2 _lastMouse; static Vector2 _sscroll;
        static void MapWin()
        {
            var r = Gui.Centered(Mathf.Min(1500, Gui.Width - 40), Mathf.Min(900, Gui.Height - 40)); Gui.Window(r, "Karte & Navigation");
            if (!_mInit) { _mE = S.PlayerLv95.E; _mN = S.PlayerLv95.N; _mInit = true; }
            var side = new Rect(r.x + 12, r.y + 48, 300, r.height - 60); var map = new Rect(side.xMax + 10, r.y + 48, r.width - 334, r.height - 60);
            Gui.Panel(side, Gui.Bg2);
            Gui.Label(new Rect(side.x + 10, side.y + 6, 280, 20), "Suche (Ort, Station, Spital …)", Gui.TextDim, 12);
            string ns = Gui.TextField(new Rect(side.x + 10, side.y + 28, 280, 30), _search, "txt_search"); bool enter = Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return && GUI.GetNameOfFocusedControl() == "txt_search";
            if (ns != _search || enter) { _search = ns; _hits = WorldData.Pois != null && _search.Length >= 2 ? WorldData.Pois.Search(_search, 40) : new List<PoiFeature>(); }
            var lr = new Rect(side.x + 6, side.y + 64, 288, side.height - 230); var c = new Rect(0, 0, lr.width - 18, Mathf.Max(lr.height, _hits.Count * 34)); _sscroll = GUI.BeginScrollView(lr, _sscroll, c);
            for (int i = 0; i < _hits.Count; i++) { var h = _hits[i]; if (Gui.ButtonDim(new Rect(2, i * 34, c.width - 4, 31), Short(h.Name + " · " + KindLabel(h.Kind), 36))) { _mE = h.E; _mN = h.N; _mpp = 3; _sel = h; } }
            GUI.EndScrollView();
            float by = side.yMax - 158; Gui.Label(new Rect(side.x + 10, by, 280, 40), _sel != null ? Short(_sel.Name, 34) + "\n" + KindLabel(_sel.Kind) : (_hasPin ? "Gesetzter Punkt" : "Rechtsklick/Doppelklick: Ziel setzen"), Gui.Text, 14, TextAnchor.UpperLeft, false, true);
            double tE = _sel != null ? _sel.E : _pinE, tN = _sel != null ? _sel.N : _pinN; string tn = _sel != null ? _sel.Name : "Kartenpunkt"; bool can = _sel != null || _hasPin;
            if (Gui.Button(new Rect(side.x + 10, by + 46, 280, 32), "Route dorthin starten", can, Gui.Good)) { S.Nav.SetDestination(tE, tN, tn); Close(); }
            if (Gui.ButtonDim(new Rect(side.x + 10, by + 84, 136, 30), "Zu mir")) { _mE = S.PlayerLv95.E; _mN = S.PlayerLv95.N; }
            if (Gui.ButtonDim(new Rect(side.x + 154, by + 84, 136, 30), "Route löschen", S.Nav.Active)) S.Nav.Clear();
            if (Gui.ButtonDim(new Rect(side.x + 10, by + 120, 136, 30), "Nächste Polizei")) { var p = WorldData.Pois?.Nearest(S.PlayerLv95.E, S.PlayerLv95.N, PoiKind.Police); if (p != null) { _sel = p; _mE = p.E; _mN = p.N; _mpp = 3; } }
            if (Gui.ButtonDim(new Rect(side.x + 154, by + 120, 136, 30), "Nächstes Spital")) { var p = WorldData.Pois?.Nearest(S.PlayerLv95.E, S.PlayerLv95.N, PoiKind.Hospital); if (p != null) { _sel = p; _mE = p.E; _mN = p.N; _mpp = 3; } }
            // map interaction
            Gui.Panel(new Rect(map.x - 2, map.y - 2, map.width + 4, map.height + 4), Gui.Bg);
            var ev = Event.current; bool inMap = map.Contains(ev.mousePosition);
            if (ev.type == EventType.ScrollWheel && inMap) { _mpp = Math.Max(0.8, Math.Min(900, _mpp * (ev.delta.y > 0 ? 1.18 : 1 / 1.18))); ev.Use(); }
            if (ev.type == EventType.MouseDown && inMap && ev.button == 0) { _drag = true; _lastMouse = ev.mousePosition; if (ev.clickCount == 2) SetPin(map, ev.mousePosition); }
            if (ev.type == EventType.MouseDown && inMap && ev.button == 1) { SetPin(map, ev.mousePosition); ev.Use(); }
            if (ev.type == EventType.MouseUp) _drag = false;
            if (ev.type == EventType.MouseDrag && _drag) { _mE -= (ev.mousePosition.x - _lastMouse.x) * _mpp; _mN += (ev.mousePosition.y - _lastMouse.y) * _mpp; _lastMouse = ev.mousePosition; ev.Use(); }
            MapDraw.Draw(map, _mE, _mN, _mpp);
            MapDraw.Area = map; MapDraw.CentreE = _mE; MapDraw.CentreN = _mN; MapDraw.MetersPerPixel = _mpp; GUI.BeginClip(map); var saved = MapDraw.Area; var shift = new Vector2(map.x, map.y); MapDraw.Area = new Rect(0, 0, map.width, map.height);
            if (WorldData.Pois != null && _mpp < 40) foreach (var p in WorldData.Pois.Within(_mE, _mN, Math.Max(map.width, map.height) * _mpp * 0.6, null)) { if (p.Kind == PoiKind.Parking && _mpp > 6) continue; var sp = MapDraw.ToScreen(p.E, p.N); if (!MapDraw.InView(sp, 10)) continue; Gui.Disc(sp, 6, PoiColor(p.Kind)); Gui.RingAt(sp, 7.5f, Color.white); if (_mpp < 6 && p.Kind != PoiKind.Parking) Gui.Label(new Rect(sp.x + 9, sp.y - 9, 180, 18), p.Name, Gui.Text, 11); }
            if (S.Nav.Route != null) { var rt = S.Nav.Route; Vector2 prev = default; bool has = false; int step = Math.Max(1, rt.Count / 1200); for (int i = 0; i < rt.Count; i += step) { var sp = MapDraw.ToScreen(rt.E[i], rt.N[i]); if (has) Gui.Line2D(prev, sp, 5f, Gui.Accent); prev = sp; has = true; } }
            var inc = S.Incidents != null ? (S.Incidents.Active ?? S.Incidents.Offered) : null; if (inc != null) { var sp = MapDraw.ToScreen(inc.E, inc.N); Gui.Disc(sp, 9, Gui.Bad); Gui.RingAt(sp, 13, Color.white); Gui.Label(new Rect(sp.x + 14, sp.y - 10, 220, 20), inc.TypeLabel, Gui.Bad, 12, TextAnchor.MiddleLeft, true); }
            if (_hasPin) { var sp = MapDraw.ToScreen(_pinE, _pinN); Gui.Disc(sp, 7, Gui.Orange); Gui.RingAt(sp, 11, Color.white); }
            var me = MapDraw.ToScreen(S.PlayerLv95.E, S.PlayerLv95.N); Gui.RotatedTexture(new Rect(me.x - 12, me.y - 12, 24, 24), Gui.Arrow, S.Player.FocusTransform.eulerAngles.y, Color.white);
            MapDraw.Area = saved; GUI.EndClip();
            Gui.Label(new Rect(map.x + 10, map.yMax - 28, 460, 24), $"Massstab ≈ {_mpp * 100:0} m je 100 px · Mausrad zoomen · ziehen verschieben · Rechtsklick: Ziel", Gui.TextDim, 12);
            if (Gui.Button(new Rect(r.xMax - 124, r.y + 8, 110, 30), "Schliessen") || (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape && string.IsNullOrEmpty(GUI.GetNameOfFocusedControl()))) Close();
        }
        static PoiFeature _sel; static bool _hasPin; static double _pinE, _pinN;
        static void SetPin(Rect map, Vector2 mouse) { var local = mouse - new Vector2(map.x, map.y); var keep = MapDraw.Area; MapDraw.Area = new Rect(0, 0, map.width, map.height); MapDraw.CentreE = _mE; MapDraw.CentreN = _mN; MapDraw.MetersPerPixel = _mpp; MapDraw.ToWorld(local, out _pinE, out _pinN); MapDraw.Area = keep; _hasPin = true; _sel = null; }
        static string Short(string s, int n) => s.Length > n ? s.Substring(0, n - 1) + "…" : s;
        static string KindLabel(PoiKind k) { switch (k) { case PoiKind.Police: return "Polizei"; case PoiKind.Fuel: return "Tankstelle"; case PoiKind.Hospital: return "Spital"; case PoiKind.FireStation: return "Feuerwehr"; case PoiKind.Station: return "Bahnhof"; case PoiKind.Parking: return "Parkplatz"; default: return "Ort"; } }
        static Color PoiColor(PoiKind k) { switch (k) { case PoiKind.Police: return Gui.Accent; case PoiKind.Fuel: return Gui.Warn; case PoiKind.Hospital: return Gui.Bad; case PoiKind.FireStation: return Gui.Orange; case PoiKind.Station: return new Color(0.7f, 0.2f, 0.2f); case PoiKind.Parking: return Gui.TextDim; default: return Gui.Good; } }

        // ============================================================ Report
        static int _rkind; static int _atFault = -1; static bool _inj, _tow, _amb; static string _narr = ""; static Vector2 _rscroll;
        static void ReportWin()
        {
            var inc = S.Incidents != null ? S.Incidents.Active : null; var r = Gui.Centered(860, 640); Gui.Window(r, "Rapport");
            if (inc == null || inc.State != IncidentState.OnScene) { Gui.Label(new Rect(r.x + 20, r.y + 70, r.width - 40, 80), "Es gibt keinen Einsatz vor Ort, für den ein Rapport zu verfassen wäre. Kontrollen werden direkt im Kontaktfenster verzeigt.", Gui.Text, 16, TextAnchor.UpperLeft, false, true); if (Gui.Button(new Rect(r.xMax - 164, r.yMax - 44, 150, 32), "Schliessen")) Close(); return; }
            Gui.Label(new Rect(r.x + 20, r.y + 50, r.width - 40, 26), inc.TypeLabel + " · " + inc.Address + ", " + inc.Municipality, Gui.Warn, 16, TextAnchor.MiddleLeft, true);
            var roles = inc.Scenario.Roles; float y = r.y + 86;
            if (roles.Count > 0) { Gui.Label(new Rect(r.x + 20, y, 400, 22), "Hauptverantwortliche/r (laut Ihren Erkenntnissen)", Gui.TextDim, 13); y += 24; for (int i = 0; i < roles.Count; i++) { string nm = roles[i].Person != null ? roles[i].Person.FullName : "–"; if (Gui.Button(new Rect(r.x + 20 + (i % 3) * 270, y + (i / 3) * 34, 262, 30), roles[i].Role + ": " + Short(nm, 20), true, i == _atFault ? Gui.Accent : Gui.AccentDim)) _atFault = i; } y += ((roles.Count + 2) / 3) * 34 + 8; if (Gui.ButtonDim(new Rect(r.x + 20, y, 262, 28), "Niemand / unklar")) _atFault = -1; y += 36; }
            _inj = Gui.Toggle(new Rect(r.x + 20, y, 250, 26), _inj, "Verletzte festgehalten"); _amb = Gui.Toggle(new Rect(r.x + 280, y, 250, 26), _amb, "Rettungsdienst aufgeboten"); _tow = Gui.Toggle(new Rect(r.x + 540, y, 250, 26), _tow, "Abschleppdienst aufgeboten"); y += 40;
            Gui.Label(new Rect(r.x + 20, y, 400, 22), "Sachverhalt (Ihre Darstellung der Ereignisse)", Gui.TextDim, 13); y += 24;
            GUI.SetNextControlName("txt_narr"); _narr = GUI.TextArea(new Rect(r.x + 20, y, r.width - 40, 150), _narr, Gui.Wrap); y += 160;
            Gui.Label(new Rect(r.x + 20, y, r.width - 40, 40), "Wahrheitsgemäss, sachlich, vollständig – falsche Angaben im Rapport sind strafbar (Art. 317 StGB) und werden im Spiel erkannt.", Gui.TextDim, 12, TextAnchor.UpperLeft, false, true);
            bool ok = _narr.Trim().Length >= 25;
            if (Gui.Button(new Rect(r.xMax - 380, r.yMax - 44, 210, 32), "Rapport abgeben", ok, Gui.Good)) { SubmitReport(inc); Close(); }
            if (Gui.Button(new Rect(r.xMax - 164, r.yMax - 44, 150, 32), "Abbrechen")) Close();
        }

        static void SubmitReport(Incident inc)
        {
            var rep = new PoliceReport { Kind = inc.Scenario.AtFaultRole >= 0 || inc.Type == IncidentType.TrafficAccident || inc.Type == IncidentType.TrafficAccidentInjured ? ReportKind.Unfallrapport : ReportKind.Einsatzrapport, IncidentId = inc.Id, Narrative = _narr, AtFaultRole = _atFault, InjuredReported = _inj, TowCalled = _tow || S.Incidents.TowEnRoute, AmbulanceCalled = _amb || inc.AmbulanceCalled, CreatedAt = (float)S.Clock.ElapsedGameSeconds };
            ReportEvaluation ev;
            if (rep.Kind == ReportKind.Unfallrapport) ev = ReportEvaluator.EvaluateAccident(rep, inc.Scenario);
            else { ev = new ReportEvaluation { Correct = true, Score = 80 }; if (inc.Scenario.NeedsAmbulance && !rep.AmbulanceCalled) { ev.Correct = false; ev.Score = 40; ev.Findings.Add("Rettungsdienst wäre nötig gewesen."); } if (inc.Scenario.NeedsTow && !rep.TowCalled) { ev.Score -= 10; ev.Findings.Add("Abschleppdienst wäre nötig gewesen."); } if (ev.Findings.Count == 0) ev.Findings.Add("Rapport vollständig."); }
            foreach (var f in ev.Findings) Notifier.Push(f, ev.Correct ? NoteKind.Info : NoteKind.Warn, 9f);
            S.AddCase(rep.Kind == ReportKind.Unfallrapport ? "Unfall" : "Einsatz", inc.TypeLabel + " – " + inc.Address, "", "", 0, ev.Correct);
            if (ev.Falsified) S.Career.ApplyAssessment(new LegalAssessment { Lawful = false, Severity = 3, Headline = "Falsche Angaben im Rapport" }, 0.1f, true, (float)S.Clock.ElapsedGameSeconds);
            S.Incidents.Complete(Mathf.Clamp01(ev.Score / 100f), ev.Correct); _narr = ""; _atFault = -1; _inj = _tow = _amb = false;
        }

        // ============================================================ Pause / save / load
        public static bool MenuMode; static int _pmode;
        static void PauseWin()
        {
            var r = Gui.Centered(520, 540); Gui.Window(r, "Pause");
            float y = r.y + 54; if (Gui.Button(new Rect(r.x + 30, y, r.width - 60, 38), "Weiterspielen")) { S.Paused = false; Close(); } y += 46;
            Gui.Label(new Rect(r.x + 30, y, 300, 22), "Spielstände", Gui.TextDim, 13); y += 24;
            for (int i = 1; i <= 5; i++)
            {
                Gui.Label(new Rect(r.x + 30, y, 250, 34), $"{i}: {Short(GameSession.SlotInfo(i), 34)}", Gui.Text, 13); if (Gui.ButtonDim(new Rect(r.x + 300, y + 2, 90, 30), "Speichern")) S.Save(i); if (Gui.ButtonDim(new Rect(r.x + 396, y + 2, 94, 30), "Laden", GameSession.SlotExists(i))) { if (S.Load(i)) { S.Paused = false; Close(); } }
                y += 38;
            }
            y += 6; Gui.Label(new Rect(r.x + 30, y, 300, 22), "Zeit & Ton", Gui.TextDim, 13); y += 24;
            Gui.Label(new Rect(r.x + 30, y, 200, 28), "Zeitraffer ×" + S.Clock.TimeScale.ToString("0"), Gui.Text, 14); float ts = GUI.HorizontalSlider(new Rect(r.x + 230, y + 8, 250, 20), S.Clock.TimeScale, 1f, 60f); S.Clock.TimeScale = Mathf.Round(ts); y += 34;
            if (Voice != null) { Voice.TtsEnabled = Gui.Toggle(new Rect(r.x + 30, y, 220, 26), Voice.TtsEnabled, "Sprachausgabe (Windows)"); Voice.SttEnabled = Gui.Toggle(new Rect(r.x + 270, y, 220, 26), Voice.SttEnabled, "Spracheingabe (Mikro)"); y += 30; }
            if (Llm != null) { Llm.Enabled = Gui.Toggle(new Rect(r.x + 30, y, 440, 26), Llm.Enabled, "KI-Formulierung (ANTHROPIC_API_KEY): " + (Llm.Ready ? "bereit" : "kein Schlüssel")); }
            if (Gui.Button(new Rect(r.x + 30, r.yMax - 52, 210, 36), "Hilfe / Steuerung")) Open(Win.Help);
            if (Gui.Button(new Rect(r.xMax - 240, r.yMax - 52, 210, 36), "Spiel beenden", true, Gui.Bad)) Application.Quit();
        }

        // ============================================================ Help
        static void HelpWin()
        {
            var r = Gui.Centered(920, 640); Gui.Window(r, "Steuerung & Hinweise");
            string left = "ZU FUSS\nW A S D  gehen · Shift rennen · Leertaste springen\nMaus  umsehen · C  Kameraansicht · V  Ego-Ansicht\nF  Fahrzeug betreten/verlassen\nE  Person/Fahrer ansprechen\nR  Radarpistole (Geschwindigkeit messen)\nG  Fahrzeug anhalten (Anhaltezeichen)\n\nIM FAHRZEUG\nW/S  Gas/Bremse · A/D  lenken · Leertaste  Handbremse\nB  Blaulicht · Q  Horn/Sirene · H  Hupe · L  Licht\nZ/X  Blinker · J  Warnblinker";
            string right = "OBERFLÄCHE\nTab  Polizeicomputer · M  Karte/Navigation\nP  Rapport · N  Notizbuch · K  Laufbahn\nEnter/Rück  Funkauftrag annehmen/ablehnen\nF5/F9  Schnellspeichern/-laden · Esc  Pause\n\nGRUNDSÄTZE\nHandeln Sie rechtmässig und verhältnismässig. Jede Handlung\nwird gegen die hinterlegten Normen geprüft; Verstösse haben\nKonsequenzen von der Ermahnung bis zur Entlassung.\nGesetzestexte stammen aus Fedlex; kantonales Recht ist\nals ‹nicht verifiziert› gekennzeichnet.";
            Gui.Label(new Rect(r.x + 24, r.y + 56, 430, r.height - 120), left, Gui.Text, 15, TextAnchor.UpperLeft, false, true); Gui.Label(new Rect(r.x + 470, r.y + 56, 430, r.height - 120), right, Gui.Text, 15, TextAnchor.UpperLeft, false, true);
            if (Gui.Button(new Rect(r.xMax - 164, r.yMax - 44, 150, 32), "Zurück")) { if (S.Paused) Current = Win.Pause; else Close(); }
        }

        // ============================================================ Career
        static void CareerWin()
        {
            var r = Gui.Centered(860, 640); Gui.Window(r, "Laufbahn"); var c = S.Career; float y = r.y + 54;
            Gui.Label(new Rect(r.x + 24, y, 500, 30), $"{c.RankName(S.Corps)} · {S.Corps.Name}", Gui.Text, 22, TextAnchor.MiddleLeft, true); y += 34;
            double next = c.RankIndex + 1 < CareerState.XpForRank.Length ? CareerState.XpForRank[c.RankIndex + 1] : c.Xp; Gui.Label(new Rect(r.x + 24, y, 300, 22), $"Erfahrung {c.Xp:0} / {next:0}", Gui.TextDim, 13); Gui.Bar(new Rect(r.x + 24, y + 24, 400, 10), (float)Math.Min(1, c.Xp / Math.Max(1, next)), Gui.Accent);
            if (Gui.Button(new Rect(r.x + 450, y, 200, 34), "Beförderung beantragen", c.CanPromote, Gui.Good)) { if (c.TryPromote()) Notifier.Good("Befördert zum Rang: " + c.RankName(S.Corps)); }
            y += 52; string[] names = { "Rechtmässigkeit", "Professionalität", "Deeskalation", "Berichtswesen", "Bürgerkontakt" }; float[] v = { c.Legality, c.Professionalism, c.DeEscalation, c.Reporting, c.Community };
            for (int i = 0; i < 5; i++) { Gui.Label(new Rect(r.x + 24, y + i * 30, 180, 26), names[i], Gui.Text, 14); Gui.Bar(new Rect(r.x + 210, y + i * 30 + 8, 300, 10), v[i] / 100f, v[i] < 45 ? Gui.Bad : v[i] < 65 ? Gui.Warn : Gui.Good); Gui.Label(new Rect(r.x + 520, y + i * 30, 60, 26), v[i].ToString("0"), Gui.TextDim, 13); }
            Gui.Label(new Rect(r.x + 600, y, 240, 150), $"Beschwerden offen: {c.ComplaintsOpen}\nUntersuchungen: {c.InvestigationsOpen}\nDisziplinarisch: {c.DisciplinaryCount}\nBussen: {c.FinesIssued}\nFestnahmen: {c.ArrestsMade}", Gui.TextDim, 14, TextAnchor.UpperLeft, false, true);
            y += 168; Gui.Label(new Rect(r.x + 24, y, 400, 22), "Konsequenzen", Gui.TextDim, 13); var rr = new Rect(r.x + 24, y + 24, r.width - 48, r.yMax - y - 80); Gui.Panel(rr, Gui.Bg2);
            var log = S.ConsequenceLog; var cont = new Rect(0, 0, rr.width - 22, Mathf.Max(rr.height, log.Count * 24 + 10)); _cscroll = GUI.BeginScrollView(rr, _cscroll, cont); for (int i = 0; i < log.Count; i++) Gui.Label(new Rect(8, 6 + i * 24, cont.width - 10, 22), log[log.Count - 1 - i], Gui.Warn, 13); GUI.EndScrollView();
            if (Gui.Button(new Rect(r.xMax - 164, r.yMax - 44, 150, 32), "Schliessen")) Close();
        }

        // ============================================================ Notebook
        static void NotebookWin()
        {
            var r = Gui.Centered(900, 640); Gui.Window(r, "Notizbuch – Fälle & Funkverlauf"); var half = (r.width - 60) / 2;
            var a = new Rect(r.x + 20, r.y + 70, half, r.height - 130); var b = new Rect(a.xMax + 20, a.y, half, a.height); Gui.Label(new Rect(a.x, a.y - 22, 300, 20), "Fälle", Gui.TextDim, 13); Gui.Label(new Rect(b.x, b.y - 22, 300, 20), "Meldungen", Gui.TextDim, 13); Gui.Panel(a, Gui.Bg2); Gui.Panel(b, Gui.Bg2);
            var cases = S.Cases; var cc = new Rect(0, 0, a.width - 22, Mathf.Max(a.height, cases.Count * 44 + 10)); _cscroll = GUI.BeginScrollView(a, _cscroll, cc); for (int i = 0; i < cases.Count; i++) { var k = cases[cases.Count - 1 - i]; Gui.Label(new Rect(8, 6 + i * 44, cc.width - 10, 42), $"{k.Kind}: {k.Summary}" + (k.Correct ? "" : "  (Mängel)"), k.Correct ? Gui.Text : Gui.Warn, 13, TextAnchor.UpperLeft, false, true); } GUI.EndScrollView();
            var log = Notifier.Log; var lc = new Rect(0, 0, b.width - 22, Mathf.Max(b.height, log.Count * 40 + 10)); _sscroll = GUI.BeginScrollView(b, _sscroll, lc); for (int i = 0; i < log.Count; i++) Gui.Label(new Rect(8, 6 + i * 40, lc.width - 10, 38), log[log.Count - 1 - i], Gui.TextDim, 12, TextAnchor.UpperLeft, false, true); GUI.EndScrollView();
            if (Gui.Button(new Rect(r.xMax - 164, r.yMax - 44, 150, 32), "Schliessen")) Close();
        }
    }
}
