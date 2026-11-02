using System;
using System.Collections.Generic;
using AvatarTuner.Core;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Tests for the texture decision engine. These pin down the safety rules the product
    /// specification calls out: exclusions win, detail textures are preserved, memory never
    /// grows, and quality figures are never invented.
    /// </summary>
    public static class CompressionPlannerTests
    {
        private static TexturePlanRequest NewRequest(int width, int height, TunerTextureFormat current)
        {
            var request = new TexturePlanRequest();
            request.SourceWidth = width;
            request.SourceHeight = height;
            request.CurrentFormat = current;
            request.CurrentFormatKnown = current != TunerTextureFormat.Automatic && current != TunerTextureFormat.Unknown;
            request.CurrentMaxSize = 0;
            request.CurrentMipMaps = true;
            request.CurrentSRGB = true;
            request.Usage = TextureUsageKind.AlbedoOpaque;
            request.SourceHasAlpha = false;
            request.AlphaChannelUsed = false;
            request.FaceCount = 1;
            return request;
        }

        private static bool HasNote(TexturePlan plan, string key)
        {
            for (var i = 0; i < plan.Notes.Count; i++)
            {
                if (plan.Notes[i].Key == key)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasWarning(TexturePlan plan, string key)
        {
            for (var i = 0; i < plan.Notes.Count; i++)
            {
                if (plan.Notes[i].Key == key && plan.Notes[i].Level == PlanNote.Severity.Warning)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------ safety rules

        [Test("an excluded texture is never touched")]
        public static void ExcludedTexturesAreUntouched()
        {
            var request = NewRequest(2048, 2048, TunerTextureFormat.RGBA32);
            var ovr = new TextureOverride();
            ovr.Excluded = true;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.SizeFirst), ovr, null);

            Assert.IsTrue(plan.IsNoOp, "excluded textures produce no work");
            Assert.IsTrue(plan.IsExcluded, "the plan is flagged as excluded");
            Assert.AreEqual("plan.skip.excluded", plan.SkipReasonKey, "skip reason names the exclusion");
            Assert.AreEqual(TunerTextureFormat.RGBA32, plan.TargetFormat, "format is unchanged");
            Assert.AreEqual(2048, plan.TargetWidth, "resolution is unchanged");
            Assert.IsTrue(HasNote(plan, "plan.note.excludedByUser"), "the plan explains the exclusion");
        }

        [Test("a user pinned texture keeps its resolution and format")]
        public static void PinnedTexturesAreUnchanged()
        {
            var request = NewRequest(2048, 2048, TunerTextureFormat.RGBA32);
            var ovr = new TextureOverride();
            ovr.KeepResolution = true;
            ovr.KeepFormat = true;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.SizeFirst), ovr, null);

            Assert.AreEqual(TunerTextureFormat.RGBA32, plan.TargetFormat, "pinned format is kept");
            Assert.AreEqual(2048, plan.TargetWidth, "pinned resolution is kept");
            Assert.IsFalse(plan.FormatChanged, "no format change");
            Assert.IsFalse(plan.ResolutionChanged, "no resolution change");
            Assert.IsTrue(plan.IsNoOp, "a fully pinned texture is a no-op");
        }

        [Test("memory use never increases")]
        public static void MemoryNeverIncreases()
        {
            // BC1 is already the cheapest colour format; proposing BC7 would cost more.
            var request = NewRequest(1024, 1024, TunerTextureFormat.BC1);
            request.Usage = TextureUsageKind.AlbedoOpaque;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.QualityFirst), null, null);

            Assert.AreEqual(TunerTextureFormat.BC1, plan.TargetFormat, "a cheaper existing format is not replaced by a bigger one");
            Assert.IsTrue(plan.SavingBytes <= 0 || plan.IsNoOp, "the plan must not claim a saving while growing the texture");
        }

        [Test("face, eye and small text textures keep their resolution")]
        public static void DetailTexturesKeepResolution()
        {
            var request = NewRequest(2048, 2048, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.AlbedoOpaque;
            request.Evidence = new ClassificationEvidence();
            request.Evidence.NameIndicatesFace = true;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.SizeFirst), null, null);

            Assert.IsTrue(plan.IsDetailPreserving, "the texture is flagged as detail preserving");
            Assert.AreEqual(2048, plan.TargetWidth, "resolution is preserved");
            Assert.IsTrue(HasNote(plan, "plan.note.resolutionKeptDetailPreserving"), "the plan explains why");
        }

        [Test("UI textures are always detail preserving")]
        public static void UiTexturesAreDetailPreserving()
        {
            var request = NewRequest(512, 512, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.UserInterface;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.SizeFirst), null, null);

            Assert.IsTrue(plan.IsDetailPreserving, "UI textures never lose resolution");
            Assert.AreEqual(512, plan.TargetWidth, "resolution is preserved");
        }

        // --------------------------------------------------------------- formatting

        [Test("normal maps are routed to BC5")]
        public static void NormalMapsGoToBc5()
        {
            var request = NewRequest(1024, 1024, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.NormalMap;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.QualityFirst), null, null);

            Assert.AreEqual(TunerTextureFormat.BC5, plan.TargetFormat, "normal maps use BC5");
            Assert.IsTrue(plan.FormatChanged, "the format changes from RGBA32");
            Assert.IsGreater(0.0, plan.SavingBytes, "BC5 costs less than RGBA32");
        }

        [Test("greyscale data maps are routed to BC4")]
        public static void GreyscaleMapsGoToBc4()
        {
            var request = NewRequest(512, 512, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.GreyscaleData;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.QualityFirst), null, null);

            Assert.AreEqual(TunerTextureFormat.BC4, plan.TargetFormat, "single channel data uses BC4");
        }

        [Test("opaque albedo picks BC1 only when aggressive formats are allowed")]
        public static void OpaqueAlbedoFormatChoice()
        {
            var qualityFirst = CompressionPlanner.Plan(
                NewRequest(1024, 1024, TunerTextureFormat.RGBA32),
                PresetCatalog.GetPolicy(OptimizationPreset.QualityFirst), null, null);
            Assert.AreEqual(TunerTextureFormat.BC7, qualityFirst.TargetFormat,
                "Quality First avoids BC1 for colour maps");

            var sizeFirst = CompressionPlanner.Plan(
                NewRequest(1024, 1024, TunerTextureFormat.RGBA32),
                PresetCatalog.GetPolicy(OptimizationPreset.SizeFirst), null, null);
            Assert.AreEqual(TunerTextureFormat.BC1, sizeFirst.TargetFormat,
                "Size First may use BC1 for alpha-free colour maps");
        }

        [Test("alpha-carrying albedo never loses its alpha silently")]
        public static void AlphaIsNeverLostSilently()
        {
            var request = NewRequest(1024, 1024, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.AlbedoWithAlpha;
            request.SourceHasAlpha = true;
            request.AlphaChannelUsed = true;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.SizeFirst), null, null);

            Assert.IsTrue(
                TextureFormatInfo.SupportsAlpha(plan.TargetFormat),
                "a texture with used alpha must land on a format that stores alpha, got " + plan.TargetFormat);
            Assert.IsFalse(HasWarning(plan, "plan.warning.alphaLost"), "no alpha loss warning is needed when alpha survives");
        }

        [Test("a forced format that cannot hold alpha produces a warning")]
        public static void ForcedFormatLosingAlphaWarns()
        {
            var request = NewRequest(1024, 1024, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.AlbedoWithAlpha;
            request.SourceHasAlpha = true;
            request.AlphaChannelUsed = true;

            var ovr = new TextureOverride();
            ovr.ForceFormat = true;
            ovr.ForcedFormat = TunerTextureFormat.RGB24;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), ovr, null);

            Assert.AreEqual(TunerTextureFormat.RGB24, plan.TargetFormat, "the explicit override is honoured");
            Assert.IsTrue(HasWarning(plan, "plan.warning.alphaLost"), "the user is told the alpha will be dropped");
        }

        [Test("a forced format that is not supported on PC is rejected")]
        public static void ForcedUnsupportedFormatIsRejected()
        {
            var request = NewRequest(1024, 1024, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.NormalMap;

            var ovr = new TextureOverride();
            ovr.ForceFormat = true;
            ovr.ForcedFormat = TunerTextureFormat.ETC2_RGBA8;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), ovr, null);

            Assert.AreNotEqual(TunerTextureFormat.ETC2_RGBA8, plan.TargetFormat, "mobile formats are refused for PC output");
            Assert.AreEqual(TunerTextureFormat.BC5, plan.TargetFormat, "the planner falls back to its own choice");
        }

        // ---------------------------------------------------------------- resolution

        [Test("Quality First never reduces resolution")]
        public static void QualityFirstKeepsResolution()
        {
            var request = NewRequest(2048, 2048, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.AlbedoOpaque;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.QualityFirst), null, null);

            Assert.IsFalse(plan.ResolutionChanged, "Quality First keeps every texture at full resolution");
            Assert.IsTrue(HasNote(plan, "plan.note.downscaleDisabledByPreset"), "the reason is recorded");
        }

        [Test("Balanced reduces resolution by at most one ladder step")]
        public static void BalancedReducesOneStep()
        {
            var request = NewRequest(4096, 4096, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.AlbedoOpaque;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, null);

            Assert.IsTrue(plan.ResolutionChanged, "Balanced is allowed to downscale");
            Assert.AreEqual(2048, plan.TargetMaxSize, "Balanced takes a single step down the ladder");
        }

        [Test("Size First reduces resolution by up to two ladder steps")]
        public static void SizeFirstReducesTwoSteps()
        {
            var request = NewRequest(4096, 4096, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.AlbedoOpaque;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.SizeFirst), null, null);

            Assert.AreEqual(1024, plan.TargetMaxSize, "Size First takes two steps down the ladder");
        }

        [Test("the preset floor is never crossed")]
        public static void PresetFloorIsRespected()
        {
            var request = NewRequest(1024, 1024, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.AlbedoOpaque;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, null);

            Assert.AreEqual(1024, plan.TargetMaxSize, "Balanced never goes below 1024");
            Assert.IsFalse(plan.ResolutionChanged, "nothing to reduce");
        }

        [Test("high detail images are not downscaled even when the preset allows it")]
        public static void HighDetailImagesKeepResolution()
        {
            var request = NewRequest(4096, 4096, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.AlbedoOpaque;

            var evidence = new QualityEvidence();
            evidence.HasDetailScore = true;
            evidence.DetailScore = 0.95;
            evidence.HasEstimatedPsnr = true;
            evidence.EstimatedPsnrDb = 55.0;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, evidence);

            Assert.IsFalse(plan.ResolutionChanged, "a high detail image keeps its resolution");
            Assert.IsTrue(HasNote(plan, "plan.note.resolutionKeptHighDetail"), "the detail score is quoted in the reason");
        }

        [Test("low detail images may be downscaled")]
        public static void LowDetailImagesMayDownscale()
        {
            var request = NewRequest(4096, 4096, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.AlbedoOpaque;

            var evidence = new QualityEvidence();
            evidence.HasDetailScore = true;
            evidence.DetailScore = 0.05;
            evidence.HasEstimatedPsnr = true;
            evidence.EstimatedPsnrDb = 55.0;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, evidence);

            Assert.IsTrue(plan.ResolutionChanged, "a smooth image may be downscaled");
            Assert.AreEqual(2048, plan.TargetWidth, "one step down the ladder");
        }

        // ------------------------------------------------------------------ honesty

        [Test("no quality number is produced when none was derived")]
        public static void NoInventedQualityNumbers()
        {
            var request = NewRequest(1024, 1024, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.AlbedoOpaque;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, null);

            Assert.IsFalse(plan.HasEstimatedPsnr, "no estimated PSNR when nothing was computed");
            Assert.IsFalse(plan.HasMeasuredPsnr, "no measured PSNR when nothing was measured");
            Assert.AreEqual(QualityConfidence.Unknown, plan.Confidence, "confidence reflects the absence of evidence");
            Assert.IsTrue(HasWarning(plan, "plan.warning.noQualityEvidence"), "the user is told the evidence is missing");
        }

        [Test("an estimated figure lowers the confidence compared with a measured one")]
        public static void ConfidenceReflectsEvidenceQuality()
        {
            var request = NewRequest(1024, 1024, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.AlbedoOpaque;

            var estimated = new QualityEvidence();
            estimated.HasEstimatedPsnr = true;
            estimated.EstimatedPsnrDb = 60.0;
            var estimatedPlan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, estimated);
            Assert.AreEqual(QualityConfidence.Medium, estimatedPlan.Confidence, "an estimate gives medium confidence");
            Assert.IsTrue(estimatedPlan.HasEstimatedPsnr, "the estimate is reported");
            Assert.IsFalse(estimatedPlan.HasMeasuredPsnr, "an estimate is never reported as a measurement");

            var measured = new QualityEvidence();
            measured.HasMeasuredPsnr = true;
            measured.MeasuredPsnrDb = 60.0;
            var measuredPlan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, measured);
            Assert.AreEqual(QualityConfidence.High, measuredPlan.Confidence, "a measurement gives high confidence");
            Assert.IsTrue(measuredPlan.HasMeasuredPsnr, "the measurement is reported");
        }

        [Test("a measured figure below the preset threshold produces a warning")]
        public static void LowMeasuredQualityWarns()
        {
            var request = NewRequest(1024, 1024, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.AlbedoOpaque;

            var evidence = new QualityEvidence();
            evidence.HasMeasuredPsnr = true;
            evidence.MeasuredPsnrDb = 20.0;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, evidence);

            Assert.IsTrue(HasWarning(plan, "plan.warning.measuredQualityBelowThreshold"), "a poor measurement is flagged");
        }

        [Test("resolution changes are never given a PSNR figure")]
        public static void ResolutionChangesAreNotQuantified()
        {
            var request = NewRequest(4096, 4096, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.AlbedoOpaque;

            var evidence = new QualityEvidence();
            evidence.HasDetailScore = true;
            evidence.DetailScore = 0.02;
            evidence.HasEstimatedPsnr = true;
            evidence.EstimatedPsnrDb = 70.0;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, evidence);

            Assert.IsTrue(plan.ResolutionChanged, "the texture is downscaled");
            Assert.IsTrue(HasWarning(plan, "plan.warning.resolutionChangeNotQuantified"),
                "the user is told the resolution loss is not quantified");
        }

        [Test("an unknown current format is reported as unknown rather than guessed")]
        public static void UnknownCurrentFormatIsReported()
        {
            var request = NewRequest(1024, 1024, TunerTextureFormat.Automatic);
            request.CurrentFormatKnown = false;
            request.Usage = TextureUsageKind.AlbedoOpaque;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, null);

            Assert.IsTrue(plan.SavingUnknown, "savings cannot be computed without knowing the source format");
            Assert.IsTrue(HasWarning(plan, "plan.note.currentFormatUnknown"), "the uncertainty is surfaced");
        }

        // ------------------------------------------------------------- determinism

        [Test("planning is deterministic")]
        public static void PlanningIsDeterministic()
        {
            var policy = PresetCatalog.GetPolicy(OptimizationPreset.Balanced);

            var request = NewRequest(2048, 2048, TunerTextureFormat.RGBA32);
            request.Usage = TextureUsageKind.AlbedoWithAlpha;
            request.SourceHasAlpha = true;
            request.AlphaChannelUsed = true;

            var evidence = new QualityEvidence();
            evidence.HasDetailScore = true;
            evidence.DetailScore = 0.2;
            evidence.HasEstimatedPsnr = true;
            evidence.EstimatedPsnrDb = 52.0;

            var first = CompressionPlanner.Plan(request, policy, null, evidence);
            var second = CompressionPlanner.Plan(request, policy, null, evidence);

            Assert.AreEqual(first.ToHashToken(), second.ToHashToken(), "identical inputs produce identical plans");
            Assert.AreEqual(first.TargetFormat, second.TargetFormat, "format is stable");
            Assert.AreEqual(first.TargetMaxSize, second.TargetMaxSize, "max size is stable");
        }

        [Test("invalid requests are refused without throwing")]
        public static void InvalidRequestsAreRefused()
        {
            var request = new TexturePlanRequest();
            request.SourceWidth = 0;
            request.SourceHeight = 0;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, null);
            Assert.IsTrue(plan.IsNoOp, "an invalid request produces no work");
            Assert.AreEqual("plan.skip.invalidRequest", plan.SkipReasonKey, "the skip reason is recorded");
        }

        [Test("a missing policy or request throws rather than guessing")]
        public static void NullArgumentsThrow()
        {
            Assert.Throws<ArgumentNullException>(
                delegate { CompressionPlanner.Plan(null, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, null); },
                "null request");
            Assert.Throws<ArgumentNullException>(
                delegate { CompressionPlanner.Plan(NewRequest(64, 64, TunerTextureFormat.RGBA32), null, null, null); },
                "null policy");
        }

        // ------------------------------------------------------------------ presets

        [Test("presets have distinct, stable identifiers")]
        public static void PresetIdentifiersAreStable()
        {
            Assert.AreEqual("quality-first", PresetCatalog.GetId(OptimizationPreset.QualityFirst), "quality-first id");
            Assert.AreEqual("balanced", PresetCatalog.GetId(OptimizationPreset.Balanced), "balanced id");
            Assert.AreEqual("size-first", PresetCatalog.GetId(OptimizationPreset.SizeFirst), "size-first id");
            Assert.AreEqual(3, PresetCatalog.All.Length, "three shipping presets");
        }

        [Test("preset policies are ordered by aggressiveness")]
        public static void PresetAggressivenessOrdering()
        {
            var quality = PresetCatalog.GetPolicy(OptimizationPreset.QualityFirst);
            var balanced = PresetCatalog.GetPolicy(OptimizationPreset.Balanced);
            var size = PresetCatalog.GetPolicy(OptimizationPreset.SizeFirst);

            Assert.IsFalse(quality.AllowDownscale, "Quality First never downscales");
            Assert.IsTrue(balanced.AllowDownscale, "Balanced may downscale");
            Assert.IsTrue(size.AllowDownscale, "Size First may downscale");

            Assert.IsFalse(quality.AllowAggressiveFormats, "Quality First avoids weak formats");
            Assert.IsTrue(size.AllowAggressiveFormats, "Size First may use weak formats");

            Assert.IsGreater(size.MinEstimatedQualityDb, quality.MinEstimatedQualityDb,
                "Quality First demands a higher quality floor than Size First");
            Assert.IsGreater(quality.MaxDetailScoreForDownscale, size.MaxDetailScoreForDownscale,
                "Size First tolerates more detail before refusing to downscale");
            Assert.IsLess(balanced.MinAllowedMaxSize, size.MinAllowedMaxSize,
                "Size First has a lower resolution floor than Balanced");
        }

        [Test("every preset protects detail textures by default")]
        public static void EveryPresetProtectsDetailTextures()
        {
            foreach (var preset in PresetCatalog.All)
            {
                var policy = PresetCatalog.GetPolicy(preset);
                Assert.IsFalse(policy.AllowDownscaleOfDetailTextures,
                    "preset " + preset + " must not downscale faces, eyes, text or UI by default");
            }
        }

        [Test("preset policy hash changes when a rule changes")]
        public static void PolicyHashIsSensitive()
        {
            var baseline = PresetCatalog.GetPolicy(OptimizationPreset.Balanced);
            var modified = baseline.Clone();
            modified.MinEstimatedQualityDb = baseline.MinEstimatedQualityDb + 1;

            Assert.AreNotEqual(
                PresetCatalog.ToHashToken(baseline),
                PresetCatalog.ToHashToken(modified),
                "changing a rule must change the configuration hash so outputs are invalidated");

            Assert.AreEqual(
                PresetCatalog.ToHashToken(baseline),
                PresetCatalog.ToHashToken(baseline.Clone()),
                "an unchanged copy hashes identically");
        }

        [Test("texture overrides round trip through JSON")]
        public static void OverrideRoundTrips()
        {
            var ovr = new TextureOverride();
            ovr.Excluded = true;
            ovr.KeepResolution = true;
            ovr.KeepFormat = false;
            ovr.ForceFormat = true;
            ovr.ForcedFormat = TunerTextureFormat.BC3;
            ovr.ForceMaxSize = true;
            ovr.ForcedMaxSize = 512;
            ovr.ForceMipMaps = true;
            ovr.ForcedMipMaps = false;
            ovr.Note = "hand tuned";

            var json = MiniJson.Serialize(ovr.ToDictionary(), false);
            var restored = TextureOverride.FromDictionary(MiniJson.ParseObject(json));

            Assert.AreEqual(ovr.Excluded, restored.Excluded, "excluded");
            Assert.AreEqual(ovr.KeepResolution, restored.KeepResolution, "keepResolution");
            Assert.AreEqual(ovr.KeepFormat, restored.KeepFormat, "keepFormat");
            Assert.AreEqual(ovr.ForceFormat, restored.ForceFormat, "forceFormat");
            Assert.AreEqual(ovr.ForcedFormat, restored.ForcedFormat, "forcedFormat");
            Assert.AreEqual(ovr.ForceMaxSize, restored.ForceMaxSize, "forceMaxSize");
            Assert.AreEqual(ovr.ForcedMaxSize, restored.ForcedMaxSize, "forcedMaxSize");
            Assert.AreEqual(ovr.ForceMipMaps, restored.ForceMipMaps, "forceMipMaps");
            Assert.AreEqual(ovr.ForcedMipMaps, restored.ForcedMipMaps, "forcedMipMaps");
            Assert.AreEqual(ovr.Note, restored.Note, "note");
            Assert.AreEqual(ovr.ToHashToken(), restored.ToHashToken(), "the hash token round trips");
        }

        [Test("an override hash changes when any field changes")]
        public static void OverrideHashIsSensitive()
        {
            var a = new TextureOverride();
            var b = new TextureOverride();
            Assert.AreEqual(a.ToHashToken(), b.ToHashToken(), "identical overrides hash identically");

            b.ForcedMaxSize = 1024;
            Assert.AreNotEqual(a.ToHashToken(), b.ToHashToken(), "changing a value changes the hash");
        }

        [Test("forced max size above the source is clamped down")]
        public static void ForcedMaxSizeAboveSourceIsClamped()
        {
            var request = NewRequest(512, 512, TunerTextureFormat.RGBA32);
            var ovr = new TextureOverride();
            ovr.ForceMaxSize = true;
            ovr.ForcedMaxSize = 4096;

            var plan = CompressionPlanner.Plan(request, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), ovr, null);

            Assert.IsTrue(plan.TargetMaxSize <= 512, "the plan must not claim an upscaling max size");
            Assert.IsTrue(HasWarning(plan, "plan.note.forcedMaxSizeAboveSource"), "the clamp is explained");
        }
    }

    /// <summary>Tests for the string-evidence texture classifier.</summary>
    public static class TextureUsageClassifierTests
    {
        [Test("importer normal map type always wins")]
        public static void ImporterNormalMapWins()
        {
            var evidence = new ClassificationEvidence();
            var kind = TextureUsageClassifier.Classify("_MainTex", "Standard", "body.png", true, true, false, evidence);

            Assert.AreEqual(TextureUsageKind.NormalMap, kind, "importer type is decisive");
            Assert.IsTrue(evidence.ShaderIndicatesNormal, "the evidence records the normal flag");
        }

        [Test("normal map naming is recognised")]
        public static void NormalMapNamingIsRecognised()
        {
            var kind = TextureUsageClassifier.Classify("_BumpMap", "Standard", "body_normal.png", false, false, false, null);
            Assert.AreEqual(TextureUsageKind.NormalMap, kind, "_BumpMap is a normal map");
        }

        [Test("mask and data properties are classified as data")]
        public static void MaskPropertiesAreData()
        {
            Assert.AreEqual(TextureUsageKind.GreyscaleData,
                TextureUsageClassifier.Classify("_MetallicGlossMap", "Standard", "m.png", false, true, false, null),
                "metallic/smoothness is greyscale data");

            Assert.AreEqual(TextureUsageKind.DataMask,
                TextureUsageClassifier.Classify("_MaskTex", "lilToon", "mask.png", false, true, false, null),
                "a generic mask is a data map");
        }

        [Test("albedo with alpha evidence is separated from opaque albedo")]
        public static void AlphaEvidenceSeparatesAlbedo()
        {
            var withAlpha = TextureUsageClassifier.Classify(
                "_MainTex", "Standard/Transparent", "body.png", false, true, true, null);
            Assert.AreEqual(TextureUsageKind.AlbedoWithAlpha, withAlpha, "a transparent shader implies used alpha");

            var opaque = TextureUsageClassifier.Classify(
                "_MainTex", "Standard", "body.png", false, true, false, null);
            Assert.AreEqual(TextureUsageKind.AlbedoOpaque, opaque, "no alpha evidence means opaque albedo");
        }

        [Test("alphaIsTransparency together with a source alpha channel implies used alpha")]
        public static void AlphaIsTransparencyImpliesUsedAlpha()
        {
            var kind = TextureUsageClassifier.Classify(
                "_MainTex", "Standard", "body.png", false, true, true, null);
            Assert.AreEqual(TextureUsageKind.AlbedoWithAlpha, kind, "alphaIsTransparency is alpha usage evidence");
        }

        [Test("detail, face, eye and text names are recognised")]
        public static void DetailNamesAreRecognised()
        {
            var detail = new ClassificationEvidence();
            TextureUsageClassifier.Classify("_DetailAlbedoMap", "Standard", "d.png", false, false, false, detail);
            Assert.IsTrue(detail.NameIndicatesDetail, "detail naming is detected");
            Assert.AreEqual(TextureUsageKind.Detail,
                TextureUsageClassifier.Classify("_DetailAlbedoMap", "Standard", "d.png", false, false, false, null),
                "detail textures are their own kind");

            var face = new ClassificationEvidence();
            TextureUsageClassifier.Classify("_MainTex", "lilToon", "face_skin.png", false, true, false, face);
            Assert.IsTrue(face.NameIndicatesFace, "face naming is detected");

            var eye = new ClassificationEvidence();
            TextureUsageClassifier.Classify("_MainTex", "lilToon", "eye_iris.png", false, true, false, eye);
            Assert.IsTrue(eye.NameIndicatesEye, "eye naming is detected");

            var text = new ClassificationEvidence();
            TextureUsageClassifier.Classify("_MainTex", "Unlit/Text", "name_label.png", false, true, false, text);
            Assert.IsTrue(text.NameIndicatesSmallText, "small text naming is detected");
        }

        [Test("unclassifiable textures fall back to Unknown rather than guessing")]
        public static void UnknownFallsBackSafely()
        {
            var kind = TextureUsageClassifier.Classify(
                "_SomethingCustom", "Custom/Shader", "zz.png", false, false, false, null);
            Assert.AreEqual(TextureUsageKind.Unknown, kind, "no evidence means unknown");
        }

        [Test("detail preserving covers faces, eyes, text and UI")]
        public static void DetailPreservingCoverage()
        {
            Assert.IsTrue(TextureUsageClassifier.IsDetailPreserving(TextureUsageKind.UserInterface, null), "UI is detail preserving");

            var face = new ClassificationEvidence();
            face.NameIndicatesFace = true;
            Assert.IsTrue(TextureUsageClassifier.IsDetailPreserving(TextureUsageKind.AlbedoOpaque, face), "faces are detail preserving");

            var eye = new ClassificationEvidence();
            eye.NameIndicatesEye = true;
            Assert.IsTrue(TextureUsageClassifier.IsDetailPreserving(TextureUsageKind.AlbedoOpaque, eye), "eyes are detail preserving");

            var text = new ClassificationEvidence();
            text.NameIndicatesSmallText = true;
            Assert.IsTrue(TextureUsageClassifier.IsDetailPreserving(TextureUsageKind.AlbedoOpaque, text), "small text is detail preserving");

            Assert.IsFalse(TextureUsageClassifier.IsDetailPreserving(TextureUsageKind.AlbedoOpaque, null), "plain albedo is not");
        }

        [Test("compression friendliness matches the spec's safe set")]
        public static void CompressionFriendlySet()
        {
            Assert.IsTrue(TextureUsageClassifier.IsCompressionFriendly(TextureUsageKind.NormalMap), "normal maps");
            Assert.IsTrue(TextureUsageClassifier.IsCompressionFriendly(TextureUsageKind.DataMask), "data masks");
            Assert.IsTrue(TextureUsageClassifier.IsCompressionFriendly(TextureUsageKind.AlbedoOpaque), "opaque albedo");
            Assert.IsFalse(TextureUsageClassifier.IsCompressionFriendly(TextureUsageKind.UserInterface), "UI is not");
            Assert.IsFalse(TextureUsageClassifier.IsCompressionFriendly(TextureUsageKind.Unknown), "unknown is not");
        }
    }
}
