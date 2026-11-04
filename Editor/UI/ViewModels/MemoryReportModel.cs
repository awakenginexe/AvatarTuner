// Shared GPU texture-memory accounting. Imported properties are estimates of GPU allocation.

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
        /// <summary>Known source GPU memory subtotal; HasSource indicates completeness.</summary>
        public long SourceBytes;
        public MemoryReportBasis Basis;
        public int UnknownSourceCount;
        public int UnknownOutputCount;

        /// <summary>Known output GPU memory subtotal; HasOutput indicates completeness.</summary>
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
        /// True when generated outputs have imported byte estimates and the output total is complete.
        /// </summary>
        public bool OutputFromGeneratedOutputs;

        /// <summary>
        /// True when the output uses planned estimates.
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

            var byGuid = new Dictionary<string, GeneratedTextureAsset>(StringComparer.OrdinalIgnoreCase);
            var byPath = new Dictionary<string, GeneratedTextureAsset>(StringComparer.OrdinalIgnoreCase);
            if (generation != null)
                foreach (var asset in generation.Generated)
                {
                    if (asset == null) continue;
                    if (!string.IsNullOrEmpty(asset.SourceGuid)) byGuid[asset.SourceGuid] = asset;
                    if (!string.IsNullOrEmpty(asset.SourcePath)) byPath[asset.SourcePath] = asset;
                }

            var seenGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allImported = generation != null && generation.Generated.Count > 0;
            var matchedGenerated = 0;
            foreach (var item in plan.Textures)
            {
                if (item == null) { numbers.UnknownSourceCount++; numbers.UnknownOutputCount++; continue; }
                if ((!string.IsNullOrEmpty(item.TextureGuid) && !seenGuids.Add(item.TextureGuid)) ||
                    (!string.IsNullOrEmpty(item.TexturePath) && !seenPaths.Add(item.TexturePath))) continue;
                var decision = item.Plan;
                if (decision == null) { numbers.UnknownSourceCount++; numbers.UnknownOutputCount++; continue; }
                bool sourceKnown = decision.SourceBytes > 0;
                if (sourceKnown) numbers.SourceBytes += decision.SourceBytes;
                else numbers.UnknownSourceCount++;
                GeneratedTextureAsset asset = null;
                if (generation != null && !string.IsNullOrEmpty(item.TextureGuid)) byGuid.TryGetValue(item.TextureGuid, out asset);
                if (generation != null && asset == null && !string.IsNullOrEmpty(item.TexturePath)) byPath.TryGetValue(item.TexturePath, out asset);
                long output;
                if (asset != null)
                {
                    matchedGenerated++;
                    if (asset.ImportedBytesKnown && asset.ImportedBytes > 0) output = asset.ImportedBytes;
                    else { output = asset.PlannedTargetBytes; allImported = false; }
                }
                else output = generation == null ? decision.TargetBytes : decision.SourceBytes;
                if (output > 0) numbers.OutputBytes += output;
                else numbers.UnknownOutputCount++;
            }
            numbers.HasSource = numbers.UnknownSourceCount == 0;
            numbers.HasOutput = numbers.UnknownOutputCount == 0;
            allImported = allImported && generation != null && matchedGenerated == generation.Generated.Count;
            numbers.Basis = allImported ? MemoryReportBasis.ImportedResult : MemoryReportBasis.PlannedEstimate;
            numbers.OutputFromGeneratedOutputs = allImported && numbers.HasOutput;
            numbers.OptimizedCount = generation == null ? plan.ChangedCount : generation.Generated.Count;
            numbers.SkippedCount = generation == null ? 0 : generation.SkippedCount;
            numbers.ExcludedCount = plan.ExcludedCount;
            numbers.ReviewCount = TextureRows.CountNeedingReview(plan);
            if (numbers.HasSource && numbers.HasOutput)
            {
                numbers.SavingBytes = numbers.SourceBytes - numbers.OutputBytes;
                numbers.SavingPercent = SavingPercentOf(numbers.SourceBytes, numbers.OutputBytes);
            }
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
            if (recorded == null) return numbers;
            numbers.HasSource = recorded.HasSource;
            numbers.SourceBytes = recorded.SourceBytesTotal;
            numbers.HasOutput = recorded.HasOutput;
            numbers.OutputBytes = recorded.OutputBytesTotal;
            numbers.UnknownSourceCount = recorded.UnknownSourceCount;
            numbers.UnknownOutputCount = recorded.UnknownOutputCount;
            numbers.Basis = recorded.Basis;
            numbers.OutputFromGeneratedOutputs = recorded.Basis != MemoryReportBasis.PlannedEstimate && recorded.HasOutput;
            if (numbers.HasSource && numbers.HasOutput)
            {
                numbers.SavingBytes = numbers.SourceBytes - numbers.OutputBytes;
                numbers.SavingPercent = SavingPercentOf(numbers.SourceBytes, numbers.OutputBytes);
            }
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

    }
}
