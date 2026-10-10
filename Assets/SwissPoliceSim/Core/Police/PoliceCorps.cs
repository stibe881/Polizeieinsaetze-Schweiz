using System;
using System.Collections.Generic;
using SwissPoliceSim.Core.Geo;
using SwissPoliceSim.Core.Legal;

namespace SwissPoliceSim.Core.Police
{
    public enum CorpsLevel { Cantonal, Municipal }

    public sealed class PoliceCorps
    {
        public string Id, Name; public CorpsLevel Level; public int CantonId; public int MunicipalityBfs;     // municipal corps: only this municipality
        public string Language = "de"; public string Concordat = "";
        public string[] Ranks;
        public override string ToString() => Name;
    }

    public enum JurisdictionKind { OwnTerritory, OtherMunicipality, OtherCanton, Unknown }

    public sealed class JurisdictionStatus
    {
        public JurisdictionKind Kind; public int CantonId; public string CantonName = "", MunicipalityName = ""; public int MunicipalityBfs;
        public bool InOwnTerritory => Kind == JurisdictionKind.OwnTerritory;
        public bool CooperationAgreement;            // same police concordat (game rule)
        public double DistanceToBorderM;
        public string Describe(PoliceCorps corps)
        {
            switch (Kind)
            {
                case JurisdictionKind.OwnTerritory: return $"{MunicipalityName} ({CantonName}) – eigenes Zuständigkeitsgebiet der {corps.Name}.";
                case JurisdictionKind.OtherMunicipality: return $"{MunicipalityName}: ausserhalb der Gemeinde der {corps.Name} (Kanton {CantonName}); Zuständigkeit der Kantonspolizei.";
                case JurisdictionKind.OtherCanton: return $"{MunicipalityName}, Kanton {CantonName}: ausserhalb des eigenen Kantons." + (CooperationAgreement ? " Polizeikonkordat: Zusammenarbeit möglich (Spielregel)." : "");
                default: return "Unbekanntes Gebiet.";
            }
        }
    }

    /// <summary>All corps of the game (cantonal police of the 26 cantons + the main municipal police forces) and jurisdiction decisions based on real boundaries.</summary>
    public static class PoliceCatalog
    {
        public static readonly string[] RanksDe = { "Aspirant/in", "Polizist/in", "Gefreite/r", "Korporal/in", "Wachtmeister/in", "Feldweibel/in", "Leutnant", "Oberleutnant", "Hauptmann", "Major" };
        public static readonly string[] RanksFr = { "Aspirant(e)", "Agent(e)", "Appointé(e)", "Appointé(e) chef", "Sergent(e)", "Sergent(e)-major", "Lieutenant", "Premier-lieutenant", "Capitaine", "Major" };
        public static readonly string[] RanksIt = { "Aspirante", "Agente", "Appuntato", "Appuntato capo", "Sergente", "Sergente maggiore", "Tenente", "Primo tenente", "Capitano", "Maggiore" };

        public static readonly List<PoliceCorps> All = Build();

        static string Concordat(int canton)
        {
            switch (canton)
            {
                case 19: case 13: case 12: case 11: return "Nordwestschweiz";                     // AG BL BS SO
                case 17: case 20: case 15: case 16: case 8: case 14: case 18: return "Ostschweiz"; // SG TG AR AI GL SH GR
                case 3: case 7: case 6: case 5: case 4: case 9: return "Zentralschweiz";           // LU NW OW SZ UR ZG
                case 10: case 25: case 26: case 24: case 22: case 23: return "Westschweiz";        // FR GE JU NE VD VS
                default: return "";                                                                // ZH, BE, TI: eigenständig
            }
        }

