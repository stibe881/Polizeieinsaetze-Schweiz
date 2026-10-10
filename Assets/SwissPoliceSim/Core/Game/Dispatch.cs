using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Npc;
using SwissPoliceSim.Core.Traffic;

namespace SwissPoliceSim.Core.Game
{
    public enum IncidentType
    {
        TrafficAccident, TrafficAccidentInjured, NoiseComplaint, Burglary, Shoplifting, Dispute, MissingPerson, AssistMotorist, WantedVehicle, RoadHazard, SuspiciousPerson, DrunkPerson
    }
    public enum IncidentState { Offered, Accepted, EnRoute, OnScene, Resolved, Declined, Failed }

    public sealed class ScenarioRole
    {
        public string Role = "";               // "Fahrer A", "Verdächtiger", "Zeuge", "Geschädigter"
        public Citizen Person; public bool Guilty; public bool Injured; public bool IsVehicleBound; public string Statement = "";
        public Temperament Behaviour;
    }

    /// <summary>The hidden truth of an incident: who did what. Statements of NPCs and the evaluation of the officer's report derive from it.</summary>
    public sealed class Scenario
    {
        public readonly List<ScenarioRole> Roles = new List<ScenarioRole>();
        public string Truth = "";               // one-paragraph objective description
        public ViolationType AtFaultViolation = ViolationType.None; public int AtFaultRole = -1;
        public bool NeedsAmbulance, NeedsFire, NeedsTow, NeedsCriminalPolice;
        public List<string> Objectives = new List<string>();
    }

    public sealed class Incident
    {
        public int Id; public IncidentType Type; public IncidentState State = IncidentState.Offered;
        public double E, N; public string Address = "", Municipality = "", Canton = "";
        public int Urgency;                    // 1 low … 4 life-threatening (Blaulicht/Horn)
        public string RadioText = "", CallerText = "";
        public float CreatedAt, AcceptedAt, ArrivedAt, ResolvedAt; public Scenario Scenario = new Scenario();
        public float DeadlineSeconds = 900;
        public bool AmbulanceCalled, FireCalled, BackupCalled;
        public string TypeLabel
        {
            get
            {
                switch (Type)
                {
                    case IncidentType.TrafficAccident: return "Verkehrsunfall (Sachschaden)"; case IncidentType.TrafficAccidentInjured: return "Verkehrsunfall mit Verletzten"; case IncidentType.NoiseComplaint: return "Ruhestörung";
                    case IncidentType.Burglary: return "Einbruchmeldung"; case IncidentType.Shoplifting: return "Ladendiebstahl"; case IncidentType.Dispute: return "Streitigkeit"; case IncidentType.MissingPerson: return "Vermisstenfall";
                    case IncidentType.AssistMotorist: return "Hilfeleistung"; case IncidentType.WantedVehicle: return "Fahndung Fahrzeug"; case IncidentType.RoadHazard: return "Hindernis auf der Fahrbahn";
                    case IncidentType.SuspiciousPerson: return "Verdächtige Person"; default: return "Betrunkene Person";
                }
            }
        }
    }

    /// <summary>Dispatch centre ("Leitstelle"): generates incidents anywhere on the real road network with Swiss radio phrasing.</summary>
    public sealed class DispatchCenter
    {
        readonly Random _r; int _next = 1; readonly TrafficNetwork _net; readonly AdminBoundaries _admin;
        public readonly List<Incident> Incidents = new List<Incident>();
        public string CallSign = "Patrouille 12";
        public DispatchCenter(TrafficNetwork net, AdminBoundaries admin, int seed) { _net = net; _admin = admin; _r = new Random(seed); }

