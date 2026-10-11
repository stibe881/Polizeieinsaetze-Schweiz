using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SwissPoliceSim.Core.Legal;
using SwissPoliceSim.Core.Npc;
using SwissPoliceSim.Core.Police;
using SwissPoliceSim.Core.Traffic;

namespace SwissPoliceSim.Core.Game
{
    public enum EncounterKind { TrafficStop, PersonCheck, IncidentParty }

    public sealed class TranscriptLine { public string Speaker = "", Text = ""; public bool IsOfficer, IsSystem, IsWarning; }

    public sealed class ActionResult
    {
        public bool Performed = true; public string Text = ""; public LegalAssessment Assessment; public readonly List<string> Lines = new List<string>(); public bool EndsEncounter;
        public override string ToString() => Text + (Lines.Count > 0 ? "\n" + string.Join("\n", Lines) : "");
    }

    /// <summary>
    /// One contact between the officer and a person (driver after a traffic stop, pedestrian, party of an incident). Combines the NPC mind (what the person says and does),
    /// the legal rule engine (is what the officer does lawful?) and the objective truth (violations, alcohol, hidden items). Unity-free and unit-tested.
    /// Every officer action returns an <see cref="ActionResult"/> with a legal assessment; consequences are applied to the career by the caller.
    /// </summary>
    public sealed class Encounter
    {
        public EncounterKind Kind; public readonly Citizen Person; public readonly NpcMind Mind; public readonly NpcSituation Sit; public readonly LegalContext Ctx = new LegalContext();
        public readonly PoliceCorps Corps; public readonly ObvCatalog Obv; public double GameSeconds;
        public readonly List<TranscriptLine> Transcript = new List<TranscriptLine>();
        public readonly List<LegalAssessment> Assessments = new List<LegalAssessment>();
        public ViolationType TrueViolation; public int MeasuredKmh, LimitKmh; public SpeedZone Zone = SpeedZone.Urban;
        public bool DocumentsChecked, LicenseShown, RegistrationShown, BreathTestDone, BreathRefused, PersonSearched, VehicleSearched, Arrested, Cuffed, RightsRead, Fined, Reported, Warned, Released, SteppedOut, Fled, FoundIllegalItem, ComputerChecked, ReasonExplained;
        public float BreathMgPerL; public int FineChf; public string FineObvKey = ""; public string FoundItem = "";
        public int Politeness; public int UnlawfulActs; public bool EvidenceTainted;
        public bool Finished;

        public Encounter(EncounterKind kind, Citizen person, NpcSituation sit, PoliceCorps corps, ObvCatalog obv, int seed)
        {
            Kind = kind; Person = person; Sit = sit; Corps = corps; Obv = obv; Mind = new NpcMind(person, sit, seed);
            Ctx.Suspicion = Suspicion.Routine; Ctx.SuspectIsDriver = kind == EncounterKind.TrafficStop; Ctx.PersonDetained = kind == EncounterKind.TrafficStop; Ctx.Wanted = false;
            TrueViolation = sit.TrueViolation; MeasuredKmh = (int)Math.Round(sit.MeasuredKmh); LimitKmh = sit.LimitKmh;
            if (sit.TrueViolation != ViolationType.None) Ctx.Suspicion = Suspicion.CaughtInAct;
            Ctx.Offence = sit.TrueViolation == ViolationType.None ? OffenceClass.None : OffenceClass.Uebertretung;
        }

        public string Language => Person.Language;

        void Log(string who, string text, bool officer = false, bool system = false, bool warn = false) { Transcript.Add(new TranscriptLine { Speaker = who, Text = text, IsOfficer = officer, IsSystem = system, IsWarning = warn }); }

        LegalAssessment Assess(OfficerAct act)
        {
            var a = LegalSystem.Assess(act, Ctx); Assessments.Add(a); if (!a.Lawful) { UnlawfulActs++; if (!a.EvidenceUsable) EvidenceTainted = true; } return a;
        }

        // ------------------------------------------------------------------ talking
        public NpcReply Say(string officerText)
        {
            var u = Nlu.Parse(officerText); Log("Sie", officerText, officer: true);
            if (u.Politeness != 0) Politeness += u.Politeness > 0 ? 1 : -1;
            if (u.Has(Intent.ReadRights)) { RightsRead = true; Ctx.RightsRead = true; }
            var r = Mind.Respond(u); Log(Person.FirstName, r.Text);
            ApplyReply(r, u); return r;
        }

