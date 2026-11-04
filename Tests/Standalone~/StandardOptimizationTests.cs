using System;
using System.IO;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;

namespace AvatarTuner.StandaloneTests
{
    public static class StandardOptimizationTests
    {
        [Test("Maximum protects local color and used alpha features at its viewing scale")]
        public static void VisualScaleRetainsColorAndAlphaFeatures()
        {
            const int width = 1024, height = 128;
            var flat = new byte[width * height * 4];
            for (var i = 0; i < flat.Length; i++) flat[i] = 128;
            var settings = new OptimizationSettings { StandardLevel = StandardOptimizationLevel.Maximum }.ForCurrentMode();
            var request = Request();
            request.SourceWidth = width; request.SourceHeight = height; request.CurrentMaxSize = width;
            foreach (var alpha in new[] { false, true })
            {
                var source = (byte[])flat.Clone();
                for (var y = 32; y < 64; y++)
                    for (var x = 64; x < 96; x++)
                    {
                        var index = (y * width + x) * 4;
                        var sign = (x / 8 + y / 8) % 2 == 0 ? 1 : -1;
                        if (alpha) source[index + 3] = (byte)(128 + sign * 16);
                        else { source[index] = (byte)(128 + sign * 30); source[index + 1] = (byte)(128 - sign * 9); }
                    }
                var quality = CandidateQuality.Compare(source, flat, width, height, alpha);
                Assert.IsTrue(quality.Visual256.Psnr > 34 && quality.Visual256.Ssim > 0.99, "averages miss the erased feature");
                var plan = AdaptivePlanner.Select(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                    settings.Global, SemanticCategory.Body, candidate => quality, standardLevel: StandardOptimizationLevel.Maximum);
                Assert.IsTrue(plan.IsNoOp, alpha ? "used alpha feature survives" : "isoluminant color feature survives");
                if (alpha)
                {
                    var unused = CandidateQuality.Compare(source, flat, width, height, false);
                    var allowed = AdaptivePlanner.Select(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                        settings.Global, SemanticCategory.Body, candidate => unused, standardLevel: StandardOptimizationLevel.Maximum);
                    Assert.IsFalse(allowed.IsNoOp, "unused alpha detail does not block visual optimization");
                }
            }
        }

        [Test("Standard tiers reduce real resampled pixels despite isolated source-texel errors")]
        public static void ResampledPixelsHaveUsefulTierHeadroom()
        {
            const int width = 2048, height = 256;
            var source = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var index = (y * width + x) * 4;
                    var ink = y >= 96 && y < 160 && x > 450 + y / 3 && x < 1530 - y / 4;
                    source[index] = (byte)(ink ? 35 : 190 + x / 64);
                    source[index + 1] = (byte)(ink ? 45 : 170 + y / 8);
                    source[index + 2] = (byte)(ink ? 55 : 140 + x / 96);
                    source[index + 3] = 255;
                }
            var scores = new System.Collections.Generic.Dictionary<int, CandidateQuality>();
            foreach (var size in new[] { 2048, 1024, 512, 256, 128 })
            {
                var resized = TextureResampler.Resize(source, width, height, size, size * height / width, ResizeFilter.Area, true);
                var reconstructed = TextureResampler.Resize(resized, size, size * height / width, width, height, ResizeFilter.Mitchell, true);
                scores[size] = CandidateQuality.Compare(source, reconstructed, width, height, false);
            }
            var request = Request();
            request.SourceHeight = height;
            long previous = long.MaxValue;
            foreach (StandardOptimizationLevel level in Enum.GetValues(typeof(StandardOptimizationLevel)))
            {
                var settings = new OptimizationSettings { StandardLevel = level }.ForCurrentMode();
                var plan = AdaptivePlanner.Select(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                    settings.Global, SemanticCategory.Body, candidate => scores[candidate.Width], standardLevel: level);
                Console.WriteLine(level + " resampled fixture: " + plan.TargetWidth + "px");
                if (level == StandardOptimizationLevel.Normal) Assert.IsTrue(plan.TargetWidth <= 1024, "Normal reduces oversampled visual structure by at least one resolution step");
                Assert.IsTrue(plan.TargetBytes < previous, level + " provides additional memory savings from measured pixels");
                previous = plan.TargetBytes;
            }
            var erased = (byte[])source.Clone();
            for (var y = 96; y < 160; y++)
                for (var x = 500; x < 1450; x++)
                {
                    var index = (y * width + x) * 4;
                    erased[index] = 205; erased[index + 1] = 185; erased[index + 2] = 150;
                }
            var maximum = new OptimizationSettings { StandardLevel = StandardOptimizationLevel.Maximum }.ForCurrentMode();
            var lostFeature = CandidateQuality.Compare(source, erased, width, height, false);
            var rejected = AdaptivePlanner.Select(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                maximum.Global, SemanticCategory.Body, candidate => lostFeature, standardLevel: StandardOptimizationLevel.Maximum);
            Assert.IsTrue(rejected.IsNoOp, "Maximum retains a prominent feature instead of accepting a flat replacement");
        }

