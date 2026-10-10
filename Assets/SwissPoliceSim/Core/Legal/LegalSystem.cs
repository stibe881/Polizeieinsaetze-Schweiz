using System;
using System.Collections.Generic;
using System.Text;

namespace SwissPoliceSim.Core.Legal
{
    public enum OfficerAct
    {
        StopVehicle, RequestDocuments, BreathTest, OrderBloodTest, IdentityCheck, DetainPerson, SearchPerson, SearchVehicle, SearchIntimate,
        Arrest, ReadRights, Handcuff, PhysicalForce, PepperSpray, Baton, DrawFirearm, WarningShot, FireAtPerson,
        QueryPersonData, QueryVehicleData, EmergencyDrive, Pursuit, CrossCantonBorder, SeizeLicense, SeizeVehicleDocs, ReleaseSuspect, HoldBeyond24h, IssueFine
    }

    public enum Suspicion { None, Routine, Concrete, Strong, CaughtInAct }
    public enum OffenceClass { None, Uebertretung, Vergehen, Verbrechen }
    public enum Resistance { None, Passive, Active, Violent, Armed }
    public enum UseOfForceLevel { None, Hold, Handcuffs, Spray, Baton, Firearm }

    /// <summary>Everything the legal check needs to know about the situation at the moment of the action.</summary>
    public sealed class LegalContext
    {
        // jurisdiction
        public bool InOwnTerritory = true, InPursuit, RequestedByLocalPolice, CooperationAgreement;
        public bool Urgent;
        // suspicion / evidence
        public Suspicion Suspicion = Suspicion.Routine; public OffenceClass Offence = OffenceClass.None; public bool Wanted;
        public bool ConsentGiven; public bool DangerInDelay; public bool SearchOrder;
        // person state
        public bool PersonDetained, PersonArrested, RightsRead, SuspectIsDriver = true, ObservedBySafeWitness = true;
        public Resistance Resistance = Resistance.None; public bool SuspectArmed; public bool ThreatToLife;
        public UseOfForceLevel PreviousForce = UseOfForceLevel.None; public bool AnnouncedForce = true; public bool WarningGiven = true;
        public bool GravityOffenceSerious;       // schwere Straftat (for firearms)
        public int MinutesInCustody;
        // driving
        public bool BlueLights, Siren, CarefulDriving = true, RiskToOthers, OvershootsNecessarySpeed;
        // data
        public bool OfficialPurpose = true;
        // search specifics
        public bool SameGenderOrDoctor = true;
        // alcohol
        public bool AlcoholIndication;
    }

    public sealed class LegalAssessment
    {
        public bool Lawful = true;
        public int Severity;                      // 0 fine, 1 minor irregularity, 2 serious, 3 severe (criminal relevance)
        public readonly List<Norm> Norms = new List<Norm>();
        public readonly List<string> Reasons = new List<string>();
        public bool EvidenceUsable = true;        // e.g. rights notice missing → statements unusable
        public string Headline = "";
        public Verification WeakestStatus = Verification.FedlexVerified;

        public void Cite(string normId) { var n = Norms_.Get(normId); if (!Norms.Contains(n)) { Norms.Add(n); if (n.Status > WeakestStatus) WeakestStatus = n.Status; } }
        public void Fail(int severity, string reason, params string[] normIds) { Lawful = false; Severity = Math.Max(Severity, severity); Reasons.Add(reason); foreach (var id in normIds) Cite(id); }
        public void Ok(string reason, params string[] normIds) { Reasons.Add(reason); foreach (var id in normIds) Cite(id); }

        public string Describe()
        {
            var sb = new StringBuilder(); sb.AppendLine(Lawful ? "RECHTMÄSSIG" : $"RECHTSWIDRIG (Schweregrad {Severity})");
            foreach (var r in Reasons) sb.AppendLine("• " + r);
            if (Norms.Count > 0) { sb.Append("Rechtsgrundlagen: "); var p = new List<string>(); foreach (var n in Norms) p.Add($"{n.Title} [{n.CitationText()}]"); sb.AppendLine(string.Join("; ", p)); }
            sb.Append("Prüfstatus: ").Append(StatusText(WeakestStatus)); return sb.ToString();
        }

