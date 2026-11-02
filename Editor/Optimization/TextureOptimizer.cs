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
        private const string PlatformName = "Standalone";

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

                GenerateOne(targets[i], result);
            }

            if (progress != null && !result.Cancelled)
            {
                progress(1.0f, "opt.phase.textures");
            }

            result.Succeeded = result.ErrorCount == 0 && !result.Cancelled;
            return result;
        }

        private static void GenerateOne(PlannedTexture planned, TextureGenerationResult result)
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
            if (File.Exists(targetPath))
            {
                // Only files inside the generated folder are ever deleted.
                AssetDatabase.DeleteAsset(targetPath);
            }

            if (!AssetDatabase.CopyAsset(sourcePath, targetPath))
            {
                AddError(result, "error.ioFailed", sourcePath);
                result.SkippedCount++;
                return;
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
                AssetDatabase.DeleteAsset(targetPath);
                result.SkippedCount++;
                return;
            }

            ApplySettings(importer, plan, importerFormat);

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

        private static void ApplySettings(TextureImporter importer, TexturePlan plan, TextureImporterFormat format)
        {
            importer.maxTextureSize = plan.TargetMaxSize;
            importer.mipmapEnabled = plan.TargetMipMaps;

            var settings = importer.GetPlatformTextureSettings(PlatformName);
            settings.overridden = true;
            settings.format = format;
            settings.maxTextureSize = plan.TargetMaxSize;
            settings.textureCompression = TextureFormatInfo.IsBlockCompressed(plan.TargetFormat)
                ? TextureImporterCompression.Compressed
                : TextureImporterCompression.Uncompressed;
            settings.crunchedCompression = false;
            importer.SetPlatformTextureSettings(settings);
        }

        private static string BuildTargetPath(string outputFolder, PlannedTexture planned)
        {
            var extension = string.Empty;
            if (!string.IsNullOrEmpty(planned.TexturePath))
            {
                extension = Path.GetExtension(planned.TexturePath);
            }

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