        [Test("Normal and Maximum accept progressively stronger candidates under realistic quality losses")]
        public static void TierSeparationWithQualityLoss()
        {
            var report = new AvatarAnalysisReport();
            report.Textures.Add(new TextureEntry { Guid = "body", Path = "Assets/body.png", Width = 2048, Height = 1024,
                CurrentFormatName = "RGBA32", CurrentFormatKnown = true, CurrentMipCount = 1,
                Usage = TextureUsageKind.AlbedoOpaque, Category = SemanticCategory.Body });
            var expected = new[] { 2048, 512, 128 };
            long previous = long.MaxValue;
            foreach (StandardOptimizationLevel level in Enum.GetValues(typeof(StandardOptimizationLevel)))
            {
                var settings = new OptimizationSettings { StandardLevel = level }.ForCurrentMode();
                var plan = TexturePlanBuilder.BuildAdaptive(report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, settings,
                    (entry, candidate) => candidate.Width >= 2048
                        ? new CandidateQuality { Available = true, Psnr = 60, Ssim = 0.999, MinLocalSsim = 1 }
                        : candidate.Width >= 512
                            ? new CandidateQuality { Available = true, Psnr = 43, Ssim = 0.975, MinLocalSsim = 0.89 }
                            : new CandidateQuality { Available = true, Psnr = 35, Ssim = 0.945, MinLocalSsim = 0.83 });
                Assert.AreEqual(expected[(int)level], plan.Textures[0].Plan.TargetWidth, level + " permits a stronger result");
                Assert.IsTrue(plan.TargetBytesTotal < previous, level + " memory decreases");
                previous = plan.TargetBytesTotal;
            }
            var maximum = new OptimizationSettings { StandardLevel = StandardOptimizationLevel.Maximum }.ForCurrentMode();
            var protectedDetail = TexturePlanBuilder.BuildAdaptive(report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, maximum,
                (entry, candidate) => new CandidateQuality { Available = true, Psnr = 60, Ssim = 1, MinLocalSsim = candidate.Width < 2048 ? 0.79 : 1 });
            Assert.AreEqual(2048, protectedDetail.Textures[0].Plan.TargetWidth, "strongest tier still rejects excessive local detail loss");
        }

        [Test("face and hair levels differ while generic and Advanced protections retain their existing limits")]
        public static void StrongerFaceAndHairLevels()
        {
            foreach (var category in new[] { SemanticCategory.Face, SemanticCategory.Hair })
            {
                var widths = category == SemanticCategory.Face ? new[] { 2048, 1024, 512 } : new[] { 2048, 512, 256 };
                foreach (StandardOptimizationLevel level in Enum.GetValues(typeof(StandardOptimizationLevel)))
                {
                    var settings = new OptimizationSettings { StandardLevel = level }.ForCurrentMode();
                    var plan = AdaptivePlanner.Select(Request(), PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, settings.Global, category,
                        candidate => candidate.Width >= 2048 ? new CandidateQuality { Available = true, Psnr = 60, Ssim = 1 } :
                            candidate.Width >= (category == SemanticCategory.Face ? 1024 : 512)
                                ? new CandidateQuality { Available = true, Psnr = 48, Ssim = 0.988, MinLocalSsim = 0.89 }
                                : new CandidateQuality { Available = true, Psnr = 43, Ssim = 0.975, MinLocalSsim = 0.84 }, standardLevel: level);
                    Assert.AreEqual(widths[(int)level], plan.TargetWidth, category + " " + level);
                }
            }
            var generic = AdaptivePlanner.Select(Request(), PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                new TextureSettings(), SemanticCategory.Face, candidate => new CandidateQuality { Available = true, Psnr = 48, Ssim = 0.988 });
            Assert.IsTrue(generic.IsNoOp, "without a Standard tier, the existing face quality floor stays in force");
        }

