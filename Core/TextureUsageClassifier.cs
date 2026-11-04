using System;
using System.Collections.Generic;

namespace AvatarTuner.Core
{
    /// <summary>
    /// What a texture is used for, as far as can be inferred from its shader property name,
    /// the shader it is bound to, and its importer settings.
    ///
    /// The classifier is deliberately conservative: when the evidence is weak it returns
    /// <see cref="TextureUsageKind.Unknown" /> so that the planner treats the texture as
    /// precious rather than disposable.
    /// </summary>
    public enum TextureUsageKind
    {
        Unknown = 0,

        /// <summary>Colour map where the alpha channel is part of the look (transparency, cut-out).</summary>
        AlbedoWithAlpha,

        /// <summary>Colour map whose alpha channel is unused.</summary>
        AlbedoOpaque,

        /// <summary>Tangent-space or object-space normal map.</summary>
        NormalMap,

        /// <summary>Single or dual channel data map: smoothness, metallic, occlusion, mask.</summary>
        DataMask,

        /// <summary>Greyscale data such as a LUT, height map or detail mask.</summary>
        GreyscaleData,

        /// <summary>Emission / HDR colour contribution.</summary>
        Emission,

        /// <summary>Detail overlay (detail albedo, detail normal).</summary>
        Detail,

        /// <summary>UI / overlay texture that is read at full resolution.</summary>
        UserInterface
    }

    /// <summary>
    /// Evidence gathered while classifying a texture. Exposed so the UI can explain *why*
    /// a decision was reached rather than presenting an unexplained verdict.
    /// </summary>
    public sealed class ClassificationEvidence
    {
        public readonly List<string> Reasons = new List<string>();
        public bool ShaderIndicatesAlpha;
        public bool ShaderIndicatesNormal;
        public bool ShaderIndicatesMask;
        public bool NameIndicatesDetail;
        public bool NameIndicatesFace;
        public bool NameIndicatesEye;
        public bool NameIndicatesSmallText;

        public void Add(string reason)
        {
            if (!string.IsNullOrEmpty(reason))
            {
                Reasons.Add(reason);
            }
        }
    }

    /// <summary>
    /// Classifies texture usage from naming and binding evidence.
    ///
    /// Shader property names follow the conventions shared by Standard, lilToon, Poiyomi,
    /// UTS, XSToon and similar shaders, so the heuristics transfer across them. Because the
    /// input is only strings and flags, this class is fully testable without Unity.
    /// </summary>
    public static class TextureUsageClassifier
    {
        private static readonly string[] AlphaPropertySuffixes =
        {
            "_main_tex", "_maintex", "_albedo", "_basecolormap", "_base_map", "_basecolor",
            "_diffuse", "_diffusemap", "_colormap", "_col", "_texture", "_tex", "_maskmap"
        };

        private static readonly string[] NormalNameTokens =
        {
            "_normal", "_normalmap", "_bumpmap", "_bump", "_nrm", "_normaldx", "_normalgl",
            "normal", "bump"
        };

        private static readonly string[] MaskNameTokens =
        {
            "_mask", "_metallic", "_smoothness", "_glossiness", "_gloss", "_occlusion",
            "_ao", "_roughness", "_detailmask", "_specular", "_reflection",
            "_parallax", "_height", "_displacement", "_cutoff", "_opacitymask",
            "_emissionmask", "_shadowmask", "_shadownmask", "_rimmask", "_widthmask"
        };

        private static readonly string[] GreyscaleNameTokens =
        {
            "_height", "_parallax", "_displacement", "_ao", "_occlusion", "_roughness",
            "_smoothness", "_metallic", "_gloss", "_cavity", "_thickness"
        };

        private static readonly string[] DetailNameTokens =
        {
            "_detail", "detail", "_overlay", "_decal"
        };

        private static readonly string[] FaceNameTokens =
        {
            "_face", "face", "_head", "head", "_skin", "skin", "_body", "_makeup", "makeup",
            "_blush", "_eyebrow", "eyebrow", "_lash", "_lip", "_mouth", "_cheek"
        };

        private static readonly string[] EyeNameTokens =
        {
            "_eye", "eye", "_iris", "iris", "_pupil", "pupil", "_sclera", "_highlight"
        };

        private static readonly string[] SmallTextNameTokens =
        {
            "_text", "text", "_font", "font", "_label", "label", "_logo", "logo",
            "_ui", "ui_", "_hud", "_badge", "_sign", "_title"
        };

