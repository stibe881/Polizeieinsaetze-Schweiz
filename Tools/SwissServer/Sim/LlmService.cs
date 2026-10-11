using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Npc;
using SwissPoliceSim.Core.Util;

namespace SwissServer.Sim
{
    /// <summary>
    /// Optional Claude-backed NPC phrasing. The offline NpcMind stays authoritative for actions and state; the model only rewrites the spoken line
    /// (validated by NpcActionValidator, 6 s timeout, silent fallback). The key comes from the ANTHROPIC_API_KEY environment variable or the "llm" command and is never written to disk.
    /// </summary>
    public sealed class LlmService : ILanguageBackend
    {
        public bool Enabled; public string Model = "claude-haiku-5-5"; public string ApiKey = ""; public string Status = "aus";
        public string Endpoint = Environment.GetEnvironmentVariable("SPS_LLM_URL") ?? "https://api.anthropic.com/v1/messages";
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        public static string KeyFromEnvironment => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
        string Key => string.IsNullOrEmpty(ApiKey) ? KeyFromEnvironment : ApiKey;
        public bool Ready => Enabled && !string.IsNullOrEmpty(Key);

        public async Task<string> CompleteAsync(string systemPrompt, IList<KeyValuePair<string, string>> turns, CancellationToken ct)
        {
            string key = Key; if (string.IsNullOrEmpty(key)) return null;
            var sb = new StringBuilder(); sb.Append("{\"model\":").Append(Q(Model)).Append(",\"max_tokens\":300,\"system\":").Append(Q(systemPrompt)).Append(",\"messages\":[");
            for (int i = 0; i < turns.Count; i++) { if (i > 0) sb.Append(','); sb.Append("{\"role\":").Append(Q(turns[i].Key)).Append(",\"content\":").Append(Q(turns[i].Value)).Append('}'); }
            sb.Append("]}");
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            req.Headers.Add("x-api-key", key); req.Headers.Add("anthropic-version", "2023-06-01"); req.Content = new StringContent(sb.ToString(), Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) { Status = "Fehler " + (int)resp.StatusCode; return null; }
            var o = MiniJson.Parse(body) as Dictionary<string, object>; if (o == null || !o.TryGetValue("content", out var c) || !(c is List<object> list) || list.Count == 0) return null;
            return (list[0] as Dictionary<string, object>)?["text"] as string;
        }

        static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "") { switch (c) { case '"': sb.Append("\\\""); break; case '\\': sb.Append("\\\\"); break; case '\n': sb.Append("\\n"); break; case '\r': break; case '\t': sb.Append("\\t"); break; default: if (c < 32) sb.Append(' '); else sb.Append(c); break; } }
            return sb.Append('"').ToString();
        }

        /// <summary>Asks the model to phrase the NPC's answer; returns the validated line or null.</summary>
        public async Task<string> PhraseAsync(NpcMind mind, string corps, ParsedUtterance utterance, string officerText, string offlineLine)
        {
            if (!Ready) return null;
            try
            {
                Status = "Modell denkt …";
                var turns = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("user", "Der Polizist sagt: \"" + officerText + "\"\nVorschlag der Spiellogik für deine Antwort (Inhalt und Haltung beibehalten, nur natürlich formulieren): \"" + offlineLine + "\"") };
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                string txt = await CompleteAsync(NpcPromptBuilder.Build(mind, corps), turns, cts.Token).ConfigureAwait(false);
                var r = NpcActionValidator.ParseAndValidate(txt, mind, utterance); Status = r != null ? "Modell aktiv" : "Modellantwort verworfen"; return r?.Text;
            }
            catch (Exception ex) { Status = "Modell nicht erreichbar"; Console.WriteLine("LLM: " + ex.Message); return null; }
        }
    }
}
