using System;
using System.Collections.Generic;
using System.Globalization;
namespace AvatarTuner.Core
{
    public sealed class TextureCandidate
    {
        public int Width, Height, MaxSize, CompressionQuality;
        public TunerTextureFormat Format;
        public bool MipMaps;
        public ResizeFilter Filter;
        public long Bytes;
        public string ToHashToken()
        {
            return StableHash.CombineOrdered(new[]
            {
                Width.ToString(), Height.ToString(), MaxSize.ToString(), Format.ToString(),
                MipMaps.ToString(), Filter.ToString(), CompressionQuality.ToString()
            });
        }
    }
    public sealed class CandidateQuality
    {
        public const string ComparisonVersion = "visual-detail-v2";
        public double Psnr, Ssim, AlphaCoverageError, EdgeError, MeanColorError, MaxChannelError, NormalAngularError;
        public double MinLocalSsim = 1;
        public bool Available;
        public CandidateQuality Visual512, Visual256;
        public static CandidateQuality Compare(byte[] reference, byte[] candidate, int width, int height, bool alphaUsed, bool normalMap = false)
        {
            var quality = ComparePixels(reference, candidate, width, height, alphaUsed, normalMap);
            if (!normalMap)
            {
                quality.Visual512 = CompareView(reference, candidate, width, height, alphaUsed, 512);
                quality.Visual256 = CompareView(reference, candidate, width, height, alphaUsed, 256);
            }
            return quality;
        }

        private static CandidateQuality CompareView(byte[] reference, byte[] candidate, int width, int height, bool alphaUsed, int longEdge)
        {
            var maximum = Math.Max(width, height);
            if (maximum <= longEdge) return null;
            var w = Math.Max(1, (int)((long)width * longEdge / maximum));
            var h = Math.Max(1, (int)((long)height * longEdge / maximum));
            // Filter both images at the same viewing scale; never compare a candidate to itself.
            return ComparePixels(QualityMetrics.DownsampleBox(reference, width, height, 4, w, h),
                QualityMetrics.DownsampleBox(candidate, width, height, 4, w, h), w, h, alphaUsed, false);
        }