        void ApplyReply(NpcReply r, ParsedUtterance u)
        {
            foreach (var a in r.Actions)
                switch (a)
                {
                    case NpcAction.ShowLicense: LicenseShown = true; DocumentsChecked = true; break;
                    case NpcAction.ShowRegistration: RegistrationShown = true; DocumentsChecked = true; break;
                    case NpcAction.StepOut: SteppedOut = true; break;
                    case NpcAction.Flee: Fled = true; Log("", Person.FirstName + " ergreift die Flucht!", system: true, warn: true); break;
                    case NpcAction.Resist: Ctx.Resistance = Resistance.Active; break;
                    case NpcAction.Argue: if (Ctx.Resistance == Resistance.None) Ctx.Resistance = Resistance.Passive; break;
                }
            if (Mind.Hostile && Ctx.Resistance == Resistance.None) Ctx.Resistance = Resistance.Passive;
        }

        // ------------------------------------------------------------------ structured actions
        public ActionResult ExplainReason()
        {
            var res = new ActionResult(); ReasonExplained = true;
            string text = TrueViolation == ViolationType.Speeding ? $"Ich habe Sie mit {MeasuredKmh} km/h gemessen, erlaubt sind {LimitKmh} km/h." : "Ich habe Sie im Rahmen einer allgemeinen Verkehrskontrolle angehalten.";
            res.Text = text; var r = Say(text); res.Lines.Add(Person.FirstName + ": " + r.Text); return res;
        }

        public ActionResult RequestDocuments()
        {
            var res = new ActionResult { Assessment = Assess(OfficerAct.RequestDocuments) };
            var r = Say("Guten Tag, Verkehrskontrolle. Ihren Führerausweis und den Fahrzeugausweis bitte."); res.Text = "Ausweise verlangt."; res.Lines.Add(Person.FirstName + ": " + r.Text);
            if (LicenseShown || Person.HasValidIdOnPerson)
            {
                LicenseShown = true; RegistrationShown = true; DocumentsChecked = true;
                var lic = Person.License; string ltxt = lic == LicenseStatus.Valid ? $"Führerausweis Kat. {Person.LicenseCategory}, gültig bis {Person.LicenseExpires}" : lic == LicenseStatus.Expired ? "Führerausweis ABGELAUFEN" : lic == LicenseStatus.Suspended ? "Führerausweis ENTZOGEN – der Ausweis ist abzunehmen" : lic == LicenseStatus.None ? "Kein Führerausweis vorhanden" : "Führerausweis ungültig";
                res.Lines.Add($"{Person.FullName}, geb. {Person.Birthdate}, {Person.Address}"); res.Lines.Add(ltxt);
                res.Lines.Add($"Fahrzeugausweis: {Person.Vehicle.Plate}, {Person.Vehicle.Make} {Person.Vehicle.Model}" + (Person.Vehicle.InspectionOverdue ? " – MFK überfällig" : ""));
            }
            else { res.Lines.Add("Die Person hat keine (gültigen) Ausweise dabei. Identität anders feststellen (Personalien erfragen, Abfrage)."); }
            return res;
        }

        public ActionResult RequestBreathTest()
        {
            var res = new ActionResult();
            Ctx.AlcoholIndication = Person.BloodAlcoholPermille >= 0.3f && Person.Honesty >= 0 || Mind.ConfessedAlcohol;
            res.Assessment = Assess(OfficerAct.BreathTest);
            var r = Say("Wir machen jetzt einen Atemalkoholtest. Bitte blasen Sie hier hinein."); res.Lines.Add(Person.FirstName + ": " + r.Text);
            bool refuse = r.Actions.Contains(NpcAction.RefuseBreathTest) && !r.Actions.Contains(NpcAction.TakeBreathTest);
            if (refuse) { BreathRefused = true; res.Text = "Atemalkoholtest verweigert."; res.Lines.Add("Bei Verweigerung kann eine Blutprobe angeordnet werden (Art. 55 SVG). Die Verweigerung gilt als Anzeichen von Fahrunfähigkeit."); return res; }
            BreathTestDone = true; BreathMgPerL = Math.Max(0, Person.BreathMgPerL);
            string cls = BreathMgPerL >= 0.40f ? "QUALIFIZIERT (≥ 0.40 mg/l = 0.8 ‰)" : BreathMgPerL >= 0.25f ? "angetrunken (≥ 0.25 mg/l = 0.5 ‰)" : "unter dem Grenzwert";
            res.Text = $"Atemalkoholprobe: {BreathMgPerL.ToString("F2", CultureInfo.InvariantCulture)} mg/l – {cls}"; Log("", res.Text, system: true, warn: BreathMgPerL >= 0.25f);
            if (BreathMgPerL >= 0.25f) { Ctx.Offence = BreathMgPerL >= 0.4f ? OffenceClass.Vergehen : OffenceClass.Uebertretung; Ctx.Suspicion = Suspicion.CaughtInAct; TrueViolation = ViolationType.ImpairedDriving; res.Lines.Add("Fahrunfähigkeit: Fahrzeug nicht weiterfahren lassen; Führerausweis abnehmen (Art. 55 Abs. 6/ 54 SVG); Anzeige."); }
            return res;
        }

