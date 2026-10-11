using System;
using SwissPoliceSim.Core.Game;
using SwissPoliceSim.Core.Legal;
using SwissPoliceSim.Core.Traffic;
using SwissPoliceSim.World;
using UnityEngine;

namespace SwissPoliceSim.Game
{
    /// <summary>Pursuit of a fleeing vehicle: contact tracking, helicopter, roadblock, abort, and the legal/safety judgement of how the officer drove.</summary>
    public sealed class PursuitController : MonoBehaviour
    {
        public readonly PursuitManager Core = new PursuitManager(); public TrafficVehicle Target; public bool IsActive => Core.Active; public float RiskScore; public int NearMisses; float _assessTimer, _heliTimer; public bool RoadblockReady = true;
        GameSession S => GameSession.I;

        public bool Begin(TrafficVehicle v, OffenceClass offence, Suspicion suspicion)
        {
            if (v == null || S.Traffic.Sim == null) return false; Target = v; RiskScore = 0; NearMisses = 0; _assessTimer = 0; RoadblockReady = true; return Core.Start(S.Traffic.Sim, v, offence, suspicion);
        }

        void Update()
        {
            if (!IsActive || S == null || S.Traffic.Sim == null) return;
            var p = S.PlayerLv95; Core.Update(S.Traffic.Sim, Time.deltaTime, p.E, p.N);
            _assessTimer += Time.deltaTime;
            if (_assessTimer > 4f) { _assessTimer = 0; float tv = Core.TargetKmh; bool urbanHere = S.UrbanHere; if ((urbanHere && tv > 90f) || tv > 160f) RiskScore += 1f; if (S.Car != null && S.Car.SpeedKmh > (urbanHere ? 110f : 190f)) RiskScore += 1f; }
            if (!IsActive) End();
        }

        public void CallHelicopter() { if (!IsActive) return; Core.CallHelicopter(); }

        public void Roadblock()
        {
            if (!IsActive || !RoadblockReady || Target == null) return; var sim = S.Traffic.Sim; var net = sim.Net; if (Target.Link < 0 || Target.Link >= net.Links.Count) return;
            double dist = 0; int link = Target.Link; var l = net.Links[link]; double e = Target.E, n = Target.N; int hops = 0;
            while (dist < 520 && hops < 6) { dist += l.Length - (hops == 0 ? Target.S : 0); if (l.Out.Count == 0) break; link = l.Out[0]; l = net.Links[link]; hops++; }
            l.PointAt(l.Length * 0.5f, out e, out n, out _, out _); Core.SetRoadblock(e, n); RoadblockReady = false;
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = "Strassensperre"; Destroy(g.GetComponent<Collider>()); var h = S.Streamer.Height(e, n); g.transform.position = WorldOrigin.ToUnity(e, n, (float.IsNaN(h) ? 0 : h) + 0.6f); g.transform.localScale = new Vector3(6f, 1.1f, 0.6f); g.GetComponent<MeshRenderer>().sharedMaterial = SwissPoliceSim.Rendering.MaterialLibrary.Make("roadblock", null, new Color(0.9f, 0.3f, 0.1f), 0.2f, 0f, false); Destroy(g, 120f);
        }

        public void Abort() { if (IsActive) { Core.Abort("durch die Einsatzleitung"); End(); } }

        void End()
        {
            var outcome = Core.Outcome; bool lawfulDriving = RiskScore < 3; bool risk = RiskScore >= 3 && Core.Offence <= OffenceClass.Uebertretung;
            bool blue = S.Car != null && S.Car.Visual != null && S.Car.Visual.Blue && S.Car.Siren;
            var a = Core.Assess(blue, lawfulDriving, risk, S.Jurisdiction.InOwnTerritory, true); S.Judge(a, 0.3f);
            switch (outcome)
            {
                case PursuitOutcome.Stopped: Notifier.Good("Verfolgung beendet: Fahrzeug gestoppt."); S.Career.Xp += 80; S.Career.RewardDeEscalation(); BeginStopEncounter(); break;
                case PursuitOutcome.Escaped: Notifier.Warn("Verfolgung abgebrochen: Fahrzeug entkommen. Fahndung der Zentrale läuft."); break;
                case PursuitOutcome.Crashed: Notifier.Warn("Das Fluchtfahrzeug ist verunfallt. Unfallstelle sichern und Rettung aufbieten."); break;
                case PursuitOutcome.Aborted: Notifier.Push("Verfolgung abgebrochen."); break;
            }
            Target = null;
        }

        void BeginStopEncounter()
        {
            if (Target == null) return; var citizen = S.CitizenForVehicle(Target); Target.Mode = DriverMode.Stopped; Target.Hazard = true;
            var m = new Measurement { VehicleId = Target.Id, Plate = Target.Plate, Zone = SpeedZone.Urban, Time = Time.time }; S.Encounters.BeginWithVehicle(Target, citizen, null);
            if (S.Encounters.Current != null) { S.Encounters.Current.Fled = true; S.Encounters.Current.Ctx.Suspicion = Suspicion.CaughtInAct; S.Encounters.Current.Ctx.Offence = OffenceClass.Uebertretung; S.Encounters.Current.TrueViolation = ViolationType.DangerousDriving; }
        }
    }
}
