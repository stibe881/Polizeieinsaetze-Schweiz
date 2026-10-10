using System;
using System.Collections.Generic;
using System.Text;
using SwissPoliceSim.Core.Traffic;

namespace SwissPoliceSim.Core.Npc
{
    public enum NpcAction
    {
        None, ShowLicense, ShowRegistration, RefuseDocuments, StepOut, StayInCar, OpenTrunk, RefuseSearch, ConsentSearch, TakeBreathTest, RefuseBreathTest,
        Flee, Argue, Comply, Resist, RaiseHands, Cry, FilmWithPhone, HandOverItem, DriveOff, TurnOffEngine
    }

    public sealed class NpcReply
    {
        public string Text = ""; public readonly List<NpcAction> Actions = new List<NpcAction>();
        public bool Lied; public float AngerDelta, AnxietyDelta, TrustDelta; public readonly List<string> Tags = new List<string>();
        public NpcAction First => Actions.Count > 0 ? Actions[0] : NpcAction.None;
    }

    /// <summary>The objective facts of the current contact – known to the game, partly hidden from the NPC's honesty.</summary>
    public sealed class NpcSituation
    {
        public ViolationType TrueViolation = ViolationType.None; public bool NpcGuilty; public float MeasuredKmh; public int LimitKmh; public string Place = "";
        public bool IsDriver = true; public bool InVehicle = true; public bool BreathTestDone; public bool ExplainedReason;
        public bool TimeOfDayNight;
    }

    /// <summary>
    /// The person behind the window: personality, emotions (anger / anxiety / trust), memory and honesty. Produces replies and actions;
    /// whether the NPC lies depends on its traits and what it has to hide. The game validates every returned action (see NpcActionValidator).
    /// </summary>
    public sealed class NpcMind
    {
        public readonly Citizen C; public readonly NpcSituation S; readonly Random _r;
        public float Anger, Anxiety, Trust;
        public bool GaveLicense, GaveRegistration, SteppedOut, Fled, Hostile, ConfessedAlcohol, AdmittedViolation, DeniedViolation, ComplaintPlanned;
        public bool GaveFalseName; public readonly List<string> Memory = new List<string>();
        public int Turns;
        public string Language => C.Language;

        public NpcMind(Citizen c, NpcSituation s, int seed)
        {
            C = c; S = s; _r = new Random(seed); Anger = c.Anger; Anxiety = c.Anxiety; Trust = c.Trust;
        }

        // ------------------------------------------------------------------ entry
        public NpcReply Respond(ParsedUtterance u)
        {
            Turns++; Memory.Add("Polizei: " + u.Raw);
            string lang = C.Language == "rm" ? "de" : C.Language;
            var reply = new NpcReply(); var parts = new List<string>();
            // tone first
            Anger = Clamp(Anger - u.Politeness * 0.12f); Trust = Clamp(Trust + u.Politeness * 0.1f); Anxiety = Clamp(Anxiety - (u.Politeness > 0 ? 0.04f : -0.06f));
            if (u.Has(Intent.Insult)) { Anger = Clamp(Anger + 0.3f); Trust = Clamp(Trust - 0.25f); ComplaintPlanned = true; parts.Add(Pick(lang, new[] { "Sagen Sie mal, geht's noch? So lasse ich nicht mit mir reden.", "Beleidigen lasse ich mich nicht! Ich merke mir Ihren Namen.", "Wie reden Sie denn mit mir?" }, new[] { "Vous plaisantez ? On ne me parle pas comme ça.", "Je n'accepte pas d'être insulté, je vais déposer plainte." }, new[] { "Ma come si permette? Non accetto questo tono.", "Non mi parli così, farò un reclamo." }, new[] { "Excuse me? Don't talk to me like that." })); reply.Tags.Add("complaint"); }
            if (u.Has(Intent.Threaten)) { Anxiety = Clamp(Anxiety + 0.35f); Anger = Clamp(Anger + 0.15f); Trust = Clamp(Trust - 0.3f); parts.Add(Pick(lang, new[] { "Bitte … ich habe nichts getan. Beruhigen Sie sich!", "Sie drohen mir? Das ist doch nicht erlaubt!" }, new[] { "S'il vous plaît… calmez-vous, je n'ai rien fait.", "Vous me menacez ? C'est inadmissible." }, new[] { "La prego, si calmi… non ho fatto nulla.", "Mi sta minacciando? Non è ammesso." }, new[] { "Please calm down, I haven't done anything." })); reply.Tags.Add("complaint"); reply.Tags.Add("fear"); if (_r.NextDouble() < 0.3 + Anxiety * 0.3) reply.Actions.Add(NpcAction.RaiseHands); }
            foreach (var intent in u.Intents)
            {
                var piece = HandleIntent(intent, u, lang, reply);
                if (!string.IsNullOrEmpty(piece)) parts.Add(piece);
            }
            reply.Text = string.Join(" ", parts).Trim();
            if (reply.Text.Length == 0) reply.Text = Pick(lang, new[] { "Wie bitte?", "Entschuldigung, ich habe Sie nicht verstanden.", "Können Sie das wiederholen?" }, new[] { "Pardon ?", "Je n'ai pas compris, pouvez-vous répéter ?" }, new[] { "Prego?", "Non ho capito, può ripetere?" }, new[] { "Sorry, could you repeat that?" });
            // flight decision
            if (!Fled && ShouldFlee(u)) { Fled = true; reply.Actions.Insert(0, NpcAction.Flee); reply.Tags.Add("flight"); }
            if (Anger > 0.8f && !Hostile) { Hostile = true; reply.Tags.Add("hostile"); }
            reply.AngerDelta = Anger - C.Anger; reply.AnxietyDelta = Anxiety - C.Anxiety; reply.TrustDelta = Trust - C.Trust;
            Memory.Add(C.FirstName + ": " + reply.Text);
            return reply;
        }

