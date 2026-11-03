using System;
using System.Collections.Generic;
using System.Globalization;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;
using AvatarTuner.Editor.ViewModels;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// The shared before/after/savings numbers. These tests pin the two real defects seen
    /// in the Unity test: the dashboard showed source memory while the plan showed the
    /// estimate (numbers disagreeing between panels), and the displayed trio could fail
    /// basic arithmetic because each total was rounded independently
    /// ("249.18 MiB - 43.86 MiB = 205.33 MiB").
    /// </summary>
    public static class MemoryReportTests
    {
        private const long MiB = 1024L * 1024L;

        [Test("the displayed before/after/saving trio is internally consistent")]
        public static void TrioIsInternallyConsistent()
        {
            // Raw values chosen so independent rounding shows the old 205.33 artifact:
            // 249.1823 MiB and 43.8550 MiB display as 249.18 / 43.86 while the raw
            // difference (205.3272 MiB) used to display as 205.33.
            var numbers = new MemoryNumbers
            {
                HasSource = true,
                HasOutput = true,
                SourceBytes = 261285700L,
                OutputBytes = 45987444L
            };
            numbers.SavingBytes = numbers.SourceBytes - numbers.OutputBytes;

            var trio = MemoryReportModel.FormatTrio(numbers);

            Assert.AreEqual("249.18 MiB", trio.BeforeText, "before shows the source total");
            Assert.AreEqual("43.86 MiB", trio.AfterText, "after shows the output total");
            Assert.AreEqual("205.32 MiB", trio.SavingText,
                "the saving is the difference of the DISPLAYED values, so the trio adds up");
        }

        [Test("every displayed trio subtracts exactly across many sizes")]
        public static void TrioSubtractsExactly()
        {
            long source = 1;
            for (var i = 0; i < 24; i++)
            {
                var output = source / 3 + 7919;
                var numbers = new MemoryNumbers
                {
                    HasSource = true,
                    HasOutput = true,
                    SourceBytes = source,
                    OutputBytes = output
                };
                numbers.SavingBytes = numbers.SourceBytes - numbers.OutputBytes;

                var trio = MemoryReportModel.FormatTrio(numbers);
                var before = ParseValue(trio.BeforeText);
                var after = ParseValue(trio.AfterText);
                var saving = ParseValue(trio.SavingText);

                Assert.AreClose(before - after, saving, 0.005,
                    "displayed " + trio.BeforeText + " - " + trio.AfterText + " must be " + trio.SavingText);

                if (source > long.MaxValue / 8)
                {
                    break;
                }

                source = source * 7 + 12345;
            }
        }

        [Test("before a run the saving is a potential estimate, not a realized one")]
        public static void PlanOnlyNumbersArePotential()
        {
            var plan = MakePlan();

            var numbers = MemoryReportModel.Build(MakeReport(), plan, null);

            Assert.IsTrue(numbers.HasSource, "source memory is known");
            Assert.AreEqual(plan.SourceBytesTotal, numbers.SourceBytes, "source is the plan's source total");
            Assert.IsTrue(numbers.HasOutput, "the estimate exists");
            Assert.AreEqual(plan.TargetBytesTotal, numbers.OutputBytes, "output is the plan's estimate");
            Assert.IsFalse(numbers.OutputFromGeneratedOutputs, "nothing was generated yet");
            Assert.IsTrue(numbers.SavingsArePotential, "so the saving is only potential");
            Assert.AreEqual(numbers.SourceBytes - numbers.OutputBytes, numbers.SavingBytes,
                "the saving is the raw difference of the shared totals");
            Assert.AreClose((numbers.SourceBytes - numbers.OutputBytes) * 100.0 / numbers.SourceBytes,
                numbers.SavingPercent, 0.0001, "the percent comes from the raw totals");
        }

        [Test("after a run the output memory comes from the generated outputs")]
        public static void GeneratedNumbersAreRealized()
        {
            var plan = MakePlan();
            var generation = new TextureGenerationResult();
            generation.Generated.Add(new GeneratedTextureAsset
            {
                SourceGuid = "tex-a",
                SourcePath = "Assets/Tex/a.png",
                GeneratedPath = "Assets/AvatarTuner-Generated/x/Textures/a_gen.png",
                PlannedTargetBytes = 3L * MiB
            });

            var numbers = MemoryReportModel.Build(MakeReport(), plan, generation);

            Assert.IsTrue(numbers.HasOutput, "output is known");
            Assert.IsTrue(numbers.OutputFromGeneratedOutputs,
                "output memory is derived from really generated files");
            Assert.IsFalse(numbers.SavingsArePotential, "so the saving is realized, not potential");
            Assert.AreEqual(1, numbers.OptimizedCount, "one texture received a generated output");
            Assert.AreEqual(0, numbers.SkippedCount, "the run reports its own skip count");
        }

        [Test("skipped textures stay at source memory in the output total")]
        public static void SkippedTexturesStayAtSource()
        {
            var plan = MakePlan();
            var generation = new TextureGenerationResult();
            generation.SkippedCount = 1;
            generation.Generated.Add(new GeneratedTextureAsset
            {
                SourceGuid = "tex-a",
                PlannedTargetBytes = 3L * MiB
            });

            var numbers = MemoryReportModel.Build(MakeReport(), plan, generation);

            // tex-b was planned to change but nothing was generated for it: the output set
            // really contains it at source size (4 MiB), so it counts at source memory.
            Assert.AreEqual(3L * MiB + 4L * MiB, numbers.OutputBytes,
                "skipped textures count at their source memory");
            Assert.IsTrue(numbers.OutputBytes > plan.TargetBytesTotal,
                "the realized output is honestly larger than the plan's optimistic estimate");
        }

        [Test("restored numbers come from the manifest and invent nothing")]
        public static void RestoredNumbersComeFromTheManifest()
        {
            var recorded = new AvatarOptimizationState
            {
                SourceBytesTotal = 249L * MiB,
                OutputBytesTotal = 43L * MiB,
                OptimizedCount = 34,
                SkippedCount = 12,
                ExcludedCount = 3,
                ReviewCount = 34
            };

            var numbers = MemoryReportModel.FromRecordedState(recorded);
            Assert.IsTrue(numbers.HasSource, "recorded source memory is restored");
            Assert.AreEqual(249L * MiB, numbers.SourceBytes, "source matches the manifest");
            Assert.AreEqual(43L * MiB, numbers.OutputBytes, "output matches the manifest");
            Assert.IsTrue(numbers.OutputFromGeneratedOutputs,
                "recorded numbers describe real generated outputs");
            Assert.AreEqual(34, numbers.OptimizedCount, "counts are restored");
            Assert.AreEqual(12, numbers.SkippedCount, "counts are restored");
            Assert.AreEqual(3, numbers.ExcludedCount, "counts are restored");
            Assert.AreEqual(34, numbers.ReviewCount, "counts are restored");

            Assert.IsFalse(MemoryReportModel.FromRecordedState(null).HasSource,
                "no manifest means no numbers");
            Assert.IsFalse(MemoryReportModel.FromRecordedState(new AvatarOptimizationState()).HasSource,
                "an old manifest without recorded metrics invents nothing");
        }

        [Test("counts describe the same run as the memory numbers")]
        public static void CountsMatchTheRun()
        {
            var plan = MakePlan();
            plan.ExcludedCount = 5;

            var estimate = MemoryReportModel.Build(MakeReport(), plan, null);
            Assert.AreEqual(plan.ChangedCount, estimate.OptimizedCount, "planned changes are planned, not done");
            Assert.AreEqual(5, estimate.ExcludedCount, "exclusions come from the plan");

            var generation = new TextureGenerationResult();
            generation.SkippedCount = 2;
            generation.Generated.Add(new GeneratedTextureAsset { SourceGuid = "tex-a", PlannedTargetBytes = 1 });

            var realized = MemoryReportModel.Build(MakeReport(), plan, generation);
            Assert.AreEqual(generation.Generated.Count, realized.OptimizedCount, "generated count is the run's");
            Assert.AreEqual(2, realized.SkippedCount, "skipped count is the run's");
            Assert.AreEqual(TextureRows.CountNeedingReview(plan), realized.ReviewCount,
                "review count matches the row classifier exactly");
        }

        [Test("no numbers exist before anything is analyzed")]
        public static void NothingBeforeAnalysis()
        {
            Assert.IsFalse(MemoryReportModel.Build(null, null, null).HasSource, "nothing is known");
            Assert.IsFalse(MemoryReportModel.Build(MakeReport(), null, null).HasSource,
                "a report without a plan has no memory arithmetic");
            Assert.IsFalse(MemoryReportModel.FormatTrio(null).HasAfter, "and no trio is shown");
        }

        // ===================================================================== helpers

        private static double ParseValue(string text)
        {
            var space = text.IndexOf(' ');
            return double.Parse(text.Substring(0, space), CultureInfo.InvariantCulture);
        }

        private static AvatarAnalysisReport MakeReport()
        {
            var report = new AvatarAnalysisReport();
            report.AvatarName = "TestAvatar";
            report.ScanSucceeded = true;

            var texA = new TextureEntry { Guid = "tex-a", Path = "Assets/Tex/a.png", Name = "body_skin" };
            var texB = new TextureEntry { Guid = "tex-b", Path = "Assets/Tex/b.png", Name = "prop_metal" };
            report.Textures.Add(texA);
            report.Textures.Add(texB);
            return report;
        }

        /// <summary>Two changed textures: 8→3 MiB and 4→2 MiB (24→10 MiB source→target).</summary>
        private static TextureOptimizationPlan MakePlan()
        {
            var plan = new TextureOptimizationPlan();

            var a = new TexturePlan
            {
                IsNoOp = false,
                FormatChanged = true,
                SourceBytes = 8L * MiB,
                TargetBytes = 3L * MiB
            };
            var b = new TexturePlan
            {
                IsNoOp = false,
                FormatChanged = true,
                SourceBytes = 4L * MiB,
                TargetBytes = 2L * MiB
            };

            plan.Textures.Add(new PlannedTexture
            {
                TextureGuid = "tex-a",
                TextureName = "body_skin",
                TexturePath = "Assets/Tex/a.png",
                Plan = a
            });
            plan.Textures.Add(new PlannedTexture
            {
                TextureGuid = "tex-b",
                TextureName = "prop_metal",
                TexturePath = "Assets/Tex/b.png",
                Plan = b
            });

            plan.SourceBytesTotal = 12L * MiB;
            plan.TargetBytesTotal = 5L * MiB;
            plan.SavingBytes = 7L * MiB;
            plan.SavingPercent = 7L * MiB * 100.0 / (12L * MiB);
            plan.ChangedCount = 2;
            return plan;
        }
    }
}
