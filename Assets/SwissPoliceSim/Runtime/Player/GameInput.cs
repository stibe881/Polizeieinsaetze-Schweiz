using UnityEngine;
#if ENABLE_INPUT_SYSTEM && SPS_HAS_INPUTSYSTEM
using UnityEngine.InputSystem;
#endif

namespace SwissPoliceSim.Player
{
    /// <summary>
    /// Thin input abstraction so gameplay code works with the new Input System or the legacy Input Manager
    /// (whichever "Active Input Handling" is set to). Keyboard + mouse for Phase 1.
    /// </summary>
    public static class GameInput
    {
        public static float Throttle() => Axis(Key.W, Key.S);                // forward +, reverse/brake -
        public static float Steer() => Axis(Key.D, Key.A);
        public static Vector2 Move() => new Vector2(Axis(Key.D, Key.A), Axis(Key.W, Key.S));
        public static bool Handbrake() => Held(Key.Space);
        public static bool Run() => Held(Key.LeftShift);
        public static bool Interact() => Pressed(Key.F);
        public static bool CameraToggle() => Pressed(Key.C);
        public static bool Headlights() => Pressed(Key.L);
        public static bool BlueLights() => Pressed(Key.B);
        public static bool Siren() => Pressed(Key.Q);
        public static bool IndicatorLeft() => Pressed(Key.Z);
        public static bool IndicatorRight() => Pressed(Key.X);
        public static bool Hazard() => Pressed(Key.H);
        public static bool NextDestination() => Pressed(Key.N);
        public static bool ClearRoute() => Pressed(Key.M);
        public static bool ToggleCursor() => Pressed(Key.Escape);
        public static bool FirstPersonToggle() => Pressed(Key.V);

        public enum Key { W, A, S, D, Space, LeftShift, F, C, L, B, Q, Z, X, H, N, M, Escape, V }

#if ENABLE_INPUT_SYSTEM && SPS_HAS_INPUTSYSTEM
        static UnityEngine.InputSystem.Controls.KeyControl K(Key k)
        {
            var kb = Keyboard.current;
            if (kb == null) return null;
            switch (k)
            {
                case Key.W: return kb.wKey; case Key.A: return kb.aKey; case Key.S: return kb.sKey; case Key.D: return kb.dKey;
                case Key.Space: return kb.spaceKey; case Key.LeftShift: return kb.leftShiftKey;
                case Key.F: return kb.fKey; case Key.C: return kb.cKey; case Key.L: return kb.lKey; case Key.B: return kb.bKey;
                case Key.Q: return kb.qKey; case Key.Z: return kb.zKey; case Key.X: return kb.xKey; case Key.H: return kb.hKey;
                case Key.N: return kb.nKey; case Key.M: return kb.mKey; case Key.Escape: return kb.escapeKey; case Key.V: return kb.vKey;
            }
            return null;
        }
        static bool Held(Key k) { var c = K(k); return c != null && c.isPressed; }
        static bool Pressed(Key k) { var c = K(k); return c != null && c.wasPressedThisFrame; }
        public static Vector2 LookDelta() => Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.08f : Vector2.zero;
        public static float Scroll() => Mouse.current != null ? Mouse.current.scroll.ReadValue().y * 0.01f : 0f;
#else
        static KeyCode C(Key k)
        {
            switch (k)
            {
                case Key.W: return KeyCode.W; case Key.A: return KeyCode.A; case Key.S: return KeyCode.S; case Key.D: return KeyCode.D;
                case Key.Space: return KeyCode.Space; case Key.LeftShift: return KeyCode.LeftShift;
                case Key.F: return KeyCode.F; case Key.C: return KeyCode.C; case Key.L: return KeyCode.L; case Key.B: return KeyCode.B;
                case Key.Q: return KeyCode.Q; case Key.Z: return KeyCode.Z; case Key.X: return KeyCode.X; case Key.H: return KeyCode.H;
                case Key.N: return KeyCode.N; case Key.M: return KeyCode.M; case Key.Escape: return KeyCode.Escape; case Key.V: return KeyCode.V;
            }
            return KeyCode.None;
        }
        static bool Held(Key k) => Input.GetKey(C(k));
        static bool Pressed(Key k) => Input.GetKeyDown(C(k));
        public static Vector2 LookDelta() => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 3f;
        public static float Scroll() => Input.mouseScrollDelta.y;
#endif

        static float Axis(Key pos, Key neg) => (Held(pos) ? 1f : 0f) - (Held(neg) ? 1f : 0f);
    }
}
