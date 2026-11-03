using System;
using System.Collections.Generic;
using AvatarTuner.Core;
using AvatarTuner.Editor.Build;
using AvatarTuner.Editor.ViewModels;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Offline regression tests for the build-time texture application (NDMF) milestone.
    /// The planner is Unity-free: the NDMF pass only gathers these inputs on the build
    /// clone and executes the plan, so the whole decision surface is pinned here.
    /// </summary>
    public static class BuildRemapTests
    {
        // ------------------------------------------------------------------ fixtures

        private static MaterialUsage Material(string id, string name, params string[] textureGuids)
        {
            var usage = new MaterialUsage { MaterialId = id, MaterialName = name, RendererCount = 1 };
            for (var i = 0; i < textureGuids.Length; i++)
            {
                usage.Textures.Add(new MaterialTextureRef
                {
                    Property = i == 0 ? "_MainTex" : "_Prop" + i,
                    SourceTextureGuid = textureGuids[i]
                });
            }

            return usage;
        }

        private static BuildRemapRequest ReadyRequest()
        {
            return new BuildRemapRequest
            {
                BuildEnabled = true,
                HasRecordedOutputs = true,
                SourcesAreUnchanged = true
            };
        }

        private static AvatarOptimizationState RecordedAvatar(string name, string rootGuid, params string[] sourceGuids)
        {
            var state = new AvatarOptimizationState { AvatarName = name, SourceRootGuid = rootGuid };
            for (var i = 0; i < sourceGuids.Length; i++)
            {
                var generated = new GeneratedAssetState
                {
                    Path = "Assets/AvatarTuner-Generated/" + name + "/Textures/" + sourceGuids[i] + "_gen.png"
                };
                generated.DerivedFromGuids.Add(sourceGuids[i]);
                state.Generated.Add(generated);
            }

            return state;
        }

        // ------------------------------------------------------------------ mapping

        [Test("source texture references map to their generated optimized textures")]
        public static void SourceToGeneratedMapping()
        {
            var request = ReadyRequest();
            BuildTexturePlanner.BuildGeneratedMap(RecordedAvatar("A", "root-a", "tex-a", "tex-b"), request);
            request.Materials.Add(Material("mat-1", "Body", "tex-a", "tex-b"));

            var plan = BuildTexturePlanner.Plan(request);

            Assert.AreEqual(BuildRemapVerdict.Ready, plan.Verdict, "a clean remap is ready");
            Assert.AreEqual(1, plan.Replacements.Count, "one material is remapped");
            Assert.AreEqual(2, plan.TextureReplacementCount, "both textures map");
            Assert.AreEqual("Assets/AvatarTuner-Generated/A/Textures/tex-a_gen.png",
                plan.Replacements[0].Slots[0].GeneratedPath, "source guid maps to the generated path");
        }

        [Test("shared materials produce exactly one replacement shared by every renderer")]
        public static void SharedMaterialsShareOneReplacement()
        {
            var request = ReadyRequest();
            BuildTexturePlanner.BuildGeneratedMap(RecordedAvatar("A", "root-a", "tex-a"), request);
            var shared = Material("mat-1", "Shared", "tex-a");
            shared.RendererCount = 3;
            request.Materials.Add(shared);

            var plan = BuildTexturePlanner.Plan(request);

            Assert.AreEqual(1, plan.Replacements.Count, "one clone per material, not per renderer");
            Assert.AreEqual(3, plan.Replacements[0].SharedRendererCount,
                "every renderer sharing the material shares the clone");
            Assert.AreEqual(1, plan.TextureReplacementCount, "one texture remap total");
        }

        [Test("materials used only by inactive renderers are remapped like any other")]
        public static void InactiveRenderersAreCovered()
        {
            var request = ReadyRequest();
            BuildTexturePlanner.BuildGeneratedMap(RecordedAvatar("A", "root-a", "tex-a"), request);
            var inactive = Material("mat-1", "Hidden", "tex-a");
            inactive.UsedByInactiveRenderer = true;
            request.Materials.Add(inactive);

            var plan = BuildTexturePlanner.Plan(request);

            Assert.AreEqual(1, plan.Replacements.Count,
                "inactive renderers take part in the remap (gathered with includeInactive)");
            Assert.IsTrue(plan.Replacements[0].SharedRendererCount >= 1, "usage is recorded");
        }

        [Test("animated material swaps keep functionality and are reported")]
        public static void AnimatedMaterialReferencesAreHandled()
        {
            var request = ReadyRequest();
            BuildTexturePlanner.BuildGeneratedMap(RecordedAvatar("A", "root-a", "tex-a"), request);
            var animated = Material("mat-1", "AnimatedOutfit", "tex-a");
            animated.IsAnimationTarget = true;
            request.Materials.Add(animated);

            var plan = BuildTexturePlanner.Plan(request);

            Assert.AreEqual(BuildRemapVerdict.ReadyWithWarnings, plan.Verdict,
                "animation-driven swaps apply with a warning, not silently");
            Assert.AreEqual(1, plan.Replacements.Count, "the base slot is still remapped");
            Assert.AreEqual("build.warn.animSwapKept", plan.Findings[0].MessageKey,
                "the animated limitation is reported");
        }

        [Test("missing or stale generated textures are failures, never silent success")]
        public static void MissingAndStaleOutputsFailTruthfully()
        {
            var stale = ReadyRequest();
            stale.SourcesAreUnchanged = false;
            stale.Materials.Add(Material("mat-1", "Body", "tex-a"));
            Assert.AreEqual(BuildRemapVerdict.SkippedStale, BuildTexturePlanner.Plan(stale).Verdict,
                "stale outputs skip everything and demand re-optimization");
            Assert.AreEqual(0, BuildTexturePlanner.Plan(stale).Replacements.Count,
                "a stale plan remaps nothing");

            var missing = ReadyRequest();
            BuildTexturePlanner.BuildGeneratedMap(RecordedAvatar("A", "root-a", "tex-a"), missing);
            missing.MissingGeneratedPaths.Add("Assets/AvatarTuner-Generated/A/Textures/tex-a_gen.png");
            missing.Materials.Add(Material("mat-1", "Body", "tex-a"));

            var plan = BuildTexturePlanner.Plan(missing);
            Assert.AreEqual(BuildRemapVerdict.PartialFailure, plan.Verdict,
                "a missing generated texture is a failure, not a success");
            Assert.AreEqual(0, plan.Replacements.Count, "the missing texture is not replaced");
            Assert.AreEqual("build.warn.missingGenerated", plan.Findings[0].MessageKey,
                "the failure names the missing file");

            var ambiguous = ReadyRequest();
            ambiguous.AmbiguousSourceGuids.Add("tex-a");
            ambiguous.GeneratedPathBySourceGuid["tex-a"] = "Assets/Gen/a.png";
            ambiguous.Materials.Add(Material("mat-1", "Body", "tex-a"));
            var ambiguousPlan = BuildTexturePlanner.Plan(ambiguous);
            Assert.AreEqual(BuildRemapVerdict.PartialFailure, ambiguousPlan.Verdict,
                "an ambiguous mapping is a failure, not a guess");
            Assert.AreEqual("build.warn.ambiguousMapping", ambiguousPlan.Findings[0].MessageKey,
                "ambiguity is reported");
        }

        [Test("multiple avatars in one project match by guid, never by guess")]
        public static void MultipleAvatarsMatchExactly()
        {
            var avatars = new List<AvatarOptimizationState>
            {
                RecordedAvatar("Sakura", "root-1", "tex-a"),
                RecordedAvatar("Sakura", "root-2", "tex-b")
            };

            AvatarOptimizationState chosen;
            Assert.AreEqual(AvatarMatchVerdict.Matched,
                BuildTexturePlanner.SelectAvatar("root-2", "Sakura", avatars, out chosen),
                "the prefab root guid wins over the name");
            Assert.AreEqual("root-2", chosen.SourceRootGuid, "the exact avatar is chosen");

            Assert.AreEqual(AvatarMatchVerdict.Ambiguous,
                BuildTexturePlanner.SelectAvatar("", "Sakura", avatars, out chosen),
                "two same-name scene avatars are ambiguous, never guessed");
            Assert.IsNull(chosen, "nothing is chosen when ambiguous");

            Assert.AreEqual(AvatarMatchVerdict.NotFound,
                BuildTexturePlanner.SelectAvatar("", "Unknown", avatars, out chosen),
                "an unknown avatar matches nothing");
        }

        [Test("build-time application requires the consent setting")]
        public static void BuildTimeEnableDisableIsHonored()
        {
            var off = ReadyRequest();
            off.BuildEnabled = false;
            off.Materials.Add(Material("mat-1", "Body", "tex-a"));
            var plan = BuildTexturePlanner.Plan(off);
            Assert.AreEqual(BuildRemapVerdict.NotEnabled, plan.Verdict, "no consent, no remap");
            Assert.AreEqual(0, plan.Replacements.Count, "nothing is applied when disabled");

            var noOutputs = ReadyRequest();
            noOutputs.HasRecordedOutputs = false;
            Assert.AreEqual(BuildRemapVerdict.NoOutputs, BuildTexturePlanner.Plan(noOutputs).Verdict,
                "consent without outputs is reported, not guessed");

            var conflict = ReadyRequest();
            off.BuildEnabled = true;
            conflict.OptimizerConflictDetected = true;
            conflict.Materials.Add(Material("mat-1", "Body", "tex-a"));
            var conflictPlan = BuildTexturePlanner.Plan(conflict);
            Assert.AreEqual(BuildRemapVerdict.SkippedOptimizerConflict, conflictPlan.Verdict,
                "optimizer conflicts skip the unsafe transformation");
            Assert.AreEqual("build.warn.optimizerConflict", conflictPlan.Findings[0].MessageKey,
                "with an actionable warning");
        }

        [Test("a build remap plan can only touch material texture slots, never source assets")]
        public static void SourceAssetsAreImmutable()
        {
            var request = ReadyRequest();
            BuildTexturePlanner.BuildGeneratedMap(RecordedAvatar("A", "root-a", "tex-a"), request);
            request.Materials.Add(Material("mat-1", "Body", "tex-a"));

            var plan = BuildTexturePlanner.Plan(request);

            Assert.IsFalse(plan.CreatesRenderersOrMeshes, "no renderers or meshes are ever created");
            Assert.IsTrue(plan.Replacements[0].Slots[0].GeneratedPath
                    .IndexOf("Assets/Tex/", StringComparison.OrdinalIgnoreCase) < 0,
                "replacements never target source asset paths");
        }

        [Test("one replacement per material, no duplicated renderers or materials")]
        public static void NoDuplication()
        {
            var request = ReadyRequest();
            BuildTexturePlanner.BuildGeneratedMap(RecordedAvatar("A", "root-a", "tex-a", "tex-b"), request);
            var shared = Material("mat-1", "Shared", "tex-a");
            shared.RendererCount = 2;
            request.Materials.Add(shared);
            request.Materials.Add(Material("mat-2", "Other", "tex-b"));

            var plan = BuildTexturePlanner.Plan(request);

            Assert.IsFalse(plan.CreatesRenderersOrMeshes, "the plan creates no renderers or meshes");
            Assert.AreEqual(2, plan.Replacements.Count, "one replacement per material");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < plan.Replacements.Count; i++)
            {
                Assert.IsTrue(ids.Add(plan.Replacements[i].MaterialId),
                    "material " + plan.Replacements[i].MaterialId + " appears exactly once");
            }
        }

        [Test("failure states are reported truthfully in the status derivation")]
        public static void FailureHandlingAndStatus()
        {
            Assert.AreEqual(AvatarDisplayStatus.BuildTimeFailed,
                StatusModel.DeriveBuildTime(AvatarDisplayStatus.OutputGenerated, true, true, true),
                "a recorded build failure is shown as failed");
            Assert.AreEqual(AvatarDisplayStatus.BuildTimeEnabled,
                StatusModel.DeriveBuildTime(AvatarDisplayStatus.OutputGenerated, true, true, false),
                "consent given reads as enabled");
            Assert.AreEqual(AvatarDisplayStatus.BuildTimeDisabled,
                StatusModel.DeriveBuildTime(AvatarDisplayStatus.OutputGenerated, true, false, false),
                "consent withdrawn reads as disabled");
            Assert.AreEqual(AvatarDisplayStatus.OutputGenerated,
                StatusModel.DeriveBuildTime(AvatarDisplayStatus.OutputGenerated, false, false, false),
                "an unconfigured setting reads as generated, not applied");
            Assert.AreEqual(AvatarDisplayStatus.ReoptimizationRequired,
                StatusModel.DeriveBuildTime(AvatarDisplayStatus.ReoptimizationRequired, true, true, false),
                "stale outputs outrank the build setting");
        }

        [Test("build-application settings survive the manifest round trip")]
        public static void BuildSettingsRoundTrip()
        {
            var state = RecordedAvatar("A", "root-a", "tex-a");
            state.BuildApplicationConfigured = true;
            state.UseOptimizedTexturesOnBuild = true;
            state.LastBuildApplicationUtc = "2026-01-02T03:04:05Z";
            state.LastBuildApplicationError = "build.error.partialApplication";

            var restored = AvatarOptimizationState.FromDictionary(state.ToDictionary());
            Assert.IsTrue(restored.BuildApplicationConfigured, "configured survives");
            Assert.IsTrue(restored.UseOptimizedTexturesOnBuild, "consent survives");
            Assert.AreEqual("2026-01-02T03:04:05Z", restored.LastBuildApplicationUtc, "evidence survives");
            Assert.AreEqual("build.error.partialApplication", restored.LastBuildApplicationError,
                "failure evidence survives");

            var fresh = AvatarOptimizationState.FromDictionary(new Dictionary<string, object>());
            Assert.IsFalse(fresh.BuildApplicationConfigured, "an old manifest defaults to not configured");
            Assert.IsFalse(fresh.UseOptimizedTexturesOnBuild, "and never consented");
        }
    }
}