        public static string StatusText(Verification v)
        {
            switch (v)
            {
                case Verification.FedlexVerified: return "mit dem Fedlex-Text abgeglichen";
                case Verification.CourtPractice: return "Bundesgerichtspraxis, vereinfacht";
                case Verification.GameRule: return "Spielregel (kein Anspruch auf juristische Genauigkeit)";
                default: return "kantonales Recht – nicht geprüft";
            }
        }
        static class Norms_ { public static Norm Get(string id) => Legal.Norms.Get(id); }
    }

    /// <summary>
    /// Rule engine: assesses a police action in its context against federal law (checked texts) and flags where the answer depends on
    /// cantonal law (not verified here). Nothing in here invents statutory articles: every cited norm is in <see cref="Norms"/>.
    /// </summary>
    public static class LegalSystem
    {
        public static LegalAssessment Assess(OfficerAct act, LegalContext c)
        {
            var a = new LegalAssessment { Headline = act.ToString() };
            // ----- territorial competence applies to everything that is not simply helping in an emergency
            bool outside = !c.InOwnTerritory;
            if (outside && !(c.InPursuit || c.RequestedByLocalPolice || c.CooperationAgreement || act == OfficerAct.CrossCantonBorder))
                a.Fail(2, "Ausserhalb des eigenen Zuständigkeitsgebiets fehlt eine Rechtsgrundlage (kein Ersuchen, keine Nacheile, keine Vereinbarung).", "CANTONAL_POLICE_ACTS", "MUTUAL_ASSISTANCE");
            else if (outside && c.InPursuit) a.Ok("Handlung im Rahmen der Nacheile.", "PURSUIT_ACROSS_BORDER");
            else if (outside) a.Ok("Zusammenarbeit/Ersuchen der örtlichen Polizei liegt vor.", "MUTUAL_ASSISTANCE");

            switch (act)
            {
                case OfficerAct.StopVehicle:
                    a.Ok(c.Suspicion >= Suspicion.Concrete ? "Anhalten aufgrund einer beobachteten/vermuteten Widerhandlung." : "Allgemeine Verkehrskontrolle – Anhalten zulässig; die Kontrolle bleibt auf Verkehrsbelange beschränkt.", "FOLLOW_POLICE", "TRAFFIC_CONTROL_COMPETENCE");
                    break;

                case OfficerAct.RequestDocuments:
                    a.Ok("Führer- und Fahrzeugausweis müssen auf Verlangen vorgewiesen werden.", "DOCUMENTS"); break;

                case OfficerAct.BreathTest:
                    a.Ok(c.AlcoholIndication ? "Atemalkoholprobe mit Anzeichen für Alkoholeinfluss." : "Atemalkoholprobe ist bei Fahrzeugführern auch ohne besonderen Anlass zulässig.", "BREATH_TEST", "ALCOHOL_LIMITS"); break;

                case OfficerAct.OrderBloodTest:
                    if (c.AlcoholIndication || c.Suspicion >= Suspicion.Concrete) a.Ok("Blutprobe bei Weigerung/Anzeichen anderer Fahrunfähigkeit angeordnet.", "BLOOD_TEST");
                    else a.Fail(2, "Blutprobe ohne Anzeichen von Fahrunfähigkeit oder Weigerung ist nicht begründet.", "BLOOD_TEST", "FORCE_PROPORTIONALITY"); break;

                case OfficerAct.IdentityCheck:
                case OfficerAct.DetainPerson:
                    if (c.Suspicion >= Suspicion.Concrete || c.Wanted || (c.SuspectIsDriver && c.Suspicion >= Suspicion.Routine && act == OfficerAct.IdentityCheck && c.PersonDetained))
                        a.Ok("Anhaltung/Identitätsfeststellung zur Aufklärung einer Straftat.", "DETAIN_ANHALTUNG");
                    else if (c.SuspectIsDriver && c.Suspicion == Suspicion.Routine) a.Ok("Personalien eines Fahrzeugführers im Rahmen der Verkehrskontrolle (Ausweispflicht).", "DOCUMENTS", "DETAIN_ANHALTUNG");
                    else a.Fail(1, "Ohne konkreten Anhaltspunkt für eine Straftat trägt Art. 215 StPO die Anhaltung nicht; kantonales Polizeirecht (Gefahrenabwehr) könnte eine Identitätsfeststellung erlauben – nicht geprüft.", "DETAIN_ANHALTUNG", "CANTONAL_POLICE_ACTS");
                    break;

                case OfficerAct.SearchPerson:
                    if (c.ConsentGiven) a.Ok("Durchsuchung mit Einwilligung.", "SEARCH_SUSPICION");
                    else if (c.PersonDetained || c.PersonArrested) a.Ok("Sicherheitsdurchsuchung der angehaltenen/festgenommenen Person.", "SEARCH_SAFETY");
                    else if (c.Suspicion >= Suspicion.Strong && (c.SearchOrder || c.DangerInDelay)) a.Ok("Durchsuchung bei begründeter Vermutung; Anordnung bzw. Gefahr im Verzug.", "SEARCH_SUSPICION", "SEARCH_ORDER");
                    else a.Fail(2, "Durchsuchung ohne Einwilligung, ohne Anhaltung und ohne zu vermutende Tatspuren: Eingriff ohne Rechtsgrundlage.", "SEARCH_SUSPICION", "SEARCH_SAFETY", "FORCE_PROPORTIONALITY");
                    break;

                case OfficerAct.SearchIntimate:
                    if (!c.SameGenderOrDoctor && !c.DangerInDelay) a.Fail(2, "Intimdurchsuchung nur durch gleichgeschlechtliche Personen oder ärztlich.", "SEARCH_INTIMATE");
                    else if (c.Suspicion < Suspicion.Strong) a.Fail(3, "Intimdurchsuchung ohne dringenden Tatverdacht ist unverhältnismässig.", "SEARCH_INTIMATE", "FORCE_PROPORTIONALITY");
                    else a.Ok("Intimdurchsuchung bei dringendem Verdacht in zulässiger Form.", "SEARCH_INTIMATE", "SEARCH_SUSPICION"); break;

                case OfficerAct.SearchVehicle:
                    if (c.ConsentGiven) a.Ok("Fahrzeugdurchsuchung mit Einwilligung.", "SEARCH_SUSPICION");
                    else if (c.PersonDetained && c.Suspicion >= Suspicion.Concrete) a.Ok("Fahrzeug darf im Rahmen der Anhaltung geöffnet werden; weitere Durchsuchung nur bei Vermutung von Tatspuren.", "DETAIN_ANHALTUNG", "SEARCH_SUSPICION");
                    else a.Fail(2, "Keine Einwilligung und keine zu vermutenden Tatspuren/Beschlagnahmegegenstände – die Fahrzeugdurchsuchung ist unzulässig.", "SEARCH_SUSPICION", "FORCE_PROPORTIONALITY");
                    break;

                case OfficerAct.Arrest:
                    if (c.Offence >= OffenceClass.Vergehen && (c.Suspicion >= Suspicion.CaughtInAct || c.Wanted)) a.Ok("Pflicht zur vorläufigen Festnahme (Verbrechen/Vergehen auf frischer Tat bzw. Ausschreibung).", "ARREST");
                    else if (c.Offence >= OffenceClass.Vergehen && c.Suspicion >= Suspicion.Strong) a.Ok("Vorläufige Festnahme bei dringendem Verdacht zulässig.", "ARREST");
                    else if (c.Offence == OffenceClass.Uebertretung && c.Suspicion == Suspicion.CaughtInAct) a.Fail(1, "Bei einer Übertretung ist eine Festnahme nur zulässig, wenn die Person die Personalien nicht angibt, nicht in der Schweiz wohnt und keine Sicherheit leistet oder weitere Übertretungen zu verhindern sind – prüfen!", "ARREST");
                    else a.Fail(3, "Festnahme ohne Tatverdacht auf Verbrechen/Vergehen ist rechtswidrig (Freiheitsentzug ohne gesetzliche Grundlage).", "ARREST", "FORCE_PROPORTIONALITY", "OFFICIAL_ABUSE");
                    if (c.PersonArrested || act == OfficerAct.Arrest) { if (!c.RightsRead) a.Ok("Nach der Festnahme: Identität feststellen, Gründe nennen und Rechte erklären (Art. 219/158 StPO).", "ARREST_PROCEDURE", "RIGHTS_NOTICE"); }
                    break;

                case OfficerAct.ReadRights:
                    a.Ok("Belehrung über Aussageverweigerung, Verteidigung und Übersetzung.", "RIGHTS_NOTICE", "ARREST_PROCEDURE"); break;

                case OfficerAct.HoldBeyond24h:
                    if (c.MinutesInCustody > 24 * 60) a.Fail(3, "Freilassung oder Zuführung hätte spätestens nach 24 Stunden erfolgen müssen.", "ARREST_PROCEDURE");
                    else a.Ok("Frist von 24 Stunden eingehalten.", "ARREST_PROCEDURE"); break;

                case OfficerAct.ReleaseSuspect: a.Ok("Freilassung, wenn Haftgründe nicht (mehr) bestehen.", "ARREST_PROCEDURE"); break;

                case OfficerAct.Handcuff:
                    AssessForce(a, c, UseOfForceLevel.Handcuffs); break;
                case OfficerAct.PhysicalForce:
                    AssessForce(a, c, UseOfForceLevel.Hold); break;
                case OfficerAct.PepperSpray:
                    AssessForce(a, c, UseOfForceLevel.Spray); break;
                case OfficerAct.Baton:
                    AssessForce(a, c, UseOfForceLevel.Baton); break;
                case OfficerAct.DrawFirearm:
                    if (c.Resistance == Resistance.Armed || c.ThreatToLife || c.SuspectArmed) a.Ok("Ziehen der Waffe angesichts bewaffneten Gegenübers/Lebensgefahr gerechtfertigt.", "WEAPON_LAST_RESORT");
                    else a.Fail(2, "Waffe ziehen ohne bewaffnetes Gegenüber oder Lebensgefahr ist unverhältnismässig.", "WEAPON_LAST_RESORT", "FORCE_PROPORTIONALITY"); break;
                case OfficerAct.WarningShot:
                    if (!c.WarningGiven) a.Fail(3, "Ein Warnschuss ist nur zulässig, wenn ein Warnruf wirkungslos bleibt oder keine Wirkung haben kann.", "WEAPON_LAST_RESORT");
                    else if (!(c.Resistance == Resistance.Armed || c.GravityOffenceSerious || c.ThreatToLife)) a.Fail(3, "Waffeneinsatz ohne schwere Straftat bzw. Bedrohung ist unzulässig.", "WEAPON_LAST_RESORT", "FORCE_PROPORTIONALITY");
                    else a.Ok("Warnschuss nach wirkungslosem Warnruf bei schwerer Straftat/Bedrohung.", "WEAPON_LAST_RESORT"); break;
                case OfficerAct.FireAtPerson:
                    if ((c.GravityOffenceSerious || c.ThreatToLife || c.Resistance == Resistance.Armed) && c.PreviousForce >= UseOfForceLevel.Hold && c.WarningGiven)
                        a.Ok("Schusswaffeneinsatz als letztes Mittel zur Festnahme/Flucht­verhinderung bei schwerer Straftat bzw. Notwehr/Notstand.", "WEAPON_LAST_RESORT", "FORCE_PROPORTIONALITY");
                    else a.Fail(3, "Schusswaffeneinsatz ohne schwere Straftat, ohne letztes-Mittel-Situation oder ohne Warnung – strafrechtlich relevant.", "WEAPON_LAST_RESORT", "FORCE_PROPORTIONALITY", "OFFICIAL_ABUSE");
                    break;

                case OfficerAct.QueryPersonData:
                case OfficerAct.QueryVehicleData:
                    if (c.OfficialPurpose) a.Ok("Abfrage zu einem dienstlichen Zweck (Verkehrskontrolle/Ermittlung).", "DATA_PROTECTION");
                    else a.Fail(3, "Abfrage ohne dienstlichen Zweck ist Missbrauch persönlicher Daten.", "DATA_PROTECTION", "OFFICIAL_ABUSE"); break;

                case OfficerAct.EmergencyDrive:
                    if (!c.Urgent) a.Fail(2, "Keine dringliche Dienstfahrt: normale Verkehrsregeln gelten.", "EMERGENCY_DRIVE");
                    else if (c.BlueLights && c.Siren && c.CarefulDriving && !c.RiskToOthers) a.Ok("Dringliche Dienstfahrt mit Blaulicht und Wechselklanghorn unter gebotener Sorgfalt.", "EMERGENCY_DRIVE", "EMERGENCY_YIELD");
                    else if (!(c.BlueLights && c.Siren)) a.Fail(2, "Bei dringlichen Dienstfahrten sind die erforderlichen Warnsignale abzugeben (Ausnahme: Erfüllung der Aufgabe steht entgegen).", "EMERGENCY_DRIVE");
                    else a.Fail(2, "Die erforderliche Sorgfalt wurde nicht gewahrt – die Straflosigkeit entfällt, die Strafe ist zu mildern.", "EMERGENCY_DRIVE");
                    if (c.OvershootsNecessarySpeed) a.Fail(1, "Bei Geschwindigkeitsüberschreitungen zählt nur die Differenz zur für den Einsatz angemessenen Geschwindigkeit.", "EMERGENCY_DRIVE");
                    break;

                case OfficerAct.Pursuit:
                    if (c.Suspicion < Suspicion.Concrete) a.Fail(2, "Verfolgung ohne konkreten Tatverdacht ist nicht verhältnismässig.", "FORCE_PROPORTIONALITY");
                    else if (c.RiskToOthers && c.Offence <= OffenceClass.Uebertretung) a.Fail(2, "Gefährdung Dritter bei blosser Übertretung – Verfolgung abbrechen (Verhältnismässigkeit).", "FORCE_PROPORTIONALITY", "EMERGENCY_DRIVE");
                    else a.Ok("Verfolgung eines Fahrzeugs mit konkretem Tatverdacht.", "EMERGENCY_DRIVE", "DETAIN_ANHALTUNG"); break;

                case OfficerAct.CrossCantonBorder:
                    if (c.InPursuit && c.Urgent) a.Ok("Nacheile über die Kantonsgrenze in einem dringenden Fall; Festgehaltene sind der örtlich zuständigen Behörde zu übergeben.", "PURSUIT_ACROSS_BORDER");
                    else if (c.RequestedByLocalPolice || c.CooperationAgreement) a.Ok("Einsatz im anderen Kanton aufgrund Ersuchens/Vereinbarung.", "MUTUAL_ASSISTANCE");
                    else a.Fail(1, "Kein dringender Fall, kein Ersuchen: Befugnisse enden an der Kantonsgrenze.", "PURSUIT_ACROSS_BORDER", "CANTONAL_POLICE_ACTS"); break;

                case OfficerAct.SeizeLicense:
                    if (c.AlcoholIndication || c.Suspicion >= Suspicion.Strong) a.Ok("Fahrunfähigkeit/grobe Verletzung: Weiterfahrt verhindern und Führerausweis abnehmen.", "LICENSE_SEIZURE");
                    else a.Fail(2, "Ausweisabnahme setzt Fahrunfähigkeit oder grobe Verkehrsregelverletzung voraus.", "LICENSE_SEIZURE"); break;

                case OfficerAct.SeizeVehicleDocs:
                    if (c.Suspicion >= Suspicion.Concrete) a.Ok("Fahrzeug nicht zugelassen/gefährdend: Weiterfahrt verhindern, Fahrzeugausweis abnehmen.", "VEHICLE_UNSAFE");
                    else a.Fail(1, "Ohne Mangel am Fahrzeug kein Grund zur Abnahme des Fahrzeugausweises.", "VEHICLE_UNSAFE"); break;

                case OfficerAct.IssueFine:
                    a.Ok("Ordnungsbusse im Ordnungsbussenverfahren bzw. Verzeigung.", "TRAFFIC_CONTROL_COMPETENCE"); break;
            }
            // statements made without rights notice
            if (c.PersonArrested && !c.RightsRead && (act == OfficerAct.DetainPerson || act == OfficerAct.IdentityCheck || act == OfficerAct.SearchPerson)) { a.EvidenceUsable = true; }
            return a;
        }

