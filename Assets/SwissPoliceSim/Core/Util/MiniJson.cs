using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SwissPoliceSim.Core.Util
{
    /// <summary>
    /// Minimal JSON reader (objects -> Dictionary&lt;string,object&gt;, arrays -> List&lt;object&gt;, numbers -> double,
    /// strings, bool, null). Only used for STAC API responses so Core needs no external JSON package.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            int i = 0;
            var v = ReadValue(json, ref i);
            SkipWs(json, ref i);
            if (i != json.Length) throw new FormatException("Trailing characters at " + i);
            return v;
        }

        static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object ReadValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end");
            char c = s[i];
            if (c == '{') return ReadObject(s, ref i);
            if (c == '[') return ReadArray(s, ref i);
            if (c == '"') return ReadString(s, ref i);
            if (Match(s, ref i, "true")) return true;
            if (Match(s, ref i, "false")) return false;
            if (Match(s, ref i, "null")) return null;
            return ReadNumber(s, ref i);
        }

        static bool Match(string s, ref int i, string lit)
        {
            if (string.CompareOrdinal(s, i, lit, 0, lit.Length) != 0) return false;
            i += lit.Length; return true;
        }

        static Dictionary<string, object> ReadObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; SkipWs(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                string key = ReadString(s, ref i);
                SkipWs(s, ref i);
                if (s[i++] != ':') throw new FormatException("Expected ':' at " + (i - 1));
                d[key] = ReadValue(s, ref i);
                SkipWs(s, ref i);
                char c = s[i++];
                if (c == '}') return d;
                if (c != ',') throw new FormatException("Expected ',' or '}' at " + (i - 1));
            }
        }

        static List<object> ReadArray(string s, ref int i)
        {
            var l = new List<object>();
            i++; SkipWs(s, ref i);
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(ReadValue(s, ref i));
                SkipWs(s, ref i);
                char c = s[i++];
                if (c == ']') return l;
                if (c != ',') throw new FormatException("Expected ',' or ']' at " + (i - 1));
            }
        }

        static string ReadString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected string at " + i);
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
        }

        static double ReadNumber(string s, ref int i)
        {
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (st == i) throw new FormatException("Unexpected character '" + s[i] + "' at " + i);
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }
    }
}
