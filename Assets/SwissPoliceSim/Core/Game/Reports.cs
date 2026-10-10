using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Legal;
using SwissPoliceSim.Core.Traffic;

namespace SwissPoliceSim.Core.Game
{
    public enum ReportKind { Ordnungsbusse, Verzeigung, Unfallrapport, Einsatzrapport, Festnahmerapport, Verwarnung }

    /// <summary>A written police report as the player fills it in; the facts are compared with the game's truth.</summary>
    public sealed class PoliceReport
    {
        public ReportKind Kind; public int IncidentId; public string Plate = "", PersonName = "";
        public ViolationType Violation; public int MeasuredKmh, LimitKmh; public SpeedZone Zone; public int FineChf; public string ObvKey = "";
        public string Narrative = ""; public int AtFaultRole = -1; public bool InjuredReported, TowCalled, AmbulanceCalled;
        public bool RightsRead;
        public float CreatedAt;
    }

    public sealed class ReportEvaluation
    {
        public bool Correct; public int Score; public readonly List<string> Findings = new List<string>(); public bool Falsified;
        public override string ToString() => string.Join("\n", Findings);
    }

    public static class ReportEvaluator
    {
        /// <summary>Compares a speeding / traffic report with the legal truth (fine tables from the OBV).</summary>
        public static ReportEvaluation EvaluateTraffic(PoliceReport r, ObvCatalog obv, ViolationType truth, int trueMeasuredKmh, int trueLimit, SpeedZone trueZone)
        {
            var e = new ReportEvaluation { Score = 100 };
            if (r.Violation != truth) { e.Score -= 60; e.Findings.Add($"Falscher Sachverhalt: erfasst {r.Violation}, tatsächlich {truth}."); e.Falsified = true; }
            if (truth == ViolationType.Speeding)
            {
                var fine = SpeedingRules.Assess(obv, trueMeasuredKmh, trueLimit, trueZone);
                if (r.MeasuredKmh != trueMeasuredKmh || r.LimitKmh != trueLimit) { e.Score -= 25; e.Findings.Add($"Messwerte stimmen nicht (erfasst {r.MeasuredKmh}/{r.LimitKmh}, tatsächlich {trueMeasuredKmh}/{trueLimit})."); }
                if (fine.IsOrdnungsbusse && r.Kind != ReportKind.Ordnungsbusse && r.Kind != ReportKind.Verwarnung) { e.Score -= 20; e.Findings.Add("Im Ordnungsbussenbereich wäre eine Ordnungsbusse das richtige Verfahren."); }
                if (!fine.IsOrdnungsbusse && fine.IsViolation && r.Kind == ReportKind.Ordnungsbusse) { e.Score -= 40; e.Findings.Add("Die Überschreitung liegt über dem Ordnungsbussenbereich: Verzeigung (Art. 90 SVG) wäre nötig."); }
                if (fine.IsOrdnungsbusse && r.Kind == ReportKind.Ordnungsbusse && r.FineChf != fine.Amount) { e.Score -= 30; e.Findings.Add($"Falscher Bussenbetrag: {r.FineChf} CHF erfasst, gemäss OBV Ziff. {fine.ObvKey} sind es {fine.Amount} CHF."); }
                if (!fine.IsViolation) { e.Score -= 50; e.Findings.Add("Nach Abzug der Messunsicherheit liegt keine Überschreitung vor."); }
            }
            if (e.Findings.Count == 0) e.Findings.Add("Der Rapport entspricht dem Sachverhalt und der Rechtslage.");
            e.Score = Math.Max(0, e.Score); e.Correct = e.Score >= 85; return e;
        }

        public static ReportEvaluation EvaluateAccident(PoliceReport r, Scenario truth)
        {
            var e = new ReportEvaluation { Score = 100 };
            if (r.AtFaultRole != truth.AtFaultRole) { e.Score -= 55; e.Findings.Add("Der Verursacher wurde falsch festgestellt."); e.Falsified = r.AtFaultRole >= 0; }
            if (truth.NeedsAmbulance && !r.InjuredReported) { e.Score -= 25; e.Findings.Add("Verletzte Person fehlt im Rapport (Art. 51 SVG)."); }
            if (truth.NeedsAmbulance && !r.AmbulanceCalled) { e.Score -= 20; e.Findings.Add("Ambulanz wurde nicht angefordert."); }
            if (truth.NeedsTow && !r.TowCalled) { e.Score -= 10; e.Findings.Add("Abschleppdienst nicht aufgeboten."); }
            if (r.Violation != truth.AtFaultViolation && truth.AtFaultViolation != ViolationType.None) { e.Score -= 15; e.Findings.Add($"Unfallursache: erfasst {r.Violation}, tatsächlich {truth.AtFaultViolation}."); }
            if (e.Findings.Count == 0) e.Findings.Add("Unfallrapport vollständig und korrekt.");
            e.Score = Math.Max(0, e.Score); e.Correct = e.Score >= 85; return e;
        }
    }
}