        private static CandidateQuality ComparePixels(byte[] reference, byte[] candidate, int width, int height, bool alphaUsed, bool normalMap)
        {
            var q = new CandidateQuality();
            q.Psnr = QualityMetrics.Psnr(reference, candidate, width, height, 4);
            q.Ssim = QualityMetrics.Ssim(reference, candidate, width, height, 4, out q.MinLocalSsim, alphaUsed);
            q.AlphaCoverageError = alphaUsed ? Math.Abs(QualityMetrics.AlphaCoverage(reference, width, height, 4, 0.5) - QualityMetrics.AlphaCoverage(candidate, width, height, 4, 0.5)) : 0;
            q.EdgeError = Math.Abs(QualityMetrics.EdgeEnergy(reference, width, height, 4) - QualityMetrics.EdgeEnergy(candidate, width, height, 4));
            double sum = 0, angle = 0;
            int pixels = width * height;
            for (int i = 0; i < pixels; i++)
            {
                for (int c = 0; c < (alphaUsed ? 4 : 3); c++)
                {
                    double d = Math.Abs(reference[i * 4 + c] - candidate[i * 4 + c]) / 255.0;
                    sum += d;
                    q.MaxChannelError = Math.Max(q.MaxChannelError, d);
                }
                if (normalMap)
                {
                    double dot = 0, a = 0, b = 0;
                    for (int c = 0; c < 3; c++)
                    {
                        double x = reference[i * 4 + c] / 127.5 - 1, y = candidate[i * 4 + c] / 127.5 - 1;
                        dot += x * y;
                        a += x * x;
                        b += y * y;
                    }
                    if (a > 0 && b > 0) angle += Math.Acos(Math.Max(-1, Math.Min(1, dot / Math.Sqrt(a * b)))) * 180 / Math.PI;
                }
            }
            q.MeanColorError = sum / (pixels * (alphaUsed ? 4 : 3));
            q.NormalAngularError = angle / pixels;
            q.Available = true;
            return q;
        }
    }
    public static class AdaptivePlanner
    {
        public static List<TextureCandidate> Candidates(TexturePlanRequest r, TexturePolicy policy, TextureOverride o, TextureSettings settings, SemanticCategory category,
            StandardOptimizationLevel? standardLevel = null)
        {
            var result = new List<TextureCandidate>();
            if (r == null || policy == null || settings == null || !ValidSettings(settings) || !r.IsValid || !r.CurrentFormatKnown || settings.Excluded || settings.ProtectQuality || (o != null && o.Excluded) || category == SemanticCategory.Mixed || r.Usage == TextureUsageKind.Unknown || r.Usage == TextureUsageKind.UserInterface || r.FaceCount != 1 ||
                (standardLevel.HasValue && !Enum.IsDefined(typeof(StandardOptimizationLevel), standardLevel.Value))) return result;
            if (TextureFormatInfo.BytesPerPixel(r.CurrentFormat) <= 0) return result;
            var formats = new List<TunerTextureFormat>();
            if (settings.Platform == OptimizationPlatform.Windows)
            {
                if (r.Usage == TextureUsageKind.NormalMap || r.ImporterTypeIsNormalMap)
                {
                    if (!r.AlphaChannelUsed) formats.Add(TunerTextureFormat.BC5);
                }
                else if (r.Usage == TextureUsageKind.DataMask)
                {
                    int channels = TextureFormatInfo.ChannelCount(r.CurrentFormat);
                    if (channels == 1) formats.Add(TunerTextureFormat.BC4);
                    else if (channels == 2) formats.Add(TunerTextureFormat.BC5);
                    else formats.Add(TunerTextureFormat.BC7);
                }
                else if (r.Usage == TextureUsageKind.GreyscaleData && TextureFormatInfo.ChannelCount(r.CurrentFormat) == 1)
                    formats.Add(TunerTextureFormat.BC4);
                else if ((r.Usage == TextureUsageKind.AlbedoOpaque || r.Usage == TextureUsageKind.Emission) && !r.SourceHasAlpha && !r.AlphaChannelUsed)
                {
                    formats.Add(TunerTextureFormat.BC1);
                    formats.Add(TunerTextureFormat.BC7);
                }
                else if (r.Usage == TextureUsageKind.AlbedoWithAlpha || r.Usage == TextureUsageKind.AlbedoOpaque || r.Usage == TextureUsageKind.Emission)
                {
                    formats.Add(TunerTextureFormat.BC3);
                    formats.Add(TunerTextureFormat.BC7);
                }
            }
            else
            {
                formats.Add(TunerTextureFormat.ASTC_4x4);
                formats.Add(TunerTextureFormat.ASTC_6x6);
                formats.Add(TunerTextureFormat.ASTC_8x8);
            }
            if (r.Usage == TextureUsageKind.Detail) return result;
            // HDR is preserved until decoded floating-point comparisons are available.
            if (r.CurrentFormat == TunerTextureFormat.BC6H || r.CurrentFormat == TunerTextureFormat.RGBAHalf || r.CurrentFormat == TunerTextureFormat.RGBAFloat || r.CurrentFormat == TunerTextureFormat.RHalf || r.CurrentFormat == TunerTextureFormat.RFloat || r.CurrentFormat == TunerTextureFormat.RGHalf || r.CurrentFormat == TunerTextureFormat.RGFloat) return result;
            if (o != null && o.KeepFormat)
            {
                formats.Clear();
                formats.Add(r.CurrentFormat);
            }
            else if (settings.Format != TunerTextureFormat.Automatic || (o != null && o.ForceFormat))
            {
                var f = o != null && o.ForceFormat ? o.ForcedFormat : settings.Format;
                if (!formats.Contains(f)) return result;
                formats.Clear();
                formats.Add(f);
            }
            if (settings.Format == TunerTextureFormat.Automatic && (o == null || !o.ForceFormat) &&
                (settings.Platform != OptimizationPlatform.Windows || TextureFormatInfo.IsSupportedOnPc(r.CurrentFormat)) &&
                !formats.Contains(r.CurrentFormat)) formats.Add(r.CurrentFormat);
            int max = Math.Max(r.SourceWidth, r.SourceHeight);
            int minimum = category == SemanticCategory.Face || category == SemanticCategory.Eyes ? 1024 :
                category == SemanticCategory.Hair ? 512 : 256;
            if (standardLevel == StandardOptimizationLevel.Maximum && !IsDataTexture(r, category)) minimum /= 2;
            int floor = Math.Min(max, minimum);
            if (settings.MaxDownscaleSteps >= 0)
                floor = Math.Max(floor, Math.Max(1, max >> settings.MaxDownscaleSteps));
            bool keep = settings.KeepResolution || (o != null && o.KeepResolution);
            int limit = settings.MaxResolution > 0 ? settings.MaxResolution : max;
            if (o != null && o.ForceMaxSize) limit = Math.Min(limit, o.ForcedMaxSize);
            for (int size = max; size >= floor; size = Math.Max(floor, size / 2))
            {
                if (size <= limit)
                {
                    int w = Math.Max(1, (int)Math.Round((double)r.SourceWidth * size / max)), h = Math.Max(1, (int)Math.Round((double)r.SourceHeight * size / max));
                    foreach (var f in formats)
                    {
                        if (r.CurrentSRGB && (f == TunerTextureFormat.BC4 || f == TunerTextureFormat.BC5)) continue;
                        if (settings.Platform == OptimizationPlatform.Android && f == r.CurrentFormat && f != TunerTextureFormat.ASTC_4x4 && f != TunerTextureFormat.ASTC_6x6 && f != TunerTextureFormat.ASTC_8x8) continue;
                        var filters = settings.Effort == OptimizationEffort.BestQuality
                            ? new[] { ResizeFilter.UnityMitchell, ResizeFilter.UnityBilinear, ResizeFilter.Area, ResizeFilter.Lanczos3, ResizeFilter.Mitchell }
                            : new[] { ResizeFilter.UnityMitchell };
                        foreach (var filter in filters)
                        {
                            bool mipMaps = o != null && o.ForceMipMaps ? o.ForcedMipMaps : r.CurrentMipMaps;
                            result.Add(new TextureCandidate
                            {
                                Width = w,
                                Height = h,
                                MaxSize = size,
                                Format = f,
                                MipMaps = mipMaps,
                                Filter = filter,
                                CompressionQuality = settings.Effort == OptimizationEffort.BestQuality ? 100 : 50,
                                Bytes = VramCalculator.EstimateBytes(w, h, f, mipMaps, r.FaceCount)
                            });
                        }
                    }
                }
                if (keep || size == floor) break;
            }
            return result;
        }
        public static TexturePlan Select(TexturePlanRequest r, TexturePolicy p, TextureOverride o, TextureSettings s, SemanticCategory category, Func<TextureCandidate, CandidateQuality> evaluate,
            bool useReductionTarget = false, StandardOptimizationLevel? standardLevel = null)
        {
            if (r == null) throw new ArgumentNullException("r");
            if (p == null) throw new ArgumentNullException("p");
            if (s == null) throw new ArgumentNullException("s");
            var plan = new TexturePlan
            {
                SourceWidth = r.SourceWidth,
                SourceHeight = r.SourceHeight,
                TargetWidth = r.SourceWidth,
                TargetHeight = r.SourceHeight,
                SourceFormat = r.CurrentFormat,
                TargetFormat = r.CurrentFormat,
                SourceMipMaps = r.CurrentMipMaps,
                TargetMipMaps = r.CurrentMipMaps,
                SourceMaxSize = r.CurrentMaxSize,
                TargetMaxSize = r.CurrentMaxSize,
                Usage = r.Usage,
                IsNoOp = true,
                IsExcluded = s.Excluded || (o != null && o.Excluded),
                RequestedReduction = s.DesiredReduction,
                ReductionAchieved = 1
            };
            if (!r.IsValid || !r.CurrentFormatKnown || TextureFormatInfo.BytesPerPixel(r.CurrentFormat) <= 0)
            {
                plan.SavingUnknown = true;
                return plan;
            }
            plan.SourceBytes = VramCalculator.EstimateBytes(r.SourceWidth, r.SourceHeight, r.CurrentFormat, r.CurrentMipMaps, r.FaceCount);
            plan.TargetBytes = plan.SourceBytes;
            if (evaluate == null) return plan;
            TextureCandidate best = null;
            CandidateQuality quality = null;
            CandidateQuality visualQuality = null;
            var detailRejected = false;
            double targetBytes = 0;
            double bestScore = double.MaxValue;
            if (useReductionTarget)
            {
                long reportedSource;
                var mipCount = r.CurrentMipCount > 0 ? r.CurrentMipCount : r.CurrentMipMaps ? VramCalculator.MipLevelCount(r.SourceWidth, r.SourceHeight) : 1;
                if (!VrcTextureMemory.TryEstimateBytes(r.SourceWidth, r.SourceHeight, r.CurrentFormat, mipCount, r.FaceCount, out reportedSource)) return plan;
                targetBytes = reportedSource / s.DesiredReduction;
                if (s.Format == TunerTextureFormat.Automatic && s.MaxResolution == 0 && (o == null || !o.HasAnyEffect))
                    bestScore = Math.Abs(reportedSource - targetBytes);
            }
            foreach (var c in Candidates(r, p, o, s, category, standardLevel))
            {
                if (c.Bytes >= plan.SourceBytes) continue;
                double score = c.Bytes;
                if (useReductionTarget)
                {
                    long reportedTarget;
                    if (!VrcTextureMemory.TryEstimateBytes(c.Width, c.Height, c.Format,
                        c.MipMaps ? VramCalculator.MipLevelCount(c.Width, c.Height) : 1, 1, out reportedTarget)) continue;
                    score = Math.Abs(reportedTarget - targetBytes);
                }
                if (score > bestScore || (best == null && score == bestScore)) continue;
                var q = evaluate(c);
                if (!ValidQuality(q)) continue;
                var data = IsDataTexture(r, category);
                var normalMap = r.Usage == TextureUsageKind.NormalMap || r.ImporterTypeIsNormalMap;
                var maximum = standardLevel == StandardOptimizationLevel.Maximum;
                var normal = standardLevel == StandardOptimizationLevel.Normal;
                // Masks shade what is seen, so Normal/Maximum judge them at the same viewing scale as color.
                var visual = !normalMap && normal ? q.Visual512 ?? q : !normalMap && maximum ? q.Visual256 ?? q : q;
                // Normal-map channels are not colors: the visible change is how far the lighting direction moves.
                if (normalMap && (normal || maximum))
                {
                    if (q.NormalAngularError > (maximum ? 3.5 : 2)) continue;
                }
                else
                {
                    // Edge and mean color loss are judged at the tier's viewing scale, like PSNR/SSIM.
                    if (!ValidQuality(visual) || visual.Psnr < s.MinPsnr || visual.Ssim < s.MinSsim ||
                        q.AlphaCoverageError > 0.02 || visual.EdgeError > 0.03 || visual.MeanColorError > 0.03 ||
                        visual.MaxChannelError > 0.25 || q.NormalAngularError > 5) continue;
                    // Local features are checked at the tier's viewing scale, not by the worst source texel.
                    var localMinimum = data || standardLevel == StandardOptimizationLevel.Minimum ? 0.9 :
                        0.8;
                    if (standardLevel.HasValue && visual.MinLocalSsim < localMinimum) { detailRejected = true; continue; }
                    if ((category == SemanticCategory.Masks || r.Usage == TextureUsageKind.DataMask || r.Usage == TextureUsageKind.GreyscaleData) &&
                        visual.MaxChannelError > (maximum ? 0.25 : normal ? 0.12 : 0.03)) continue;
                    var face = category == SemanticCategory.Face || category == SemanticCategory.Eyes;
                    double minPsnr = face ? (maximum ? 38 : normal ? 44 : 50) :
                        category == SemanticCategory.Hair ? (maximum ? 36 : normal ? 43 : 48) :
                        category == SemanticCategory.Masks && !standardLevel.HasValue ? 48 : s.MinPsnr;
                    double minSsim = face ? (maximum ? 0.97 : normal ? 0.985 : 0.99) :
                        category == SemanticCategory.Hair ? (maximum ? 0.96 : normal ? 0.98 : 0.985) :
                        category == SemanticCategory.Masks && !standardLevel.HasValue ? 0.985 : s.MinSsim;
                    if (standardLevel.HasValue && data) { minPsnr = Math.Max(minPsnr, maximum ? 40 : normal ? 44 : 48); minSsim = Math.Max(minSsim, maximum ? 0.96 : normal ? 0.975 : 0.985); }
                    if (visual.Psnr < Math.Max(s.MinPsnr, minPsnr) || visual.Ssim < Math.Max(s.MinSsim, minSsim)) continue;
                }
                if (best != null && score == bestScore &&
                    (q.Ssim < quality.Ssim || (q.Ssim == quality.Ssim && q.Psnr <= quality.Psnr))) continue;
                best = c;
                bestScore = score;
                quality = q;
                visualQuality = visual;
            }
            if (detailRejected) plan.AddNote("plan.note.localDetailProtected");
            if (best == null)
            {
                plan.SkipReasonKey = plan.IsExcluded ? "plan.skip.excluded" :
                    useReductionTarget ? "plan.skip.noCloserTarget" : "plan.skip.noQualityEvidence";
                return plan;
            }
            plan.Candidate = best;
            plan.IsNoOp = false;
            plan.TargetWidth = best.Width;
            plan.TargetHeight = best.Height;
            plan.TargetMaxSize = best.MaxSize;
            plan.TargetFormat = best.Format;
            plan.TargetMipMaps = best.MipMaps;
            plan.TargetBytes = best.Bytes;
            plan.SavingBytes = plan.SourceBytes - best.Bytes;
            plan.SavingPercent = 100.0 * plan.SavingBytes / plan.SourceBytes;
            plan.ReductionAchieved = (double)plan.SourceBytes / best.Bytes;
            plan.FormatChanged = best.Format != r.CurrentFormat;
            plan.ResolutionChanged = best.Width != r.SourceWidth || best.Height != r.SourceHeight;
            plan.MipMapChanged = best.MipMaps != r.CurrentMipMaps;
            plan.HasEstimatedPsnr = true;
            plan.EstimatedPsnrDb = quality.Psnr;
            plan.Confidence = QualityConfidence.Medium;
            if (visualQuality != quality)
                plan.AddNote("plan.note.visualQuality", standardLevel == StandardOptimizationLevel.Maximum ? "256" : "512",
                    visualQuality.MinLocalSsim.ToString("0.####", CultureInfo.InvariantCulture));
            plan.AddNote("plan.note.adaptiveAccepted", best.Filter.ToString(), best.CompressionQuality.ToString(CultureInfo.InvariantCulture),
                double.IsPositiveInfinity(quality.Psnr) ? "∞" : quality.Psnr.ToString("0.##", CultureInfo.InvariantCulture),
                quality.Ssim.ToString("0.####", CultureInfo.InvariantCulture));
            return plan;
        }

