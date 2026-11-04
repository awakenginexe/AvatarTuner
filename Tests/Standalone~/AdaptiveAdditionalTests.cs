using System;
using AvatarTuner.Core;
namespace AvatarTuner.StandaloneTests
{
    public static class AdaptiveAdditionalTests
    {
        [Test] public static void EqualSizeCandidatesCompareQuality()
        {
            var r = Request(); var s = new TextureSettings { Effort = OptimizationEffort.BestQuality, KeepResolution = true };
            var p = AdaptivePlanner.Select(r, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, s, SemanticCategory.Outfit,
                c => new CandidateQuality { Available = true, Psnr = c.Filter == ResizeFilter.UnityBilinear ? 70 : 60, Ssim = 1 });
            Assert.AreEqual(ResizeFilter.UnityBilinear, p.Candidate.Filter, "best equal-byte candidate");
        }
        [Test] public static void NormalAlphaNeverUsesColorFormats()
        {
            var r = Request(); r.Usage = TextureUsageKind.NormalMap; r.SourceHasAlpha = true;
            foreach (var c in AdaptivePlanner.Candidates(r, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, new TextureSettings(), SemanticCategory.Hair))
                Assert.IsTrue(c.Format == TunerTextureFormat.BC5 || c.Format == r.CurrentFormat, "normal format");
        }
        [Test] public static void FaceUsesStricterFloorAndQuality()
        {
            var r = Request(); r.SourceWidth = 2048; r.SourceHeight = 2048;
            foreach (var c in AdaptivePlanner.Candidates(r, PresetCatalog.GetPolicy(OptimizationPreset.QualityFirst), null, new TextureSettings(), SemanticCategory.Face))
                Assert.IsTrue(c.MaxSize >= 1024, "face floor");
            var plan = AdaptivePlanner.Select(r, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, new TextureSettings(), SemanticCategory.Face,
                c => new CandidateQuality { Available = true, Psnr = 46, Ssim = 0.981 });
            Assert.IsTrue(plan.IsNoOp, "face needs higher quality");
        }
        [Test] public static void RequestedFormatIsNeverBypassed()
        {
            var settings = new TextureSettings { Format = TunerTextureFormat.BC7 };
            var candidates = AdaptivePlanner.Candidates(Request(), PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, settings, SemanticCategory.Outfit);
            Assert.IsTrue(candidates.Count > 0, "requested candidates exist");
            foreach (var c in candidates) Assert.AreEqual(TunerTextureFormat.BC7, c.Format, "requested format");
        }
        static TexturePlanRequest Request() { return new TexturePlanRequest { SourceWidth = 1024, SourceHeight = 512, CurrentFormat = TunerTextureFormat.RGBA32, CurrentFormatKnown = true, Usage = TextureUsageKind.AlbedoOpaque }; }
    }
}
