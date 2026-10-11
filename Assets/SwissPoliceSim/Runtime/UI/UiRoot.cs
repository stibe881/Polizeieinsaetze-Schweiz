using System;
using System.Linq;
using SwissPoliceSim.Core.Police;
using SwissPoliceSim.Game;
using SwissPoliceSim.Player;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.UI
{
    /// <summary>Immediate-mode root: menu, loading, HUD, windows, suspension and dismissal screens, and the global key bindings.</summary>
    public sealed class UiRoot : MonoBehaviour
    {
        GameBootstrap B => GameBootstrap.Instance; GameSession S => GameSession.I;
        Vector2 _menuScroll; int _menuSeed = 1; bool _menuMunicipal; float _fade;

        void Update()
        {
            var b = B; if (b == null) return;
            if (b.Phase == BootPhase.Menu || b.Phase == BootPhase.Loading || b.Phase == BootPhase.Starting) { if (Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } return; }
            var s = S; if (s == null || !s.Playing) return;
            // encounter window opens by itself when contact begins
            if (s.Encounters != null && s.Encounters.WindowRequested && Windows.Current != Win.Encounter) { Windows.Open(Win.Encounter); s.Encounters.WindowRequested = false; }
            if (Windows.Current == Win.None && !GameInput.TextInputActive)
            {
                if (GameInput.Computer()) Windows.Open(Win.Computer); else if (GameInput.Map()) Windows.Open(Win.Map); else if (GameInput.Report()) Windows.Open(Win.Report); else if (GameInput.Notebook()) Windows.Open(Win.Notebook);
                else if (GameInput.Help()) Windows.Open(Win.Help); else if (GameInput.Pressed(KeyCode.K)) Windows.Open(Win.Career);
                else if (GameInput.Escape()) { s.Paused = true; Windows.Open(Win.Pause); }
                else if (GameInput.Pressed(KeyCode.F8)) Hud.MinimapBig = !Hud.MinimapBig;
                else if (s.Encounters.Current != null && GameInput.Talk()) Windows.Open(Win.Encounter);
                if (GameInput.QuickSave()) s.Save(1); if (GameInput.QuickLoad() && GameSession.SlotExists(1)) s.Load(1);
            }
            else if (!GameInput.TextInputActive)
            {
                if ((Windows.Current == Win.Computer && GameInput.Computer()) || (Windows.Current == Win.Map && GameInput.Map()) || (Windows.Current == Win.Notebook && GameInput.Notebook()) || (Windows.Current == Win.Career && GameInput.Pressed(KeyCode.K))) Windows.Close();
                else if (GameInput.Escape() && Windows.Current != Win.Encounter && Windows.Current != Win.Map) { if (Windows.Current == Win.Pause) { s.Paused = false; Windows.Close(); } else if (Windows.Current == Win.Help && s.Paused) Windows.Open(Win.Pause); else Windows.Close(); }
            }
            Time.timeScale = s.Paused ? 0f : 1f;
        }

        void OnGUI()
        {
            var b = B; if (b == null) return; Gui.Begin();
            try
            {
                switch (b.Phase)
                {
                    case BootPhase.Loading: Loading(b); break;
                    case BootPhase.Menu: Menu(b); break;
                    case BootPhase.Starting: Starting(b); break;
                    case BootPhase.Playing: Playing(b); break;
                    case BootPhase.GameOver: GameOver(b); break;
                }
            }
            finally { Gui.End(); }
        }

        void Backdrop(string sub)
        {
            Gui.Fill(new Rect(0, 0, Gui.Width, Gui.Height), new Color(0.04f, 0.055f, 0.09f, 1f)); Gui.Fill(new Rect(0, Gui.Height * 0.62f, Gui.Width, Gui.Height * 0.38f), new Color(0.06f, 0.08f, 0.12f, 1f));
            Gui.Fill(new Rect(0, Gui.Height * 0.62f - 3, Gui.Width, 3), Gui.Accent); Gui.Fill(new Rect(0, Gui.Height * 0.62f + 6, Gui.Width, 3), new Color(1f, 0.35f, 0.1f));
            Gui.Label(new Rect(60, Gui.Height * 0.16f, 1000, 70), "Swiss Police Simulator", Color.white, 54, TextAnchor.MiddleLeft, true); Gui.Label(new Rect(62, Gui.Height * 0.16f + 62, 1000, 30), sub, Gui.TextDim, 20);
        }

        void Loading(GameBootstrap b)
        {
            Backdrop("Schweiz – offene Welt"); string st = string.IsNullOrEmpty(b.Error) ? WorldData.Status : "Fehler: " + b.Error; Gui.Label(new Rect(62, Gui.Height * 0.62f + 30, 900, 30), st, string.IsNullOrEmpty(b.Error) ? Gui.Text : Gui.Bad, 18);
            _fade += Time.unscaledDeltaTime; Gui.Bar(new Rect(62, Gui.Height * 0.62f + 70, 500, 8), Mathf.PingPong(_fade * 0.6f, 1f), Gui.Accent);
        }

        void Menu(GameBootstrap b)
        {
            Backdrop("Realistische Polizeisimulation · Recht nach Fedlex · Pilotgebiet: Region Bern");
            var r = new Rect(60, Gui.Height * 0.62f + 24, Gui.Width - 120, Gui.Height * 0.38f - 40);
            Gui.Label(new Rect(r.x, r.y - 6, 300, 24), "Korps wählen", Gui.TextDim, 14);
            var list = PoliceCatalog.All.Where(c => (c.Level == CorpsLevel.Municipal) == _menuMunicipal).ToList();
            if (Gui.ButtonDim(new Rect(r.x + 130, r.y - 8, 170, 26), _menuMunicipal ? "Stadtpolizeien" : "Kantonspolizeien")) _menuMunicipal = !_menuMunicipal;
            var lr = new Rect(r.x, r.y + 24, 520, r.height - 30); Gui.Panel(lr, Gui.Bg2); int perRow = 2; float bw = 246; var cont = new Rect(0, 0, lr.width - 20, Mathf.Max(lr.height, ((list.Count + 1) / perRow) * 34 + 6));
            _menuScroll = GUI.BeginScrollView(lr, _menuScroll, cont);
            for (int i = 0; i < list.Count; i++) if (Gui.Button(new Rect(4 + (i % perRow) * (bw + 4), 4 + (i / perRow) * 34, bw, 30), list[i].Name, true, list[i] == b.SelectedCorps ? Gui.Accent : Gui.AccentDim)) b.SelectedCorps = list[i];
            GUI.EndScrollView();
            float x = lr.xMax + 40; Gui.Label(new Rect(x, r.y + 24, 420, 24), "Gewählt: " + b.SelectedCorps.Name, Gui.Text, 17, TextAnchor.MiddleLeft, true);
            if (Gui.Button(new Rect(x, r.y + 56, 300, 44), "Neues Spiel", true, Gui.Good)) b.StartNew(b.SelectedCorps, _menuSeed + (int)(DateTime.Now.Ticks % 9973));
            int row = 0; for (int i = 1; i <= 3; i++) { bool ex = GameSession.SlotExists(i); if (ex && Gui.ButtonDim(new Rect(x, r.y + 108 + row * 36, 420, 32), $"Laden {i}: " + Short(GameSession.SlotInfo(i), 44))) b.StartFromSlot(i); if (ex) row++; }
            if (Gui.ButtonDim(new Rect(x, r.yMax - 36, 140, 32), "Beenden")) Application.Quit();
            if (!string.IsNullOrEmpty(b.Error)) Gui.Label(new Rect(x, r.yMax - 76, 600, 36), b.Error, Gui.Bad, 14, TextAnchor.UpperLeft, false, true);
            Gui.Label(new Rect(Gui.Width - 560, Gui.Height - 34, 540, 24), "Kartendaten: © OpenStreetMap-Mitwirkende · Höhen: swisstopo · Gesetze: Fedlex", Gui.TextDim, 12, TextAnchor.MiddleRight);
        }
        static string Short(string s, int n) => s.Length > n ? s.Substring(0, n - 1) + "…" : s;

        void Starting(GameBootstrap b)
        {
            Backdrop("Einsatzgebiet wird geladen …"); var s = b.Session; string txt = "Gelände und Strassen werden aufgebaut";
            if (s != null && s.Streamer != null) txt += $" – {s.Streamer.TilesBuilt} Kacheln";
            Gui.Label(new Rect(62, Gui.Height * 0.62f + 30, 900, 30), txt, Gui.Text, 18); _fade += Time.unscaledDeltaTime; Gui.Bar(new Rect(62, Gui.Height * 0.62f + 70, 500, 8), Mathf.PingPong(_fade * 0.6f, 1f), Gui.Accent);
        }

        void Playing(GameBootstrap b)
        {
            var s = S; if (s == null) return;
            if (s.Player != null && s.Player.Frozen) { Gui.Fill(new Rect(0, 0, Gui.Width, Gui.Height), new Color(0, 0, 0, 0.55f)); Gui.Label(new Rect(0, Gui.Height / 2 - 20, Gui.Width, 40), "Umgebung wird geladen …", Gui.Text, 24, TextAnchor.MiddleCenter, true); }
            Hud.Draw(); Windows.Draw();
            if (s.Career != null && s.Career.Suspended) Suspension(s);
        }

        void Suspension(GameSession s)
        {
            Gui.Fill(new Rect(0, 0, Gui.Width, Gui.Height), new Color(0.05f, 0.02f, 0.02f, 0.93f)); var r = Gui.Centered(720, 360); Gui.Window(r, "Vom Dienst suspendiert", Gui.Bad);
            double left = Math.Max(0, s.Career.SuspendedUntil - s.Clock.ElapsedGameSeconds);
            Gui.Label(new Rect(r.x + 30, r.y + 60, r.width - 60, 150), "Sie wurden wegen eines schweren Verstosses im Dienst eingestellt, bis die Strafuntersuchung geklärt ist. Während der Suspendierung dürfen Sie keine polizeilichen Handlungen vornehmen.\n\nVerbleibende Dauer: " + TimeSpan.FromSeconds(left).ToString(@"d\.hh\:mm") + " (Spielzeit)", Gui.Text, 17, TextAnchor.UpperLeft, false, true);
            if (Gui.Button(new Rect(r.x + 30, r.yMax - 64, 300, 38), "Zeit überspringen")) { s.Clock.ElapsedGameSeconds = s.Career.SuspendedUntil + 1; s.Career.Suspended = false; s.Career.ComplaintsOpen = 0; s.Career.InvestigationsOpen = Math.Max(0, s.Career.InvestigationsOpen - 1); Notifier.Warn("Die Suspendierung ist abgelaufen. Sie sind wieder im Dienst."); }
            if (Gui.ButtonDim(new Rect(r.xMax - 250, r.yMax - 64, 220, 38), "Hauptmenü")) B.ToMenu();
        }

        void GameOver(GameBootstrap b)
        {
            Gui.Fill(new Rect(0, 0, Gui.Width, Gui.Height), new Color(0.02f, 0.02f, 0.03f, 0.97f)); var r = Gui.Centered(760, 420); Gui.Window(r, "Dienstenthebung", Gui.Bad);
            var s = S; string why = s != null && s.ConsequenceLog.Count > 0 ? s.ConsequenceLog[s.ConsequenceLog.Count - 1] : "";
            Gui.Label(new Rect(r.x + 30, r.y + 60, r.width - 60, 220), "Ihre Anstellung wurde aufgelöst.\n\nGrund: wiederholte oder schwere Verstösse gegen Recht und Dienstpflichten.\n" + why + (s != null ? $"\n\nBilanz: {s.Career.FinesIssued} Bussen · {s.Career.ArrestsMade} Festnahmen · {s.Cases.Count} Fälle." : ""), Gui.Text, 17, TextAnchor.UpperLeft, false, true);
            if (Gui.Button(new Rect(r.x + 30, r.yMax - 64, 260, 38), "Hauptmenü")) b.ToMenu();
            if (s != null && GameSession.SlotExists(1) && Gui.ButtonDim(new Rect(r.x + 310, r.yMax - 64, 300, 38), "Letzten Spielstand laden")) b.StartFromSlot(1);
        }
    }
}
