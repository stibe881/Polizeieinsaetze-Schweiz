using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Legal;
using SwissPoliceSim.Core.Traffic;

namespace SwissPoliceSim.Core.Game
{
    public enum PursuitOutcome { None, Active, Stopped, Escaped, Crashed, Aborted }

    /// <summary>
    /// A pursuit of a fleeing vehicle from the traffic simulation: contact tracking, escape logic, helicopter and roadblock support and the
    /// proportionality assessment (a pursuit for a minor offence that endangers others has to be aborted).
    /// </summary>
    public sealed class PursuitManager
    {
        public TrafficVehicle Target; public PursuitOutcome Outcome = PursuitOutcome.None;
        public float Elapsed, LostFor, DistanceM, TargetKmh, MaxTargetKmh; public int RedLightsRun, NearMisses;
        public bool Helicopter, RoadblockActive, Inside = false; public double RoadblockE, RoadblockN;
        public OffenceClass Offence = OffenceClass.Vergehen; public Suspicion Suspicion = Suspicion.Concrete;
        public readonly List<string> Events = new List<string>();
        public bool Active => Outcome == PursuitOutcome.Active;
        const float EscapeDistance = 700f, LoseContactDistance = 320f, LoseContactSeconds = 40f;

        public bool Start(TrafficSimulation sim, TrafficVehicle target, OffenceClass offence, Suspicion suspicion)
        {
            if (target == null || target.Mode == DriverMode.Crashed) return false;
            Target = target; Offence = offence; Suspicion = suspicion; Outcome = PursuitOutcome.Active; Elapsed = LostFor = 0; MaxTargetKmh = 0;
            target.Mode = DriverMode.Fleeing; target.V0Factor = 1.55f + 0.25f * target.Driver.Aggression; target.TimeInMode = 0; target.Driver.RunsRedLights = true; target.Hazard = false; target.Indicator = 0;
            Events.Add("Flucht des Fahrzeugs " + target.Plate + " – Verfolgung beginnt."); return true;
        }

        public void CallHelicopter() { if (!Helicopter) { Helicopter = true; Events.Add("Helikopter der Polizei übernimmt die Beobachtung."); } }
        public void SetRoadblock(double e, double n) { RoadblockActive = true; RoadblockE = e; RoadblockN = n; Events.Add("Strassensperre eingerichtet."); }

        public void Update(TrafficSimulation sim, float dt, double officerE, double officerN)
        {
            if (!Active) return;
            Elapsed += dt;
            if (Target.Mode == DriverMode.Crashed) { Outcome = PursuitOutcome.Crashed; Events.Add("Das Fluchtfahrzeug ist verunfallt."); return; }
            if (Target.Mode == DriverMode.Stopped || (Target.V < 0.3f && Target.TimeInMode > 4f && Elapsed > 6f)) { Outcome = PursuitOutcome.Stopped; Events.Add("Das Fahrzeug wurde angehalten."); return; }
            double de = Target.E - officerE, dn = Target.N - officerN; DistanceM = (float)Math.Sqrt(de * de + dn * dn);
            TargetKmh = Target.V * 3.6f; MaxTargetKmh = Math.Max(MaxTargetKmh, TargetKmh);
            if (RoadblockActive)
            {
                double be = Target.E - RoadblockE, bn = Target.N - RoadblockN;
                if (be * be + bn * bn < 35 * 35) { Target.V0Factor = 0.05f; Target.Driver.Aggression *= 0.5f; Events.Add("Fluchtfahrzeug an der Strassensperre gestoppt."); Target.Mode = DriverMode.Stopped; Outcome = PursuitOutcome.Stopped; return; }
            }
            if (DistanceM > LoseContactDistance && !Helicopter) LostFor += dt; else LostFor = Math.Max(0, LostFor - dt * 2);
            if (LostFor > LoseContactSeconds || DistanceM > EscapeDistance && !Helicopter)
            {   // the driver slows down and blends back into traffic
                Outcome = PursuitOutcome.Escaped; Target.Mode = DriverMode.Driving; Target.V0Factor = 1.0f; Events.Add("Kontakt verloren – das Fahrzeug ist entkommen."); return;
            }
            // a driver who feels safe slows down again after a while
            if (Elapsed > 240f) { Target.V0Factor = Math.Max(0.4f, Target.V0Factor - 0.01f); }
        }

        public void Abort(string reason) { if (!Active) return; Outcome = PursuitOutcome.Aborted; if (Target != null) { Target.V0Factor = 1f; Target.Mode = DriverMode.Driving; } Events.Add("Verfolgung abgebrochen: " + reason); }

        /// <summary>Legal assessment of the pursuit as conducted so far.</summary>
        public LegalAssessment Assess(bool officerBlueLightsAndSiren, bool officerDrivesCarefully, bool riskToOthers, bool inOwnTerritory, bool acrossBorderUrgent)
        {
            var ctx = new LegalContext
            {
                Suspicion = Suspicion, Offence = Offence, RiskToOthers = riskToOthers, BlueLights = officerBlueLightsAndSiren, Siren = officerBlueLightsAndSiren, CarefulDriving = officerDrivesCarefully,
                Urgent = true, InOwnTerritory = inOwnTerritory, InPursuit = true
            };
            var a = LegalSystem.Assess(OfficerAct.Pursuit, ctx);
            var b = LegalSystem.Assess(OfficerAct.EmergencyDrive, ctx);
            foreach (var n in b.Norms) if (!a.Norms.Contains(n)) a.Norms.Add(n);
            if (!b.Lawful) { a.Lawful = false; a.Severity = Math.Max(a.Severity, b.Severity); a.Reasons.AddRange(b.Reasons); }
            if (!inOwnTerritory) { var c = LegalSystem.Assess(OfficerAct.CrossCantonBorder, ctx); if (!c.Lawful) { a.Lawful = false; a.Severity = Math.Max(a.Severity, c.Severity); a.Reasons.AddRange(c.Reasons); } }
            return a;
        }
    }
}
