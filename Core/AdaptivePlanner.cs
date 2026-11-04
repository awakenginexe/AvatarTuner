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
        public double Psnr, Ssim, AlphaCoverageError, EdgeError, MeanColorError, MaxChannelError, NormalAngularError;
        public bool Available;
        public static CandidateQuality Compare(byte[] reference, byte[] candidate, int width, int height, bool alphaUsed, bool normalMap = false)
        {
            var q = new CandidateQuality();
            q.Psnr = QualityMetrics.Psnr(reference, candidate, width, height, 4);
            q.Ssim = QualityMetrics.Ssim(reference, candidate, width, height, 4);
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
        public static List<TextureCandidate> Candidates(TexturePlanRequest r, TexturePolicy policy, TextureOverride o, TextureSettings settings, SemanticCategory category)
        {
            var result = new List<TextureCandidate>();
            if (r == null || policy == null || settings == null || !ValidSettings(settings) || !r.IsValid || !r.CurrentFormatKnown || settings.Excluded || settings.ProtectQuality || (o != null && o.Excluded) || category == SemanticCategory.Unknown || category == SemanticCategory.Mixed || r.Usage == TextureUsageKind.Unknown || r.Usage == TextureUsageKind.UserInterface || r.FaceCount != 1) return result;
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
                else if (r.Usage == TextureUsageKind.AlbedoOpaque && !r.SourceHasAlpha && !r.AlphaChannelUsed)
                {
                    formats.Add(TunerTextureFormat.BC1);
                    formats.Add(TunerTextureFormat.BC7);
                }
                else if (r.Usage == TextureUsageKind.AlbedoWithAlpha || r.Usage == TextureUsageKind.AlbedoOpaque)
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
            if (r.Usage == TextureUsageKind.Emission || r.Usage == TextureUsageKind.Detail) return result;
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
            int floor = Math.Min(max, minimum);
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
        public static TexturePlan Select(TexturePlanRequest r, TexturePolicy p, TextureOverride o, TextureSettings s, SemanticCategory category, Func<TextureCandidate, CandidateQuality> evaluate)
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
            foreach (var c in Candidates(r, p, o, s, category))
            {
                if (c.Bytes >= plan.SourceBytes || (best != null && c.Bytes > best.Bytes)) continue;
                var q = evaluate(c);
                if (q != null && (!Between(q.Ssim, -1, 1) || !Between(q.AlphaCoverageError, 0, 1) ||
                    !Between(q.EdgeError, 0, 1) || !Between(q.MeanColorError, 0, 1) ||
                    !Between(q.MaxChannelError, 0, 1) || !Between(q.NormalAngularError, 0, 180))) continue;
                if (q == null || !q.Available || double.IsNaN(q.Psnr) || double.IsNaN(q.Ssim) || double.IsNaN(q.AlphaCoverageError) || double.IsNaN(q.EdgeError) || double.IsNaN(q.MeanColorError) || double.IsNaN(q.MaxChannelError) || double.IsNaN(q.NormalAngularError) || q.Psnr < s.MinPsnr || q.Ssim < s.MinSsim || q.AlphaCoverageError > 0.02 || q.EdgeError > 0.03 || q.MeanColorError > 0.03 || q.MaxChannelError > 0.25 || q.NormalAngularError > 5) continue;
                double minPsnr = category == SemanticCategory.Face || category == SemanticCategory.Eyes ? 50 :
                    category == SemanticCategory.Hair || category == SemanticCategory.Masks ? 48 : s.MinPsnr;
                double minSsim = category == SemanticCategory.Face || category == SemanticCategory.Eyes ? 0.99 :
                    category == SemanticCategory.Hair || category == SemanticCategory.Masks ? 0.985 : s.MinSsim;
                if (q.Psnr < Math.Max(s.MinPsnr, minPsnr) || q.Ssim < Math.Max(s.MinSsim, minSsim)) continue;
                if (best != null && c.Bytes == best.Bytes &&
                    (q.Ssim < quality.Ssim || (q.Ssim == quality.Ssim && q.Psnr <= quality.Psnr))) continue;
                best = c;
                quality = q;
            }
            if (best == null)
            {
                plan.SkipReasonKey = plan.IsExcluded ? "plan.skip.excluded" : "plan.skip.noQualityEvidence";
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
            return plan;
        }

        private static bool Between(double value, double min, double max)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max;
        }

        private static bool ValidSettings(TextureSettings settings)
        {
            return Between(settings.MinPsnr, 0, 200) && Between(settings.MinSsim, 0, 1) &&
                Between(settings.DesiredReduction, 1, 16) && settings.MaxResolution >= 0 &&
                Enum.IsDefined(typeof(OptimizationPlatform), settings.Platform) &&
                Enum.IsDefined(typeof(OptimizationEffort), settings.Effort);
        }
    }
}