        bool ShouldFlee(ParsedUtterance u)
        {
            if (!S.IsDriver && !S.InVehicle) { }
            bool trigger = u.Has(Intent.RequestStepOut) || u.Has(Intent.AnnounceArrest) || u.Has(Intent.RequestBreathTest) || u.Has(Intent.RequestLicense) || u.Has(Intent.RequestOpenTrunk);
            if (!trigger) return false;
            float risk = (C.Wanted ? 0.55f : 0f) + (C.License == LicenseStatus.None || C.License == LicenseStatus.Suspended ? 0.30f : 0f) + (C.BloodAlcoholPermille >= 0.8f ? 0.30f : 0f) + (C.CarriesIllegalItem ? 0.2f : 0f);
            risk = risk * (0.45f + Anxiety * 0.6f) * (1.1f - C.Honesty * 0.5f);
            return _r.NextDouble() < risk * 0.9f;
        }

        // ------------------------------------------------------------------ intents
        string HandleIntent(Intent i, ParsedUtterance u, string lang, NpcReply reply)
        {
            string addr = C.FormalAddress(lang);
            switch (i)
            {
                case Intent.Greeting:
                    Trust = Clamp(Trust + 0.04f);
                    return Pick(lang, new[] { "Grüezi.", "Guten Tag.", "Guten Tag, Herr Wachtmeister.", "Grüezi, was ist denn los?" }, new[] { "Bonjour.", "Bonjour, monsieur l'agent.", "Bonjour, il y a un problème ?" }, new[] { "Buongiorno.", "Buongiorno, c'è un problema?" }, new[] { "Hello.", "Good day, officer." });

                case Intent.Goodbye:
                case Intent.ReleaseDriver:
                    return Pick(lang, new[] { "Danke, auf Wiedersehen.", "Ich wünsche Ihnen noch einen schönen Tag.", "Adieu, schönen Tag noch." }, new[] { "Merci, au revoir.", "Bonne journée." }, new[] { "Grazie, arrivederci.", "Buona giornata." }, new[] { "Thank you, goodbye." });

                case Intent.ThankYou: return Pick(lang, new[] { "Gern geschehen." }, new[] { "Je vous en prie." }, new[] { "Prego." }, new[] { "You're welcome." });

                case Intent.Apologize: Anger = Clamp(Anger - 0.15f); Trust = Clamp(Trust + 0.1f); return Pick(lang, new[] { "Schon gut, Entschuldigung angenommen.", "Danke für die Entschuldigung." }, new[] { "C'est bon, merci.", "Excuses acceptées." }, new[] { "Va bene, grazie.", "Scuse accettate." }, new[] { "That's alright." });

                case Intent.Calm: Anger = Clamp(Anger - 0.2f); Anxiety = Clamp(Anxiety - 0.15f); return Pick(lang, new[] { "Ja, ja, ich bin ja ruhig …", "Okay, ich atme durch." }, new[] { "Oui, je suis calme…", "D'accord, je respire." }, new[] { "Sì, sono calmo…", "Va bene, respiro." }, new[] { "Okay, I'm calm." });

                case Intent.AskToRepeat: return null;

                case Intent.AskIfKnowsReason: return AnswerKnowsReason(lang, reply);
                case Intent.StateReason: return AnswerStatedReason(u, lang, reply);

                case Intent.RequestLicense: return AnswerLicense(lang, reply);
                case Intent.RequestRegistration: return AnswerRegistration(lang, reply);
                case Intent.RequestAllDocuments: { var a = AnswerLicense(lang, reply); var b = AnswerRegistration(lang, reply); return a + " " + b; }

                case Intent.AskName:
                    if (C.Wanted && C.Honesty < 0.45f && !GaveFalseName && _r.NextDouble() < 0.5) { GaveFalseName = true; reply.Lied = true; return Pick(lang, new[] { "Ich heisse Marco Keller.", "Peter Meier." }, new[] { "Je m'appelle Jean Martin." }, new[] { "Mi chiamo Marco Rossi." }, new[] { "My name is John Smith." }); }
                    return Pick(lang, new[] { $"Ich heisse {C.FullName}.", $"{C.LastName}, {C.FirstName}." }, new[] { $"Je m'appelle {C.FullName}." }, new[] { $"Mi chiamo {C.FullName}." }, new[] { $"My name is {C.FullName}." });

                case Intent.AskAddress: return Pick(lang, new[] { $"Ich wohne an der {C.Address}.", $"{C.Address}." }, new[] { $"J'habite {C.Address}." }, new[] { $"Abito in {C.Address}." }, new[] { $"I live at {C.Address}." });

                case Intent.AskDestination: return Pick(lang, new[] { "Ich fahre nach Hause.", "Ich bin unterwegs zur Arbeit.", "Ich muss noch einkaufen.", "Zum Bahnhof, ich hole jemanden ab.", "Ich besuche meine Eltern." }, new[] { "Je rentre à la maison.", "Je vais au travail.", "Je vais faire des courses." }, new[] { "Torno a casa.", "Vado al lavoro.", "Devo fare la spesa." }, new[] { "I'm heading home.", "I'm on my way to work." });
                case Intent.AskOrigin: return Pick(lang, new[] { "Ich komme von der Arbeit.", "Ich war bei einem Kollegen.", "Von zu Hause.", "Ich komme vom Einkaufen." }, new[] { "Je viens du travail.", "Je viens de chez un ami." }, new[] { "Vengo dal lavoro.", "Da un amico." }, new[] { "I'm coming from work." });

                case Intent.AskAlcohol: return AnswerAlcohol(lang, reply);
                case Intent.AskDrugs:
                    if (C.UnderDrugs && C.Honesty < 0.5f) { reply.Lied = true; Anxiety = Clamp(Anxiety + 0.2f); return Pick(lang, new[] { "Nein, natürlich nicht!" }, new[] { "Non, bien sûr que non !" }, new[] { "No, certo che no!" }, new[] { "No, of course not." }); }
                    return Pick(lang, new[] { "Nein, ich nehme nichts. Höchstens ein Medikament gegen Heuschnupfen." }, new[] { "Non, rien du tout." }, new[] { "No, niente." }, new[] { "No, nothing." });

                case Intent.RequestBreathTest: return AnswerBreath(lang, reply);

                case Intent.RequestStepOut:
                    if (Hostile && _r.NextDouble() < 0.4) { reply.Actions.Add(NpcAction.Argue); return Pick(lang, new[] { "Wieso soll ich aussteigen? Ich habe nichts gemacht!" }, new[] { "Pourquoi je descendrais ? Je n'ai rien fait !" }, new[] { "Perché dovrei scendere? Non ho fatto niente!" }, new[] { "Why should I get out? I haven't done anything!" }); }
                    SteppedOut = true; reply.Actions.Add(NpcAction.StepOut);
                    return Pick(lang, new[] { "Okay, ich steige aus.", "Natürlich, einen Moment." }, new[] { "D'accord, je descends." }, new[] { "Va bene, scendo." }, new[] { "Okay, getting out." });

                case Intent.OrderStayInCar: reply.Actions.Add(NpcAction.StayInCar); return Pick(lang, new[] { "Gut, ich bleibe sitzen." }, new[] { "D'accord, je reste dans la voiture." }, new[] { "Va bene, resto in macchina." }, new[] { "Okay, I'll stay here." });

                case Intent.RequestMove: reply.Actions.Add(NpcAction.TurnOffEngine); return Pick(lang, new[] { "Ich stelle den Motor ab." }, new[] { "J'éteins le moteur." }, new[] { "Spengo il motore." }, new[] { "Turning the engine off." });

                case Intent.RequestOpenTrunk:
                    if (C.CarriesIllegalItem && C.Honesty < 0.6f) { reply.Actions.Add(NpcAction.RefuseSearch); Anxiety = Clamp(Anxiety + 0.25f); reply.Lied = false; return Pick(lang, new[] { "Muss das sein? Da ist nur Gerümpel drin.", "Dazu brauchen Sie doch einen Grund, oder?" }, new[] { "Il faut vraiment ? Il n'y a que des bricoles.", "Vous avez une raison pour ça ?" }, new[] { "Deve proprio? Ci sono solo cianfrusaglie.", "Ha un motivo per farlo?" }, new[] { "Do you have a reason to search my trunk?" }); }
                    reply.Actions.Add(NpcAction.OpenTrunk); return Pick(lang, new[] { "Kein Problem, ich mache den Kofferraum auf." }, new[] { "Pas de problème, j'ouvre le coffre." }, new[] { "Nessun problema, apro il bagagliaio." }, new[] { "Sure, I'll open the trunk." });

                case Intent.RequestSearchConsent:
                    if (C.CarriesIllegalItem || C.Wanted) { reply.Actions.Add(NpcAction.RefuseSearch); return Pick(lang, new[] { "Nein, ich bin nicht einverstanden. Haben Sie einen Durchsuchungsbefehl?", "Ich möchte das nicht. Sie haben keinen Grund." }, new[] { "Non, je ne suis pas d'accord.", "Avez-vous un mandat ?" }, new[] { "No, non sono d'accordo. Ha un mandato?" }, new[] { "No, I don't consent. Do you have a warrant?" }); }
                    if (C.Temperament == Temperament.Arrogant || Anger > 0.6f) { reply.Actions.Add(NpcAction.RefuseSearch); return Pick(lang, new[] { "Das lasse ich mir nicht gefallen. Wieso ich?" }, new[] { "Pourquoi moi ? Je refuse." }, new[] { "Perché proprio io? Rifiuto." }, new[] { "Why me? I refuse." }); }
                    reply.Actions.Add(NpcAction.ConsentSearch); return Pick(lang, new[] { "Ich habe nichts zu verbergen, bitte.", "Von mir aus, gerne." }, new[] { "Je n'ai rien à cacher, allez-y." }, new[] { "Non ho niente da nascondere, prego." }, new[] { "I have nothing to hide, go ahead." });

                case Intent.AnnounceFine: return AnswerSanction(lang, reply, "fine");
                case Intent.AnnounceWarning: Anger = Clamp(Anger - 0.2f); Trust = Clamp(Trust + 0.15f); return Pick(lang, new[] { "Vielen Dank, das war nicht mit Absicht. Es kommt nicht wieder vor.", "Danke für das Verständnis, ich fahre ab jetzt vorsichtiger." }, new[] { "Merci beaucoup, ça ne se reproduira pas." }, new[] { "Grazie mille, non succederà più." }, new[] { "Thank you, it won't happen again." });
                case Intent.AnnounceReport: return AnswerSanction(lang, reply, "report");

                case Intent.AnnounceArrest:
                    Anxiety = Clamp(Anxiety + 0.3f);
                    if (C.Temperament == Temperament.Aggressive || Anger > 0.7f) { reply.Actions.Add(NpcAction.Resist); Anger = Clamp(Anger + 0.2f); return Pick(lang, new[] { "Lassen Sie mich los! Das ist ein Fehler!", "Fassen Sie mich nicht an!" }, new[] { "Lâchez-moi ! C'est une erreur !" }, new[] { "Mi lasci! È un errore!" }, new[] { "Let go of me!" }); }
                    reply.Actions.Add(NpcAction.Comply); return Pick(lang, new[] { "Ich komme mit, aber ich will einen Anwalt.", "Okay … ich leiste keinen Widerstand." }, new[] { "Je vous suis, mais je veux un avocat." }, new[] { "Vengo con voi, ma voglio un avvocato." }, new[] { "I'll come along, but I want a lawyer." });

                case Intent.ReadRights: Trust = Clamp(Trust + 0.05f); return Pick(lang, new[] { "Ich habe verstanden. Ich sage jetzt nichts mehr ohne Anwalt." }, new[] { "J'ai compris. Je ne dirai plus rien sans avocat." }, new[] { "Ho capito. Non dico più niente senza avvocato." }, new[] { "I understand. I'll wait for my lawyer." });

                case Intent.AskIfInjured: return Pick(lang, new[] { "Nein, mir geht es gut, danke der Nachfrage." }, new[] { "Non, ça va, merci." }, new[] { "No, sto bene, grazie." }, new[] { "No, I'm fine, thanks." });

                case Intent.AskPhoneUse: return AnswerPhone(lang, reply);
                case Intent.AskSeatbelt: return AnswerBelt(lang, reply);
                case Intent.AskSpeedReason: return Pick(lang, new[] { "Ich war ein bisschen spät dran, tut mir leid.", "Ich habe nicht auf die Geschwindigkeit geachtet.", "Ich bin keineswegs zu schnell gefahren." }, new[] { "J'étais un peu en retard.", "Je n'ai pas fait attention à ma vitesse." }, new[] { "Ero un po' in ritardo.", "Non ho badato alla velocità." }, new[] { "I was running a bit late." });

                case Intent.Unknown: default: return null;
            }
        }

