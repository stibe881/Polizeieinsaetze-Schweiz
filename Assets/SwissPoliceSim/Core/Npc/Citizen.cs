using System;
using System.Collections.Generic;

namespace SwissPoliceSim.Core.Npc
{
    public enum Gender { Female, Male }
    public enum LicenseStatus { Valid, Expired, Suspended, Withdrawn, None }
    public enum Temperament { Calm, Nervous, Talkative, Arrogant, Aggressive, Friendly, Anxious }

    public sealed class VehicleRecord
    {
        public string Plate = "", Make = "", Model = "", Colour = "", Vin = "", HolderName = "";
        public int Year; public string LastInspection = ""; public bool InspectionOverdue, Insured = true, Stolen, Defect; public string DefectText = "";
        public int BodyKind;
        public override string ToString() => $"{Make} {Model} ({Colour}, {Year})";
    }

    public sealed class CriminalEntry { public string Date = "", Offence = "", Outcome = ""; }

    /// <summary>One consistent person: used for dialogue, the police computer, documents and consequences.</summary>
    public sealed class Citizen
    {
        public int Seed; public string FirstName = "", LastName = "", Birthdate = "", Address = "", Municipality = "", Canton = "", Nationality = "Schweiz", Occupation = "", Phone = "";
        public Gender Gender; public int Age; public string Language = "de";
        public LicenseStatus License = LicenseStatus.Valid; public string LicenseCategory = "B", LicenseIssued = "", LicenseExpires = "";
        public bool Wanted; public string WantedReason = "";
        public readonly List<CriminalEntry> Record = new List<CriminalEntry>();
        public readonly List<string> AdminMeasures = new List<string>();      // frühere Führerausweisentzüge / Verwarnungen
        public VehicleRecord Vehicle = new VehicleRecord();
        public Temperament Temperament; public float Honesty = 0.7f, Anxiety, Anger, Trust = 0.5f;
        public float BloodAlcoholPermille, BreathMgPerL; public bool UnderDrugs;
        public bool CarriesIllegalItem; public string IllegalItem = "";
        public bool HasValidIdOnPerson = true;
        public string FullName => FirstName + " " + LastName;
        public string FormalAddress(string salutationLang) => Gender == Gender.Female ? (salutationLang == "fr" ? "Madame " : salutationLang == "it" ? "Signora " : "Frau ") + LastName : (salutationLang == "fr" ? "Monsieur " : salutationLang == "it" ? "Signor " : "Herr ") + LastName;
    }

    public static class CitizenFactory
    {
        static readonly string[] Streets = { "Bahnhofstrasse", "Hauptstrasse", "Dorfstrasse", "Schulstrasse", "Kirchgasse", "Industriestrasse", "Bergstrasse", "Gartenweg", "Lindenweg", "Rosenweg" };
        static readonly (string make, string model, int body)[] Cars =
        {
            ("Škoda","Octavia Combi",0),("VW","Passat Variant",0),("Volvo","V60",0),("Audi","A4 Avant",0),("BMW","320d Touring",0),("Mercedes-Benz","C 220 T",0),
            ("VW","Golf",1),("Ford","Focus",1),("Renault","Clio",1),("Seat","Leon",1),("Opel","Corsa",1),("Toyota","Yaris",1),("Hyundai","i20",1),("Peugeot","208",1),
            ("Audi","A4 Limousine",2),("BMW","330i",2),("Mercedes-Benz","C 200",2),("Tesla","Model 3",2),("Škoda","Superb",2),
            ("VW","Tiguan",3),("Toyota","RAV4",3),("Škoda","Kodiaq",3),("Audi","Q5",3),("Volvo","XC60",3),("Subaru","Forester",3),
            ("Mercedes-Benz","Vito",4),("VW","Transporter",4),("Ford","Transit",4)
        };
        static readonly string[] ColourNames = { "weiss", "schwarz", "silber", "grau", "blau", "rot", "grün", "braun", "beige", "orange", "gelb", "bordeaux" };

