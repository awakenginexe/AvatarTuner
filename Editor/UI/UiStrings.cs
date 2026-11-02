// AvatarTuner v1.0.0 — localized UI strings.
//
// Loads the three shipped catalogs (en / th / zh-Hans) through Unity's asset pipeline so
// they resolve no matter whether the package is embedded, local or cached, and resolves
// keys through Core's TranslationResolver with English as the permanent fallback. The
// language choice survives domain reloads via SessionState.

using System;
using System.Collections.Generic;
using AvatarTuner.Core;
using UnityEditor;
using UnityEngine;

namespace AvatarTuner.Editor.UI
{
    public static class UiStrings
    {
        private const string SessionKey = "AvatarTuner.Language";
        private const string CatalogSuffix = "/Editor/Localization/Translations/";

        private static readonly Dictionary<string, TranslationCatalog> Catalogs =
            new Dictionary<string, TranslationCatalog>(StringComparer.OrdinalIgnoreCase);

        private static TranslationResolver _resolver;
        private static InterfaceLanguage _language;

        /// <summary>The active interface language. Switching takes effect immediately.</summary>
        public static InterfaceLanguage Language
        {
            get
            {
                EnsureLoaded();
                return _language;
            }

            set
            {
                EnsureLoaded();
                if (_language == value && _resolver != null)
                {
                    return;
                }

                _language = value;
                SessionState.SetInt(SessionKey, (int)value);
                RebuildResolver();
            }
        }

        /// <summary>Resolves a key, falling back to the key itself (never blank).</summary>
        public static string T(string key)
        {
            EnsureLoaded();
            return _resolver.Resolve(key, key);
        }

        /// <summary>Resolves a key and interpolates its arguments.</summary>
        public static string T(string key, params string[] args)
        {
            EnsureLoaded();
            return _resolver.Resolve(key, args, key);
        }

        /// <summary>The three shipped languages, in menu order.</summary>
        public static readonly InterfaceLanguage[] Languages =
        {
            InterfaceLanguage.English,
            InterfaceLanguage.Thai,
            InterfaceLanguage.SimplifiedChinese
        };

        /// <summary>Native display name of a language, as shipped in its own catalog.</summary>
        public static string LanguageName(InterfaceLanguage language)
        {
            EnsureLoaded();
            TranslationCatalog catalog;
            if (Catalogs.TryGetValue(CodeFor(language), out catalog) &&
                !string.IsNullOrEmpty(catalog.DisplayName))
            {
                return catalog.DisplayName;
            }

            return language.ToString();
        }

        // ===================================================================== loading

        private static void EnsureLoaded()
        {
            if (_resolver != null)
            {
                return;
            }

            foreach (InterfaceLanguage language in Enum.GetValues(typeof(InterfaceLanguage)))
            {
                var code = CodeFor(language);
                if (!Catalogs.ContainsKey(code))
                {
                    var catalog = LoadCatalog(code);
                    if (catalog != null)
                    {
                        Catalogs[code] = catalog;
                    }
                }
            }

            _language = (InterfaceLanguage)SessionState.GetInt(SessionKey, (int)InterfaceLanguage.English);
            RebuildResolver();
        }

        private static void RebuildResolver()
        {
            var chain = new List<TranslationCatalog>();

            TranslationCatalog selected;
            if (Catalogs.TryGetValue(CodeFor(_language), out selected))
            {
                chain.Add(selected);
            }

            TranslationCatalog english;
            if (Catalogs.TryGetValue("en", out english))
            {
                chain.Add(english);
            }

            _resolver = new TranslationResolver(chain.ToArray());
        }

        private static string CodeFor(InterfaceLanguage language)
        {
            switch (language)
            {
                case InterfaceLanguage.Thai: return "th";
                case InterfaceLanguage.SimplifiedChinese: return "zh-Hans";
                default: return "en";
            }
        }

        private static TranslationCatalog LoadCatalog(string code)
        {
            try
            {
                var suffix = CatalogSuffix + code + ".json";
                var guids = AssetDatabase.FindAssets(code + " t:TextAsset");
                for (var i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    if (textAsset != null)
                    {
                        return TranslationCatalog.FromJson(textAsset.text, code);
                    }
                }
            }
            catch (Exception)
            {
                // A missing or unreadable catalog degrades to key text, never to a broken
                // window; the English fallback chain still applies when available.
            }

            return null;
        }
    }
}