        /// <summary>
        /// Classifies a texture.
        /// </summary>
        /// <param name="propertyName">Shader property the texture is bound to, e.g. <c>_MainTex</c>.</param>
        /// <param name="shaderName">Shader the owning material uses.</param>
        /// <param name="textureAssetName">File name of the texture asset.</param>
        /// <param name="importerTypeIsNormalMap">TextureImporter reports NormalMap type.</param>
        /// <param name="sourceHasAlpha">TextureImporter.DoesSourceTextureHaveAlpha().</param>
        /// <param name="alphaIsTransparency">TextureImporter alphaIsTransparency flag.</param>
        /// <param name="evidence">Optional sink for the reasons behind the verdict.</param>
        public static TextureUsageKind Classify(
            string propertyName,
            string shaderName,
            string textureAssetName,
            bool importerTypeIsNormalMap,
            bool sourceHasAlpha,
            bool alphaIsTransparency,
            ClassificationEvidence evidence)
        {
            var prop = (propertyName ?? string.Empty).ToLowerInvariant();
            var shader = (shaderName ?? string.Empty).ToLowerInvariant();
            var asset = (textureAssetName ?? string.Empty).ToLowerInvariant();

            if (evidence == null)
            {
                evidence = new ClassificationEvidence();
            }

            evidence.ShaderIndicatesNormal = importerTypeIsNormalMap || ContainsAny(prop, NormalNameTokens);
            // lilToon/Poiyomi masks end in "Mask" without an underscore (_MatCapBlendMask, _ShadowBorderMask).
            evidence.ShaderIndicatesMask = ContainsAny(prop, MaskNameTokens) || prop.EndsWith("mask", StringComparison.Ordinal);
            evidence.ShaderIndicatesAlpha = DetectsAlphaUsage(shader, prop, sourceHasAlpha, alphaIsTransparency);
            evidence.NameIndicatesDetail = ContainsAny(prop, DetailNameTokens) || ContainsAny(asset, DetailNameTokens);
            evidence.NameIndicatesFace = ContainsAny(prop, FaceNameTokens) || ContainsAny(asset, FaceNameTokens);
            evidence.NameIndicatesEye = ContainsAny(prop, EyeNameTokens) || ContainsAny(asset, EyeNameTokens);
            evidence.NameIndicatesSmallText = ContainsAny(prop, SmallTextNameTokens) || ContainsAny(asset, SmallTextNameTokens);

            if (evidence.ShaderIndicatesNormal)
            {
                evidence.Add("property/importer indicates a normal map");
                return TextureUsageKind.NormalMap;
            }

            var isMatCap = prop.Contains("matcap") || asset.Contains("matcap");
            if (isMatCap && !prop.Contains("mask") && !asset.Contains("mask"))
            {
                evidence.Add("property/name indicates a full-color matcap texture");
                return (sourceHasAlpha && (alphaIsTransparency || evidence.ShaderIndicatesAlpha))
                    ? TextureUsageKind.AlbedoWithAlpha
                    : TextureUsageKind.AlbedoOpaque;
            }

            var isColorRampOrLut = (prop.Contains("lut") || prop.Contains("ramp") || asset.Contains("ramp")) &&
                                   !prop.Contains("mask") && !asset.Contains("mask");
            if (isColorRampOrLut)
            {
                evidence.Add("property/name indicates a color ramp or lookup texture");
                return (sourceHasAlpha && (alphaIsTransparency || evidence.ShaderIndicatesAlpha))
                    ? TextureUsageKind.AlbedoWithAlpha
                    : TextureUsageKind.AlbedoOpaque;
            }

            if (evidence.NameIndicatesDetail)
            {
                evidence.Add("name indicates a detail overlay");
                return TextureUsageKind.Detail;
            }

            if (evidence.ShaderIndicatesMask)
            {
                evidence.Add("property name indicates a data/mask channel");
                return ContainsAny(prop, GreyscaleNameTokens) || ContainsAny(asset, GreyscaleNameTokens)
                    ? TextureUsageKind.GreyscaleData
                    : TextureUsageKind.DataMask;
            }

            if (ContainsAny(prop, SmallTextNameTokens) || ContainsAny(asset, SmallTextNameTokens))
            {
                evidence.Add("name indicates UI or small text content");
                return TextureUsageKind.UserInterface;
            }

            if (prop.Contains("emission") || prop.Contains("_emis") || prop.Contains("_glow"))
            {
                evidence.Add("property name indicates emissive contribution");
                return TextureUsageKind.Emission;
            }

            if (IsAlbedoProperty(prop))
            {
                if (evidence.ShaderIndicatesAlpha)
                {
                    evidence.Add("albedo bound with evidence that the alpha channel is used");
                    return TextureUsageKind.AlbedoWithAlpha;
                }

                evidence.Add("albedo bound without evidence of alpha usage");
                return TextureUsageKind.AlbedoOpaque;
            }

            evidence.Add("no conclusive evidence; treated as an unclassified texture");
            return TextureUsageKind.Unknown;
        }

