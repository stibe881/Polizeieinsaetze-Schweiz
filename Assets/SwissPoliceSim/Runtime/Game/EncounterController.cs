using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Game;
using SwissPoliceSim.Core.Legal;
using SwissPoliceSim.Core.Npc;
using SwissPoliceSim.Core.Police;
using SwissPoliceSim.Core.Traffic;
using SwissPoliceSim.Characters;
using SwissPoliceSim.Player;
using UnityEngine;

namespace SwissPoliceSim.Game
{
    /// <summary>Runs the current contact with a person (driver or pedestrian): wraps the Core <see cref="Encounter"/>, applies legal judgement to the career, handles flight, and records the case.</summary>
    public sealed class EncounterController : MonoBehaviour
    {
        public Encounter Current; public NpcActor Actor; public TrafficVehicle Vehicle; public bool InEncounter => Current != null; public string LastResultText = "";
        public event Action<Encounter> Began, Ended; public bool WindowRequested; public LlmService Llm;
        System.Threading.Tasks.Task<string> _llmTask; int _llmLine = -1; Encounter _llmEnc;
        GameSession S => GameSession.I;

        public void BeginWithActor(NpcActor a)
        {
            if (a == null || a.Enc == null || InEncounter) return; Current = a.Enc; Actor = a; Vehicle = null; ApplyJurisdiction(Current); a.FaceTowards(S.PlayerPos); a.LookAt = S.Player.FocusTransform; if (a.Visual.Pose == PoseKind.Idle) a.Visual.Pose = PoseKind.Talk;
            S.Computer.Register(a.Person); WindowRequested = true; Began?.Invoke(Current);
        }

        public void BeginWithVehicle(TrafficVehicle v, Citizen driver, Measurement m)
        {
            if (v == null || InEncounter) return; Vehicle = v; Actor = null;
            var sit = new NpcSituation { Place = S.PlaceName, IsDriver = true, InVehicle = true, TimeOfDayNight = S.Hour < 6 || S.Hour > 21 };
            if (m != null && m.VehicleId == v.Id && Time.time - m.Time < 120f) { sit.MeasuredKmh = m.Kmh; sit.LimitKmh = m.Limit; sit.TrueViolation = m.Fine != null && m.Fine.IsViolation ? ViolationType.Speeding : ViolationType.None; sit.NpcGuilty = sit.TrueViolation != ViolationType.None; }
            else if (v.LastViolation != ViolationType.None && S.Traffic.Sim != null && S.Traffic.Sim.Time - v.LastViolationTime < 40f && v.LastViolation != ViolationType.Speeding) { sit.TrueViolation = v.LastViolation; sit.NpcGuilty = true; }
            if (v.Driver.Impaired) sit.NpcGuilty = true;
            var zone = SpeedZone.Urban; if (m != null) zone = m.Zone;
            Current = new Encounter(EncounterKind.TrafficStop, driver, sit, S.Corps, S.Obv, driver.Seed) { Zone = zone }; ApplyJurisdiction(Current);
            if (sit.TrueViolation == ViolationType.None && v.Driver.Impaired) Current.Ctx.Suspicion = Suspicion.Routine;
            S.Computer.Register(driver); WindowRequested = true; Began?.Invoke(Current);
            if (S.Traffic != null && S.Traffic.TryGetVisual(v.Id, out var vis)) { /* driver visible in the car while stopped */ }
        }

        void ApplyJurisdiction(Encounter e) { JurisdictionManager.Apply(e.Ctx, S.Jurisdiction, S.Pursuit != null && S.Pursuit.IsActive, false); }