        [Test("Maximum keeps mask and packed-data quality and resolution protections")]
        public static void MaximumDataProtection()
        {
            var settings = new OptimizationSettings { StandardLevel = StandardOptimizationLevel.Maximum }.ForCurrentMode();
            var request = Request();
            foreach (var usage in new[] { TextureUsageKind.DataMask, TextureUsageKind.GreyscaleData, TextureUsageKind.NormalMap })
            foreach (var category in new[] { SemanticCategory.Masks, SemanticCategory.Body })
            {
                request.Usage = usage;
                request.ImporterTypeIsNormalMap = usage == TextureUsageKind.NormalMap;
                foreach (var candidate in AdaptivePlanner.Candidates(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                    settings.Global, category, StandardOptimizationLevel.Maximum))
                    Assert.IsTrue(candidate.MaxSize >= 256, "data floor is retained");
                var plan = AdaptivePlanner.Select(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                    settings.Global, category, candidate => candidate.Width >= 2048
                        ? new CandidateQuality { Available = true, Psnr = 60, Ssim = 1 }
                        : new CandidateQuality { Available = true, Psnr = 43, Ssim = 0.975, MinLocalSsim = 0.89, NormalAngularError = 4 }, standardLevel: StandardOptimizationLevel.Maximum);
                Assert.AreEqual(2048, plan.TargetWidth, "visual-tier relaxation does not weaken " + usage + " checks");
            }
            Assert.AreEqual(0, AdaptivePlanner.Candidates(Request(), PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                settings.Global, SemanticCategory.Body, (StandardOptimizationLevel)999).Count, "invalid tier is rejected");
        }

        [Test("Maximum retains a detailed texture when a smaller candidate erases a small feature")]
        public static void MaximumFindsContentDependentMinimum()
        {
            var flat = new byte[512 * 512 * 4];
            for (var i = 0; i < flat.Length; i += 4) { flat[i] = flat[i + 1] = flat[i + 2] = 128; flat[i + 3] = 255; }
            var detailed = (byte[])flat.Clone();
            for (var y = 128; y < 136; y++)
                for (var x = 128; x < 136; x++)
                {
                    var index = (y * 512 + x) * 4;
                    detailed[index] = detailed[index + 1] = detailed[index + 2] = (byte)(((x / 2) + (y / 2)) % 2 == 0 ? 120 : 136);
                }
            var blurred = CandidateQuality.Compare(detailed, flat, 512, 512, false);
            var perfect = CandidateQuality.Compare(detailed, detailed, 512, 512, false);
            Assert.IsTrue(blurred.Psnr > 50 && blurred.Ssim > 0.99, "whole-texture averages hide the small erased feature");
            var report = new AvatarAnalysisReport();
            foreach (var guid in new[] { "detail", "flat" }) report.Textures.Add(new TextureEntry
            {
                Guid = guid, Path = "Assets/" + guid + ".png", Width = 512, Height = 512,
                CurrentFormatName = "RGBA32", CurrentFormatKnown = true, CurrentMipCount = 1,
                Usage = TextureUsageKind.AlbedoOpaque, Category = SemanticCategory.Body
            });
            var settings = new OptimizationSettings { StandardLevel = StandardOptimizationLevel.Maximum }.ForCurrentMode();
            var plan = TexturePlanBuilder.BuildAdaptive(report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, settings,
                (entry, candidate) => entry.Guid == "detail" && candidate.Width < 512 ? blurred : perfect);
            Assert.AreEqual(512, plan.Textures[0].Plan.TargetWidth, "detailed texture retains its resolution while still allowing compression");
            Assert.AreEqual(128, plan.Textures[1].Plan.TargetWidth, "simple texture can shrink further");
            Assert.IsTrue(plan.Textures[0].Plan.Notes.Exists(note => note.Key == "plan.note.localDetailProtected"), "the retained detail is explained");
            Assert.IsTrue(blurred.MinLocalSsim < 0.9, "local feature damage is detected despite the good averages");
            var preserved = TexturePlanBuilder.BuildAdaptive(report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, settings,
                (entry, candidate) => blurred);
            Assert.IsTrue(preserved.Textures[0].Plan.IsNoOp, "original remains when no candidate preserves its detail");
            var advanced = new OptimizationSettings { Mode = OptimizationMode.Advanced, Global = settings.Global.Clone() };
            advanced.Global.DesiredReduction = 32;
            advanced.IncludedTextures.Add("detail");
            var manual = TexturePlanBuilder.BuildAdaptive(report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, advanced,
                (entry, candidate) => candidate.Width < 512 ? blurred : perfect);
            Assert.AreEqual(256, manual.Textures[0].Plan.TargetWidth, "Advanced retains its explicit manual quality/target behavior");
        }

