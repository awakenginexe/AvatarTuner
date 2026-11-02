using System;
using System.Collections.Generic;
using System.Globalization;

namespace AvatarTuner.Core
{
    /// <summary>
    /// Facts about the source texture as the importer reports them.
    /// </summary>
    public sealed class TexturePlanRequest
    {
        public int SourceWidth;
        public int SourceHeight;

        /// <summary>The format the importer will actually produce for the current target.</summary>
        public TunerTextureFormat CurrentFormat = TunerTextureFormat.Automatic;

        /// <summary>False when <see cref="CurrentFormat" /> is Automatic/Unknown and therefore unusable for arithmetic.</summary>
        public bool CurrentFormatKnown;

        /// <summary>The importer's current Max Size setting, or 0 when not overridden.</summary>
        public int CurrentMaxSize;

        public bool CurrentMipMaps = true;
        public bool CurrentSRGB = true;

        public TextureUsageKind Usage = TextureUsageKind.Unknown;
        public ClassificationEvidence Evidence;

        /// <summary>TextureImporter.DoesSourceTextureHaveAlpha().</summary>
        public bool SourceHasAlpha;

        /// <summary>True when the material/shader actually reads the alpha channel.</summary>
        public bool AlphaChannelUsed;

        /// <summary>Number of faces: 6 for a cubemap, 1 for a normal texture.</summary>
        public int FaceCount = 1;

        /// <summary>True when the texture is a normal map according to the importer.</summary>
        public bool ImporterTypeIsNormalMap;

        public bool IsValid
        {
            get { return SourceWidth > 0 && SourceHeight > 0 && FaceCount > 0; }
        }
    }

    /// <summary>
    /// Optional pixel-derived evidence about a texture's content.
    ///
    /// Every field here must be produced by really looking at the source pixels (see
    /// <see cref="QualityMetrics" />) or by reading back a generated asset. When a field is
    /// unavailable its <c>Has*</c> flag is false and the corresponding number is meaningless;
    /// the planner never substitutes a guess for a missing measurement.
    /// </summary>
    public sealed class QualityEvidence
    {
        /// <summary>True when <see cref="EstimatedPsnrDb" /> was computed from source pixels.</summary>
        public bool HasEstimatedPsnr;

        /// <summary>
        /// PSNR in dB of a *simulated* format conversion against the source, computed in the
        /// source domain. This is an estimate of what block compression will cost, not a
        /// measurement of the compressed output.
        /// </summary>
        public double EstimatedPsnrDb;

        /// <summary>True when <see cref="MeasuredPsnrDb" /> was read back from a real compressed asset.</summary>
        public bool HasMeasuredPsnr;

        /// <summary>PSNR in dB measured against a really generated and imported asset.</summary>
        public double MeasuredPsnrDb;

        /// <summary>True when <see cref="DetailScore" /> was computed from source pixels.</summary>
        public bool HasDetailScore;

        /// <summary>High-frequency energy of the source, normalised to 0..1. Higher means more fine detail.</summary>
        public double DetailScore;

        /// <summary>Gradient energy of the source, normalised to 0..1.</summary>
        public double EdgeEnergy;

        /// <summary>Fraction of pixels whose alpha exceeds the cut-out threshold, 0..1.</summary>
        public double AlphaCoverage;

        /// <summary>Standard deviation of source luminance, normalised to 0..1.</summary>
        public double LuminanceSpread;

        public bool HasAnyEvidence
        {
            get { return HasEstimatedPsnr || HasMeasuredPsnr || HasDetailScore; }
        }
    }

    /// <summary>
    /// The proposed transformation for one texture, together with the reasoning behind it.
    /// </summary>
    public sealed class TexturePlan
    {
        /// <summary>True when nothing should be done to this texture.</summary>
        public bool IsNoOp;

        /// <summary>True when the user excluded the texture. Implies <see cref="IsNoOp" />.</summary>
        public bool IsExcluded;

        /// <summary>Localization key explaining why the texture was skipped.</summary>
        public string SkipReasonKey;

