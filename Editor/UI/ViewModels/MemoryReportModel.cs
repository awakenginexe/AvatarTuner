// AvatarTuner v1.0.0 — shared memory numbers (Unity free).
//
// One source of truth for every "before / after / savings" number the UI shows, so the
// Overview dashboard, the Optimization Results page and the exported report can never
// disagree. Honesty rules encoded here:
//   - Source memory is the scanned source textures' GPU memory (computed, not measured).
//   - Output memory is computed from the really generated outputs when a run exists;
//     before a run it is the plan's estimate and is labelled as potential.
//   - Savings are always Source - Output of the SAME texture set, so the displayed
//     before/after/saving trio is internally consistent even after rounding.
//   - Generated outputs are standalone files; nothing is applied to the avatar in the
//     scene, so there is no "active avatar" measurement to report - ever.

using System;
using System.Collections.Generic;
using System.Globalization;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;

namespace AvatarTuner.Editor.ViewModels
{
    /// <summary>The shared before/after/savings numbers for one avatar.</summary>
    public sealed class MemoryNumbers
    {
        /// <summary>GPU texture memory of the source textures. 0 when unknown.</summary>
        public long SourceBytes;

        /// <summary>GPU texture memory of the optimized output. 0 when unknown.</summary>
        public long OutputBytes;

        /// <summary>SourceBytes - OutputBytes (raw arithmetic on the shared totals).</summary>
        public long SavingBytes;

        /// <summary>SavingBytes / SourceBytes as a percentage of the raw totals.</summary>
        public double SavingPercent;

        /// <summary>True when source/output memory is known at all (a plan exists).</summary>
        public bool HasSource;

        /// <summary>True when OutputBytes reflects a real (or estimated) output set.</summary>
        public bool HasOutput;

        /// <summary>
        /// True when OutputBytes was derived from really generated output files. False
        /// means it is the plan's estimate and must be labelled as computed/potential.
        /// </summary>
        public bool OutputFromGeneratedOutputs;

        /// <summary>
        /// True when the saving is only potential (nothing generated yet). A realized
        /// saving is only ever claimed alongside generated outputs.
        /// </summary>
        public bool SavingsArePotential
        {
            get { return !OutputFromGeneratedOutputs; }
        }

        /// <summary>
        /// Texture memory from what the avatar references right now, computed from the
        /// current references. Only meaningful with <see cref="HasCurrent" />.
        /// </summary>
        public long CurrentBytes;

        /// <summary>True when every currently referenced texture could be valued.</summary>
        public bool HasCurrent;

        // ---- per-texture counts of the same run/plan
        public int OptimizedCount;
        public int SkippedCount;
        public int ExcludedCount;
        public int ReviewCount;
    }

    /// <summary>
    /// Display strings for the before/after/saving trio. The three values are rounded to a
    /// shared unit and the saving is the exact difference of the two displayed values, so
    /// "249.18 MiB - 43.86 MiB" is shown as "205.32 MiB" and never a rounding artifact.
    /// </summary>
    public sealed class MemoryTrio
    {
        public string BeforeText;
        public string AfterText;
        public string SavingText;
        public string SavingPercentText;
        public bool HasAfter;

        /// <summary>Current-references memory text (same shared unit), when known.</summary>
        public string CurrentText;

        public bool HasCurrent;
    }

    public static class MemoryReportModel
    {
        /// <summary>
        /// Builds the shared numbers from the live working set. Textures the generator
        /// skipped (or the user excluded) count at their SOURCE memory in the output total,
        /// because that is what the output set really contains.
        /// </summary>
        public static MemoryNumbers Build(
            AvatarAnalysisReport report,
            TextureOptimizationPlan plan,
            TextureGenerationResult generation)
        {
            var numbers = new MemoryNumbers();
            if (report == null || plan == null)
            {
                return numbers;
            }

            numbers.HasSource = true;
            numbers.SourceBytes = plan.SourceBytesTotal;

            if (generation == null)
            {
                // No run yet: the plan's own estimate, explicitly potential.
                numbers.HasOutput = true;
                numbers.OutputFromGeneratedOutputs = false;
                numbers.OutputBytes = plan.TargetBytesTotal;
                numbers.OptimizedCount = plan.ChangedCount;
                numbers.SkippedCount = 0;
                numbers.ExcludedCount = plan.ExcludedCount;
                numbers.ReviewCount = TextureRows.CountNeedingReview(plan);
            }
            else
            {
                // A run happened: output memory comes from what was really written.
                // Textures without a generated output stay at their source memory.
                numbers.HasOutput = true;
                numbers.OutputFromGeneratedOutputs = true;
                numbers.OutputBytes = OutputBytesFromGenerated(plan, generation);
                numbers.OptimizedCount = generation.Generated.Count;
                numbers.SkippedCount = generation.SkippedCount;
                numbers.ExcludedCount = plan.ExcludedCount;
                numbers.ReviewCount = TextureRows.CountNeedingReview(plan);
            }

            numbers.SavingBytes = numbers.SourceBytes - numbers.OutputBytes;
            numbers.SavingPercent = SavingPercentOf(numbers.SourceBytes, numbers.OutputBytes);
            return numbers;
        }