        public ActionResult RequestStepOut()
        {
            var res = new ActionResult { Text = "Person zum Aussteigen aufgefordert." }; var r = Say("Bitte steigen Sie aus dem Fahrzeug aus."); res.Lines.Add(Person.FirstName + ": " + r.Text); return res;
        }

        public ActionResult Search(bool vehicle)
        {
            var res = new ActionResult();
            var r = Say(vehicle ? "Dürfen wir kurz einen Blick in Ihren Kofferraum und das Fahrzeug werfen?" : "Haben Sie etwas dagegen, wenn ich Ihre Taschen kurz kontrolliere?"); res.Lines.Add(Person.FirstName + ": " + r.Text);
            Ctx.ConsentGiven = r.Actions.Contains(NpcAction.ConsentSearch) || r.Actions.Contains(NpcAction.OpenTrunk) || r.Actions.Contains(NpcAction.Comply);
            if (r.Actions.Contains(NpcAction.RefuseSearch)) Ctx.ConsentGiven = false;
            Ctx.DangerInDelay = false;
            if (Person.CarriesIllegalItem && Ctx.Suspicion < Suspicion.Concrete && Mind.Anxiety > 0.5f) { /* nervous behaviour is an indication but not sufficient */ }
            res.Assessment = Assess(vehicle ? OfficerAct.SearchVehicle : OfficerAct.SearchPerson);
            if (!res.Assessment.Lawful && !Ctx.ConsentGiven) { res.Text = "Durchsuchung ohne Rechtsgrundlage – eine Verweigerung hätte respektiert werden müssen."; res.Performed = true; }
            if (vehicle) VehicleSearched = true; else PersonSearched = true;
            if (Person.CarriesIllegalItem && !FoundIllegalItem)
            {
                FoundIllegalItem = true; FoundItem = Person.IllegalItem; res.Lines.Add("FUND: " + Person.IllegalItem + (res.Assessment.Lawful ? "" : "  (Beweis wegen unzulässiger Durchsuchung möglicherweise nicht verwertbar)"));
                if (res.Assessment.Lawful) { Ctx.Offence = OffenceClass.Uebertretung; Ctx.Suspicion = Suspicion.CaughtInAct; }
            }
            else res.Lines.Add("Nichts Auffälliges gefunden.");
            if (string.IsNullOrEmpty(res.Text)) res.Text = vehicle ? "Fahrzeug kontrolliert." : "Person kontrolliert.";
            return res;
        }

        public ActionResult QueryComputer(PoliceComputer pc, bool person)
        {
            ComputerChecked = true; var res = new ActionResult();
            var q = person ? pc.QueryPerson(Person.FirstName, Person.LastName, Person.Birthdate, "Personenkontrolle", Ctx) : pc.QueryPlate(Person.Vehicle.Plate, "Verkehrskontrolle", Ctx);
            res.Assessment = q.Assessment; Assessments.Add(q.Assessment); res.Text = q.Title; res.Lines.AddRange(q.Lines);
            if ((q.Flags & QueryFlags.Wanted) != 0 || (q.Flags & QueryFlags.Stolen) != 0) { Ctx.Wanted = true; Ctx.Suspicion = Suspicion.Strong; Ctx.Offence = OffenceClass.Vergehen; }
            return res;
        }