        static void AssessForce(LegalAssessment a, LegalContext c, UseOfForceLevel level)
        {
            var allowed = AllowedForce(c);
            if (level > allowed)
                a.Fail(level >= UseOfForceLevel.Baton ? 3 : 2, $"Gegenüber {ResistanceText(c.Resistance)} ist {LevelText(level)} unverhältnismässig (zulässig bis {LevelText(allowed)}).", "FORCE_PROPORTIONALITY", "FORCE_DEFINITION");
            else a.Ok($"{LevelText(level)} gegenüber {ResistanceText(c.Resistance)} verhältnismässig.", "FORCE_PROPORTIONALITY", "FORCE_DEFINITION");
            if (!c.AnnouncedForce && !c.DangerInDelay && c.Resistance != Resistance.Violent && c.Resistance != Resistance.Armed) a.Fail(1, "Der Zwang hätte angekündigt werden müssen.", "FORCE_ANNOUNCEMENT");
            else a.Cite("FORCE_ANNOUNCEMENT");
        }

        public static UseOfForceLevel AllowedForce(LegalContext c)
        {
            switch (c.Resistance)
            {
                case Resistance.None: return c.PersonArrested && c.Offence >= OffenceClass.Vergehen && c.Suspicion >= Suspicion.CaughtInAct ? UseOfForceLevel.Handcuffs : UseOfForceLevel.None;
                case Resistance.Passive: return c.PersonArrested ? UseOfForceLevel.Handcuffs : UseOfForceLevel.Hold;
                case Resistance.Active: return UseOfForceLevel.Spray;
                case Resistance.Violent: return UseOfForceLevel.Baton;
                default: return UseOfForceLevel.Firearm;
            }
        }

        static string LevelText(UseOfForceLevel l) { switch (l) { case UseOfForceLevel.None: return "keinen Zwang"; case UseOfForceLevel.Hold: return "körperliche Gewalt/Festhalten"; case UseOfForceLevel.Handcuffs: return "Fesselung"; case UseOfForceLevel.Spray: return "Reizstoffeinsatz"; case UseOfForceLevel.Baton: return "Schlagstock"; default: return "Schusswaffe"; } }
        static string ResistanceText(Resistance r) { switch (r) { case Resistance.None: return "kooperativer Person"; case Resistance.Passive: return "passivem Widerstand"; case Resistance.Active: return "aktivem Widerstand"; case Resistance.Violent: return "gewalttätigem Widerstand"; default: return "bewaffnetem Angriff"; } }
    }
}
