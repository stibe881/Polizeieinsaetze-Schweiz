using System;
using System.Collections.Generic;
using SwissPoliceSim.Player;
using UnityEngine;

namespace SwissPoliceSim.UI
{
    /// <summary>
    /// Tiny IMGUI toolkit with a consistent dark "police terminal" look: panels, buttons, bars, text fields, rotated needles and rings.
    /// IMGUI is used on purpose: no EventSystem / input-module setup, works with the legacy input handling, and cannot break at scene load.
    /// All metrics are in 1080p units and scaled by <see cref="Scale"/>.
    /// </summary>
    public static class Gui
    {
        public static readonly Color Bg = new Color(0.055f, 0.07f, 0.10f, 0.94f), Bg2 = new Color(0.09f, 0.11f, 0.16f, 0.96f), Line = new Color(0.22f, 0.27f, 0.36f, 1f);
        public static readonly Color Accent = new Color(0.20f, 0.52f, 0.95f, 1f), AccentDim = new Color(0.14f, 0.30f, 0.55f, 1f), Text = new Color(0.92f, 0.94f, 0.97f), TextDim = new Color(0.62f, 0.68f, 0.78f);
        public static readonly Color Good = new Color(0.30f, 0.80f, 0.45f), Warn = new Color(0.98f, 0.76f, 0.22f), Bad = new Color(0.95f, 0.30f, 0.28f), Orange = new Color(1f, 0.55f, 0.15f);

        public static float Scale => Mathf.Max(0.6f, Screen.height / 1080f);
        public static Texture2D White, Circle, Ring, Rounded, RoundedLine, Soft, Arrow, Needle;
        static GUIStyle _label, _small, _big, _title, _button, _buttonDim, _field, _box, _mono, _wrap, _center, _huge;
        static bool _init; static Matrix4x4 _saved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { _init = false; White = Circle = Ring = Rounded = RoundedLine = Soft = Arrow = Needle = null; _label = _small = _big = _title = _button = _buttonDim = _field = _box = _mono = _wrap = _center = _huge = null; }

        static Texture2D Make(int w, int h, Func<int, int, Color> f) { var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp }; var px = new Color[w * h]; for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f(x, y); t.SetPixels(px); t.Apply(); return t; }

