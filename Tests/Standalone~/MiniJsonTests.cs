using System;
using System.Collections.Generic;
using AvatarTuner.Core;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Tests for the dependency free JSON reader/writer. The manifest and every translation
    /// file go through this code, so its behaviour is pinned down thoroughly here.
    /// </summary>
    public static class MiniJsonTests
    {
        [Test("parses a flat object with all scalar types")]
        public static void ParsesScalars()
        {
            var result = MiniJson.ParseObject("{\"s\":\"text\",\"i\":42,\"neg\":-7,\"f\":1.5,\"t\":true,\"f2\":false,\"n\":null}");

            Assert.AreEqual("text", MiniJson.AsString(result["s"], null), "string value");
            Assert.AreEqual(42L, MiniJson.AsLong(result["i"], 0), "integer value is preserved as long");
            Assert.AreEqual(-7L, MiniJson.AsLong(result["neg"], 0), "negative integer");
            Assert.AreEqual(1.5, MiniJson.AsDouble(result["f"], 0), "floating value");
            Assert.AreEqual(true, MiniJson.AsBool(result["t"], false), "true literal");
            Assert.AreEqual(false, MiniJson.AsBool(result["f2"], true), "false literal");
            Assert.IsNull(result["n"], "null literal");
        }

        [Test("integers survive a round trip as integers")]
        public static void IntegersRoundTrip()
        {
            var parsed = MiniJson.ParseObject("{\"schemaVersion\":1,\"count\":0,\"big\":9007199254740993}");
            Assert.AreEqual(1L, parsed["schemaVersion"], "schemaVersion stays integral");
            Assert.AreEqual(0L, parsed["count"], "zero stays integral");

            var reprinted = MiniJson.Serialize(parsed);
            Assert.Contains("9007199254740993", reprinted, "64-bit integers are not widened to double and lost");
            Assert.Contains("\"schemaVersion\":1", reprinted, "no decimal point is introduced");
        }

        [Test("parses nested objects and arrays")]
        public static void ParsesNesting()
        {
            var root = MiniJson.ParseObject("{\"a\":[1,[2,3],{\"b\":\"c\"}],\"d\":{\"e\":{\"f\":[]}}}");

            var a = MiniJson.AsList(root["a"]);
            Assert.AreEqual(3, a.Count, "outer array length");
            Assert.AreEqual(1L, a[0], "first element");

            var inner = MiniJson.AsList(a[1]);
            Assert.AreEqual(2, inner.Count, "nested array length");
            Assert.AreEqual(3L, inner[1], "nested array element");

            var b = MiniJson.AsDict(a[2]);
            Assert.AreEqual("c", MiniJson.AsString(b["b"], null), "object inside array");

            var d = MiniJson.AsDict(MiniJson.AsDict(root["d"])["e"]);
            Assert.AreEqual(0, MiniJson.AsList(d["f"]).Count, "empty nested array");
        }

        [Test("parses empty object and empty array")]
        public static void ParsesEmptyContainers()
        {
            Assert.AreEqual(0, MiniJson.ParseObject("{}").Count, "empty object");
            Assert.AreEqual(0, MiniJson.ParseArray("[]").Count, "empty array");
            Assert.AreEqual(0, MiniJson.ParseObject("{ }").Count, "whitespace only object");
            Assert.AreEqual(0, MiniJson.ParseArray("[  ]").Count, "whitespace only array");
        }

        [Test("decodes every escape sequence")]
        public static void DecodesEscapes()
        {
            var result = MiniJson.ParseObject("{\"a\":\"quote\\\" back\\\\ slash\\/ tab\\t nl\\n cr\\r bs\\b ff\\f uni\\u0041\"}");
            var text = MiniJson.AsString(result["a"], null);

            Assert.AreEqual("quote\" back\\ slash/ tab\t nl\n cr\r bs\b ff\f uniA", text, "escape decoding");
        }

        [Test("encodes control characters and quotes safely")]
        public static void EncodesEscapes()
        {
            var value = new Dictionary<string, object>();
            value["text"] = "line1\nline2\t\"quoted\"\\\u0001";

            var json = MiniJson.Serialize(value);
            Assert.Contains("\\n", json, "newline is escaped");
            Assert.Contains("\\t", json, "tab is escaped");
            Assert.Contains("\\\"", json, "quote is escaped");
            Assert.Contains("\\\\", json, "backslash is escaped");
            Assert.Contains("\\u0001", json, "control character is escaped as \\u");

            var back = MiniJson.ParseObject(json);
            Assert.AreEqual("line1\nline2\t\"quoted\"\\\u0001", MiniJson.AsString(back["text"], null), "round trip");
        }

        [Test("round trips a complex document through pretty and compact forms")]
        public static void RoundTripsComplexDocument()
        {
            var original = "{\"name\":\"Avatar\",\"n\":3,\"list\":[1,2,3],\"nested\":{\"k\":true,\"x\":null}}";

            var compact = MiniJson.Serialize(MiniJson.Parse(original), false);
            var pretty = MiniJson.Serialize(MiniJson.Parse(original), true);
            var fromPretty = MiniJson.Serialize(MiniJson.Parse(pretty), false);

            Assert.AreEqual(compact, fromPretty, "pretty printing does not change the data");
            Assert.Contains("\n", pretty, "pretty output is indented");
        }

        [Test("rejects malformed input with a position")]
        public static void RejectsMalformedInput()
        {
            Assert.Throws<MiniJsonException>(delegate { MiniJson.Parse("{\"a\":}"); }, "missing value");
            Assert.Throws<MiniJsonException>(delegate { MiniJson.Parse("{\"a\":1"); }, "unterminated object");
            Assert.Throws<MiniJsonException>(delegate { MiniJson.Parse("{\"a\" 1}"); }, "missing colon");
            Assert.Throws<MiniJsonException>(delegate { MiniJson.Parse("{a:1}"); }, "unquoted key");
            Assert.Throws<MiniJsonException>(delegate { MiniJson.Parse("[1,]"); }, "trailing comma in array");
            Assert.Throws<MiniJsonException>(delegate { MiniJson.Parse("\"abc"); }, "unterminated string");
            Assert.Throws<MiniJsonException>(delegate { MiniJson.Parse("{\"a\":1}extra"); }, "trailing content");
            Assert.Throws<MiniJsonException>(delegate { MiniJson.Parse("01x"); }, "garbage");
            Assert.Throws<MiniJsonException>(delegate { MiniJson.Parse(""); }, "empty document");
        }

        [Test("reports the offset of a parse failure")]
        public static void ReportsPosition()
        {
            try
            {
                MiniJson.Parse("{\"ok\":1,}");
                Assert.Fail("expected a MiniJsonException");
            }
            catch (MiniJsonException ex)
            {
                Assert.IsTrue(ex.Position >= 8, "position points at or after the offending character, got " + ex.Position);
                Assert.Contains("offset", ex.Message, "message mentions the offset");
            }
        }

        [Test("typed accessors fall back instead of throwing")]
        public static void TypedAccessorsAreForgiving()
        {
            var dict = MiniJson.ParseObject("{\"a\":\"x\",\"b\":5,\"c\":[1],\"d\":{}}");

            Assert.AreEqual("def", MiniJson.GetString(dict, "missing", "def"), "missing string");
            Assert.AreEqual("def", MiniJson.AsString(null, "def"), "null string");
            Assert.AreEqual(9L, MiniJson.GetLong(dict, "a", 9L), "wrong type long");
            Assert.AreEqual(9L, MiniJson.GetLong(dict, "missing", 9L), "missing long");
            Assert.AreEqual(2.5, MiniJson.GetDouble(dict, "a", 2.5), "wrong type double");
            Assert.AreEqual(5.0, MiniJson.GetDouble(dict, "b", 0), "long read as double");
            Assert.AreEqual(true, MiniJson.GetBool(dict, "missing", true), "missing bool");
            Assert.AreEqual(5, MiniJson.GetInt(dict, "b", 0), "int read from long");
            Assert.AreEqual(0, MiniJson.GetList(dict, "a").Count, "wrong type list");
            Assert.AreEqual(1, MiniJson.GetList(dict, "c").Count, "real list");
            Assert.AreEqual(0, MiniJson.GetDict(dict, "a").Count, "wrong type dict");
            Assert.AreEqual(0, MiniJson.GetDict(dict, "d").Count, "real dict");
            Assert.AreEqual(0, MiniJson.GetStringList(dict, "missing").Count, "missing string list");
            Assert.IsFalse(MiniJson.Has(dict, "missing"), "Has on a missing key");
            Assert.IsTrue(MiniJson.Has(dict, "a"), "Has on a present key");
            Assert.IsFalse(MiniJson.Has(null, "a"), "Has on a null dictionary");
        }

        [Test("AsStringList skips non string members")]
        public static void AsStringListSkipsNonStrings()
        {
            var dict = MiniJson.ParseObject("{\"a\":[\"one\",2,\"three\",null]}");
            var list = MiniJson.AsStringList(dict["a"]);

            Assert.AreEqual(2, list.Count, "only strings are collected");
            Assert.AreEqual("one", list[0], "first string");
            Assert.AreEqual("three", list[1], "second string");
        }

        [Test("refuses to serialize non finite numbers")]
        public static void RefusesNonFiniteNumbers()
        {
            var dict = new Dictionary<string, object>();
            dict["bad"] = double.NaN;
            Assert.Throws<MiniJsonException>(delegate { MiniJson.Serialize(dict); }, "NaN must not be written");

            dict["bad"] = double.PositiveInfinity;
            Assert.Throws<MiniJsonException>(delegate { MiniJson.Serialize(dict); }, "Infinity must not be written");
        }

        [Test("unicode survives a round trip including surrogate pairs")]
        public static void UnicodeRoundTrips()
        {
            var value = new Dictionary<string, object>();
            value["thai"] = "ภาษาไทย";
            value["chinese"] = "简体中文";
            value["emoji"] = "\U0001F600";

            var json = MiniJson.Serialize(value);
            var back = MiniJson.ParseObject(json);

            Assert.AreEqual(value["thai"], back["thai"], "Thai round trip");
            Assert.AreEqual(value["chinese"], back["chinese"], "Chinese round trip");
            Assert.AreEqual(value["emoji"], back["emoji"], "surrogate pair round trip");
        }

        [Test("duplicate keys keep the last occurrence")]
        public static void DuplicateKeysLastWins()
        {
            var result = MiniJson.ParseObject("{\"k\":1,\"k\":2}");
            Assert.AreEqual(2L, result["k"], "later value wins");
            Assert.AreEqual(1, result.Count, "duplicate key is not duplicated in the map");
        }

        [Test("numbers in exponent form parse correctly")]
        public static void ParsesExponents()
        {
            var result = MiniJson.ParseObject("{\"a\":1e3,\"b\":1.5e-2,\"c\":-2E+2}");
            Assert.AreEqual(1000.0, MiniJson.AsDouble(result["a"], 0), "1e3");
            Assert.AreEqual(0.015, MiniJson.AsDouble(result["b"], 0), "1.5e-2");
            Assert.AreEqual(-200.0, MiniJson.AsDouble(result["c"], 0), "-2E+2");
        }
    }
}
