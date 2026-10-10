using SwissPoliceSim.Core.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SwissPoliceSim.World
{
    /// <summary>
    /// Keeps the tracked target close to Unity's (0,0,0) by shifting ALL root objects of the active scene whenever the
    /// target is further than <see cref="threshold"/> metres away. Physics velocities are untouched (positions only).
    /// </summary>
    public sealed class FloatingOrigin : MonoBehaviour
    {
        public Transform target;
        [Tooltip("Shift when the target is further than this horizontal distance from (0,0,0).")]
        public float threshold = 1500f;
        [Tooltip("Origin moves in multiples of this size (keeps tile edges on round numbers).")]
        public float snap = 500f;

        void LateUpdate()
        {
            if (target == null) return;
            var p = target.position;
            if (!WorldFrame.NeedsRecenter(p.x, p.z, threshold)) return;
            var offset = new Vector3(Mathf.Round(p.x / snap) * snap, 0, Mathf.Round(p.z / snap) * snap);
            Shift(offset);
        }

        public static void Shift(Vector3 offset)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                var cc = root.GetComponentInChildren<CharacterController>();
                bool wasEnabled = cc != null && cc.enabled;
                if (wasEnabled) cc.enabled = false; // CharacterController overrides teleports while enabled
                root.transform.position -= offset;
                if (wasEnabled) cc.enabled = true;
            }
            Physics.SyncTransforms();
            WorldOrigin.ShiftOrigin(offset);
        }
    }
}