        [Test("local detail comparison includes partial borders and narrow textures")]
        public static void LocalDetailIncludesBorders()
        {
            foreach (var height in new[] { 9, 1 })
            {
                const int width = 17;
                var reference = new byte[width * height * 4];
                for (var i = 0; i < reference.Length; i += 4) { reference[i] = reference[i + 1] = reference[i + 2] = 128; reference[i + 3] = 255; }
                var candidate = (byte[])reference.Clone();
                for (var y = 0; y < height; y++)
                {
                    var index = (y * width + width - 1) * 4;
                    reference[index] = reference[index + 1] = reference[index + 2] = (byte)(y % 2 == 0 ? 64 : 192);
                }
                double minimum;
                var average = QualityMetrics.Ssim(reference, candidate, width, height, 4, out minimum);
                Assert.IsTrue(minimum < average && minimum < 0.9, "partial border is compared at height " + height);
                Assert.AreClose(1, QualityMetrics.Ssim(reference, reference, width, height, 4, out minimum), 0.000001, "identical average");
                Assert.AreClose(1, minimum, 0.000001, "identical local score");
            }
        }

        [Test("local detail detects erased color features with unchanged brightness")]
        public static void LocalColorDetail()
        {
            const int size = 128;
            var flat = new byte[size * size * 4];
            for (var i = 0; i < flat.Length; i += 4) { flat[i] = flat[i + 1] = flat[i + 2] = 128; flat[i + 3] = 255; }
            var reference = (byte[])flat.Clone();
            for (var y = 32; y < 40; y++)
                for (var x = 32; x < 40; x++)
                {
                    var index = (y * size + x) * 4;
                    var sign = (x + y) % 2 == 0 ? 1 : -1;
                    reference[index] = (byte)(128 + sign * 30);
                    reference[index + 1] = (byte)(128 - sign * 9);
                }
            var quality = CandidateQuality.Compare(reference, flat, size, size, false);
            Assert.IsTrue(quality.Psnr > 45 && quality.Ssim > 0.99, "overall brightness metrics accept the lost color patch");
            Assert.IsTrue(quality.MinLocalSsim < 0.9, "per-channel local comparison catches lost color");
        }

        [Test("local detail checks transparency only when alpha is used")]
        public static void LocalAlphaDetail()
        {
            const int size = 128;
            var reference = new byte[size * size * 4];
            for (var i = 0; i < reference.Length; i += 4) { reference[i] = reference[i + 1] = reference[i + 2] = 128; reference[i + 3] = 255; }
            var blurred = (byte[])reference.Clone();
            for (var y = 32; y < 40; y++)
                for (var x = 32; x < 40; x++)
                {
                    var index = (y * size + x) * 4 + 3;
                    reference[index] = (byte)((x + y) % 2 == 0 ? 112 : 144);
                    blurred[index] = 128;
                }
            var quality = CandidateQuality.Compare(reference, blurred, size, size, true);
            Assert.IsTrue(quality.Psnr > 45 && quality.Ssim > 0.99 && quality.AlphaCoverageError < 0.02, "averages miss small alpha detail loss");
            Assert.IsTrue(quality.MinLocalSsim < 0.9, "used alpha detail is protected");
            Assert.AreClose(1, CandidateQuality.Compare(reference, blurred, size, size, false).MinLocalSsim, 0.000001,
                "unused alpha does not impose local transparency restrictions");
        }

