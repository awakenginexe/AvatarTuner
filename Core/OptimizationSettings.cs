using System;
using System.Collections.Generic;
using System.Globalization;
namespace AvatarTuner.Core
{
    public enum SemanticCategory
    {
        Face, Eyes, Hair, Body, Outfit, Accessories, Effects, Masks, Mixed, Unknown
    }
    public enum SettingsSource
    {
        Global, Category, Custom
    }
    public enum OptimizationEffort
    {
        Standard, BestQuality
    }
    public enum OptimizationPlatform
    {
        Windows, Android
    }
    public enum ResizeFilter
    {
        UnityMitchell, UnityBilinear, Area, Lanczos3, Mitchell
    }
    public sealed class TextureSettings
    {
        public double DesiredReduction = 4, MinPsnr = 45, MinSsim = 0.98;
        public TunerTextureFormat Format = TunerTextureFormat.Automatic;
        public bool KeepResolution, ProtectQuality, Excluded;
        public int MaxResolution;
        public OptimizationEffort Effort = OptimizationEffort.Standard;
        public OptimizationPlatform Platform = OptimizationPlatform.Windows;
        public TextureSettings Clone()
        {
            return (TextureSettings)MemberwiseClone();
        }
        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                { "desiredReduction", DesiredReduction }, { "format", Format.ToString() },
                { "keepResolution", KeepResolution }, { "maxResolution", MaxResolution },
                { "protectQuality", ProtectQuality }, { "excluded", Excluded },
                { "effort", Effort.ToString() }, { "platform", Platform.ToString() },
                { "minPsnr", MinPsnr }, { "minSsim", MinSsim }
            };
        }

        public static TextureSettings FromDictionary(Dictionary<string, object> d)
        {
            var s = new TextureSettings();
            if (d == null) return s;
            s.DesiredReduction = Clamp(MiniJson.GetDouble(d, "desiredReduction", 4), 1, 16, 4);
            s.MinPsnr = Clamp(MiniJson.GetDouble(d, "minPsnr", 45), 0, 200, 45);
            s.MinSsim = Clamp(MiniJson.GetDouble(d, "minSsim", 0.98), 0, 1, 0.98);
            TunerTextureFormat f;
            if (TextureFormatInfo.TryParse(MiniJson.GetString(d, "format", "Automatic"), out f)) s.Format = f;
            s.KeepResolution = MiniJson.GetBool(d, "keepResolution", false);
            s.ProtectQuality = MiniJson.GetBool(d, "protectQuality", false);
            s.Excluded = MiniJson.GetBool(d, "excluded", false);
            var max = MiniJson.GetLong(d, "maxResolution", 0);
            s.MaxResolution = max > 0 && max <= int.MaxValue ? (int)max : 0;
            OptimizationEffort e;
            if (Enum.TryParse(MiniJson.GetString(d, "effort", "Standard"), out e) && Enum.IsDefined(typeof(OptimizationEffort), e)) s.Effort = e;
            OptimizationPlatform p;
            if (Enum.TryParse(MiniJson.GetString(d, "platform", "Windows"), out p) && Enum.IsDefined(typeof(OptimizationPlatform), p)) s.Platform = p;
            return s;
        }
        static double Clamp(double x, double min, double max, double fallback)
        {
            return double.IsNaN(x) || double.IsInfinity(x) ? fallback : Math.Max(min, Math.Min(max, x));
        }
        public string ToHashToken()
        {
            return StableHash.CombineOrdered(new[] {
 DesiredReduction.ToString("R", CultureInfo.InvariantCulture), Format.ToString(), KeepResolution.ToString(), MaxResolution.ToString(CultureInfo.InvariantCulture), ProtectQuality.ToString(), Excluded.ToString(), Effort.ToString(), Platform.ToString(), MinPsnr.ToString("R", CultureInfo.InvariantCulture), MinSsim.ToString("R", CultureInfo.InvariantCulture) });
        }
    }
    public sealed class EffectiveTextureSettings
    {
        public TextureSettings Settings;
        public SettingsSource Source;
    }
    public sealed class OptimizationSettings
    {
        public TextureSettings Global = new TextureSettings();
        public readonly Dictionary<SemanticCategory, TextureSettings> Categories = new Dictionary<SemanticCategory, TextureSettings>();
        public readonly Dictionary<string, TextureSettings> Textures = new Dictionary<string, TextureSettings>(StringComparer.Ordinal);
        public readonly Dictionary<string, SemanticCategory> CategoryCorrections = new Dictionary<string, SemanticCategory>(StringComparer.Ordinal);
        public EffectiveTextureSettings Resolve(string guid, SemanticCategory category)
        {
            SemanticCategory corrected;
            if (guid != null && CategoryCorrections.TryGetValue(guid, out corrected)) category = corrected;
            var g = Global ?? new TextureSettings();
            TextureSettings c, t = null;
            bool hasC = Categories.TryGetValue(category, out c) && c != null;
            bool hasT = guid != null && Textures.TryGetValue(guid, out t) && t != null;
            if (!hasT) t = null;
            var selected = (hasT ? t : hasC ? c : g).Clone();
            selected.Excluded |= g.Excluded || (hasC && c.Excluded);
            selected.ProtectQuality |= g.ProtectQuality || (hasC && c.ProtectQuality);
            return new EffectiveTextureSettings
            {
                Settings = selected,
                Source = hasT ? SettingsSource.Custom : hasC ? SettingsSource.Category : SettingsSource.Global
            };
        }
        public Dictionary<string, object> ToDictionary()
        {
            var cats = new Dictionary<string, object>();
            foreach (var p in Categories) if (p.Value != null) cats[p.Key.ToString()] = p.Value.ToDictionary();
            var textures = new Dictionary<string, object>();
            foreach (var p in Textures) if (p.Value != null) textures[p.Key] = p.Value.ToDictionary();
            var corrections = new Dictionary<string, object>();
            foreach (var p in CategoryCorrections) corrections[p.Key] = p.Value.ToString();
            return new Dictionary<string, object>
            {
                { "global", (Global ?? new TextureSettings()).ToDictionary() },
                { "categories", cats },
                { "textures", textures },
                { "categoryCorrections", corrections }
            };
        }

        public static OptimizationSettings FromDictionary(Dictionary<string, object> d)
        {
            var s = new OptimizationSettings();
            if (d == null) return s;
            object v;
            if (d.TryGetValue("global", out v)) s.Global = TextureSettings.FromDictionary(v as Dictionary<string, object>);
            if (d.TryGetValue("categories", out v) && v is Dictionary<string, object> cats) foreach (var p in cats)
            {
                SemanticCategory c;
                if (Enum.TryParse(p.Key, out c) && Enum.IsDefined(typeof(SemanticCategory), c)) s.Categories[c] = TextureSettings.FromDictionary(p.Value as Dictionary<string, object>);
            }
            if (d.TryGetValue("textures", out v) && v is Dictionary<string, object> tex) foreach (var p in tex) s.Textures[p.Key] = TextureSettings.FromDictionary(p.Value as Dictionary<string, object>);
            if (d.TryGetValue("categoryCorrections", out v) && v is Dictionary<string, object> cor) foreach (var p in cor)
            {
                SemanticCategory c;
                if (Enum.TryParse(Convert.ToString(p.Value), out c) && Enum.IsDefined(typeof(SemanticCategory), c)) s.CategoryCorrections[p.Key] = c;
            }
            return s;
        }
        public string ToHashToken()
        {
            var tokens = new List<string> { (Global ?? new TextureSettings()).ToHashToken() };
            var keys = new List<string>();
            foreach (var p in Categories) keys.Add("C:" + p.Key);
            foreach (var p in Textures) keys.Add("T:" + p.Key);
            foreach (var p in CategoryCorrections) keys.Add("R:" + p.Key);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                tokens.Add(k);
                if (k[0] == 'C') tokens.Add(Categories[(SemanticCategory)Enum.Parse(typeof(SemanticCategory), k.Substring(2))]?.ToHashToken() ?? "");
                else if (k[0] == 'T') tokens.Add(Textures[k.Substring(2)]?.ToHashToken() ?? "");
                else tokens.Add(CategoryCorrections[k.Substring(2)].ToString());
            }
            return StableHash.CombineOrdered(tokens);
        }
    }
}

