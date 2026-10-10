using System;
using System.Linq;
using NUnit.Framework;
using SwissPoliceSim.Core.Npc;
using SwissPoliceSim.Core.Traffic;

namespace SwissPoliceSim.Tests
{
    public class NluTests
    {
        [Test] public void German_GreetingAndDocuments()
        {
            var p = Nlu.Parse("Grüezi, Führerausweis und Fahrzeugausweis bitte.");
            Assert.IsTrue(p.Has(Intent.Greeting)); Assert.IsTrue(p.Has(Intent.RequestLicense)); Assert.IsTrue(p.Has(Intent.RequestRegistration)); Assert.Greater(p.Politeness, 0.3f);
        }
        [Test] public void German_KnowsReasonQuestion() => Assert.IsTrue(Nlu.Parse("Wissen Sie, weshalb ich Sie angehalten habe?").Has(Intent.AskIfKnowsReason));
        [Test] public void German_RedLight()
        {
            var p = Nlu.Parse("Sie sind beim Bahnhof über Rot gefahren."); Assert.IsTrue(p.Has(Intent.StateReason)); Assert.AreEqual(ViolationType.RedLight, p.Violation);
        }
        [Test] public void Speeding_WithNumber()
        {
            var p = Nlu.Parse("Sie waren mit 78 km/h unterwegs, erlaubt sind 50."); Assert.AreEqual(ViolationType.Speeding, p.Violation); Assert.AreEqual(78, p.Number);
        }
        [Test] public void French_And_Italian()
        {
            var f = Nlu.Parse("Bonjour, votre permis de conduire s'il vous plaît."); Assert.AreEqual("fr", f.Language); Assert.IsTrue(f.Has(Intent.Greeting)); Assert.IsTrue(f.Has(Intent.RequestLicense));
            var i = Nlu.Parse("Buongiorno, la sua patente per favore."); Assert.AreEqual("it", i.Language); Assert.IsTrue(i.Has(Intent.RequestLicense));
            Assert.AreEqual("en", Nlu.Parse("Please step out of the car, do you know why I stopped you?").Language);
            Assert.IsTrue(Nlu.Parse("Savez-vous pourquoi je vous ai arrêté ?").Has(Intent.AskIfKnowsReason));
        }
        [Test] public void Rudeness_LowersPoliteness()
        {
            var p = Nlu.Parse("Sie Idiot, aussteigen!"); Assert.IsTrue(p.Has(Intent.Insult)); Assert.IsTrue(p.Has(Intent.RequestStepOut)); Assert.Less(p.Politeness, 0);
        }
        [Test] public void BreathTest_AndSearch()
        {
            Assert.IsTrue(Nlu.Parse("Ich möchte einen Atemalkoholtest durchführen.").Has(Intent.RequestBreathTest));
            Assert.IsTrue(Nlu.Parse("Dürfen wir kurz den Kofferraum anschauen?").Has(Intent.RequestOpenTrunk));
            Assert.IsTrue(Nlu.Parse("Sie sind vorläufig festgenommen.").Has(Intent.AnnounceArrest));
        }
        [Test] public void EmptyInput_IsUnknown() => Assert.IsTrue(Nlu.Parse("  ").Has(Intent.Unknown));
    }

    public class NpcMindTests
    {
        static NpcMind Make(int seed, Temperament t, float honesty, ViolationType v, bool guilty, Action<Citizen> tweak = null)
        {
            var c = CitizenFactory.Create(seed, "BE", "Bern", "BE 123456", 1, 3); c.Temperament = t; c.Honesty = honesty; tweak?.Invoke(c);
            return new NpcMind(c, new NpcSituation { TrueViolation = v, NpcGuilty = guilty, LimitKmh = 50, MeasuredKmh = 71 }, seed);
        }

        [Test] public void ValidLicense_ShowsDocument_WithAction()
        {
            var m = Make(1, Temperament.Calm, 0.9f, ViolationType.Speeding, true);
            var r = m.Respond(Nlu.Parse("Grüezi, Führerausweis bitte."));
            Assert.AreEqual(NpcAction.ShowLicense, r.First); StringAssert.Contains("Grüezi", r.Text);
        }

        [Test] public void NoLicense_Person_CannotShowIt()
        {
            var m = Make(2, Temperament.Calm, 0.9f, ViolationType.Speeding, true, c => c.License = LicenseStatus.None);
            var r = m.Respond(Nlu.Parse("Führerausweis bitte."));
            Assert.AreEqual(NpcAction.RefuseDocuments, r.First);
            Assert.Greater(r.AnxietyDelta, 0);
        }

        [Test] public void HonestPeople_AdmitMoreOftenThanDishonest()
        {
            int honest = 0, dishonest = 0;
            for (int i = 0; i < 200; i++)
            {
                var a = Make(i, Temperament.Calm, 0.95f, ViolationType.Speeding, true); a.Respond(Nlu.Parse("Grüezi.")); a.Respond(Nlu.Parse("Wissen Sie, weshalb ich Sie angehalten habe?")); if (a.AdmittedViolation) honest++;
                var b = Make(i + 1000, Temperament.Arrogant, 0.1f, ViolationType.Speeding, true, c => c.Anger = 0.4f); b.Respond(Nlu.Parse("Wissen Sie, weshalb ich Sie angehalten habe?")); if (b.AdmittedViolation) dishonest++;
            }
            TestContext.WriteLine($"honest admit {honest}/200, dishonest admit {dishonest}/200");
            Assert.Greater(honest, dishonest + 40);
        }