        // ------------------------------------------------------------------ specific answers
        string AnswerKnowsReason(string lang, NpcReply reply)
        {
            bool aware = S.NpcGuilty;
            float admitP = C.Honesty * 0.65f + Trust * 0.25f - Anger * 0.3f - (C.Wanted ? 0.2f : 0f);
            if (!aware) return Pick(lang, new[] { "Nein, keine Ahnung. Was ist denn passiert?", "Nein, wieso haben Sie mich angehalten?", "Ich wüsste nicht, was ich falsch gemacht haben soll." }, new[] { "Non, aucune idée. Qu'est-ce qui s'est passé ?", "Je ne vois pas ce que j'ai fait." }, new[] { "No, non ne ho idea. Che cosa è successo?", "Non vedo cosa abbia sbagliato." }, new[] { "No idea, what happened?" });
            if (_r.NextDouble() < admitP)
            { AdmittedViolation = true; return AdmitViolation(lang); }
            DeniedViolation = true; reply.Lied = true; Anxiety = Clamp(Anxiety + 0.08f);
            return Pick(lang, new[] { "Nein, ich weiss nicht, was Sie meinen.", "Ich bin mir keiner Schuld bewusst.", "Keine Ahnung, ich bin wie immer gefahren." }, new[] { "Non, je ne vois pas de quoi vous parlez.", "Je n'ai pas conscience d'une faute." }, new[] { "No, non so a cosa si riferisca.", "Non mi sembra di aver sbagliato." }, new[] { "No, I don't know what you mean." });
        }

