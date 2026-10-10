using System;
using System.Collections.Generic;
using System.Text;
using SwissPoliceSim.Core.Util;

namespace SwissPoliceSim.Core.Npc
{
    /// <summary>Abstraction of an optional cloud language model. The offline NpcMind always works without it.</summary>
    public interface ILanguageBackend
    {
        /// <summary>Returns the raw model text (expected: one JSON object) for the given system prompt and conversation, or null on failure.</summary>
        System.Threading.Tasks.Task<string> CompleteAsync(string systemPrompt, IList<KeyValuePair<string, string>> turns, System.Threading.CancellationToken ct);
    }

    public static class NpcPromptBuilder
    {
        public const string Schema = "{\"say\": string, \"action\": one of [none, show_license, show_registration, refuse_documents, step_out, stay_in_car, open_trunk, refuse_search, consent_search, take_breath_test, refuse_breath_test, flee, argue, comply, resist, raise_hands, cry, film_with_phone, turn_off_engine], \"anger\": 0..1, \"anxiety\": 0..1, \"lying\": boolean}";

        /// <summary>System prompt that gives the model the NPC's persona and the hidden truth, and restricts its output to a validated JSON action format.</summary>
        public static string Build(NpcMind m, string officerCorps)
        {
            var c = m.C; var s = m.S; var sb = new StringBuilder();
            sb.AppendLine("Du spielst eine Person in einem realistischen Schweizer Polizeisimulator. Du bist KEIN Assistent, sondern ausschliesslich diese Person.");
            sb.AppendLine($"Name: {c.FullName}, {c.Age} Jahre, Beruf: {c.Occupation}, wohnhaft in {c.Municipality} ({c.Canton}). Sprache: {LangName(c.Language)} (Schweizer Prägung, natürlich, nicht übertrieben dialektal).");
            sb.AppendLine($"Temperament: {c.Temperament}. Ehrlichkeit 0..1: {c.Honesty:F2}. Aktuell: Ärger {m.Anger:F2}, Angst {m.Anxiety:F2}, Vertrauen {m.Trust:F2}.");
            sb.AppendLine($"Situation: Du wurdest von der {officerCorps} bei einer Verkehrskontrolle angehalten. Wahrheit (nur du kennst sie): Verstoss = {s.TrueViolation}, du bist schuldig = {s.NpcGuilty}" + (s.MeasuredKmh > 0 ? $", gemessen {s.MeasuredKmh:F0} km/h bei erlaubten {s.LimitKmh}" : "") + ".");
            sb.AppendLine($"Geheimnisse: Führerausweis = {c.License}; Blutalkohol = {c.BloodAlcoholPermille:F2} ‰; Fahrzeug versichert = {c.Vehicle.Insured}; zur Fahndung ausgeschrieben = {c.Wanted}; verbotener Gegenstand dabei = {c.CarriesIllegalItem}.");
            sb.AppendLine("Verhalte dich glaubwürdig gemäss Persönlichkeit: Du darfst lügen, ausweichen, dich irren, Aussagen verweigern oder diskutieren, wenn es zu deiner Rolle passt. Du bist nicht unrealistisch kooperativ und auch nicht grundlos aggressiv.");
            sb.AppendLine("Du darfst NIEMALS Spielmechaniken erwähnen, aus der Rolle fallen, Gewalt anleiten oder reale Personen beleidigen/diskriminieren.");
            sb.AppendLine("Antworte AUSSCHLIESSLICH mit einem JSON-Objekt (keine Erklärungen) im Format: " + Schema);
            sb.AppendLine("'say' ist höchstens 2 Sätze. Handlungen (action) müssen zur Situation passen: z. B. show_license nur, wenn du einen Führerausweis besitzt und ihn zeigen willst.");
            return sb.ToString();
        }

        static string LangName(string l) { switch (l) { case "fr": return "Französisch"; case "it": return "Italienisch"; case "rm": return "Deutsch (Rätoromanisch nicht möglich)"; case "en": return "Englisch"; default: return "Schweizer Hochdeutsch"; } }
    }

