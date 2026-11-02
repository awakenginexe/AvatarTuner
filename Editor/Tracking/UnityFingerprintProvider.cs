// AvatarTuner v1.0.0 — Unity-backed asset fingerprints for change tracking.
//
// Supplies the two fingerprints SourceAssetState records:
//   - AssetDatabase.GetAssetDependencyHash: Unity's own aggregate over source, .meta,
//     platform and importer version. Recorded as-is, never re-derived.
//   - An AvatarTuner hash over the importer settings that actually affect the generated
//     output (format, max size, mip maps, sRGB, alpha handling, compression), kept
//     separate so platform/importer-version churn does not invalidate outputs.
//
// Every method is best effort: a missing asset or unreadable importer yields an empty
// fingerprint, which the analyzer reads as "unknown", not as "unchanged".

using System;
using System.Collections.Generic;
using AvatarTuner.Core;
using UnityEditor;

namespace AvatarTuner.Editor.Tracking
{
    public sealed class UnityFingerprintProvider : IFingerprintProvider
    {
        private const string PlatformName = "Standalone";

        public string GetAssetDependencyHash(string guid, string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return string.Empty;
                }

                return AssetDatabase.GetAssetDependencyHash(path).ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        public string GetImporterSettingsHash(string guid, string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return string.Empty;
                }

                var importer = AssetImporter.GetAtPath(path);
                if (importer == null)
                {
                    return string.Empty;
                }

                var textureImporter = importer as TextureImporter;
                if (textureImporter == null)
                {
                    // Non-texture assets change their output through the asset itself,
                    // which GetAssetDependencyHash already covers. The importer type is
                    // hashed so swapping the importer is still noticed.
                    return StableHash.Sha256Hex(importer.GetType().FullName);
                }

                return HashTextureImporter(textureImporter);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string HashTextureImporter(TextureImporter importer)
        {
            var tokens = new List<string>();
            tokens.Add("type:" + importer.textureType);
            tokens.Add("max:" + importer.maxTextureSize);
            tokens.Add("mips:" + importer.mipmapEnabled);
            tokens.Add("srgb:" + importer.sRGBTexture);
            tokens.Add("alphaTransparency:" + importer.alphaIsTransparency);
            tokens.Add("alphaSource:" + importer.alphaSource);
            tokens.Add("normalFilter:" + importer.convertToNormalmap);
            tokens.Add("heightmap:" + importer.heightmapScale);

            var platform = importer.GetPlatformTextureSettings(PlatformName);
            tokens.Add("platformOverridden:" + platform.overridden);
            tokens.Add("platformFormat:" + platform.format);
            tokens.Add("platformMax:" + platform.maxTextureSize);
            tokens.Add("platformCompression:" + platform.textureCompression);
            tokens.Add("platformCompressionQuality:" + platform.compressionQuality);
            tokens.Add("platformCrunch:" + platform.crunchedCompression);

            return StableHash.CombineOrdered(tokens);
        }
    }
}
