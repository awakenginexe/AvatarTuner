// AvatarTuner v1.0.0 — dashboard view model (Unity free).
//
// Turns real scan/plan data into the dashboard's metric cards and insight list. Every
// number shown comes from the engines; nothing is estimated unless the planner itself
// estimated it, and savings are only ever the plan's own arithmetic. The model is Unity
// free so the offline tests can pin down exactly what the dashboard will show.

using System;
using System.Collections.Generic;
using System.Globalization;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;

namespace AvatarTuner.Editor.ViewModels
{
    /// <summary>How loudly a metric should be presented.</summary>
    public enum MetricEmphasis
    {
        /// <summary>Hero metric: large value, own card.</summary>
        Primary = 0,

        /// <summary>Supporting metric: compact card.</summary>
        Secondary
    }

    /// <summary>Semantic meaning behind a color, never the color itself.</summary>
    public enum SemanticTone
    {
        Neutral = 0,
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>One metric card on the dashboard.</summary>
    public sealed class MetricCard
    {
        public string LabelKey;

        /// <summary>Pre-formatted value text (counts, MB values). Not localized further.</summary>
        public string Value;

        /// <summary>Optional localized value (status names); used when not empty.</summary>
        public string ValueKey;

        /// <summary>Optional context line (already formatted).</summary>
        public string SubText;

        /// <summary>Optional key for the context line; used when not empty.</summary>
        public string SubTextKey;

        public MetricEmphasis Emphasis;
        public SemanticTone Tone;
    }

    /// <summary>One actionable insight on the dashboard.</summary>
    public sealed class InsightItem
    {
        public string MessageKey;
        public readonly List<string> Args = new List<string>();
        public SemanticTone Tone;

        public InsightItem(string messageKey, SemanticTone tone, params string[] args)
        {
            MessageKey = messageKey;
            Tone = tone;
            if (args != null)
            {
                Args.AddRange(args);
            }
        }
    }

    public static class DashboardModel
    {
        /// <summary>GPU memory per texture at or above which it counts as "large".</summary>
        public const long LargeTextureThresholdBytes = 4L * 1024L * 1024L;

        /// <summary>
        /// The dashboard metric cards, in display order: the honest optimization status
        /// first, then the real scan counts. The memory before/after comparison is built
        /// from the shared <see cref="MemoryNumbers" /> by the window (and pinned by the
        /// memory tests), so every page shows the same numbers.
        /// </summary>
        public static List<MetricCard> BuildMetrics(
            AvatarAnalysisReport report,
            AvatarDisplayStatus status)
        {
            var cards = new List<MetricCard>();
            if (report == null)
            {
                return cards;
            }

            var statusCard = new MetricCard
            {
                LabelKey = "ui.label.status",
                Emphasis = MetricEmphasis.Primary,
                ValueKey = StatusModel.NameKey(status),
                SubTextKey = StatusModel.SummaryKey(status),
                Tone = StatusModel.Tone(status)
            };
            cards.Add(statusCard);

            cards.Add(Count("ui.label.textures", report.Textures.Count, SemanticTone.Neutral));
            cards.Add(Count("ui.label.materials", report.Materials.Count, SemanticTone.Neutral));
            cards.Add(Count("ui.label.renderers", report.Renderers.Count, SemanticTone.Neutral));
            cards.Add(Count("ui.label.triangles", report.TotalTriangles, SemanticTone.Neutral));
            cards.Add(Count("ui.label.blendShapes", report.TotalBlendShapes, SemanticTone.Neutral));
            cards.Add(Count("ui.label.physBones", CountPhysBones(report), SemanticTone.Neutral));

            return cards;
        }

