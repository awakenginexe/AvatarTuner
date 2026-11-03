using System;
using System.Collections.Generic;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;
using AvatarTuner.Editor.Tracking;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Offline tests for the generated-asset management layer: manifest state assembly and
    /// the deletion policy. The persistence and deletion code itself runs in Unity; these
    /// tests cover the rules it obeys — provenance, config hashing and the safety gate.
    /// </summary>
    public static class TrackingTests
    {
        private static readonly DateTime FixedStamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        // ------------------------------------------------------------------ state assembly

        [Test("a successful run records full provenance")]
        public static void SuccessfulRunRecordsProvenance()
        {
            var state = ManifestStateBuilder.Build(
                MakeReport(), MakeGeneration(true, false), MakeSources(), MakeInfo());

            Assert.AreEqual("TestAvatar", state.AvatarName, "the avatar name is recorded");
            Assert.AreEqual("rootguid", state.SourceRootGuid, "the root GUID is recorded");
            Assert.AreEqual("Assets/TestAvatar.prefab", state.SourceRootPath, "the root path is recorded");
            Assert.AreEqual("cfg-hash", state.ConfigHash, "the configuration hash is recorded");
            Assert.AreEqual("2022.3.22f1", state.EngineVersionAtOptimization, "the engine version is recorded");
            Assert.AreEqual("1.0.0", state.ToolVersionAtOptimization, "the tool version is recorded");
            Assert.AreEqual("balanced", state.PresetId, "the preset is recorded");
            Assert.AreEqual(OptimizationStatus.Optimized, state.Status, "a successful run is recorded as optimized");
            Assert.AreEqual(string.Empty, state.LastError, "a successful run has no error");
            Assert.AreEqual("2026-01-02T03:04:05Z", state.LastSuccessUtc, "the timestamp is ISO-8601 UTC");
            Assert.AreEqual(state.LastSuccessUtc, state.LastAttemptUtc, "success stamps both timestamps");

            Assert.AreEqual(3, state.Sources.Count, "every valid source is recorded");
            Assert.AreEqual(2, state.Generated.Count, "every generated asset is recorded");

            var generated = state.Generated[0];
            Assert.AreEqual("texture", generated.Kind, "generated textures are typed");
            Assert.AreEqual("plan-token-1", generated.PlanHash, "the producing plan is recorded");
            Assert.AreEqual("hash-1", generated.ContentHash, "the content hash is recorded");
            Assert.AreEqual(1, generated.DerivedFromGuids.Count, "provenance is recorded");
            Assert.AreEqual("tex-a", generated.DerivedFromGuids[0], "the source GUID is the provenance");
        }

        [Test("a failed run keeps its status and error")]
        public static void FailedRunKeepsStatus()
        {
            var failed = ManifestStateBuilder.Build(
                MakeReport(), MakeGeneration(false, false), null, MakeInfo());
            Assert.AreEqual(OptimizationStatus.OptimizationFailed, failed.Status, "a failed run is recorded as failed");
            Assert.AreEqual("error.optimizeFailed", failed.LastError, "the failure reason is recorded");
            Assert.AreEqual("2026-01-02T03:04:05Z", failed.LastAttemptUtc, "the attempt is timestamped");
            Assert.AreEqual(string.Empty, failed.LastSuccessUtc,
                "a failed run never claims a success timestamp");

            var cancelled = ManifestStateBuilder.Build(
                MakeReport(), MakeGeneration(false, true), null, MakeInfo());
            Assert.AreEqual("error.cancelled", cancelled.LastError, "cancellation is distinguished");
            Assert.AreEqual(string.Empty, cancelled.LastSuccessUtc, "cancellation is not a success");
        }

        [Test("generated entries without provenance are still recorded honestly")]
        public static void ProvenanceIsHonest()
        {
            Assert.IsNull(ManifestStateBuilder.FromGeneratedAsset(null), "no asset, no state");
            Assert.IsNull(ManifestStateBuilder.FromGeneratedAsset(new GeneratedTextureAsset()),
                "no path, no state");

            var noSource = new GeneratedTextureAsset { GeneratedPath = "Assets/Gen/a.png" };
            var state = ManifestStateBuilder.FromGeneratedAsset(noSource);
            Assert.AreEqual(0, state.DerivedFromGuids.Count, "no source means no provenance, not a guess");
        }

        [Test("run metrics are recorded and survive the manifest round trip")]
        public static void RunMetricsAreRecorded()
        {
            var info = MakeInfo();
            info.SourceBytesTotal = 249L * 1024L * 1024L;
            info.OutputBytesTotal = 43L * 1024L * 1024L;
            info.OptimizedCount = 34;
            info.SkippedCount = 12;
            info.ExcludedCount = 3;
            info.ReviewCount = 34;

            var state = ManifestStateBuilder.Build(
                MakeReport(), MakeGeneration(true, false), MakeSources(), info);

            Assert.AreEqual(249L * 1024L * 1024L, state.SourceBytesTotal, "source memory is recorded");
            Assert.AreEqual(43L * 1024L * 1024L, state.OutputBytesTotal, "output memory is recorded");
            Assert.AreEqual(34, state.OptimizedCount, "optimized count is recorded");
            Assert.AreEqual(12, state.SkippedCount, "skipped count is recorded");
            Assert.AreEqual(3, state.ExcludedCount, "excluded count is recorded");
            Assert.AreEqual(34, state.ReviewCount, "review count is recorded");

            var restored = AvatarOptimizationState.FromDictionary(state.ToDictionary());
            Assert.AreEqual(state.SourceBytesTotal, restored.SourceBytesTotal, "metrics survive the manifest");
            Assert.AreEqual(state.OutputBytesTotal, restored.OutputBytesTotal, "metrics survive the manifest");
            Assert.AreEqual(state.OptimizedCount, restored.OptimizedCount, "metrics survive the manifest");
            Assert.AreEqual(state.SkippedCount, restored.SkippedCount, "metrics survive the manifest");
            Assert.AreEqual(state.ExcludedCount, restored.ExcludedCount, "metrics survive the manifest");
            Assert.AreEqual(state.ReviewCount, restored.ReviewCount, "metrics survive the manifest");
        }

        [Test("invalid arguments are refused")]
        public static void InvalidArgumentsAreRefused()
        {
            Assert.Throws<ArgumentNullException>(
                () => ManifestStateBuilder.Build(null, MakeGeneration(true, false), null, MakeInfo()),
                "a missing report is refused");
            Assert.Throws<ArgumentNullException>(
                () => ManifestStateBuilder.Build(MakeReport(), null, null, MakeInfo()),
                "a missing generation result is refused");
            Assert.Throws<ArgumentNullException>(
                () => ManifestStateBuilder.Build(MakeReport(), MakeGeneration(true, false), null, null),
                "a missing run info is refused");
        }

        // ------------------------------------------------------------------ config hash

        [Test("config hash covers preset and overrides")]
        public static void ConfigHashCoversConfiguration()
        {
            var quality = PresetCatalog.GetPolicy(OptimizationPreset.QualityFirst);
            var balanced = PresetCatalog.GetPolicy(OptimizationPreset.Balanced);

            var a = ManifestStateBuilder.ConfigHash(quality, null);
            var b = ManifestStateBuilder.ConfigHash(quality, null);
            Assert.AreEqual(a, b, "the hash is deterministic");
            Assert.AreNotEqual(a, ManifestStateBuilder.ConfigHash(balanced, null),
                "changing the preset changes the hash");

            var overrides = new Dictionary<string, TextureOverride>(StringComparer.OrdinalIgnoreCase);
            overrides["tex-a"] = new TextureOverride { ForceMaxSize = true, ForcedMaxSize = 512 };
            var withOverride = ManifestStateBuilder.ConfigHash(quality, overrides);
            Assert.AreNotEqual(a, withOverride, "adding an override changes the hash");

            overrides["tex-a"].ForcedMaxSize = 1024;
            Assert.AreNotEqual(withOverride, ManifestStateBuilder.ConfigHash(quality, overrides),
                "editing the override changes the hash");

            var swapped = new Dictionary<string, TextureOverride>(StringComparer.OrdinalIgnoreCase);
            swapped["tex-a"] = new TextureOverride { ForceMaxSize = true, ForcedMaxSize = 512 };
            var reversed = new Dictionary<string, TextureOverride>(StringComparer.OrdinalIgnoreCase);
            reversed["tex-b"] = new TextureOverride { ForceMaxSize = true, ForcedMaxSize = 512 };
            Assert.AreNotEqual(
                ManifestStateBuilder.ConfigHash(quality, swapped),
                ManifestStateBuilder.ConfigHash(quality, reversed),
                "which texture an override targets is part of the configuration");

            var two = new Dictionary<string, TextureOverride>(StringComparer.OrdinalIgnoreCase);
            two["tex-a"] = new TextureOverride { ForceMaxSize = true, ForcedMaxSize = 512 };
            two["tex-b"] = new TextureOverride { KeepResolution = true };
            var twoReversed = new Dictionary<string, TextureOverride>(StringComparer.OrdinalIgnoreCase);
            twoReversed["tex-b"] = new TextureOverride { KeepResolution = true };
            twoReversed["tex-a"] = new TextureOverride { ForceMaxSize = true, ForcedMaxSize = 512 };
            Assert.AreEqual(ManifestStateBuilder.ConfigHash(quality, two),
                ManifestStateBuilder.ConfigHash(quality, twoReversed),
                "override order does not matter");

            var inert = new Dictionary<string, TextureOverride>(StringComparer.OrdinalIgnoreCase);
            inert["tex-a"] = new TextureOverride();
            Assert.AreEqual(a, ManifestStateBuilder.ConfigHash(quality, inert),
                "an override with no effect is not part of the configuration");
        }

        [Test("config hash refuses a missing policy")]
        public static void ConfigHashRefusesNullPolicy()
        {
            Assert.Throws<ArgumentNullException>(
                () => ManifestStateBuilder.ConfigHash(null, null),
                "a missing policy is refused");
        }

        // ------------------------------------------------------------------ cleanup policy

        [Test("the deletion safety gate is airtight")]
        public static void DeletionSafetyGate()
        {
            const string root = "Assets/AvatarTuner-Generated";
            const string manifest = "Assets/AvatarTuner-Generated/avatartuner-manifest.json";

            Assert.IsTrue(CleanupPlanner.IsUnderRoot("Assets/AvatarTuner-Generated/A/x.png", root),
                "files under the root are under the root");
            Assert.IsFalse(CleanupPlanner.IsUnderRoot("Assets/AvatarTuner-Generated-Evil/x.png", root),
                "a folder sharing the prefix is not under the root");
            Assert.IsFalse(CleanupPlanner.IsUnderRoot(root, root), "the root is not strictly under itself");
            Assert.IsTrue(CleanupPlanner.IsUnderRoot("assets\\avatartuner-generated\\a\\x.png", root),
                "separators and case are normalized");
            Assert.IsFalse(CleanupPlanner.IsUnderRoot("", root), "an empty path is under nothing");
            Assert.IsFalse(CleanupPlanner.IsUnderRoot("Assets/Gen/x.png", null), "nothing is under no root");

            Assert.IsTrue(CleanupPlanner.IsSafeToDelete("Assets/AvatarTuner-Generated/A/x.png", root, manifest),
                "recorded output paths inside the root may be deleted");
            Assert.IsFalse(CleanupPlanner.IsSafeToDelete(manifest, root, manifest),
                "the manifest itself is never deletable");
            Assert.IsFalse(CleanupPlanner.IsSafeToDelete("Assets/Shared/x.png", root, manifest),
                "anything outside the root is never deletable");
            Assert.IsFalse(CleanupPlanner.IsSafeToDelete(null, root, manifest), "null is never deletable");
            Assert.IsFalse(CleanupPlanner.IsSafeToDelete("Assets/Gen/x.png", "", manifest),
                "without a root nothing is deletable");
        }

        [Test("avatar outputs are exactly the recorded paths")]
        public static void AvatarOutputsMatchRecord()
        {
            var state = new AvatarOptimizationState();
            state.Generated.Add(new GeneratedAssetState { Path = "Assets/Gen/a.png" });
            state.Generated.Add(new GeneratedAssetState { Path = "Assets/Gen/b.png" });
            state.Generated.Add(new GeneratedAssetState { Path = string.Empty });

            var outputs = CleanupPlanner.AvatarOutputs(state);
            Assert.AreEqual(2, outputs.Count, "empty paths are not outputs");
            Assert.AreEqual(0, CleanupPlanner.AvatarOutputs(null).Count, "no state means no outputs");
        }

        [Test("regeneration reports exactly the outputs it will remove")]
        public static void RemovedOutputsAreExact()
        {
            var previous = new AvatarOptimizationState();
            previous.Generated.Add(new GeneratedAssetState { Path = "Assets/Gen/a.png" });
            previous.Generated.Add(new GeneratedAssetState { Path = "Assets/Gen/b.png" });

            var next = new AvatarOptimizationState();
            next.Generated.Add(new GeneratedAssetState { Path = "Assets/Gen/b.png" });
            next.Generated.Add(new GeneratedAssetState { Path = "Assets/Gen/c.png" });

            var removed = CleanupPlanner.RemovedOutputs(previous, next);
            Assert.AreEqual(1, removed.Count, "only the vanished output is removed");
            Assert.AreEqual("Assets/Gen/a.png", removed[0], "the right output is named");

            Assert.AreEqual(0, CleanupPlanner.RemovedOutputs(previous, previous).Count,
                "an identical run removes nothing");
            Assert.AreEqual(0, CleanupPlanner.RemovedOutputs(null, next).Count,
                "no previous run removes nothing");
        }

        [Test("orphan detection ignores recorded files and the manifest")]
        public static void OrphanDetectionIsExact()
        {
            var manifest = new Manifest();
            var state = new AvatarOptimizationState { SourceRootGuid = "root" };
            state.Generated.Add(new GeneratedAssetState { Path = "Assets/AvatarTuner-Generated/A/a.png" });
            manifest.Upsert(state);

            var existing = new[]
            {
                "Assets/AvatarTuner-Generated/A/a.png",
                "Assets/AvatarTuner-Generated/avatartuner-manifest.json",
                "Assets/AvatarTuner-Generated/A/leftover.png"
            };

            var orphans = CleanupPlanner.Orphans(existing, manifest,
                "Assets/AvatarTuner-Generated/avatartuner-manifest.json");
            Assert.AreEqual(1, orphans.Count, "only the unrecorded file is an orphan");
            Assert.AreEqual("Assets/AvatarTuner-Generated/A/leftover.png", orphans[0], "the right orphan");

            Assert.AreEqual(0, CleanupPlanner.Orphans(null, manifest, "m").Count, "no files, no orphans");
        }

        [Test("common folder is the deepest shared folder")]
        public static void CommonFolderIsDeepestShared()
        {
            Assert.AreEqual("Assets/G/A/Textures",
                CleanupPlanner.CommonFolder(new List<string>
                {
                    "Assets/G/A/Textures/x.png",
                    "Assets/G/A/Textures/y.png"
                }),
                "sibling outputs share their folder");

            Assert.AreEqual("Assets/G/A",
                CleanupPlanner.CommonFolder(new List<string>
                {
                    "Assets/G/A/Textures/x.png",
                    "Assets/G/A/Materials/y.png"
                }),
                "different output kinds widen to the avatar folder");

            Assert.AreEqual("Assets/G/A/Textures",
                CleanupPlanner.CommonFolder(new List<string> { "Assets/G/A/Textures/x.png" }),
                "a single file shares its parent folder");

            Assert.AreEqual(string.Empty, CleanupPlanner.CommonFolder(null), "no paths, no folder");
            Assert.AreEqual(string.Empty,
                CleanupPlanner.CommonFolder(new List<string> { string.Empty }), "empty paths give no folder");
        }

        // ------------------------------------------------------------------ helpers

        private static OptimizationRunInfo MakeInfo()
        {
            return new OptimizationRunInfo
            {
                ConfigHash = "cfg-hash",
                EngineVersion = "2022.3.22f1",
                ToolVersion = "1.0.0",
                PresetId = "balanced",
                UtcNow = FixedStamp
            };
        }

        private static AvatarAnalysisReport MakeReport()
        {
            var report = new AvatarAnalysisReport();
            report.AvatarName = "TestAvatar";
            report.AvatarRootGuid = "rootguid";
            report.AvatarRootPath = "Assets/TestAvatar.prefab";
            return report;
        }

        private static TextureGenerationResult MakeGeneration(bool succeeded, bool cancelled)
        {
            var result = new TextureGenerationResult();
            result.Succeeded = succeeded;
            result.Cancelled = cancelled;

            var first = new GeneratedTextureAsset
            {
                SourceGuid = "tex-a",
                SourcePath = "Assets/Tex/a.png",
                GeneratedGuid = "gen-a",
                GeneratedPath = "Assets/AvatarTuner-Generated/TestAvatar-rootgui/Textures/a_gen.png",
                PlanHashToken = "plan-token-1",
                ContentHash = "hash-1"
            };
            var second = new GeneratedTextureAsset
            {
                SourceGuid = "tex-b",
                SourcePath = "Assets/Tex/b.png",
                GeneratedGuid = "gen-b",
                GeneratedPath = "Assets/AvatarTuner-Generated/TestAvatar-rootgui/Textures/b_gen.png",
                PlanHashToken = "plan-token-2",
                ContentHash = "hash-2"
            };

            result.Generated.Add(first);
            result.Generated.Add(second);
            return result;
        }

        private static List<SourceAssetState> MakeSources()
        {
            var sources = new List<SourceAssetState>();
            sources.Add(new SourceAssetState("tex-a", "Assets/Tex/a.png", "texture"));
            sources.Add(null);
            sources.Add(new SourceAssetState("", "Assets/Tex/invalid.png", "texture"));
            sources.Add(new SourceAssetState("mat-1", "Assets/Mat/1.mat", "material"));
            sources.Add(new SourceAssetState("mesh-1", "Assets/Mesh/1.fbx", "mesh"));
            return sources;
        }
    }
}