        [Test("invalid local similarity metrics cannot authorize optimization")]
        public static void InvalidLocalQuality()
        {
            foreach (var value in new[] { double.NaN, double.PositiveInfinity, 1.1 })
            {
                var plan = AdaptivePlanner.Select(Request(), PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                    new TextureSettings(), SemanticCategory.Body,
                    candidate => new CandidateQuality { Available = true, Psnr = 100, Ssim = 1, MinLocalSsim = value }, standardLevel: StandardOptimizationLevel.Normal);
                Assert.IsTrue(plan.IsNoOp, "invalid local score is rejected");
            }
        }

        [Test("invalid viewing metrics are rejected and good views cannot weaken data checks")]
        public static void VisualEvidenceCannotBypassValidation()
        {
            var settings = new OptimizationSettings { StandardLevel = StandardOptimizationLevel.Maximum }.ForCurrentMode();
            var request = Request();
            var invalid = new CandidateQuality { Available = true, Psnr = 60, Ssim = 1, Visual256 =
                new CandidateQuality { Available = true, Psnr = 60, Ssim = 1, MinLocalSsim = double.NaN } };
            var plan = AdaptivePlanner.Select(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                settings.Global, SemanticCategory.Body, candidate => invalid, standardLevel: StandardOptimizationLevel.Maximum);
            Assert.IsTrue(plan.IsNoOp, "invalid viewing evidence cannot authorize a candidate");
            request.Usage = TextureUsageKind.DataMask;
            var data = new CandidateQuality { Available = true, Psnr = 38, Ssim = 0.95, MaxChannelError = 0.3, Visual512 =
                new CandidateQuality { Available = true, Psnr = 60, Ssim = 1, MinLocalSsim = 1 } };
            var minimum = new OptimizationSettings { StandardLevel = StandardOptimizationLevel.Minimum }.ForCurrentMode();
            plan = AdaptivePlanner.Select(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                minimum.Global, SemanticCategory.Body, candidate => data, standardLevel: StandardOptimizationLevel.Minimum);
            Assert.IsTrue(plan.IsNoOp, "Minimum judges data textures by raw texels, never a viewing scale");
        }

        [Test("Unknown-category, emission and lilToon layer/mask textures are optimizable")]
        public static void CommonAvatarTexturesAreCandidates()
        {
            var settings = new OptimizationSettings { StandardLevel = StandardOptimizationLevel.Normal }.ForCurrentMode();
            var policy = PresetCatalog.GetPolicy(OptimizationPreset.Balanced);
            var request = Request();
            request.CurrentFormat = TunerTextureFormat.RGB24;
            Assert.IsTrue(AdaptivePlanner.Candidates(request, policy, null, settings.Global, SemanticCategory.Unknown,
                StandardOptimizationLevel.Normal).Exists(c => c.Format == TunerTextureFormat.BC1), "uncompressed unknown texture gets BC1");
            request.Usage = TextureUsageKind.Emission;
            Assert.IsTrue(AdaptivePlanner.Candidates(request, policy, null, settings.Global, SemanticCategory.Body,
                StandardOptimizationLevel.Normal).Count > 0, "LDR emission is a color texture");
            Assert.AreEqual(TextureUsageKind.AlbedoOpaque, TextureUsageClassifier.Classify("_Main2ndTex", "lilToon", "gif", false, false, false, null), "lilToon 2nd layer");
            foreach (var mask in new[] { "_MatCapBlendMask", "_ShadowBorderMask", "_OutlineWidthMask" })
                Assert.AreEqual(TextureUsageKind.DataMask, TextureUsageClassifier.Classify(mask, "lilToon", "mask", false, false, false, null), mask);
        }

