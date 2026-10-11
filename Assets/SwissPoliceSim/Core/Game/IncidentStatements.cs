using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Npc;
using SwissPoliceSim.Core.Traffic;

namespace SwissPoliceSim.Core.Game
{
    /// <summary>Writes what each person of an incident will say when asked "what happened?": honest people tell the truth, dishonest guilty parties shift the blame.</summary>
    public static class IncidentStatements
    {
        public static void Fill(Incident inc, Random r)
        {
            var sc = inc.Scenario; string street = inc.Address;
            for (int i = 0; i < sc.Roles.Count; i++)
            {
                var role = sc.Roles[i]; var p = role.Person; if (p == null) continue; bool liar = role.Guilty && p.Honesty < 0.6f; string s;
                switch (inc.Type)
                {
                    case IncidentType.TrafficAccident:
                    case IncidentType.TrafficAccidentInjured:
                    {
                        int other = i == 0 ? 1 : 0; string kind = sc.AtFaultViolation == ViolationType.RedLight ? "bei Rot über die Kreuzung" : sc.AtFaultViolation == ViolationType.Tailgating ? "zu dicht aufgefahren" : sc.AtFaultViolation == ViolationType.FailureToYield ? "die Vortrittsregel missachtet" : "unvorsichtig gefahren";
                        if (role.Guilty) s = liar ? "Ich bin ganz normal gefahren, plötzlich hat mir der andere den Weg abgeschnitten. Ich konnte nicht mehr bremsen." : $"Es ist meine Schuld. Ich habe {kind} und das andere Fahrzeug übersehen. Es tut mir sehr leid.";
                        else s = $"Ich bin korrekt gefahren. Der andere Fahrer hat {kind} – ich hatte keine Chance zu reagieren.";
                        if (role.Injured) s += " Mir tut der Nacken weh, ich glaube, ich bin verletzt.";
                        break;
                    }
                    case IncidentType.NoiseComplaint: s = role.Role.Contains("Anrufer") ? "Seit etwa neun Uhr dröhnt die Musik von nebenan. Ich habe schon zweimal geklingelt, aber niemand öffnet richtig." : (liar ? "Das war doch gar nicht so laut. Der Nachbar übertreibt immer." : "Wir feiern einen Geburtstag, tut mir leid, wir stellen die Musik gleich leiser."); break;
                    case IncidentType.Burglary: s = role.Guilty ? (liar ? "Ich weiss nicht, wovon Sie reden. Ich bin nur zufällig hier vorbeigelaufen." : "Okay, ich gebe es zu, ich bin eingebrochen. Ich hatte kein Geld mehr.") : "Ich bin nach Hause gekommen und die Tür war aufgebrochen. Es fehlen Schmuck und ein Laptop. Ich habe niemanden gesehen."; break;
                    case IncidentType.Shoplifting: s = role.Role.Contains("Filialleiter") ? "Ich habe gesehen, wie die Person etwas in die Jacke gesteckt hat und ohne zu bezahlen zum Ausgang gegangen ist." : (role.Guilty ? (liar ? "Ich wollte gerade bezahlen, das ist ein Missverständnis!" : "Ja, ich habe es eingesteckt. Ich hatte das Portemonnaie vergessen.") : "Ich habe nichts eingesteckt. Das können Sie gerne prüfen."); break;
                    case IncidentType.Dispute: s = role.Guilty ? (liar ? "Er hat angefangen, ich habe mich nur verteidigt!" : "Ich bin laut geworden, das war nicht in Ordnung. Es ging um die Parklücke.") : "Er hat mich angeschrien wegen der Parklücke. Ich wollte nur meine Ruhe."; break;
                    case IncidentType.MissingPerson: s = "Meine Tochter ist acht Jahre alt, trägt eine helle Jacke und einen roten Rucksack. Sie war vor einer halben Stunde noch auf dem Spielplatz."; break;
                    case IncidentType.AssistMotorist: s = "Mein Auto ist einfach stehengeblieben. Der Motor geht nicht mehr an, ich habe die Warnblinker eingeschaltet."; break;
                    case IncidentType.WantedVehicle: s = "Ich weiss nicht, was Sie von mir wollen."; break;
                    case IncidentType.SuspiciousPerson: s = role.Guilty ? (liar ? "Ich suche nur mein Auto, ich habe es hier irgendwo geparkt." : "Ich wollte mich nur umsehen … ja, ich habe in ein paar Autos geschaut.") : "Ich habe meinen Autoschlüssel verloren und suche mein Auto."; break;
                    default: s = role.Guilty ? "Ich habe nur ein bisschen zu viel getrunken, es geht mir gut." : "Ich habe nichts gesehen."; break;
                }
                role.Statement = s;
            }
        }
    }
}
