// AvatarTuner v1.0.0 — generated texture asset materializer.
//
// Takes a built TextureOptimizationPlan and turns every changed entry into a real
// generated texture asset inside the project's output folder. The golden rule:
// originals are NEVER modified. Every output is a copy that receives the planned
// importer settings; the source asset and its importer settings stay untouched, so
// other avatars and tools that reference them are unaffected.
//
// Failures are per texture and never abort the run: one unreadable file or an
// unmappable format becomes a localized finding and the rest of the plan continues.

using System;
using System.Collections.Generic;
using System.IO;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using UnityEditor;
using UnityEngine;

namespace AvatarTuner.Editor.Optimization
{
    public static class TextureOptimizer
    {
        private const string GeneratedRoot = "Assets/AvatarTuner-Generated";

        /// <summary>Default output folder for an avatar: unique per source avatar GUID.</summary>
        public static string DefaultOutputFolder(AvatarAnalysisReport report)
        {
            var name = Sanitize(string.IsNullOrEmpty(report.AvatarName) ? "Avatar" : report.AvatarName);
            var suffix = string.IsNullOrEmpty(report.AvatarRootGuid)
                ? "scene"
                : report.AvatarRootGuid.Substring(0, Math.Min(8, report.AvatarRootGuid.Length));
            return GeneratedRoot + "/" + name + "-" + suffix + "/Textures";
        }

        /// <summary>
        /// Materializes every changed texture in the plan. Returns a result that lists what
        /// was generated and every problem that was hit; the run never throws for asset
        /// problems and never touches a source asset.
        /// </summary>
        public static TextureGenerationResult Generate(
            AvatarAnalysisReport report,
            TextureOptimizationPlan plan,
            string outputFolder,
            Action<float, string> progress,
            Func<bool> cancelled)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }

            var result = new TextureGenerationResult();
            result.OutputFolder = string.IsNullOrEmpty(outputFolder)
                ? DefaultOutputFolder(report ?? new AvatarAnalysisReport())
                : outputFolder;

            var targets = new List<PlannedTexture>();
            for (var i = 0; i < plan.Textures.Count; i++)
            {
                if (plan.Textures[i].HasChanges)
                {
                    targets.Add(plan.Textures[i]);
                }
            }

            if (targets.Count == 0)
            {
                result.Succeeded = true;
                return result;
            }

            if (!EnsureFolder(result.OutputFolder))
            {
                AddError(result, "error.ioFailed", result.OutputFolder);
                result.Succeeded = false;
                return result;
            }

