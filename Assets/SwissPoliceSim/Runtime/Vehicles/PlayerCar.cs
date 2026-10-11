using System;
using SwissPoliceSim.Audio;
using SwissPoliceSim.Common;
using SwissPoliceSim.Core.Meshing;
using UnityEngine;

namespace SwissPoliceSim.Vehicles
{
    /// <summary>
    /// Physics car (Rigidbody + 4 WheelColliders) for the player and for scripted vehicles. Arcade-leaning with stability aids (anti-roll, lateral grip assist,
    /// speed-sensitive steering, downforce). Values are starting points tuned on paper for a ~1.75 t estate car; the handling must be tuned in play (see docs).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PlayerCar : MonoBehaviour
    {
        public VehicleVisual Visual; public WheelCollider[] Wheels = new WheelCollider[4];
        public float Throttle, Steer; public bool Handbrake; public bool HasDriver; public bool EngineOn = true;
        public float MaxMotorTorque = 640f, MaxBrakeTorque = 1900f, MaxSpeedKmh = 215f, ReverseMaxKmh = 35f, MaxSteer = 31f, HighSpeedSteer = 5.5f, HighSpeedKmh = 150f, AntiRoll = 7000f, Downforce = 18f, LateralGrip = 5.5f;
        public float Fuel01 = 1f, Damage01;
        public bool Siren { get; private set; } public bool Horn { get; set; }
        public event Action<float, Collision> Collided;
        public Rigidbody Body { get; private set; }
        public float SpeedKmh => RbCompat.GetVelocity(Body).magnitude * 3.6f;
        public float ForwardSpeedKmh => Vector3.Dot(RbCompat.GetVelocity(Body), transform.forward) * 3.6f;
        public float DistanceDriven { get; private set; }
        AudioSource _engine, _siren, _horn, _fx; float _lastTick; bool _prevBlinker; float _rpm01; float _spin;
        public CarModel Model => Visual != null ? Visual.Model : null;

        public static PlayerCar Create(BodyKind kind, CarLivery liv, Vector3 position, Quaternion rotation, bool lights = true)
        {
            var vv = VehicleFactory.Create(kind, liv, lights, "PlayerCar");
            var go = vv.gameObject; go.transform.SetPositionAndRotation(position, rotation);
            var rb = go.AddComponent<Rigidbody>(); rb.mass = 1750f; RbCompat.SetDamping(rb, 0.02f, 0.3f); rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var car = go.AddComponent<PlayerCar>(); car.Visual = vv; car.Body = rb; car.Build();
            return car;
        }

        void Build()
        {
            var m = Visual.Model; float w = m.Width, h = m.Height, l = m.Length;
            var low = gameObject.AddComponent<BoxCollider>(); low.center = new Vector3(0, 0.62f, 0); low.size = new Vector3(w * 0.96f, 0.55f, l * 0.97f); PhysCompat.MakeSlippery(low);
            var cab = gameObject.AddComponent<BoxCollider>(); cab.center = new Vector3(0, 1.08f, -0.25f); cab.size = new Vector3(w * 0.84f, Mathf.Max(0.3f, h - 1.0f), l * 0.5f); PhysCompat.MakeSlippery(cab);
            Body.centerOfMass = new Vector3(0f, 0.38f, 0.05f);
            var holder = new GameObject("Wheels"); holder.transform.SetParent(transform, false);
            for (int i = 0; i < 4; i++)
            {
                var c = m.WheelCentres[i]; var wg = new GameObject("WC" + i); wg.transform.SetParent(holder.transform, false); wg.transform.localPosition = new Vector3(c[0], c[1] + 0.10f, c[2]);
                var wc = wg.AddComponent<WheelCollider>(); wc.radius = m.WheelRadius; wc.mass = 22f; wc.suspensionDistance = 0.24f; wc.wheelDampingRate = 0.6f; wc.forceAppPointDistance = 0.1f;
                wc.suspensionSpring = new JointSpring { spring = 44000f, damper = 5200f, targetPosition = 0.5f };
                wc.forwardFriction = new WheelFrictionCurve { extremumSlip = 0.4f, extremumValue = 1.05f, asymptoteSlip = 0.8f, asymptoteValue = 0.62f, stiffness = 1.7f };
                wc.sidewaysFriction = new WheelFrictionCurve { extremumSlip = 0.28f, extremumValue = 1.1f, asymptoteSlip = 0.6f, asymptoteValue = 0.7f, stiffness = 2.0f };
                Wheels[i] = wc;
            }
            _engine = AddSource("Engine", ProceduralAudio.Engine(), true, 0.5f, 60f); _siren = AddSource("Siren", ProceduralAudio.Siren(), true, 1.0f, 300f); _horn = AddSource("Horn", ProceduralAudio.Horn(), true, 0.9f, 120f);
            _fx = AddSource("Fx", null, false, 1f, 80f); _siren.volume = 0.0f;
        }

        AudioSource AddSource(string name, AudioClip clip, bool loop, float vol, float maxDist)
        {
            var g = new GameObject(name); g.transform.SetParent(transform, false); var a = g.AddComponent<AudioSource>(); a.clip = clip; a.loop = loop; a.volume = vol; a.spatialBlend = 1f; a.minDistance = 4f; a.maxDistance = maxDist; a.rolloffMode = AudioRolloffMode.Linear; a.dopplerLevel = 0.6f;
            if (clip != null && loop) a.Play(); return a;
        }

        public void SetSiren(bool on) { Siren = on; if (Visual != null) { } if (_siren != null) { _siren.volume = on ? 0.9f : 0f; } }
        public void SetBlue(bool on) { if (Visual != null) Visual.Blue = on; }

        void FixedUpdate()
        {
            float fwd = ForwardSpeedKmh, absSpeed = Mathf.Abs(fwd); float t = HasDriver && EngineOn && Damage01 < 1f && Fuel01 > 0f ? Throttle : Mathf.Min(0f, Throttle) * 0f;
            float motor = 0f, brake = 0f;
            if (!HasDriver) { brake = MaxBrakeTorque * 0.35f; }
            else if (t > 0.01f) { if (fwd < -1.5f) brake = MaxBrakeTorque * t; else if (fwd < MaxSpeedKmh) motor = MaxMotorTorque * t * Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(absSpeed / MaxSpeedKmh)); }
            else if (Throttle < -0.01f) { if (fwd > 1.5f) brake = MaxBrakeTorque * -Throttle; else if (fwd > -ReverseMaxKmh) motor = MaxMotorTorque * 0.55f * Throttle; }
            else brake = 90f + absSpeed * 1.2f;                                    // engine braking / rolling resistance
            if (Handbrake && HasDriver) brake = MaxBrakeTorque;
            float steerLimit = Mathf.Lerp(MaxSteer, HighSpeedSteer, Mathf.Clamp01(absSpeed / HighSpeedKmh));
            float steerTarget = HasDriver ? Steer * steerLimit : 0f;
            for (int i = 0; i < 4; i++)
            {
                var w = Wheels[i]; if (w == null) continue;
                w.motorTorque = motor; w.brakeTorque = (Handbrake && i >= 2) ? MaxBrakeTorque * 1.3f : (i < 2 ? brake * 1.1f : brake * 0.9f);
                if (i < 2) w.steerAngle = Mathf.MoveTowards(w.steerAngle, steerTarget, 280f * Time.fixedDeltaTime);
            }
            AntiRollBar(0, 1); AntiRollBar(2, 3);
            var v = RbCompat.GetVelocity(Body);
            Body.AddForce(-transform.up * (Downforce * v.sqrMagnitude * 0.01f));
            Body.AddForce(-v.normalized * (0.9f * v.sqrMagnitude));                // aerodynamic drag
            // lateral grip assist: removes part of the sideways velocity when at least one wheel touches the ground
            bool grounded = false; for (int i = 0; i < 4; i++) if (Wheels[i] != null && Wheels[i].isGrounded) { grounded = true; break; }
            if (grounded && !Handbrake)
            {
                var lat = Vector3.Project(v, transform.right); float k = LateralGrip * Mathf.Clamp01(absSpeed / 25f);
                Body.AddForce(-lat * (k * Body.mass * 0.35f), ForceMode.Force);
            }
            DistanceDriven += Mathf.Abs(Vector3.Dot(v, transform.forward)) * Time.fixedDeltaTime;
            if (HasDriver && EngineOn) Fuel01 = Mathf.Max(0f, Fuel01 - (0.00000012f + Mathf.Max(0f, Throttle) * 0.00000055f * (1f + absSpeed / 120f)) * 60f * Time.fixedDeltaTime * 8f);
        }

