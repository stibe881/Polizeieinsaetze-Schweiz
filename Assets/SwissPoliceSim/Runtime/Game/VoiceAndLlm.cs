using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SwissPoliceSim.Core.Npc;
using SwissPoliceSim.Core.Util;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SwissPoliceSim.Game
{
    /// <summary>
    /// Optional speech: text-to-speech and speech-to-text through the Windows speech engines (System.Speech via a hidden PowerShell process – no Unity plugin needed).
    /// Both are OFF by default and degrade silently if Windows has no voice / recogniser for the language. Dictation needs the Windows "speech recognition" language pack.
    /// </summary>
    public sealed class VoiceService : MonoBehaviour
    {
        public bool TtsEnabled, SttEnabled; public string LastHeard = ""; public bool Listening { get; private set; } public string Status = "";
        Process _stt; readonly ConcurrentQueue<string> _heard = new ConcurrentQueue<string>(); Process _tts; readonly Queue<(string, string, bool)> _queue = new Queue<(string, string, bool)>();
        public event Action<string> Heard;
        public static bool Supported => Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;

        void Awake() { Voice.SpeakText = Speak; }
        void OnDestroy() { StopListening(); try { if (_tts != null && !_tts.HasExited) _tts.Kill(); } catch { } if (Voice.SpeakText == (Action<string, string, bool>)Speak) Voice.SpeakText = null; }

        void Update()
        {
            while (_heard.TryDequeue(out var line)) { LastHeard = line; Heard?.Invoke(line); }
            if (_queue.Count > 0 && (_tts == null || _tts.HasExited)) { var (t, lang, female) = _queue.Dequeue(); StartTts(t, lang, female); }
        }

        // ------------------------------------------------------------------ TTS
        public void Speak(string text, string lang, bool female)
        {
            if (!TtsEnabled || !Supported || string.IsNullOrWhiteSpace(text)) return; if (_queue.Count > 2) _queue.Dequeue(); _queue.Enqueue((text, lang, female));
        }

        void StartTts(string text, string lang, bool female)
        {
            try
            {
                string culture = lang == "fr" ? "fr-CH" : lang == "it" ? "it-CH" : lang == "en" ? "en-GB" : "de-CH"; string tmp = Path.Combine(Application.temporaryCachePath, "tts_" + Guid.NewGuid().ToString("N") + ".txt"); File.WriteAllText(tmp, text, new UTF8Encoding(false));
                string script = "Add-Type -AssemblyName System.Speech; $s = New-Object System.Speech.Synthesis.SpeechSynthesizer; try { $s.SelectVoiceByHints('" + (female ? "Female" : "Male") + "','NotSet',0,[System.Globalization.CultureInfo]'" + culture + "') } catch { try { $s.SelectVoiceByHints('" + (female ? "Female" : "Male") + "','NotSet',0,[System.Globalization.CultureInfo]'" + culture.Substring(0, 2) + "') } catch {} }; $s.Rate = 1; $t = [System.IO.File]::ReadAllText('" + tmp.Replace("'", "''") + "'); $s.Speak($t); Remove-Item '" + tmp.Replace("'", "''") + "'";
                _tts = Start(script, false);
            }
            catch (Exception ex) { Status = "TTS: " + ex.Message; TtsEnabled = false; }
        }

        // ------------------------------------------------------------------ STT
        public void StartListening(string lang)
        {
            if (!Supported || _stt != null) return; string culture = lang == "fr" ? "fr-CH" : lang == "it" ? "it-CH" : "de-CH";
            string script = "Add-Type -AssemblyName System.Speech; $r = $null; foreach ($c in @('" + culture + "','" + culture.Substring(0, 2) + "-" + (lang == "de" ? "DE" : lang == "fr" ? "FR" : "IT") + "','')) { try { if ($c -eq '') { $r = New-Object System.Speech.Recognition.SpeechRecognitionEngine } else { $r = New-Object System.Speech.Recognition.SpeechRecognitionEngine([System.Globalization.CultureInfo]$c) }; break } catch {} }; if ($r -eq $null) { [Console]::Out.WriteLine('##ERR no recogniser'); exit }; $r.SetInputToDefaultAudioDevice(); $r.LoadGrammar((New-Object System.Speech.Recognition.DictationGrammar)); Register-ObjectEvent -InputObject $r -EventName SpeechRecognized -Action { [Console]::Out.WriteLine($Event.SourceEventArgs.Result.Text); [Console]::Out.Flush() } | Out-Null; $r.RecognizeAsync([System.Speech.Recognition.RecognizeMode]::Multiple); [Console]::Out.WriteLine('##READY'); while ($true) { Start-Sleep -Milliseconds 200 }";
            try { _stt = Start(script, true); Listening = true; Status = "Mikrofon wird gestartet …"; } catch (Exception ex) { Status = "STT: " + ex.Message; Listening = false; _stt = null; }
        }

        public void StopListening() { try { if (_stt != null && !_stt.HasExited) _stt.Kill(); } catch { } _stt = null; Listening = false; }

        Process Start(string script, bool capture)
        {
            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = capture, RedirectStandardError = capture, WindowStyle = ProcessWindowStyle.Hidden };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            if (capture) p.OutputDataReceived += (s, e) => { if (string.IsNullOrWhiteSpace(e.Data)) return; if (e.Data.StartsWith("##READY")) { Status = "Mikrofon aktiv – sprechen Sie."; return; } if (e.Data.StartsWith("##ERR")) { Status = "Keine Spracherkennung für diese Sprache installiert."; return; } _heard.Enqueue(e.Data.Trim()); };
            p.Start(); if (capture) p.BeginOutputReadLine(); return p;
        }
    }

    /// <summary>Optional Claude-backed NPC phrasing. The offline NpcMind stays authoritative for actions and state; the model only rewrites the spoken line (validated, 5 s timeout, falls back silently).</summary>
    public sealed class LlmService : MonoBehaviour, ILanguageBackend
    {
        public bool Enabled; public string Model = "claude-haiku-5-5"; public string ApiKey = ""; public string Status = "aus";
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        public static string KeyFromEnvironment => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";

        public bool Ready => Enabled && !string.IsNullOrEmpty(string.IsNullOrEmpty(ApiKey) ? KeyFromEnvironment : ApiKey);

        public async Task<string> CompleteAsync(string systemPrompt, IList<KeyValuePair<string, string>> turns, CancellationToken ct)
        {
            string key = string.IsNullOrEmpty(ApiKey) ? KeyFromEnvironment : ApiKey; if (string.IsNullOrEmpty(key)) return null;
            var sb = new StringBuilder(); sb.Append("{\"model\":").Append(Q(Model)).Append(",\"max_tokens\":300,\"system\":").Append(Q(systemPrompt)).Append(",\"messages\":[");
            for (int i = 0; i < turns.Count; i++) { if (i > 0) sb.Append(','); sb.Append("{\"role\":").Append(Q(turns[i].Key)).Append(",\"content\":").Append(Q(turns[i].Value)).Append('}'); }
            sb.Append("]}");
            using (var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages"))
            {
                req.Headers.Add("x-api-key", key); req.Headers.Add("anthropic-version", "2023-06-01"); req.Content = new StringContent(sb.ToString(), Encoding.UTF8, "application/json");
                using (var resp = await Http.SendAsync(req, ct).ConfigureAwait(false))
                {
                    string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) { Status = "Fehler " + (int)resp.StatusCode; return null; }
                    var o = MiniJson.Parse(body) as Dictionary<string, object>; if (o == null || !o.TryGetValue("content", out var c) || !(c is List<object> list) || list.Count == 0) return null;
                    return (list[0] as Dictionary<string, object>)?["text"] as string;
                }
            }
        }

        static string Q(string s)
        {
            var sb = new StringBuilder("\""); foreach (char c in s ?? "") { switch (c) { case '"': sb.Append("\\\""); break; case '\\': sb.Append("\\\\"); break; case '\n': sb.Append("\\n"); break; case '\r': break; case '\t': sb.Append("\\t"); break; default: if (c < 32) sb.Append(' '); else sb.Append(c); break; } }
            return sb.Append('"').ToString();
        }

        /// <summary>Asks the model to phrase the NPC's answer; returns the validated line or null.</summary>
        public async Task<string> PhraseAsync(NpcMind mind, string corps, ParsedUtterance utterance, string officerText, string offlineLine)
        {
            if (!Ready) return null;
            try
            {
                Status = "Modell denkt …"; var turns = new List<KeyValuePair<string, string>>();
                foreach (var m in mind.Memory) { }
                turns.Add(new KeyValuePair<string, string>("user", "Der Polizist sagt: \"" + officerText + "\"\nVorschlag der Spiellogik für deine Antwort (Inhalt und Haltung beibehalten, nur natürlich formulieren): \"" + offlineLine + "\""));
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6)))
                {
                    string txt = await CompleteAsync(NpcPromptBuilder.Build(mind, corps), turns, cts.Token).ConfigureAwait(false);
                    var r = NpcActionValidator.ParseAndValidate(txt, mind, utterance); Status = r != null ? "Modell aktiv" : "Modellantwort verworfen"; return r?.Text;
                }
            }
            catch (Exception ex) { Status = "Modell nicht erreichbar"; Debug.LogWarning("LLM: " + ex.Message); return null; }
        }
    }
}