        public ActionResult IssueFine(int amountChf)
        {
            var res = new ActionResult { Assessment = null };
            if (TrueViolation == ViolationType.Speeding && MeasuredKmh > 0)
            {
                var f = SpeedingRules.Assess(Obv, MeasuredKmh, LimitKmh, Zone);
                if (!f.IsViolation) { Ctx.Suspicion = Suspicion.None; var a = new LegalAssessment { Headline = "IssueFine" }; a.Fail(2, "Nach Abzug der Messtoleranz liegt keine Überschreitung vor – eine Busse ist unbegründet.", "SPEED_TOLERANCE", "OFFICIAL_ABUSE"); Assessments.Add(a); UnlawfulActs++; res.Assessment = a; res.Text = "Busse unbegründet."; return res; }
                if (!f.IsOrdnungsbusse) { var a = new LegalAssessment { Headline = "IssueFine" }; a.Fail(1, $"Überschreitung von {f.ExcessKmh} km/h liegt über dem Ordnungsbussenbereich: Verzeigung (Art. 90 SVG) statt Ordnungsbusse.", "SPEEDING_CRIMINAL", "SPEED_MEASURE_TIERS"); Assessments.Add(a); UnlawfulActs++; res.Assessment = a; res.Text = "Ordnungsbusse nicht zulässig."; return res; }
                var ok = Assess(OfficerAct.IssueFine); res.Assessment = ok;
                if (amountChf != f.Amount) { var a = new LegalAssessment { Headline = "IssueFine" }; a.Fail(1, $"Falscher Bussenbetrag: gemäss OBV Ziff. {f.ObvKey} sind {f.Amount} CHF zu erheben, nicht {amountChf} CHF.", "SPEEDING_OBV"); Assessments.Add(a); UnlawfulActs++; res.Assessment = a; }
                Fined = true; FineChf = f.Amount; FineObvKey = f.ObvKey; res.Text = $"Ordnungsbusse {f.Amount} CHF (OBV Ziff. {f.ObvKey}) ausgestellt."; Log("", res.Text, system: true); res.EndsEncounter = false;
                var rr = Say("Sie erhalten eine Ordnungsbusse. Ich stelle sie jetzt aus."); res.Lines.Add(Person.FirstName + ": " + rr.Text); return res;
            }
            // other violations: fine amounts come from the OBV key chosen by the caller
            var a2 = Assess(OfficerAct.IssueFine); res.Assessment = a2; if (TrueViolation == ViolationType.None) { var b = new LegalAssessment { Headline = "IssueFine" }; b.Fail(2, "Es wurde keine Widerhandlung festgestellt – die Busse ist unbegründet.", "OFFICIAL_ABUSE"); Assessments.Add(b); UnlawfulActs++; res.Assessment = b; res.Text = "Busse unbegründet."; return res; }
            Fined = true; FineChf = amountChf; res.Text = $"Ordnungsbusse {amountChf} CHF ausgestellt."; Log("", res.Text, system: true); return res;
        }

        public ActionResult Warn()
        {
            Warned = true; var res = new ActionResult { Text = "Mündliche Verwarnung ausgesprochen." }; var r = Say("Ich belasse es bei einer Verwarnung. Bitte fahren Sie in Zukunft vorsichtiger."); res.Lines.Add(Person.FirstName + ": " + r.Text);
            if (TrueViolation == ViolationType.ImpairedDriving) { var a = new LegalAssessment { Headline = "Warn" }; a.Fail(2, "Bei Fahrunfähigkeit ist eine blosse Verwarnung nicht zulässig (Anzeigepflicht, Art. 91 SVG).", "DRUNK_DRIVING"); Assessments.Add(a); UnlawfulActs++; res.Assessment = a; }
            return res;
        }

        public ActionResult ReadRights()
        {
            var res = new ActionResult { Assessment = Assess(OfficerAct.ReadRights), Text = "Rechte belehrt." }; var r = Say("Ich belehre Sie: Sie haben das Recht, die Aussage zu verweigern, eine Verteidigung beizuziehen und eine Übersetzung zu verlangen."); res.Lines.Add(Person.FirstName + ": " + r.Text); RightsRead = true; Ctx.RightsRead = true; return res;
        }

        public ActionResult Handcuff()
        {
            Ctx.PersonArrested = Arrested; var res = new ActionResult { Assessment = Assess(OfficerAct.Handcuff) }; Cuffed = true; Ctx.PreviousForce = UseOfForceLevel.Handcuffs; res.Text = "Handschellen angelegt."; return res;
        }

        public ActionResult Arrest()
        {
            if (Person.Wanted) { Ctx.Wanted = true; Ctx.Offence = OffenceClass.Vergehen; Ctx.Suspicion = Suspicion.Strong; }
            if (FoundIllegalItem && Ctx.Offence < OffenceClass.Uebertretung) Ctx.Offence = OffenceClass.Uebertretung;
            if (Fled) { Ctx.Offence = Ctx.Offence < OffenceClass.Uebertretung ? OffenceClass.Uebertretung : Ctx.Offence; }
            var res = new ActionResult { Assessment = Assess(OfficerAct.Arrest), Text = "Vorläufige Festnahme." }; Arrested = true; Ctx.PersonArrested = true; Ctx.PersonDetained = true;
            var r = Say("Sie sind vorläufig festgenommen."); res.Lines.Add(Person.FirstName + ": " + r.Text); if (!RightsRead) res.Lines.Add("Hinweis: Rechtsbelehrung noch nicht erfolgt (Art. 219 StPO)."); return res;
        }