        void AntiRollBar(int left, int right)
        {
            var l = Wheels[left]; var r = Wheels[right]; if (l == null || r == null) return; float travelL = 1f, travelR = 1f;
            bool gL = l.GetGroundHit(out var hl), gR = r.GetGroundHit(out var hr);
            if (gL) travelL = (-l.transform.InverseTransformPoint(hl.point).y - l.radius) / l.suspensionDistance;
            if (gR) travelR = (-r.transform.InverseTransformPoint(hr.point).y - r.radius) / r.suspensionDistance;
            float force = (travelL - travelR) * AntiRoll;
            if (gL) Body.AddForceAtPosition(l.transform.up * -force, l.transform.position);
            if (gR) Body.AddForceAtPosition(r.transform.up * force, r.transform.position);
        }

        void Update()
        {
            for (int i = 0; i < 4; i++) { if (Wheels[i] == null) continue; Wheels[i].GetWorldPose(out var pos, out var rot); Visual.SetWheelPose(i, pos, rot); }
            Visual.Brake = HasDriver && (Throttle < -0.05f && ForwardSpeedKmh > 1f || Handbrake) ;
            Visual.Reverse = HasDriver && ForwardSpeedKmh < -0.5f;
            // engine sound
            float v = SpeedKmh; int gear = v < 22 ? 1 : v < 45 ? 2 : v < 75 ? 3 : v < 110 ? 4 : v < 150 ? 5 : 6; float[] lo = { 0, 0, 22, 45, 75, 110, 150 }, hi = { 0, 22, 45, 75, 110, 150, 215 };
            _rpm01 = Mathf.Lerp(_rpm01, 0.12f + 0.88f * Mathf.InverseLerp(lo[gear], hi[gear], v) * 0.9f + Mathf.Max(0f, Throttle) * 0.1f, Time.deltaTime * 6f);
            if (_engine != null) { bool on = EngineOn && Damage01 < 1f && Fuel01 > 0f; _engine.volume = on ? (HasDriver ? 0.25f + 0.35f * Mathf.Abs(Throttle) : 0.12f) * (0.6f + _rpm01 * 0.6f) : 0f; _engine.pitch = 0.55f + _rpm01 * 1.6f; }
            if (_horn != null) _horn.volume = Horn ? 0.9f : 0f;
            if (_siren != null) _siren.volume = Siren ? 1f : 0f;
            // indicator tick
            bool blink = Visual.IndLeft || Visual.IndRight || Visual.Hazard;
            if (blink && Time.time - _lastTick > 0.357f) { _lastTick = Time.time; if (_fx != null) _fx.PlayOneShot(ProceduralAudio.IndicatorTick(), 0.5f); }
            _prevBlinker = blink;
            if (Damage01 > 0.55f && Damage01 < 1f && Time.frameCount % 90 == 0) { /* hook for smoke effects */ }
        }

        void OnCollisionEnter(Collision c)
        {
            float impulse = c.impulse.magnitude; float rel = c.relativeVelocity.magnitude;
            if (rel > 3f)
            {
                Damage01 = Mathf.Clamp01(Damage01 + rel * rel * 0.00085f); if (_fx != null) _fx.PlayOneShot(ProceduralAudio.Crash(), Mathf.Clamp01(rel / 14f));
                Collided?.Invoke(rel, c);
            }
        }

        public void Repair() { Damage01 = 0f; }
        public void Refuel() { Fuel01 = 1f; }
        public void Teleport(Vector3 pos, Quaternion rot) { Body.position = pos; Body.rotation = rot; RbCompat.SetVelocity(Body, Vector3.zero); Body.angularVelocity = Vector3.zero; transform.SetPositionAndRotation(pos, rot); }
    }
}
