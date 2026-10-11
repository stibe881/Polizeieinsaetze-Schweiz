using UnityEngine;

namespace SwissPoliceSim.Player
{
    /// <summary>
    /// Input abstraction over the legacy Input Manager (the project does not use the Input System package, so Player Settings → Active Input Handling = "Input Manager (Old)").
    /// Keyboard + mouse; all bindings are in one place so they can be shown in the help screen.
    /// </summary>
    public static class GameInput
    {
        // ---- driving / walking
        public static float Throttle() => Axis(KeyCode.W, KeyCode.S);
        public static float Steer() => Axis(KeyCode.D, KeyCode.A);
        public static Vector2 Move() => new Vector2(Axis(KeyCode.D, KeyCode.A), Axis(KeyCode.W, KeyCode.S));
        public static bool Handbrake() => Held(KeyCode.Space);
        public static bool Run() => Held(KeyCode.LeftShift);
        public static bool Jump() => Pressed(KeyCode.Space);
        public static bool Horn() => Held(KeyCode.H);
        public static bool Headlights() => Pressed(KeyCode.L);
        public static bool BlueLights() => Pressed(KeyCode.B);
        public static bool Siren() => Pressed(KeyCode.Q);
        public static bool IndicatorLeft() => Pressed(KeyCode.Z);
        public static bool IndicatorRight() => Pressed(KeyCode.X);
        public static bool Hazard() => Pressed(KeyCode.J);
        // ---- interaction
        public static bool Use() => Pressed(KeyCode.F);                 // enter / exit vehicle, use
        public static bool Talk() => Pressed(KeyCode.E);                // address a person / driver
        public static bool StopVehicle() => Pressed(KeyCode.G);         // signal "Anhalten"
        public static bool Radar() => Pressed(KeyCode.R);               // hand-held speed measurement
        public static bool Fire1() => Input.GetMouseButtonDown(0);
        public static bool Fire1Held() => Input.GetMouseButton(0);
        public static bool Fire2() => Input.GetMouseButtonDown(1);
        public static bool Fire2Held() => Input.GetMouseButton(1);
        // ---- UI
        public static bool Computer() => Pressed(KeyCode.Tab);
        public static bool Map() => Pressed(KeyCode.M);
        public static bool Radio() => Pressed(KeyCode.T);
        public static bool Accept() => Pressed(KeyCode.Return) || Pressed(KeyCode.KeypadEnter);
        public static bool Decline() => Pressed(KeyCode.Backspace);
        public static bool CameraToggle() => Pressed(KeyCode.C);
        public static bool FirstPersonToggle() => Pressed(KeyCode.V);
        public static bool Escape() => Pressed(KeyCode.Escape);
        public static bool QuickSave() => Pressed(KeyCode.F5);
        public static bool QuickLoad() => Pressed(KeyCode.F9);
        public static bool Help() => Pressed(KeyCode.F1);
        public static bool Report() => Pressed(KeyCode.P);
        public static bool Notebook() => Pressed(KeyCode.N);

        public static bool Held(KeyCode k) => Input.GetKey(k);
        public static bool Pressed(KeyCode k) => Input.GetKeyDown(k);
        public static Vector2 LookDelta() => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 3f;
        public static float Scroll() => Input.mouseScrollDelta.y;
        static float Axis(KeyCode pos, KeyCode neg) => (Input.GetKey(pos) ? 1f : 0f) - (Input.GetKey(neg) ? 1f : 0f);

        /// <summary>True while a UI text field has keyboard focus (gameplay keys must be ignored then).</summary>
        public static bool TextInputActive { get; set; }
    }
}