        /// <summary>
        /// Actionable insights: what a user should know before trusting the result. Order is
        /// severity first so the eye lands on what matters.
        /// </summary>
        public static List<InsightItem> BuildInsights(
            AvatarAnalysisReport report,
            TextureOptimizationPlan plan,
            CompatibilityReport compat)
        {
            var items = new List<InsightItem>();
            if (report == null)
            {
                return items;
            }

            if (compat != null)
            {
                for (var i = 0; i < compat.Blockers.Count; i++)
                {
                    items.Add(FromFinding(compat.Blockers[i], SemanticTone.Error));
                }

                for (var i = 0; i < compat.Warnings.Count; i++)
                {
                    items.Add(FromFinding(compat.Warnings[i], SemanticTone.Warning));
                }
            }

            if (plan != null)
            {
                var memory = MemoryReportModel.Build(report, plan, null);
                if (memory.HasSource && memory.HasOutput && plan.SavingBytes > 0)
                {
                    items.Add(new InsightItem(
                        "ui.insight.potentialSaving",
                        SemanticTone.Info,
                        TextureFormatInfo.FormatTextureMemory(plan.SavingBytes),
                        plan.SavingPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%"));
                }

                var large = 0;
                for (var i = 0; i < plan.Textures.Count; i++)
                {
                    if (plan.Textures[i].Plan != null &&
                        plan.Textures[i].Plan.SourceBytes >= LargeTextureThresholdBytes)
                    {
                        large++;
                    }
                }

                if (large > 0)
                {
                    items.Add(new InsightItem(
                        "analysis.largeTextures",
                        SemanticTone.Warning,
                        large.ToString(CultureInfo.InvariantCulture),
                        TextureFormatInfo.FormatTextureMemory(LargeTextureThresholdBytes)));
                }

                if (plan.ProtectedCount > 0)
                {
                    items.Add(new InsightItem(
                        "opt.protectedTextures",
                        SemanticTone.Info,
                        plan.ProtectedCount.ToString(CultureInfo.InvariantCulture)));
                }

                var review = TextureRows.CountNeedingReview(plan);
                if (review > 0)
                {
                    items.Add(new InsightItem(
                        "ui.insight.manualReview",
                        SemanticTone.Warning,
                        review.ToString(CultureInfo.InvariantCulture)));
                }
            }

            if (report.PossibleBuildTimeDependencies.Count > 0)
            {
                items.Add(new InsightItem(
                    "analysis.buildTimeSummary",
                    SemanticTone.Info,
                    report.PossibleBuildTimeDependencies.Count.ToString(CultureInfo.InvariantCulture)));
            }

            var duplicates = CountDuplicates(report);
            if (duplicates > 0)
            {
                items.Add(new InsightItem(
                    "analysis.duplicateTextures",
                    SemanticTone.Info,
                    duplicates.ToString(CultureInfo.InvariantCulture)));
            }

            if (items.Count == 0)
            {
                items.Add(new InsightItem("ui.insight.clear", SemanticTone.Success));
            }

            return items;
        }

        // ===================================================================== helpers

        internal static int CountPhysBones(AvatarAnalysisReport report)
        {
            var count = 0;
            for (var i = 0; i < report.DynamicComponents.Count; i++)
            {
                if (report.DynamicComponents[i].KindKey == "finding.kind.physBone")
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountDuplicates(AvatarAnalysisReport report)
        {
            var count = 0;
            for (var i = 0; i < report.Textures.Count; i++)
            {
                if (report.Textures[i].UsageReasons.Contains("usage.reason.duplicateContent"))
                {
                    count++;
                }
            }

            return count;
        }

        private static InsightItem FromFinding(AnalysisFinding finding, SemanticTone tone)
        {
            return new InsightItem(finding.MessageKey, tone, finding.Args.ToArray());
        }

        private static MetricCard Count(string labelKey, int value, SemanticTone tone)
        {
            return new MetricCard
            {
                LabelKey = labelKey,
                Value = value.ToString(CultureInfo.InvariantCulture),
                Emphasis = MetricEmphasis.Secondary,
                Tone = tone
            };
        }

        /// <summary>
        /// The display status for a recorded manifest status (Change Tracking shows what
        /// the manifest records; the display wording stays honest about what exists).
        /// </summary>
        internal static AvatarDisplayStatus DisplayStatusOf(OptimizationStatus status)
        {
            switch (status)
            {
                case OptimizationStatus.Optimized: return AvatarDisplayStatus.OutputGenerated;
                case OptimizationStatus.ChangesDetected: return AvatarDisplayStatus.ChangesDetected;
                case OptimizationStatus.ReoptimizationRequired: return AvatarDisplayStatus.ReoptimizationRequired;
                case OptimizationStatus.OptimizationFailed: return AvatarDisplayStatus.OptimizationFailed;
                case OptimizationStatus.Unsupported: return AvatarDisplayStatus.Unsupported;
                default: return AvatarDisplayStatus.NotOptimized;
            }
        }

        internal static string StatusNameKey(OptimizationStatus status)
        {
            return StatusModel.NameKey(DisplayStatusOf(status));
        }

        internal static SemanticTone StatusTone(OptimizationStatus status)
        {
            return StatusModel.Tone(DisplayStatusOf(status));
        }
    }
}