        string AdmitViolation(string lang)
        {
            switch (S.TrueViolation)
            {
                case ViolationType.Speeding: return Pick(lang, new[] { "Ja, ich war wohl etwas zu schnell, tut mir leid.", "Ich habe nicht auf den Tacho geschaut. Das war zu schnell, ich weiss." }, new[] { "Oui, j'étais sans doute trop vite, désolé." }, new[] { "Sì, forse andavo troppo veloce, mi scusi." }, new[] { "Yes, I was probably too fast, sorry." });
                case ViolationType.RedLight: return Pick(lang, new[] { "Die Ampel … ja, die war wohl schon rot. Mein Fehler.", "Ich dachte, es sei noch Gelb. Es tut mir leid." }, new[] { "Le feu… oui, il était rouge, je crois. Mon erreur." }, new[] { "Il semaforo… sì, era già rosso, credo." }, new[] { "The light… yes, it was red, my mistake." });
                case ViolationType.PhoneUse: return Pick(lang, new[] { "Ja, ich hatte kurz das Handy in der Hand. Das war dumm." }, new[] { "Oui, j'avais mon téléphone en main, c'était bête." }, new[] { "Sì, avevo il telefono in mano, è stato stupido." }, new[] { "Yes, I had my phone in my hand, that was dumb." });
                case ViolationType.NoSeatbelt: return Pick(lang, new[] { "Stimmt, ich hatte mich noch nicht angegurtet." }, new[] { "C'est vrai, je n'avais pas attaché ma ceinture." }, new[] { "È vero, non avevo allacciato la cintura." }, new[] { "True, I hadn't buckled up yet." });
                case ViolationType.Tailgating: return Pick(lang, new[] { "Ich bin wohl zu nah aufgefahren, ja." }, new[] { "J'étais sans doute trop près, oui." }, new[] { "Ero troppo vicino, sì." }, new[] { "I was probably too close, yes." });
                default: return Pick(lang, new[] { "Ja, ich glaube, ich habe einen Fehler gemacht." }, new[] { "Oui, je crois que j'ai fait une erreur." }, new[] { "Sì, credo di aver commesso un errore." }, new[] { "Yes, I think I made a mistake." });
            }
        }