        public TunerTextureFormat SourceFormat = TunerTextureFormat.Automatic;
        public TunerTextureFormat TargetFormat = TunerTextureFormat.Automatic;

        public int SourceMaxSize;
        public int TargetMaxSize;

        public int SourceWidth;
        public int SourceHeight;
        public int TargetWidth;
        public int TargetHeight;

        public bool SourceMipMaps;
        public bool TargetMipMaps;

        public bool FormatChanged;
        public bool ResolutionChanged;
        public bool MipMapChanged;

        public long SourceBytes;
        public long TargetBytes;
        public long SavingBytes;
        public double SavingPercent;

        /// <summary>True when <see cref="SavingBytes" /> could not be computed (source format unknown).</summary>
        public bool SavingUnknown;

        public MeasurementBasis MeasurementBasis = MeasurementBasis.ComputedFromPlan;
        public QualityConfidence Confidence = QualityConfidence.Unknown;

        public bool HasEstimatedPsnr;
        public double EstimatedPsnrDb;
        public bool HasMeasuredPsnr;
        public double MeasuredPsnrDb;

        public TextureUsageKind Usage = TextureUsageKind.Unknown;
        public readonly List<PlanNote> Notes = new List<PlanNote>();

        public bool IsDetailPreserving;

        public void AddNote(string key, params string[] args)
        {
            Notes.Add(new PlanNote(key, args));
        }

        public void AddWarning(string key, params string[] args)
        {
            Notes.Add(new PlanNote(key, args).WithSeverity(PlanNote.Severity.Warning));
        }

        public void AddError(string key, params string[] args)
        {
            Notes.Add(new PlanNote(key, args).WithSeverity(PlanNote.Severity.Error));
        }

        /// <summary>Hash token covering the whole proposed output, for change tracking.</summary>
        public string ToHashToken()
        {
            return StableHash.CombineOrdered(new[]
            {
                TargetFormat.ToString(),
                TargetMaxSize.ToString(CultureInfo.InvariantCulture),
                TargetMipMaps ? "mip1" : "mip0",
                TargetWidth.ToString(CultureInfo.InvariantCulture),
                TargetHeight.ToString(CultureInfo.InvariantCulture)
            });
        }
    }

    /// <summary>
    /// Decides what should happen to a texture.
    ///
    /// Design rules, in priority order:
    ///  1. Never modify anything the user excluded or explicitly pinned.
    ///  2. Never downscale textures carrying recognisable detail (faces, eyes, small text,
    ///     UI) unless the user has explicitly allowed it.
    ///  3. Never increase memory use. A "better" format that costs more is not proposed.
    ///  4. Only report a quality number when one was actually derived; otherwise lower the
    ///     confidence and say so.
    ///
    /// The function is pure: same inputs always produce the same plan.
    /// </summary>
    public static class CompressionPlanner
    {
        public static TexturePlan Plan(
            TexturePlanRequest request,
            TexturePolicy policy,
            TextureOverride ovr,
            QualityEvidence quality)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            if (policy == null)
            {
                throw new ArgumentNullException("policy");
            }

            quality = quality ?? new QualityEvidence();
            var evidence = request.Evidence ?? new ClassificationEvidence();
            var plan = new TexturePlan();
            plan.Usage = request.Usage;
            plan.SourceFormat = request.CurrentFormat;
            plan.SourceWidth = request.SourceWidth;
            plan.SourceHeight = request.SourceHeight;
            plan.SourceMipMaps = request.CurrentMipMaps;
            plan.IsDetailPreserving = TextureUsageClassifier.IsDetailPreserving(request.Usage, evidence);

            // ---- 1. user exclusions ---------------------------------------------
            if (ovr != null && ovr.Excluded)
            {
                plan.IsNoOp = true;
                plan.IsExcluded = true;
                plan.SkipReasonKey = "plan.skip.excluded";
                plan.AddNote("plan.note.excludedByUser");
                CopySourceIntoTarget(plan, request);
                ComputeSavings(plan, request);
                plan.Confidence = QualityConfidence.Unknown;
                return plan;
            }