        [Test] public void LiesAreFlagged_WhenGuiltyAndDenying()
        {
            int lies = 0;
            for (int i = 0; i < 100; i++)
            {
                var m = Make(i, Temperament.Arrogant, 0.1f, ViolationType.RedLight, true, c => c.Anger = 0.5f);
                var r = m.Respond(Nlu.Parse("Sie sind über Rot gefahren.")); if (r.Lied) lies++;
            }
            Assert.Greater(lies, 30);
        }

        [Test] public void FalseAccusation_IsProtested_NotAdmitted()
        {
            var m = Make(5, Temperament.Calm, 0.95f, ViolationType.None, false);
            var r = m.Respond(Nlu.Parse("Sie sind zu schnell gefahren.")); Assert.IsFalse(m.AdmittedViolation); Assert.IsFalse(r.Lied);
        }

        [Test] public void WantedPerson_MayFlee_WhenAskedToStepOut()
        {
            int fled = 0;
            for (int i = 0; i < 100; i++)
            {
                var m = Make(i, Temperament.Nervous, 0.2f, ViolationType.Speeding, true, c => { c.Wanted = true; c.Anxiety = 0.9f; });
                var r = m.Respond(Nlu.Parse("Bitte aussteigen.")); if (r.Actions.Contains(NpcAction.Flee)) fled++;
            }
            TestContext.WriteLine("fled " + fled + "/100"); Assert.Greater(fled, 10); Assert.Less(fled, 90);
        }

        [Test] public void Insult_RaisesAnger_AndFlagsComplaint()
        {
            var m = Make(8, Temperament.Calm, 0.8f, ViolationType.None, false); float before = m.Anger;
            var r = m.Respond(Nlu.Parse("Du Idiot!")); Assert.Greater(m.Anger, before); Assert.Contains("complaint", r.Tags); Assert.IsTrue(m.ComplaintPlanned);
        }

        [Test] public void Replies_FollowTheCitizensLanguage()
        {
            var c = CitizenFactory.Create(3, "VD", "Lausanne", "VD 332211", 0, 1); Assert.AreEqual("fr", c.Language);
            var m = new NpcMind(c, new NpcSituation(), 3); var r = m.Respond(Nlu.Parse("Bonjour, votre permis s'il vous plaît.")); StringAssert.Contains("Bonjour", r.Text + " Bonjour");
            Assert.AreEqual(NpcAction.ShowLicense, r.First);
            var it = CitizenFactory.Create(4, "TI", "Lugano", "TI 11223", 0, 1); Assert.AreEqual("it", it.Language);
        }

        [Test] public void LlmValidator_RejectsImpossibleActions()
        {
            var m = Make(9, Temperament.Calm, 0.9f, ViolationType.None, false, c => c.License = LicenseStatus.None);
            var u = Nlu.Parse("Führerausweis bitte");
            var r = NpcActionValidator.ParseAndValidate("{\"say\":\"Gern, hier ist er.\",\"action\":\"show_license\",\"anger\":0.1,\"anxiety\":0.2,\"lying\":false}", m, u);
            Assert.IsNotNull(r); Assert.AreEqual(NpcAction.RefuseDocuments, r.First, "a person without licence cannot show one");
            var fl = NpcActionValidator.ParseAndValidate("{\"say\":\"Ich haue ab!\",\"action\":\"flee\"}", m, u); Assert.AreNotEqual(NpcAction.Flee, fl.First);
            Assert.IsNull(NpcActionValidator.ParseAndValidate("kein json", m, u));
            Assert.IsNull(NpcActionValidator.ParseAndValidate("{\"action\":\"flee\"}", m, u));
            StringAssert.Contains("JSON", NpcPromptBuilder.Build(m, "Kantonspolizei Bern"));
        }

        [Test] public void Citizens_AreDeterministic_AndConsistent()
        {
            var a = CitizenFactory.Create(77, "ZH", "Zürich", "ZH 1", 2, 2); var b = CitizenFactory.Create(77, "ZH", "Zürich", "ZH 1", 2, 2);
            Assert.AreEqual(a.FullName, b.FullName); Assert.AreEqual(a.Birthdate, b.Birthdate); Assert.AreEqual(a.Vehicle.Vin, b.Vehicle.Vin);
            Assert.AreEqual(a.FullName, a.Vehicle.HolderName);
            CitizenFactory.ApplyDriverTruth(a, true, 1.1f, false, false, true, true, false, 5);
            Assert.AreEqual(0.55f, a.BreathMgPerL, 0.01f); Assert.IsFalse(a.Vehicle.Insured); Assert.IsTrue(a.Vehicle.Defect);
        }
    }
}