        static List<PoliceCorps> Build()
        {
            var l = new List<PoliceCorps>();
            string[] names =
            {
                "", "Kantonspolizei Zürich", "Kantonspolizei Bern", "Luzerner Polizei", "Kantonspolizei Uri", "Kantonspolizei Schwyz", "Kantonspolizei Obwalden", "Kantonspolizei Nidwalden",
                "Kantonspolizei Glarus", "Zuger Polizei", "Kantonspolizei Freiburg", "Kantonspolizei Solothurn", "Kantonspolizei Basel-Stadt", "Polizei Basel-Landschaft", "Schaffhauser Polizei",
                "Kantonspolizei Appenzell Ausserrhoden", "Kantonspolizei Appenzell Innerrhoden", "Kantonspolizei St. Gallen", "Kantonspolizei Graubünden", "Kantonspolizei Aargau", "Kantonspolizei Thurgau",
                "Polizia cantonale ticinese", "Police cantonale vaudoise", "Kantonspolizei Wallis", "Police neuchâteloise", "Police cantonale genevoise", "Police cantonale jurassienne"
            };
            for (int k = 1; k <= 26; k++)
            {
                string lang = k == 21 ? "it" : (k == 22 || k == 24 || k == 25 || k == 26) ? "fr" : "de";
                l.Add(new PoliceCorps { Id = "KAPO_" + AdminBoundaries.CantonAbbr[k], Name = names[k], Level = CorpsLevel.Cantonal, CantonId = k, Language = lang, Concordat = Concordat(k), Ranks = lang == "fr" ? RanksFr : lang == "it" ? RanksIt : RanksDe });
            }
            void City(string id, string name, int canton, int bfs, string lang) => l.Add(new PoliceCorps { Id = id, Name = name, Level = CorpsLevel.Municipal, CantonId = canton, MunicipalityBfs = bfs, Language = lang, Concordat = Concordat(canton), Ranks = lang == "fr" ? RanksFr : lang == "it" ? RanksIt : RanksDe });
            City("STAPO_ZH", "Stadtpolizei Zürich", 1, 261, "de");
            City("STAPO_WIN", "Stadtpolizei Winterthur", 1, 230, "de");
            City("STAPO_SG", "Stadtpolizei St. Gallen", 17, 3203, "de");
            City("STAPO_BIEL", "Stadtpolizei Biel/Bienne", 2, 371, "de");
            City("STAPO_CHUR", "Stadtpolizei Chur", 18, 3901, "de");
            City("POL_LAUSANNE", "Police municipale de Lausanne", 22, 5586, "fr");
            City("POL_GENEVE", "Police municipale de la Ville de Genève", 25, 6621, "fr");
            City("POL_LUGANO", "Polizia comunale di Lugano", 21, 5192, "it");
            return l;
        }

        public static PoliceCorps ById(string id) { foreach (var c in All) if (c.Id == id) return c; return null; }
        public static PoliceCorps CantonalOf(int cantonId) { foreach (var c in All) if (c.Level == CorpsLevel.Cantonal && c.CantonId == cantonId) return c; return null; }
    }

    public static class JurisdictionManager
    {
        public static JurisdictionStatus Evaluate(AdminBoundaries admin, PoliceCorps corps, double e, double n)
        {
            var st = new JurisdictionStatus();
            var muni = admin.FindMunicipality(e, n); var canton = admin.FindCanton(e, n);
            if (canton == null) { st.Kind = JurisdictionKind.Unknown; return st; }
            st.CantonId = canton.Id; st.CantonName = canton.Name; st.MunicipalityName = muni?.Name ?? ""; st.MunicipalityBfs = muni?.Id ?? 0;
            if (corps.Level == CorpsLevel.Cantonal)
            {
                st.Kind = canton.Id == corps.CantonId ? JurisdictionKind.OwnTerritory : JurisdictionKind.OtherCanton;
            }
            else
            {
                if (st.MunicipalityBfs == corps.MunicipalityBfs) st.Kind = JurisdictionKind.OwnTerritory;
                else st.Kind = canton.Id == corps.CantonId ? JurisdictionKind.OtherMunicipality : JurisdictionKind.OtherCanton;
            }
            if (st.Kind == JurisdictionKind.OtherCanton)
            {
                var other = PoliceCatalog.CantonalOf(canton.Id);
                st.CooperationAgreement = other != null && corps.Concordat.Length > 0 && other.Concordat == corps.Concordat;
            }
            return st;
        }

        /// <summary>Builds the legal context fields that depend on territory.</summary>
        public static void Apply(LegalContext ctx, JurisdictionStatus st, bool inPursuit, bool requestedByLocalPolice)
        {
            ctx.InOwnTerritory = st.InOwnTerritory; ctx.InPursuit = inPursuit; ctx.RequestedByLocalPolice = requestedByLocalPolice; ctx.CooperationAgreement = st.CooperationAgreement;
        }
    }
}
