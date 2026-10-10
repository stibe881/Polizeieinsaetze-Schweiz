using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Legal
{
    /// <summary>How reliable a rule in the game is. Shown to the player next to every assessment.</summary>
    public enum Verification
    {
        FedlexVerified,          // wording checked against the consolidated federal text (see tests)
        CourtPractice,           // follows Bundesgericht practice, not statute text – simplified in the game
        GameRule,                // abstraction made for gameplay; no claim of legal accuracy
        CantonalNotVerified      // cantonal law (not available in the federal database); structure is plausible, details unchecked
    }

    public sealed class Citation
    {
        public string Law;      // short name, e.g. "SVG"
        public string Sr;       // e.g. "741.01"
        public string Article;  // number only, e.g. "55" or "16a"
        public string Para;     // e.g. "Abs. 1" (informational)
        public string Keyword;  // phrase that must occur in the article text
        public Citation(string law, string sr, string article, string para, string keyword) { Law = law; Sr = sr; Article = article; Para = para; Keyword = keyword; }
        public override string ToString() => $"Art. {Article}{(string.IsNullOrEmpty(Para) ? "" : " " + Para)} {Law}";
    }

    public sealed class Norm
    {
        public string Id, Title, Summary; public Verification Status; public Citation[] Cites;
        public Norm(string id, string title, string summary, Verification status, params Citation[] cites) { Id = id; Title = title; Summary = summary; Status = status; Cites = cites; }
        public string CitationText() { var p = new List<string>(); foreach (var c in Cites) p.Add(c.ToString()); return string.Join("; ", p); }
    }

    /// <summary>All legal norms the game applies. Each cited article is checked against the Fedlex text by unit tests.</summary>
    public static class Norms
    {
        public const string SVG = "SVG", VRV = "VRV", StPO = "StPO", StGB = "StGB", ZAG = "ZAG", BV = "BV", OBV = "OBV", SSV = "SSV", SKV = "SKV", VAGV = "VAGV";
        public static readonly Dictionary<string, Norm> All = new Dictionary<string, Norm>();

        static void Add(Norm n) { All[n.Id] = n; }

        static Norms()
        {
            // ---------- Verkehrskontrolle / Strassenverkehr
            Add(new Norm("FOLLOW_POLICE", "Weisungen der Polizei", "Signale, Markierungen und Weisungen der Polizei sind zu befolgen (Anhaltezeichen).", Verification.FedlexVerified,
                new Citation(SVG, "741.01", "27", "Abs. 1", "Weisungen der Polizei sind zu befolgen")));
            Add(new Norm("DOCUMENTS", "Ausweise mitführen und vorweisen", "Führer- und Fahrzeugausweis sind mitzuführen und den Kontrollorganen auf Verlangen vorzuweisen.", Verification.FedlexVerified,
                new Citation(SVG, "741.01", "10", "Abs. 4", "stets mitzuführen und den Kontrollorganen auf Verlangen vorzuweisen")));
            Add(new Norm("TRAFFIC_CONTROL_COMPETENCE", "Zuständigkeit für Verkehrskontrollen", "Die Kontrolle des Verkehrs obliegt der nach kantonalem Recht zuständigen Polizei; sie wirkt helfend und verkehrserziehend, verhindert Widerhandlungen, verzeigt Fehlbare und erhebt Ordnungsbussen.", Verification.FedlexVerified,
                new Citation(SKV, "741.013", "3", "Abs. 1 und 2", "nach kantonalem Recht zuständigen Polizei")));
            Add(new Norm("BREATH_TEST", "Atemalkoholprobe", "Fahrzeugführer sowie an Unfällen Beteiligte können einer Atemalkoholprobe unterzogen werden (auch ohne besonderen Anlass).", Verification.FedlexVerified,
                new Citation(SVG, "741.01", "55", "Abs. 1", "können einer Atemalkoholprobe unterzogen werden")));
            Add(new Norm("BLOOD_TEST", "Blutprobe", "Eine Blutprobe muss angeordnet werden bei Anzeichen anderer Fahrunfähigkeit, bei Widersetzen gegen die Atemalkoholprobe oder wenn die Person die Blutalkoholprobe verlangt.", Verification.FedlexVerified,
                new Citation(SVG, "741.01", "55", "Abs. 3", "Blutprobe muss angeordnet werden")));
            Add(new Norm("ALCOHOL_LIMITS", "Alkoholgrenzwerte", "Angetrunkenheit ab 0,5 ‰ BAK bzw. 0,25 mg/l Atemluft; qualifiziert ab 0,8 ‰ bzw. 0,4 mg/l.", Verification.FedlexVerified,
                new Citation("VAGV", "741.13", "1", "", "0,25 mg Alkohol oder mehr pro Liter Atemluft"), new Citation("VAGV", "741.13", "2", "", "0,4 mg Alkohol oder mehr pro Liter Atemluft")));
            Add(new Norm("DRUNK_DRIVING", "Fahren in angetrunkenem Zustand", "Angetrunken: Busse; qualifiziert angetrunken oder anders fahrunfähig: Freiheitsstrafe bis 3 Jahre oder Geldstrafe.", Verification.FedlexVerified,
                new Citation(SVG, "741.01", "91", "Abs. 1 und 2", "in angetrunkenem Zustand mit qualifizierter Atemalkohol- oder Blutalkoholkonzentration")));
            Add(new Norm("LICENSE_SEIZURE", "Abnahme des Führerausweises", "Kann der Fahrzeugführer nicht sicher fahren, verhindert die Polizei die Weiterfahrt und nimmt den Führerausweis ab; bei grober Verletzung wichtiger Verkehrsregeln kann sie ihn auf der Stelle abnehmen.", Verification.FedlexVerified,
                new Citation(SVG, "741.01", "54", "Abs. 3 und 4", "nimmt den Führerausweis ab")));
            Add(new Norm("VEHICLE_UNSAFE", "Weiterfahrt verhindern bei unsicherem Fahrzeug", "Fahrzeuge, die nicht zugelassen sind oder den Verkehr gefährden, werden an der Weiterfahrt gehindert; Fahrzeugausweis kann abgenommen werden.", Verification.FedlexVerified,
                new Citation(SVG, "741.01", "54", "Abs. 1", "verhindert sie die Weiterfahrt")));
            Add(new Norm("NO_LICENSE", "Fahren ohne Berechtigung", "Wer ohne den erforderlichen Führerausweis fährt oder trotz Entzug: Freiheitsstrafe bis 3 Jahre oder Geldstrafe.", Verification.FedlexVerified,
                new Citation(SVG, "741.01", "95", "Abs. 1", "ohne den erforderlichen Führerausweis ein Motorfahrzeug führt")));
            Add(new Norm("NO_INSURANCE", "Fahrzeugausweis / Haftpflichtversicherung", "Fahren ohne Fahrzeugausweis oder Kontrollschilder: Busse; ohne bestehende Haftpflichtversicherung: Freiheitsstrafe bis 3 Jahre oder Geldstrafe.", Verification.FedlexVerified,
                new Citation(SVG, "741.01", "96", "Abs. 1 und 2", "ohne den erforderlichen Fahrzeugausweis")));
            Add(new Norm("SPEED_LIMITS", "Allgemeine Höchstgeschwindigkeiten", "50 km/h in Ortschaften, 80 km/h ausserorts, 100 km/h auf Autostrassen, 120 km/h auf Autobahnen.", Verification.FedlexVerified,
                new Citation(VRV, "741.11", "4a", "Abs. 1", "120 km/h auf Autobahnen")));
            Add(new Norm("SPEEDING_OBV", "Geschwindigkeitsüberschreitung – Ordnungsbusse", "Bis 15 km/h innerorts, 20 km/h ausserorts, 25 km/h auf Autobahnen nach Abzug der Messunsicherheit: Ordnungsbusse nach OBV Ziff. 303.", Verification.FedlexVerified,
                new Citation(OBV, "314.11", "", "Anhang 1 Ziff. 303", "Überschreiten allgemeiner, fahrzeugbedingter oder signalisierter Höchstgeschwindigkeit")));
            Add(new Norm("SPEEDING_CRIMINAL", "Geschwindigkeitsüberschreitung – ordentliches Verfahren", "Darüber: Anzeige; Verkehrsregelverletzung Art. 90 Abs. 1 SVG (Busse), bei grober Verletzung Abs. 2; Raserdelikt Abs. 3/4 ab +40/+50/+60/+80 km/h.", Verification.FedlexVerified,
                new Citation(SVG, "741.01", "90", "Abs. 1–4", "besonders krasse Missachtung der zulässigen Höchstgeschwindigkeit")));
            Add(new Norm("ADMIN_MEASURES", "Administrativmassnahmen", "Leichte (Verwarnung / Entzug ≥ 1 Mt. bei Rückfall), mittelschwere (Entzug ≥ 1 Mt.), schwere Widerhandlung (Entzug ≥ 3 Mt.); Raser ≥ 2 Jahre.", Verification.FedlexVerified,
                new Citation(SVG, "741.01", "16a", "", "leichte Widerhandlung"), new Citation(SVG, "741.01", "16b", "", "mittelschwere Widerhandlung"), new Citation(SVG, "741.01", "16c", "", "schwere Widerhandlung")));
            Add(new Norm("SPEED_MEASURE_TIERS", "Schweregrad nach Praxis", "Ab welcher Überschreitung eine mittelschwere/schwere Widerhandlung vorliegt, ergibt sich aus der Bundesgerichtspraxis (Faustregel), nicht aus dem Gesetzestext.", Verification.CourtPractice,
                new Citation(SVG, "741.01", "16", "", "Widerhandlung")));
            Add(new Norm("RED_LIGHT", "Nichtbeachten eines Lichtsignals", "Ordnungsbusse 250 CHF (OBV Ziff. 309.1), bei Gefährdung ordentliches Verfahren.", Verification.FedlexVerified,
                new Citation(OBV, "314.11", "", "Anhang 1 Ziff. 309", "Nichtbeachten eines Lichtsignals")));
            Add(new Norm("PHONE", "Telefonieren ohne Freisprecheinrichtung", "Ordnungsbusse 100 CHF (OBV Ziff. 311).", Verification.FedlexVerified,
                new Citation(VRV, "741.11", "3", "Abs. 1", "Aufmerksamkeit der Strasse und dem Verkehr zuwenden"), new Citation(OBV, "314.11", "", "Anhang 1 Ziff. 311", "Verwenden eines Telefons ohne Freisprecheinrichtung")));
            Add(new Norm("SEATBELT", "Sicherheitsgurt", "Ordnungsbusse 60 CHF (OBV Ziff. 312.1).", Verification.FedlexVerified,
                new Citation(VRV, "741.11", "3a", "Abs. 1", "Sicherheitsgurten"), new Citation(OBV, "314.11", "", "Anhang 1 Ziff. 312", "Nichttragen der Sicherheitsgurten")));
            Add(new Norm("PEDESTRIAN_CROSSING", "Vortritt beim Fussgängerstreifen", "Ordnungsbusse 140 CHF (OBV Ziff. 337).", Verification.FedlexVerified,
                new Citation(OBV, "314.11", "", "Anhang 1 Ziff. 337", "Nichtgewähren des Vortritts bei Fussgängerstreifen")));
            Add(new Norm("STOP_SIGN", "Stopp-Signal (Rollstopp)", "Nicht vollständiges Anhalten: Ordnungsbusse 60 CHF (OBV Ziff. 308).", Verification.FedlexVerified,
                new Citation(OBV, "314.11", "", "Anhang 1 Ziff. 308", "Nicht vollständiges Anhalten bei Stopp-Signalen")));
            Add(new Norm("ACCIDENT_DUTIES", "Verhalten bei Unfällen", "Alle Beteiligten müssen sofort anhalten, helfen und bei der Feststellung des Tatbestandes mitwirken; bei Verletzten ist die Polizei zu benachrichtigen.", Verification.FedlexVerified,
                new Citation(SVG, "741.01", "51", "Abs. 1–3", "müssen alle Beteiligten sofort anhalten")));
            Add(new Norm("EMERGENCY_YIELD", "Vortritt für Einsatzfahrzeuge", "Beim Wahrnehmen der besonderen Warnsignale ist Feuerwehr-, Sanitäts- und Polizeifahrzeugen die Strasse sofort freizugeben.", Verification.FedlexVerified,
                new Citation(SVG, "741.01", "27", "Abs. 2", "Polizei- und Zollfahrzeugen ist beim Wahrnehmen der besonderen Warnsignale die Strasse sofort freizugeben")));
            Add(new Norm("EMERGENCY_DRIVE", "Dringliche Dienstfahrt", "Missachtet der Führer eines Polizeifahrzeugs auf dringlichen oder taktisch notwendigen Dienstfahrten Verkehrsregeln, so bleibt er straflos, wenn er alle erforderliche Sorgfalt walten lässt und (bei dringlichen Fahrten) die erforderlichen Warnsignale abgibt.", Verification.FedlexVerified,
                new Citation(SVG, "741.01", "100", "Ziff. 4", "dringlichen oder taktisch notwendigen Dienstfahrten")));

            // ---------- Strafprozess
            Add(new Norm("DETAIN_ANHALTUNG", "Polizeiliche Anhaltung", "Zur Aufklärung einer Straftat darf die Polizei eine Person anhalten, ihre Identität feststellen, sie kurz befragen, Ausweise, mitgeführte Sachen, Behältnisse oder Fahrzeuge öffnen lassen.", Verification.FedlexVerified,
                new Citation(StPO, "312.0", "215", "Abs. 1 und 2", "Die Polizei kann im Interesse der Aufklärung einer Straftat eine Person anhalten")));
            Add(new Norm("PURSUIT_ACROSS_BORDER", "Nacheile", "In dringenden Fällen darf die Polizei eine beschuldigte Person auf das Gebiet einer anderen Gemeinde und eines anderen Kantons verfolgen und dort anhalten; anschliessend Übergabe an die örtlich zuständige Behörde.", Verification.FedlexVerified,
                new Citation(StPO, "312.0", "216", "Abs. 1 und 2", "eines anderen Kantons")));
            Add(new Norm("ARREST", "Vorläufige Festnahme", "Pflicht bei Verbrechen/Vergehen auf frischer Tat oder bei Ausschreibung; Recht bei Verdacht; bei Übertretungen nur unter den Voraussetzungen von Abs. 3.", Verification.FedlexVerified,
                new Citation(StPO, "312.0", "217", "Abs. 1–3", "vorläufig festzunehmen")));
            Add(new Norm("ARREST_PROCEDURE", "Vorgehen nach der Festnahme", "Identität feststellen, Gründe in verständlicher Sprache eröffnen, Rechte erklären (Art. 158), Staatsanwaltschaft informieren; Freilassung oder Zuführung spätestens nach 24 Stunden.", Verification.FedlexVerified,
                new Citation(StPO, "312.0", "219", "Abs. 1–4", "spätestens nach 24 Stunden")));
            Add(new Norm("RIGHTS_NOTICE", "Belehrung vor der ersten Einvernahme", "Aussage- und Mitwirkungsverweigerung, Verteidigung, Übersetzung; ohne Belehrung sind Einvernahmen nicht verwertbar.", Verification.FedlexVerified,
                new Citation(StPO, "312.0", "158", "Abs. 1 und 2", "nicht verwertbar")));
            Add(new Norm("SEARCH_SAFETY", "Sicherheitsdurchsuchung", "Die Polizei kann eine angehaltene oder festgenommene Person durchsuchen, namentlich um die Sicherheit von Personen zu gewährleisten.", Verification.FedlexVerified,
                new Citation(StPO, "312.0", "241", "Abs. 4", "angehaltene oder festgenommene Person durchsuchen")));
            Add(new Norm("SEARCH_SUSPICION", "Durchsuchung ohne Einwilligung", "Personen und Gegenstände dürfen ohne Einwilligung nur durchsucht werden, wenn zu vermuten ist, dass Tatspuren oder zu beschlagnahmende Gegenstände gefunden werden.", Verification.FedlexVerified,
                new Citation(StPO, "312.0", "249", "", "nur durchsucht werden, wenn zu vermuten ist")));
            Add(new Norm("SEARCH_ORDER", "Anordnung von Durchsuchungen", "Durchsuchungen werden schriftlich angeordnet; bei Gefahr im Verzug kann die Polizei ohne Befehl durchsuchen und informiert unverzüglich die Strafbehörde.", Verification.FedlexVerified,
                new Citation(StPO, "312.0", "241", "Abs. 1 und 3", "Ist Gefahr im Verzug")));
            Add(new Norm("SEARCH_INTIMATE", "Intimdurchsuchung", "Durchsuchungen im Intimbereich werden von Personen des gleichen Geschlechts oder von Ärzten durchgeführt.", Verification.FedlexVerified,
                new Citation(StPO, "312.0", "250", "Abs. 2", "Personen des gleichen Geschlechts")));

            // ---------- Zwang
            Add(new Norm("FORCE_DEFINITION", "Polizeilicher Zwang und Massnahmen", "Zwang: körperliche Gewalt, Hilfsmittel, Waffen. Massnahmen: kurzfristiges Festhalten, Wegweisung, Durchsuchung, Beschlagnahme.", Verification.FedlexVerified,
                new Citation(ZAG, "364", "5", "", "körperlicher Gewalt"), new Citation(ZAG, "364", "6", "", "kurzfristige Festhalten")));
            Add(new Norm("FORCE_ANNOUNCEMENT", "Ankündigung polizeilichen Zwangs", "Soweit Umstände und Zweck es zulassen, muss die Anwendung polizeilichen Zwangs angekündigt werden.", Verification.FedlexVerified,
                new Citation(ZAG, "364", "10", "Abs. 1", "angekündigt werden")));
            Add(new Norm("FORCE_PROPORTIONALITY", "Verhältnismässigkeit", "Staatliches Handeln muss im öffentlichen Interesse liegen und verhältnismässig sein; Grundrechtseingriffe bedürfen einer gesetzlichen Grundlage.", Verification.FedlexVerified,
                new Citation(BV, "101", "5", "Abs. 2", "verhältnismässig sein"), new Citation(BV, "101", "36", "Abs. 1 und 3", "Einschränkungen von Grundrechten bedürfen einer gesetzlichen Grundlage")));
            Add(new Norm("WEAPON_LAST_RESORT", "Einsatz von Waffen", "Waffen nur als letztes Mittel; Feuerwaffen nur zur Festnahme/Flucht­verhinderung bei schwerer Straftat; Warnschuss nur wenn Warnruf wirkungslos.", Verification.FedlexVerified,
                new Citation(ZAG, "364", "11", "Abs. 1–3", "Der Einsatz von Waffen darf nur als letztes Mittel erfolgen")));

            // ---------- Daten, Amtsmissbrauch, Widerstand
            Add(new Norm("DATA_PROTECTION", "Schutz vor Datenmissbrauch", "Jede Person hat Anspruch auf Schutz vor Missbrauch ihrer persönlichen Daten; unbefugtes Beschaffen besonders schützenswerter Personendaten ist strafbar.", Verification.FedlexVerified,
                new Citation(BV, "101", "13", "Abs. 2", "Schutz vor Missbrauch ihrer persönlichen Daten"), new Citation(StGB, "311.0", "179novies", "", "unbefugt besonders schützenswerte Personendaten")));
            Add(new Norm("OFFICIAL_ABUSE", "Amtsmissbrauch", "Wer als Beamter seine Amtsgewalt missbraucht, um sich oder einem andern einen unrechtmässigen Vorteil zu verschaffen oder einem andern einen Nachteil zuzufügen: bis 5 Jahre.", Verification.FedlexVerified,
                new Citation(StGB, "311.0", "312", "", "Amtsgewalt missbrauchen")));
            Add(new Norm("RESIST_OFFICER", "Gewalt und Drohung gegen Beamte / Hinderung einer Amtshandlung", "Gewalt oder Drohung gegen Beamte bzw. Hinderung einer Amtshandlung ist strafbar.", Verification.FedlexVerified,
                new Citation(StGB, "311.0", "285", "Ziff. 1", "tätlich angreift"), new Citation(StGB, "311.0", "286", "", "an einer Handlung hindert")));
            Add(new Norm("FALSE_REPORT", "Falsche Anzeige", "Wer wider besseres Wissen anzeigt, es sei eine strafbare Handlung begangen worden, wird bestraft.", Verification.FedlexVerified,
                new Citation(StGB, "311.0", "304", "Ziff. 1", "wider besseres Wissen anzeigt")));

            // ---------- Kantone (nicht in der Bundesdatenbank): Struktur plausibel, Details nicht geprüft
            Add(new Norm("CANTONAL_POLICE_ACTS", "Kantonale Polizeigesetze", "Aufgaben, Befugnisse und Zuständigkeitsgebiet der Kantons-, Stadt- und Gemeindepolizeien regeln die Kantone (z. B. PolG BE, PolG ZH). Befugnisse ausserhalb des eigenen Kantons nur mit Rechtsgrundlage (Nacheile, Amtshilfe, Konkordat).", Verification.CantonalNotVerified));
            Add(new Norm("MUTUAL_ASSISTANCE", "Amtshilfe / Konkordate", "Polizeikorps unterstützen sich auf Anfrage; Einsatz ausserhalb des eigenen Gebiets setzt Ersuchen oder Vereinbarung voraus (Spielregel).", Verification.GameRule));
            Add(new Norm("SPEED_TOLERANCE", "Messunsicherheit", "Von der gemessenen Geschwindigkeit wird die Geräte- und Messunsicherheit abgezogen (Spielregel: 3 km/h bis 100 km/h, 3 % darüber; die amtlichen Werte legt das ASTRA fest).", Verification.GameRule));
        }

        public static Norm Get(string id) => All[id];
    }
}
