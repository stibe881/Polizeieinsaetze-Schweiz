using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Legal;
using SwissPoliceSim.Core.Roads;
using SwissPoliceSim.Core.Traffic;
using SwissPoliceSim.Player;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.Game
{
    public sealed class Measurement
    {
        public int VehicleId; public string Plate = ""; public float Kmh; public int Limit; public SpeedZone Zone; public float Time; public float Distance; public SpeedingFine Fine; public string Street = ""; public string Colour = "";
    }

    /// <summary>
    /// Traffic enforcement: hand-held speed measurement (R toggles, left click measures with ±3 km/h tolerance applied by the law rules), the stop signal (G) that makes a vehicle pull over
    /// or flee, and the approach prompt (E) once the car is stopped.
    /// </summary>
    public sealed class EnforcementController : MonoBehaviour
    {
        public bool RadarActive; public Measurement Last; public TrafficVehicle Target; public TrafficVehicle StoppedVehicle; public string Prompt = ""; public float AimDistance; public TrafficVehicle Aimed;
        public readonly List<Measurement> History = new List<Measurement>(); float _stopRequestTime; System.Random _rnd = new System.Random(3);
        static readonly string[] Colours = { "weiss", "schwarz", "silber", "grau", "blau", "rot", "grün", "braun", "beige", "orange", "gelb", "bordeaux" };
        GameSession S => GameSession.I;

        void Update()
        {
            if (S == null || !S.Playing || S.Paused || S.Traffic == null || S.Traffic.Sim == null || UiState.Blocking) { Prompt = ""; return; }
            Prompt = "";
            Aimed = FindAimed();
            if (GameInput.Radar()) { RadarActive = !RadarActive; Notifier.Push(RadarActive ? "Lasermessgerät eingeschaltet. Linksklick = Messung." : "Lasermessgerät ausgeschaltet."); }
            if (RadarActive && GameInput.Fire1() && Aimed != null) Measure(Aimed);
            if (GameInput.StopVehicle()) SignalStop();
            TrackStopped();
            if (StoppedVehicle != null && !S.Encounters.InEncounter)
            {
                float d = Vector3.Distance(S.PlayerPos, VehiclePos(StoppedVehicle));
                if (d < 7f && !S.Player.InVehicle) { Prompt = "[E] Fahrer ansprechen"; if (GameInput.Talk()) Approach(); } else if (d < 7f && S.Player.InVehicle) Prompt = "Aussteigen [F], um den Fahrer anzusprechen";
            }
        }

        Vector3 VehiclePos(TrafficVehicle v) { return WorldOrigin.ToUnity(v.E, v.N, S.Streamer.Height(v.E, v.N) is float h && !float.IsNaN(h) ? h + 0.8f : S.PlayerPos.y); }

        /// <summary>The traffic vehicle closest to the camera centre line (within 5°) up to 450 m with a clear line of sight.</summary>
        TrafficVehicle FindAimed()
        {
            var cam = S.Rig != null ? S.Rig.Cam : Camera.main; if (cam == null) return null; var o = cam.transform.position; var f = cam.transform.forward; TrafficVehicle best = null; float bestAng = 5f; AimDistance = 0;
            foreach (var v in S.Traffic.Sim.Vehicles)
            {
                var p = VehiclePos(v); var d = p - o; float dist = d.magnitude; if (dist > 450f || dist < 4f) continue; float ang = Vector3.Angle(f, d); float tol = Mathf.Lerp(5f, 1.2f, Mathf.Clamp01(dist / 300f)); if (ang > tol || ang > bestAng) continue;
                if (Physics.Raycast(o, d.normalized, out var hit, dist - 2f, ~0, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(S.Player.transform) && hit.distance < dist - 6f && !hit.collider.GetComponentInParent<SwissPoliceSim.Vehicles.VehicleVisual>()) continue;
                bestAng = ang; best = v; AimDistance = dist;
            }
            return best;
        }

        public void Measure(TrafficVehicle v)
        {
            int limit = 50; var zone = SpeedZone.Urban; string street = ""; if (WorldQuery.NearestRoad(S.Streamer, v.E, v.N, 20, out var ri)) { limit = ri.SpeedLimitKmh; street = ri.Name; zone = ri.Way.Class == RoadClass.Motorway ? SpeedZone.Motorway : ri.Way.Motorroad ? SpeedZone.Motorroad : ri.Urban ? SpeedZone.Urban : SpeedZone.Rural; }
            float kmh = v.V * 3.6f + (float)(_rnd.NextDouble() - 0.5) * 0.8f; int measured = Mathf.RoundToInt(kmh);
            var m = new Measurement { VehicleId = v.Id, Plate = v.Plate, Kmh = measured, Limit = limit, Zone = zone, Time = Time.time, Distance = AimDistance, Street = street, Colour = Colours[((v.ColorIndex % 12) + 12) % 12], Fine = SpeedingRules.Assess(S.Obv, measured, limit, zone) };
            Last = m; History.Add(m); if (History.Count > 30) History.RemoveAt(0); Target = v; Audio.ProceduralAudio.Beep(1200f, 0.08f);
            if (m.Fine.IsViolation) Notifier.Push($"Messung: {measured} km/h bei erlaubten {limit} km/h → nach Toleranz +{m.Fine.ExcessKmh} km/h" + (m.Fine.IsOrdnungsbusse ? $" · Ordnungsbusse {m.Fine.Amount} CHF" : " · über dem Ordnungsbussenbereich: Verzeigung") + ". [G] = Anhalten", NoteKind.Warn, 9f);
            else Notifier.Push($"Messung: {measured} km/h bei erlaubten {limit} km/h – keine Überschreitung (Toleranz {SpeedingRules.Tolerance(measured)} km/h).", NoteKind.Info, 6f);
        }

        void SignalStop()
        {
            if (S.Encounters.InEncounter) return; var cam = S.Rig != null ? S.Rig.Cam : Camera.main; TrafficVehicle target = null;
            if (Last != null && Time.time - Last.Time < 25f) target = S.Traffic.FindVehicle(Last.VehicleId);
            if (target == null) target = Aimed;
            if (target == null) { float best = 60f; foreach (var v in S.Traffic.Sim.Vehicles) { var d = VehiclePos(v) - S.PlayerPos; float dist = d.magnitude; if (dist > best || Vector3.Angle(cam.transform.forward, d) > 35f) continue; best = dist; target = v; } }
            if (target == null) { Notifier.Push("Kein Fahrzeug im Sichtbereich zum Anhalten.", NoteKind.Info); return; }
            bool signal = S.Player.InVehicle ? (S.Car.Visual.Blue || S.Car.Siren) : true; if (!signal) { Notifier.Warn("Zum Anhalten Blaulicht (B) oder Sirene (Q) einschalten."); return; }
            var ctx = new LegalContext { Suspicion = Last != null && Last.VehicleId == target.Id && Last.Fine != null && Last.Fine.IsViolation ? Suspicion.CaughtInAct : Suspicion.Routine, InOwnTerritory = S.Jurisdiction.InOwnTerritory, CooperationAgreement = S.Jurisdiction.CooperationAgreement };
            var a = LegalSystem.Assess(OfficerAct.StopVehicle, ctx); if (!a.Lawful) S.Judge(a);
            if (S.Traffic.Sim.RequestStop(target))
            {
                _stopRequestTime = Time.time; StoppedVehicle = target; Notifier.Push("Anhaltezeichen gegeben.", NoteKind.Info);
                if (target.Mode == DriverMode.Fleeing) { Notifier.Warn("Das Fahrzeug beschleunigt – es flüchtet!"); S.Pursuit.Begin(target, OffenceClass.Uebertretung, Suspicion.Concrete); StoppedVehicle = null; }
            }
            else Notifier.Push("Das Fahrzeug reagiert nicht.", NoteKind.Warn);
        }

        void TrackStopped()
        {
            if (StoppedVehicle == null) return;
            if (!S.Traffic.Sim.Vehicles.Contains(StoppedVehicle) || StoppedVehicle.Mode == DriverMode.Crashed) { StoppedVehicle = null; return; }
            if (StoppedVehicle.Mode == DriverMode.Driving && Time.time - _stopRequestTime > 25f) { StoppedVehicle = null; }
            if (StoppedVehicle != null && StoppedVehicle.Mode == DriverMode.Fleeing && !S.Pursuit.IsActive) { S.Pursuit.Begin(StoppedVehicle, OffenceClass.Uebertretung, Suspicion.Concrete); StoppedVehicle = null; }
            if (StoppedVehicle != null && StoppedVehicle.Mode == DriverMode.Stopped && Time.time - _stopRequestTime > 3f && Prompt == "" && !S.Encounters.InEncounter && Vector3.Distance(S.PlayerPos, VehiclePos(StoppedVehicle)) > 60f && (int)Time.time % 20 == 0) { }
        }

        void Approach()
        {
            var v = StoppedVehicle; if (v == null) return; var citizen = S.CitizenForVehicle(v); Measurement m = Last != null && Last.VehicleId == v.Id ? Last : null;
            S.Encounters.BeginWithVehicle(v, citizen, m); StoppedVehicle = v;
        }
    }
}
