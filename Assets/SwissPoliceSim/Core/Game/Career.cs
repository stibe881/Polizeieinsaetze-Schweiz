using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Legal;
using SwissPoliceSim.Core.Police;

namespace SwissPoliceSim.Core.Game
{
    public enum ConsequenceKind { None, Admonition, CitizenComplaint, InternalInvestigation, Disciplinary, Suspension, CriminalProceedings, Dismissal }

    public sealed class Consequence
    {
        public ConsequenceKind Kind; public string Text = ""; public string NormIds = ""; public float GameTime;
        public override string ToString() => Text;
    }

    /// <summary>
    /// Career and conduct record. Promotion depends on lawful, professional, de-escalating work and correct reports – never on the number of
    /// fines or arrests (the "Quote" is deliberately not rewarded; the number of fines is capped to zero weight).
    /// </summary>
    public sealed class CareerState
    {
        public string CorpsId = "KAPO_BE"; public int RankIndex; public double Xp;
        public float Legality = 70, Professionalism = 70, DeEscalation = 70, Reporting = 70, Community = 70;   // 0..100 rolling ratings
        public int ComplaintsOpen, ComplaintsTotal, InvestigationsOpen, DisciplinaryCount, FinesIssued, ArrestsMade;
        public bool Suspended, Dismissed; public float SuspendedUntil;
        public readonly List<Consequence> History = new List<Consequence>();
        public readonly Random Rng; public int Seed;
        public CareerState(int seed = 1) { Seed = seed; Rng = new Random(seed); }

        public float Overall => (Legality * 0.34f + Professionalism * 0.2f + DeEscalation * 0.2f + Reporting * 0.16f + Community * 0.10f);
        public static readonly double[] XpForRank = { 0, 400, 1000, 1900, 3100, 4600, 6500, 9000, 12500 };
        public bool CanPromote => !Suspended && !Dismissed && RankIndex + 1 < XpForRank.Length && Xp >= XpForRank[RankIndex + 1] && Overall >= 72 && InvestigationsOpen == 0 && ComplaintsOpen <= 1 && Legality >= 70;

        public string RankName(PoliceCorps corps) { var r = corps.Ranks; return r[Math.Min(RankIndex, r.Length - 1)]; }

        public bool TryPromote() { if (!CanPromote) return false; RankIndex++; return true; }

        // ------------------------------------------------------------------ conduct
        public List<Consequence> ApplyAssessment(LegalAssessment a, float npcAnger, bool documented, float now)
        {
            var list = new List<Consequence>();
            if (a.Lawful) { Legality = Math.Min(100, Legality + 0.6f); Professionalism = Math.Min(100, Professionalism + 0.3f); Xp += 4; return list; }
            Legality = Math.Max(0, Legality - (4 + 9 * a.Severity)); Professionalism = Math.Max(0, Professionalism - 2 * a.Severity);
            if (a.Severity >= 2) DeEscalation = Math.Max(0, DeEscalation - 5);
            Xp = Math.Max(0, Xp - 20 * a.Severity);
            string ids = string.Join(",", a.Norms.ConvertAll(n => n.Id));
            // 1) Admonition by the supervisor for every minor irregularity
            if (a.Severity == 1) list.Add(Add(ConsequenceKind.Admonition, "Ermahnung durch den Vorgesetzten: Das Vorgehen entsprach nicht den rechtlichen Vorgaben (" + FirstReason(a) + ").", ids, now));
            // 2) Citizen complaint
            double pComplaint = Math.Min(0.95, 0.12 * a.Severity + 0.35 * npcAnger + (documented ? 0.2 : 0));
            if (a.Severity >= 2 && Rng.NextDouble() < pComplaint)
            {
                ComplaintsOpen++; ComplaintsTotal++; list.Add(Add(ConsequenceKind.CitizenComplaint, "Bürgerbeschwerde gegen Sie eingegangen: " + FirstReason(a), ids, now));
                // 3) Internal investigation
                if (a.Severity >= 3 || ComplaintsOpen >= 2 || Rng.NextDouble() < 0.35)
                { InvestigationsOpen++; list.Add(Add(ConsequenceKind.InternalInvestigation, "Die interne Untersuchung (Dienstaufsicht) wurde eröffnet.", ids, now)); }
            }
            // 4) Criminal proceedings for severity-3 acts that touch criminal norms
            if (a.Severity >= 3 && ids.Contains("OFFICIAL_ABUSE") && (documented || Rng.NextDouble() < 0.5))
            { list.Add(Add(ConsequenceKind.CriminalProceedings, "Die Staatsanwaltschaft eröffnet eine Strafuntersuchung wegen Verdachts auf Amtsmissbrauch (Art. 312 StGB).", ids, now)); Suspended = true; SuspendedUntil = now + 3600; list.Add(Add(ConsequenceKind.Suspension, "Sie werden vom Dienst suspendiert, bis die Untersuchung abgeschlossen ist.", ids, now)); }
            // 5) Disciplinary measure after repeated findings
            if (InvestigationsOpen > 0 && Legality < 45) { DisciplinaryCount++; InvestigationsOpen = Math.Max(0, InvestigationsOpen - 1); list.Add(Add(ConsequenceKind.Disciplinary, "Disziplinarmassnahme (Verweis) – wiederholte rechtswidrige Handlungen.", ids, now)); }
            if (DisciplinaryCount >= 3 || (Suspended && Legality < 20)) { Dismissed = true; list.Add(Add(ConsequenceKind.Dismissal, "Die Anstellung wird aufgelöst (Entlassung).", ids, now)); }
            return list;
        }

        Consequence Add(ConsequenceKind k, string text, string ids, float now) { var c = new Consequence { Kind = k, Text = text, NormIds = ids, GameTime = now }; History.Add(c); return c; }
        static string FirstReason(LegalAssessment a) => a.Reasons.Count > 0 ? a.Reasons[0] : a.Headline;

        public void ComplaintResolved(bool substantiated, float now)
        {
            if (ComplaintsOpen > 0) ComplaintsOpen--;
            if (InvestigationsOpen > 0 && ComplaintsOpen == 0) InvestigationsOpen--;
            if (substantiated) { Professionalism = Math.Max(0, Professionalism - 6); Community = Math.Max(0, Community - 5); }
            else { Community = Math.Min(100, Community + 2); }
        }

        /// <summary>Recognises good practice. No points for volume.</summary>
        public void RewardDeEscalation() { DeEscalation = Math.Min(100, DeEscalation + 2.5f); Community = Math.Min(100, Community + 1f); Xp += 12; }
        public void RewardPoliteness(float politeness) { Professionalism = Math.Min(100, Math.Max(0, Professionalism + politeness * 0.8f)); Community = Math.Min(100, Math.Max(0, Community + politeness * 0.5f)); }
        public void RegisterFine() { FinesIssued++; /* intentionally no XP: quality, not quantity */ }
        public void RegisterArrest() { ArrestsMade++; }
        public void IncidentCompleted(float objectiveScore01, bool reportCorrect, float now)
        {
            Xp += 40 + 120 * objectiveScore01 * (reportCorrect ? 1 : 0.4);
            Reporting = Math.Min(100, Math.Max(0, Reporting + (reportCorrect ? 3 : -7)));
        }
    }
}