        /// <summary>Canton-language lookup used for names (DE default; FR: VD GE NE JU, parts of FR/VS/BE; IT: TI; RM: parts of GR).</summary>
        public static string LanguageForCanton(string abbr)
        {
            switch (abbr) { case "VD": case "GE": case "NE": case "JU": return "fr"; case "TI": return "it"; case "FR": case "VS": return "de"; default: return "de"; }
        }

        public static Citizen Create(int seed, string cantonAbbr, string municipality, string plate, int bodyKind, int colourIndex, IList<string> nearbyStreets = null, string forcedLanguage = null)
        {
            var r = new Random(seed);
            var c = new Citizen { Seed = seed, Canton = cantonAbbr, Municipality = municipality };
            string lang = forcedLanguage ?? LanguageForCanton(cantonAbbr);
            if (cantonAbbr == "GR" && r.NextDouble() < 0.12) lang = "rm";
            if ((cantonAbbr == "FR" || cantonAbbr == "VS") && r.NextDouble() < 0.5) lang = "fr";
            c.Language = lang; c.Gender = r.NextDouble() < 0.5 ? Gender.Female : Gender.Male;
            bool foreign = r.NextDouble() < 0.14;
            string[] first, sur;
            switch (lang) { case "fr": first = c.Gender == Gender.Female ? NameData.FrFemale : NameData.FrMale; sur = NameData.FrSur; break; case "it": first = c.Gender == Gender.Female ? NameData.ItFemale : NameData.ItMale; sur = NameData.ItSur; break; case "rm": first = c.Gender == Gender.Female ? NameData.RmFemale : NameData.RmMale; sur = NameData.RmSur; break; default: first = c.Gender == Gender.Female ? NameData.DeFemale : NameData.DeMale; sur = NameData.DeSur; break; }
            c.FirstName = foreign ? NameData.ForeignFirst[r.Next(NameData.ForeignFirst.Length)] : first[r.Next(first.Length)];
            c.LastName = foreign ? NameData.ForeignSur[r.Next(NameData.ForeignSur.Length)] : sur[r.Next(sur.Length)];
            c.Nationality = foreign ? NameData.Nationalities[6 + r.Next(NameData.Nationalities.Length - 6)] : "Schweiz";
            // age distribution of drivers
            double ag = r.NextDouble(); c.Age = ag < 0.08 ? 18 + r.Next(7) : ag < 0.55 ? 25 + r.Next(25) : ag < 0.88 ? 50 + r.Next(18) : 68 + r.Next(20);
            int year = 2026 - c.Age; c.Birthdate = $"{1 + r.Next(28):00}.{1 + r.Next(12):00}.{year}";
            string street = nearbyStreets != null && nearbyStreets.Count > 0 ? nearbyStreets[r.Next(nearbyStreets.Count)] : Streets[r.Next(Streets.Length)];
            c.Address = $"{street} {1 + r.Next(60)}, {municipality}";
            c.Phone = $"07{r.Next(5, 10)} {r.Next(100, 999)} {r.Next(10, 99)} {r.Next(10, 99)}";
            c.Occupation = NameData.Occupations[r.Next(NameData.Occupations.Length)];
            c.Temperament = (Temperament)r.Next(7);
            c.Honesty = 0.25f + (float)r.NextDouble() * 0.7f;
            c.LicenseIssued = $"{1 + r.Next(28):00}.{1 + r.Next(12):00}.{Math.Min(2025, year + 18 + r.Next(3))}";
            c.LicenseExpires = "—";      // Swiss licences are valid for life (with medical checks from 75)
            // vehicle
            var car = Cars[0]; var matching = new List<(string, string, int)>(); foreach (var x in Cars) if (x.body == bodyKind) matching.Add(x);
            if (matching.Count > 0) car = matching[r.Next(matching.Count)];
            c.Vehicle = new VehicleRecord
            {
                Plate = plate, Make = car.make, Model = car.model, BodyKind = bodyKind, Colour = ColourNames[colourIndex % ColourNames.Length], Year = 2026 - r.Next(1, 14),
                Vin = "WVW" + r.Next(10000000, 99999999) + r.Next(1000, 9999), HolderName = c.FullName,
                LastInspection = $"{1 + r.Next(12):00}.{2026 - r.Next(0, 4)}"
            };
            return c;
        }