        private static bool Between(double value, double min, double max)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max;
        }

        private static bool ValidQuality(CandidateQuality quality)
        {
            return quality != null && quality.Available && !double.IsNaN(quality.Psnr) && quality.Psnr >= 0 &&
                Between(quality.Ssim, -1, 1) && Between(quality.MinLocalSsim, -1, 1) &&
                Between(quality.AlphaCoverageError, 0, 1) && Between(quality.EdgeError, 0, 1) &&
                Between(quality.MeanColorError, 0, 1) && Between(quality.MaxChannelError, 0, 1) && Between(quality.NormalAngularError, 0, 180);
        }

        private static bool IsDataTexture(TexturePlanRequest request, SemanticCategory category)
        {
            return category == SemanticCategory.Masks || request.Usage == TextureUsageKind.DataMask ||
                request.Usage == TextureUsageKind.GreyscaleData || request.Usage == TextureUsageKind.NormalMap || request.ImporterTypeIsNormalMap;
        }

        private static bool ValidSettings(TextureSettings settings)
        {
            return Between(settings.MinPsnr, 0, 200) && Between(settings.MinSsim, 0, 1) &&
                Between(settings.DesiredReduction, 1, double.MaxValue) && settings.MaxResolution >= 0 &&
                settings.MaxDownscaleSteps >= -1 && settings.MaxDownscaleSteps <= 30 &&
                Enum.IsDefined(typeof(OptimizationPlatform), settings.Platform) &&
                Enum.IsDefined(typeof(OptimizationEffort), settings.Effort);
        }
    }
}