        string AnswerStatedReason(ParsedUtterance u, string lang, NpcReply reply)
        {
            S.ExplainedReason = true;
            bool matches = u.Violation == S.TrueViolation || (u.Violation == ViolationType.None);
            if (!matches || !S.NpcGuilty)
            {   // officer's claim does not fit the truth (or nothing happened): honest protest
                Anger = Clamp(Anger + 0.12f);
                return Pick(lang, new[] { "Das stimmt nicht, das habe ich nicht gemacht.", "Da müssen Sie sich irren, Herr Wachtmeister.", "Das war nicht ich, das ist ein Missverständnis." }, new[] { "Ce n'est pas vrai, je n'ai pas fait ça.", "Vous devez vous tromper." }, new[] { "Non è vero, non l'ho fatto.", "Deve esserci un errore." }, new[] { "That's not true, I didn't do that." });
            }
            float admitP = C.Honesty * 0.55f + Trust * 0.3f - Anger * 0.35f + (u.Number > 0 && S.MeasuredKmh > 0 && Math.Abs(u.Number - S.MeasuredKmh) < 3 ? 0.15f : 0f);
            switch (S.TrueViolation)
            {
                case ViolationType.Speeding:
                    if (u.Number > 0 && S.MeasuredKmh > 0 && Math.Abs(u.Number - S.MeasuredKmh) > 6) return Pick(lang, new[] { $"{u.Number} km/h? Das kann nicht sein, das Gerät muss falsch messen." }, new[] { $"{u.Number} km/h ? Impossible, l'appareil se trompe." }, new[] { $"{u.Number} km/h? Impossibile, lo strumento sbaglia." }, new[] { $"{u.Number}? The device must be wrong." });
                    if (_r.NextDouble() < admitP) { AdmittedViolation = true; Anger = Clamp(Anger - 0.05f); return Pick(lang, new[] { "Ja, das stimmt, ich war zu schnell. Es tut mir leid.", "Ja, ich habe die Tafel nicht gesehen.", "Okay, ich war etwas zu schnell." }, new[] { "Oui, c'est vrai, j'étais trop vite." }, new[] { "Sì, è vero, andavo troppo veloce." }, new[] { "Yes, that's true, I was speeding." }); }
                    DeniedViolation = true; reply.Lied = true; Anger = Clamp(Anger + 0.1f);
                    return Pick(lang, new[] { "Nein, ich bin höchstens 50 gefahren. Ich schaue immer auf den Tacho.", "Das kann nicht sein, ich bin im Verkehr mitgeschwommen.", "Der Tacho zeigte weniger an." }, new[] { "Non, je roulais à 50 maximum.", "Je suivais le trafic." }, new[] { "No, andavo a 50 al massimo.", "Seguivo il traffico." }, new[] { "No, I was doing 50 at most." });
                case ViolationType.RedLight:
                    if (_r.NextDouble() < admitP) { AdmittedViolation = true; return AdmitViolation(lang); }
                    DeniedViolation = true; reply.Lied = true; return Pick(lang, new[] { "Das kann nicht sein, ich bin sicher, die Ampel war noch gelb.", "Die Ampel war orange, als ich durchgefahren bin!" }, new[] { "Impossible, le feu était encore orange.", "J'étais sûr que c'était orange." }, new[] { "Impossibile, il semaforo era ancora giallo." }, new[] { "No way, it was still amber." });
                case ViolationType.PhoneUse:
                    if (_r.NextDouble() < admitP) { AdmittedViolation = true; return AdmitViolation(lang); }
                    DeniedViolation = true; reply.Lied = true; return Pick(lang, new[] { "Ich habe nur das Navi eingestellt, nicht telefoniert.", "Ich habe die Freisprecheinrichtung benutzt." }, new[] { "J'ai juste réglé le GPS.", "J'utilisais le kit mains libres." }, new[] { "Stavo solo impostando il navigatore.", "Usavo il vivavoce." }, new[] { "I was only setting the GPS." });
                case ViolationType.NoSeatbelt:
                    if (_r.NextDouble() < admitP) { AdmittedViolation = true; return AdmitViolation(lang); }
                    DeniedViolation = true; reply.Lied = true; return Pick(lang, new[] { "Ich war angegurtet, ich habe ihn eben gerade gelöst." }, new[] { "J'étais attaché, je viens de le détacher." }, new[] { "Ero allacciato, l'ho appena slacciata." }, new[] { "I was buckled, I just undid it." });
                default:
                    if (_r.NextDouble() < admitP) { AdmittedViolation = true; return AdmitViolation(lang); }
                    DeniedViolation = true; reply.Lied = true; return Pick(lang, new[] { "Das sehe ich anders. Ich habe mich korrekt verhalten." }, new[] { "Je vois les choses autrement." }, new[] { "Io la vedo diversamente." }, new[] { "I see it differently." });
            }
        }

