using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SwissPoliceSim.Core.Traffic;

namespace SwissPoliceSim.Core.Npc
{
    public enum Intent
    {
        Unknown, Greeting, Goodbye, AskIfKnowsReason, StateReason, RequestLicense, RequestRegistration, RequestAllDocuments, AskName, AskAddress, AskDestination, AskOrigin,
        AskAlcohol, AskDrugs, RequestBreathTest, RequestStepOut, RequestOpenTrunk, RequestSearchConsent, AnnounceFine, AnnounceWarning, AnnounceReport, AnnounceArrest, ReadRights,
        ThankYou, Apologize, Threaten, Insult, Calm, AskIfInjured, AskPhoneUse, AskSeatbelt, AskSpeedReason, ReleaseDriver, OrderStayInCar, AskToRepeat, RequestMove, AskWhatHappened
    }

    public sealed class ParsedUtterance
    {
        public string Raw = ""; public string Language = "de";
        public readonly List<Intent> Intents = new List<Intent>();
        public ViolationType Violation = ViolationType.None; public int Number;      // e.g. a speed named by the officer
        public float Politeness;                    // -1 rude .. +1 polite
        public bool Has(Intent i) => Intents.Contains(i);
    }

    /// <summary>
    /// Rule-based understanding of free player input (typed or transcribed): German (incl. Swiss German spellings), French, Italian, English.
    /// Works offline; a cloud model can replace it through ILanguageBackend, but validation of NPC actions stays in this library.
    /// </summary>
    public static class Nlu
    {
        static readonly RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        sealed class Rule { public Intent Intent; public Regex Rx; public ViolationType V; public Rule(Intent i, string rx, ViolationType v = ViolationType.None) { Intent = i; Rx = new Regex(rx, O); V = v; } }