        static readonly (IncidentType t, double w, int urgency)[] Table =
        {
            (IncidentType.TrafficAccident, 0.24, 2), (IncidentType.TrafficAccidentInjured, 0.07, 4), (IncidentType.NoiseComplaint, 0.14, 1), (IncidentType.Burglary, 0.08, 3),
            (IncidentType.Shoplifting, 0.10, 2), (IncidentType.Dispute, 0.11, 2), (IncidentType.MissingPerson, 0.03, 3), (IncidentType.AssistMotorist, 0.09, 1),
            (IncidentType.WantedVehicle, 0.04, 3), (IncidentType.RoadHazard, 0.06, 2), (IncidentType.SuspiciousPerson, 0.02, 2), (IncidentType.DrunkPerson, 0.02, 1)
        };

        /// <summary>Creates a new incident near (e,n) within [minDist,maxDist] metres on the road network.</summary>
        public Incident Generate(double e, double n, float nowSeconds, float hourOfDay, double minDist = 800, double maxDist = 3500)
        {
            var type = PickType(hourOfDay, out int urgency);
            for (int attempt = 0; attempt < 30; attempt++)
            {
                double ang = _r.NextDouble() * Math.PI * 2, rad = minDist + _r.NextDouble() * (maxDist - minDist);
                int link = _net.Nearest(e + Math.Cos(ang) * rad, n + Math.Sin(ang) * rad, 150, out float s);
                if (link < 0) continue;
                var l = _net.Links[link];
                if (l.Tunnel || l.Class == Roads.RoadClass.Service && type != IncidentType.Burglary) continue;
                if ((type == IncidentType.TrafficAccident || type == IncidentType.TrafficAccidentInjured || type == IncidentType.RoadHazard || type == IncidentType.WantedVehicle) && l.Class > Roads.RoadClass.Tertiary && l.Class != Roads.RoadClass.Residential) continue;
                l.PointAt(s, out double ie, out double inn, out _, out _);
                var inc = new Incident { Id = _next++, Type = type, E = ie, N = inn, Urgency = urgency, CreatedAt = nowSeconds };
                var m = _admin?.FindMunicipality(ie, inn); var c = _admin?.FindCanton(ie, inn);
                inc.Municipality = m?.Name ?? ""; inc.Canton = c != null ? AdminBoundaries.CantonAbbr[c.Id] : "";
                inc.Address = (string.IsNullOrEmpty(l.Name) ? (string.IsNullOrEmpty(l.Ref) ? "Strasse" : l.Ref) : l.Name) + (type == IncidentType.Burglary || type == IncidentType.NoiseComplaint || type == IncidentType.Dispute ? " " + (1 + _r.Next(60)) : "");
                BuildText(inc, l); BuildScenario(inc, l);
                Incidents.Add(inc);
                return inc;
            }
            return null;
        }

