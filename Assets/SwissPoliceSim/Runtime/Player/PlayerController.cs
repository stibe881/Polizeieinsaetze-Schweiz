using System;
using SwissPoliceSim.Characters;
using SwissPoliceSim.Core.Meshing;
using SwissPoliceSim.Vehicles;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.Player
{
    /// <summary>The player: an officer on foot (CharacterController) who can enter/exit the service vehicle. Waits ("Frozen") until the ground colliders around the start are ready.</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        public PlayerCar Car; public CameraRig Rig; public TileStreamer Streamer; public HumanVisual Visual;
        public bool Frozen = true; public bool InVehicle { get; private set; }
        public event Action<bool> VehicleStateChanged;
        public string OfficerName = "Polizist/in"; public bool Female;
        CharacterController _cc; float _vy; float _walkSpeed = 1.55f, _runSpeed = 4.6f; GameObject _visualGo;
        public Vector3 WorldPosition => InVehicle && Car != null ? Car.transform.position : transform.position;
        public Transform FocusTransform => InVehicle && Car != null ? Car.transform : transform;

        void Awake() { _cc = GetComponent<CharacterController>(); _cc.height = 1.8f; _cc.radius = 0.3f; _cc.center = new Vector3(0, 0.9f, 0); _cc.stepOffset = 0.35f; _cc.skinWidth = 0.04f; }

        public void Init(HumanSpec spec)
        {
            var go = HumanFactory.Create(spec, "Officer", 0, false); go.transform.SetParent(transform, false); Visual = go.GetComponent<HumanVisual>(); _visualGo = go; Visual.EnableFootsteps();
        }

        public void SetVisualVisible(bool v) { if (_visualGo != null && _visualGo.activeSelf != v) _visualGo.SetActive(v); }

        void Update()
        {
            if (Frozen) { TryUnfreeze(); return; }
            if (UiState.Blocking) { if (Car != null) { Car.Throttle = 0; Car.Steer = 0; Car.Handbrake = true; } return; }
            if (InVehicle) DriveInput(); else FootInput();
            if (GameInput.Use()) { if (InVehicle) TryExit(); else TryEnter(); }
        }

        void TryUnfreeze()
        {
            var p = WorldPosition; var l = WorldOrigin.ToLv95(p);
            bool ready = Streamer == null || Streamer.IsAreaLoaded(l.E, l.N, 1);
            if (!ready) return;
            Frozen = false; if (Car != null && InVehicle) Car.Body.isKinematic = false;
        }

        void FootInput()
        {
            var mv = GameInput.Move(); var fwd = Rig != null ? Rig.FlatForward : Vector3.forward; var right = Quaternion.Euler(0, 90, 0) * fwd;
            var dir = fwd * mv.y + right * mv.x; if (dir.sqrMagnitude > 1) dir.Normalize();
            bool run = GameInput.Run(); float speed = (run ? _runSpeed : _walkSpeed) * dir.magnitude;
            if (_cc.isGrounded) { _vy = -1f; } else _vy -= 9.81f * Time.deltaTime;
            _cc.Move((dir * (run ? _runSpeed : _walkSpeed) + Vector3.up * _vy) * Time.deltaTime);
            if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1f - Mathf.Exp(-12f * Time.deltaTime));
            if (Visual != null) { Visual.Speed = speed; if (Visual.Pose != PoseKind.Talk && Visual.Pose != PoseKind.Point && Visual.Pose != PoseKind.Search) Visual.Pose = speed > 0.2f ? (run ? PoseKind.Run : PoseKind.Walk) : PoseKind.Idle; }
        }

        void DriveInput()
        {
            if (Car == null) return;
            Car.Throttle = GameInput.Throttle(); Car.Steer = GameInput.Steer(); Car.Handbrake = GameInput.Handbrake(); Car.Horn = GameInput.Horn();
            if (GameInput.Headlights()) Car.Visual.Headlights = !Car.Visual.Headlights;
            if (GameInput.BlueLights()) Car.SetBlue(!Car.Visual.Blue);
            if (GameInput.Siren()) { Car.SetSiren(!Car.Siren); if (Car.Siren) Car.SetBlue(true); }
            if (GameInput.IndicatorLeft()) { Car.Visual.IndLeft = !Car.Visual.IndLeft; Car.Visual.IndRight = false; Car.Visual.Hazard = false; }
            if (GameInput.IndicatorRight()) { Car.Visual.IndRight = !Car.Visual.IndRight; Car.Visual.IndLeft = false; Car.Visual.Hazard = false; }
            if (GameInput.Hazard()) { Car.Visual.Hazard = !Car.Visual.Hazard; Car.Visual.IndLeft = Car.Visual.IndRight = false; }
            // indicators cancel after a turn: steering back to centre at speed
            if ((Car.Visual.IndLeft || Car.Visual.IndRight) && Mathf.Abs(Car.Steer) < 0.05f && Car.SpeedKmh > 10f && Time.frameCount % 120 == 0 && UnityEngine.Random.value < 0.15f) { }
        }

        public void PlaceInVehicle(PlayerCar car)
        {
            Car = car; InVehicle = true; _cc.enabled = false; SetVisualVisible(false); car.HasDriver = true; transform.SetParent(car.transform, false); transform.localPosition = Vector3.zero; VehicleStateChanged?.Invoke(true);
        }

        void TryEnter()
        {
            if (Car == null) return; if ((Car.transform.position - transform.position).sqrMagnitude > 3.5f * 3.5f) return;
            PlaceInVehicle(Car); Audio.ProceduralAudio.DoorClose();
        }

        void TryExit()
        {
            if (Car == null || Car.SpeedKmh > 7f) return;
            var exit = Car.transform.TransformPoint(new Vector3(-1.4f, 0.1f, 0.3f)); if (Physics.Raycast(exit + Vector3.up * 2f, Vector3.down, out var h, 4f)) exit = h.point + Vector3.up * 0.05f;
            transform.SetParent(null); transform.position = exit; transform.rotation = Quaternion.Euler(0, Car.transform.eulerAngles.y, 0); _cc.enabled = true; SetVisualVisible(true);
            Car.HasDriver = false; Car.Throttle = 0; Car.Steer = 0; InVehicle = false; if (Rig != null) Rig.Yaw = Car.transform.eulerAngles.y; VehicleStateChanged?.Invoke(false);
        }

        public void Teleport(Vector3 pos) { bool en = _cc.enabled; _cc.enabled = false; transform.position = pos; _cc.enabled = en; }
    }
}