        static readonly List<Rule> Rules = new List<Rule>
        {
            new Rule(Intent.Greeting, @"\b(gr[uü]e?[zs]+i|gr[uü]essech|gr[uü]ezi|guten (tag|morgen|abend)|gutentag|hallo|hoi|sali|salü|servus|bonjour|bonsoir|salut|buongiorno|buonasera|ciao|hello|good (morning|afternoon|evening)|bun di)\b"),
            new Rule(Intent.Goodbye, @"\b(tsch[uü]ss|uf wiederluege|auf wiedersehen|gute fahrt|sch[oö]nen tag|au revoir|bonne route|arrivederci|buon viaggio|goodbye|have a nice day|sie k[oö]nnen weiterfahren|weiterfahren|you may go|vous pouvez partir|pu[oò] andare)\b"),
            new Rule(Intent.AskIfKnowsReason, @"(wissen sie.{0,25}(warum|weshalb|wieso|weswegen|grund)|ist ihnen.{0,30}(klar|bewusst|bekannt)|wissen sie, was|savez-vous pourquoi|sav(e|ez)\s*vous.{0,10}pourquoi|sa(pete|pete)? perch[eé]|do you know why|any idea why|warum habe ich sie angehalten|weshalb habe ich sie angehalten)"),
            new Rule(Intent.StateReason, @"(zu schnell|geschwindigkeit|tempo|rasen|gerast|[uü]berschritten|km/h|kmh|stundenkilometer|trop vite|vitesse|exc[eè]s|troppo veloce|velocit[aà]|speeding|too fast)", ViolationType.Speeding),
            new Rule(Intent.StateReason, @"(rotlicht|bei rot|[uü]ber rot|rote ampel|ampel.{0,15}rot|rot.{0,10}(gefahren|[uü]berfahren)|rotlichtverstoss|feu rouge|passé au rouge|semaforo rosso|con il rosso|red light|ran a red)", ViolationType.RedLight),
            new Rule(Intent.StateReason, @"(handy|natel|mobiltelefon|telefon[ie]ert|am telefon|smartphone|portable|t[eé]l[eé]phon|cellulare|telefonino|phone|texting)", ViolationType.PhoneUse),
            new Rule(Intent.StateReason, @"(gurt|angegurtet|angeschnallt|nicht angeschnallt|ceinture|cintura|seat ?belt)", ViolationType.NoSeatbelt),
            new Rule(Intent.StateReason, @"(abstand|zu nah|aufgefahren|dr[aä]ngeln|bedr[aä]ngt|distance de s[eé]curit[eé]|collé|distanza di sicurezza|tailgat)", ViolationType.Tailgating),
            new Rule(Intent.StateReason, @"(stopp?[- ]?(signal|schild|strasse)|stop missachtet|stoppschild|rollstopp|panneau stop|segnale di stop|stop sign)", ViolationType.StopSign),
            new Rule(Intent.StateReason, @"(vortritt|vorfahrt|nicht gew[aä]hrt|c[eé]der le passage|priorit[eé]|precedenza|right of way|yield)", ViolationType.FailureToYield),
            new Rule(Intent.StateReason, @"(gef[aä]hrlich|riskant|[uü]berholmanöver|[uü]berholt|dépassement dangereux|sorpasso pericoloso|dangerous|reckless)", ViolationType.DangerousDriving),
            new Rule(Intent.StateReason, @"(r[uü]cklicht|bremslicht|reifen|profil|beleuchtung|defekt|mangel|feu arrière|pneu|luce posteriore|gomme|broken (tail)?light)", ViolationType.DefectiveVehicle),
            new Rule(Intent.RequestLicense, @"(f[uü]hrerausweis|fahrausweis|f[uü]hrerschein|f[uü]hrerausweis|ausweis|permis de conduire|permis|patente|licenza|driver'?s? licen[cs]e|driving licen[cs]e|id)\b"),
            new Rule(Intent.RequestRegistration, @"(fahrzeugausweis|fahrzeugpapiere|kfz[- ]?papiere|fahrzeugschein|papiere|carte grise|permis de circulation|libretto|licenza di circolazione|registration|vehicle papers)"),
            new Rule(Intent.RequestAllDocuments, @"(alle papiere|beide ausweise|ausweise und papiere|ihre papiere|ihre ausweise|tous (vos )?documents|tutti i documenti|your papers|all documents)"),
            new Rule(Intent.AskName, @"(wie heissen sie|ihr name|ihren namen|personalien|vorname|nachname|comment vous appelez|votre nom|come si chiama|il suo nome|your name)"),
            new Rule(Intent.AskAddress, @"(wo wohnen sie|ihre adresse|wohnadresse|wohnort|o[uù] habitez|votre adresse|dove abita|il suo indirizzo|where do you live|your address)"),
            new Rule(Intent.AskDestination, @"(wohin|wo fahren sie|wo wollen sie hin|ziel ihrer fahrt|o[uù] allez|dove va|where are you going|destination)"),
            new Rule(Intent.AskOrigin, @"(woher|wo kommen sie|von wo|d'o[uù] venez|da dove viene|where are you coming from|where have you been)"),
            new Rule(Intent.AskAlcohol, @"(alkohol|getrunken|trinken|bier|wein|promille|boire|bu |alcool|bevuto|bevuta|drunk|had anything to drink|drinking)"),
            new Rule(Intent.AskDrugs, @"(drogen|medikamente|cannabis|kiffen|gekifft|stoff|drogue|m[eé]dicament|droga|farmaci|drugs|medication)"),
            new Rule(Intent.RequestBreathTest, @"(atemalkohol|alkoholtest|alkotest|blasen|pusten|atemtest|[eé]thylotest|alcotest|test dell'alcol|soufflez|soffiare|breath ?test|breathalyz)"),
            new Rule(Intent.RequestStepOut, @"(aussteigen|steigen sie aus|bitte aussteigen|raus aus dem|descendez|sortez du v[eé]hicule|scenda|scendere|step out|get out of the car)"),
            new Rule(Intent.OrderStayInCar, @"(bleiben sie (im|sitzen)|im fahrzeug bleiben|nicht aussteigen|restez (dans|au)|rimanga in auto|stay in the (car|vehicle)|remain seated)"),
            new Rule(Intent.RequestOpenTrunk, @"(kofferraum|heckklappe|laderaum|coffre|bagagliaio|trunk|boot)"),
            new Rule(Intent.RequestSearchConsent, @"(durchsuchen|durchsuchung|kontrollieren sie.{0,15}tasche|taschen leeren|mit ihrem einverst[aä]ndnis|einverstanden|fouiller|perquisition|perquisire|search (the car|your|you))"),
            new Rule(Intent.AnnounceFine, @"(ordnungsbusse|busse|bussgeld|strafzettel|b[uü]ssen|amende|multa|fine|ticket)"),
            new Rule(Intent.AnnounceWarning, @"(verwarnung|ermahnung|mündliche verwarnung|verwarne|avertissement|ammonizione|warning|let you off)"),
            new Rule(Intent.AnnounceReport, @"(verzeig|anzeige|rapport|strafanzeige|dénoncer|denuncia|report you|charge)"),
            new Rule(Intent.AnnounceArrest, @"(festnahme|festgenommen|verhaft|sie sind verhaftet|arrestier|arr[eê]t[eé]|vous [eê]tes en [eé]tat d'arrestation|in arresto|under arrest|handschellen)"),
            new Rule(Intent.ReadRights, @"(ihre rechte|aussage verweigern|recht zu schweigen|verteidiger|[uü]bersetzer|droit de garder le silence|avocat|diritto di rimanere in silenzio|avvocato|you have the right|remain silent)"),
            new Rule(Intent.ThankYou, @"(danke|merci|vielen dank|besten dank|grazie|thanks|thank you)"),
            new Rule(Intent.Apologize, @"(entschuldigung|tut mir leid|verzeihung|pardon|d[eé]sol[eé]|scusi|mi dispiace|sorry|apolog)"),
            new Rule(Intent.Threaten, @"(ich (schiess|schlag|verhaft|schlage|erschiess)|sonst (passiert|knallt)|wenn sie nicht|ich werde sie|dann gibt'?s|je vais vous (frapper|tirer)|ti sparo|i'?ll shoot|or else)"),
            new Rule(Intent.Insult, @"(idiot|arschloch|trottel|depp|vollidiot|dumm|schwachsinn|sauhund|c(h|k)ue|con(nard)?|stronzo|asshole|moron|stupid)"),
            new Rule(Intent.Calm, @"(beruhigen sie sich|ganz ruhig|bleiben sie ruhig|keine panik|calmez-vous|du calme|si calmi|stia calmo|calm down|relax)"),
            new Rule(Intent.AskIfInjured, @"(verletzt|alles in ordnung|geht es ihnen gut|brauchen sie hilfe|[eé]tes-vous bl|va tutto bene|[eè] ferito|are you (ok|hurt|injured))"),
            new Rule(Intent.AskPhoneUse, @"(telefoniert|am handy|auf dem handy|haben sie.{0,20}telefon|t[eé]l[eé]phon[eé]|telefonato|were you on (the|your) phone)"),
            new Rule(Intent.AskSeatbelt, @"(angegurtet|angeschnallt|gurt (an|getragen)|mettez votre ceinture|allacciat|seat ?belt on)"),
            new Rule(Intent.AskSpeedReason, @"(warum.{0,15}(so schnell|eilig)|wieso.{0,10}eilig|pressiert|pourquoi.{0,15}press|perch[eé].{0,15}fretta|why.{0,15}(hurry|rush))"),
            new Rule(Intent.ReleaseDriver, @"(sie k[oö]nnen gehen|sie sind entlassen|fahren sie weiter|weiterfahren|vous pouvez continuer|pu[oò] proseguire|you can go)"),
            new Rule(Intent.AskWhatHappened, @"(was ist (hier )?(passiert|geschehen|los)|erz[äa]hlen sie|schildern sie|wie ist es dazu gekommen|hergang|was haben sie (gesehen|beobachtet)|was ist vorgefallen|que s.est.il pass|racontez|cosa [èe] successo|racconti|what happened|tell me what)"),
            new Rule(Intent.AskToRepeat, @"(noch einmal|wiederholen sie|wie bitte|pardon\?|r[eé]p[eé]tez|ripeta|say again|repeat)"),
            new Rule(Intent.RequestMove, @"(fahren sie (vor|weiter vor|rechts ran|zur seite)|parkieren sie|stellen sie.{0,15}ab|motor abstellen|moteur|spegnere il motore|turn off the engine|pull over)"),
        };