        /// <summary>Applies the hidden truth of a simulated driver (the offences / flags created by the traffic simulation) to the citizen's record.</summary>
        public static void ApplyDriverTruth(Citizen c, bool impaired, float bloodAlcoholPermille, bool noLicense, bool suspended, bool uninsured, bool vehicleDefect, bool wanted, int seed)
        {
            var r = new Random(seed ^ 0x5bd1e995);
            c.BloodAlcoholPermille = bloodAlcoholPermille; c.BreathMgPerL = bloodAlcoholPermille * 0.5f;       // BAK 0.5 ‰ ≙ 0.25 mg/l (VAGV)
            if (noLicense) { c.License = LicenseStatus.None; c.HasValidIdOnPerson = r.NextDouble() < 0.5; }
            else if (suspended) { c.License = LicenseStatus.Suspended; c.AdminMeasures.Add($"Führerausweisentzug {2025 - r.Next(0, 2)} ({1 + r.Next(12)} Monate, Art. 16b SVG)"); }
            c.Vehicle.Insured = !uninsured;
            if (vehicleDefect) { c.Vehicle.Defect = true; c.Vehicle.DefectText = new[] { "defektes Rücklicht links", "Profiltiefe der Reifen unter 1.6 mm", "Bremslicht ausgefallen", "Frontscheibe mit Sprung im Sichtfeld" }[r.Next(4)]; }
            c.Vehicle.InspectionOverdue = r.NextDouble() < (vehicleDefect ? 0.5 : 0.05);
            if (wanted) { c.Wanted = true; c.WantedReason = new[] { "zur Verhaftung ausgeschrieben (Staatsanwaltschaft)", "Fahndung: Verdacht auf Einbruchdiebstahl", "ausgeschrieben: nicht bezahlte Geldstrafe (Ersatzfreiheitsstrafe)" }[r.Next(3)]; c.CarriesIllegalItem = r.NextDouble() < 0.4; c.IllegalItem = "Einbruchwerkzeug"; }
            else if (r.NextDouble() < 0.05) { c.CarriesIllegalItem = true; c.IllegalItem = new[] { "Cannabis (geringe Menge)", "Messer (Spring­messer)", "Pfefferspray ohne Bewilligung" }[r.Next(3)]; }
            int priors = r.NextDouble() < 0.12 ? 1 + r.Next(3) : 0;
            for (int i = 0; i < priors; i++) c.Record.Add(new CriminalEntry { Date = $"{2015 + r.Next(10)}", Offence = new[] { "Verkehrsregelverletzung (Art. 90 SVG)", "Fahren in angetrunkenem Zustand (Art. 91 SVG)", "Sachbeschädigung", "Hausfriedensbruch", "Diebstahl" }[r.Next(5)], Outcome = new[] { "Busse", "Geldstrafe bedingt", "Strafbefehl" }[r.Next(3)] });
            c.Anxiety = c.Temperament == Temperament.Nervous || c.Temperament == Temperament.Anxious ? 0.6f : 0.25f;
            if (impaired || wanted || noLicense || suspended) c.Anxiety = Math.Min(1f, c.Anxiety + 0.3f);
            c.Anger = c.Temperament == Temperament.Aggressive ? 0.45f : c.Temperament == Temperament.Arrogant ? 0.3f : 0.1f;
        }
    }
}