        public static void Init()
        {
            if (_init) return; _init = true;
            White = Make(2, 2, (x, y) => Color.white);
            Circle = Make(64, 64, (x, y) => { float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32, 32)); return new Color(1, 1, 1, Mathf.Clamp01(32f - d)); });
            Ring = Make(128, 128, (x, y) => { float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(64, 64)); float a = Mathf.Clamp01(64f - d) * Mathf.Clamp01(d - 50f); return new Color(1, 1, 1, a); });
            Soft = Make(64, 64, (x, y) => { float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32, 32)) / 32f; return new Color(1, 1, 1, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)); });
            Rounded = Make(32, 32, (x, y) => RoundedMask(x, y, 32, 8f, 0f)); RoundedLine = Make(32, 32, (x, y) => { float a = RoundedMask(x, y, 32, 8f, 0f).a; float inner = RoundedMask(x - 1, y - 1, 30, 7f, 0f).a; return new Color(1, 1, 1, Mathf.Clamp01(a - inner)); });
            Arrow = Make(64, 64, (x, y) => { float fx = (x + 0.5f) / 64f * 2 - 1, fy = (y + 0.5f) / 64f; bool inside = fy < 0.95f && Mathf.Abs(fx) < (1f - fy) * 0.9f + 0.02f && fy > 0.05f; bool notch = fy < 0.3f && Mathf.Abs(fx) < (0.3f - fy) * 1.2f; return new Color(1, 1, 1, inside && !notch ? 1 : 0); });
            Needle = Make(16, 128, (x, y) => { float fx = Mathf.Abs((x + 0.5f) / 16f * 2 - 1), fy = (y + 0.5f) / 128f; return new Color(1, 1, 1, fx < (1f - fy) * 0.9f ? 1 : 0); });
            _box = new GUIStyle(); _box.normal.background = Rounded; _box.border = new RectOffset(10, 10, 10, 10);
            _label = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, wordWrap = false, alignment = TextAnchor.MiddleLeft }; _label.normal.textColor = Text;
            _wrap = new GUIStyle(_label) { wordWrap = true, alignment = TextAnchor.UpperLeft };
            _small = new GUIStyle(_label) { fontSize = 13 }; _small.normal.textColor = TextDim;
            _big = new GUIStyle(_label) { fontSize = 22, fontStyle = FontStyle.Bold };
            _huge = new GUIStyle(_label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _title = new GUIStyle(_label) { fontSize = 19, fontStyle = FontStyle.Bold }; _center = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter };
            _mono = new GUIStyle(_label) { fontSize = 15, font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New", "monospace" }, 15) };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 16, alignment = TextAnchor.MiddleCenter, richText = true }; _button.normal.background = Rounded; _button.hover.background = Rounded; _button.active.background = Rounded; _button.focused.background = Rounded; _button.border = new RectOffset(10, 10, 10, 10);
            _button.normal.textColor = Text; _button.hover.textColor = Color.white; _button.active.textColor = Color.white;
            _buttonDim = new GUIStyle(_button);
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 17, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 4, 4) }; _field.normal.background = Rounded; _field.focused.background = Rounded; _field.hover.background = Rounded; _field.border = new RectOffset(10, 10, 10, 10);
            _field.normal.textColor = Text; _field.focused.textColor = Color.white; _field.hover.textColor = Text;
        }

        static Color RoundedMask(int x, int y, int size, float r, float pad)
        {
            float cx = Mathf.Clamp(x + 0.5f, r, size - r), cy = Mathf.Clamp(y + 0.5f, r, size - r); float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
            return new Color(1, 1, 1, Mathf.Clamp01(r - d + 0.5f));
        }

        // ------------------------------------------------------------------ scaling
        public static void Begin() { Init(); _saved = GUI.matrix; float s = Scale; GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1)); }
        public static void End() { GUI.matrix = _saved; }
        public static float Width => Screen.width / Scale; public static float Height => Screen.height / Scale;

        // ------------------------------------------------------------------ primitives
        public static void Fill(Rect r, Color c, Texture2D tex = null) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, tex ?? White, ScaleMode.StretchToFill, true); GUI.color = old; }
        public static void Panel(Rect r, Color? c = null) { var old = GUI.color; GUI.color = c ?? Bg; GUI.Box(r, GUIContent.none, _box); GUI.color = Line; GUI.DrawTexture(r, RoundedLine, ScaleMode.StretchToFill, true); GUI.color = old; }
        public static void Window(Rect r, string title, Color? accent = null)
        {
            Panel(r); var ac = accent ?? Accent; Fill(new Rect(r.x + 12, r.y + 46, r.width - 24, 2), ac);
            Label(new Rect(r.x + 18, r.y + 8, r.width - 36, 34), title, _title);
        }
        public static void Label(Rect r, string text, GUIStyle style = null) { GUI.Label(r, text, style ?? _label); }
        public static void Label(Rect r, string text, Color c, int size = 16, TextAnchor a = TextAnchor.MiddleLeft, bool bold = false, bool wrap = false)
        {
            var st = new GUIStyle(wrap ? _wrap : _label) { fontSize = size, alignment = a, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal }; st.normal.textColor = c; GUI.Label(r, text, st);
        }
        public static GUIStyle Small => _small; public static GUIStyle Big => _big; public static GUIStyle Mono => _mono; public static GUIStyle Wrap => _wrap; public static GUIStyle Title => _title; public static GUIStyle Huge => _huge; public static GUIStyle Center => _center; public static GUIStyle LabelStyle => _label;

        public static bool Button(Rect r, string text, bool enabled = true, Color? color = null)
        {
            var old = GUI.color; bool hover = r.Contains(Event.current.mousePosition); var c = color ?? Accent;
            GUI.color = enabled ? (hover ? Color.Lerp(c, Color.white, 0.18f) : c * 0.82f) : new Color(0.3f, 0.32f, 0.38f, 0.8f);
            bool clicked = GUI.Button(r, text, _button) && enabled; GUI.color = old; return clicked;
        }

        public static bool ButtonDim(Rect r, string text, bool enabled = true) => Button(r, text, enabled, AccentDim);

        public static string TextField(Rect r, string text, string controlName)
        {
            GUI.SetNextControlName(controlName); var s = GUI.TextField(r, text, _field); return s;
        }

        public static void Bar(Rect r, float value01, Color fill, Color? bg = null)
        {
            Fill(r, bg ?? new Color(1, 1, 1, 0.08f), Rounded); var f = new Rect(r.x, r.y, Mathf.Max(0, r.width * Mathf.Clamp01(value01)), r.height); if (f.width > 1) Fill(f, fill, Rounded);
        }

        public static void Line2D(Vector2 a, Vector2 b, float width, Color c)
        {
            var d = b - a; float len = d.magnitude; if (len < 0.5f) return; float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg; var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, a); Fill(new Rect(a.x, a.y - width * 0.5f, len, width), c); GUI.matrix = m;
        }

        public static void RotatedTexture(Rect r, Texture2D tex, float angleDeg, Color c) { var m = GUI.matrix; var old = GUI.color; GUIUtility.RotateAroundPivot(angleDeg, r.center); GUI.color = c; GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true); GUI.color = old; GUI.matrix = m; }

        public static void Disc(Vector2 centre, float radius, Color c) => Fill(new Rect(centre.x - radius, centre.y - radius, radius * 2, radius * 2), c, Circle);
        public static void RingAt(Vector2 centre, float radius, Color c) => Fill(new Rect(centre.x - radius, centre.y - radius, radius * 2, radius * 2), c, Ring);

        /// <summary>Swiss speed-limit sign: white disc, red ring, black figures.</summary>
        public static void SpeedSign(Vector2 centre, float radius, int limit, bool dim = false)
        {
            Disc(centre, radius, new Color(0.85f, 0.1f, 0.1f, dim ? 0.5f : 1f)); Disc(centre, radius * 0.78f, new Color(1, 1, 1, dim ? 0.6f : 1f));
            var st = new GUIStyle(_center) { fontSize = Mathf.RoundToInt(radius * (limit >= 100 ? 0.78f : 0.98f)), fontStyle = FontStyle.Bold }; st.normal.textColor = new Color(0.05f, 0.05f, 0.05f, dim ? 0.6f : 1f);
            GUI.Label(new Rect(centre.x - radius, centre.y - radius, radius * 2, radius * 2), limit.ToString(), st);
        }

        public static bool Hit(Rect r) => r.Contains(Event.current.mousePosition);
        public static Rect Centered(float w, float h) => new Rect((Width - w) / 2, (Height - h) / 2, w, h);
    }
}
