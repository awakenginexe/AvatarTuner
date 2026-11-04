using System;
using System.Collections.Generic;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;

namespace AvatarTuner.StandaloneTests
{
    public static class AdaptiveIntegrationTests
    {
        [Test("adaptive builder evaluates resolution-only candidates and retains category")]
        public static void ResolutionOnlyCandidatesAreEvaluated()
        {
            var report = Report();
            var settings = new OptimizationSettings();
            settings.Textures["tex"] = new TextureSettings { Format = TunerTextureFormat.BC7 };
            var calls = 0;
            var plan = TexturePlanBuilder.BuildAdaptive(report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced),
                null, settings, (entry, candidate) =>
                {
                    if (candidate.Width < entry.Width && candidate.Format == TunerTextureFormat.BC7) calls++;
                    return new CandidateQuality { Available = true, Psnr = 100, Ssim = 1 };
                });
            Assert.IsTrue(calls > 0, "unchanged-format resizes still need actual quality evidence");
            Assert.AreEqual(SemanticCategory.Hair, plan.Textures[0].Category, "custom is settings source, never category");
            Assert.AreEqual(SettingsSource.Custom, plan.Textures[0].EffectiveSettings.Source, "custom source retained");
        }

        [Test("adaptive builder protects driven textures before invoking evaluator")]
        public static void ProtectionsPrecedeEvaluation()
        {
            var report = Report();
            report.ToolMarkers.Add(new ToolMarkerEntry { Path = "Hair", ToolId = "vrcfury" });
            report.Renderers.Add(new RendererEntry { Path = "Hair/Mesh" });
            report.Renderers[0].MaterialGuids.Add("mat");
            var calls = 0;
            var plan = TexturePlanBuilder.BuildAdaptive(report, PresetCatalog.GetPolicy(OptimizationPreset.SizeFirst),
                null, new OptimizationSettings(), (entry, candidate) => { calls++; return null; });
            Assert.AreEqual(0, calls, "protected textures never create scratch candidates");
            Assert.IsTrue(plan.Textures[0].Plan.IsNoOp, "pinned is no-op");
            Assert.IsTrue(plan.Textures[0].ProtectedByThirdParty, "pin retained");
        }

        [Test("adaptive config hash includes categories and source revision does not reuse decisions")]
        public static void EffectiveSettingsInvalidatePlan()
        {
            var settings = new OptimizationSettings();
            var policy = PresetCatalog.GetPolicy(OptimizationPreset.Balanced);
            var before = TexturePlanBuilder.AdaptiveConfigHash(policy, null, settings);
            settings.Categories[SemanticCategory.Hair] = new TextureSettings { KeepResolution = true };
            Assert.IsFalse(before == TexturePlanBuilder.AdaptiveConfigHash(policy, null, settings), "category change stales plan");
        }

        private static AvatarAnalysisReport Report()
        {
            var report = new AvatarAnalysisReport { ScanSucceeded = true };
            var entry = new TextureEntry { Guid = "tex", Path = "Assets/Hair.png", Name = "Hair", Width = 2048,
                Height = 1024, CurrentFormatName = "BC7", CurrentFormatKnown = true, MipMapsEnabled = true,
                SrgbTexture = true, Usage = TextureUsageKind.AlbedoOpaque, Category = SemanticCategory.Hair };
            entry.UsedByMaterialGuids.Add("mat");
            report.Textures.Add(entry);
            return report;
        }

        [Test("invalid quality settings and metric values cannot bypass quality gates")]
        public static void InvalidQualityValuesFailSafely()
        {
            var request = new TexturePlanRequest { SourceWidth = 1024, SourceHeight = 1024,
                CurrentFormat = TunerTextureFormat.RGBA32, CurrentFormatKnown = true, Usage = TextureUsageKind.AlbedoOpaque };
            var settings = new TextureSettings { MinPsnr = double.NaN };
            var policy = PresetCatalog.GetPolicy(OptimizationPreset.SizeFirst);
            Assert.IsTrue(AdaptivePlanner.Select(request, policy, null, settings, SemanticCategory.Outfit,
                c => new CandidateQuality { Available = true, Psnr = 0, Ssim = 1 }).IsNoOp, "NaN cannot remove PSNR requirement");
            settings.MinPsnr = 45;
            Assert.IsTrue(AdaptivePlanner.Select(request, policy, null, settings, SemanticCategory.Outfit,
                c => new CandidateQuality { Available = true, Psnr = 100, Ssim = double.PositiveInfinity }).IsNoOp,
                "impossible SSIM is unavailable evidence");
        }
    }
}
