using UnityEngine;

namespace SwissPoliceSim.Player
{
    /// <summary>Shared "a window is open" flag: gameplay input and mouse look are suspended and the cursor is released while true.</summary>
    public static class UiState
    {
        public static int OpenWindows;
        public static bool Blocking => OpenWindows > 0 || GameInput.TextInputActive;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { OpenWindows = 0; }
        public static void Open() { OpenWindows++; Apply(); }
        public static void Close() { OpenWindows = Mathf.Max(0, OpenWindows - 1); Apply(); }
        public static void Apply() { Cursor.lockState = OpenWindows > 0 ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = OpenWindows > 0; }
    }
}