            if (!request.IsValid)
            {
                plan.IsNoOp = true;
                plan.SkipReasonKey = "plan.skip.invalidRequest";
                plan.AddWarning("plan.note.invalidDimensions");
                return plan;
            }

            if (request.CurrentFormat == TunerTextureFormat.Automatic ||
                request.CurrentFormat == TunerTextureFormat.Unknown)
            {
                plan.AddWarning("plan.note.currentFormatUnknown");
            }

            // ---- 2. format -------------------------------------------------------
            var targetFormat = ResolveTargetFormat(request, policy, ovr, plan);
            plan.TargetFormat = targetFormat;

            // ---- 3. resolution ---------------------------------------------------
            var targetMaxSize = ResolveTargetMaxSize(request, policy, ovr, quality, plan);
            plan.TargetMaxSize = targetMaxSize;

            // ---- 4. mip maps -----------------------------------------------------
            var targetMips = policy.KeepMipMaps;
            if (ovr != null && ovr.ForceMipMaps)
            {
                targetMips = ovr.ForcedMipMaps;
                plan.AddNote("plan.note.mipMapForced", targetMips ? "on" : "off");
            }
            else if (request.CurrentMipMaps && !policy.KeepMipMaps)
            {
                plan.AddWarning("plan.note.mipMapsRemoved");
            }

            plan.TargetMipMaps = targetMips;

            // ---- 5. derived sizes ------------------------------------------------
            int sourceWidth, sourceHeight;
            VramCalculator.ApplyMaxSize(
                request.SourceWidth,
                request.SourceHeight,
                EffectiveCurrentMaxSize(request),
                out sourceWidth,
                out sourceHeight);

            int targetWidth, targetHeight;
            VramCalculator.ApplyMaxSize(request.SourceWidth, request.SourceHeight, targetMaxSize, out targetWidth, out targetHeight);

            plan.TargetWidth = targetWidth;
            plan.TargetHeight = targetHeight;
            plan.SourceWidth = sourceWidth;
            plan.SourceHeight = sourceHeight;

            plan.FormatChanged = targetFormat != request.CurrentFormat;
            plan.ResolutionChanged = targetWidth != sourceWidth || targetHeight != sourceHeight;
            plan.MipMapChanged = targetMips != request.CurrentMipMaps;

            if (!plan.FormatChanged && !plan.ResolutionChanged && !plan.MipMapChanged)
            {
                plan.IsNoOp = true;
                plan.SkipReasonKey = "plan.skip.alreadyOptimal";
                plan.AddNote("plan.note.alreadyOptimal");
            }

            ComputeSavings(plan, request);

            // A plan that would make the texture larger is always discarded.
            if (!plan.SavingUnknown && plan.SavingBytes < 0)
            {
                plan.IsNoOp = true;
                plan.SkipReasonKey = "plan.skip.wouldIncreaseSize";
                plan.AddNote("plan.note.wouldIncreaseSize");
                plan.TargetFormat = request.CurrentFormat;
                plan.TargetMaxSize = EffectiveCurrentMaxSize(request);
                plan.TargetMipMaps = request.CurrentMipMaps;
                plan.TargetWidth = sourceWidth;
                plan.TargetHeight = sourceHeight;
                plan.FormatChanged = false;
                plan.ResolutionChanged = false;
                plan.MipMapChanged = false;
                ComputeSavings(plan, request);
            }

            // ---- 6. quality ------------------------------------------------------
            ApplyQualityAssessment(plan, policy, quality, request, targetFormat);

            return plan;
        }

        // ------------------------------------------------------------------ format