        string AnswerLicense(string lang, NpcReply reply)
        {
            GaveLicense = true;
            switch (C.License)
            {
                case LicenseStatus.None:
                    if (C.Honesty < 0.5f) { reply.Lied = true; reply.Actions.Add(NpcAction.RefuseDocuments); Anxiety = Clamp(Anxiety + 0.3f); return Pick(lang, new[] { "Den habe ich zuhause vergessen, tut mir leid.", "Der liegt im anderen Mantel, den habe ich nicht dabei." }, new[] { "Je l'ai oublié à la maison.", "Il est dans l'autre manteau." }, new[] { "L'ho dimenticata a casa.", "È nell'altro cappotto." }, new[] { "I left it at home." }); }
                    reply.Actions.Add(NpcAction.RefuseDocuments); Anxiety = Clamp(Anxiety + 0.2f);
                    return Pick(lang, new[] { "Ich … habe keinen Führerausweis. Ich habe die Prüfung nie gemacht." }, new[] { "Je… n'ai pas de permis." }, new[] { "Io… non ho la patente." }, new[] { "I… don't have a license." });
                case LicenseStatus.Suspended:
                case LicenseStatus.Withdrawn:
                    if (C.Honesty < 0.55f) { reply.Lied = true; Anxiety = Clamp(Anxiety + 0.25f); reply.Actions.Add(NpcAction.ShowLicense); return Pick(lang, new[] { "Bitte, hier ist er.", "Einen Moment, ich suche ihn." }, new[] { "Voici.", "Un instant." }, new[] { "Ecco.", "Un momento." }, new[] { "Here you go." }); }
                    reply.Actions.Add(NpcAction.ShowLicense); Anxiety = Clamp(Anxiety + 0.3f);
                    return Pick(lang, new[] { "Hier. Aber … er ist mir vorübergehend entzogen worden, ich hätte nicht fahren dürfen." }, new[] { "Voici… mais mon permis m'a été retiré." }, new[] { "Ecco… ma mi è stata ritirata." }, new[] { "Here… but it was suspended." });
                default:
                    reply.Actions.Add(NpcAction.ShowLicense);
                    return Pick(lang, new[] { "Ja, einen Moment bitte.", "Natürlich, hier ist mein Führerausweis.", "Moment, der steckt im Portemonnaie." }, new[] { "Oui, un instant, voici mon permis.", "Bien sûr, le voici." }, new[] { "Certo, un momento, ecco la patente.", "Prego." }, new[] { "Sure, here's my license." });
            }
        }

