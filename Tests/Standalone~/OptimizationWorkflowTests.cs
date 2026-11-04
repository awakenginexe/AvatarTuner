using System;
using System.Collections.Generic;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;
using AvatarTuner.Editor.Tracking;
using AvatarTuner.Editor.ViewModels;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// The optimization workflow rules behind the configure dialog: preset selection and
    /// persistence, plan freshness (a stale plan is never executed), and the confirm gate
    /// that makes Optimize a review-and-confirm step instead of a one-click mutation.
    /// </summary>
    public static class OptimizationWorkflowTests
    {
        [Test("the chosen preset round-trips through persistence")]
        public static void PresetRoundTripsThroughPersistence()
        {
            foreach (OptimizationPreset preset in Enum.GetValues(typeof(OptimizationPreset)))
            {
                var store = new Dictionary<string, string>(StringComparer.Ordinal);
                PresetMemory.Save((key, value) => store[key] = value, preset);
                Assert.AreEqual(PresetCatalog.GetId(preset), store[PresetMemory.SessionKey],
                    preset + " is stored as its stable id");

                var loaded = PresetMemory.Load((key, fallback) =>
                    store.ContainsKey(key) ? store[key] : fallback);
                Assert.AreEqual(preset, loaded, preset + " survives the round trip");
            }
        }

        [Test("invalid or missing preset data falls back to the recommended preset")]
        public static void PresetFallsBackSafely()
        {
            var store = new Dictionary<string, string>(StringComparer.Ordinal);
            Assert.AreEqual(PresetMemory.Fallback,
                PresetMemory.Load((key, fallback) => store.ContainsKey(key) ? store[key] : fallback),
                "a fresh editor session starts on the recommended preset");

            store[PresetMemory.SessionKey] = "nonsense-preset";
            Assert.AreEqual(PresetMemory.Fallback,
                PresetMemory.Load((key, fallback) => store.ContainsKey(key) ? store[key] : fallback),
                "corrupted data falls back instead of crashing");

            Assert.AreEqual(PresetMemory.Fallback, PresetMemory.Load(null), "a missing store is safe");
            PresetMemory.Save(null, OptimizationPreset.QualityFirst);

            foreach (OptimizationPreset preset in Enum.GetValues(typeof(OptimizationPreset)))
            {
                Assert.AreEqual(preset,
                    PresetMemory.ParsePreset(PresetCatalog.GetId(preset).ToUpperInvariant(), PresetMemory.Fallback),
                    preset + " parses regardless of case");
            }
        }

        [Test("a plan built with an older configuration is stale and unconfirmable")]
        public static void StalePlansAreRefused()
        {
            Assert.IsFalse(OptimizationDialogModel.IsPlanFresh(null, "abc"), "no plan is not fresh");
            Assert.IsFalse(OptimizationDialogModel.IsPlanFresh("", "abc"), "an empty token is not fresh");
            Assert.IsFalse(OptimizationDialogModel.IsPlanFresh("old", "new"), "a config change stales the plan");
            Assert.IsTrue(OptimizationDialogModel.IsPlanFresh("abc", "abc"), "the current plan is fresh");

        }

        [Test("confirming starts from analysis without requiring a separate evaluation")]
        public static void ConfirmGateExplainsItself()
        {
            Assert.AreEqual("ui.hint.busy",
                OptimizationDialogModel.ConfirmDisabledReasonKey(true, true, true),
                "busy wins");
            Assert.AreEqual("ui.hint.analyzeFirst",
                OptimizationDialogModel.ConfirmDisabledReasonKey(false, false, true),
                "no plan is next");
            Assert.AreEqual("ui.hint.blocked",
                OptimizationDialogModel.ConfirmDisabledReasonKey(false, true, false),
                "compatibility blockers are named");
            Assert.IsNull(
                OptimizationDialogModel.ConfirmDisabledReasonKey(false, true, true),
                "analyzed avatars can confirm before candidate evaluation");
        }

        [Test("changing preset or overrides regenerates the plan token")]
        public static void ConfigChangesInvalidateThePlan()
        {
            var report = new AvatarAnalysisReport();
            report.AvatarName = "TestAvatar";
            report.ScanSucceeded = true;

            var baseline = TexturePlanBuilder.Build(
                report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, null);
            var sameConfig = TexturePlanBuilder.Build(
                report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, null);
            Assert.AreEqual(baseline.ConfigHashToken, sameConfig.ConfigHashToken,
                "the same configuration rebuilds the same plan token");

            var otherPreset = TexturePlanBuilder.Build(
                report, PresetCatalog.GetPolicy(OptimizationPreset.SizeFirst), null, null);
            Assert.AreNotEqual(baseline.ConfigHashToken, otherPreset.ConfigHashToken,
                "changing the preset regenerates the plan token");
            Assert.IsFalse(OptimizationDialogModel.IsPlanFresh(baseline.ConfigHashToken, otherPreset.ConfigHashToken),
                "so the old plan is stale and cannot be confirmed");

            var overrides = new Dictionary<string, TextureOverride>(StringComparer.OrdinalIgnoreCase)
            {
                { "tex-a", new TextureOverride { Excluded = true } }
            };
            var withOverride = TexturePlanBuilder.Build(
                report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), overrides, null);
            Assert.AreNotEqual(baseline.ConfigHashToken, withOverride.ConfigHashToken,
                "changing a per-texture override regenerates the plan token too");
            Assert.IsFalse(
                OptimizationDialogModel.IsPlanFresh(baseline.ConfigHashToken, withOverride.ConfigHashToken),
                "so the old plan is stale and cannot be confirmed");
        }

        [Test("the stale plan gate mirrors the recorded config hash exactly")]
        public static void PlanTokenMatchesTheRecordedConfigHash()
        {
            var report = new AvatarAnalysisReport();
            report.ScanSucceeded = true;

            var overrides = new Dictionary<string, TextureOverride>(StringComparer.OrdinalIgnoreCase);
            var plan = TexturePlanBuilder.Build(
                report, PresetCatalog.GetPolicy(OptimizationPreset.QualityFirst), overrides, null);

            Assert.AreEqual(
                ManifestStateBuilder.ConfigHash(PresetCatalog.GetPolicy(OptimizationPreset.QualityFirst), overrides),
                plan.ConfigHashToken,
                "the plan token is the same config hash the manifest records");
        }
    }
}
