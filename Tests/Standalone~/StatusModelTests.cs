using AvatarTuner.Core;
using AvatarTuner.Editor.ViewModels;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// The honest status taxonomy. The Unity test showed "Optimized" next to source
    /// memory numbers after a run generated only standalone outputs - these tests pin the
    /// corrected semantics: statuses describe what really exists, and "Applied to Avatar"
    /// is never claimed because AvatarTuner never applies anything to the scene model.
    /// </summary>
    public static class StatusModelTests
    {
        [Test("nothing analyzed reads as Not Optimized")]
        public static void NothingAnalyzedIsNotOptimized()
        {
            Assert.AreEqual(AvatarDisplayStatus.NotOptimized,
                StatusModel.Derive(false, false, false, false, false, false, OptimizationStatus.NotOptimized),
                "empty state");
        }

        [Test("analysis and plan states are distinguished")]
        public static void AnalysisAndPlanStates()
        {
            Assert.AreEqual(AvatarDisplayStatus.AnalysisComplete,
                StatusModel.Derive(true, false, false, false, false, false, OptimizationStatus.NotOptimized),
                "analysis without changes reads as analysis complete");
            Assert.AreEqual(AvatarDisplayStatus.AnalysisComplete,
                StatusModel.Derive(true, false, false, false, false, false, OptimizationStatus.NotOptimized),
                "and is not 'Optimized'");
            Assert.AreEqual(AvatarDisplayStatus.ReadyToOptimize,
                StatusModel.Derive(true, true, false, false, false, false, OptimizationStatus.NotOptimized),
                "a plan with changes reads as ready to optimize");
        }

        [Test("a finished run reports generated output, with warnings when honest")]
        public static void RunStates()
        {
            Assert.AreEqual(AvatarDisplayStatus.OutputGenerated,
                StatusModel.Derive(true, true, true, true, false, true, OptimizationStatus.Optimized),
                "a clean successful run reads as optimized output generated");
            Assert.AreEqual(AvatarDisplayStatus.OutputGeneratedWithWarnings,
                StatusModel.Derive(true, true, true, true, true, true, OptimizationStatus.Optimized),
                "skipped or review-needing textures make it a warned success");
            Assert.AreEqual(AvatarDisplayStatus.OptimizationFailed,
                StatusModel.Derive(true, true, true, false, false, true, OptimizationStatus.OptimizationFailed),
                "a failed run never reads as success");
        }

        [Test("recorded outputs are tracked, not blindly celebrated")]
        public static void RecordedStates()
        {
            Assert.AreEqual(AvatarDisplayStatus.OutputGenerated,
                StatusModel.Derive(true, false, false, false, false, true, OptimizationStatus.Optimized),
                "recorded outputs with clean tracking");
            Assert.AreEqual(AvatarDisplayStatus.ChangesDetected,
                StatusModel.Derive(true, true, false, false, false, true, OptimizationStatus.ChangesDetected),
                "changed inputs read as changes detected");
            Assert.AreEqual(AvatarDisplayStatus.ReoptimizationRequired,
                StatusModel.Derive(true, true, false, false, false, true, OptimizationStatus.ReoptimizationRequired),
                "total invalidation reads as re-optimization required");
            Assert.AreEqual(AvatarDisplayStatus.OptimizationFailed,
                StatusModel.Derive(true, true, false, false, false, true, OptimizationStatus.OptimizationFailed),
                "a recorded failure stays a failure");
        }

        [Test("unsupported avatars are reported even without recorded outputs")]
        public static void UnsupportedIsReported()
        {
            Assert.AreEqual(AvatarDisplayStatus.Unsupported,
                StatusModel.Derive(true, false, false, false, false, false, OptimizationStatus.Unsupported),
                "compatibility blockers surface before anything else");
        }

        [Test("the applied status is never fabricated")]
        public static void AppliedIsNeverFabricated()
        {
            var bools = new[] { false, true };
            foreach (var hasReport in bools)
            foreach (var planHasChanges in bools)
            foreach (var ran in bools)
            foreach (var succeeded in bools)
            foreach (var warnings in bools)
            foreach (var recorded in bools)
            foreach (OptimizationStatus live in System.Enum.GetValues(typeof(OptimizationStatus)))
            {
                var status = StatusModel.Derive(
                    hasReport, planHasChanges, ran, succeeded, warnings, recorded, live);
                Assert.IsTrue(status != AvatarDisplayStatus.AppliedToAvatar,
                    "AvatarTuner v1.0.0 never applies outputs to the avatar, so the status is never shown");
            }
        }

        [Test("statuses that describe generated output say so")]
        public static void GeneratedOutputMapping()
        {
            Assert.IsTrue(StatusModel.DescribesGeneratedOutput(AvatarDisplayStatus.OutputGenerated),
                "generated output describes generated output");
            Assert.IsTrue(StatusModel.DescribesGeneratedOutput(AvatarDisplayStatus.OutputGeneratedWithWarnings),
                "warned output is still output");
            Assert.IsTrue(StatusModel.DescribesGeneratedOutput(AvatarDisplayStatus.ChangesDetected),
                "stale outputs still exist as files");
            Assert.IsFalse(StatusModel.DescribesGeneratedOutput(AvatarDisplayStatus.ReadyToOptimize),
                "a plan is not output");
            Assert.IsFalse(StatusModel.DescribesGeneratedOutput(AvatarDisplayStatus.NotOptimized),
                "nothing is not output");
        }

        [Test("every status has localized name and summary keys and a tone")]
        public static void EveryStatusIsLabelled()
        {
            foreach (AvatarDisplayStatus status in System.Enum.GetValues(typeof(AvatarDisplayStatus)))
            {
                var nameKey = StatusModel.NameKey(status);
                var summaryKey = StatusModel.SummaryKey(status);
                Assert.IsTrue(TranslationValidator.LooksLikeTranslationKey(nameKey),
                    status + " name key is a key");
                Assert.IsTrue(TranslationValidator.LooksLikeTranslationKey(summaryKey),
                    status + " summary key is a key");
                Assert.AreNotEqual(null, StatusModel.Tone(status), status + " has a tone");
            }
        }

        [Test("recorded manifest statuses map to honest display wording")]
        public static void RecordedStatusMapping()
        {
            Assert.AreEqual(AvatarDisplayStatus.OutputGenerated,
                DashboardModel.DisplayStatusOf(OptimizationStatus.Optimized),
                "a recorded run maps to output generated, not 'applied'");
            Assert.AreEqual(AvatarDisplayStatus.NotOptimized,
                DashboardModel.DisplayStatusOf(OptimizationStatus.NotOptimized),
                "nothing recorded maps to not optimized");
        }
    }
}
