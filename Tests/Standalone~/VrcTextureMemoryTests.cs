using System;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;
using AvatarTuner.Editor.ViewModels;

namespace AvatarTuner.StandaloneTests
{
    public static class VrcTextureMemoryTests
    {
        [Test("SDK savings can be signed even when physical memory decreases")]
        public static void SignedSaving()
        {
            long source, target;
            VrcTextureMemory.TryEstimateBytes(2, 2, TunerTextureFormat.BC1, 1, 1, out source);
            VrcTextureMemory.TryEstimateBytes(2, 2, TunerTextureFormat.R8, 1, 1, out target);
            Assert.IsTrue(VramCalculator.LevelBytes(2, 2, TunerTextureFormat.BC1) > VramCalculator.LevelBytes(2, 2, TunerTextureFormat.R8), "physical bytes decrease");
            Assert.IsTrue(source < target, "SDK bytes increase");
            Assert.AreEqual("-1 MB", TextureFormatInfo.FormatTextureMemory(-1048576), "signed savings render without throwing");
        }

        [Test("unknown texture memory is propagated to rows and suppresses savings insights")]
        public static void UnknownReporting()
        {
            var report = new AvatarAnalysisReport();
            var entry = new TextureEntry { Guid = "unknown", Width = 64, Height = 64 };
            report.Textures.Add(entry);
            var plan = TexturePlanBuilder.Build(report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, null);
            var row = TextureRows.Build(entry, plan.Textures[0]);
            Assert.IsFalse(row.SourceBytesKnown, "unknown format has no source estimate");
            Assert.IsTrue(plan.Textures[0].Plan.SavingUnknown, "incomplete source/output cannot claim savings");
            Assert.IsTrue(row.SavingUnknown, "row preserves incomplete savings");
            var partial = TextureRows.Build(entry, new PlannedTexture { Plan = new TexturePlan
            {
                SourceBytes = 100, SourceBytesKnown = true, TargetBytes = 0, TargetBytesKnown = false,
                SavingBytes = 100, SavingPercent = 100, SavingUnknown = true
            } });
            Assert.IsTrue(partial.SourceBytesKnown && partial.SavingUnknown,
                "a known source with unknown target must not imply a full saving");
            plan.SavingBytes = 1048576;
            var insights = DashboardModel.BuildInsights(report, plan, null);
            Assert.IsFalse(insights.Exists(item => item.MessageKey == "ui.insight.potentialSaving"), "partial totals do not imply full savings");
        }
        [Test("SDK RGB24 reporting uses four bytes while physical planning retains three")]
        public static void Rgb24Reporting()
        {
            long bytes;
            Assert.IsTrue(VrcTextureMemory.TryEstimateBytes(1024, 1024, TunerTextureFormat.RGB24, 1, 1, out bytes), "known format");
            Assert.AreEqual(4194304L, bytes, "SDK RGB24 table entry");
            Assert.AreEqual(3145728L, VramCalculator.LevelBytes(1024, 1024, TunerTextureFormat.RGB24), "physical estimate is preserved");
        }

        [Test("SDK mip accounting uses shifted area, per-level float rounding and no block padding")]
        public static void SdkMipArithmetic()
        {
            long bytes;
            Assert.IsTrue(VrcTextureMemory.TryEstimateBytes(8, 4, TunerTextureFormat.BC7, 4, 1, out bytes), "known mip chain");
            Assert.AreEqual(42L, bytes, "32 + 8 + 2 + 0, including SDK rectangular tail behavior");
            Assert.IsTrue(VrcTextureMemory.TryEstimateBytes(3, 3, TunerTextureFormat.BC1, 1, 1, out bytes), "fractional byte count");
            Assert.AreEqual(4L, bytes, "4.5 rounds to even as Mathf.RoundToInt does");
            Assert.IsTrue(VrcTextureMemory.TryEstimateBytes(1024, 1024, TunerTextureFormat.ASTC_6x6, 1, 1, out bytes), "ASTC table entry");
            Assert.AreEqual(466616L, bytes, "SDK uses 0.445f bytes per pixel");
            Assert.IsTrue(VrcTextureMemory.TryEstimateBytes(8, 8, TunerTextureFormat.BC7, 1, 6, out bytes), "cube");
            Assert.AreEqual(64L, bytes, "installed SDK counts cube area without a six-face multiplier");
            Assert.IsFalse(VrcTextureMemory.TryEstimateBytes(8, 4, TunerTextureFormat.Unknown, 1, 1, out bytes), "unknown format stays unknown");
            Assert.IsFalse(VrcTextureMemory.TryEstimateBytes(8, 4, TunerTextureFormat.RGBA32, 5, 1, out bytes), "impossible mip chain");
            Assert.IsFalse(VrcTextureMemory.TryEstimateBytes(int.MaxValue, 2, TunerTextureFormat.RGBA32, 1, 1, out bytes), "invalid area does not wrap");
        }

