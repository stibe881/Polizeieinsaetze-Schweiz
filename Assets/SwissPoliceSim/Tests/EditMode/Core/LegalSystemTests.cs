using NUnit.Framework;
using SwissPoliceSim.Core.Legal;

namespace SwissPoliceSim.Tests
{
    public class LegalSystemTests
    {
        static LegalContext Ctx() => new LegalContext();

        [Test] public void RoutineTrafficStop_Documents_BreathTest_AreLawful()
        {
            var c = Ctx();
            Assert.IsTrue(LegalSystem.Assess(OfficerAct.StopVehicle, c).Lawful);
            Assert.IsTrue(LegalSystem.Assess(OfficerAct.RequestDocuments, c).Lawful);
            var b = LegalSystem.Assess(OfficerAct.BreathTest, c); Assert.IsTrue(b.Lawful); CollectionAssert.Contains(b.Norms.ConvertAll(n => n.Id), "BREATH_TEST");
        }

        [Test] public void SearchingAPersonWithoutAnyBasis_IsUnlawful()
        {
            var c = Ctx(); c.Suspicion = Suspicion.Routine;
            var a = LegalSystem.Assess(OfficerAct.SearchPerson, c);
            Assert.IsFalse(a.Lawful); Assert.GreaterOrEqual(a.Severity, 2);
            c.PersonDetained = true; Assert.IsTrue(LegalSystem.Assess(OfficerAct.SearchPerson, c).Lawful);       // Art. 241 Abs. 4 StPO
            c.PersonDetained = false; c.ConsentGiven = true; Assert.IsTrue(LegalSystem.Assess(OfficerAct.SearchPerson, c).Lawful);
        }

        [Test] public void VehicleSearch_NeedsConsentOrSuspicion()
        {
            var c = Ctx(); Assert.IsFalse(LegalSystem.Assess(OfficerAct.SearchVehicle, c).Lawful);
            c.Suspicion = Suspicion.Concrete; c.PersonDetained = true; Assert.IsTrue(LegalSystem.Assess(OfficerAct.SearchVehicle, c).Lawful);
        }

        [Test] public void Arrest_RulesFollowArt217()
        {
            var c = Ctx(); c.Offence = OffenceClass.Vergehen; c.Suspicion = Suspicion.CaughtInAct;
            Assert.IsTrue(LegalSystem.Assess(OfficerAct.Arrest, c).Lawful);
            c.Offence = OffenceClass.None; c.Suspicion = Suspicion.Routine;
            var bad = LegalSystem.Assess(OfficerAct.Arrest, c); Assert.IsFalse(bad.Lawful); Assert.AreEqual(3, bad.Severity);
            c.Offence = OffenceClass.Uebertretung; c.Suspicion = Suspicion.CaughtInAct;
            Assert.IsFalse(LegalSystem.Assess(OfficerAct.Arrest, c).Lawful, "Übertretung: nur unter den engen Voraussetzungen von Art. 217 Abs. 3");
        }

        [Test] public void Holding_Over24Hours_IsUnlawful()
        {
            var c = Ctx(); c.MinutesInCustody = 25 * 60; Assert.IsFalse(LegalSystem.Assess(OfficerAct.HoldBeyond24h, c).Lawful);
            c.MinutesInCustody = 3 * 60; Assert.IsTrue(LegalSystem.Assess(OfficerAct.HoldBeyond24h, c).Lawful);
        }

        [TestCase(Resistance.None, OfficerAct.Handcuff, false)]
        [TestCase(Resistance.Passive, OfficerAct.PhysicalForce, true)]
        [TestCase(Resistance.Passive, OfficerAct.PepperSpray, false)]
        [TestCase(Resistance.Active, OfficerAct.PepperSpray, true)]
        [TestCase(Resistance.Active, OfficerAct.Baton, false)]
        [TestCase(Resistance.Violent, OfficerAct.Baton, true)]
        public void UseOfForce_IsProportionate(Resistance r, OfficerAct act, bool lawful)
        {
            var c = Ctx(); c.Resistance = r; Assert.AreEqual(lawful, LegalSystem.Assess(act, c).Lawful, $"{act} vs {r}");
        }