    public static class NpcActionValidator
    {
        static readonly Dictionary<string, NpcAction> Map = new Dictionary<string, NpcAction>
        {
            ["none"] = NpcAction.None, ["show_license"] = NpcAction.ShowLicense, ["show_registration"] = NpcAction.ShowRegistration, ["refuse_documents"] = NpcAction.RefuseDocuments,
            ["step_out"] = NpcAction.StepOut, ["stay_in_car"] = NpcAction.StayInCar, ["open_trunk"] = NpcAction.OpenTrunk, ["refuse_search"] = NpcAction.RefuseSearch, ["consent_search"] = NpcAction.ConsentSearch,
            ["take_breath_test"] = NpcAction.TakeBreathTest, ["refuse_breath_test"] = NpcAction.RefuseBreathTest, ["flee"] = NpcAction.Flee, ["argue"] = NpcAction.Argue, ["comply"] = NpcAction.Comply,
            ["resist"] = NpcAction.Resist, ["raise_hands"] = NpcAction.RaiseHands, ["cry"] = NpcAction.Cry, ["film_with_phone"] = NpcAction.FilmWithPhone, ["turn_off_engine"] = NpcAction.TurnOffEngine
        };

        /// <summary>Parses a model answer into a reply and enforces game rules. Returns null if the answer is unusable (caller falls back to the offline brain).</summary>
        public static NpcReply ParseAndValidate(string modelText, NpcMind mind, ParsedUtterance utterance)
        {
            if (string.IsNullOrWhiteSpace(modelText)) return null;
            int a = modelText.IndexOf('{'), b = modelText.LastIndexOf('}');
            if (a < 0 || b <= a) return null;
            Dictionary<string, object> o;
            try { o = MiniJson.Parse(modelText.Substring(a, b - a + 1)) as Dictionary<string, object>; } catch { return null; }
            if (o == null || !o.TryGetValue("say", out var sayObj) || !(sayObj is string say) || say.Trim().Length == 0) return null;
            var r = new NpcReply { Text = say.Trim().Length > 400 ? say.Trim().Substring(0, 400) : say.Trim() };
            if (o.TryGetValue("lying", out var l) && l is bool lb) r.Lied = lb;
            float anger = o.TryGetValue("anger", out var an) && an is double ad ? (float)ad : mind.Anger;
            float anx = o.TryGetValue("anxiety", out var ax) && ax is double xd ? (float)xd : mind.Anxiety;
            mind.Anger = Math.Max(0, Math.Min(1, (mind.Anger + anger) * 0.5f)); mind.Anxiety = Math.Max(0, Math.Min(1, (mind.Anxiety + anx) * 0.5f));   // damped: the model cannot swing emotions arbitrarily
            if (o.TryGetValue("action", out var act) && act is string acts && Map.TryGetValue(acts, out var na)) r.Actions.Add(Sanitize(na, mind, utterance));
            r.Actions.RemoveAll(x => x == NpcAction.None && r.Actions.Count > 1);
            return r;
        }

        /// <summary>The game, not the language model, decides what is physically/legally possible.</summary>
        public static NpcAction Sanitize(NpcAction a, NpcMind m, ParsedUtterance u)
        {
            var c = m.C;
            switch (a)
            {
                case NpcAction.ShowLicense: return c.License == LicenseStatus.None || !c.HasValidIdOnPerson ? NpcAction.RefuseDocuments : a;
                case NpcAction.ShowRegistration: return a;
                case NpcAction.Flee: return (c.Wanted || c.License == LicenseStatus.None || c.BloodAlcoholPermille >= 0.5f || c.CarriesIllegalItem) && m.Anxiety > 0.45f && m.Turns > 1 ? a : NpcAction.Argue;
                case NpcAction.Resist: return m.Anger > 0.55f || u.Has(Intent.AnnounceArrest) && m.Anxiety > 0.6f ? a : NpcAction.Argue;
                case NpcAction.OpenTrunk: case NpcAction.ConsentSearch: return a;
                case NpcAction.TakeBreathTest: case NpcAction.RefuseBreathTest: return u.Has(Intent.RequestBreathTest) ? a : NpcAction.None;
                case NpcAction.StepOut: return u.Has(Intent.RequestStepOut) ? a : NpcAction.None;
                case NpcAction.FilmWithPhone: return m.Anger > 0.4f ? a : NpcAction.None;
                default: return a;
            }
        }
    }
}
