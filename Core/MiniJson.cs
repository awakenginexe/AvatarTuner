using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AvatarTuner.Core
{
    /// <summary>
    /// Raised when a JSON document cannot be parsed or serialized.
    /// Carries the character offset at which the failure occurred.
    /// </summary>
    public sealed class MiniJsonException : Exception
    {
        public int Position { get; private set; }

        public MiniJsonException(string message, int position)
            : base(message + " (at character offset " + position.ToString(CultureInfo.InvariantCulture) + ")")
        {
            Position = position;
        }
    }

    /// <summary>
    /// Dependency-free JSON reader/writer.
    ///
    /// Why this exists instead of <c>UnityEngine.JsonUtility</c>:
    ///  * JsonUtility cannot round-trip dictionaries, which the localization catalog
    ///    and the optimization manifest both require.
    ///  * The Core assembly has <c>noEngineReferences</c> enabled so it can be compiled
    ///    and unit tested outside Unity. That rules out any UnityEngine type.
    ///
    /// Object model:
    ///   JSON object  -> Dictionary&lt;string, object&gt;
    ///   JSON array   -> List&lt;object&gt;
    ///   JSON string  -> string
    ///   JSON integer -> long
    ///   JSON float   -> double
    ///   JSON bool    -> bool
    ///   JSON null    -> null
    ///
    /// Integers and floats are kept apart so that round-tripping a manifest does not
    /// silently turn <c>schemaVersion: 1</c> into <c>1.0</c>.
    /// </summary>
    public static class MiniJson
    {
        // ------------------------------------------------------------------ parse

        public static object Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException("json");
            }

            var state = new ParserState(json);
            state.SkipWhitespace();
            var value = ParseValue(state);
            state.SkipWhitespace();
            if (!state.AtEnd)
            {
                throw new MiniJsonException("Unexpected trailing content after the top-level JSON value", state.Index);
            }

            return value;
        }

        /// <summary>Parses a JSON document that must be an object.</summary>
        public static Dictionary<string, object> ParseObject(string json)
        {
            var parsed = Parse(json) as Dictionary<string, object>;
            if (parsed == null)
            {
                throw new MiniJsonException("Top-level JSON value is not an object", 0);
            }

            return parsed;
        }

        /// <summary>Parses a JSON document that must be an array.</summary>
        public static List<object> ParseArray(string json)
        {
            var parsed = Parse(json) as List<object>;
            if (parsed == null)
            {
                throw new MiniJsonException("Top-level JSON value is not an array", 0);
            }

            return parsed;
        }

        private static object ParseValue(ParserState s)
        {
            if (s.AtEnd)
            {
                throw new MiniJsonException("Unexpected end of input while reading a JSON value", s.Index);
            }

            var c = s.Peek();
            switch (c)
            {
                case '{':
                    return ParseObjectBody(s);
                case '[':
                    return ParseArrayBody(s);
                case '"':
                    return ParseString(s);
                case 't':
                    s.ExpectLiteral("true");
                    return true;
                case 'f':
                    s.ExpectLiteral("false");
                    return false;
                case 'n':
                    s.ExpectLiteral("null");
                    return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                    {
                        return ParseNumber(s);
                    }

                    throw new MiniJsonException("Unexpected character '" + c + "' while reading a JSON value", s.Index);
            }
        }

        private static Dictionary<string, object> ParseObjectBody(ParserState s)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            s.Expect('{');
            s.SkipWhitespace();
            if (s.TryConsume('}'))
            {
                return result;
            }

            while (true)
            {
                s.SkipWhitespace();
                if (s.AtEnd || s.Peek() != '"')
                {
                    throw new MiniJsonException("Expected a quoted property name", s.Index);
                }

                var key = ParseString(s);
                s.SkipWhitespace();
                s.Expect(':');
                s.SkipWhitespace();
                var value = ParseValue(s);
                result[key] = value;
                s.SkipWhitespace();
                if (s.TryConsume('}'))
                {
                    return result;
                }

                s.Expect(',');
            }
        }

        private static List<object> ParseArrayBody(ParserState s)
        {
            var result = new List<object>();
            s.Expect('[');
            s.SkipWhitespace();
            if (s.TryConsume(']'))
            {
                return result;
            }

            while (true)
            {
                s.SkipWhitespace();
                result.Add(ParseValue(s));
                s.SkipWhitespace();
                if (s.TryConsume(']'))
                {
                    return result;
                }

                s.Expect(',');
            }
        }

        private static string ParseString(ParserState s)
        {
            s.Expect('"');
            var sb = new StringBuilder();
            while (true)
            {
                if (s.AtEnd)
                {
                    throw new MiniJsonException("Unterminated string literal", s.Index);
                }

                var c = s.Next();
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c != '\\')
                {
                    if (c < 0x20)
                    {
                        throw new MiniJsonException("Unescaped control character in string literal", s.Index - 1);
                    }

                    sb.Append(c);
                    continue;
                }

                if (s.AtEnd)
                {
                    throw new MiniJsonException("Unterminated escape sequence", s.Index);
                }

                var e = s.Next();
                switch (e)
                {
                    case '"':
                        sb.Append('"');
                        break;
                    case '\\':
                        sb.Append('\\');
                        break;
                    case '/':
                        sb.Append('/');
                        break;
                    case 'b':
                        sb.Append('\b');
                        break;
                    case 'f':
                        sb.Append('\f');
                        break;
                    case 'n':
                        sb.Append('\n');
                        break;
                    case 'r':
                        sb.Append('\r');
                        break;
                    case 't':
                        sb.Append('\t');
                        break;
                    case 'u':
                        sb.Append(ParseUnicodeEscape(s));
                        break;
                    default:
                        throw new MiniJsonException("Unrecognised escape sequence '\\" + e + "'", s.Index - 1);
                }
            }
        }

        private static char ParseUnicodeEscape(ParserState s)
        {
            if (s.Index + 4 > s.Length)
            {
                throw new MiniJsonException("Truncated \\u escape sequence", s.Index);
            }

            var hex = s.Source.Substring(s.Index, 4);
            int code;
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
            {
                throw new MiniJsonException("Invalid \\u escape sequence '" + hex + "'", s.Index);
            }

            s.Advance(4);
            return (char)code;
        }

        private static object ParseNumber(ParserState s)
        {
            var start = s.Index;
            if (s.TryConsume('-'))
            {
                // sign consumed
            }

            var isIntegral = true;
            while (!s.AtEnd && IsDigit(s.Peek()))
            {
                s.Next();
            }

            if (!s.AtEnd && s.Peek() == '.')
            {
                isIntegral = false;
                s.Next();
                if (s.AtEnd || !IsDigit(s.Peek()))
                {
                    throw new MiniJsonException("Expected a digit after the decimal point", s.Index);
                }

                while (!s.AtEnd && IsDigit(s.Peek()))
                {
                    s.Next();
                }
            }

            if (!s.AtEnd && (s.Peek() == 'e' || s.Peek() == 'E'))
            {
                isIntegral = false;
                s.Next();
                if (!s.AtEnd && (s.Peek() == '+' || s.Peek() == '-'))
                {
                    s.Next();
                }

                if (s.AtEnd || !IsDigit(s.Peek()))
                {
                    throw new MiniJsonException("Expected a digit in the exponent", s.Index);
                }

                while (!s.AtEnd && IsDigit(s.Peek()))
                {
                    s.Next();
                }
            }

            var text = s.Source.Substring(start, s.Index - start);
            if (isIntegral)
            {
                long longValue;
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out longValue))
                {
                    return longValue;
                }
            }

            double doubleValue;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
            {
                throw new MiniJsonException("Invalid number literal '" + text + "'", start);
            }

            return doubleValue;
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        // ---------------------------------------------------------------- serialize

        /// <summary>Serializes a value to compact JSON.</summary>
        public static string Serialize(object value)
        {
            return Serialize(value, false);
        }

        /// <summary>Serializes a value to JSON, optionally with indentation.</summary>
        public static string Serialize(object value, bool pretty)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, value, pretty, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, bool pretty, int depth)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            var s = value as string;
            if (s != null)
            {
                WriteString(sb, s);
                return;
            }

            if (value is bool)
            {
                sb.Append((bool)value ? "true" : "false");
                return;
            }

            // NOTE: bool is a struct so the `is` test above is exact; the numeric tests
            // below use boxed-type checks because the compiler widens small integers.
            if (value is int || value is long || value is short || value is byte || value is uint || value is ulong || value is ushort || value is sbyte)
            {
                sb.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (value is float || value is double || value is decimal)
            {
                var d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    throw new MiniJsonException("Cannot serialize non-finite number '" + d.ToString(CultureInfo.InvariantCulture) + "'", 0);
                }

                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            var dict = value as IDictionary<string, object>;
            if (dict != null)
            {
                WriteObject(sb, dict, pretty, depth);
                return;
            }

            var map = value as IDictionary;
            if (map != null)
            {
                // Non-generic dictionaries (e.g. Hashtable) are flattened to string keys.
                var converted = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in map)
                {
                    converted[Convert.ToString(entry.Key, CultureInfo.InvariantCulture)] = entry.Value;
                }

                WriteObject(sb, converted, pretty, depth);
                return;
            }

            var list = value as IList;
            if (list != null)
            {
                WriteArray(sb, list, pretty, depth);
                return;
            }

            // Anything else is rendered through its invariant string form as a JSON string,
            // so that an unexpected type never produces silently corrupt JSON.
            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteObject(StringBuilder sb, IDictionary<string, object> dict, bool pretty, int depth)
        {
            sb.Append('{');
            var first = true;
            foreach (var pair in dict)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                NewLineIndent(sb, pretty, depth + 1);
                WriteString(sb, pair.Key);
                sb.Append(':');
                if (pretty)
                {
                    sb.Append(' ');
                }

                WriteValue(sb, pair.Value, pretty, depth + 1);
            }

            if (!first)
            {
                NewLineIndent(sb, pretty, depth);
            }

            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IList list, bool pretty, int depth)
        {
            sb.Append('[');
            var first = true;
            foreach (var item in list)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                NewLineIndent(sb, pretty, depth + 1);
                WriteValue(sb, item, pretty, depth + 1);
            }

            if (!first)
            {
                NewLineIndent(sb, pretty, depth);
            }

            sb.Append(']');
        }

        private static void NewLineIndent(StringBuilder sb, bool pretty, int depth)
        {
            if (!pretty)
            {
                return;
            }

            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
        }

        // ------------------------------------------------------------ typed access

        /// <summary>Reads a string, returning <paramref name="fallback" /> when absent or of the wrong type.</summary>
        public static string AsString(object value, string fallback)
        {
            return value == null ? fallback : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        /// <summary>Reads a boolean, returning <paramref name="fallback" /> when absent or of the wrong type.</summary>
        public static bool AsBool(object value, bool fallback)
        {
            if (value is bool)
            {
                return (bool)value;
            }

            return fallback;
        }

        /// <summary>Reads an integer, returning <paramref name="fallback" /> when absent or of the wrong type.</summary>
        public static long AsLong(object value, long fallback)
        {
            if (value is bool || value == null)
            {
                return fallback;
            }

            if (value is long)
            {
                return (long)value;
            }

            if (value is double)
            {
                return (long)(double)value;
            }

            long parsed;
            return long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        /// <summary>Reads a floating point value, returning <paramref name="fallback" /> when absent or of the wrong type.</summary>
        public static double AsDouble(object value, double fallback)
        {
            if (value is bool || value == null)
            {
                return fallback;
            }

            if (value is long)
            {
                return (long)value;
            }

            if (value is double)
            {
                return (double)value;
            }

            double parsed;
            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : fallback;
        }

        /// <summary>Reads an array, returning an empty list when absent or of the wrong type.</summary>
        public static List<object> AsList(object value)
        {
            return value as List<object> ?? new List<object>();
        }

        /// <summary>Reads an object, returning an empty dictionary when absent or of the wrong type.</summary>
        public static Dictionary<string, object> AsDict(object value)
        {
            return value as Dictionary<string, object> ?? new Dictionary<string, object>(StringComparer.Ordinal);
        }

        /// <summary>Reads a string list, silently skipping non-string members.</summary>
        public static List<string> AsStringList(object value)
        {
            var result = new List<string>();
            var list = value as List<object>;
            if (list == null)
            {
                return result;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var text = list[i] as string;
                if (text != null)
                {
                    result.Add(text);
                }
            }

            return result;
        }

        // ------------------------------------------------- dictionary convenience
        //
        // Deserialisation code reads hundreds of optional fields. Doing that with
        // `dict.ContainsKey(k) ? dict[k] : null` at every call site is both noisy and easy
        // to get wrong, so these overloads encode the "absent means fallback" contract once.

        /// <summary>Reads <paramref name="key" /> from <paramref name="dict" /> as a string.</summary>
        public static string GetString(Dictionary<string, object> dict, string key, string fallback)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? AsString(value, fallback) : fallback;
        }

        /// <summary>Reads <paramref name="key" /> from <paramref name="dict" /> as a boolean.</summary>
        public static bool GetBool(Dictionary<string, object> dict, string key, bool fallback)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? AsBool(value, fallback) : fallback;
        }

        /// <summary>Reads <paramref name="key" /> from <paramref name="dict" /> as an integer.</summary>
        public static long GetLong(Dictionary<string, object> dict, string key, long fallback)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? AsLong(value, fallback) : fallback;
        }

        /// <summary>Reads <paramref name="key" /> from <paramref name="dict" /> as an integer.</summary>
        public static int GetInt(Dictionary<string, object> dict, string key, int fallback)
        {
            var value = GetLong(dict, key, fallback);
            return value < int.MinValue ? int.MinValue : (value > int.MaxValue ? int.MaxValue : (int)value);
        }

        /// <summary>Reads <paramref name="key" /> from <paramref name="dict" /> as a floating point value.</summary>
        public static double GetDouble(Dictionary<string, object> dict, string key, double fallback)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? AsDouble(value, fallback) : fallback;
        }

        /// <summary>Reads <paramref name="key" /> from <paramref name="dict" /> as an array.</summary>
        public static List<object> GetList(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? AsList(value) : new List<object>();
        }

        /// <summary>Reads <paramref name="key" /> from <paramref name="dict" /> as an object.</summary>
        public static Dictionary<string, object> GetDict(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? AsDict(value) : new Dictionary<string, object>(StringComparer.Ordinal);
        }

        /// <summary>Reads <paramref name="key" /> from <paramref name="dict" /> as a string list.</summary>
        public static List<string> GetStringList(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? AsStringList(value) : new List<string>();
        }

        /// <summary>True when the dictionary contains a non-null value for the key.</summary>
        public static bool Has(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) && value != null;
        }

        // --------------------------------------------------------------- internals

        private sealed class ParserState
        {
            public readonly string Source;
            public int Index;

            public ParserState(string source)
            {
                Source = source;
                Index = 0;
            }

            public int Length
            {
                get { return Source.Length; }
            }

            public bool AtEnd
            {
                get { return Index >= Source.Length; }
            }

            public char Peek()
            {
                return Source[Index];
            }

            public char Next()
            {
                return Source[Index++];
            }

            public void Advance(int count)
            {
                Index += count;
            }

            public bool TryConsume(char expected)
            {
                if (Index < Source.Length && Source[Index] == expected)
                {
                    Index++;
                    return true;
                }

                return false;
            }

            public void Expect(char expected)
            {
                if (TryConsume(expected))
                {
                    return;
                }

                throw new MiniJsonException("Expected '" + expected + "'", Index);
            }

            public void ExpectLiteral(string literal)
            {
                if (Index + literal.Length > Source.Length ||
                    string.CompareOrdinal(Source, Index, literal, 0, literal.Length) != 0)
                {
                    throw new MiniJsonException("Expected literal '" + literal + "'", Index);
                }

                Index += literal.Length;
            }

            public void SkipWhitespace()
            {
                while (Index < Source.Length)
                {
                    var c = Source[Index];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    {
                        Index++;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
    }
}
