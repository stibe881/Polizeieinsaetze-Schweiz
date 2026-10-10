using SwissPoliceSim.Vehicle;
using UnityEngine;

namespace SwissPoliceSim.Player
{
    /// <summary>Owns "who is the player right now": the officer on foot or the driver of the patrol car.</summary>
    public sealed class PlayerSession : MonoBehaviour
    {
        public OnFootController foot;
        public PoliceVehicleController vehicle;
        public VehicleLights lights;
        public CameraRig camera;
        public float enterDistance = 4.5f;

        public bool InVehicle { get; private set; }
        public Transform Active => InVehicle ? vehicle.transform : foot.transform;

        public void StartInVehicle()
        {
            InVehicle = true;
            foot.gameObject.SetActive(false);
            vehicle.HasDriver = true;
            camera.FootTarget = foot.transform; camera.VehicleTarget = vehicle.transform;
            camera.SetMode(CameraMode.VehicleChase);
        }

        void Update()
        {
            if (GameInput.ToggleCursor()) camera.SetCursorLocked(!camera.CursorLocked);

            if (GameInput.Interact())
            {
                if (InVehicle) Exit();
                else if (Vector3.Distance(foot.transform.position, vehicle.transform.position) < enterDistance) Enter();
            }

            if (InVehicle)
            {
                vehicle.Throttle = GameInput.Throttle();
                vehicle.Steer = GameInput.Steer();
                vehicle.Handbrake = GameInput.Handbrake();
                lights.HandleInput();
                if (GameInput.CameraToggle()) camera.CycleVehicleMode();
            }
            else
            {
                vehicle.Throttle = 0; vehicle.Steer = 0; vehicle.Handbrake = true;   // parked
                if (GameInput.FirstPersonToggle())
                    camera.SetMode(camera.Mode == CameraMode.OnFootFirst ? CameraMode.OnFootThird : CameraMode.OnFootFirst);
            }
        }

        void Enter()
        {
            InVehicle = true; vehicle.HasDriver = true;
            foot.gameObject.SetActive(false);
            camera.SetMode(CameraMode.VehicleChase);
        }

        void Exit()
        {
            InVehicle = false; vehicle.HasDriver = false;
            var v = vehicle.transform;
            var pos = v.position - v.right * 2.0f + Vector3.up * 0.2f;
            foot.gameObject.SetActive(true);
            foot.Teleport(pos);
            camera.SetMode(CameraMode.OnFootThird);
            camera.SnapTo(foot.transform, v.eulerAngles.y);
        }
    }
}