        [Test("plan totals use SDK accounting for both unchanged source and projected output")]
        public static void PlanReporting()
        {
            var report = new AvatarAnalysisReport();
            report.Textures.Add(new TextureEntry
            {
                Guid = "rgb", Path = "Assets/rgb.png", Name = "rgb", Width = 1024, Height = 1024,
                CurrentFormatKnown = true, CurrentFormatName = "RGB24", CurrentMipCount = 1,
                MipMapsEnabled = false, Category = SemanticCategory.Outfit
            });
            var plan = TexturePlanBuilder.Build(report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced),
                new System.Collections.Generic.Dictionary<string, TextureOverride>
                { ["rgb"] = new TextureOverride { Excluded = true } }, null);
            Assert.AreEqual(4194304L, plan.SourceBytesTotal, "SDK source estimate");
            Assert.AreEqual(plan.SourceBytesTotal, plan.TargetBytesTotal, "unchanged bytes use same accounting");
            var numbers = MemoryReportModel.Build(report, plan, null);
            Assert.AreEqual(4194304L, numbers.SourceBytes, "report uses the shared source total");

            var changed = TexturePlanBuilder.Build(report, PresetCatalog.GetPolicy(OptimizationPreset.SizeFirst), null, null);
            var decision = changed.Textures[0].Plan;
            Assert.IsFalse(decision.IsNoOp, "fixture produces a projected output");
            long expected;
            Assert.IsTrue(VrcTextureMemory.TryEstimateBytes(decision.TargetWidth, decision.TargetHeight, decision.TargetFormat,
                decision.TargetMipMaps ? VramCalculator.MipLevelCount(decision.TargetWidth, decision.TargetHeight) : 1, 1, out expected), "known projected format");
            Assert.AreEqual(expected, decision.TargetBytes, "projected output uses the same SDK calculation");
        }

        [Test("SDK rounding to zero is a known estimate, including generated outputs")]
        public static void KnownZero()
        {
            var report = new AvatarAnalysisReport();
            report.Textures.Add(new TextureEntry { Guid = "tiny", Path = "Assets/tiny.png", Width = 1, Height = 1,
                CurrentFormatName = "DXT1", CurrentFormatKnown = true, CurrentMipCount = 1 });
            var plan = TexturePlanBuilder.Build(report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced),
                new System.Collections.Generic.Dictionary<string, TextureOverride> { ["tiny"] = new TextureOverride { Excluded = true } }, null);
            var numbers = MemoryReportModel.Build(report, plan, null);
            Assert.IsTrue(numbers.HasSource && numbers.HasOutput, "rounded zero is complete");
            Assert.AreEqual(0L, numbers.SourceBytes, "SDK rounds half a byte to zero");
            var generation = new TextureGenerationResult();
            generation.Generated.Add(new GeneratedTextureAsset { SourceGuid = "tiny", ImportedBytesKnown = true, ImportedBytes = 0 });
            numbers = MemoryReportModel.Build(report, plan, generation);
            Assert.IsTrue(numbers.HasOutput && numbers.OutputFromGeneratedOutputs, "imported zero is complete and uses imported facts");
        }

        [Test("old recorded physical totals are not relabelled as SDK totals")]
        public static void OldRecordedTotals()
        {
            var state = new AvatarOptimizationState { HasSource = true, HasOutput = true, SourceBytesTotal = 100, OutputBytesTotal = 50 };
            var dict = state.ToDictionary();
            dict.Remove("memoryAccountingVersion");
            var old = AvatarOptimizationState.FromDictionary(dict);
            Assert.IsFalse(MemoryReportModel.FromRecordedState(old).HasSource, "old accounting requires a new analysis");
            Assert.IsTrue(MemoryReportModel.FromRecordedState(AvatarOptimizationState.FromDictionary(state.ToDictionary())).HasSource,
                "new accounting survives persistence");
        }
    }
}
