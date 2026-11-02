using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AvatarTuner.Core;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Locates the package root and the shipped translation files, so tests can validate the
    /// real files that Unity will load rather than copies of them.
    /// </summary>
    public static class PackageLayout
    {
        private static string _root;

        /// <summary>Walks up from the test binary until it finds package.json.</summary>
        public static string Root
        {
            get
            {
                if (_root != null)
                {
                    return _root;
                }

                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "package.json")))
                    {
                        _root = dir.FullName;
                        return _root;
                    }

                    dir = dir.Parent;
                }

                throw new InvalidOperationException(
                    "Could not locate the AvatarTuner package root from " + AppContext.BaseDirectory);
            }
        }

        public static string TranslationsDir
        {
            get { return Path.Combine(Root, "Editor", "Localization", "Translations"); }
        }

        public static string TranslationPath(string languageCode)
        {
            return Path.Combine(TranslationsDir, languageCode + ".json");
        }

        /// <summary>All C# sources in the package that can reference translation keys.</summary>
        public static List<string> SourceFiles()
        {
            var files = new List<string>();
            Collect(new DirectoryInfo(Path.Combine(Root, "Core")), files);
            Collect(new DirectoryInfo(Path.Combine(Root, "Editor")), files);
            Collect(new DirectoryInfo(Path.Combine(Root, "Tests", "Editor")), files);
            return files;
        }

        private static void Collect(DirectoryInfo dir, List<string> files)
        {
            if (!dir.Exists)
            {
                return;
            }

            foreach (var file in dir.GetFiles("*.cs", SearchOption.AllDirectories))
            {
                files.Add(file.FullName);
            }
        }
    }

    /// <summary>
    /// Automated translation verification.
    ///
    /// Three independent guarantees are checked here:
    ///   1. The three shipped files agree on the key set, with no blanks and matching
    ///      placeholders.
    ///   2. Every key literal that appears in the C# sources exists in every language.
    ///   3. Keys that are assembled at runtime (preset names) also exist in every language.
    ///
    /// Point 2 is what stops a new UI label from silently rendering as a raw key in Thai or
    /// Chinese while looking fine in English.
    /// </summary>
    public static class LocalizationTests
    {
        private static TranslationCatalog Load(string code)
        {
            var path = PackageLayout.TranslationPath(code);
            Assert.IsTrue(File.Exists(path), "the translation file for '" + code + "' exists at " + path);
            return TranslationCatalog.FromJson(File.ReadAllText(path, Encoding.UTF8), code);
        }

        [Test("all three shipped translation files are complete and consistent")]
        public static void TranslationFilesAreConsistent()
        {
            var english = Load("en");
            var thai = Load("th");
            var chinese = Load("zh-Hans");

            Assert.IsTrue(english.Count > 200, "the English catalog is substantial, got " + english.Count + " keys");
            Assert.IsTrue(english.IsFallback, "English is the fallback language");

            var report = TranslationValidator.Validate(english, thai, chinese);
            Assert.AreEqual(3, report.CheckedLanguageCount, "three languages are checked");
            Assert.IsTrue(report.IsValid,
                "translation validation failed with " + report.ErrorCount + " error(s): " + Describe(report));
        }

        [Test("every language has exactly the same key set")]
        public static void KeySetsMatchExactly()
        {
            var english = Load("en");
            var thai = Load("th");
            var chinese = Load("zh-Hans");

            var englishKeys = new HashSet<string>(english.SortedKeys(), StringComparer.Ordinal);
            Assert.IsTrue(englishKeys.SetEquals(new HashSet<string>(thai.SortedKeys(), StringComparer.Ordinal)),
                "Thai key set differs from English");
            Assert.IsTrue(englishKeys.SetEquals(new HashSet<string>(chinese.SortedKeys(), StringComparer.Ordinal)),
                "Chinese key set differs from English");
            Assert.AreEqual(english.Count, thai.Count, "Thai has the same number of keys");
            Assert.AreEqual(english.Count, chinese.Count, "Chinese has the same number of keys");
        }

        [Test("no translation is blank or whitespace")]
        public static void NoBlankTranslations()
        {
            var catalog = Load("en");
            foreach (var code in new[] { "en", "th", "zh-Hans" })
            {
                var current = Load(code);
                foreach (var key in current.SortedKeys())
                {
                    var value = current.GetRaw(key);
                    Assert.IsFalse(string.IsNullOrEmpty(value) || value.Trim().Length == 0,
                        code + " has a blank value for '" + key + "'");
                }
            }

            Assert.IsTrue(catalog.Count > 0, "the catalog is not empty");
        }

        [Test("non-Latin scripts are really present in Thai and Chinese")]
        public static void NonLatinScriptsArePresent()
        {
            var thai = Load("th");
            var chinese = Load("zh-Hans");

            var thaiChars = 0;
            var chineseChars = 0;
            foreach (var key in thai.SortedKeys())
            {
                foreach (var c in thai.GetRaw(key))
                {
                    if (c >= 0x0E00 && c <= 0x0E7F)
                    {
                        thaiChars++;
                    }
                }
            }

            foreach (var key in chinese.SortedKeys())
            {
                foreach (var c in chinese.GetRaw(key))
                {
                    if (c >= 0x4E00 && c <= 0x9FFF)
                    {
                        chineseChars++;
                    }
                }
            }

            Assert.IsGreater(500, thaiChars, "the Thai translation contains real Thai script, found " + thaiChars + " characters");
            Assert.IsGreater(500, chineseChars, "the Chinese translation contains real Han characters, found " + chineseChars + " characters");
        }

        [Test("placeholders match across every language")]
        public static void PlaceholdersMatch()
        {
            var english = Load("en");
            var thai = Load("th");
            var chinese = Load("zh-Hans");

            foreach (var key in english.SortedKeys())
            {
                var expected = TranslationValidator.ExtractPlaceholders(english.GetRaw(key));

                foreach (var pair in new[] { new[] { "th", thai.GetRaw(key) }, new[] { "zh-Hans", chinese.GetRaw(key) } })
                {
                    var actual = TranslationValidator.ExtractPlaceholders(pair[1]);
                    Assert.AreEqual(
                        string.Join(",", expected.ToArray()),
                        string.Join(",", actual.ToArray()),
                        pair[0] + " placeholders for '" + key + "'");
                }
            }
        }

        [Test("every translation key referenced by the C# sources exists in every language")]
        public static void SourceKeysAreAllTranslated()
        {
            var english = Load("en");
            var thai = Load("th");
            var chinese = Load("zh-Hans");

            var referenced = new SortedSet<string>(StringComparer.Ordinal);
            var files = PackageLayout.SourceFiles();
            Assert.IsGreater(5, files.Count, "the package has sources to scan, found " + files.Count + " file(s)");

            foreach (var file in files)
            {
                foreach (var literal in ExtractStringLiterals(File.ReadAllText(file, Encoding.UTF8)))
                {
                    if (TranslationValidator.LooksLikeTranslationKey(literal))
                    {
                        referenced.Add(literal);
                    }
                }
            }

            Assert.IsGreater(50, referenced.Count,
                "the source scanner found translation keys, found " + referenced.Count);

            var missing = new List<string>();
            foreach (var key in referenced)
            {
                if (!english.Contains(key))
                {
                    missing.Add("en:" + key);
                }
                else if (!thai.Contains(key))
                {
                    missing.Add("th:" + key);
                }
                else if (!chinese.Contains(key))
                {
                    missing.Add("zh-Hans:" + key);
                }
            }

            Assert.AreEqual(0, missing.Count,
                "every key used by the code must exist in every translation file. Missing: " +
                string.Join(", ", missing.ToArray()));
        }

        [Test("dynamically assembled keys are also translated")]
        public static void DynamicKeysAreTranslated()
        {
            var english = Load("en");
            var thai = Load("th");
            var chinese = Load("zh-Hans");

            var missing = new List<string>();
            foreach (var preset in PresetCatalog.All)
            {
                foreach (var key in new[] { PresetCatalog.GetDisplayNameKey(preset), PresetCatalog.GetDescriptionKey(preset) })
                {
                    if (!english.Contains(key))
                    {
                        missing.Add("en:" + key);
                    }

                    if (!thai.Contains(key))
                    {
                        missing.Add("th:" + key);
                    }

                    if (!chinese.Contains(key))
                    {
                        missing.Add("zh-Hans:" + key);
                    }
                }
            }

            // Status names and summaries are assembled the same way.
            foreach (var status in new[]
                     {
                         OptimizationStatus.NotOptimized, OptimizationStatus.Optimized,
                         OptimizationStatus.ChangesDetected, OptimizationStatus.ReoptimizationRequired,
                         OptimizationStatus.OptimizationFailed, OptimizationStatus.Unsupported
                     })
            {
                var id = statusId(status);
                foreach (var key in new[] { "status." + id + ".name", "status." + id + ".summary" })
                {
                    if (!english.Contains(key))
                    {
                        missing.Add("en:" + key);
                    }

                    if (!thai.Contains(key))
                    {
                        missing.Add("th:" + key);
                    }

                    if (!chinese.Contains(key))
                    {
                        missing.Add("zh-Hans:" + key);
                    }
                }
            }

            Assert.AreEqual(0, missing.Count,
                "dynamically assembled keys must exist in every language. Missing: " + string.Join(", ", missing.ToArray()));
        }

        [Test("the resolver falls back to English when a key is missing")]
        public static void ResolverFallsBackToEnglish()
        {
            var english = Load("en");
            var incomplete = new TranslationCatalog("zz", "Test");
            incomplete.Set("ui.button.close", "Fermer");

            var resolver = new TranslationResolver(incomplete, english);

            Assert.AreEqual("Fermer", resolver.Resolve("ui.button.close", null), "the target language wins when it has the key");
            Assert.AreEqual(english.GetRaw("ui.button.analyze"), resolver.Resolve("ui.button.analyze", null),
                "a missing key falls back to English rather than leaking a raw key");
            Assert.AreEqual("fallback-key", resolver.Resolve("no.such.key", "fallback-key"),
                "a key missing everywhere falls back to the supplied default");
            Assert.IsTrue(resolver.CanResolve("ui.button.close"), "the resolver reports what it can answer");
            Assert.IsFalse(resolver.CanResolve("no.such.key"), "the resolver reports what it cannot answer");
        }

        [Test("placeholder substitution handles braces and missing arguments")]
        public static void PlaceholderSubstitution()
        {
            Assert.AreEqual("a b c", TranslationCatalog.Format("a {0} c", new List<string> { "b" }), "simple substitution");
            Assert.AreEqual("1 then 2", TranslationCatalog.Format("{0} then {1}", new List<string> { "1", "2" }), "two arguments");
            Assert.AreEqual("keep {0}", TranslationCatalog.Format("keep {0}", new List<string>()), "an unused placeholder is left visible");
            Assert.AreEqual("out of range {2}", TranslationCatalog.Format("out of range {2}", new List<string> { "x" }),
                "an out of range placeholder is left visible");
            Assert.AreEqual("{literal}", TranslationCatalog.Format("{{literal}}", new List<string>()), "doubled braces are literal");
            Assert.AreEqual(string.Empty, TranslationCatalog.Format(null, null), "null template renders empty");
            Assert.AreEqual("no args", TranslationCatalog.Format("no args", null), "null arguments are safe");
        }

        [Test("catalogs round trip through their own serializer")]
        public static void CatalogRoundTrips()
        {
            var original = Load("th");
            var json = original.ToJson(true);
            var restored = TranslationCatalog.FromJson(json, "th");

            Assert.AreEqual(original.Count, restored.Count, "the key count survives a round trip");
            Assert.AreEqual(original.LanguageCode, restored.LanguageCode, "the language code survives");
            Assert.AreEqual(original.DisplayName, restored.DisplayName, "the display name survives");
            Assert.AreEqual(original.GetRaw("ui.button.close"), restored.GetRaw("ui.button.close"), "a sample value survives");
        }

        [Test("key shape detection is precise")]
        public static void KeyShapeDetection()
        {
            Assert.IsTrue(TranslationValidator.LooksLikeTranslationKey("ui.button.close"), "a real key is recognised");
            Assert.IsTrue(TranslationValidator.LooksLikeTranslationKey("plan.warning.alphaLost"), "camel case segments are fine");
            Assert.IsTrue(TranslationValidator.LooksLikeTranslationKey("preset.size-first.name"), "kebab case segments are fine");

            Assert.IsFalse(TranslationValidator.LooksLikeTranslationKey("Assets/Tex/a.png"), "a file path is not a key");
            Assert.IsFalse(TranslationValidator.LooksLikeTranslationKey("_MainTex"), "a shader property is not a key");
            Assert.IsFalse(TranslationValidator.LooksLikeTranslationKey("UnityEditor"), "a type name is not a key");
            Assert.IsFalse(TranslationValidator.LooksLikeTranslationKey("preset."), "an incomplete key is not a key");
            Assert.IsFalse(TranslationValidator.LooksLikeTranslationKey(""), "the empty string is not a key");
            Assert.IsTrue(TranslationValidator.LooksLikeTranslationKey("ui.button"), "two segments in a known namespace are a key");
            Assert.IsFalse(TranslationValidator.LooksLikeTranslationKey("ui.Button.close"), "segments must start with a lowercase letter");
            Assert.IsFalse(TranslationValidator.LooksLikeTranslationKey("ui..close"), "empty segments are rejected");
        }

        // ------------------------------------------------------------------ helpers

        private static string statusId(OptimizationStatus status)
        {
            switch (status)
            {
                case OptimizationStatus.NotOptimized:
                    return "notOptimized";
                case OptimizationStatus.Optimized:
                    return "optimized";
                case OptimizationStatus.ChangesDetected:
                    return "changesDetected";
                case OptimizationStatus.ReoptimizationRequired:
                    return "reoptimizationRequired";
                case OptimizationStatus.OptimizationFailed:
                    return "optimizationFailed";
                case OptimizationStatus.Unsupported:
                    return "unsupported";
                default:
                    return "notOptimized";
            }
        }

        private static string Describe(TranslationValidationReport report)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < report.Issues.Count && i < 12; i++)
            {
                sb.Append(report.Issues[i].ToString());
                sb.Append("; ");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Extracts every string literal from a C# source file, handling the common escape
        /// sequences and skipping comments and verbatim strings.
        /// </summary>
        public static List<string> ExtractStringLiterals(string source)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(source))
            {
                return results;
            }

            var index = 0;
            var length = source.Length;

            while (index < length)
            {
                var c = source[index];

                // Line comment.
                if (c == '/' && index + 1 < length && source[index + 1] == '/')
                {
                    while (index < length && source[index] != '\n')
                    {
                        index++;
                    }

                    continue;
                }

                // Block comment.
                if (c == '/' && index + 1 < length && source[index + 1] == '*')
                {
                    index += 2;
                    while (index + 1 < length && !(source[index] == '*' && source[index + 1] == '/'))
                    {
                        index++;
                    }

                    index += 2;
                    continue;
                }

                // Verbatim string: skip, its contents are not key material.
                if (c == '@' && index + 1 < length && source[index + 1] == '"')
                {
                    index += 2;
                    while (index < length)
                    {
                        if (source[index] == '"')
                        {
                            if (index + 1 < length && source[index + 1] == '"')
                            {
                                index += 2;
                                continue;
                            }

                            index++;
                            break;
                        }

                        index++;
                    }

                    continue;
                }

                // Interpolated string: treat the literal text as ordinary content and skip
                // the hole expressions, which never contain translation keys.
                if (c == '$' && index + 1 < length && source[index + 1] == '"')
                {
                    index += 2;
                    var sb = new StringBuilder();
                    while (index < length)
                    {
                        if (source[index] == '\\')
                        {
                            index += 2;
                            continue;
                        }

                        if (source[index] == '{')
                        {
                            var depth = 1;
                            index++;
                            while (index < length && depth > 0)
                            {
                                if (source[index] == '{')
                                {
                                    depth++;
                                }
                                else if (source[index] == '}')
                                {
                                    depth--;
                                }

                                index++;
                            }

                            continue;
                        }

                        if (source[index] == '"')
                        {
                            index++;
                            break;
                        }

                        sb.Append(source[index]);
                        index++;
                    }

                    AddIfUseful(results, sb.ToString());
                    continue;
                }

                // Ordinary string literal.
                if (c == '"')
                {
                    index++;
                    var sb = new StringBuilder();
                    while (index < length)
                    {
                        var ch = source[index];
                        if (ch == '\\')
                        {
                            if (index + 1 < length)
                            {
                                var next = source[index + 1];
                                switch (next)
                                {
                                    case 'n':
                                        sb.Append('\n');
                                        break;
                                    case 'r':
                                        sb.Append('\r');
                                        break;
                                    case 't':
                                        sb.Append('\t');
                                        break;
                                    case '\\':
                                        sb.Append('\\');
                                        break;
                                    case '"':
                                        sb.Append('"');
                                        break;
                                    case 'u':
                                        index += 4;
                                        sb.Append('?');
                                        break;
                                    default:
                                        sb.Append(next);
                                        break;
                                }
                            }

                            index += 2;
                            continue;
                        }

                        if (ch == '"')
                        {
                            index++;
                            break;
                        }

                        sb.Append(ch);
                        index++;
                    }

                    AddIfUseful(results, sb.ToString());
                    continue;
                }

                index++;
            }

            return results;
        }

        private static void AddIfUseful(List<string> results, string literal)
        {
            if (!string.IsNullOrEmpty(literal) && literal.Length >= 3)
            {
                results.Add(literal);
            }
        }
    }
}