        /// <summary>
        /// True when the shader/property combination suggests the alpha channel is load
        /// bearing: transparency keywords, cut-out keywords, or a shader whose name advertises
        /// transparency support.
        /// </summary>
        public static bool DetectsAlphaUsage(string shaderName, string propertyName, bool sourceHasAlpha, bool alphaIsTransparency)
        {
            var shader = (shaderName ?? string.Empty).ToLowerInvariant();
            var prop = (propertyName ?? string.Empty).ToLowerInvariant();

            if (shader.Contains("transparent") || shader.Contains("fade") || shader.Contains("cutout") ||
                shader.Contains("alpha") || shader.Contains("blend"))
            {
                return true;
            }

            if (prop.Contains("opacity") || prop.Contains("alpha") || prop.Contains("cutout") || prop.Contains("_cutoff"))
            {
                return true;
            }

            if (alphaIsTransparency && sourceHasAlpha)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// True when the texture name or its binding suggests that resolution must be
        /// preserved: faces, eyes, small text, UI. The planner refuses to downscale these
        /// unless the selected preset explicitly opts into aggressive behaviour.
        /// </summary>
        public static bool IsDetailPreserving(TextureUsageKind kind, ClassificationEvidence evidence)
        {
            if (kind == TextureUsageKind.UserInterface)
            {
                return true;
            }

            if (evidence == null)
            {
                return false;
            }

            return evidence.NameIndicatesFace || evidence.NameIndicatesEye || evidence.NameIndicatesSmallText;
        }

        /// <summary>
        /// Merges classifications across multiple bindings on the same texture conservatively.
        /// When different bindings disagree, color preservation always wins over data/mask
        /// compression to ensure required RGB/Alpha channels are never discarded.
        /// </summary>
        public static TextureUsageKind MergeUsage(TextureUsageKind current, TextureUsageKind incoming, bool hasAlpha = false)
        {
            if (current == TextureUsageKind.Unknown) return incoming;
            if (incoming == TextureUsageKind.Unknown) return current;
            if (current == incoming) return current;

            // UI textures must be read at full resolution and full fidelity.
            if (current == TextureUsageKind.UserInterface || incoming == TextureUsageKind.UserInterface)
            {
                return TextureUsageKind.UserInterface;
            }

            var currentIsColor = IsColorUsage(current);
            var incomingIsColor = IsColorUsage(incoming);

            // Mixed color/data usage: color MUST win to prevent color textures from being
            // compressed into 1-channel or 2-channel formats like BC4 or BC5!
            if (currentIsColor && !incomingIsColor)
            {
                if (current == TextureUsageKind.AlbedoWithAlpha || (incoming == TextureUsageKind.DataMask && hasAlpha))
                {
                    return TextureUsageKind.AlbedoWithAlpha;
                }
                return current;
            }

            if (!currentIsColor && incomingIsColor)
            {
                if (incoming == TextureUsageKind.AlbedoWithAlpha || (current == TextureUsageKind.DataMask && hasAlpha))
                {
                    return TextureUsageKind.AlbedoWithAlpha;
                }
                return incoming;
            }

            // Both are color usages: prioritize alpha preservation, then detail, emission, opaque albedo.
            if (currentIsColor && incomingIsColor)
            {
                if (current == TextureUsageKind.AlbedoWithAlpha || incoming == TextureUsageKind.AlbedoWithAlpha)
                {
                    return TextureUsageKind.AlbedoWithAlpha;
                }

                if (current == TextureUsageKind.Detail || incoming == TextureUsageKind.Detail)
                {
                    return TextureUsageKind.Detail;
                }

                if (current == TextureUsageKind.Emission || incoming == TextureUsageKind.Emission)
                {
                    return TextureUsageKind.Emission;
                }

                return TextureUsageKind.AlbedoOpaque;
            }

            // Normal map conflicting with non-color data (e.g. mask): conservative Unknown
            if (current == TextureUsageKind.NormalMap || incoming == TextureUsageKind.NormalMap)
            {
                return TextureUsageKind.Unknown;
            }

            // Both are data usages: multi-channel DataMask beats single-channel GreyscaleData
            if (current == TextureUsageKind.DataMask || incoming == TextureUsageKind.DataMask)
            {
                return TextureUsageKind.DataMask;
            }

            return current;
        }

        /// <summary>
        /// True when the usage represents color (RGB or RGBA) rather than purely non-color data.
        /// </summary>
        public static bool IsColorUsage(TextureUsageKind kind)
        {
            switch (kind)
            {
                case TextureUsageKind.AlbedoWithAlpha:
                case TextureUsageKind.AlbedoOpaque:
                case TextureUsageKind.Emission:
                case TextureUsageKind.Detail:
                case TextureUsageKind.UserInterface:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True when the usage tolerates block compression without visible damage and can
        /// safely be moved to a smaller block format.
        /// </summary>
        public static bool IsCompressionFriendly(TextureUsageKind kind)
        {
            switch (kind)
            {
                case TextureUsageKind.NormalMap:
                case TextureUsageKind.DataMask:
                case TextureUsageKind.GreyscaleData:
                case TextureUsageKind.AlbedoOpaque:
                case TextureUsageKind.Detail:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsAlbedoProperty(string prop)
        {
            if (string.IsNullOrEmpty(prop))
            {
                return false;
            }

            foreach (var suffix in AlphaPropertySuffixes)
            {
                if (prop.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return prop == "_main" || prop == "_base" || prop == "_diff" || prop == "_col" ||
                prop == "_main2ndtex" || prop == "_main3rdtex"; // lilToon color layers
        }

        private static bool ContainsAny(string haystack, string[] needles)
        {
            if (string.IsNullOrEmpty(haystack))
            {
                return false;
            }

            for (var i = 0; i < needles.Length; i++)
            {
                if (haystack.Contains(needles[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