        static readonly Regex Num = new Regex(@"(\d{2,3})\s*(km/?h|kmh|stundenkilometer|kilomètres|chilometri)", O);

        public static string DetectLanguage(string t)
        {
            string s = " " + t.ToLowerInvariant() + " ";
            int de = Count(s, " ich ", " sie ", " bitte ", " ihr ", " der ", " die ", " das ", " ist ", " haben ", " mit ", "grüezi", "führer", "fahrzeug", " und ", " nicht ");
            int fr = Count(s, " vous ", " votre ", " je ", " le ", " la ", " les ", " est ", "bonjour", " pourquoi ", "permis", " s'il ", " avez ", " êtes ");
            int it = Count(s, " lei ", " il suo ", " la sua ", " è ", "buongiorno", " perché ", "patente", " per favore ", " ha ", " sono ", " dove ");
            int en = Count(s, " you ", " your ", " the ", " please ", " are ", " is ", " license ", " have ", " do ", " what ");
            int best = Math.Max(Math.Max(de, fr), Math.Max(it, en));
            if (best == 0) return "de";
            return best == de ? "de" : best == fr ? "fr" : best == it ? "it" : "en";
        }

        static int Count(string s, params string[] keys) { int n = 0; foreach (var k in keys) if (s.Contains(k)) n++; return n; }

