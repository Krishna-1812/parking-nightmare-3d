using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PN3D.Core
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// Minimal dependency-free JSON reader.
    ///
    /// Core deliberately has no external references (see PN3D.Core.asmdef,
    /// noEngineReferences), which lets the exact same sources compile under Unity and
    /// under desktop .NET for the validation harness. Unity's JsonUtility cannot
    /// express the optional fields this data uses (a missing key and a zero must stay
    /// distinguishable), and taking a Newtonsoft dependency would make the harness a
    /// different code path from the game — which is precisely what we are trying to
    /// avoid.
    /// </summary>
    public sealed class JsonValue
    {
        public JsonKind Kind;
        public bool Bool;
        public double Number;
        public string Str;
        public List<JsonValue> Items;
        public Dictionary<string, JsonValue> Members;

        public int Count => Items?.Count ?? 0;
        public bool IsNull => Kind == JsonKind.Null;

        public JsonValue this[int i] => Items[i];

        /// <summary>Missing member returns null, modelling JS <c>undefined</c>.</summary>
        public JsonValue this[string key]
            => Members != null && Members.TryGetValue(key, out var v) ? v : null;

        public double AsDouble => Number;
        public int AsInt => (int)Number;
        public string AsString => Str;
        public bool AsBool => Bool;

        // ---- optional accessors: absent or JSON null both yield no value ----

        public double? OptDouble(string key)
        {
            var v = this[key];
            return v == null || v.IsNull ? (double?)null : v.Number;
        }

        public int? OptInt(string key)
        {
            var v = this[key];
            return v == null || v.IsNull ? (int?)null : (int)v.Number;
        }

        public bool? OptBool(string key)
        {
            var v = this[key];
            return v == null || v.IsNull ? (bool?)null : v.Bool;
        }

        public string OptString(string key)
        {
            var v = this[key];
            return v == null || v.IsNull ? null : v.Str;
        }

        public double DoubleOr(string key, double fallback) => OptDouble(key) ?? fallback;
        public int IntOr(string key, int fallback) => OptInt(key) ?? fallback;
        public bool BoolOr(string key, bool fallback) => OptBool(key) ?? fallback;

        // ---- parser ----

        public static JsonValue Parse(string text)
        {
            int i = 0;
            var v = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i != text.Length)
                throw new FormatException($"trailing JSON content at offset {i}");
            return v;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                else break;
            }
        }

        static JsonValue ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end of JSON");
            switch (s[i])
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return new JsonValue { Kind = JsonKind.String, Str = ParseString(s, ref i) };
                case 't': Expect(s, ref i, "true"); return new JsonValue { Kind = JsonKind.Bool, Bool = true };
                case 'f': Expect(s, ref i, "false"); return new JsonValue { Kind = JsonKind.Bool, Bool = false };
                case 'n': Expect(s, ref i, "null"); return new JsonValue { Kind = JsonKind.Null };
                default: return ParseNumber(s, ref i);
            }
        }

        static void Expect(string s, ref int i, string lit)
        {
            if (i + lit.Length > s.Length || string.CompareOrdinal(s, i, lit, 0, lit.Length) != 0)
                throw new FormatException($"expected '{lit}' at offset {i}");
            i += lit.Length;
        }

        static JsonValue ParseObject(string s, ref int i)
        {
            var o = new JsonValue { Kind = JsonKind.Object, Members = new Dictionary<string, JsonValue>() };
            i++; // '{'
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return o; }
            while (true)
            {
                SkipWs(s, ref i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException($"expected ':' at offset {i}");
                i++;
                o.Members[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return o; }
                throw new FormatException($"expected ',' or '}}' at offset {i}");
            }
        }

        static JsonValue ParseArray(string s, ref int i)
        {
            var a = new JsonValue { Kind = JsonKind.Array, Items = new List<JsonValue>() };
            i++; // '['
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (true)
            {
                a.Items.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return a; }
                throw new FormatException($"expected ',' or ']' at offset {i}");
            }
        }

        static string ParseString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') throw new FormatException($"expected string at offset {i}");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                        sb.Append((char)ushort.Parse(s.Substring(i, 4), NumberStyles.HexNumber,
                                                     CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException($"bad escape '\\{e}' at offset {i}");
                }
            }
            throw new FormatException("unterminated string");
        }

        static JsonValue ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                else break;
            }
            if (start == i) throw new FormatException($"expected number at offset {i}");
            return new JsonValue
            {
                Kind = JsonKind.Number,
                Number = double.Parse(s.Substring(start, i - start), NumberStyles.Float,
                                      CultureInfo.InvariantCulture),
            };
        }
    }
}