        private static TunerTextureFormat ResolveTargetFormat(
            TexturePlanRequest request,
            TexturePolicy policy,
            TextureOverride ovr,
            TexturePlan plan)
        {
            var current = request.CurrentFormat;

            if (ovr != null && ovr.ForceFormat)
            {
                if (!TextureFormatInfo.IsSupportedOnPc(ovr.ForcedFormat))
                {
                    plan.AddError("plan.error.forcedFormatUnsupported",
                        TextureFormatInfo.DisplayName(ovr.ForcedFormat));
                    // fall through to the planner's own choice rather than honouring a bad override
                }
                else
                {
                    plan.AddNote("plan.note.formatForced", TextureFormatInfo.DisplayName(ovr.ForcedFormat));
                    return ovr.ForcedFormat;
                }
            }

            if (ovr != null && ovr.KeepFormat)
            {
                plan.AddNote("plan.note.formatPinnedByUser");
                return current;
            }

            if (!policy.AllowFormatChange)
            {
                plan.AddNote("plan.note.formatChangeDisabledByPreset");
                return current;
            }

            var needsSmoothAlpha = request.AlphaChannelUsed && request.SourceHasAlpha;
            var proposed = ProposeFormat(request.Usage, needsSmoothAlpha, policy);

            if (proposed == current)
            {
                plan.AddNote("plan.note.formatAlreadyAppropriate", TextureFormatInfo.DisplayName(current));
                return current;
            }

            // Never propose a format that costs more memory than what is already in use.
            if (request.CurrentFormatKnown)
            {
                var currentBpp = TextureFormatInfo.BytesPerPixel(current);
                var proposedBpp = TextureFormatInfo.BytesPerPixel(proposed);
                if (proposedBpp > currentBpp && currentBpp > 0)
                {
                    plan.AddNote("plan.note.formatKeptCheaperCurrent", TextureFormatInfo.DisplayName(current));
                    return current;
                }
            }

            plan.AddNote("plan.note.formatProposed",
                TextureFormatInfo.DisplayName(current),
                TextureFormatInfo.DisplayName(proposed));
            return proposed;
        }

        /// <summary>
        /// Chooses an output format for a usage. The mapping is intentionally short and
        /// explicit so it can be reviewed line by line:
        ///   normal map              -> BC5   (two channels, the standard normal format)
        ///   data / mask / greyscale -> BC4   (one channel) or BC5 when the source has alpha
        ///   emission                -> BC7   (HDR-ish colour, keeps gradients)
        ///   albedo with alpha       -> BC7   (quality) or BC3 when aggressive formats allowed
        ///   albedo opaque           -> BC7   (quality) or BC1 when aggressive formats allowed
        ///   detail / UI / unknown   -> BC7   only when the source is currently uncompressed;
        ///                                   otherwise the current format is left alone.
        /// </summary>
        private static TunerTextureFormat ProposeFormat(TextureUsageKind usage, bool needsSmoothAlpha, TexturePolicy policy)
        {
            switch (usage)
            {
                case TextureUsageKind.NormalMap:
                    return TunerTextureFormat.BC5;

                case TextureUsageKind.GreyscaleData:
                    return TunerTextureFormat.BC4;

                case TextureUsageKind.DataMask:
                    return needsSmoothAlpha ? TunerTextureFormat.BC5 : TunerTextureFormat.BC4;

                case TextureUsageKind.Emission:
                    return TunerTextureFormat.BC7;

                case TextureUsageKind.AlbedoWithAlpha:
                    return policy.AllowAggressiveFormats ? TunerTextureFormat.BC3 : TunerTextureFormat.BC7;

                case TextureUsageKind.AlbedoOpaque:
                    return policy.AllowAggressiveFormats ? TunerTextureFormat.BC1 : TunerTextureFormat.BC7;

                default:
                    return TunerTextureFormat.BC7;
            }
        }

        // ------------------------------------------------------------------- size