        [Test("Normal maps are judged by lighting-angle error that grows with the tier")]
        public static void NormalMapAngleTiers()
        {
            var request = Request();
            request.Usage = TextureUsageKind.NormalMap;
            request.ImporterTypeIsNormalMap = true;
            request.CurrentSRGB = false;
            request.CurrentFormat = TunerTextureFormat.BC3;
            // Typical half-resolution normal map: low channel PSNR, small angle change.
            var halved = new CandidateQuality { Available = true, Psnr = 33, Ssim = 0.94, MinLocalSsim = 0, NormalAngularError = 1.5 };
            foreach (StandardOptimizationLevel level in Enum.GetValues(typeof(StandardOptimizationLevel)))
            {
                var settings = new OptimizationSettings { StandardLevel = level }.ForCurrentMode();
                var plan = AdaptivePlanner.Select(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                    settings.Global, SemanticCategory.Masks, candidate => halved, standardLevel: level);
                Assert.AreEqual(level == StandardOptimizationLevel.Minimum, plan.IsNoOp, level + " normal map");
            }
            var bent = new CandidateQuality { Available = true, Psnr = 60, Ssim = 1, MinLocalSsim = 1, NormalAngularError = 4 };
            var max = new OptimizationSettings { StandardLevel = StandardOptimizationLevel.Maximum }.ForCurrentMode();
            Assert.IsTrue(AdaptivePlanner.Select(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                max.Global, SemanticCategory.Masks, candidate => bent, standardLevel: StandardOptimizationLevel.Maximum).IsNoOp, "Maximum still caps angle error");
        }

        [Test("Normal maps decode identically from BC5 and DXT5nm encodings")]
        public static void NormalEncodingsDecodeAlike()
        {
            var bc5 = QualityMetrics.UnpackNormals(new byte[] { 200, 90, 0, 255 });
            var dxt5nm = QualityMetrics.UnpackNormals(new byte[] { 255, 90, 255, 200 });
            for (var c = 0; c < 4; c++) Assert.AreEqual(bc5[c], dxt5nm[c], "channel " + c);
            Assert.IsTrue(bc5[2] > 200, "z is rebuilt and points outward");
        }

        [Test("Standard level labels and descriptions exist in all supported languages")]
        public static void LevelTranslations()
        {
            foreach (var code in new[] { "en", "th", "zh-Hans" })
            {
                var catalog = TranslationCatalog.FromJson(File.ReadAllText(PackageLayout.TranslationPath(code)), code);
                Assert.IsTrue(catalog.Contains("workspace.standardLevel"), code + " level heading");
                foreach (StandardOptimizationLevel level in Enum.GetValues(typeof(StandardOptimizationLevel)))
                    foreach (var suffix in new[] { ".name", ".description" })
                        Assert.IsTrue(catalog.Contains("workspace.standardLevel." + level + suffix), code + " " + level + suffix);
            }
        }

        private static TexturePlanRequest Request()
        {
            return new TexturePlanRequest { SourceWidth = 2048, SourceHeight = 1024,
                CurrentFormat = TunerTextureFormat.RGBA32, CurrentFormatKnown = true,
                CurrentMaxSize = 2048, CurrentMipMaps = false, Usage = TextureUsageKind.AlbedoOpaque };
        }

        [Test("Standard levels progressively widen resolution reduction and compression")]
        public static void ProgressiveLevels()
        {
            long previous = long.MaxValue;
            var widths = new[] { 1024, 512, 128 };
            foreach (StandardOptimizationLevel level in Enum.GetValues(typeof(StandardOptimizationLevel)))
            {
                var settings = new OptimizationSettings { StandardLevel = level }.ForCurrentMode();
                var plan = AdaptivePlanner.Select(Request(), PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                    settings.Global, SemanticCategory.Body, candidate => new CandidateQuality { Available = true, Psnr = 100, Ssim = 1 }, standardLevel: level);
                Assert.IsFalse(plan.IsNoOp, level + " compresses known color textures");
                Assert.AreEqual(widths[(int)level], plan.TargetWidth, level + " resolution limit");
                Assert.IsTrue(plan.TargetBytes < previous, level + " has more scope for savings");
                previous = plan.TargetBytes;
            }
        }

