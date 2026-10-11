using System;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Vehicles;
using UnityEngine;

namespace SwissPoliceSim.Player
{
    /// <summary>Chase / cockpit / hood camera for the vehicle and third / first person camera on foot, with collision, speed FOV and impact shake.</summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public enum View { Chase, Cockpit, Hood, FootThird, FootFirst }
        public Camera Cam; public PlayerController Player;
        public View VehicleView = View.Chase, FootView = View.FootThird;
        public float Yaw, Pitch = 12f; float _userYaw, _userPitch, _lastLook, _shake; Vector3 _vel; float _fov = 62f;
        public View Current => Player != null && Player.InVehicle ? VehicleView : FootView;

        void LateUpdate()
        {
            if (Cam == null || Player == null) return;
            if (!UiState.Blocking && Cursor.lockState == CursorLockMode.Locked)
            {
                var d = GameInput.LookDelta();
                if (d.sqrMagnitude > 0.0001f) { _lastLook = Time.time; if (Player.InVehicle) { _userYaw += d.x; _userPitch = Mathf.Clamp(_userPitch - d.y, -25f, 40f); } else { Yaw += d.x; Pitch = Mathf.Clamp(Pitch - d.y, -70f, 70f); } }
                if (GameInput.CameraToggle()) { if (Player.InVehicle) VehicleView = (View)(((int)VehicleView + 1) % 3); else FootView = FootView == View.FootThird ? View.FootFirst : View.FootThird; }
                if (GameInput.FirstPersonToggle() && !Player.InVehicle) FootView = FootView == View.FootThird ? View.FootFirst : View.FootThird;
            }
            if (Player.InVehicle) UpdateVehicle(); else UpdateFoot();
            if (_shake > 0.001f) { Cam.transform.position += UnityEngine.Random.insideUnitSphere * _shake * 0.12f; _shake = Mathf.MoveTowards(_shake, 0, Time.deltaTime * 2.5f); }
        }

        public void Shake(float amount) { _shake = Mathf.Max(_shake, Mathf.Clamp01(amount)); }

        void UpdateVehicle()
        {
            var car = Player.Car; if (car == null) return; var t = car.transform; float speed = car.SpeedKmh;
            if (Time.time - _lastLook > 2.0f) { _userYaw = Mathf.MoveTowardsAngle(_userYaw, 0, 140f * Time.deltaTime); _userPitch = Mathf.MoveTowards(_userPitch, 0, 40f * Time.deltaTime); }
            float fovTarget = 62f + Mathf.Clamp01(speed / 200f) * 16f; _fov = Mathf.Lerp(_fov, fovTarget, 3f * Time.deltaTime); Cam.fieldOfView = _fov;
            switch (VehicleView)
            {
                case View.Cockpit:
                case View.Hood:
                {
                    var m = car.Model; var anchor = VehicleView == View.Cockpit ? m.DriverEye : m.HoodCam; var p = t.TransformPoint(new Vector3(anchor[0], anchor[1], anchor[2]));
                    Cam.transform.position = p; Cam.transform.rotation = t.rotation * Quaternion.Euler(Mathf.Clamp(_userPitch * 0.7f, -25f, 30f), Mathf.Clamp(_userYaw, -110f, 110f), 0);
                    Cam.nearClipPlane = 0.08f; return;
                }
                default:
                {
                    Cam.nearClipPlane = 0.2f;
                    float dist = 6.6f + speed * 0.012f; float h = 2.35f + speed * 0.002f;
                    float yaw = t.eulerAngles.y + _userYaw; var rot = Quaternion.Euler(10f + _userPitch, yaw, 0);
                    var focus = t.position + Vector3.up * 1.15f + t.forward * Mathf.Clamp(speed * 0.01f, 0, 1.5f);
                    var desired = focus - rot * Vector3.forward * dist + Vector3.up * (h - 1.15f);
                    if (Physics.SphereCast(focus, 0.25f, (desired - focus).normalized, out var hit, dist + 0.5f, ~0, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(t)) desired = focus + (desired - focus).normalized * Mathf.Max(0.8f, hit.distance - 0.2f);
                    Cam.transform.position = Vector3.SmoothDamp(Cam.transform.position, desired, ref _vel, 0.06f);
                    Cam.transform.rotation = Quaternion.Slerp(Cam.transform.rotation, Quaternion.LookRotation(focus - Cam.transform.position, Vector3.up), 1f - Mathf.Exp(-14f * Time.deltaTime));
                    return;
                }
            }
        }

        void UpdateFoot()
        {
            var pt = Player.transform; Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, 62f, 4f * Time.deltaTime);
            var head = pt.position + Vector3.up * 1.62f; var rot = Quaternion.Euler(Pitch, Yaw, 0);
            if (FootView == View.FootFirst) { Cam.nearClipPlane = 0.05f; Cam.transform.SetPositionAndRotation(head + rot * Vector3.forward * 0.12f, rot); Player.SetVisualVisible(false); return; }
            Player.SetVisualVisible(true); Cam.nearClipPlane = 0.12f;
            var pivot = head + Vector3.up * 0.12f; var desired = pivot + rot * new Vector3(0.55f, 0, -3.1f);
            if (Physics.SphereCast(pivot, 0.2f, (desired - pivot).normalized, out var hit, (desired - pivot).magnitude, ~0, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(pt)) desired = pivot + (desired - pivot).normalized * Mathf.Max(0.5f, hit.distance - 0.15f);
            Cam.transform.position = desired; Cam.transform.rotation = rot;
        }

        /// <summary>Horizontal forward direction of the camera (for camera-relative movement).</summary>
        public Vector3 FlatForward { get { var f = Cam != null ? Cam.transform.forward : Vector3.forward; f.y = 0; return f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized; } }
    }
}
