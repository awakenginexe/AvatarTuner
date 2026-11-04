using System;
using System.Collections.Generic;
using AvatarTuner.Core;
namespace AvatarTuner.StandaloneTests
{
    public static class AdaptiveSafetyTests
    {
        [Test] public static void AstcBlockArithmeticAndPlatform()
        {
            Assert.AreEqual(64L, VramCalculator.LevelBytes(7, 7, TunerTextureFormat.ASTC_6x6), "ASTC padding");
            Assert.AreEqual(16L, VramCalculator.LevelBytes(8, 8, TunerTextureFormat.ASTC_8x8), "ASTC 8x8");
            Assert.IsFalse(TextureFormatInfo.IsSupportedOnPc(TunerTextureFormat.ASTC_6x6), "mobile format");
            var r = Request(); var s = new TextureSettings { Platform = OptimizationPlatform.Android };
            foreach (var c in AdaptivePlanner.Candidates(r, Policy(), null, s, SemanticCategory.Outfit))
                Assert.IsTrue(c.Format == TunerTextureFormat.ASTC_4x4 || c.Format == TunerTextureFormat.ASTC_6x6 || c.Format == TunerTextureFormat.ASTC_8x8, "Android format");
        }
        [Test] public static void UnsafeFormatsAndDataChannelsAreRejected()
        {
            var r = Request(); var s = new TextureSettings { Format = TunerTextureFormat.BC6H };
            Assert.AreEqual(0, AdaptivePlanner.Candidates(r, Policy(), null, s, SemanticCategory.Outfit).Count, "HDR requires HDR source");
            s.Format = TunerTextureFormat.Automatic; r.Usage = TextureUsageKind.DataMask;
            foreach (var c in AdaptivePlanner.Candidates(r, Policy(), null, s, SemanticCategory.Masks))
                Assert.IsTrue(c.Format != TunerTextureFormat.BC4 && c.Format != TunerTextureFormat.BC5, "packed channels");
            r.Usage = TextureUsageKind.Unknown;
            Assert.AreEqual(0, AdaptivePlanner.Candidates(r, Policy(), null, s, SemanticCategory.Unknown).Count, "unknown");
        }
        [Test] public static void ResolutionOnlyRequiresPassingEvaluation()
        {
            var r = Request(); var s = new TextureSettings(); var o = new TextureOverride { KeepFormat = true };
            var plan = AdaptivePlanner.Select(r, Policy(), o, s, SemanticCategory.Outfit, c => new CandidateQuality { Available = true, Psnr = 10, Ssim = 0.2 });
            Assert.IsTrue(plan.IsNoOp, "bad resolution must fail");
            plan = AdaptivePlanner.Select(r, Policy(), o, s, SemanticCategory.Outfit, c => new CandidateQuality { Available = true, Psnr = 60, Ssim = 1 });
            Assert.IsTrue(plan.ResolutionChanged, "evaluated resolution accepted");
        }
        [Test] public static void MissingSettingsUseDefaultsAndHashIsStable()
        {
            var a = OptimizationSettings.FromDictionary(new Dictionary<string, object>());
            Assert.AreEqual(4.0, a.Global.DesiredReduction, "migration default");
            var b = OptimizationSettings.FromDictionary(a.ToDictionary());
            Assert.AreEqual(a.ToHashToken(), b.ToHashToken(), "roundtrip");
        }
        [Test] public static void UniformFiltersAndNormalVectors()
        {
            var pixels = new byte[] { 128, 128, 255, 255, 128, 128, 255, 255, 128, 128, 255, 255, 128, 128, 255, 255 };
            foreach (var f in new[] { ResizeFilter.Area, ResizeFilter.Lanczos3, ResizeFilter.Mitchell })
            {
                var result = TextureResampler.Resize(pixels, 2, 2, 1, 1, f, false, false, true);
                Assert.AreEqual(255, result[2], "normal z");
                Assert.AreEqual(255, result[3], "alpha");
            }
        }
        static TexturePolicy Policy() { return PresetCatalog.GetPolicy(OptimizationPreset.Balanced); }

        [Test("packed data rejects isolated channel damage even when aggregate metrics pass")]
        public static void PackedDataBoundsEachChannel()
        {
            var request = Request();
            request.Usage = TextureUsageKind.DataMask;
            request.CurrentSRGB = false;
            var plan = AdaptivePlanner.Select(request, Policy(), null, new TextureSettings(), SemanticCategory.Masks,
                c => new CandidateQuality { Available = true, Psnr = 100, Ssim = 1, MaxChannelError = 0.1 });
            Assert.IsTrue(plan.IsNoOp, "small isolated data features cannot be hidden by PSNR/SSIM averages");
        }
        static TexturePlanRequest Request() { return new TexturePlanRequest { SourceWidth = 1024, SourceHeight = 512, CurrentFormat = TunerTextureFormat.RGBA32, CurrentFormatKnown = true, Usage = TextureUsageKind.AlbedoOpaque }; }
    }
}