        [Test("Maximum relaxes quality thresholds while protected textures remain protected")]
        public static void QualityAndProtection()
        {
            foreach (StandardOptimizationLevel level in Enum.GetValues(typeof(StandardOptimizationLevel)))
            {
                var settings = new OptimizationSettings { StandardLevel = level };
                settings.Textures["protected"] = new TextureSettings { ProtectQuality = true };
                var effective = settings.ForCurrentMode();
                var plan = AdaptivePlanner.Select(Request(), PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                    effective.Global, SemanticCategory.Body, candidate => new CandidateQuality { Available = true, Psnr = 40, Ssim = 0.975 });
                Assert.AreEqual(level != StandardOptimizationLevel.Maximum, plan.IsNoOp, level + " quality threshold");
                Assert.IsTrue(effective.Resolve("protected", SemanticCategory.Body).Settings.ProtectQuality, "protection survives " + level);
            }
        }

        [Test("Standard level persists, invalidates plans and defaults safely")]
        public static void PersistenceAndFreshness()
        {
            var settings = new OptimizationSettings();
            Assert.AreEqual(StandardOptimizationLevel.Normal, settings.StandardLevel, "default is Normal");
            var before = settings.ForCurrentMode().ToHashToken();
            settings.StandardLevel = StandardOptimizationLevel.Maximum;
            Assert.AreNotEqual(before, settings.ForCurrentMode().ToHashToken(), "level invalidates old plan");
            var copy = OptimizationSettings.FromDictionary(settings.ToDictionary());
            Assert.AreEqual(StandardOptimizationLevel.Maximum, copy.StandardLevel, "level round trips");
            Assert.AreEqual(settings.ToHashToken(), copy.ToHashToken(), "hash round trips");
            var legacy = settings.ToDictionary();
            legacy.Remove("standardLevel");
            Assert.AreEqual(StandardOptimizationLevel.Normal, OptimizationSettings.FromDictionary(legacy).StandardLevel, "old settings use Normal");
            legacy["standardLevel"] = "invalid";
            Assert.AreEqual(StandardOptimizationLevel.Normal, OptimizationSettings.FromDictionary(legacy).StandardLevel, "invalid level uses Normal");
            var limited = new TextureSettings { MaxDownscaleSteps = 1 };
            Assert.AreEqual(limited.ToHashToken(), TextureSettings.FromDictionary(limited.ToDictionary()).ToHashToken(), "resolution constraint round trips");
        }

        [Test("invalid resolution caps are rejected before bit shifting")]
        public static void InvalidResolutionCaps()
        {
            foreach (var steps in new[] { -2, 31, int.MaxValue })
            {
                var invalid = new TextureSettings { MaxDownscaleSteps = steps };
                Assert.AreEqual(0, AdaptivePlanner.Candidates(Request(), PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null,
                    invalid, SemanticCategory.Body).Count, "invalid cap does not wrap " + steps);
                Assert.AreEqual(-1, TextureSettings.FromDictionary(invalid.ToDictionary()).MaxDownscaleSteps, "invalid stored cap defaults safely");
            }
        }

        [Test("Standard levels do not change saved Advanced tuning")]
        public static void AdvancedRemainsIndependent()
        {
            var settings = new OptimizationSettings { Mode = OptimizationMode.Advanced };
            settings.Global.DesiredReduction = 3.5;
            settings.Global.Format = TunerTextureFormat.BC7;
            foreach (StandardOptimizationLevel level in Enum.GetValues(typeof(StandardOptimizationLevel)))
            {
                settings.StandardLevel = level;
                var effective = settings.ForCurrentMode();
                Assert.AreEqual(3.5, effective.Global.DesiredReduction, "manual target is retained");
                Assert.AreEqual(TunerTextureFormat.BC7, effective.Global.Format, "manual format is retained");
                Assert.AreEqual(-1, effective.Global.MaxDownscaleSteps, "no Standard resolution cap leaks into Advanced");
            }
        }
    }
}
