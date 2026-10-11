using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Scripted end-to-end test of SwissServer: connects like the Unreal client, plays a short game and checks that every subsystem answers.</summary>
static class P
{
    static readonly System.Net.Http.HttpClient Http = new System.Net.Http.HttpClient(); static ClientWebSocket ws; static readonly List<JsonElement> Texts = new List<JsonElement>(); static int tiles, snaps, models; static readonly HashSet<int> CarsSeen = new HashSet<int>(); static readonly object L = new object();
    static double E, N, H; static List<(int id, double e, double n, float v)> LastCars = new List<(int, double, double, float)>(); static int failures;
    static List<(int id, double e, double n)> LastActors = new List<(int, double, double)>();

    static void Check(bool ok, string what) { Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what); if (!ok) failures++; }

    static async Task Send(object o) { await ws.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o)), WebSocketMessageType.Text, true, CancellationToken.None); }

    static async Task Receive()
    {
        var buf = new byte[1 << 20];
        var chunkBuf = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream(); WebSocketReceiveResult r; do { r = await ws.ReceiveAsync(buf, CancellationToken.None); if (r.MessageType == WebSocketMessageType.Close) return; ms.Write(buf, 0, r.Count); } while (!r.EndOfMessage);
            var data = ms.ToArray();
            if (r.MessageType == WebSocketMessageType.Binary && data.Length > 9 && data[0] == 6)   // Chunk (siehe Msg.Chunk): zusammensetzen
            {
                int idx = BitConverter.ToInt32(data, 1), cnt = BitConverter.ToInt32(data, 5); if (idx == 0) chunkBuf.SetLength(0); chunkBuf.Write(data, 9, data.Length - 9);
                if (idx != cnt - 1) continue; data = chunkBuf.ToArray(); chunkBuf.SetLength(0);
            }
            if (r.MessageType == WebSocketMessageType.Text)
            {   // HTTP-Tile-Download (wie der Unreal-Client): tileReady -> GET /tile/{token}
                using (var d0 = JsonDocument.Parse(data)) if (d0.RootElement.TryGetProperty("t", out var tt) && tt.GetString() == "tileReady")
                { var bytes = await Http.GetByteArrayAsync("http://127.0.0.1:47800/tile/" + d0.RootElement.GetProperty("token").GetString()); if (bytes.Length > 1 && bytes[0] == 1) tiles++; continue; }
            }
            if (r.MessageType == WebSocketMessageType.Text) { using var d = JsonDocument.Parse(data); lock (L) { Texts.Add(d.RootElement.Clone()); if (Texts.Count > 5000) Texts.RemoveRange(0, 1000); } }
            else
            {
                switch (data[0])
                {
                    case 1: tiles++; break; case 5: models++; break;
                    case 2:
                        snaps++; var br = new BinaryReader(new MemoryStream(data)); br.ReadByte(); br.ReadDouble(); int n = br.ReadInt32(); var cars = new List<(int, double, double, float)>();
                        for (int i = 0; i < n; i++) { int id = br.ReadInt32(); double e = br.ReadDouble(), nn = br.ReadDouble(); br.ReadSingle(); br.ReadSingle(); float v = br.ReadSingle(); br.ReadUInt16(); cars.Add((id, e, nn, v)); CarsSeen.Add(id); }
                        int np = br.ReadInt32(); for (int i = 0; i < np; i++) { br.ReadInt32(); br.ReadDouble(); br.ReadDouble(); br.ReadSingle(); br.ReadSingle(); br.ReadByte(); br.ReadSingle(); }
                        int na = br.ReadInt32(); var acts = new List<(int, double, double)>(); for (int i = 0; i < na; i++) { int id = br.ReadInt32(); double e = br.ReadDouble(), nn = br.ReadDouble(); br.ReadSingle(); br.ReadSingle(); br.ReadByte(); br.ReadByte(); acts.Add((id, e, nn)); }
                        lock (L) { LastCars = cars; LastActors = acts; } break;
                }
            }
        }
    }

    static JsonElement? Last(string t) { lock (L) { for (int i = Texts.Count - 1; i >= 0; i--) if (Texts[i].TryGetProperty("t", out var v) && v.GetString() == t) return Texts[i]; } return null; }
    static int Count(string t) { lock (L) return Texts.Count(x => x.TryGetProperty("t", out var v) && v.GetString() == t); }
    static async Task<JsonElement?> WaitFor(string t, int ms, int after = 0) { var end = DateTime.UtcNow.AddMilliseconds(ms); int start = Count(t); while (DateTime.UtcNow < end) { if (Count(t) > start + after - (after > 0 ? 0 : 0) && Count(t) > start) return Last(t); await Task.Delay(50); } return null; }

    static async Task Pose(double e, double n, double h, bool inVeh = false, double speedKmh = 0)
    {
        E = e; N = n; H = h; await Send(new { t = "pose", e, n, h, yaw = 0, speed = speedKmh, inVehicle = inVeh, siren = false, blue = false, vE = e + 6, vN = n, vH = h, vYaw = 0, fuel = 1.0, damage = 0.0 });
    }

    static async Task<int> Main(string[] args)
    {
        string url = args.Length > 0 ? args[0] : "ws://127.0.0.1:47800/ws"; ws = new ClientWebSocket(); await ws.ConnectAsync(new Uri(url), CancellationToken.None); var rx = Receive();
        await Send(new { t = "hello", role = "game", name = "selftest", http = true }); var w = await WaitFor("welcome", 5000); Check(w != null, "welcome message received");
        if (w != null) Check(w.Value.GetProperty("corps").GetArrayLength() >= 26, $"corps list has {w.Value.GetProperty("corps").GetArrayLength()} entries (26 cantons + cities)");
        await Send(new { t = "new", corps = "KAPO_BE", seed = 5 }); var st = await WaitFor("started", 5000); Check(st != null, "game started");
        if (st == null) return 1; E = st.Value.GetProperty("e").GetDouble(); N = st.Value.GetProperty("n").GetDouble(); H = st.Value.GetProperty("h").GetDouble(); Console.WriteLine($"  start LV95 {E:F0}/{N:F0}/{H:F0}");
        await Send(new { t = "timescale", v = 12 });
        for (int i = 0; i < 160; i++) { await Pose(E, N, H, false, 0); await Task.Delay(100); }
        Check(tiles >= 9, $"{tiles} tile messages streamed");
        Check(snaps > 60, $"{snaps} snapshots");
        var state = Last("state"); Check(state != null, "state message");
        if (state != null) { var s = state.Value; Check(s.GetProperty("place").GetString().Length > 0, "place = " + s.GetProperty("place").GetString()); Check(s.GetProperty("limit").GetInt32() >= 0, "speed limit = " + s.GetProperty("limit").GetInt32() + " km/h at " + s.GetProperty("street").GetString()); Check(s.GetProperty("jurisdiction").GetProperty("text").GetString().Length > 0, "jurisdiction text"); Console.WriteLine("  " + s.GetProperty("jurisdiction").GetProperty("text").GetString()); }
        Check(CarsSeen.Count > 5, $"{CarsSeen.Count} traffic vehicles simulated"); Check(Count("spawn") > 5, $"{Count("spawn")} spawn messages"); Check(models > 2, $"{models} model messages");

        // radar + stop + encounter
        (int id, double e, double n, float v) target = default; lock (L) { var cand = LastCars.Where(c => c.v > 3).OrderBy(c => (c.e - E) * (c.e - E) + (c.n - N) * (c.n - N)).ToList(); if (cand.Count > 0) target = cand[0]; }
        Check(target.id != 0, "found a moving car near the player");
        if (target.id != 0)
        {
            await Send(new { t = "radar", on = true }); await Send(new { t = "measure", vehicleId = target.id, dist = 80.0 }); var m = await WaitFor("measure", 3000); Check(m != null, "radar measurement"); if (m != null) Console.WriteLine($"  measured {m.Value.GetProperty("kmh").GetDouble()} km/h, limit {m.Value.GetProperty("limit").GetInt32()}");
            await Send(new { t = "pose", e = E, n = N, h = H, yaw = 0, speed = 0, inVehicle = false, siren = true, blue = true, vE = E + 6, vN = N, vH = H, vYaw = 0 });
            await Send(new { t = "stop", vehicleId = target.id });
            for (int i = 0; i < 300; i++) { await Task.Delay(100); lock (L) { var c = LastCars.FirstOrDefault(x => x.id == target.id); if (c.id != 0 && c.v < 0.3f) { target = c; break; } } }
            Check(target.v < 0.3f, "vehicle pulled over and stopped");
            await Pose(target.e + 2, target.n, H); await Task.Delay(300); await Send(new { t = "approach", vehicleId = target.id }); var enc = await WaitFor("encounter", 3000); Check(enc != null && enc.Value.GetProperty("enc").ValueKind == JsonValueKind.Object, "encounter opened");
            await Send(new { t = "say", text = "Guten Tag, Verkehrskontrolle. Ihre Papiere bitte." }); await Task.Delay(300); await Send(new { t = "enc", op = "docs" }); await Task.Delay(300);
            var e2 = Last("encounter"); if (e2 != null && e2.Value.GetProperty("enc").ValueKind == JsonValueKind.Object) { var card = e2.Value.GetProperty("enc").GetProperty("card"); Check(card.ValueKind == JsonValueKind.Object, "ID card shown after documents request"); if (card.ValueKind == JsonValueKind.Object) Console.WriteLine("  card: " + card.GetProperty("name").GetString() + ", " + card.GetProperty("vehicle").GetString()); Check(e2.Value.GetProperty("enc").GetProperty("transcript").GetArrayLength() >= 3, "transcript has lines"); }
            await Send(new { t = "enc", op = "queryPlate" }); await Task.Delay(300); await Send(new { t = "enc", op = "end" }); var ee = await WaitFor("encounterEnd", 3000); Check(ee != null, "encounter ended");
        }

        // optional LLM rephrasing against a local fake of the Anthropic API
        if (target.id != 0)
        {
            var http = new System.Net.HttpListener(); http.Prefixes.Add("http://127.0.0.1:47811/"); http.Start();
            _ = Task.Run(async () => { while (http.IsListening) { try { var ctx = await http.GetContextAsync(); var body = "{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"say\\\":\\\"Selbstverständlich, einen Moment, ich suche meine Papiere zusammen.\\\",\\\"action\\\":\\\"none\\\"}\"}]}"; var b = Encoding.UTF8.GetBytes(body); ctx.Response.ContentType = "application/json"; await ctx.Response.OutputStream.WriteAsync(b); ctx.Response.Close(); } catch (Exception) { } } });
            await Send(new { t = "llm", enabled = true, key = "test-key", url = "http://127.0.0.1:47811/v1/messages" });
            await Pose(target.e + 2, target.n, H); await Send(new { t = "approach", vehicleId = target.id }); var enc2 = await WaitFor("encounter", 3000);
            if (enc2 != null) { lock (L) Texts.RemoveAll(x => x.TryGetProperty("t", out var tv) && (tv.GetString() == "speak")); await Send(new { t = "say", text = "Guten Tag, Ihre Papiere bitte." }); var sp = await WaitFor("speak", 5000); Check(sp != null && sp.Value.GetProperty("text").GetString().Contains("Papiere zusammen"), "LLM line replaced the offline reply: " + (sp != null ? sp.Value.GetProperty("text").GetString() : "(none)")); await Send(new { t = "enc", op = "end" }); await Task.Delay(300); }
            await Send(new { t = "llm", enabled = false }); http.Stop();
        }

        // computer, navigation, save/load
        await Send(new { t = "computer", kind = "plate", plate = "BE 123456", reason = 0 }); var cr = await WaitFor("computerResult", 3000); Check(cr != null && cr.Value.GetProperty("lines").GetArrayLength() > 0, "computer plate query");
        await Send(new { t = "computer", kind = "plate", plate = "BE 123456", reason = 4 }); cr = await WaitFor("computerResult", 3000); Check(cr != null && !cr.Value.GetProperty("lawful").GetBoolean(), "private-interest query is flagged unlawful");
        await Send(new { t = "search", text = "Bahnhof" }); var sr = await WaitFor("searchResult", 3000); Check(sr != null && sr.Value.GetProperty("items").GetArrayLength() > 0, "POI search");
        await Send(new { t = "route", e = E + 1500, n = N + 900, name = "Testziel" }); var rt = await WaitFor("route", 8000); Check(rt != null && rt.Value.GetProperty("points").GetArrayLength() > 10, "route computed"); 
        for (int i = 0; i < 20; i++) { await Pose(E, N, H, true, 30); await Task.Delay(100); } state = Last("state"); if (state != null) Check(state.Value.GetProperty("nav").GetProperty("active").GetBoolean(), "navigation active, next: " + (state.Value.GetProperty("nav").GetProperty("next").ValueKind == JsonValueKind.Object ? state.Value.GetProperty("nav").GetProperty("next").GetProperty("text").GetString() : "-"));
        await Send(new { t = "routeClear" });
        await Send(new { t = "save", slot = 5 }); await Task.Delay(500); await Send(new { t = "load", slot = 5 }); var st2 = await WaitFor("started", 4000); Check(st2 != null, "save and load round trip");

        // incident flow: fast clock until dispatch offers a call
        await Send(new { t = "timescale", v = 60 }); lock (L) Texts.RemoveAll(x => x.TryGetProperty("t", out var tv) && tv.GetString() == "state"); JsonElement? offered = null; for (int i = 0; i < 600 && offered == null; i++) { await Pose(E, N, H, false, 0); await Task.Delay(100); var s = Last("state"); if (s != null && s.Value.GetProperty("offered").ValueKind == JsonValueKind.Object) offered = s; }
        Check(offered != null, "dispatch offered an incident"); if (offered != null) Console.WriteLine("  radio: " + offered.Value.GetProperty("offered").GetProperty("text").GetString());
        if (offered != null)
        {
            await Send(new { t = "accept" }); for (int i = 0; i < 40; i++) { await Task.Delay(100); var ss = Last("state"); if (ss != null && ss.Value.GetProperty("active").ValueKind == JsonValueKind.Object) break; } Console.WriteLine("  state after accept: " + Last("state").Value.GetProperty("active").ToString().Substring(0, Math.Min(200, Last("state").Value.GetProperty("active").ToString().Length)) + " | offered=" + Last("state").Value.GetProperty("offered").ValueKind); var a = Last("state").Value.GetProperty("active"); Check(a.ValueKind == JsonValueKind.Object, "incident accepted"); double ie = a.GetProperty("e").GetDouble(), inn = a.GetProperty("n").GetDouble(); Console.WriteLine($"  at {a.GetProperty("address").GetString()}, {a.GetProperty("muni").GetString()} ({a.GetProperty("type").GetString()})");
            for (int i = 0; i < 40; i++) { await Pose(ie, inn, H, false, 0); await Task.Delay(100); }
            var s3 = Last("state").Value.GetProperty("active"); Check(s3.GetProperty("state").GetString() == "OnScene", "arrived on scene");
            Check(Count("actors") > 0 || Count("props") > 0, "scene entities sent (actors/props)");
            await Task.Delay(700); await Send(new { t = "report", atFault = 0, injured = false, amb = false, tow = false, narrative = "Fahrer A fuhr auf das stehende Fahrzeug auf; keine Verletzten, Sachschaden." }); await Task.Delay(900); Check(Last("state").Value.GetProperty("active").ValueKind == JsonValueKind.Null, "report submitted, incident closed");
        }
        await Send(new { t = "career" }); Check((await WaitFor("career", 2000)) != null, "career view");
        Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECKS FAILED"); try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch (Exception) { } return failures == 0 ? 0 : 1;
    }
}