        string AnswerRegistration(string lang, NpcReply reply)
        {
            GaveRegistration = true;
            if (!C.Vehicle.Insured && C.Honesty < 0.5f) { reply.Actions.Add(NpcAction.ShowRegistration); Anxiety = Clamp(Anxiety + 0.15f); return Pick(lang, new[] { "Der Fahrzeugausweis ist im Handschuhfach, einen Moment.", "Hier, bitte." }, new[] { "La carte grise est dans la boîte à gants.", "Voilà." }, new[] { "Il libretto è nel cassetto portaoggetti.", "Ecco." }, new[] { "It's in the glovebox, one moment." }); }
            reply.Actions.Add(NpcAction.ShowRegistration);
            return Pick(lang, new[] { "Der Fahrzeugausweis ist im Handschuhfach, ich hole ihn.", "Hier ist der Fahrzeugausweis.", "Moment, ich greife kurz nach rechts." }, new[] { "La carte grise est dans la boîte à gants, je la prends.", "Voici les papiers du véhicule." }, new[] { "Il libretto è nel cruscotto, lo prendo.", "Ecco i documenti del veicolo." }, new[] { "Here are the vehicle papers." });
        }

        string AnswerAlcohol(string lang, NpcReply reply)
        {
            if (C.BloodAlcoholPermille <= 0.05f)
                return Pick(lang, new[] { "Nein, ich habe nichts getrunken.", "Ich trinke nie, wenn ich fahre." }, new[] { "Non, je n'ai rien bu.", "Je ne bois jamais quand je conduis." }, new[] { "No, non ho bevuto.", "Non bevo mai quando guido." }, new[] { "No, I haven't had anything to drink." });
            if (C.Honesty > 0.6f && Trust > 0.35f) { ConfessedAlcohol = true; return Pick(lang, new[] { "Ich habe zum Mittagessen zwei Gläser Wein getrunken, aber das ist schon eine Weile her.", "Ein, zwei Bier vorhin … ich dachte, es geht noch." }, new[] { "J'ai bu deux verres de vin au déjeuner.", "Une ou deux bières tout à l'heure." }, new[] { "Ho bevuto due bicchieri di vino a pranzo.", "Una o due birre prima." }, new[] { "I had two glasses of wine at lunch." }); }
            reply.Lied = true; Anxiety = Clamp(Anxiety + 0.2f);
            return Pick(lang, new[] { "Nein, ich habe nichts getrunken. Nur ein Mineralwasser.", "Das ist eine Frechheit, ich bin nüchtern!" }, new[] { "Non, juste de l'eau minérale.", "Quelle insolence, je suis sobre !" }, new[] { "No, solo acqua minerale.", "Che insolenza, sono sobrio!" }, new[] { "No, only water." });
        }

        string AnswerBreath(string lang, NpcReply reply)
        {
            if (C.BloodAlcoholPermille >= 0.5f && C.Honesty < 0.45f && Anger + Anxiety > 0.7f && _r.NextDouble() < 0.5)
            { reply.Actions.Add(NpcAction.RefuseBreathTest); Anxiety = Clamp(Anxiety + 0.2f); Anger = Clamp(Anger + 0.1f); return Pick(lang, new[] { "Nein, ich blase nicht. Das muss ich nicht.", "Ich verweigere den Test." }, new[] { "Non, je ne souffle pas.", "Je refuse le test." }, new[] { "No, non soffio.", "Rifiuto il test." }, new[] { "I refuse the breath test." }); }
            reply.Actions.Add(NpcAction.TakeBreathTest);
            return Pick(lang, new[] { "Gut, wenn es sein muss. Wo soll ich blasen?", "Kein Problem, ich habe nichts zu verbergen." }, new[] { "D'accord, je souffle.", "Aucun problème." }, new[] { "Va bene, soffio.", "Nessun problema." }, new[] { "Fine, I'll do the test." });
        }

