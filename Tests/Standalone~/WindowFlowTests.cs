using System;
using AvatarTuner.Editor.UI;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Offline tests for the window's state machine and control gating: which button is
    /// enabled when, and which lifecycle transitions follow which operation. The rules live
    /// in WindowFlow (Unity free) so the UI cannot accidentally enable an unsafe action.
    /// </summary>
    public static class WindowFlowTests
    {
        [Test("busy states block every operation")]
        public static void BusyStatesBlockEverything()
        {
            foreach (var busy in new[] { WindowState.Analyzing, WindowState.Optimizing })
            {
                Assert.IsTrue(WindowFlow.IsBusy(busy), busy + " is busy");
                Assert.IsFalse(WindowFlow.CanAnalyze(busy), busy + " cannot start another analysis");
                Assert.IsFalse(WindowFlow.CanPreview(busy, true), busy + " cannot preview");
                Assert.IsFalse(WindowFlow.CanOptimize(busy, true, true, true), busy + " cannot optimize");
                Assert.IsFalse(WindowFlow.CanCleanup(busy, true), busy + " cannot clean up");
                Assert.IsFalse(WindowFlow.CanEditSettings(busy), busy + " cannot edit settings");
            }
        }

        [Test("idle states allow analysis and settings editing")]
        public static void IdleStatesAllowAnalysis()
        {
            foreach (var idle in new[]
                     {
                         WindowState.NoSelection, WindowState.Ready, WindowState.Analyzed,
                         WindowState.Done, WindowState.Error
                     })
            {
                Assert.IsTrue(WindowFlow.CanAnalyze(idle), idle + " can start an analysis");
                Assert.IsTrue(WindowFlow.CanEditSettings(idle), idle + " can edit settings");
                Assert.IsFalse(WindowFlow.IsBusy(idle), idle + " is not busy");
            }
        }

        [Test("optimization demands a plan with real changes and compatibility approval")]
        public static void OptimizationGatesAreComplete()
        {
            Assert.IsTrue(WindowFlow.CanOptimize(WindowState.Analyzed, true, true, true),
                "an analyzed avatar with changes and approval can be optimized");
            Assert.IsTrue(WindowFlow.CanOptimize(WindowState.Done, true, true, true),
                "a done avatar can be regenerated");
            Assert.IsTrue(WindowFlow.CanOptimize(WindowState.Error, true, true, true),
                "a failed run can be retried");

            Assert.IsFalse(WindowFlow.CanOptimize(WindowState.Ready, true, true, true),
                "no analysis means no optimization");
            Assert.IsFalse(WindowFlow.CanOptimize(WindowState.NoSelection, true, true, true),
                "no avatar means no optimization");
            Assert.IsFalse(WindowFlow.CanOptimize(WindowState.Analyzed, false, true, true),
                "no plan means no optimization");
            Assert.IsFalse(WindowFlow.CanOptimize(WindowState.Analyzed, true, false, true),
                "an already optimal plan has nothing to run");
            Assert.IsFalse(WindowFlow.CanOptimize(WindowState.Analyzed, true, true, false),
                "a compatibility blocker refuses optimization");
        }

        [Test("preview and cleanup have their own gates")]
        public static void PreviewAndCleanupGates()
        {
            Assert.IsTrue(WindowFlow.CanPreview(WindowState.Analyzed, true), "a plan can be previewed");
            Assert.IsFalse(WindowFlow.CanPreview(WindowState.Analyzed, false), "no plan, no preview");
            Assert.IsFalse(WindowFlow.CanPreview(WindowState.Ready, true), "preview needs an analysis");

            Assert.IsTrue(WindowFlow.CanCleanup(WindowState.Done, true), "recorded outputs can be cleaned");
            Assert.IsFalse(WindowFlow.CanCleanup(WindowState.Done, false), "nothing recorded, nothing to clean");
        }

        [Test("selection and result transitions are exact")]
        public static void TransitionsAreExact()
        {
            Assert.AreEqual(WindowState.NoSelection, WindowFlow.AfterDeselectingAvatar(), "deselect clears");
            Assert.AreEqual(WindowState.Ready, WindowFlow.AfterSelectingAvatar(), "select readies");
            Assert.AreEqual(WindowState.Analyzing, WindowFlow.BeginAnalysis(), "analysis starts");
            Assert.AreEqual(WindowState.Analyzed, WindowFlow.AfterAnalysis(true), "analysis succeeds");
            Assert.AreEqual(WindowState.Error, WindowFlow.AfterAnalysis(false), "analysis fails to error");
            Assert.AreEqual(WindowState.Optimizing, WindowFlow.BeginOptimization(), "optimization starts");
            Assert.AreEqual(WindowState.Done, WindowFlow.AfterOptimization(true), "optimization succeeds");
            Assert.AreEqual(WindowState.Error, WindowFlow.AfterOptimization(false), "optimization fails to error");
        }

        [Test("an error state still allows retrying everything that needs data")]
        public static void ErrorStateAllowsRetry()
        {
            Assert.IsTrue(WindowFlow.CanAnalyze(WindowState.Error), "a failed run can be re-analyzed");
            Assert.IsTrue(WindowFlow.CanOptimize(WindowState.Error, true, true, true), "and re-optimized");
            Assert.IsTrue(WindowFlow.CanCleanup(WindowState.Error, true), "and cleaned up");
        }
    }
}