        private static int ResolveTargetMaxSize(
            TexturePlanRequest request,
            TexturePolicy policy,
            TextureOverride ovr,
            QualityEvidence quality,
            TexturePlan plan)
        {
            var current = EffectiveCurrentMaxSize(request);

            if (ovr != null && ovr.ForceMaxSize)
            {
                var forced = ClampMaxSize(ovr.ForcedMaxSize);
                if (forced > current)
                {
                    plan.AddWarning("plan.note.forcedMaxSizeAboveSource",
                        forced.ToString(CultureInfo.InvariantCulture));
                    return current;
                }

                plan.AddNote("plan.note.maxSizeForced", forced.ToString(CultureInfo.InvariantCulture));
                return forced;
            }

            if (ovr != null && ovr.KeepResolution)
            {
                plan.AddNote("plan.note.resolutionPinnedByUser");
                return current;
            }

            if (!policy.AllowDownscale)
            {
                plan.AddNote("plan.note.downscaleDisabledByPreset");
                return current;
            }

            if (plan.IsDetailPreserving && !policy.AllowDownscaleOfDetailTextures)
            {
                plan.AddNote("plan.note.resolutionKeptDetailPreserving");
                return current;
            }

            // Quality gate for resolution reduction.
            if (quality.HasDetailScore)
            {
                if (quality.DetailScore > policy.MaxDetailScoreForDownscale)
                {
                    plan.AddNote("plan.note.resolutionKeptHighDetail",
                        quality.DetailScore.ToString("0.00", CultureInfo.InvariantCulture),
                        policy.MaxDetailScoreForDownscale.ToString("0.00", CultureInfo.InvariantCulture));
                    return current;
                }
            }
            else if (!policy.AllowChangesWithoutQualityEvidence)
            {
                plan.AddNote("plan.note.resolutionKeptNoEvidence");
                return current;
            }

            var candidate = current;
            var accepted = current;
            for (var step = 0; step < policy.MaxDownscaleSteps; step++)
            {
                int next;
                if (!VramCalculator.TryStepDownMaxSize(candidate, out next))
                {
                    break;
                }

                if (next < policy.MinAllowedMaxSize)
                {
                    plan.AddNote("plan.note.downscaleFloorReached",
                        policy.MinAllowedMaxSize.ToString(CultureInfo.InvariantCulture));
                    break;
                }

                accepted = next;
                candidate = next;
            }

            if (accepted < current)
            {
                plan.AddNote("plan.note.resolutionReduced",
                    current.ToString(CultureInfo.InvariantCulture),
                    accepted.ToString(CultureInfo.InvariantCulture));
            }

            return accepted;
        }

        private static int EffectiveCurrentMaxSize(TexturePlanRequest request)
        {
            var lossless = VramCalculator.NearestLosslessMaxSize(request.SourceWidth, request.SourceHeight);
            if (request.CurrentMaxSize <= 0)
            {
                return lossless;
            }

            return Math.Min(request.CurrentMaxSize, lossless);
        }

        private static int ClampMaxSize(int value)
        {
            foreach (var rung in VramCalculator.MaxSizeLadder)
            {
                if (value <= rung)
                {
                    return rung;
                }
            }

            return VramCalculator.MaxSizeLadder[VramCalculator.MaxSizeLadder.Length - 1];
        }

        // ----------------------------------------------------------------- savings

        private static void CopySourceIntoTarget(TexturePlan plan, TexturePlanRequest request)
        {
            plan.TargetFormat = request.CurrentFormat;
            plan.TargetMaxSize = EffectiveCurrentMaxSize(request);
            plan.TargetMipMaps = request.CurrentMipMaps;
            int w, h;
            VramCalculator.ApplyMaxSize(request.SourceWidth, request.SourceHeight, plan.TargetMaxSize, out w, out h);
            plan.TargetWidth = w;
            plan.TargetHeight = h;
            plan.SourceWidth = w;
            plan.SourceHeight = h;
        }

        private static void ComputeSavings(TexturePlan plan, TexturePlanRequest request)
        {
            if (!request.CurrentFormatKnown ||
                request.CurrentFormat == TunerTextureFormat.Automatic ||
                request.CurrentFormat == TunerTextureFormat.Unknown ||
                TextureFormatInfo.BytesPerPixel(request.CurrentFormat) <= 0.0)
            {
                plan.SavingUnknown = true;
                plan.SourceBytes = 0;
                plan.TargetBytes = 0;
                plan.SavingBytes = 0;
                plan.SavingPercent = 0;
                plan.MeasurementBasis = MeasurementBasis.ComputedFromPlan;
                return;
            }

            plan.SourceBytes = VramCalculator.EstimateBytes(
                plan.SourceWidth, plan.SourceHeight, plan.SourceFormat, plan.SourceMipMaps, request.FaceCount);

            if (TextureFormatInfo.BytesPerPixel(plan.TargetFormat) <= 0.0)
            {
                plan.SavingUnknown = true;
                return;
            }

            plan.TargetBytes = VramCalculator.EstimateBytes(
                plan.TargetWidth, plan.TargetHeight, plan.TargetFormat, plan.TargetMipMaps, request.FaceCount);

            plan.SavingBytes = plan.SourceBytes - plan.TargetBytes;
            plan.SavingPercent = VramCalculator.SavingPercent(plan.SourceBytes, plan.TargetBytes);
            plan.MeasurementBasis = MeasurementBasis.ComputedFromPlan;
        }