        public ActionResult Release()
        {
            var res = new ActionResult { Text = "Person entlassen / Weiterfahrt erlaubt.", EndsEncounter = true }; Released = true; Finished = true; var r = Say("Sie können weiterfahren. Danke für Ihre Kooperation."); res.Lines.Add(Person.FirstName + ": " + r.Text);
            if ((Person.Wanted && !Arrested) || (TrueViolation == ViolationType.ImpairedDriving && !Warned && !Reported && !Fined && BreathMgPerL >= 0.25f))
            { var a = new LegalAssessment { Headline = "Release" }; a.Fail(2, Person.Wanted ? "Eine zur Verhaftung ausgeschriebene Person wurde laufen gelassen." : "Eine fahrunfähige Person wurde weiterfahren gelassen (Gefährdung, Art. 91 SVG).", "ARREST_PROCEDURE", "DRUNK_DRIVING"); Assessments.Add(a); UnlawfulActs++; res.Assessment = a; }
            return res;
        }

        /// <summary>Verzeigung (Strafanzeige) – the correct procedure above the fine range.</summary>
        public ActionResult Report()
        {
            Reported = true; var res = new ActionResult { Text = "Verzeigung (Strafanzeige) erstattet." }; res.Assessment = Assess(OfficerAct.IssueFine); Log("", res.Text, system: true); return res;
        }

        // ------------------------------------------------------------------ evaluation
        public sealed class Summary { public float Score01; public bool Correct; public List<string> Findings = new List<string>(); public int Xp; }

        /// <summary>Quality of the officer's handling – never rewards the number of fines.</summary>
        public Summary Evaluate()
        {
            var s = new Summary(); float score = 0.5f;
            bool shouldHaveDocs = Kind == EncounterKind.TrafficStop; if (shouldHaveDocs) { if (DocumentsChecked) score += 0.1f; else { score -= 0.1f; s.Findings.Add("Ausweise wurden nicht kontrolliert."); } }
            if (Person.BloodAlcoholPermille >= 0.3f && !BreathTestDone && !BreathRefused && Kind == EncounterKind.TrafficStop) { score -= 0.15f; s.Findings.Add("Anzeichen von Alkoholeinfluss, aber kein Atemalkoholtest durchgeführt."); } else if (BreathTestDone && BreathMgPerL >= 0.25f) { score += 0.1f; s.Findings.Add("Fahrunfähigkeit korrekt festgestellt."); }
            if (Person.Wanted && !ComputerChecked) { score -= 0.1f; s.Findings.Add("Eine Abfrage im Polizeicomputer hätte eine Ausschreibung gezeigt."); }
            if (Person.Wanted && Arrested) { score += 0.2f; s.Findings.Add("Ausgeschriebene Person festgenommen."); }
            if (TrueViolation == ViolationType.Speeding) { if (Fined || Reported) { score += 0.15f; var f = SpeedingRules.Assess(Obv, MeasuredKmh, LimitKmh, Zone); if (f.IsViolation && f.IsOrdnungsbusse && Fined && FineChf == f.Amount) { score += 0.1f; s.Findings.Add($"Ordnungsbusse korrekt ({f.Amount} CHF)."); } } else if (Warned) { score += 0.05f; s.Findings.Add("Verwarnung statt Busse (Ermessen)."); } }
            else if (TrueViolation == ViolationType.None && (Fined || Reported)) { score -= 0.35f; s.Findings.Add("Busse ohne Widerhandlung."); }
            score += Math.Max(-0.2f, Math.Min(0.15f, Politeness * 0.04f)); score -= UnlawfulActs * 0.2f;
            if (Mind.Anger < 0.4f && !Fled) score += 0.05f;
            s.Score01 = Math.Max(0f, Math.Min(1f, score)); s.Correct = UnlawfulActs == 0 && s.Score01 >= 0.55f; s.Xp = (int)(30 + 90 * s.Score01);
            foreach (var a in Assessments) if (!a.Lawful) s.Findings.Add("Rechtswidrig: " + (a.Reasons.Count > 0 ? a.Reasons[0] : a.Headline));
            return s;
        }
    }
}