        public static ParsedUtterance Parse(string text)
        {
            var p = new ParsedUtterance { Raw = text ?? "" };
            if (string.IsNullOrWhiteSpace(text)) { p.Intents.Add(Intent.Unknown); return p; }
            p.Language = DetectLanguage(text);
            string t = text.Trim();
            foreach (var r in Rules)
                if (r.Rx.IsMatch(t))
                {
                    if (r.Intent == Intent.StateReason) { if (p.Violation == ViolationType.None || r.V == ViolationType.Speeding && !p.Has(Intent.StateReason)) p.Violation = r.V; if (!p.Has(Intent.StateReason)) p.Intents.Add(Intent.StateReason); }
                    else if (!p.Has(r.Intent)) p.Intents.Add(r.Intent);
                }
            var m = Num.Match(t); if (m.Success) { p.Number = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); if (!p.Has(Intent.StateReason)) { p.Intents.Add(Intent.StateReason); p.Violation = ViolationType.Speeding; } }
            // Remove accidental overlaps: "ausweis" matches both license and generic; if registration words present too, keep both.
            if (p.Has(Intent.RequestAllDocuments)) { p.Intents.Remove(Intent.RequestLicense); p.Intents.Remove(Intent.RequestRegistration); }
            if (p.Has(Intent.AskAlcohol) && p.Has(Intent.RequestBreathTest)) p.Intents.Remove(Intent.AskAlcohol);
            if (p.Has(Intent.AskPhoneUse) && p.Violation == ViolationType.PhoneUse) { p.Intents.Remove(Intent.AskPhoneUse); }
            if (p.Has(Intent.AskSeatbelt) && p.Violation == ViolationType.NoSeatbelt) { p.Intents.Remove(Intent.AskSeatbelt); }
            if (p.Has(Intent.AnnounceFine) && p.Has(Intent.AnnounceReport)) p.Intents.Remove(Intent.AnnounceFine);
            if (p.Has(Intent.Insult)) p.Politeness -= 1f; if (p.Has(Intent.Threaten)) p.Politeness -= 0.8f;
            if (Regex.IsMatch(t, @"\b(bitte|s'il vous pla[iî]t|per favore|please|danke|merci|grazie)\b", O)) p.Politeness += 0.4f;
            if (p.Has(Intent.Greeting) || p.Has(Intent.ThankYou) || p.Has(Intent.Apologize)) p.Politeness += 0.3f;
            if (p.Has(Intent.Calm)) p.Politeness += 0.2f;
            if (t.Contains("!") && t.Length < 25 && !p.Has(Intent.Greeting)) p.Politeness -= 0.15f;
            p.Politeness = Math.Max(-1, Math.Min(1, p.Politeness));
            if (p.Intents.Count == 0) p.Intents.Add(Intent.Unknown);
            return p;
        }
    }
}
