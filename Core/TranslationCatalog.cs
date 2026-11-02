using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AvatarTuner.Core
{
    /// <summary>The three languages shipped in v1.0.0.</summary>
    public enum InterfaceLanguage
    {
        /// <summary>English - the default and the fallback language.</summary>
        English = 0,

        /// <summary>Thai.</summary>
        Thai = 1,

        /// <summary>Simplified Chinese.</summary>
        SimplifiedChinese = 2
    }

    /// <summary>Metadata and lookup for one language's translation table.</summary>
    public sealed class TranslationCatalog
    {
        private readonly Dictionary<string, string> _entries =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public string LanguageCode = "en";
        public string DisplayName = "English";

        /// <summary>True for English. English is the fallback and must always be complete.</summary>
        public bool IsFallback;

        public TranslationCatalog()
        {
        }

        public TranslationCatalog(string languageCode, string displayName)
        {
            LanguageCode = languageCode;
            DisplayName = displayName;
        }

        public int Count
        {
            get { return _entries.Count; }
        }

        public IEnumerable<string> Keys
        {
            get { return _entries.Keys; }
        }

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("key must not be empty", "key");
            }

            _entries[key] = value;
        }

        public bool Contains(string key)
        {
            return !string.IsNullOrEmpty(key) && _entries.ContainsKey(key);
        }

        /// <summary>Returns the translation, or null when the key is absent.</summary>
        public string GetRaw(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            string value;
            return _entries.TryGetValue(key, out value) ? value : null;
        }

        /// <summary>
        /// Returns the translation, or <paramref name="fallback" /> when absent. Never
        /// returns null so that callers can render text unconditionally.
        /// </summary>
        public string Get(string key, string fallback)
        {
            var value = GetRaw(key);
            return value ?? fallback;
        }

        public List<string> SortedKeys()
        {
            var keys = new List<string>(_entries.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        /// <summary>
        /// Parses a translation file. Two shapes are accepted:
        ///   { "entries": { "key": "value", ... }, ... }   (preferred, carries metadata)
        ///   { "key": "value", ... }                        (flat, metadata-free)
        /// Unknown top-level scalar keys are ignored in the first shape so metadata can grow.
        /// </summary>
        public static TranslationCatalog FromJson(string json, string fallbackLanguageCode)
        {
            if (json == null)
            {
                throw new ArgumentNullException("json");
            }

            var root = MiniJson.ParseObject(json);
            var catalog = new TranslationCatalog();

            object entriesObject;
            Dictionary<string, object> entries;
            if (root.TryGetValue("entries", out entriesObject))
            {
                entries = MiniJson.AsDict(entriesObject);
                catalog.LanguageCode = MiniJson.GetString(root, "languageCode", fallbackLanguageCode ?? "en");
                catalog.DisplayName = MiniJson.GetString(root, "displayName", catalog.LanguageCode);
            }
            else
            {
                entries = root;
                catalog.LanguageCode = fallbackLanguageCode ?? "en";
                catalog.DisplayName = catalog.LanguageCode;
            }

            foreach (var pair in entries)
            {
                var text = pair.Value as string;
                if (text != null)
                {
                    catalog._entries[pair.Key] = text;
                }
            }

            catalog.IsFallback = string.Equals(catalog.LanguageCode, "en", StringComparison.OrdinalIgnoreCase);
            return catalog;
        }

        /// <summary>Serializes the catalog in the preferred shape.</summary>
        public string ToJson(bool pretty)
        {
            var entries = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in _entries)
            {
                entries[pair.Key] = pair.Value;
            }

            var root = new Dictionary<string, object>(StringComparer.Ordinal);
            root["schemaVersion"] = 1L;
            root["languageCode"] = LanguageCode ?? "en";
            root["displayName"] = DisplayName ?? string.Empty;
            root["entries"] = entries;
            return MiniJson.Serialize(root, pretty);
        }

        /// <summary>
        /// Interpolates <c>{0}</c>, <c>{1}</c> ... placeholders. Doubled braces
        /// (<c>{{</c> and <c>}}</c>) are literal braces. Unknown placeholders are left
        /// untouched so a missing argument is visible rather than silently blank.
        /// </summary>
        public static string Format(string template, IList<string> args)
        {
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            if (args == null || args.Count == 0)
            {
                return UnescapeBraces(template);
            }

            var sb = new StringBuilder(template.Length + 16);
            for (var i = 0; i < template.Length; i++)
            {
                var c = template[i];

                if (c == '{')
                {
                    if (i + 1 < template.Length && template[i + 1] == '{')
                    {
                        sb.Append('{');
                        i++;
                        continue;
                    }

                    var close = template.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        var inner = template.Substring(i + 1, close - i - 1);
                        int index;
                        if (int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) &&
                            index >= 0 && index < args.Count)
                        {
                            sb.Append(args[index] ?? string.Empty);
                            i = close;
                            continue;
                        }
                    }

                    sb.Append(c);
                    continue;
                }

                if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
                {
                    sb.Append('}');
                    i++;
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static string UnescapeBraces(string template)
        {
            if (template.IndexOf("{{", StringComparison.Ordinal) < 0 &&
                template.IndexOf("}}", StringComparison.Ordinal) < 0)
            {
                return template;
            }

            // Standalone single pass. Must NOT delegate back to Format(), because
            // Format() routes the empty-argument case here and would recurse forever.
            var sb = new StringBuilder(template.Length);
            for (var i = 0; i < template.Length; i++)
            {
                var c = template[i];
                if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
                {
                    sb.Append('{');
                    i++;
                    continue;
                }

                if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
                {
                    sb.Append('}');
                    i++;
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Resolves keys with an explicit fallback chain. The chain always ends at English so a
    /// missing Thai or Chinese string degrades to a readable English sentence instead of a
    /// raw key leaking into the UI.
    /// </summary>
    public sealed class TranslationResolver
    {
        private readonly List<TranslationCatalog> _chain = new List<TranslationCatalog>();

        /// <summary>
        /// Builds a resolver. The first catalog is consulted first; the last should be the
        /// English fallback. Caches nothing so that switching language takes effect at once.
        /// </summary>
        public TranslationResolver(params TranslationCatalog[] catalogs)
        {
            if (catalogs != null)
            {
                for (var i = 0; i < catalogs.Length; i++)
                {
                    if (catalogs[i] != null)
                    {
                        _chain.Add(catalogs[i]);
                    }
                }
            }
        }

        /// <summary>Resolves a key, or returns <paramref name="finalFallback" />.</summary>
        public string Resolve(string key, string finalFallback)
        {
            for (var i = 0; i < _chain.Count; i++)
            {
                var value = _chain[i].GetRaw(key);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return finalFallback ?? key;
        }

        /// <summary>Resolves a key and interpolates arguments.</summary>
        public string Resolve(string key, IList<string> args, string finalFallback)
        {
            return TranslationCatalog.Format(Resolve(key, finalFallback), args);
        }

        /// <summary>True when at least one catalog in the chain can answer the key.</summary>
        public bool CanResolve(string key)
        {
            for (var i = 0; i < _chain.Count; i++)
            {
                if (!string.IsNullOrEmpty(_chain[i].GetRaw(key)))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>One problem found in a translation catalog.</summary>
    public sealed class TranslationIssue
    {
        public enum Severity
        {
            Error = 0,
            Warning = 1
        }

        public Severity Level;
        public string Key;
        public string LanguageCode;
        public string MessageKey;
        public readonly List<string> Args = new List<string>();

        public TranslationIssue(Severity level, string languageCode, string key, string messageKey, params string[] args)
        {
            Level = level;
            LanguageCode = languageCode;
            Key = key;
            MessageKey = messageKey;
            if (args != null)
            {
                Args.AddRange(args);
            }
        }

        public override string ToString()
        {
            return Level + " [" + LanguageCode + "] " + Key + ": " + MessageKey;
        }
    }

    /// <summary>Result of validating the translation set.</summary>
    public sealed class TranslationValidationReport
    {
        public readonly List<TranslationIssue> Issues = new List<TranslationIssue>();
        public int CheckedKeyCount;
        public int CheckedLanguageCount;

        public bool IsValid
        {
            get
            {
                for (var i = 0; i < Issues.Count; i++)
                {
                    if (Issues[i].Level == TranslationIssue.Severity.Error)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public int ErrorCount
        {
            get { return CountBy(TranslationIssue.Severity.Error); }
        }

        public int WarningCount
        {
            get { return CountBy(TranslationIssue.Severity.Warning); }
        }

        private int CountBy(TranslationIssue.Severity severity)
        {
            var count = 0;
            for (var i = 0; i < Issues.Count; i++)
            {
                if (Issues[i].Level == severity)
                {
                    count++;
                }
            }

            return count;
        }

        public void Add(TranslationIssue issue)
        {
            if (issue != null)
            {
                Issues.Add(issue);
            }
        }
    }

    /// <summary>
    /// Verifies that the translation set is complete and internally consistent.
    ///
    /// Checks performed:
    ///  1. English must contain every key present in any other language.
    ///  2. No language may leave a key blank or whitespace only.
    ///  3. Every language must contain every key English defines (no missing translations).
    ///  4. Placeholder sets must match: a translation may not drop or invent <c>{n}</c>.
    ///  5. A language must not contain keys English does not define (usually a typo).
    /// </summary>
    public static class TranslationValidator
    {
        public static TranslationValidationReport Validate(TranslationCatalog fallback, params TranslationCatalog[] others)
        {
            if (fallback == null)
            {
                throw new ArgumentNullException("fallback");
            }

            var report = new TranslationValidationReport();
            var all = new List<TranslationCatalog>();
            all.Add(fallback);
            if (others != null)
            {
                for (var i = 0; i < others.Length; i++)
                {
                    if (others[i] != null)
                    {
                        all.Add(others[i]);
                    }
                }
            }

            report.CheckedLanguageCount = all.Count;
            report.CheckedKeyCount = fallback.Count;

            // Every key anywhere must exist in the fallback catalog.
            for (var i = 1; i < all.Count; i++)
            {
                foreach (var key in all[i].Keys)
                {
                    if (!fallback.Contains(key))
                    {
                        report.Add(new TranslationIssue(
                            TranslationIssue.Severity.Error,
                            all[i].LanguageCode,
                            key,
                            "translation.error.notInFallback"));
                    }
                }
            }

            foreach (var key in fallback.SortedKeys())
            {
                CheckValue(report, fallback, key);

                var fallbackTemplate = fallback.GetRaw(key) ?? string.Empty;
                var fallbackPlaceholders = ExtractPlaceholders(fallbackTemplate);

                for (var i = 1; i < all.Count; i++)
                {
                    var catalog = all[i];
                    if (!catalog.Contains(key))
                    {
                        report.Add(new TranslationIssue(
                            TranslationIssue.Severity.Error,
                            catalog.LanguageCode,
                            key,
                            "translation.error.missingTranslation"));
                        continue;
                    }

                    CheckValue(report, catalog, key);

                    var template = catalog.GetRaw(key) ?? string.Empty;
                    var placeholders = ExtractPlaceholders(template);
                    if (!SamePlaceholders(fallbackPlaceholders, placeholders))
                    {
                        report.Add(new TranslationIssue(
                            TranslationIssue.Severity.Error,
                            catalog.LanguageCode,
                            key,
                            "translation.error.placeholderMismatch",
                            string.Join(",", fallbackPlaceholders.ToArray()),
                            string.Join(",", placeholders.ToArray())));
                    }
                }
            }

            return report;
        }

        private static void CheckValue(TranslationValidationReport report, TranslationCatalog catalog, string key)
        {
            var value = catalog.GetRaw(key);
            if (value == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(value) || value.Trim().Length == 0)
            {
                report.Add(new TranslationIssue(
                    TranslationIssue.Severity.Error,
                    catalog.LanguageCode,
                    key,
                    "translation.error.emptyValue"));
            }
        }

        /// <summary>
        /// Extracts the placeholder indices used by a template, ignoring doubled braces.
        /// Returned sorted and de-duplicated for stable comparison.
        /// </summary>
        public static List<int> ExtractPlaceholders(string template)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();
            if (string.IsNullOrEmpty(template))
            {
                return result;
            }

            for (var i = 0; i < template.Length; i++)
            {
                if (template[i] == '{')
                {
                    if (i + 1 < template.Length && template[i + 1] == '{')
                    {
                        i++;
                        continue;
                    }

                    var close = template.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        var inner = template.Substring(i + 1, close - i - 1);
                        int index;
                        if (int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                        {
                            if (!seen.Contains(index))
                            {
                                seen.Add(index);
                                result.Add(index);
                            }

                            i = close;
                            continue;
                        }
                    }
                }
                else if (template[i] == '}' && i + 1 < template.Length && template[i + 1] == '}')
                {
                    i++;
                }
            }

            result.Sort();
            return result;
        }

        private static bool SamePlaceholders(List<int> expected, List<int> actual)
        {
            if (expected.Count != actual.Count)
            {
                return false;
            }

            for (var i = 0; i < expected.Count; i++)
            {
                if (expected[i] != actual[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The key namespaces AvatarTuner uses. A string literal in the source that matches
        /// one of these namespaces is assumed to be a translation key, which is what the
        /// source scanner uses to prove that every key referenced by code exists in every
        /// translation file.
        /// </summary>
        public static readonly string[] KnownKeyNamespaces =
        {
            "ui.", "tip.", "dlg.", "error.", "plan.", "change.", "status.", "preset.",
            "analysis.", "scan.", "compat.", "results.", "opt.", "unit.", "translation.",
            "menu.", "report.", "finding.", "usage."
        };

        /// <summary>
        /// True when a string literal should be treated as a translation key.
        /// </summary>
        public static bool LooksLikeTranslationKey(string literal)
        {
            if (string.IsNullOrEmpty(literal) || literal.Length < 3)
            {
                return false;
            }

            for (var i = 0; i < KnownKeyNamespaces.Length; i++)
            {
                if (literal.StartsWith(KnownKeyNamespaces[i], StringComparison.Ordinal))
                {
                    return IsKeyShape(literal);
                }
            }

            return false;
        }

        private static bool IsKeyShape(string literal)
        {
            // keys are dot separated lowerCamel / kebab segments: [a-z][a-zA-Z0-9_-]* ( . ... )+
            var segmentStart = true;
            var dots = 0;
            for (var i = 0; i < literal.Length; i++)
            {
                var c = literal[i];
                if (c == '.')
                {
                    if (segmentStart)
                    {
                        return false;
                    }

                    segmentStart = true;
                    dots++;
                    continue;
                }

                if (segmentStart)
                {
                    if (!((c >= 'a' && c <= 'z')))
                    {
                        return false;
                    }

                    segmentStart = false;
                    continue;
                }

                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-'))
                {
                    return false;
                }
            }

            return dots >= 1 && !segmentStart;
        }
    }
}
