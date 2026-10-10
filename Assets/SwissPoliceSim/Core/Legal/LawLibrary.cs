using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SwissPoliceSim.Core.Legal
{
    /// <summary>
    /// The official German texts of federal acts (downloaded from Fedlex by Tools/legal/fetch_fedlex.py, files "SR_&lt;nr&gt;.txt").
    /// Lets the game quote articles and lets tests prove that every legal citation in the rules really exists.
    /// </summary>
    public sealed class LawLibrary
    {
        readonly Dictionary<string, string[]> _lines = new Dictionary<string, string[]>();
        public readonly Dictionary<string, string> Meta = new Dictionary<string, string>();

        public static LawLibrary LoadDirectory(string dir)
        {
            var lib = new LawLibrary();
            if (!Directory.Exists(dir)) return lib;
            foreach (var f in Directory.GetFiles(dir, "SR_*.txt"))
            {
                string sr = Path.GetFileNameWithoutExtension(f).Substring(3);
                lib._lines[sr] = File.ReadAllText(f).Replace("\r", "").Split('\n');
            }
            string idx = Path.Combine(dir, "index.json");
            if (File.Exists(idx))
            {
                var parsed = Util.MiniJson.Parse(File.ReadAllText(idx)) as List<object>;
                if (parsed != null)
                    foreach (var o in parsed)
                    {
                        var d = (Dictionary<string, object>)o;
                        lib.Meta[(string)d["sr"]] = $"Stand {d["applicable_from"]}, abgerufen {d["retrieved"]} (Fedlex)";
                    }
            }
            return lib;
        }

        public bool Has(string sr) => _lines.ContainsKey(sr);
        public IEnumerable<string> LoadedActs => _lines.Keys;

        /// <summary>Article text (heading + paragraphs, footnotes may trail) or null. <paramref name="number"/> like "55", "16a", "179novies".</summary>
        public string Article(string sr, string number)
        {
            if (!_lines.TryGetValue(sr, out var lines)) return null;
            var head = new Regex(@"^Art\. " + Regex.Escape(number) + @"(\d*)(\s.*)?$");
            var any = new Regex(@"^Art\. \d+[a-z]*(bis|ter|quater|quinquies|sexies|septies|octies|novies|decies)?\d*(\s.*)?$");
            for (int i = 0; i < lines.Length; i++)
            {
                if (!head.IsMatch(lines[i])) continue;
                var sb = new System.Text.StringBuilder(lines[i]);
                for (int j = i + 1; j < lines.Length && j < i + 160 && !any.IsMatch(lines[j]); j++) sb.Append('\n').Append(lines[j]);
                return sb.ToString();
            }
            return null;
        }

        /// <summary>Plain contains-check (case/space insensitive) used by citation verification.</summary>
        public bool ArticleContains(string sr, string number, string keyword)
        {
            var a = Article(sr, number); if (a == null) return false;
            return Normalize(a).Contains(Normalize(keyword));
        }

        public bool TextContains(string sr, string keyword)
        {
            if (!_lines.TryGetValue(sr, out var lines)) return false;
            string k = Normalize(keyword);
            foreach (var l in lines) if (Normalize(l).Contains(k)) return true;
            return false;
        }

        static string Normalize(string s) => Regex.Replace(s.ToLowerInvariant(), @"\s+", " ").Trim();
    }
}
