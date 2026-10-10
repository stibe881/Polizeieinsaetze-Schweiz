using SwissPoliceSim.Player;
using UnityEngine;

namespace SwissPoliceSim.Vehicle
{
    /// <summary>
    /// Arcade-leaning WheelCollider car (all-wheel drive). Throttle/brake/steering come from <see cref="Throttle"/>,
    /// <see cref="Steer"/>, <see cref="Handbrake"/> which a driver (player or AI later) sets.
    /// Values are tuned for a ~1.9 t estate car; they are starting points, NOT calibrated against a real vehicle.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PoliceVehicleController : MonoBehaviour
    {
        [Header("Wheels (FL, FR, RL, RR)")]
        public WheelCollider[] wheelColliders = new WheelCollider[4];
        public Transform[] wheelMeshes = new Transform[4];

        [Header("Drivetrain")]
        public float maxMotorTorque = 1100f;     // Nm per wheel
        public float maxBrakeTorque = 3200f;     // Nm per wheel
        public float maxSpeedKmh = 210f;
        public float reverseMaxSpeedKmh = 35f;
        public float maxSteerAngle = 32f;
        public float highSpeedSteerAngle = 6f;
        public float highSpeedKmh = 160f;
        public float antiRoll = 6000f;
        public float downforce = 25f;

        [HideInInspector] public float Throttle;   // -1 (brake/reverse) .. 1
        [HideInInspector] public float Steer;      // -1 .. 1
        [HideInInspector] public bool Handbrake;
        public bool HasDriver { get; set; }

        Rigidbody _rb;
        public float SpeedKmh => _rb.linearVelocity.magnitude * 3.6f;
        public float ForwardSpeedKmh => Vector3.Dot(_rb.linearVelocity, transform.forward) * 3.6f;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.centerOfMass = new Vector3(0f, -0.45f, 0.1f);
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        void FixedUpdate()
        {
            float fwd = ForwardSpeedKmh;
            float speedAbs = Mathf.Abs(fwd);
            float t = Throttle;

            float motor = 0f, brake = 0f;
            if (t > 0.01f)
            {
                if (fwd < -1f) brake = maxBrakeTorque * t;                       // moving backwards: brake first
                else if (fwd < maxSpeedKmh) motor = maxMotorTorque * t;
            }
            else if (t < -0.01f)
            {
                if (fwd > 1f) brake = maxBrakeTorque * -t;                       // moving forwards: brake
                else if (fwd > -reverseMaxSpeedKmh) motor = maxMotorTorque * 0.5f * t;
            }
            else brake = 200f;                                                   // engine braking / rolling resistance
            if (Handbrake) brake = maxBrakeTorque;

            float steerLimit = Mathf.Lerp(maxSteerAngle, highSpeedSteerAngle, Mathf.Clamp01(speedAbs / highSpeedKmh));
            for (int i = 0; i < wheelColliders.Length; i++)
            {
                var w = wheelColliders[i];
                if (w == null) continue;
                w.motorTorque = motor;
                w.brakeTorque = (Handbrake && i >= 2) || !Handbrake ? brake : brake * 0.4f;
                if (i < 2) w.steerAngle = Steer * steerLimit;
            }

            ApplyAntiRoll(0, 1); ApplyAntiRoll(2, 3);
            _rb.AddForce(-transform.up * (downforce * _rb.linearVelocity.sqrMagnitude * 0.01f));
        }

        void Update()
        {
            for (int i = 0; i < wheelColliders.Length; i++)
            {
                if (wheelColliders[i] == null || wheelMeshes[i] == null) continue;
                wheelColliders[i].GetWorldPose(out var pos, out var rot);
                wheelMeshes[i].SetPositionAndRotation(pos, rot);
            }
        }

        void ApplyAntiRoll(int left, int right)
        {
            var l = wheelColliders[left]; var r = wheelColliders[right];
            if (l == null || r == null) return;
            float travelL = 1f, travelR = 1f;
            bool gL = l.GetGroundHit(out var hl), gR = r.GetGroundHit(out var hr);
            if (gL) travelL = (-l.transform.InverseTransformPoint(hl.point).y - l.radius) / l.suspensionDistance;
            if (gR) travelR = (-r.transform.InverseTransformPoint(hr.point).y - r.radius) / r.suspensionDistance;
            float force = (travelL - travelR) * antiRoll;
            if (gL) _rb.AddForceAtPosition(l.transform.up * -force, l.transform.position);
            if (gR) _rb.AddForceAtPosition(r.transform.up * force, r.transform.position);
        }
    }
}
