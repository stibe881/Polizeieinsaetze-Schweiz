using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SwissPoliceSim.Core.Util;

namespace SwissPoliceSim.Core.Game
{
    /// <summary>Everything that must survive a restart. Serialised as JSON so saves stay small even though the world is huge (the world itself is never saved; only the player's state, ongoing work and the changes to the world).</summary>
    public sealed class GameSave
    {
        public const int Version = 2;
        public string CorpsId = "KAPO_BE"; public int RankIndex; public double Xp;
        public double PlayerE, PlayerN, PlayerH; public float PlayerYaw; public bool InVehicle = true;
        public double VehicleE, VehicleN, VehicleH; public float VehicleYaw, Fuel = 1f, VehicleDamage;
        public double GameSeconds; public float TimeScale = 12f; public string StartIso = "2026-10-12T07:30:00";
        public float Legality = 70, Professionalism = 70, DeEscalation = 70, Reporting = 70, Community = 70;
        public int ComplaintsOpen, ComplaintsTotal, InvestigationsOpen, DisciplinaryCount, FinesIssued, ArrestsMade; public bool Suspended, Dismissed;
        public int Seed = 1; public string Region = "";
        public readonly List<SavedIncident> Incidents = new List<SavedIncident>();
        public readonly List<SavedCase> Cases = new List<SavedCase>();
        public readonly List<string> Consequences = new List<string>();
        public readonly List<string> Flags = new List<string>();

        public sealed class SavedIncident { public int Id, Type, State, Urgency; public double E, N; public string Address = "", Municipality = "", Canton = "", RadioText = ""; public bool AmbulanceCalled, FireCalled; }
        public sealed class SavedCase { public string Kind = "", Summary = "", Plate = "", Person = ""; public int FineChf; public bool Correct; public double Time; }

        public string ToJson()
        {
            var w = new JsonWriter(); w.BeginObject();
            w.P("version", Version); w.P("corps", CorpsId); w.P("rank", RankIndex); w.P("xp", Xp);
            w.P("playerE", PlayerE); w.P("playerN", PlayerN); w.P("playerH", PlayerH); w.P("playerYaw", PlayerYaw); w.P("inVehicle", InVehicle);
            w.P("vehE", VehicleE); w.P("vehN", VehicleN); w.P("vehH", VehicleH); w.P("vehYaw", VehicleYaw); w.P("fuel", Fuel); w.P("vehDamage", VehicleDamage);
            w.P("gameSeconds", GameSeconds); w.P("timeScale", TimeScale); w.P("startIso", StartIso);
            w.P("legality", Legality); w.P("professionalism", Professionalism); w.P("deescalation", DeEscalation); w.P("reporting", Reporting); w.P("community", Community);
            w.P("complaintsOpen", ComplaintsOpen); w.P("complaintsTotal", ComplaintsTotal); w.P("investigationsOpen", InvestigationsOpen); w.P("disciplinary", DisciplinaryCount);
            w.P("fines", FinesIssued); w.P("arrests", ArrestsMade); w.P("suspended", Suspended); w.P("dismissed", Dismissed); w.P("seed", Seed); w.P("region", Region);
            w.Key("incidents"); w.BeginArray();
            foreach (var i in Incidents) { w.BeginObject(); w.P("id", i.Id); w.P("type", i.Type); w.P("state", i.State); w.P("urgency", i.Urgency); w.P("e", i.E); w.P("n", i.N); w.P("address", i.Address); w.P("muni", i.Municipality); w.P("canton", i.Canton); w.P("radio", i.RadioText); w.P("amb", i.AmbulanceCalled); w.P("fire", i.FireCalled); w.EndObject(); }
            w.EndArray();
            w.Key("cases"); w.BeginArray();
            foreach (var c in Cases) { w.BeginObject(); w.P("kind", c.Kind); w.P("summary", c.Summary); w.P("plate", c.Plate); w.P("person", c.Person); w.P("fine", c.FineChf); w.P("correct", c.Correct); w.P("time", c.Time); w.EndObject(); }
            w.EndArray();
            w.Key("consequences"); w.BeginArray(); foreach (var s in Consequences) w.Str(s); w.EndArray();
            w.Key("flags"); w.BeginArray(); foreach (var s in Flags) w.Str(s); w.EndArray();
            w.EndObject(); return w.ToString();
        }