        /// <summary>
        /// Rebuilds the numbers of a previous run from the manifest, so reopening the tool
        /// restores honest results without a re-scan. Returns empty numbers when the
        /// manifest has nothing recorded (or predates the recorded metrics).
        /// </summary>
        public static MemoryNumbers FromRecordedState(AvatarOptimizationState recorded)
        {
            var numbers = new MemoryNumbers();
            if (recorded == null || (recorded.SourceBytesTotal <= 0 && recorded.OutputBytesTotal <= 0))
            {
                return numbers;
            }

            numbers.HasSource = true;
            numbers.SourceBytes = recorded.SourceBytesTotal;
            numbers.HasOutput = true;
            numbers.OutputFromGeneratedOutputs = true;
            numbers.OutputBytes = recorded.OutputBytesTotal;
            numbers.SavingBytes = numbers.SourceBytes - numbers.OutputBytes;
            numbers.SavingPercent = SavingPercentOf(numbers.SourceBytes, numbers.OutputBytes);
            numbers.OptimizedCount = recorded.OptimizedCount;
            numbers.SkippedCount = recorded.SkippedCount;
            numbers.ExcludedCount = recorded.ExcludedCount;
            numbers.ReviewCount = recorded.ReviewCount;
            return numbers;
        }

        /// <summary>
        /// Formats the trio from the shared raw totals with rounding that stays internally
        /// consistent: the saving is displayed as the difference of the displayed before
        /// and after values, so users never see "249.18 - 43.86 = 205.33".
        /// </summary>
        public static MemoryTrio FormatTrio(MemoryNumbers numbers)
        {
            var trio = new MemoryTrio();
            if (numbers == null || !numbers.HasSource)
            {
                return trio;
            }

            long unit;
            var scale = PickUnit(Math.Max(numbers.SourceBytes, Math.Max(numbers.OutputBytes, numbers.SavingBytes)), out unit);

            var before = Round2(numbers.SourceBytes / scale);
            var after = numbers.HasOutput ? Round2(numbers.OutputBytes / scale) : 0.0;
            var saving = Round2(before - after);

            trio.BeforeText = FormatValue(before, unit);
            trio.HasAfter = numbers.HasOutput;
            trio.AfterText = numbers.HasOutput ? FormatValue(after, unit) : string.Empty;
            trio.SavingText = numbers.HasOutput ? FormatValue(saving, unit) : string.Empty;
            trio.SavingPercentText = numbers.HasOutput
                ? numbers.SavingPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%"
                : string.Empty;

            trio.HasCurrent = numbers.HasCurrent;
            if (numbers.HasCurrent)
            {
                trio.CurrentText = FormatValue(Round2(numbers.CurrentBytes / scale), unit);
            }

            return trio;
        }

        /// <summary>The unit suffix used by <see cref="FormatTrio" /> for a byte scale.</summary>
        public static string UnitName(long bytes)
        {
            long unit;
            PickUnit(bytes, out unit);
            return UnitNames[unit];
        }

        // ===================================================================== helpers

        private static readonly string[] UnitNames = { "B", "KiB", "MiB", "GiB", "TiB" };

        private static double PickUnit(long bytes, out long unit)
        {
            double scale = 1.0;
            unit = 0;
            var value = (double)bytes;
            while (value >= 1024.0 && unit < UnitNames.Length - 1)
            {
                value /= 1024.0;
                scale *= 1024.0;
                unit++;
            }

            return scale;
        }

        private static double Round2(double value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static string FormatValue(double value, long unit)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture) + " " + UnitNames[unit];
        }

        private static double SavingPercentOf(long sourceBytes, long outputBytes)
        {
            if (sourceBytes <= 0)
            {
                return 0.0;
            }

            return (sourceBytes - outputBytes) * 100.0 / sourceBytes;
        }

        /// <summary>
        /// Output memory from really generated outputs: generated textures at their
        /// planned target memory, every other planned texture at its source memory
        /// (skipped, excluded or unchanged textures really are still at source size).
        /// </summary>
        internal static long OutputBytesFromGenerated(
            TextureOptimizationPlan plan,
            TextureGenerationResult generation)
        {
            var generatedByGuid = new Dictionary<string, GeneratedTextureAsset>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < generation.Generated.Count; i++)
            {
                var asset = generation.Generated[i];
                if (!string.IsNullOrEmpty(asset.SourceGuid))
                {
                    generatedByGuid[asset.SourceGuid] = asset;
                }
            }

            long total = 0;
            for (var i = 0; i < plan.Textures.Count; i++)
            {
                var planned = plan.Textures[i];
                if (planned.Plan == null)
                {
                    continue;
                }

                GeneratedTextureAsset asset;
                if (generatedByGuid.TryGetValue(planned.TextureGuid ?? string.Empty, out asset))
                {
                    total += asset.PlannedTargetBytes;
                }
                else
                {
                    total += planned.Plan.SourceBytes;
                }
            }

            return total;
        }
    }
}
