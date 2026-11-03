using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;
using AvatarTuner.Editor.Tracking;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Regression tests for v1.0.1 hotfix:
    /// - Priority 0: Texture color corruption fixes (MatCaps, packed masks, channel safety, quality fallback).
    /// - Priority 1: Reliability fixes (animation curves, GUID stability, read-only Analyze, safe Restore, clone refresh).
    /// </summary>
    public static class RegressionTestsV101
    {
        private static TexturePlanRequest NewRequest(
            int width, int height,
            TunerTextureFormat current,
            TextureUsageKind usage,
            bool hasAlpha = false,
            bool alphaUsed = false)
        {
            var request = new TexturePlanRequest();
            request.SourceWidth = width;
            request.SourceHeight = height;
            request.CurrentFormat = current;
            request.CurrentFormatKnown = current != TunerTextureFormat.Automatic && current != TunerTextureFormat.Unknown;
            request.CurrentMaxSize = 0;
            request.CurrentMipMaps = true;
            request.CurrentSRGB = usage != TextureUsageKind.NormalMap;
            request.Usage = usage;
            request.SourceHasAlpha = hasAlpha;
            request.AlphaChannelUsed = alphaUsed;
            request.FaceCount = 1;
            return request;
        }

        // --------------------------------------------------------------------- 1. Full-color MatCap textures

        [Test("Full-color MatCap textures are classified as color textures and never converted to BC4 or BC5")]
        public static void FullColorMatcapNeverBc4OrBc5()
        {
            var matcapTokens = new[] { "_MatCap", "_matcap", "_MatcapTex", "matcap_albedo", "matcap" };
            foreach (var token in matcapTokens)
            {
                var classified = TextureUsageClassifier.Classify(token, "Standard", string.Empty, false, false, false, null);
                Assert.IsTrue(classified == TextureUsageKind.AlbedoOpaque || classified == TextureUsageKind.AlbedoWithAlpha,
                    "Matcap token '" + token + "' must classify as color (Albedo), was: " + classified);
            }

            // Plan for matcap under all presets
            var presets = new[] { OptimizationPreset.QualityFirst, OptimizationPreset.Balanced, OptimizationPreset.SizeFirst };
            foreach (var preset in presets)
            {
                var policy = PresetCatalog.GetPolicy(preset);
                var request = NewRequest(1024, 1024, TunerTextureFormat.RGBA32, TextureUsageKind.AlbedoWithAlpha, true, true);

                var plan = CompressionPlanner.Plan(request, policy, null, null);
                Assert.IsFalse(plan.TargetFormat == TunerTextureFormat.BC4,
                    "MatCap must NEVER be compressed as 1-channel BC4 (which turns it red)");
                Assert.IsFalse(plan.TargetFormat == TunerTextureFormat.BC5,
                    "MatCap must NEVER be compressed as 2-channel BC5 (which strips blue)");
                Assert.IsTrue(TextureFormatInfo.HasColorChannels(plan.TargetFormat),
                    "MatCap must retain full color channels (RGB/RGBA), target was: " + plan.TargetFormat);
            }
        }

        // --------------------------------------------------------------------- 2. RGB and RGBA packed masks

        [Test("RGB and RGBA packed masks preserve color and alpha channels, never dropping to BC4/BC5")]
        public static void RgbAndRgbaPackedMasksPreserveChannels()
        {
            var policy = PresetCatalog.GetPolicy(OptimizationPreset.Balanced);

            // RGBA mask (e.g. Metallic, Occlusion, Detail, Smoothness in Alpha)
            var rgbaMaskRequest = NewRequest(2048, 2048, TunerTextureFormat.RGBA32, TextureUsageKind.DataMask, true, true);
            var rgbaPlan = CompressionPlanner.Plan(rgbaMaskRequest, policy, null, null);
            Assert.IsTrue(rgbaPlan.TargetFormat == TunerTextureFormat.BC7 || rgbaPlan.TargetFormat == TunerTextureFormat.BC3,
                "RGBA packed mask must use a 4-channel format (BC7 or BC3), was: " + rgbaPlan.TargetFormat);
            Assert.AreEqual(4, TextureFormatInfo.ChannelCount(rgbaPlan.TargetFormat),
                "RGBA packed mask must preserve all 4 channels");

            // RGB mask without alpha
            var rgbMaskRequest = NewRequest(2048, 2048, TunerTextureFormat.RGB24, TextureUsageKind.DataMask, false, false);
            var rgbPlan = CompressionPlanner.Plan(rgbMaskRequest, policy, null, null);
            Assert.IsFalse(rgbPlan.TargetFormat == TunerTextureFormat.BC4, "RGB packed mask must not be collapsed to BC4");
            Assert.IsTrue(TextureFormatInfo.ChannelCount(rgbPlan.TargetFormat) >= 3,
                "RGB packed mask must keep at least 3 channels, was: " + rgbPlan.TargetFormat);
        }

        // --------------------------------------------------------------------- 3. Alpha used as shader data

        [Test("Alpha channel used as shader data is preserved in plan builder and planner")]
        public static void AlphaUsedAsShaderDataPreserved()
        {
            var entry = new TextureEntry
            {
                Guid = "smoothness-guid",
                Path = "Assets/Textures/metallic.png",
                Usage = TextureUsageKind.DataMask,
                HasAlphaChannel = true,
                AlphaIsTransparency = false, // Not transparency! It's data (smoothness/roughness)
                Width = 1024,
                Height = 1024,
                CurrentFormatName = "RGBA32",
                CurrentFormatKnown = true
            };
            entry.BoundProperties.Add("_MetallicGlossMap");

            var request = TexturePlanBuilder.BuildRequest(entry);

            Assert.IsTrue(request.AlphaChannelUsed,
                "AlphaChannelUsed must be true when entry.HasAlphaChannel is true for DataMask textures");

            var policy = PresetCatalog.GetPolicy(OptimizationPreset.Balanced);
            var plan = CompressionPlanner.Plan(request, policy, null, null);
            Assert.IsTrue(TextureFormatInfo.SupportsAlpha(plan.TargetFormat),
                "Target format must retain alpha channel for packed shader data");
        }

        // --------------------------------------------------------------------- 4. Single-channel grayscale masks

        [Test("Single-channel grayscale masks use BC4 safely without alpha loss")]
        public static void SingleChannelGrayscaleMaskUsesBc4()
        {
            var request = NewRequest(1024, 1024, TunerTextureFormat.RGBA32, TextureUsageKind.GreyscaleData, false, false);
            var policy = PresetCatalog.GetPolicy(OptimizationPreset.Balanced);
            var plan = CompressionPlanner.Plan(request, policy, null, null);
            Assert.AreEqual(TunerTextureFormat.BC4, plan.TargetFormat,
                "Single-channel greyscale mask without alpha is safely compressed as BC4");
            Assert.AreEqual(1, TextureFormatInfo.ChannelCount(plan.TargetFormat),
                "BC4 stores 1 channel");
        }

        // --------------------------------------------------------------------- 5. Normal maps

        [Test("Normal maps are routed to BC5 and preserve required channel structure")]
        public static void NormalMapsGoToBc5()
        {
            var request = NewRequest(2048, 2048, TunerTextureFormat.RGBA32, TextureUsageKind.NormalMap, false, false);
            var policy = PresetCatalog.GetPolicy(OptimizationPreset.Balanced);
            var plan = CompressionPlanner.Plan(request, policy, null, null);
            Assert.AreEqual(TunerTextureFormat.BC5, plan.TargetFormat,
                "Normal maps must be routed to 2-channel BC5");
            Assert.AreEqual(2, TextureFormatInfo.ChannelCount(plan.TargetFormat),
                "BC5 stores 2 channels (RG) for normal vectors");
        }

        // --------------------------------------------------------------------- 6. Conflicting usage

        [Test("Conflicting usage across materials resolves to conservative color format")]
        public static void SharedTexturesWithConflictingUsageResolveToColor()
        {
            // When a texture is bound both as a mask and as an albedo color:
            var merged1 = TextureUsageClassifier.MergeUsage(TextureUsageKind.DataMask, TextureUsageKind.AlbedoWithAlpha, true);
            Assert.AreEqual(TextureUsageKind.AlbedoWithAlpha, merged1,
                "Color with alpha must take precedence over data mask");

            var merged2 = TextureUsageClassifier.MergeUsage(TextureUsageKind.GreyscaleData, TextureUsageKind.AlbedoOpaque, false);
            Assert.AreEqual(TextureUsageKind.AlbedoOpaque, merged2,
                "Opaque color must take precedence over greyscale data");

            var merged3 = TextureUsageClassifier.MergeUsage(TextureUsageKind.AlbedoOpaque, TextureUsageKind.DataMask, false);
            Assert.AreEqual(TextureUsageKind.AlbedoOpaque, merged3,
                "Color must never be demoted to data mask by subsequent bindings");

            Assert.IsTrue(TextureUsageClassifier.IsColorUsage(merged1), "merged1 is color usage");
            Assert.IsTrue(TextureUsageClassifier.IsColorUsage(merged2), "merged2 is color usage");
        }

        // --------------------------------------------------------------------- 7. Quality threshold fallback

        [Test("Quality threshold fallback prevents low-quality compression and downscaling")]
        public static void QualityThresholdFallbackRejectsBadConversion()
        {
            var policy = PresetCatalog.GetPolicy(OptimizationPreset.Balanced);
            var request = NewRequest(2048, 2048, TunerTextureFormat.RGBA32, TextureUsageKind.AlbedoOpaque, false, false);

            // Quality below threshold (e.g. 24 dB < policy.MinEstimatedQualityDb 36 dB)
            var badQuality = new QualityEvidence
            {
                HasMeasuredPsnr = true,
                MeasuredPsnrDb = 24.0
            };
            var plan = CompressionPlanner.Plan(request, policy, null, badQuality);

            Assert.AreEqual(QualityConfidence.Low, plan.Confidence,
                "Confidence must be marked Low when quality is below threshold");
            Assert.IsTrue(plan.Notes.Exists(n => n.Key == "plan.warning.measuredQualityBelowThreshold"),
                "A measuredQualityBelowThreshold warning must be recorded");
            // Downscale should be cancelled due to poor quality
            Assert.IsFalse(plan.ResolutionChanged,
                "Resolution downscale must be reverted when measured quality fails threshold");
        }

        // --------------------------------------------------------------------- 8. Generated asset GUID preservation

        [Test("TextureOptimizer uses File.Copy overwrite to preserve asset GUIDs across re-optimization")]
        public static void GeneratedAssetGuidPreservedInOptimizer()
        {
            var optimizerPath = Path.Combine(PackageLayout.Root, "Editor", "Optimization", "TextureOptimizer.cs");
            Assert.IsTrue(File.Exists(optimizerPath), "TextureOptimizer.cs exists");
            var source = File.ReadAllText(optimizerPath, Encoding.UTF8);

            Assert.IsFalse(source.Contains("AssetDatabase.DeleteAsset(outputPath)"),
                "TextureOptimizer must not call DeleteAsset on target output (deleting the .meta file destroys GUID)");
            Assert.IsTrue(source.Contains("File.Copy(sourcePath, targetPath, overwrite: true)") ||
                          source.Contains("File.Copy(sourcePath, targetPath, true)"),
                "TextureOptimizer must use File.Copy with overwrite: true to preserve the .meta file");
        }

        // --------------------------------------------------------------------- 9. Repeated optimization with different presets

        [Test("Repeated optimization with different presets produces consistent plans without mutating sources")]
        public static void RepeatedOptimizationWithDifferentPresets()
        {
            var request = NewRequest(2048, 2048, TunerTextureFormat.RGBA32, TextureUsageKind.AlbedoOpaque, false, false);

            var qualityPlan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.QualityFirst), null, null);
            var balancedPlan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, null);
            var sizePlan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.SizeFirst), null, null);

            // QualityFirst keeps 2048
            Assert.AreEqual(2048, qualityPlan.TargetMaxSize, "QualityFirst preserves 2048 max size");
            // Balanced drops 1 step to 1024
            Assert.AreEqual(1024, balancedPlan.TargetMaxSize, "Balanced drops 1 step to 1024 max size");
            // SizeFirst drops 2 steps to 512
            Assert.AreEqual(512, sizePlan.TargetMaxSize, "SizeFirst drops 2 steps to 512 max size");

            // All plans preserve color
            Assert.IsTrue(TextureFormatInfo.HasColorChannels(qualityPlan.TargetFormat), "QualityFirst keeps color");
            Assert.IsTrue(TextureFormatInfo.HasColorChannels(balancedPlan.TargetFormat), "Balanced keeps color");
            Assert.IsTrue(TextureFormatInfo.HasColorChannels(sizePlan.TargetFormat), "SizeFirst keeps color");
        }

        // --------------------------------------------------------------------- 10. Read-only Analyze behavior

        [Test("Analyze is strictly read-only and never restores or clears recorded application state")]
        public static void AnalyzeIsReadOnly()
        {
            var controllerPath = Path.Combine(PackageLayout.Root, "Editor", "UI", "AvatarTunerController.cs");
            Assert.IsTrue(File.Exists(controllerPath), "AvatarTunerController.cs exists");
            var source = File.ReadAllText(controllerPath, Encoding.UTF8);

            // Find the Analyze method
            var analyzeIdx = source.IndexOf("public void Analyze()", StringComparison.Ordinal);
            Assert.IsTrue(analyzeIdx >= 0, "Analyze method found");
            var analyzeMethod = source.Substring(analyzeIdx, Math.Min(2500, source.Length - analyzeIdx));

            Assert.IsFalse(analyzeMethod.Contains("SceneTextureApplier.Restore"),
                "Analyze must not call SceneTextureApplier.Restore");
            Assert.IsFalse(analyzeMethod.Contains("GeneratedAssetStore.SetApplicationState"),
                "Analyze must not clear GeneratedAssetStore state");
            Assert.IsTrue(analyzeMethod.Contains("RecordedState = RecordedState"),
                "Analyze must pass RecordedState into ScanOptions so applied clones map back to source");
        }

        // --------------------------------------------------------------------- 11. Partial Restore failure handling

        [Test("Partial restore failure preserves un-restored bindings and returns error")]
        public static void PartialRestorePreservesRecoveryInfo()
        {
            var state = new AvatarOptimizationState
            {
                AvatarName = "TestAvatar",
                SourceRootGuid = "root-guid",
                AppliedToAvatar = true,
                AppliedPartially = false
            };

            state.RendererBindings.Add(new AppliedRendererBinding
            {
                RendererPath = "Body",
                SlotIndex = 0,
                OriginalMaterialGuid = "mat-1",
                AppliedMaterialGuid = "mat-clone-1"
            });
            state.RendererBindings.Add(new AppliedRendererBinding
            {
                RendererPath = "MissingRenderer",
                SlotIndex = 0,
                OriginalMaterialGuid = "mat-2",
                AppliedMaterialGuid = "mat-clone-2"
            });

            // Verify that SceneTextureApplier.Restore source retains remaining bindings when partial
            var applierPath = Path.Combine(PackageLayout.Root, "Editor", "Apply", "SceneTextureApplier.cs");
            var source = File.ReadAllText(applierPath, Encoding.UTF8);

            Assert.IsTrue(source.Contains("remainingRendererBindings"),
                "Restore must track remaining un-restored renderer bindings");
            Assert.IsTrue(source.Contains("remainingAnimationBindings"),
                "Restore must track remaining un-restored animation bindings");

            var controllerPath = Path.Combine(PackageLayout.Root, "Editor", "UI", "AvatarTunerController.cs");
            var cSource = File.ReadAllText(controllerPath, Encoding.UTF8);

            Assert.IsTrue(cSource.Contains("build.error.partialApplication"),
                "RestoreOriginalTextures must return partial application warning when restore is partial");
        }

        // --------------------------------------------------------------------- 12. Updated material properties on reused clones

        [Test("Reused material clones copy current source properties, keywords, and queue")]
        public static void ReusedMaterialClonesRefreshProperties()
        {
            var applierPath = Path.Combine(PackageLayout.Root, "Editor", "Apply", "SceneTextureApplier.cs");
            var source = File.ReadAllText(applierPath, Encoding.UTF8);

            Assert.IsTrue(source.Contains("clone.CopyPropertiesFromMaterial(sourceMat)"),
                "Reused clone must copy updated properties from source material");
            Assert.IsTrue(source.Contains("clone.shaderKeywords = sourceMat.shaderKeywords"),
                "Reused clone must copy current shader keywords");
            Assert.IsTrue(source.Contains("clone.renderQueue = sourceMat.renderQueue"),
                "Reused clone must copy current renderQueue");
        }

        // --------------------------------------------------------------------- 13. Material-swap animation binding detection

        [Test("Material-swap animation detection inspects Renderer components and VRChat descriptors")]
        public static void MaterialSwapAnimationBindingDetection()
        {
            var scannerPath = Path.Combine(PackageLayout.Root, "Editor", "Analysis", "AvatarScanner.cs");
            var scannerSource = File.ReadAllText(scannerPath, Encoding.UTF8);

            Assert.IsFalse(scannerSource.Contains("binding.type != typeof(Material)"),
                "AvatarScanner must not use binding.type != typeof(Material) which drops renderer curves");
            Assert.IsTrue(scannerSource.Contains("IsMaterialBinding"),
                "AvatarScanner must use IsMaterialBinding helper");
            Assert.IsTrue(scannerSource.Contains("CollectDescriptorControllers"),
                "AvatarScanner must scan VRCAvatarDescriptor playable controllers");

            var applierPath = Path.Combine(PackageLayout.Root, "Editor", "Apply", "SceneTextureApplier.cs");
            var applierSource = File.ReadAllText(applierPath, Encoding.UTF8);

            Assert.IsFalse(applierSource.Contains("bindings[b].type != typeof(Material)"),
                "SceneTextureApplier must not check bindings[b].type != typeof(Material)");
            Assert.IsTrue(applierSource.Contains("AvatarScanner.IsMaterialBinding"),
                "SceneTextureApplier must use AvatarScanner.IsMaterialBinding");
            Assert.IsTrue(applierSource.Contains("CollectDescriptorControllers"),
                "SceneTextureApplier must scan descriptor controllers");
            Assert.IsTrue(applierSource.Contains("AnimatorOverrideController"),
                "SceneTextureApplier must support AnimatorOverrideController");
        }
    }
}
