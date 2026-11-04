// AvatarTuner v1.0.0 — texture list view model (Unity free).
//
// One row per texture with everything the list shows: what will happen (recommended
// change), what it saves, how risky it is, and the state chip. The state is derived from
// the plan's own decisions and reasons - never from guesswork - so the row a user reads in
// the UI is exactly the row the offline tests pin down.
//
// States: AlreadyOptimized (nothing to do), OptimizationAvailable (real changes proposed),
// RequiresManualReview (warnings or third-party protection), Excluded (user choice),
// Unsupported (the plan refuses the request).

using System;
using System.Collections.Generic;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;

namespace AvatarTuner.Editor.ViewModels
{
    public enum TextureRowStatus
    {
        OptimizationAvailable = 0,
        AlreadyOptimized,
        RequiresManualReview,
        Excluded,
        Unsupported
    }

    public enum TextureSortMode
    {
        Name = 0,
        Vram,
        Savings,
        Status
    }

    public sealed class TextureRowModel
    {
        public string Guid;
        public string Name;
        public string Path;

        public int Width;
        public int Height;

        /// <summary>Current format text as reported (or "unknown" marker key).</summary>
        public string FormatText;
        public bool FormatKnown;

        /// <summary>Planned format text; empty when nothing changes.</summary>
        public string TargetFormatText;

        public long SourceBytes;
        public bool SourceBytesKnown;
        public long TargetBytes;
        public long SavingBytes;
        public double SavingPercent;
        public bool SavingUnknown;

        public TextureRowStatus Status;

        /// <summary>Quality risk level derived from plan warnings and confidence.</summary>
        public CompatRisk Risk;

        public bool IsProtected;
        public bool IsProtectedByUser;

        /// <summary>Raw planner exclusion flag (user choice or third-party protection).</summary>
        public bool IsExcluded;

        public bool HasChanges;

        /// <summary>Plan notes (keys + args) explaining the row.</summary>
        public readonly List<PlanNote> Notes = new List<PlanNote>();

        /// <summary>Why the row is skipped, when it is.</summary>
        public string SkipReasonKey;

        public TextureUsageKind Usage;
        public int BoundPropertyCount;
        public int MaterialCount;

        public static string StatusLabelKey(TextureRowStatus status)
        {
            switch (status)
            {
                case TextureRowStatus.AlreadyOptimized: return "ui.textureStatus.optimized";
                case TextureRowStatus.OptimizationAvailable: return "ui.textureStatus.available";
                case TextureRowStatus.RequiresManualReview: return "ui.textureStatus.manualReview";
                case TextureRowStatus.Excluded: return "ui.textureStatus.excluded";
                default: return "ui.textureStatus.unsupported";
            }
        }

        public static SemanticTone StatusTone(TextureRowStatus status)
        {
            switch (status)
            {
                case TextureRowStatus.AlreadyOptimized: return SemanticTone.Success;
                case TextureRowStatus.OptimizationAvailable: return SemanticTone.Info;
                case TextureRowStatus.RequiresManualReview: return SemanticTone.Warning;
                case TextureRowStatus.Excluded: return SemanticTone.Neutral;
                default: return SemanticTone.Error;
            }
        }
    }

    public static class TextureRows
    {
        /// <summary>Builds one row from scan data plus the planner's decision.</summary>
        public static TextureRowModel Build(TextureEntry entry, PlannedTexture planned)
        {
            if (entry == null)
            {
                throw new ArgumentNullException("entry");
            }

            var row = new TextureRowModel();
            row.Guid = entry.Guid;
            row.Name = string.IsNullOrEmpty(entry.Name) ? entry.Guid : entry.Name;
            row.Path = entry.Path;
            row.Width = entry.Width;
            row.Height = entry.Height;
            row.FormatText = string.IsNullOrEmpty(entry.CurrentFormatName) ? string.Empty : entry.CurrentFormatName;
            row.FormatKnown = entry.CurrentFormatKnown;
            row.Usage = entry.Usage;
            row.BoundPropertyCount = entry.BoundProperties.Count;
            row.MaterialCount = entry.UsedByMaterialGuids.Count;

            if (planned == null || planned.Plan == null)
            {
                row.Status = TextureRowStatus.Unsupported;
                return row;
            }

            var plan = planned.Plan;
            row.IsProtected = planned.ProtectedByThirdParty;
            row.IsExcluded = plan.IsExcluded;
            row.IsProtectedByUser = plan.IsExcluded && !planned.ProtectedByThirdParty;
            row.HasChanges = planned.HasChanges;
            row.SkipReasonKey = plan.SkipReasonKey;
            row.Notes.AddRange(plan.Notes);
            row.SourceBytes = plan.SourceBytes;
            row.SourceBytesKnown = plan.SourceBytesKnown || plan.SourceBytes > 0;
            row.TargetBytes = plan.TargetBytes;
            row.SavingBytes = plan.SavingBytes;
            row.SavingPercent = plan.SavingPercent;
            row.SavingUnknown = plan.SavingUnknown;

            if (plan.FormatChanged &&
                plan.TargetFormat != TunerTextureFormat.Automatic &&
                plan.TargetFormat != TunerTextureFormat.Unknown)
            {
                row.TargetFormatText = TextureFormatInfo.DisplayName(plan.TargetFormat);
            }

            row.Status = Classify(planned, plan);
            row.Risk = AssessRisk(plan);
            return row;
        }