        void Update()
        {
            if (_llmTask != null && _llmTask.IsCompleted)
            {
                string line = _llmTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? _llmTask.Result : null; _llmTask = null;
                if (_llmEnc != null && _llmLine >= 0 && _llmLine < _llmEnc.Transcript.Count && !string.IsNullOrWhiteSpace(line)) _llmEnc.Transcript[_llmLine].Text = line;
                if (_llmEnc != null) Voice.SpeakText?.Invoke(!string.IsNullOrWhiteSpace(line) ? line : _llmEnc.Transcript[Math.Min(_llmLine, _llmEnc.Transcript.Count - 1)].Text, _llmEnc.Language, _llmEnc.Person.Gender == Gender.Female);
                _llmEnc = null;
            }
        }

        // ------------------------------------------------------------------ actions
        ActionResult Wrap(ActionResult r)
        {
            if (r == null) return null;
            if (r.Assessment != null) { var cons = S.Judge(r.Assessment, Current.Mind.Anger); foreach (var c in cons) Current.Transcript.Add(new TranscriptLine { Speaker = "", Text = c.Text, IsSystem = true, IsWarning = true }); }
            if (!string.IsNullOrEmpty(r.Text)) Current.Transcript.Add(new TranscriptLine { Speaker = "", Text = r.Text, IsSystem = true });
            foreach (var l in r.Lines) if (!l.StartsWith(Current.Person.FirstName + ":")) Current.Transcript.Add(new TranscriptLine { Speaker = "", Text = l, IsSystem = true });
            HandleReactions(); Voice.Speak(Current, r); return r;
        }

        public NpcReply Say(string text)
        {
            if (!InEncounter || string.IsNullOrWhiteSpace(text)) return null; var r = Current.Say(text); HandleReactions();
            if (Llm != null && Llm.Ready && _llmTask == null && Current != null)
            {   // the model only rephrases the line; actions and state stay with the offline mind
                _llmEnc = Current; _llmLine = Current.Transcript.Count - 1; string corps = S.Corps.Name; var mind = Current.Mind; var pu = Core.Npc.Nlu.Parse(text); string offline = r.Text;
                _llmTask = Llm.PhraseAsync(mind, corps, pu, text, offline);
            }
            else Voice.SpeakReply(Current, r);
            if (Current.Mind.Anger > 0.85f && Current.Ctx.Resistance == Resistance.None) Current.Ctx.Resistance = Resistance.Passive;
            return r;
        }

        void HandleReactions()
        {
            if (Current == null || !Current.Fled) return;
            if (Actor != null && !Actor.Fleeing && !Actor.Cuffed) { Actor.Flee(S.PlayerPos); Notifier.Warn(Actor.Label + " flüchtet!"); EndInternal(false); }
            else if (Vehicle != null && Vehicle.Mode != DriverMode.Fleeing && S.Traffic != null && S.Traffic.Sim != null)
            {
                S.Pursuit.Begin(Vehicle, Current.Ctx.Offence == OffenceClass.None ? OffenceClass.Uebertretung : Current.Ctx.Offence, Suspicion.Concrete); Notifier.Warn("Das Fahrzeug flüchtet! Verfolgung (Blaulicht und Horn einschalten).");
                S.Traffic.Sim.ReleaseStop(Vehicle); Vehicle.Mode = DriverMode.Fleeing; EndInternal(false);
            }
        }

        public ActionResult Docs() => Wrap(Current.RequestDocuments()); public ActionResult Breath() => Wrap(Current.RequestBreathTest()); public ActionResult StepOut() => Wrap(Current.RequestStepOut());
        public ActionResult Reason() => Wrap(Current.ExplainReason()); public ActionResult SearchPerson() => Wrap(Current.Search(false)); public ActionResult SearchVehicle() => Wrap(Current.Search(true));
        public ActionResult QueryPlate() => Wrap(Current.QueryComputer(S.Computer, false)); public ActionResult QueryPerson() => Wrap(Current.QueryComputer(S.Computer, true));
        public ActionResult Fine(int chf) { var r = Wrap(Current.IssueFine(chf)); if (Current.Fined) { S.Career.RegisterFine(); } return r; }
        public ActionResult Warn() => Wrap(Current.Warn()); public ActionResult Rights() => Wrap(Current.ReadRights());
        public ActionResult Cuff() { var r = Wrap(Current.Handcuff()); if (Actor != null) Actor.SetCuffed(true); return r; }
        public ActionResult Arrest() { var r = Wrap(Current.Arrest()); if (Current.Arrested) { S.Career.RegisterArrest(); if (Actor != null) Actor.Arrested = true; } return r; }
        public ActionResult Report() => Wrap(Current.Report());
        public ActionResult Release() { var r = Wrap(Current.Release()); End(); return r; }