        IncidentType PickType(float hour, out int urgency)
        {
            double tot = 0; var w = new double[Table.Length];
            for (int i = 0; i < Table.Length; i++)
            {
                double x = Table[i].w;
                bool night = hour < 5 || hour >= 22;
                if (Table[i].t == IncidentType.NoiseComplaint && night) x *= 2.5;
                if (Table[i].t == IncidentType.DrunkPerson && night) x *= 4;
                if (Table[i].t == IncidentType.Burglary && night) x *= 2;
                if (Table[i].t == IncidentType.Shoplifting && (night || hour < 9)) x *= 0.1;
                if ((Table[i].t == IncidentType.TrafficAccident || Table[i].t == IncidentType.TrafficAccidentInjured) && ((hour >= 7 && hour <= 9) || (hour >= 16 && hour <= 19))) x *= 1.6;
                w[i] = x; tot += x;
            }
            double r = _r.NextDouble() * tot;
            for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r <= 0) { urgency = Table[i].urgency; return Table[i].t; } }
            urgency = 2; return IncidentType.TrafficAccident;
        }

        void BuildText(Incident inc, TLink l)
        {
            string where = $"{inc.Address}, {inc.Municipality}";
            string cs = CallSign;
            switch (inc.Type)
            {
                case IncidentType.TrafficAccident: inc.RadioText = $"Zentrale an {cs}. Verkehrsunfall, {where}. Zwei Fahrzeuge beteiligt, keine Verletzten gemeldet. Bitte übernehmen."; inc.CallerText = "Es hat gerade gekracht, zwei Autos, bitte kommen Sie!"; break;
                case IncidentType.TrafficAccidentInjured: inc.RadioText = $"Zentrale an {cs}. Verkehrsunfall mit mindestens einer verletzten Person, {where}. Ambulanz ist avisiert. Dringend, bitte übernehmen."; inc.CallerText = "Da liegt jemand neben dem Auto und bewegt sich kaum!"; break;
                case IncidentType.NoiseComplaint: inc.RadioText = $"Zentrale an {cs}. Ruhestörung, {where}. Anrufer meldet laute Musik seit mehreren Stunden. Bitte übernehmen."; inc.CallerText = "Nebenan feiern sie seit Stunden, ich kann nicht schlafen."; break;
                case IncidentType.Burglary: inc.RadioText = $"Zentrale an {cs}. Meldung Einbruch, {where}. Anrufer bemerkt aufgebrochene Tür, Täter möglicherweise noch vor Ort. Vorsicht, bitte übernehmen."; inc.CallerText = "Die Haustür steht offen und das Schloss ist aufgebrochen."; break;
                case IncidentType.Shoplifting: inc.RadioText = $"Zentrale an {cs}. Ladendiebstahl, Geschäft an der {where}. Filialleiter hält eine Person fest. Bitte übernehmen."; inc.CallerText = "Wir haben einen Ladendieb erwischt, er wartet im Büro."; break;
                case IncidentType.Dispute: inc.RadioText = $"Zentrale an {cs}. Streitigkeit, {where}. Zwei Personen lautstark, Handgreiflichkeiten nicht ausgeschlossen. Bitte übernehmen."; inc.CallerText = "Zwei Männer schreien sich an, es wird immer lauter."; break;
                case IncidentType.MissingPerson: inc.RadioText = $"Zentrale an {cs}. Vermisstmeldung, zuletzt gesehen im Raum {inc.Municipality}. Person, 8 Jahre, helle Jacke. Bitte Umgebung absuchen und bei {where} Meldende befragen."; inc.CallerText = "Mein Sohn ist seit einer Stunde weg, er wollte nur zum Spielplatz."; break;
                case IncidentType.AssistMotorist: inc.RadioText = $"Zentrale an {cs}. Hilfeleistung, {where}. Fahrzeug mit Panne auf der Fahrbahn, Warnblinker eingeschaltet. Bitte absichern."; inc.CallerText = "Mein Auto ist stehen geblieben, ich stehe mitten auf der Strasse."; break;
                case IncidentType.WantedVehicle: inc.RadioText = $"Zentrale an alle Patrouillen. Fahndung nach einem {Color()} Personenwagen, Kontrollschild {Plates.Random(_r)}, zuletzt gesehen {where}. Fahrer möglicherweise bewaffnet, nicht annähern, Verstärkung anfordern."; inc.CallerText = ""; break;
                case IncidentType.RoadHazard: inc.RadioText = $"Zentrale an {cs}. Hindernis auf der Fahrbahn, {where}. Grosse Ladung auf der Strasse, Verkehr behindert. Bitte absichern."; inc.CallerText = "Da liegen Paletten mitten auf der Strasse."; break;
                case IncidentType.SuspiciousPerson: inc.RadioText = $"Zentrale an {cs}. Verdächtige Person, {where}. Zeuge meldet, jemand schaue in parkierte Fahrzeuge. Bitte überprüfen."; inc.CallerText = "Da schleicht einer um die Autos und schaut hinein."; break;
                default: inc.RadioText = $"Zentrale an {cs}. Alkoholisierte Person, {where}. Person liegt teilweise auf dem Trottoir. Bitte überprüfen."; inc.CallerText = "Da liegt ein Mann auf dem Gehsteig, er riecht nach Alkohol."; break;
            }
        }

        string Color() => new[] { "dunkelblauen", "weissen", "schwarzen", "silbernen", "roten" }[_r.Next(5)];

        void BuildScenario(Incident inc, TLink l)
        {
            var sc = inc.Scenario; int seed = _r.Next();
            string abbr = string.IsNullOrEmpty(inc.Canton) ? "BE" : inc.Canton;
            Citizen P(int k, int body = 1) => CitizenFactory.Create(seed + k, abbr, inc.Municipality, Plates.Random(_r), body, _r.Next(12));
            switch (inc.Type)
            {
                case IncidentType.TrafficAccident:
                case IncidentType.TrafficAccidentInjured:
                {
                    var a = new ScenarioRole { Role = "Fahrzeuglenker A", Person = P(1, _r.Next(3)) }; var b = new ScenarioRole { Role = "Fahrzeuglenker B", Person = P(2, _r.Next(3)) };
                    bool aAtFault = _r.NextDouble() < 0.5; var kind = new[] { ViolationType.FailureToYield, ViolationType.Tailgating, ViolationType.RedLight, ViolationType.DangerousDriving }[_r.Next(4)];
                    (aAtFault ? a : b).Guilty = true; sc.AtFaultRole = aAtFault ? 0 : 1; sc.AtFaultViolation = kind;
                    sc.Roles.Add(a); sc.Roles.Add(b);
                    if (inc.Type == IncidentType.TrafficAccidentInjured) { sc.Roles[_r.Next(2)].Injured = true; sc.NeedsAmbulance = true; }
                    sc.NeedsTow = true;
                    sc.Truth = $"Fahrzeuglenker {(aAtFault ? "A" : "B")} verursachte den Unfall ({kind}). Sachschaden an beiden Fahrzeugen." + (sc.NeedsAmbulance ? " Eine Person ist verletzt." : "");
                    sc.Objectives.AddRange(new[] { "Unfallstelle absichern (Blaulicht/Warndreieck)", "Verletzte betreuen, Ambulanz anfordern falls nötig", "Personalien und Ausweise beider Beteiligter kontrollieren", "Beteiligte und Zeugen befragen (Atemalkoholprobe)", "Verursacher korrekt feststellen", "Unfallrapport korrekt erfassen" });
                    break;
                }
                case IncidentType.NoiseComplaint:
                    sc.Roles.Add(new ScenarioRole { Role = "Gastgeber", Person = P(1, 1), Behaviour = (Temperament)_r.Next(7), Guilty = true }); sc.Roles.Add(new ScenarioRole { Role = "Anrufer (Nachbar)", Person = P(2, 1) });
                    sc.Truth = "Private Feier mit Lärm nach 22 Uhr; Gastgeber ist einsichtig oder uneinsichtig je nach Temperament."; sc.Objectives.AddRange(new[] { "Beide Seiten anhören", "Verhältnismässig deeskalieren", "Verwarnung oder Verzeigung begründen" }); break;
                case IncidentType.Burglary:
                    sc.Roles.Add(new ScenarioRole { Role = "Geschädigter", Person = P(1, 1) }); sc.Roles.Add(new ScenarioRole { Role = "Mutmasslicher Täter", Person = P(2, 1), Guilty = true, Behaviour = Temperament.Nervous });
                    sc.NeedsCriminalPolice = true; sc.Truth = "Einbruch; der Täter hält sich in der Nähe auf und trägt Einbruchwerkzeug."; sc.Objectives.AddRange(new[] { "Tatort sichern", "Geschädigten befragen", "Umgebung nach Tatverdächtigem absuchen", "Verdächtigen nur bei Tatverdacht anhalten/durchsuchen", "Kriminalpolizei beiziehen" }); break;
                case IncidentType.Shoplifting:
                    sc.Roles.Add(new ScenarioRole { Role = "Filialleiter", Person = P(1, 1) }); sc.Roles.Add(new ScenarioRole { Role = "Verdächtige Person", Person = P(2, 1), Guilty = _r.NextDouble() < 0.8, Behaviour = (Temperament)_r.Next(7) });
                    sc.Truth = "Eine Person hat Waren im Wert von etwa " + (20 + _r.Next(150)) + " CHF eingesteckt (Art. 139/172ter StGB, geringfügig)."; sc.Objectives.AddRange(new[] { "Beide befragen", "Personalien feststellen", "Rapport erstellen", "Verhältnismässig handeln" }); break;
                case IncidentType.Dispute:
                    sc.Roles.Add(new ScenarioRole { Role = "Beteiligter 1", Person = P(1, 1), Behaviour = Temperament.Aggressive, Guilty = _r.NextDouble() < 0.5 }); sc.Roles.Add(new ScenarioRole { Role = "Beteiligter 2", Person = P(2, 1), Behaviour = (Temperament)_r.Next(7) });
                    sc.Truth = "Verbaler Streit um eine Parklücke; einer ist angetrunken."; sc.Objectives.AddRange(new[] { "Trennen und beruhigen", "Beide einzeln befragen", "Wegweisung oder Verzeigung nur falls begründet" }); break;
                case IncidentType.MissingPerson:
                    sc.Roles.Add(new ScenarioRole { Role = "Elternteil", Person = P(1, 1), Behaviour = Temperament.Anxious }); sc.Truth = "Das Kind versteckt sich am nahen Spielplatz / Bach."; sc.Objectives.AddRange(new[] { "Beschreibung aufnehmen", "Umgebung absuchen", "Verstärkung anfordern", "Kind wohlbehalten finden" }); break;
                case IncidentType.AssistMotorist:
                    sc.Roles.Add(new ScenarioRole { Role = "Pannenfahrer", Person = P(1, _r.Next(3)), Behaviour = Temperament.Anxious }); sc.NeedsTow = true; sc.Truth = "Panne; Pannenfahrer steht auf der Fahrbahn."; sc.Objectives.AddRange(new[] { "Stelle absichern", "Pannenhilfe/Abschleppdienst organisieren", "Sicherheit des Fahrers gewährleisten" }); break;
                case IncidentType.WantedVehicle:
                    sc.Roles.Add(new ScenarioRole { Role = "Gesuchter Fahrer", Person = P(1, _r.Next(3)), Guilty = true, Behaviour = Temperament.Aggressive }); sc.NeedsCriminalPolice = true; sc.Truth = "Fahrzeug im Zusammenhang mit einer Straftat ausgeschrieben."; sc.Objectives.AddRange(new[] { "Fahrzeug sichten", "Verstärkung anfordern", "Anhalten nur mit geeigneten Mitteln", "Verhältnismässig festnehmen, Belehrung (Art. 219/158 StPO)" }); break;
                case IncidentType.RoadHazard:
                    sc.Truth = "Verlorene Ladung auf der Fahrbahn."; sc.NeedsTow = false; sc.Objectives.AddRange(new[] { "Gefahrenstelle absichern", "Verkehr umleiten", "Strassenunterhalt informieren" }); break;
                case IncidentType.SuspiciousPerson:
                    sc.Roles.Add(new ScenarioRole { Role = "Person", Person = P(1, 1), Guilty = _r.NextDouble() < 0.35, Behaviour = (Temperament)_r.Next(7) }); sc.Truth = "Person sucht ihr Auto / oder prüft Türen fremder Fahrzeuge."; sc.Objectives.AddRange(new[] { "Ohne Vorurteil ansprechen", "Anhaltung nur bei konkretem Anhaltspunkt (Art. 215 StPO)", "Sachlich dokumentieren" }); break;
                default:
                    sc.Roles.Add(new ScenarioRole { Role = "Alkoholisierte Person", Person = P(1, 1), Behaviour = Temperament.Talkative }); sc.Truth = "Person ist stark alkoholisiert, aber nicht gewalttätig."; sc.Objectives.AddRange(new[] { "Gesundheitszustand prüfen", "Sanität bei Bedarf", "Hilfsbereit und respektvoll" }); break;
            }
        }
    }

}