            var allowedRoot = Path.GetFullPath(GeneratedRoot) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(result.OutputFolder).StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Generated output must stay under " + GeneratedRoot);
            using (var transaction = new GeneratedFileTransaction(GeneratedRoot, "Library/AvatarTuner/Transactions"))
            {
            for (var i = 0; i < targets.Count; i++)
            {
                if (cancelled != null && cancelled())
                {
                    result.Cancelled = true;
                    result.Findings.Add(new AnalysisFinding("error.cancelled"));
                    break;
                }

                if (progress != null)
                {
                    progress((float)i / targets.Count, "opt.phase.textures");
                }

                transaction.Record(BuildTargetPath(result.OutputFolder, targets[i]));
                GenerateOne(targets[i], result);
            }

            if (progress != null && !result.Cancelled)
            {
                progress(1.0f, "opt.phase.textures");
            }

            result.Succeeded = result.ErrorCount == 0 && !result.Cancelled;
            if (result.Succeeded) transaction.Commit();
            }
            if (!result.Succeeded)
            {
                result.Generated.Clear();
                result.PlannedTargetBytesTotal = 0;
                AssetDatabase.Refresh();
            }
            return result;
        }

        private static void GenerateOne(PlannedTexture planned, TextureGenerationResult result)
        {
            try
            {
                using (var evaluator = new CandidateEvaluator(null))
                {
                    var source = planned.Plan.Candidate == null ? planned.TexturePath
                        : evaluator.Materialize(planned.TexturePath, planned.Plan.Candidate);
                    GenerateOneCore(planned, result, source);
                }
            }
            catch (Exception ex)
            {
                AddError(result, "error.ioFailed", planned.TexturePath + " (" + ex.Message + ")");
                result.SkippedCount++;
            }
        }

        private static void GenerateOneCore(PlannedTexture planned, TextureGenerationResult result, string generationSource)
        {
            var plan = planned.Plan;
            var sourcePath = planned.TexturePath;

            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                AddError(result, "error.assetMissing", sourcePath ?? string.Empty);
                result.SkippedCount++;
                return;
            }

            var importerFormatName = TextureFormatMapping.ImporterFormatName(plan.TargetFormat);
            if (importerFormatName == null)
            {
                // Never approximate an inexpressible format; keep the original untouched
                // and say so.
                AddError(result, "error.unsupportedConversion",
                    TextureFormatInfo.DisplayName(plan.TargetFormat));
                result.SkippedCount++;
                return;
            }

            var targetPath = BuildTargetPath(result.OutputFolder, planned);
            bool targetAlreadyExisted = File.Exists(targetPath);
            if (targetAlreadyExisted)
            {
                // Re-optimization: overwrite file in place so the .meta file and its GUID are preserved!
                try
                {
                    File.Copy(generationSource, targetPath, overwrite: true);
                    AssetDatabase.ImportAsset(targetPath);
                }
                catch (Exception ex)
                {
                    AddError(result, "error.ioFailed", targetPath + " (" + ex.Message + ")");
                    result.SkippedCount++;
                    return;
                }
            }
            else
            {
                if (!AssetDatabase.CopyAsset(generationSource, targetPath))
                {
                    AddError(result, "error.ioFailed", sourcePath);
                    result.SkippedCount++;
                    return;
                }
            }

            var importer = AssetImporter.GetAtPath(targetPath) as TextureImporter;
            if (importer == null)
            {
                AddError(result, "error.ioFailed", targetPath);
                result.SkippedCount++;
                return;
            }

            TextureImporterFormat importerFormat;
            if (!Enum.TryParse(importerFormatName, true, out importerFormat))
            {
                AddError(result, "error.unsupportedConversion", importerFormatName);
                if (!targetAlreadyExisted)
                {
                    AssetDatabase.DeleteAsset(targetPath);
                }
                result.SkippedCount++;
                return;
            }

            var sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            ApplySettings(importer, sourceImporter, plan, importerFormat);
            importer.isReadable = false;

            try
            {
                importer.SaveAndReimport();
            }
            catch (Exception ex)
            {
                AddError(result, "error.ioFailed", targetPath + " (" + ex.Message + ")");
                result.SkippedCount++;
                return;
            }

            var asset = new GeneratedTextureAsset();
            asset.SourceGuid = planned.TextureGuid;
            asset.SourcePath = sourcePath;
            asset.GeneratedPath = targetPath;
            asset.GeneratedGuid = AssetDatabase.AssetPathToGUID(targetPath);
            asset.PlanHashToken = plan.ToHashToken();
            asset.PlannedTargetBytes = plan.TargetBytes;
            var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
            if (imported != null)
            {
                asset.ImportedWidth = imported.width;
                asset.ImportedHeight = imported.height;
                asset.ImportedFormat = imported.format.ToString();
                asset.ImportedMipCount = imported.mipmapCount;
                asset.ImportedMipmaps = imported.mipmapCount > 1;
                long bytes;
                asset.ImportedBytesKnown = TryImportedBytes(targetPath, out bytes);
                asset.ImportedBytes = bytes;
                TunerTextureFormat actual;
                if (plan.Candidate != null && (!asset.ImportedBytesKnown || bytes > plan.SourceBytes ||
                    imported.width != plan.TargetWidth || imported.height != plan.TargetHeight ||
                    !TextureFormatInfo.TryParse(asset.ImportedFormat, out actual) || actual != plan.TargetFormat))
                    throw new InvalidOperationException("Imported result does not match the reviewed candidate.");
            }
            else if (plan.Candidate != null) throw new InvalidOperationException("Imported result unavailable.");

            try
            {
                var info = new FileInfo(targetPath);
                if (info.Exists)
                {
                    asset.GeneratedFileSizeBytes = info.Length;
                    asset.ContentHash = StableHash.Sha256Hex(File.ReadAllBytes(targetPath));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            result.Generated.Add(asset);
            result.PlannedTargetBytesTotal += plan.TargetBytes;
        }

        internal static void ApplySettings(
            TextureImporter importer,
            TextureImporter sourceImporter,
            TexturePlan plan,
            TextureImporterFormat format)
        {
            importer.maxTextureSize = plan.TargetMaxSize;
            importer.mipmapEnabled = plan.TargetMipMaps;

            if (sourceImporter != null)
            {
                var sourceSettings = new TextureImporterSettings();
                sourceImporter.ReadTextureSettings(sourceSettings);
                importer.SetTextureSettings(sourceSettings);
                importer.maxTextureSize = plan.TargetMaxSize;
                importer.mipmapEnabled = plan.TargetMipMaps;
                // Texture type: normal maps require NormalMap type
                if (plan.Usage == TextureUsageKind.NormalMap ||
                    sourceImporter.textureType == TextureImporterType.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    importer.convertToNormalmap = sourceImporter.convertToNormalmap;
                    importer.heightmapScale = sourceImporter.heightmapScale;
                }
                else
                {
                    importer.textureType = sourceImporter.textureType;
                }

                // sRGB / Linear:
                // Normal maps, BC4 and BC5 are purely linear data and must have sRGB disabled.
                // Otherwise preserve the source texture's sRGB configuration so color interpretation is exact.
                if (importer.textureType == TextureImporterType.NormalMap ||
                    plan.TargetFormat == TunerTextureFormat.BC4 ||
                    plan.TargetFormat == TunerTextureFormat.BC5)
                {
                    importer.sRGBTexture = false;
                }
                else
                {
                    importer.sRGBTexture = sourceImporter.sRGBTexture;
                }

                // Alpha
                importer.alphaSource = sourceImporter.alphaSource;
                if (!TextureFormatInfo.SupportsAlpha(plan.TargetFormat))
                {
                    importer.alphaIsTransparency = false;
                }
                else
                {
                    importer.alphaIsTransparency = sourceImporter.alphaIsTransparency;
                }

                // Filtering & sampling
                importer.filterMode = sourceImporter.filterMode;
                importer.anisoLevel = sourceImporter.anisoLevel;
                importer.wrapMode = sourceImporter.wrapMode;
                importer.wrapModeU = sourceImporter.wrapModeU;
                importer.wrapModeV = sourceImporter.wrapModeV;
                importer.wrapModeW = sourceImporter.wrapModeW;

                // Mipmaps
                importer.streamingMipmaps = sourceImporter.streamingMipmaps;
                importer.borderMipmap = sourceImporter.borderMipmap;
            }
            else
            {
                if (plan.Usage == TextureUsageKind.NormalMap)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    importer.sRGBTexture = false;
                }
                else if (plan.TargetFormat == TunerTextureFormat.BC4)
                {
                    importer.sRGBTexture = false;
                }
            }

            var settings = importer.GetPlatformTextureSettings(CandidateEvaluator.PlatformName);
            settings.overridden = true;
            settings.format = format;
            settings.maxTextureSize = plan.TargetMaxSize;
            settings.textureCompression = TextureFormatInfo.IsBlockCompressed(plan.TargetFormat)
                ? TextureImporterCompression.Compressed
                : TextureImporterCompression.Uncompressed;
            settings.crunchedCompression = false;
            if (plan.Candidate != null)
            {
                settings.compressionQuality = plan.Candidate.CompressionQuality;
                settings.resizeAlgorithm = plan.Candidate.Filter == ResizeFilter.UnityBilinear
                    ? TextureResizeAlgorithm.Bilinear : TextureResizeAlgorithm.Mitchell;
                if (TextureFormatInfo.IsBlockCompressed(plan.TargetFormat) && plan.Candidate.CompressionQuality >= 100)
                    settings.textureCompression = TextureImporterCompression.CompressedHQ;
            }
            importer.SetPlatformTextureSettings(settings);
        }

        public static bool TryImportedBytes(string path, out long bytes)
        {
            bytes = 0;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            TunerTextureFormat format;
            return texture != null && TextureFormatInfo.TryParse(texture.format.ToString(), out format) &&
                ImportedTextureMemory.TryEstimateBytes(texture.width, texture.height, format, texture.mipmapCount, 1, out bytes);
        }

        private static string BuildTargetPath(string outputFolder, PlannedTexture planned)
        {
            var extension = string.Empty;
            if (!string.IsNullOrEmpty(planned.TexturePath))
            {
                extension = Path.GetExtension(planned.TexturePath);
            }
            if (CandidateEvaluator.UsesCustomResize(planned.Plan == null ? null : planned.Plan.Candidate)) extension = ".png";

            var stem = Sanitize(string.IsNullOrEmpty(planned.TextureName) ? "texture" : planned.TextureName);
            var guidPart = string.IsNullOrEmpty(planned.TextureGuid)
                ? "noguid"
                : planned.TextureGuid.Substring(0, Math.Min(8, planned.TextureGuid.Length));
            return outputFolder + "/" + stem + "_" + guidPart + extension;
        }

        /// <summary>Creates every missing segment of a project-relative folder path.</summary>
        internal static bool EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder))
            {
                return false;
            }

            var segments = folder.Split('/');
            if (segments.Length == 0 || segments[0] != "Assets")
            {
                return false;
            }

            var current = "Assets";
            for (var i = 1; i < segments.Length; i++)
            {
                if (string.IsNullOrEmpty(segments[i]))
                {
                    continue;
                }

                var next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    var guid = AssetDatabase.CreateFolder(current, segments[i]);
                    if (string.IsNullOrEmpty(guid))
                    {
                        return false;
                    }
                }

                current = next;
            }

            return true;
        }

        private static string Sanitize(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var characters = name.Trim().ToCharArray();
            for (var i = 0; i < characters.Length; i++)
            {
                if (Array.IndexOf(invalid, characters[i]) >= 0 || characters[i] == '/' || characters[i] == '\\')
                {
                    characters[i] = '_';
                }
            }

            var result = new string(characters).Trim();
            return result.Length == 0 ? "generated" : result;
        }

        private static void AddError(TextureGenerationResult result, string key, string arg)
        {
            result.ErrorCount++;
            result.Findings.Add(new AnalysisFinding(key, arg));
        }
    }
}
