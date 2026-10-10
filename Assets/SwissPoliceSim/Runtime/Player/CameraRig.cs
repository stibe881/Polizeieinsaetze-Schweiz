using UnityEngine;

namespace SwissPoliceSim.Player
{
    public enum CameraMode { OnFootThird, OnFootFirst, VehicleChase, VehicleCockpit, VehicleHood }

    /// <summary>One camera for all situations: third/first person on foot, chase / cockpit / hood in the vehicle.</summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera cam;
        public CameraMode Mode { get; private set; } = CameraMode.VehicleChase;
        public Transform FootTarget, VehicleTarget, CockpitAnchor, HoodAnchor;
        public bool CursorLocked { get; private set; }

        float _yaw, _pitch = 12f, _dist = 5.5f, _chaseYawOffset;

        public Transform YawSource => cam.transform;

        void Start() { SetCursorLocked(true); }

        public void SetCursorLocked(bool locked)
        {
            CursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        public void SetMode(CameraMode m) { Mode = m; }
        public void CycleVehicleMode()
        {
            Mode = Mode == CameraMode.VehicleChase ? CameraMode.VehicleCockpit
                 : Mode == CameraMode.VehicleCockpit ? CameraMode.VehicleHood : CameraMode.VehicleChase;
        }

        void LateUpdate()
        {
            if (cam == null) return;
            var look = CursorLocked ? GameInput.LookDelta() : Vector2.zero;
            _dist = Mathf.Clamp(_dist - GameInput.Scroll() * 0.5f, 2.5f, 12f);
            var t = cam.transform;

            switch (Mode)
            {
                case CameraMode.OnFootThird:
                case CameraMode.OnFootFirst:
                    if (FootTarget == null) return;
                    _yaw += look.x; _pitch = Mathf.Clamp(_pitch - look.y, -60f, 70f);
                    var rot = Quaternion.Euler(_pitch, _yaw, 0);
                    var head = FootTarget.position + Vector3.up * 1.6f;
                    if (Mode == CameraMode.OnFootFirst) { t.SetPositionAndRotation(head, rot); }
                    else
                    {
                        var pivot = FootTarget.position + Vector3.up * 1.5f;
                        var desired = pivot - rot * Vector3.forward * _dist * 0.6f;
                        if (Physics.Linecast(pivot, desired, out var hit)) desired = hit.point + hit.normal * 0.15f;
                        t.SetPositionAndRotation(desired, rot);
                    }
                    break;

                case CameraMode.VehicleChase:
                    if (VehicleTarget == null) return;
                    _chaseYawOffset += look.x;
                    _pitch = Mathf.Clamp(_pitch - look.y, -10f, 60f);
                    if (look.sqrMagnitude < 0.0001f) { _chaseYawOffset = Mathf.Lerp(_chaseYawOffset, 0, 2f * Time.deltaTime); _pitch = Mathf.Lerp(_pitch, 12f, Time.deltaTime); }
                    float yaw = VehicleTarget.eulerAngles.y + _chaseYawOffset;
                    var r2 = Quaternion.Euler(_pitch, yaw, 0);
                    var piv = VehicleTarget.position + Vector3.up * 1.5f;
                    var d2 = piv - r2 * Vector3.forward * _dist;
                    t.position = Vector3.Lerp(t.position, d2, 1f - Mathf.Exp(-12f * Time.deltaTime));
                    t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(piv - t.position, Vector3.up), 1f - Mathf.Exp(-14f * Time.deltaTime));
                    break;

                case CameraMode.VehicleCockpit:
                case CameraMode.VehicleHood:
                    var a = Mode == CameraMode.VehicleCockpit ? CockpitAnchor : HoodAnchor;
                    if (a == null) return;
                    _chaseYawOffset = Mathf.Clamp(_chaseYawOffset + look.x, -110f, 110f);
                    if (look.sqrMagnitude < 0.0001f) _chaseYawOffset = Mathf.Lerp(_chaseYawOffset, 0, 3f * Time.deltaTime);
                    t.SetPositionAndRotation(a.position, a.rotation * Quaternion.Euler(0, _chaseYawOffset, 0));
                    break;
            }
        }

        public void SnapTo(Transform target, float yaw)
        {
            _yaw = yaw; _chaseYawOffset = 0;
            if (cam != null && target != null)
                cam.transform.SetPositionAndRotation(target.position - Quaternion.Euler(10, yaw, 0) * Vector3.forward * _dist + Vector3.up * 2f, Quaternion.Euler(10, yaw, 0));
        }
    }
}
