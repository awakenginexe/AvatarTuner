using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AvatarTuner.Core
{
    public enum SemanticConfidence { Low, Medium, High }

    public sealed class SemanticEvidence
    {
        public string RendererPath;
        public string MaterialName;
        public string ShaderName;
        public string PropertyName;
        public string MeshName;
        public string BoneName;
        public bool IsAnimated;
    }

    public sealed class SemanticReason
    {
        public string Key;
        public string Evidence;
        public SemanticReason(string key, string evidence) { Key = key; Evidence = evidence; }
    }

    public sealed class SemanticClassification
    {
        public SemanticCategory Category = SemanticCategory.Unknown;
        public SemanticConfidence Confidence = SemanticConfidence.Low;
        public readonly List<SemanticReason> Reasons = new List<SemanticReason>();
    }

    public static class SemanticClassifier
    {
        private static readonly Dictionary<string, SemanticCategory> Tokens = new Dictionary<string, SemanticCategory>(StringComparer.OrdinalIgnoreCase)
        {
            { "face", SemanticCategory.Face }, { "head", SemanticCategory.Face },
            { "eye", SemanticCategory.Eyes }, { "eyes", SemanticCategory.Eyes }, { "iris", SemanticCategory.Eyes },
            { "hair", SemanticCategory.Hair }, { "bangs", SemanticCategory.Hair },
            { "skin", SemanticCategory.Body }, { "body", SemanticCategory.Body },
            { "outfit", SemanticCategory.Outfit }, { "clothes", SemanticCategory.Outfit }, { "clothing", SemanticCategory.Outfit }, { "dress", SemanticCategory.Outfit }, { "shirt", SemanticCategory.Outfit },
            { "accessory", SemanticCategory.Accessories }, { "accessories", SemanticCategory.Accessories },
            { "effect", SemanticCategory.Effects }, { "effects", SemanticCategory.Effects }, { "particle", SemanticCategory.Effects },
            { "mask", SemanticCategory.Masks }, { "masks", SemanticCategory.Masks },
            { "normal", SemanticCategory.Masks }, { "bump", SemanticCategory.Masks }, { "metallic", SemanticCategory.Masks },
            { "roughness", SemanticCategory.Masks }, { "occlusion", SemanticCategory.Masks }, { "smoothness", SemanticCategory.Masks }
        };

        private static HashSet<SemanticCategory> Find(string value)
        {
            var found = new HashSet<SemanticCategory>();
            if (string.IsNullOrEmpty(value)) return found;
            var words = Regex.Replace(value, "([a-z0-9])([A-Z])", "$1 $2");
            foreach (var word in Regex.Split(words, "[^A-Za-z0-9]+"))
            {
                SemanticCategory category;
                if (Tokens.TryGetValue(word, out category)) found.Add(category);
            }
            return found;
        }

        public static SemanticClassification Classify(string textureName, IEnumerable<SemanticEvidence> evidence)
        {
            var result = new SemanticClassification();
            var categories = new HashSet<SemanticCategory>();
            var confidence = SemanticConfidence.Low;
            if (evidence != null)
            {
                foreach (var item in evidence)
                {
                    if (item == null) continue;
                    var property = Find(item.PropertyName);
                    HashSet<SemanticCategory> found;
                    string source;
                    string key;
                    SemanticConfidence strength;
                    if (property.Contains(SemanticCategory.Masks))
                    {
                        found = new HashSet<SemanticCategory> { SemanticCategory.Masks }; source = item.PropertyName; key = "semantic.reason.property"; strength = SemanticConfidence.High;
                    }
                    else
                    {
                        // Ignore the avatar root: its name applies to every renderer, not this binding.
                        var path = item.RendererPath ?? string.Empty;
                        var slash = path.IndexOf('/');
                        source = slash >= 0 ? path.Substring(slash + 1) : path;
                        found = new HashSet<SemanticCategory>();
                        var segments = source.Split('/');
                        for (var i = segments.Length - 1; i >= 0 && found.Count == 0; i--) found = Find(segments[i]);
                        key = item.IsAnimated ? "semantic.reason.animation" : "semantic.reason.renderer"; strength = SemanticConfidence.High;
                        if (found.Count == 0) { source = item.MaterialName; found = Find(source); key = "semantic.reason.material"; strength = SemanticConfidence.Medium; }
                        if (found.Count == 0) { source = item.MeshName; found = Find(source); key = "semantic.reason.mesh"; strength = SemanticConfidence.Medium; }
                        if (found.Count == 0) { source = item.BoneName; found = Find(source); key = "semantic.reason.bone"; strength = SemanticConfidence.Medium; }
                        if (found.Count == 0) { source = item.PropertyName; found = property; key = "semantic.reason.property"; strength = SemanticConfidence.Medium; }
                        if (found.Count == 0) { source = item.ShaderName; found = Find(source); key = "semantic.reason.shader"; strength = SemanticConfidence.Low; }
                    }
                    if (found.Count == 0) continue;
                    foreach (var category in found) categories.Add(category);
                    result.Reasons.Add(new SemanticReason(key, source));
                    if (strength > confidence) confidence = strength;
                }
            }
            if (categories.Count == 0)
            {
                categories = Find(textureName);
                if (categories.Count > 0) result.Reasons.Add(new SemanticReason("semantic.reason.filename", textureName));
            }
            if (categories.Count > 1) result.Category = SemanticCategory.Mixed;
            else foreach (var category in categories) result.Category = category;
            result.Confidence = confidence;
            return result;
        }
    }
}