        public static GameSave FromJson(string json)
        {
            var o = (Dictionary<string, object>)MiniJson.Parse(json); var g = new GameSave();
            double D(string k, double d = 0) => o.TryGetValue(k, out var v) && v is double x ? x : d;
            string S(string k, string d = "") => o.TryGetValue(k, out var v) && v is string x ? x : d;
            bool B(string k, bool d = false) => o.TryGetValue(k, out var v) && v is bool x ? x : d;
            if ((int)D("version", 0) > Version) throw new InvalidDataException("save from a newer game version");
            g.CorpsId = S("corps", g.CorpsId); g.RankIndex = (int)D("rank"); g.Xp = D("xp");
            g.PlayerE = D("playerE"); g.PlayerN = D("playerN"); g.PlayerH = D("playerH"); g.PlayerYaw = (float)D("playerYaw"); g.InVehicle = B("inVehicle", true);
            g.VehicleE = D("vehE"); g.VehicleN = D("vehN"); g.VehicleH = D("vehH"); g.VehicleYaw = (float)D("vehYaw"); g.Fuel = (float)D("fuel", 1); g.VehicleDamage = (float)D("vehDamage");
            g.GameSeconds = D("gameSeconds"); g.TimeScale = (float)D("timeScale", 12); g.StartIso = S("startIso", g.StartIso);
            g.Legality = (float)D("legality", 70); g.Professionalism = (float)D("professionalism", 70); g.DeEscalation = (float)D("deescalation", 70); g.Reporting = (float)D("reporting", 70); g.Community = (float)D("community", 70);
            g.ComplaintsOpen = (int)D("complaintsOpen"); g.ComplaintsTotal = (int)D("complaintsTotal"); g.InvestigationsOpen = (int)D("investigationsOpen"); g.DisciplinaryCount = (int)D("disciplinary");
            g.FinesIssued = (int)D("fines"); g.ArrestsMade = (int)D("arrests"); g.Suspended = B("suspended"); g.Dismissed = B("dismissed"); g.Seed = (int)D("seed", 1); g.Region = S("region");
            if (o.TryGetValue("incidents", out var il) && il is List<object> ilist)
                foreach (Dictionary<string, object> d in ilist) g.Incidents.Add(new SavedIncident { Id = (int)(double)d["id"], Type = (int)(double)d["type"], State = (int)(double)d["state"], Urgency = (int)(double)d["urgency"], E = (double)d["e"], N = (double)d["n"], Address = (string)d["address"], Municipality = (string)d["muni"], Canton = (string)d["canton"], RadioText = (string)d["radio"], AmbulanceCalled = (bool)d["amb"], FireCalled = (bool)d["fire"] });
            if (o.TryGetValue("cases", out var cl) && cl is List<object> clist)
                foreach (Dictionary<string, object> d in clist) g.Cases.Add(new SavedCase { Kind = (string)d["kind"], Summary = (string)d["summary"], Plate = (string)d["plate"], Person = (string)d["person"], FineChf = (int)(double)d["fine"], Correct = (bool)d["correct"], Time = (double)d["time"] });
            if (o.TryGetValue("consequences", out var cs) && cs is List<object> cslist) foreach (var s in cslist) g.Consequences.Add((string)s);
            if (o.TryGetValue("flags", out var fl) && fl is List<object> fllist) foreach (var s in fllist) g.Flags.Add((string)s);
            return g;
        }

        public void SaveToFile(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp"; File.WriteAllText(tmp, ToJson(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak"); else File.Move(tmp, path);      // atomic-ish: never leaves a half-written save
        }

        public static GameSave LoadFromFile(string path)
        {
            try { return FromJson(File.ReadAllText(path)); }
            catch { string bak = path + ".bak"; if (File.Exists(bak)) return FromJson(File.ReadAllText(bak)); throw; }
        }
    }

    public sealed class JsonWriter
    {
        readonly StringBuilder _sb = new StringBuilder(); readonly Stack<bool> _first = new Stack<bool>(); bool _needComma;
        void Comma() { if (_needComma) _sb.Append(','); _needComma = true; }
        public void BeginObject() { Comma(); _sb.Append('{'); _needComma = false; }
        public void EndObject() { _sb.Append('}'); _needComma = true; }
        public void BeginArray() { Comma(); _sb.Append('['); _needComma = false; }
        public void EndArray() { _sb.Append(']'); _needComma = true; }
        public void Key(string k) { Comma(); _sb.Append('"').Append(Esc(k)).Append("\":"); _needComma = false; }
        public void Str(string s) { Comma(); _sb.Append('"').Append(Esc(s)).Append('"'); }
        public void P(string k, string v) { Key(k); _sb.Append('"').Append(Esc(v ?? "")).Append('"'); _needComma = true; }
        public void P(string k, double v) { Key(k); _sb.Append(v.ToString("R", CultureInfo.InvariantCulture)); _needComma = true; }
        public void P(string k, int v) { Key(k); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); _needComma = true; }
        public void P(string k, float v) { Key(k); _sb.Append(v.ToString("R", CultureInfo.InvariantCulture)); _needComma = true; }
        public void P(string k, bool v) { Key(k); _sb.Append(v ? "true" : "false"); _needComma = true; }
        static string Esc(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t"); }
        public override string ToString() => _sb.ToString();
    }
}