        string AnswerPhone(string lang, NpcReply reply)
        {
            if (S.TrueViolation == ViolationType.PhoneUse && S.NpcGuilty && C.Honesty < 0.6f) { reply.Lied = true; return Pick(lang, new[] { "Nein, ich habe nicht telefoniert, das war die Freisprecheinrichtung." }, new[] { "Non, j'utilisais le kit mains libres." }, new[] { "No, usavo il vivavoce." }, new[] { "No, hands-free only." }); }
            if (S.TrueViolation == ViolationType.PhoneUse && S.NpcGuilty) { AdmittedViolation = true; return AdmitViolation(lang); }
            return Pick(lang, new[] { "Nein, ich habe nicht telefoniert." }, new[] { "Non, je n'ai pas téléphoné." }, new[] { "No, non ho telefonato." }, new[] { "No, I wasn't on the phone." });
        }

        string AnswerBelt(string lang, NpcReply reply)
        {
            if (S.TrueViolation == ViolationType.NoSeatbelt && S.NpcGuilty) { if (C.Honesty < 0.5f) { reply.Lied = true; return Pick(lang, new[] { "Doch, natürlich war ich angegurtet." }, new[] { "Mais si, j'étais attaché." }, new[] { "Certo che ero allacciato." }, new[] { "Of course I was buckled." }); } AdmittedViolation = true; return AdmitViolation(lang); }
            return Pick(lang, new[] { "Ja, ich bin immer angegurtet." }, new[] { "Oui, toujours." }, new[] { "Sì, sempre." }, new[] { "Yes, always." });
        }

        string AnswerSanction(string lang, NpcReply reply, string kind)
        {
            bool fine = kind == "fine";
            if (C.Temperament == Temperament.Aggressive || Anger > 0.6f) { Anger = Clamp(Anger + 0.15f); ComplaintPlanned = true; reply.Tags.Add("complaint"); reply.Actions.Add(NpcAction.Argue); return Pick(lang, new[] { fine ? "Das ist doch lächerlich! Dagegen wehre ich mich." : "Eine Anzeige?! Da hören Sie noch von mir.", "Das ist reine Schikane, ich beschwere mich." }, new[] { "C'est ridicule ! Je vais contester.", "C'est du harcèlement, je vais me plaindre." }, new[] { "È ridicolo! Farò ricorso.", "È una vessazione, farò un reclamo." }, new[] { "That's ridiculous, I'll contest it." }); }
            if (C.Temperament == Temperament.Arrogant) { reply.Actions.Add(NpcAction.Comply); return Pick(lang, new[] { "Na gut. Das zahle ich aus der Portokasse.", "Meinetwegen, machen Sie Ihren Papierkram." }, new[] { "Très bien, faites votre paperasse." }, new[] { "Va bene, faccia pure le sue pratiche." }, new[] { "Fine, do your paperwork." }); }
            if (C.Temperament == Temperament.Anxious || C.Temperament == Temperament.Nervous) { Anxiety = Clamp(Anxiety + 0.1f); reply.Actions.Add(NpcAction.Comply); return Pick(lang, new[] { "Oh nein … okay. Was passiert jetzt mit meinem Ausweis?", "Ich zahle sofort. Bitte nehmen Sie mir den Ausweis nicht weg!" }, new[] { "Oh non… d'accord. Que va-t-il arriver à mon permis ?" }, new[] { "Oh no… va bene. Cosa succede alla mia patente?" }, new[] { "Oh no… okay. What happens to my license?" }); }
            reply.Actions.Add(NpcAction.Comply);
            return Pick(lang, new[] { "Okay, ist in Ordnung. Ich akzeptiere das.", "Gut, ich habe es verstanden. Danke für die Erklärung." }, new[] { "D'accord, j'accepte.", "Bien, j'ai compris." }, new[] { "Va bene, accetto.", "Ho capito, grazie." }, new[] { "Okay, I accept that." });
        }

        // ------------------------------------------------------------------ helpers
        string Pick(string lang, string[] de, string[] fr, string[] it, string[] en)
        {
            string[] arr = lang == "fr" ? fr : lang == "it" ? it : lang == "en" ? en : de;
            if (arr == null || arr.Length == 0) arr = de;
            return arr[_r.Next(arr.Length)];
        }

        static float Clamp(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