        [Test] public void Firearms_OnlyAsLastResort()
        {
            var c = Ctx(); c.Resistance = Resistance.Passive;
            Assert.IsFalse(LegalSystem.Assess(OfficerAct.DrawFirearm, c).Lawful);
            Assert.IsFalse(LegalSystem.Assess(OfficerAct.FireAtPerson, c).Lawful);
            c.Resistance = Resistance.Armed; c.SuspectArmed = true; c.ThreatToLife = true; c.GravityOffenceSerious = true; c.PreviousForce = UseOfForceLevel.Hold; c.WarningGiven = true;
            Assert.IsTrue(LegalSystem.Assess(OfficerAct.DrawFirearm, c).Lawful);
            Assert.IsTrue(LegalSystem.Assess(OfficerAct.FireAtPerson, c).Lawful);
            c.WarningGiven = false; Assert.IsFalse(LegalSystem.Assess(OfficerAct.WarningShot, c).Lawful);
        }

        [Test] public void DataQuery_NeedsOfficialPurpose()
        {
            var c = Ctx(); Assert.IsTrue(LegalSystem.Assess(OfficerAct.QueryPersonData, c).Lawful);
            c.OfficialPurpose = false; var a = LegalSystem.Assess(OfficerAct.QueryPersonData, c); Assert.IsFalse(a.Lawful); Assert.AreEqual(3, a.Severity);
        }

        [Test] public void EmergencyDrive_NeedsWarningSignalsAndCare()
        {
            var c = Ctx(); c.Urgent = true; c.BlueLights = true; c.Siren = true; c.CarefulDriving = true;
            Assert.IsTrue(LegalSystem.Assess(OfficerAct.EmergencyDrive, c).Lawful);
            c.Siren = false; Assert.IsFalse(LegalSystem.Assess(OfficerAct.EmergencyDrive, c).Lawful);
            c.Siren = true; c.RiskToOthers = true; Assert.IsFalse(LegalSystem.Assess(OfficerAct.EmergencyDrive, c).Lawful);
            c.Urgent = false; c.RiskToOthers = false; Assert.IsFalse(LegalSystem.Assess(OfficerAct.EmergencyDrive, c).Lawful, "ohne Dringlichkeit keine Sonderrechte");
        }

        [Test] public void Jurisdiction_OutsideOwnCanton_NeedsLegalBasis()
        {
            var c = Ctx(); c.InOwnTerritory = false; c.Suspicion = Suspicion.Concrete;
            Assert.IsFalse(LegalSystem.Assess(OfficerAct.StopVehicle, c).Lawful);
            c.InPursuit = true; Assert.IsTrue(LegalSystem.Assess(OfficerAct.StopVehicle, c).Lawful, "Nacheile Art. 216 StPO");
            c = Ctx(); c.InOwnTerritory = false; c.RequestedByLocalPolice = true; Assert.IsTrue(LegalSystem.Assess(OfficerAct.StopVehicle, c).Lawful);
            c = Ctx(); Assert.IsFalse(LegalSystem.Assess(OfficerAct.CrossCantonBorder, new LegalContext { Urgent = false }).Lawful);
            Assert.IsTrue(LegalSystem.Assess(OfficerAct.CrossCantonBorder, new LegalContext { InPursuit = true, Urgent = true, InOwnTerritory = true }).Lawful);
        }

        [Test] public void Assessment_ExposesVerificationStatus()
        {
            var a = LegalSystem.Assess(OfficerAct.StopVehicle, Ctx());
            Assert.AreEqual(Verification.FedlexVerified, a.WeakestStatus);
            var b = LegalSystem.Assess(OfficerAct.IdentityCheck, new LegalContext { Suspicion = Suspicion.None, SuspectIsDriver = false });
            Assert.AreEqual(Verification.CantonalNotVerified, b.WeakestStatus);
            StringAssert.Contains("Rechtsgrundlagen", b.Describe());
        }
    }
}