        public int ExpectedFine()
        {
            if (Current == null) return 0; if (Current.TrueViolation == ViolationType.Speeding && Current.MeasuredKmh > 0) { var f = SpeedingRules.Assess(S.Obv, Current.MeasuredKmh, Current.LimitKmh, Current.Zone); return f.IsOrdnungsbusse ? f.Amount : 0; } return 0;
        }

        // ------------------------------------------------------------------ end
        public void End() { EndInternal(true); }

        void EndInternal(bool evaluate)
        {
            if (Current == null) return; var enc = Current; var sum = enc.Evaluate();
            if (evaluate) { S.Career.IncidentCompleted(sum.Score01, sum.Correct, (float)S.Clock.ElapsedGameSeconds); S.Career.Xp += sum.Xp; if (enc.Politeness > 1 && enc.Mind.Anger < 0.4f) S.Career.RewardDeEscalation(); S.Career.RewardPoliteness(enc.Politeness * 0.5f); }
            string summary = enc.Kind == EncounterKind.TrafficStop ? (enc.Fined ? $"Ordnungsbusse {enc.FineChf} CHF – {enc.Person.FullName}" : enc.Reported ? "Verzeigung – " + enc.Person.FullName : enc.Arrested ? "Festnahme – " + enc.Person.FullName : "Verkehrskontrolle – " + enc.Person.FullName) : "Personenkontakt – " + enc.Person.FullName;
            S.AddCase(enc.Kind == EncounterKind.TrafficStop ? "Verkehr" : "Person", summary, enc.Person.Vehicle?.Plate, enc.Person.FullName, enc.FineChf, sum.Correct);
            foreach (var f in sum.Findings) Notifier.Push(f, f.StartsWith("Rechtswidrig") ? NoteKind.Warn : NoteKind.Info, 9f);
            if (Vehicle != null && S.Traffic != null && S.Traffic.Sim != null && Vehicle.Mode != DriverMode.Fleeing && !enc.Arrested) S.Traffic.Sim.ReleaseStop(Vehicle);
            if (Actor != null && Actor.Visual != null && !Actor.Cuffed && !Actor.Fleeing) { Actor.Visual.Pose = PoseKind.Idle; }
            Current = null; Actor = null; Vehicle = null; WindowRequested = false; Ended?.Invoke(enc);
            if (evaluate) Notifier.Push($"Kontakt beendet – Bewertung {Mathf.RoundToInt(sum.Score01 * 100)} %", sum.Correct ? NoteKind.Good : NoteKind.Warn, 7f);
        }
    }

    /// <summary>Voice output hook (implemented by <see cref="VoiceService"/> when enabled).</summary>
    public static class Voice
    {
        public static Action<string, string, bool> SpeakText;       // text, language, female
        public static void SpeakReply(Encounter e, NpcReply r) { if (r != null && SpeakText != null) SpeakText(r.Text, e.Language, e.Person.Gender == Gender.Female); }
        public static void Speak(Encounter e, ActionResult r) { if (r != null && SpeakText != null && r.Lines.Count > 0) { string first = r.Lines[0]; int i = first.IndexOf(": "); if (first.StartsWith(e.Person.FirstName + ":") && i > 0) SpeakText(first.Substring(i + 2), e.Language, e.Person.Gender == Gender.Female); } }
    }
}