        // ----------------------------------------------------------------- quality

        private static void ApplyQualityAssessment(
            TexturePlan plan,
            TexturePolicy policy,
            QualityEvidence quality,
            TexturePlanRequest request,
            TunerTextureFormat targetFormat)
        {
            // A measured figure always wins, then the source-domain estimate, then nothing.
            if (quality.HasMeasuredPsnr)
            {
                plan.HasMeasuredPsnr = true;
                plan.MeasuredPsnrDb = quality.MeasuredPsnrDb;
                plan.Confidence = QualityConfidence.High;
                plan.AddNote("plan.note.qualityMeasured",
                    quality.MeasuredPsnrDb.ToString("0.0", CultureInfo.InvariantCulture));

                if (quality.MeasuredPsnrDb < policy.MinEstimatedQualityDb)
                {
                    plan.AddWarning("plan.warning.measuredQualityBelowThreshold",
                        quality.MeasuredPsnrDb.ToString("0.0", CultureInfo.InvariantCulture),
                        policy.MinEstimatedQualityDb.ToString("0.0", CultureInfo.InvariantCulture));
                }
            }
            else if (quality.HasEstimatedPsnr)
            {
                plan.HasEstimatedPsnr = true;
                plan.EstimatedPsnrDb = quality.EstimatedPsnrDb;
                plan.Confidence = QualityConfidence.Medium;
                plan.AddNote("plan.note.qualityEstimated",
                    quality.EstimatedPsnrDb.ToString("0.0", CultureInfo.InvariantCulture));

                if (plan.FormatChanged && quality.EstimatedPsnrDb < policy.MinEstimatedQualityDb)
                {
                    plan.AddWarning("plan.warning.estimatedQualityBelowThreshold",
                        quality.EstimatedPsnrDb.ToString("0.0", CultureInfo.InvariantCulture),
                        policy.MinEstimatedQualityDb.ToString("0.0", CultureInfo.InvariantCulture));
                    plan.Confidence = QualityConfidence.Low;
                }
            }
            else
            {
                plan.Confidence = quality.HasDetailScore ? QualityConfidence.Low : QualityConfidence.Unknown;
                plan.AddWarning("plan.warning.noQualityEvidence");
            }

            if (plan.ResolutionChanged)
            {
                // Resolution reduction is never given a PSNR figure: the planner has no
                // honest way to quantify the loss without rendering the downsampled result.
                plan.AddWarning("plan.warning.resolutionChangeNotQuantified",
                    plan.SourceWidth.ToString(CultureInfo.InvariantCulture),
                    plan.SourceHeight.ToString(CultureInfo.InvariantCulture),
                    plan.TargetWidth.ToString(CultureInfo.InvariantCulture),
                    plan.TargetHeight.ToString(CultureInfo.InvariantCulture));
            }

            if (!TextureFormatInfo.SupportsAlpha(targetFormat) && request.AlphaChannelUsed && request.SourceHasAlpha)
            {
                plan.AddWarning("plan.warning.alphaLost", TextureFormatInfo.DisplayName(targetFormat));
            }
            else if (targetFormat == TunerTextureFormat.BC1 && request.AlphaChannelUsed && request.SourceHasAlpha)
            {
                plan.AddWarning("plan.warning.alphaOneBit", TextureFormatInfo.DisplayName(targetFormat));
            }

            if (plan.IsDetailPreserving && plan.ResolutionChanged)
            {
                plan.AddWarning("plan.warning.detailTextureResized");
            }
        }
    }
}
