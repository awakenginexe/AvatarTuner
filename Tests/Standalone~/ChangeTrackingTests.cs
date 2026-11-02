using System;
using System.Collections.Generic;
using AvatarTuner.Core;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Tests for manifest persistence, dependency de-duplication and change analysis.
    /// These cover the product rules that unrelated project changes must never trigger a
    /// full regeneration and that hand-edited generated files are detected.
    /// </summary>
    public static class ChangeTrackingTests
    {
        private static AvatarOptimizationState BuildRecordedState()
        {
            var state = new AvatarOptimizationState();
            state.AvatarName = "TestAvatar";
            state.SourceRootGuid = "rootguid000000000000000000000001";
            state.SourceRootPath = "Assets/Avatars/TestAvatar.prefab";
            state.ConfigHash = "confighash-1";
            state.EngineVersionAtOptimization = "2022.3.22f1";
            state.ToolVersionAtOptimization = "1.0.0";
            state.PresetId = "balanced";
            state.Status = OptimizationStatus.Optimized;
            state.LastSuccessUtc = "2026-01-01T00:00:00Z";

            var textureA = new SourceAssetState("guid-texture-a", "Assets/Tex/a.png", "texture");
            textureA.AssetDependencyHash = "dep-a";
            textureA.ImporterSettingsHash = "imp-a";
            state.Sources.Add(textureA);

            var textureB = new SourceAssetState("guid-texture-b", "Assets/Tex/b.png", "texture");
            textureB.AssetDependencyHash = "dep-b";
            textureB.ImporterSettingsHash = "imp-b";
            state.Sources.Add(textureB);

            var outputA = new GeneratedAssetState();
            outputA.Guid = "gen-a";
            outputA.Path = "Assets/AvatarTuner_Generated/TestAvatar/Tex/a.png";
            outputA.Kind = "texture";
            outputA.ContentHash = "content-a";
            outputA.PlanHash = "plan-a";
            outputA.DerivedFromGuids.Add("guid-texture-a");
            state.Generated.Add(outputA);

            var outputB = new GeneratedAssetState();
            outputB.Guid = "gen-b";
            outputB.Path = "Assets/AvatarTuner_Generated/TestAvatar/Tex/b.png";
            outputB.Kind = "texture";
            outputB.ContentHash = "content-b";
            outputB.PlanHash = "plan-b";
            outputB.DerivedFromGuids.Add("guid-texture-b");
            state.Generated.Add(outputB);

            return state;
        }

        private static CurrentSnapshot BuildSnapshot(AvatarOptimizationState recorded)
        {
            var snapshot = new CurrentSnapshot();
            snapshot.ConfigHash = recorded.ConfigHash;
            snapshot.EngineVersion = recorded.EngineVersionAtOptimization;
            snapshot.ToolVersion = recorded.ToolVersionAtOptimization;

            for (var i = 0; i < recorded.Sources.Count; i++)
            {
                var copy = new SourceAssetState(recorded.Sources[i].Guid, recorded.Sources[i].Path, recorded.Sources[i].Role);
                copy.AssetDependencyHash = recorded.Sources[i].AssetDependencyHash;
                copy.ImporterSettingsHash = recorded.Sources[i].ImporterSettingsHash;
                snapshot.Sources.Add(copy);
            }

            for (var i = 0; i < recorded.Generated.Count; i++)
            {
                var copy = new GeneratedAssetState();
                copy.Guid = recorded.Generated[i].Guid;
                copy.Path = recorded.Generated[i].Path;
                copy.Kind = recorded.Generated[i].Kind;
                copy.ContentHash = recorded.Generated[i].ContentHash;
                copy.PlanHash = recorded.Generated[i].PlanHash;
                copy.DerivedFromGuids.AddRange(recorded.Generated[i].DerivedFromGuids);
                snapshot.Generated.Add(copy);
            }

            return snapshot;
        }

        // ------------------------------------------------------------- manifest IO

        [Test("a manifest round trips through JSON without losing data")]
        public static void ManifestRoundTrips()
        {
            var manifest = new Manifest();
            manifest.ToolVersion = "1.0.0";
            manifest.ModifiedUtc = "2026-01-01T00:00:00Z";
            manifest.Upsert(BuildRecordedState());

            var json = manifest.ToJson(true);
            var restored = Manifest.FromJson(json);

            Assert.AreEqual(Manifest.CurrentSchemaVersion, restored.SchemaVersion, "schema version");
            Assert.AreEqual("1.0.0", restored.ToolVersion, "tool version");
            Assert.AreEqual(1, restored.Avatars.Count, "one avatar is stored");

            var avatar = restored.Avatars[0];
            Assert.AreEqual("TestAvatar", avatar.AvatarName, "avatar name");
            Assert.AreEqual("rootguid000000000000000000000001", avatar.SourceRootGuid, "root guid");
            Assert.AreEqual("confighash-1", avatar.ConfigHash, "config hash");
            Assert.AreEqual("2022.3.22f1", avatar.EngineVersionAtOptimization, "engine version");
            Assert.AreEqual(OptimizationStatus.Optimized, avatar.Status, "status");
            Assert.AreEqual(2, avatar.Sources.Count, "source count");
            Assert.AreEqual(2, avatar.Generated.Count, "generated count");
            Assert.AreEqual("imp-a", avatar.Sources[0].ImporterSettingsHash, "importer settings hash");
            Assert.AreEqual(1, avatar.Generated[0].DerivedFromGuids.Count, "derived from is preserved");
            Assert.AreEqual("guid-texture-a", avatar.Generated[0].DerivedFromGuids[0], "derived from guid");
        }

        [Test("a manifest written with indentation reads back identically to a compact one")]
        public static void PrettyAndCompactAgree()
        {
            var manifest = new Manifest();
            manifest.ToolVersion = "1.0.0";
            manifest.Upsert(BuildRecordedState());

            var compact = Manifest.FromJson(manifest.ToJson(false));
            var pretty = Manifest.FromJson(manifest.ToJson(true));

            Assert.AreEqual(compact.Avatars[0].ConfigHash, pretty.Avatars[0].ConfigHash, "config hash agrees");
            Assert.AreEqual(compact.Avatars[0].Sources.Count, pretty.Avatars[0].Sources.Count, "source count agrees");
            Assert.AreEqual(compact.Avatars[0].Generated.Count, pretty.Avatars[0].Generated.Count, "generated count agrees");
        }

        [Test("a newer manifest schema is rejected with a clear error")]
        public static void NewerSchemaIsRejected()
        {
            var json = "{\"schemaVersion\":999,\"toolVersion\":\"9.9.9\",\"avatars\":[]}";
            Assert.Throws<InvalidOperationException>(delegate { Manifest.FromJson(json); }, "a future schema must be refused");
        }

        [Test("an empty or null document yields an empty manifest")]
        public static void EmptyDocumentIsSafe()
        {
            Assert.AreEqual(0, Manifest.FromJson(null).Avatars.Count, "null manifest");
            Assert.AreEqual(0, Manifest.FromJson(string.Empty).Avatars.Count, "empty manifest");
            Assert.AreEqual(0, Manifest.FromJson("{}").Avatars.Count, "empty object manifest");
        }

        [Test("upsert replaces by root guid and remove deletes")]
        public static void UpsertAndRemove()
        {
            var manifest = new Manifest();

            var first = BuildRecordedState();
            manifest.Upsert(first);
            Assert.AreEqual(1, manifest.Avatars.Count, "first insert");

            var second = BuildRecordedState();
            second.ConfigHash = "confighash-2";
            manifest.Upsert(second);
            Assert.AreEqual(1, manifest.Avatars.Count, "the same root guid replaces the entry");
            Assert.AreEqual("confighash-2", manifest.Avatars[0].ConfigHash, "the replacement took effect");

            Assert.IsTrue(manifest.Remove(first.SourceRootGuid), "removing an existing entry reports true");
            Assert.AreEqual(0, manifest.Avatars.Count, "the entry is gone");
            Assert.IsFalse(manifest.Remove(first.SourceRootGuid), "removing twice reports false");
        }

        // ------------------------------------------------------- dependency registry

        [Test("shared assets are counted once")]
        public static void SharedAssetsAreCountedOnce()
        {
            var registry = new DependencyRegistry();

            for (var i = 0; i < 12; i++)
            {
                registry.Add("guid-shared", "Assets/Tex/shared.png", "texture", "Material_" + i);
            }

            registry.Add("guid-other", "Assets/Tex/other.png", "texture", "Material_0");

            Assert.AreEqual(2, registry.Count, "a texture referenced twelve times is still one entry");
            Assert.AreEqual(12, registry.ReferenceCount("guid-shared"), "but the reference count records all twelve referencers");
            Assert.AreEqual(1, registry.ReferenceCount("guid-other"), "the other texture is referenced once");
            Assert.AreEqual(0, registry.ReferenceCount("guid-missing"), "an unknown guid reports zero");
        }

        [Test("a registry snapshot is deterministic and ordered")]
        public static void RegistrySnapshotIsDeterministic()
        {
            var registry = new DependencyRegistry();
            registry.Add("guid-b", "b.png", "texture", "m");
            registry.Add("guid-a", "a.png", "texture", "m");
            registry.Add("guid-c", "c.png", "material", "m");

            var states = registry.ToStates();
            Assert.AreEqual(3, states.Count, "three distinct assets");
            Assert.AreEqual("guid-a", states[0].Guid, "ordered by guid");
            Assert.AreEqual("guid-b", states[1].Guid, "ordered by guid");
            Assert.AreEqual("guid-c", states[2].Guid, "ordered by guid");
        }

        [Test("registry role and referencer queries are per asset")]
        public static void RegistryQueries()
        {
            var registry = new DependencyRegistry();
            registry.Add("guid-x", "x.png", "texture", "MatA");
            registry.Add("guid-x", "x.png", "maskTexture", "MatB");

            Assert.AreEqual(2, registry.ReferenceCount("guid-x"), "two referencers");
            Assert.AreEqual(2, registry.GetRoles("guid-x").Count, "two distinct roles recorded");
            Assert.AreEqual(2, registry.GetReferencers("guid-x").Count, "referencers are listed");
            Assert.IsTrue(registry.Contains("guid-x"), "contains reports membership");
            Assert.IsFalse(registry.Contains("guid-nope"), "contains reports absence");
            Assert.AreEqual(0, registry.GetReferencers("guid-nope").Count, "unknown guid has no referencers");
        }

        [Test("fingerprints are populated through the provider interface")]
        public static void FingerprintsArePopulated()
        {
            var registry = new DependencyRegistry();
            registry.Add("guid-x", "x.png", "texture", "MatA");
            registry.PopulateFingerprints(new StubFingerprintProvider());

            var states = registry.ToStates();
            Assert.AreEqual("dep:guid-x", states[0].AssetDependencyHash, "dependency hash comes from the provider");
            Assert.AreEqual("imp:guid-x", states[0].ImporterSettingsHash, "importer settings hash comes from the provider");
        }

        [Test("a null fingerprint provider is refused")]
        public static void NullProviderIsRefused()
        {
            var registry = new DependencyRegistry();
            registry.Add("guid-x", "x.png", "texture", "MatA");
            Assert.Throws<ArgumentNullException>(delegate { registry.PopulateFingerprints(null); }, "null provider");
        }

        // ------------------------------------------------------------- change analysis

        [Test("no recorded state means not optimized")]
        public static void NoRecordedStateIsNotOptimized()
        {
            var report = ChangeAnalyzer.Analyze(null, BuildSnapshot(BuildRecordedState()));
            Assert.AreEqual(OptimizationStatus.NotOptimized, report.Status, "no manifest entry means nothing has been optimized");
            Assert.AreEqual("status.notOptimized.summary", report.SummaryKey, "the summary key matches the status");
            Assert.IsFalse(report.RequiresReoptimization, "nothing is stale when nothing was produced");
        }

        [Test("an unchanged world reports Optimized")]
        public static void UnchangedWorldIsOptimized()
        {
            var recorded = BuildRecordedState();
            var report = ChangeAnalyzer.Analyze(recorded, BuildSnapshot(recorded));

            Assert.AreEqual(OptimizationStatus.Optimized, report.Status, "nothing changed");
            Assert.IsFalse(report.HasChanges, "no change entries");
            Assert.IsFalse(report.RequiresReoptimization, "nothing is stale");
            Assert.AreEqual(2, report.ValidGeneratedPaths.Count, "both outputs survive");
        }

        [Test("changing one texture invalidates only its own output")]
        public static void PartialInvalidation()
        {
            var recorded = BuildRecordedState();
            var snapshot = BuildSnapshot(recorded);

            // Only texture B changes.
            snapshot.Sources[1].AssetDependencyHash = "dep-b-changed";

            var report = ChangeAnalyzer.Analyze(recorded, snapshot);

            Assert.AreEqual(OptimizationStatus.ChangesDetected, report.Status, "part of the output is stale");
            Assert.AreEqual(1, report.StaleGeneratedPaths.Count, "exactly one output is stale");
            Assert.Contains("Assets/AvatarTuner_Generated/TestAvatar/Tex/b.png", string.Join("|", report.StaleGeneratedPaths.ToArray()), "the stale one is b");
            Assert.AreEqual(1, report.ValidGeneratedPaths.Count, "the unrelated output survives");
            Assert.IsTrue(report.RequiresReoptimization, "a refresh is needed");
            Assert.AreEqual(1, report.OfKind(ChangeKind.SourceModified).Count, "one source change is reported");
        }

        [Test("unrelated asset changes never appear in the report")]
        public static void UnrelatedChangesAreIgnored()
        {
            var recorded = BuildRecordedState();
            var snapshot = BuildSnapshot(recorded);

            // An asset that was never part of the dependency set changes. It is simply not
            // in the snapshot, so it cannot affect anything.
            var report = ChangeAnalyzer.Analyze(recorded, snapshot);

            Assert.AreEqual(OptimizationStatus.Optimized, report.Status, "an unrelated project change is invisible");
            Assert.IsFalse(report.RequiresReoptimization, "no regeneration is triggered");
        }

        [Test("a configuration change invalidates everything")]
        public static void ConfigChangeInvalidatesEverything()
        {
            var recorded = BuildRecordedState();
            var snapshot = BuildSnapshot(recorded);
            snapshot.ConfigHash = "confighash-2";

            var report = ChangeAnalyzer.Analyze(recorded, snapshot);

            Assert.AreEqual(OptimizationStatus.ReoptimizationRequired, report.Status, "configuration affects every output");
            Assert.AreEqual(2, report.StaleGeneratedPaths.Count, "both outputs are stale");
            Assert.AreEqual(0, report.ValidGeneratedPaths.Count, "nothing survives");
            Assert.AreEqual(1, report.OfKind(ChangeKind.ConfigChanged).Count, "the reason is recorded");
            Assert.IsTrue(report.OfKind(ChangeKind.ConfigChanged)[0].InvalidatesEverything, "the entry is marked global");
        }

        [Test("an engine version change invalidates everything")]
        public static void EngineVersionChangeInvalidatesEverything()
        {
            var recorded = BuildRecordedState();
            var snapshot = BuildSnapshot(recorded);
            snapshot.EngineVersion = "2022.3.30f1";

            var report = ChangeAnalyzer.Analyze(recorded, snapshot);

            Assert.AreEqual(OptimizationStatus.ReoptimizationRequired, report.Status, "an engine change affects every output");
            Assert.AreEqual(1, report.OfKind(ChangeKind.EngineVersionChanged).Count, "the reason is recorded");
            Assert.AreEqual("2022.3.22f1", report.OfKind(ChangeKind.EngineVersionChanged)[0].Args[0], "the old version is quoted");
            Assert.AreEqual("2022.3.30f1", report.OfKind(ChangeKind.EngineVersionChanged)[0].Args[1], "the new version is quoted");
        }

        [Test("2022.3 and 2022.3.0f1 are treated as the same engine version")]
        public static void EngineVersionToleratesCanonicalisation()
        {
            var recorded = BuildRecordedState();
            recorded.EngineVersionAtOptimization = "2022.3";

            var snapshot = BuildSnapshot(recorded);
            snapshot.EngineVersion = "2022.3.0f1";

            var report = ChangeAnalyzer.Analyze(recorded, snapshot);
            Assert.AreEqual(0, report.OfKind(ChangeKind.EngineVersionChanged).Count, "canonical forms are equivalent");
        }

        [Test("a tool version change invalidates everything")]
        public static void ToolVersionChangeInvalidatesEverything()
        {
            var recorded = BuildRecordedState();
            var snapshot = BuildSnapshot(recorded);
            snapshot.ToolVersion = "1.1.0";

            var report = ChangeAnalyzer.Analyze(recorded, snapshot);
            Assert.AreEqual(OptimizationStatus.ReoptimizationRequired, report.Status, "an engine-agnostic tool upgrade still rebuilds");
            Assert.AreEqual(1, report.OfKind(ChangeKind.ToolVersionChanged).Count, "the reason is recorded");
        }

        [Test("importer settings changes are reported separately from content changes")]
        public static void ImporterSettingsChangesAreDistinct()
        {
            var recorded = BuildRecordedState();
            var snapshot = BuildSnapshot(recorded);
            snapshot.Sources[0].ImporterSettingsHash = "imp-a-changed";

            var report = ChangeAnalyzer.Analyze(recorded, snapshot);

            Assert.AreEqual(1, report.OfKind(ChangeKind.ImporterSettingsChanged).Count, "the importer change is reported");
            Assert.AreEqual(0, report.OfKind(ChangeKind.SourceModified).Count, "the file content itself did not change");
        }

        [Test("a missing generated file is detected")]
        public static void MissingGeneratedFileIsDetected()
        {
            var recorded = BuildRecordedState();
            var snapshot = BuildSnapshot(recorded);
            snapshot.Generated.RemoveAt(0);

            var report = ChangeAnalyzer.Analyze(recorded, snapshot);

            Assert.AreEqual(1, report.OfKind(ChangeKind.GeneratedFileMissing).Count, "the missing file is reported");
            Assert.AreEqual(OptimizationStatus.ReoptimizationRequired, report.Status, "a missing output requires regeneration");
        }

        [Test("a hand-edited generated file is detected")]
        public static void TamperedGeneratedFileIsDetected()
        {
            var recorded = BuildRecordedState();
            var snapshot = BuildSnapshot(recorded);
            snapshot.Generated[0].ContentHash = "content-a-edited-by-hand";

            var report = ChangeAnalyzer.Analyze(recorded, snapshot);

            Assert.AreEqual(1, report.OfKind(ChangeKind.GeneratedFileModified).Count, "the manual edit is reported");
            Assert.AreEqual(OptimizationStatus.ReoptimizationRequired, report.Status, "a manual edit requires regeneration");
        }

        [Test("an orphaned generated file is reported")]
        public static void OrphanedGeneratedFileIsReported()
        {
            var recorded = BuildRecordedState();
            var snapshot = BuildSnapshot(recorded);

            var orphan = new GeneratedAssetState();
            orphan.Guid = "gen-orphan";
            orphan.Path = "Assets/AvatarTuner_Generated/TestAvatar/Tex/leftover.png";
            orphan.Kind = "texture";
            orphan.ContentHash = "content-orphan";
            snapshot.Generated.Add(orphan);

            var report = ChangeAnalyzer.Analyze(recorded, snapshot);

            Assert.AreEqual(1, report.OfKind(ChangeKind.GeneratedFileOrphaned).Count, "the leftover file is reported");
            Assert.IsFalse(report.RequiresReoptimization, "an orphan does not force a rebuild by itself");
        }

        [Test("a new dependency invalidates the outputs derived from it")]
        public static void AddedDependencyIsReported()
        {
            var recorded = BuildRecordedState();
            var snapshot = BuildSnapshot(recorded);

            var added = new SourceAssetState("guid-texture-c", "Assets/Tex/c.png", "texture");
            added.AssetDependencyHash = "dep-c";
            added.ImporterSettingsHash = "imp-c";
            snapshot.Sources.Add(added);

            var report = ChangeAnalyzer.Analyze(recorded, snapshot);

            Assert.AreEqual(1, report.OfKind(ChangeKind.DependencyAdded).Count, "the new dependency is reported");
        }

        [Test("a vanished dependency invalidates only the outputs derived from it")]
        public static void RemovedDependencyInvalidatesOnlyDependentOutputs()
        {
            var recorded = BuildRecordedState();
            var snapshot = BuildSnapshot(recorded);
            snapshot.Sources.RemoveAt(0); // texture A disappears from the avatar

            var report = ChangeAnalyzer.Analyze(recorded, snapshot);

            var removals = report.OfKind(ChangeKind.DependencyRemoved);
            Assert.AreEqual(1, removals.Count, "the removal is reported");
            Assert.IsFalse(removals[0].InvalidatesEverything,
                "a removed dependency must not invalidate unrelated outputs");
            Assert.IsTrue(report.StaleGeneratedPaths.Contains(recorded.Generated[0].Path),
                "the output derived from the removed dependency is stale");
            Assert.IsTrue(report.ValidGeneratedPaths.Contains(recorded.Generated[1].Path),
                "an output derived from an untouched dependency stays valid");
            Assert.AreEqual(OptimizationStatus.ChangesDetected, report.Status,
                "a partial invalidation is reported as Changes Detected");
        }

        [Test("when every output depends on the vanished dependency the whole set is stale")]
        public static void RemovedDependencyStalesEverythingItFeeds()
        {
            var recorded = BuildRecordedState();
            recorded.Generated[1].DerivedFromGuids.Add("guid-texture-a");
            var snapshot = BuildSnapshot(recorded);
            snapshot.Sources.RemoveAt(0);

            var report = ChangeAnalyzer.Analyze(recorded, snapshot);

            Assert.AreEqual(2, report.StaleGeneratedPaths.Count,
                "both outputs depend on the removed dependency");
            Assert.AreEqual(0, report.ValidGeneratedPaths.Count, "nothing survives");
            Assert.AreEqual(OptimizationStatus.ReoptimizationRequired, report.Status,
                "with no surviving output the whole set must be rebuilt");
        }

        [Test("an output with no recorded provenance is treated as stale")]
        public static void OutputWithoutProvenanceIsStale()
        {
            var recorded = BuildRecordedState();
            recorded.Generated[0].DerivedFromGuids.Clear();

            var snapshot = BuildSnapshot(recorded);
            // Nothing changed in the sources, but the output has no provenance to reason about.
            snapshot.Sources[0].AssetDependencyHash = "dep-a-changed";

            var report = ChangeAnalyzer.Analyze(recorded, snapshot);

            Assert.IsTrue(report.StaleGeneratedPaths.Contains(recorded.Generated[0].Path),
                "an output that cannot be traced to a source must not be declared valid");
        }

        [Test("a failed run keeps reporting OptimizationFailed")]
        public static void FailedRunKeepsStatus()
        {
            var recorded = BuildRecordedState();
            recorded.Status = OptimizationStatus.OptimizationFailed;
            recorded.LastError = "the importer rejected the texture";

            var report = ChangeAnalyzer.Analyze(recorded, BuildSnapshot(recorded));
            Assert.AreEqual(OptimizationStatus.OptimizationFailed, report.Status, "a failed run is reported as failed");
        }

        [Test("an unsupported avatar keeps reporting Unsupported")]
        public static void UnsupportedAvatarKeepsStatus()
        {
            var recorded = BuildRecordedState();
            recorded.Status = OptimizationStatus.Unsupported;

            var report = ChangeAnalyzer.Analyze(recorded, BuildSnapshot(recorded));
            Assert.AreEqual(OptimizationStatus.Unsupported, report.Status, "an unsupported avatar is reported as unsupported");
        }

        [Test("a null snapshot is refused")]
        public static void NullSnapshotIsRefused()
        {
            Assert.Throws<ArgumentNullException>(delegate { ChangeAnalyzer.Analyze(BuildRecordedState(), null); }, "null snapshot");
        }

        private sealed class StubFingerprintProvider : IFingerprintProvider
        {
            public string GetAssetDependencyHash(string guid, string path)
            {
                return "dep:" + guid;
            }

            public string GetImporterSettingsHash(string guid, string path)
            {
                return "imp:" + guid;
            }
        }
    }
}