        /// <summary>Builds every row, one per texture, in plan order.</summary>
        public static List<TextureRowModel> BuildAll(
            AvatarAnalysisReport report,
            TextureOptimizationPlan plan)
        {
            var rows = new List<TextureRowModel>();
            if (report == null)
            {
                return rows;
            }

            for (var i = 0; i < report.Textures.Count; i++)
            {
                var entry = report.Textures[i];
                rows.Add(Build(entry, plan == null ? null : FindPlanned(plan, entry.Guid)));
            }

            return rows;
        }

        /// <summary>Case-insensitive filter over name and path.</summary>
        public static List<TextureRowModel> Filter(IEnumerable<TextureRowModel> rows, string query)
        {
            var result = new List<TextureRowModel>();
            if (rows == null)
            {
                return result;
            }

            foreach (var row in rows)
            {
                if (row == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(query) ||
                    (!string.IsNullOrEmpty(row.Name) &&
                     row.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(row.Path) &&
                     row.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    result.Add(row);
                }
            }

            return result;
        }

        /// <summary>Sorts in place. Deterministic: ties break by name.</summary>
        public static void Sort(List<TextureRowModel> rows, TextureSortMode mode)
        {
            if (rows == null)
            {
                return;
            }

            rows.Sort((left, right) => Compare(left, right, mode));
        }

        public static int CountNeedingReview(TextureOptimizationPlan plan)
        {
            var count = 0;
            if (plan == null)
            {
                return count;
            }

            for (var i = 0; i < plan.Textures.Count; i++)
            {
                if (Classify(plan.Textures[i], plan.Textures[i].Plan) ==
                    TextureRowStatus.RequiresManualReview)
                {
                    count++;
                }
            }

            return count;
        }

        // ===================================================================== classification

        internal static TextureRowStatus Classify(PlannedTexture planned, TexturePlan plan)
        {
            if (plan == null)
            {
                return TextureRowStatus.Unsupported;
            }

            if (planned.ProtectedByThirdParty)
            {
                return TextureRowStatus.RequiresManualReview;
            }

            if (plan.IsExcluded)
            {
                return TextureRowStatus.Excluded;
            }

            if (plan.IsNoOp)
            {
                if (plan.SkipReasonKey == "plan.skip.invalidRequest")
                {
                    return TextureRowStatus.Unsupported;
                }

                return TextureRowStatus.AlreadyOptimized;
            }

            return HasWarning(plan)
                ? TextureRowStatus.RequiresManualReview
                : TextureRowStatus.OptimizationAvailable;
        }

        private static bool HasWarning(TexturePlan plan)
        {
            for (var i = 0; i < plan.Notes.Count; i++)
            {
                if (plan.Notes[i].Level != PlanNote.Severity.Info)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Quality risk, honestly derived: High when the planner itself raised a warning,
        /// Medium when there is no measured/estimated quality evidence for a format change,
        /// Low otherwise. No risk level is ever invented from nothing.
        /// </summary>
        private static CompatRisk AssessRisk(TexturePlan plan)
        {
            if (HasWarning(plan))
            {
                return CompatRisk.High;
            }

            if (plan.IsNoOp || plan.IsExcluded)
            {
                return CompatRisk.Low;
            }

            if (plan.FormatChanged &&
                plan.Confidence != QualityConfidence.High &&
                plan.Confidence != QualityConfidence.Medium)
            {
                return CompatRisk.Medium;
            }

            return CompatRisk.Low;
        }

        private static PlannedTexture FindPlanned(TextureOptimizationPlan plan, string guid)
        {
            for (var i = 0; i < plan.Textures.Count; i++)
            {
                if (string.Equals(plan.Textures[i].TextureGuid, guid, StringComparison.OrdinalIgnoreCase))
                {
                    return plan.Textures[i];
                }
            }

            return null;
        }

        /// <summary>Deterministic comparison; ties always break by name.</summary>
        private static int Compare(TextureRowModel left, TextureRowModel right, TextureSortMode mode)
        {
            if (left == null || right == null)
            {
                return 0;
            }

            int result;
            switch (mode)
            {
                case TextureSortMode.Vram:
                    result = right.SourceBytes.CompareTo(left.SourceBytes);
                    break;
                case TextureSortMode.Savings:
                    result = right.SavingBytes.CompareTo(left.SavingBytes);
                    break;
                case TextureSortMode.Status:
                    result = StatusOrder(left.Status).CompareTo(StatusOrder(right.Status));
                    break;
                default:
                    result = 0;
                    break;
            }

            if (result == 0)
            {
                result = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            }

            return result;
        }

        private static int StatusOrder(TextureRowStatus status)
        {
            switch (status)
            {
                case TextureRowStatus.RequiresManualReview: return 0;
                case TextureRowStatus.OptimizationAvailable: return 1;
                case TextureRowStatus.Excluded: return 2;
                case TextureRowStatus.Unsupported: return 3;
                default: return 4;
            }
        }
    }
}
