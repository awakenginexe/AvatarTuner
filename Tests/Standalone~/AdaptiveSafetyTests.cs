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
            var r = Request(); var s = new TextureSettings { Platform = OptimizationPlatform.Android, KeepResolution = true };
            var candidates = AdaptivePlanner.Candidates(r, Policy(), null, s, SemanticCategory.Outfit);
            Assert.AreEqual(6, candidates.Count, "six LDR ASTC block sizes");
            Assert.IsTrue(candidates.TrueForAll(c => c.CompressionQuality == 50), "Advanced keeps the user's encoding effort");
            var standard = AdaptivePlanner.Candidates(r, Policy(), null, s, SemanticCategory.Outfit, StandardOptimizationLevel.Normal);
            Assert.IsTrue(standard.Count == 6 && standard.TrueForAll(c => c.CompressionQuality == 100),
                "Standard measures the high-quality ASTC encode at each block size");
            foreach (var width in new[] { 4, 5, 6, 8, 10, 12 })
            {
                var size = width + "x" + width;
                var f = (TunerTextureFormat)Enum.Parse(typeof(TunerTextureFormat), "ASTC_" + size);
                Assert.IsTrue(candidates.Exists(c => c.Format == f), size);
                Assert.AreEqual(64L, VramCalculator.LevelBytes(width + 1, width + 1, f), "padding " + size);
                Assert.IsFalse(TextureFormatInfo.IsSupportedOnPc(f), size);
                TunerTextureFormat parsed;
                Assert.IsTrue(TextureFormatInfo.TryParse("RGBA_ASTC" + size + "_SRGB", out parsed) && parsed == f, "graphics alias");
                Assert.IsFalse(TextureFormatInfo.TryParse("RGBA_ASTC" + size + "_UFloat", out parsed), "HDR is distinct");
            }
            long sdkBytes;
            Assert.IsTrue(VrcTextureMemory.TryEstimateBytes(1024, 1024, TunerTextureFormat.ASTC_12x12, 1, 1, out sdkBytes), "SDK format");
            Assert.AreEqual(116654L, sdkBytes, "SDK table rounds differently from physical blocks");
        }

        [Test] public static void RgbaColorNeedsFullResolutionOpacityProofForBc1()
        {
            var r = Request();
            r.SourceHasAlpha = r.AlphaChannelUsed = true;
            r.Usage = TextureUsageKind.AlbedoWithAlpha;
            var s = new TextureSettings { KeepResolution = true };
            var pixels = new byte[1024 * 4];
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            var opaque = CandidateQuality.Compare(pixels, pixels, 1024, 1, true);
            var plan = AdaptivePlanner.Select(r, Policy(), null, s, SemanticCategory.Body, c => opaque);
            Assert.AreEqual(TunerTextureFormat.BC1, plan.TargetFormat, "opaque RGBA qualifies");
            pixels[pixels.Length - 1] = 254;
            var sparse = CandidateQuality.Compare(pixels, pixels, 1024, 1, true);
            foreach (var evidence in new[] { sparse, new CandidateQuality { Available = true, Psnr = 99, Ssim = 1 } })
            {
                s.Format = TunerTextureFormat.BC1;
                Assert.IsTrue(AdaptivePlanner.Select(r, Policy(), null, s, SemanticCategory.Body, c => evidence).IsNoOp,
                    "a forced format cannot bypass missing opacity proof or one translucent texel");
            }
            r.SourceHasAlpha = r.AlphaChannelUsed = false;
            r.Usage = TextureUsageKind.AlbedoOpaque;
            Assert.IsTrue(AdaptivePlanner.Select(r, Policy(), null, s, SemanticCategory.Body, c => sparse).IsNoOp,
                "decoded alpha takes precedence over importer hints");
        }

        [Test] public static void LocalNormalDamageSurvivesWholeTextureAveraging()
        {
            const int width = 129, height = 65;
            var source = new byte[width * height * 4];
            for (int i = 0; i < source.Length; i += 4)
            {
                source[i] = source[i + 1] = 128;
                source[i + 2] = source[i + 3] = 255;
            }
            var damaged = (byte[])source.Clone();
            for (int y = 8; y < 16; y++)
                for (int x = 16; x < 24; x++)
                {
                    int offset = (y * width + x) * 4;
                    damaged[offset] = 255;
                    damaged[offset + 2] = 128;
                }
            var q = CandidateQuality.Compare(source, damaged, width, height, false, true);
            Assert.IsTrue(q.NormalAngularError < 1 && q.WorstLocalNormalAngularError > 80, "a small erased feature can pass a whole-image average");
            var r = Request();
            r.Usage = TextureUsageKind.NormalMap;
            r.ImporterTypeIsNormalMap = true;
            var s = new TextureSettings();
            foreach (var level in new[] { StandardOptimizationLevel.Normal, StandardOptimizationLevel.Maximum })
                Assert.IsTrue(AdaptivePlanner.Select(r, Policy(), null, s, SemanticCategory.Body, c => q, standardLevel: level).IsNoOp,
                    "localized lighting damage is rejected");
            r.ImporterTypeIsNormalMap = false;
            Assert.AreEqual(0, AdaptivePlanner.Candidates(r, Policy(), null, s, SemanticCategory.Body).Count, "unknown normal encoding stays untouched");
        }

        [Test] public static void StandardMeasuresSmallestPassingSizeFirst()
        {
            var measured = new List<TextureCandidate>();
            var r = Request();
            var plan = AdaptivePlanner.Select(r, Policy(), null, new TextureSettings(), SemanticCategory.Body, c =>
            {
                measured.Add(c);
                return new CandidateQuality { Available = true, Psnr = 99, Ssim = 1 };
            });
            Assert.AreEqual(1, measured.Count, "larger imports cannot improve the smallest passing recommendation");
            Assert.AreEqual(plan.TargetBytes, measured[0].Bytes, "selection used the measured result");
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
