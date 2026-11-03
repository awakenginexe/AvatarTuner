using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AvatarTuner.Core;
using AvatarTuner.Editor.Apply;
using AvatarTuner.Editor.ViewModels;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Offline regression tests for direct scene application: the planner decides, the
    /// (Unity) applier only gathers and executes. Covers material replacement, shared
    /// materials, inactive renderers, animated material references, idempotent
    /// re-optimization, change detection, restore, missing outputs, status honesty and
    /// source immutability.
    /// </summary>
    public static class ApplicationTests
    {
        private const string Root = "Assets/AvatarTuner-Generated/A-rootguid";

        private static ApplicationInput MakeInput()
        {
            var input = new ApplicationInput();
            input.GeneratedTexturePathBySourceGuid["tex-a"] = Root + "/Textures/a_gen.png";
            input.GeneratedTexturePathBySourceGuid["tex-b"] = Root + "/Textures/b_gen.png";
            return input;
        }

        private static ApplicationInput InputWithTwoMaterials()
        {
            var input = MakeInput();

            // Material 1 ("Body") has an optimizable texture; material 2 ("Prop") has one
            // texture that was never optimized (kept at source) - both get renderer slots.
            input.TextureSlots.Add(new TextureSlotRow
            {
                MaterialGuid = "mat-1", MaterialName = "Body", MaterialPath = "Assets/Mat/body.mat",
                Property = "_MainTex", SourceTextureGuid = "tex-a"
            });
            input.TextureSlots.Add(new TextureSlotRow
            {
                MaterialGuid = "mat-2", MaterialName = "Prop", MaterialPath = "Assets/Mat/prop.mat",
                Property = "_MainTex", SourceTextureGuid = "tex-x"
            });
            input.RendererSlots.Add(new RendererSlotRow
            {
                RendererPath = "Avatar/Body", SlotIndex = 0, MaterialGuid = "mat-1", IsActive = true
            });
            input.RendererSlots.Add(new RendererSlotRow
            {
                RendererPath = "Avatar/Body", SlotIndex = 1, MaterialGuid = "mat-2", IsActive = true
            });
            return input;
        }

        // ------------------------------------------------------------------ planning

        [Test("material references are replaced by generated clones with remapped textures")]
        public static void MaterialReferenceReplacement()
        {
            var plan = ApplicationPlanner.Plan(InputWithTwoMaterials(), Root);

            Assert.AreEqual(ApplicationVerdict.Ready, plan.Verdict, "a clean remap is ready");
            Assert.AreEqual(1, plan.MaterialClones.Count, "only the optimizable material is cloned");
            var clone = plan.MaterialClones[0];
            Assert.AreEqual("mat-1", clone.OriginalMaterialGuid, "the clone tracks its original");
            Assert.AreEqual(Root + "/Materials/Body-mat-1.mat", clone.GeneratedMaterialPath,
                "the clone lives under the generated root with a deterministic name");
            Assert.AreEqual(1, clone.Slots.Count, "one texture slot is remapped");
            Assert.AreEqual("_MainTex", clone.Slots[0].Property, "the property is preserved");
            Assert.AreEqual(Root + "/Textures/a_gen.png", clone.Slots[0].GeneratedTexturePath,
                "the slot points at the generated texture");

            Assert.AreEqual(1, plan.RendererAssignments.Count, "only slots using the clone are assigned");
            Assert.AreEqual("Avatar/Body", plan.RendererAssignments[0].RendererPath, "the renderer is named");
            Assert.AreEqual("mat-1", plan.RendererAssignments[0].OriginalMaterialGuid,
                "the original slot is remembered for restore");
        }

        [Test("a shared material gets one clone shared by every renderer slot")]
        public static void SharedMaterialsShareOneClone()
        {
            var input = MakeInput();
            input.TextureSlots.Add(new TextureSlotRow
            {
                MaterialGuid = "mat-1", MaterialName = "Shared", MaterialPath = "Assets/Mat/s.mat",
                Property = "_MainTex", SourceTextureGuid = "tex-a"
            });
            input.RendererSlots.Add(new RendererSlotRow { RendererPath = "Avatar/A", MaterialGuid = "mat-1", IsActive = true });
            input.RendererSlots.Add(new RendererSlotRow { RendererPath = "Avatar/B", MaterialGuid = "mat-1", IsActive = true });
            input.RendererSlots.Add(new RendererSlotRow { RendererPath = "Avatar/C", MaterialGuid = "mat-1", IsActive = true });

            var plan = ApplicationPlanner.Plan(input, Root);

            Assert.AreEqual(1, plan.MaterialClones.Count, "one clone for the shared material");
            Assert.AreEqual(3, plan.RendererAssignments.Count, "all three slots reference the clone");
            for (var i = 0; i < plan.RendererAssignments.Count; i++)
            {
                Assert.AreEqual(plan.MaterialClones[0].GeneratedMaterialPath,
                    plan.RendererAssignments[i].GeneratedMaterialPath, "the same clone is shared");
            }
        }

        [Test("inactive renderers are included in the application plan")]
        public static void InactiveRenderersAreIncluded()
        {
            var input = MakeInput();
            input.TextureSlots.Add(new TextureSlotRow
            {
                MaterialGuid = "mat-1", MaterialName = "Body", MaterialPath = "Assets/Mat/body.mat",
                Property = "_MainTex", SourceTextureGuid = "tex-a"
            });
            input.RendererSlots.Add(new RendererSlotRow
            {
                RendererPath = "Avatar/Outfit (hidden)", SlotIndex = 0, MaterialGuid = "mat-1", IsActive = false
            });

            var plan = ApplicationPlanner.Plan(input, Root);
            Assert.AreEqual(1, plan.RendererAssignments.Count, "inactive renderers are not dropped");
            Assert.IsFalse(plan.RendererAssignments[0].RendererIsActive, "activeness is recorded");
        }

        [Test("animated material references are remapped on cloned animation assets")]
        public static void AnimatedMaterialReferencesAreRemapped()
        {
            var input = MakeInput();
            input.TextureSlots.Add(new TextureSlotRow
            {
                MaterialGuid = "mat-1", MaterialName = "Body", MaterialPath = "Assets/Mat/body.mat",
                Property = "_MainTex", SourceTextureGuid = "tex-a"
            });
            input.RendererSlots.Add(new RendererSlotRow { RendererPath = "Avatar/Body", MaterialGuid = "mat-1" });
            input.AnimationMaterials.Add(new AnimationMaterialRow
            {
                ClipPath = "Assets/Anim/outfit-toggle.anim",
                ControllerPath = "Assets/Anim/fx.controller",
                AnimatorPath = "Avatar",
                MaterialGuid = "mat-1"
            });

            var plan = ApplicationPlanner.Plan(input, Root);

            Assert.AreEqual(ApplicationVerdict.Ready, plan.Verdict, "resolvable animations are remapped");
            Assert.AreEqual(1, plan.ControllerClones.Count, "the controller is cloned");
            Assert.AreEqual(1, plan.ControllerClones[0].Clips.Count, "its clip is cloned");
            Assert.AreEqual(Root + "/Animations/outfit-toggle-opt.anim",
                plan.ControllerClones[0].Clips[0].GeneratedClipPath, "the clip copy is generated");
            Assert.AreEqual("Avatar", plan.ControllerClones[0].AnimatorPath,
                "the animator gets the cloned controller");
            Assert.AreEqual(1, plan.ControllerClones[0].Clips[0].RemapCount, "the reference count is recorded");
        }

        [Test("unresolvable animated references mark the result partial, never silent")]
        public static void UnresolvableAnimatedReferencesArePartial()
        {
            var input = MakeInput();
            input.TextureSlots.Add(new TextureSlotRow
            {
                MaterialGuid = "mat-1", MaterialName = "Body", MaterialPath = "Assets/Mat/body.mat",
                Property = "_MainTex", SourceTextureGuid = "tex-a"
            });
            input.RendererSlots.Add(new RendererSlotRow { RendererPath = "Avatar/Body", MaterialGuid = "mat-1" });
            input.AnimationMaterials.Add(new AnimationMaterialRow
            {
                ClipPath = string.Empty, // runtime-generated clip: cannot clone an asset
                ControllerPath = string.Empty,
                AnimatorPath = "Avatar",
                MaterialGuid = "mat-1"
            });

            var plan = ApplicationPlanner.Plan(input, Root);

            Assert.AreEqual(ApplicationVerdict.PartiallyRemappable, plan.Verdict,
                "unresolvable references make the result partial");
            Assert.AreEqual("apply.warn.animUnresolvable", plan.Findings[0].MessageKey,
                "and the unresolvable reference is named");
            Assert.AreEqual(0, plan.ControllerClones.Count, "no controller is guessed");
        }

        [Test("re-optimization plans identical paths: no duplicate materials pile up")]
        public static void ReOptimizationIsIdempotent()
        {
            var first = ApplicationPlanner.Plan(InputWithTwoMaterials(), Root);
            var second = ApplicationPlanner.Plan(InputWithTwoMaterials(), Root);

            Assert.AreEqual(first.MaterialClones.Count, second.MaterialClones.Count, "same clone count");
            for (var i = 0; i < first.MaterialClones.Count; i++)
            {
                Assert.AreEqual(first.MaterialClones[i].GeneratedMaterialPath,
                    second.MaterialClones[i].GeneratedMaterialPath,
                    "generated material paths are deterministic (overwrite in place)");
            }

            Assert.AreEqual(first.RendererAssignments.Count, second.RendererAssignments.Count,
                "the same renderer assignments are planned");
        }

        // ------------------------------------------------------------------ evaluation

        private static AvatarOptimizationState AppliedState(bool partial)
        {
            var state = new AvatarOptimizationState { AvatarName = "Avatar", SourceRootGuid = "root" };
            state.AppliedToAvatar = true;
            state.AppliedPartially = partial;
            state.RendererBindings.Add(new AppliedRendererBinding
            {
                RendererPath = "Avatar/Body",
                SlotIndex = 0,
                OriginalMaterialGuid = "mat-1",
                AppliedMaterialGuid = "mat-1-clone",
                AppliedMaterialPath = Root + "/Materials/body.mat"
            });
            return state;
        }

        [Test("change detection tracks references, not unrelated edits")]
        public static void ChangeDetectionIsReferenceLevel()
        {
            var recorded = AppliedState(false);
            var entries = new List<string>();

            var same = new List<ObservedSlot>
            {
                new ObservedSlot { RendererPath = "Avatar/Body", SlotIndex = 0, MaterialGuid = "mat-1-clone" }
            };
            Assert.AreEqual(ApplicationHealth.Applied,
                ApplicationPlanner.Evaluate(recorded, same, entries), "unchanged references stay applied");
            Assert.AreEqual(0, entries.Count, "nothing reported");

            var reverted = new List<ObservedSlot>
            {
                new ObservedSlot { RendererPath = "Avatar/Body", SlotIndex = 0, MaterialGuid = "mat-1" }
            };
            Assert.AreEqual(ApplicationHealth.Drifted,
                ApplicationPlanner.Evaluate(recorded, reverted, entries),
                "a user reversion to the original material is detected");
            Assert.AreEqual("Avatar/Body", entries[0], "the drifted renderer is named");
        }

        [Test("a new renderer reusing an unoptimized original material is detected")]
        public static void NewUnoptimizedRendererIsDetected()
        {
            var recorded = AppliedState(false);
            var entries = new List<string>();
            var observed = new List<ObservedSlot>
            {
                new ObservedSlot { RendererPath = "Avatar/Body", SlotIndex = 0, MaterialGuid = "mat-1-clone" },
                new ObservedSlot { RendererPath = "Avatar/NewProp", SlotIndex = 0, MaterialGuid = "mat-1" }
            };

            Assert.AreEqual(ApplicationHealth.Drifted,
                ApplicationPlanner.Evaluate(recorded, observed, entries),
                "a new renderer on the original material is drift");
            Assert.AreEqual("Avatar/NewProp", entries[0], "the new renderer is named");

            // Unrelated slots (materials that were never optimizable) must not drift.
            var calm = new List<ObservedSlot>
            {
                new ObservedSlot { RendererPath = "Avatar/Body", SlotIndex = 0, MaterialGuid = "mat-1-clone" },
                new ObservedSlot { RendererPath = "Avatar/Other", SlotIndex = 0, MaterialGuid = "unrelated" }
            };
            entries.Clear();
            Assert.AreEqual(ApplicationHealth.Applied,
                ApplicationPlanner.Evaluate(recorded, calm, entries),
                "unrelated renderers never cause drift");
        }

        [Test("a missing recorded slot (renderer removed) is drift")]
        public static void RemovedRendererIsDetected()
        {
            var recorded = AppliedState(false);
            var entries = new List<string>();
            var observed = new List<ObservedSlot>();

            Assert.AreEqual(ApplicationHealth.Drifted,
                ApplicationPlanner.Evaluate(recorded, observed, entries), "a missing slot is drift");
            Assert.AreEqual("Avatar/Body", entries[0], "and it is named");
        }

        [Test("restore plans return every slot to its recorded original")]
        public static void RestorePlansOriginals()
        {
            var recorded = AppliedState(false);
            var steps = ApplicationPlanner.BuildRestoreAssignments(recorded);

            Assert.AreEqual(1, steps.Count, "every binding is restored");
            Assert.AreEqual("mat-1", steps[0].OriginalMaterialGuid, "the original material is the target");
            Assert.AreEqual(string.Empty, steps[0].GeneratedMaterialPath,
                "an empty generated path marks the restore");

            Assert.AreEqual(0, ApplicationPlanner.BuildRestoreAssignments(null).Count,
                "nothing recorded, nothing restored");
        }

        // ------------------------------------------------------------------ status & honesty

        [Test("applied status follows the verified application health")]
        public static void AppliedStatusIsVerified()
        {
            Assert.AreEqual(AvatarDisplayStatus.AppliedToAvatar,
                StatusModel.DeriveApplication(AvatarDisplayStatus.OutputGenerated, ApplicationHealth.Applied),
                "verified references read as applied");
            Assert.AreEqual(AvatarDisplayStatus.PartiallyOptimized,
                StatusModel.DeriveApplication(AvatarDisplayStatus.OutputGenerated, ApplicationHealth.PartiallyApplied),
                "partial references read as partial");
            Assert.AreEqual(AvatarDisplayStatus.ChangesDetected,
                StatusModel.DeriveApplication(AvatarDisplayStatus.AppliedToAvatar, ApplicationHealth.Drifted),
                "drifted references read as changes detected");
            Assert.AreEqual(AvatarDisplayStatus.OutputGenerated,
                StatusModel.DeriveApplication(AvatarDisplayStatus.OutputGenerated, ApplicationHealth.NotApplied),
                "nothing applied keeps the generated-not-applied status");
        }

        [Test("the application plan can only touch material slots and animation copies")]
        public static void SourceAssetsAreImmutableByConstruction()
        {
            var plan = ApplicationPlanner.Plan(InputWithTwoMaterials(), Root);

            Assert.IsFalse(plan.CreatesRenderersOrMeshes, "no renderers or meshes are ever created");
            for (var i = 0; i < plan.MaterialClones.Count; i++)
            {
                Assert.IsTrue(plan.MaterialClones[i].GeneratedMaterialPath.StartsWith(Root, StringComparison.Ordinal),
                    "clones are written under the generated root only");
                Assert.AreNotEqual(plan.MaterialClones[i].OriginalMaterialPath,
                    plan.MaterialClones[i].GeneratedMaterialPath, "the original is never the target");
            }

            for (var i = 0; i < plan.RendererAssignments.Count; i++)
            {
                Assert.AreNotEqual(plan.RendererAssignments[i].OriginalMaterialGuid,
                    plan.RendererAssignments[i].GeneratedMaterialPath, "assignments never self-reference");
            }
        }

        [Test("missing generated textures skip that slot and warn")]
        public static void MissingGeneratedTexturesAreHandled()
        {
            var input = InputWithTwoMaterials();
            input.MissingGeneratedPaths.Add(Root + "/Textures/a_gen.png");

            var plan = ApplicationPlanner.Plan(input, Root);

            Assert.AreEqual(ApplicationVerdict.PartiallyRemappable, plan.Verdict, "missing outputs are partial");
            Assert.AreEqual(0, plan.MaterialClones.Count, "the material is not cloned without its texture");
            Assert.AreEqual("build.warn.missingGenerated", plan.Findings[0].MessageKey,
                "the missing output is named");
        }

        [Test("current memory values only known references and never invents")]
        public static void CurrentMemoryNeverInvents()
        {
            var source = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                { "tex-a", 1000 }
            };
            var target = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
            {
                { "tex-a-gen", 400 }
            };

            bool allKnown;
            var total = ApplicationPlanner.ComputeCurrentBytes(
                source, target, new[] { "tex-a", "tex-a-gen" }, out allKnown);
            Assert.AreEqual(1400, total, "known references are valued");
            Assert.IsTrue(allKnown, "all known");

            total = ApplicationPlanner.ComputeCurrentBytes(
                source, target, new[] { "tex-a", "mystery" }, out allKnown);
            Assert.IsFalse(allKnown, "an unknown reference makes the value unavailable");

            total = ApplicationPlanner.ComputeCurrentBytes(
                source, target, new[] { "tex-a", "tex-a" }, out allKnown);
            Assert.AreEqual(1000, total, "a texture referenced twice counts once");
        }

        // ------------------------------------------------------------------ Unity glue guards

        [Test("the plan declares every destination folder before any copy (CopyAsset needs them)")]
        public static void PlanDeclaresFoldersBeforeCopy()
        {
            var withMaterials = ApplicationPlanner.Plan(InputWithTwoMaterials(), Root);
            Assert.AreEqual(1, withMaterials.RequiredFolders.Count, "the materials folder is declared");
            Assert.AreEqual(Root + "/Materials", withMaterials.RequiredFolders[0],
                "the exact folder the clones are copied into");

            var empty = ApplicationPlanner.Plan(new ApplicationInput(), Root);
            Assert.AreEqual(0, empty.RequiredFolders.Count, "nothing to copy means no folders");
        }

        [Test("the success timestamp requires a verified application")]
        public static void ApplicationStampRequiresVerifiedApplication()
        {
            Assert.IsFalse(ApplicationPlanner.IsVerifiedApplication(null), "no outcome is not applied");

            var empty = new SceneApplicationOutcome();
            Assert.IsFalse(ApplicationPlanner.IsVerifiedApplication(empty), "no success flag is not applied");

            empty.Success = true;
            Assert.IsFalse(ApplicationPlanner.IsVerifiedApplication(empty),
                "success with zero assignments is not applied (the regression: stamp without effect)");

            empty.AssignedSlots = 1;
            Assert.IsTrue(ApplicationPlanner.IsVerifiedApplication(empty),
                "a verified assignment counts as applied");
        }

        [Test("an application failure marks the manifest state as failed, not optimized")]
        public static void ApplicationFailureMarksManifestFailed()
        {
            var state = new AvatarOptimizationState
            {
                Status = OptimizationStatus.Optimized,
                LastSuccessUtc = "2026-01-02T03:04:05Z"
            };

            ApplicationPlanner.MarkApplicationFailed(state, "apply.error.applyFailed");

            Assert.AreEqual(OptimizationStatus.OptimizationFailed, state.Status,
                "the manifest says failed, not optimized");
            Assert.AreEqual("apply.error.applyFailed", state.LastError, "the error key is recorded");
            Assert.AreEqual(string.Empty, state.LastSuccessUtc,
                "a failed application never claims a success timestamp");
        }

        [Test("the applier preflights folders and rolls back scene changes on failure")]
        public static void ApplierIsTransactional()
        {
            var source = ReadApplier();
            Assert.Contains("TextureOptimizer.EnsureFolder", source,
                "destination folders are created before any copy");
            Assert.Contains("plan.RequiredFolders", source,
                "the folders come from the plan's preflight contract");
            Assert.Contains("RollbackTracker", source, "scene changes are tracked for rollback");
            Assert.Contains("rollback.RestoreAll()", source, "failure restores the originals");
            Assert.Contains("EditorSceneManager.MarkSceneDirty", source,
                "the scene is marked dirty so applied references persist across saves");
        }

        [Test("scene application is Undo-recorded and prefab-instance aware")]
        public static void SceneApplyIsUndoAndPrefabAware()
        {
            var source = ReadApplier();
            Assert.Contains("Undo.RecordObject(renderer", source,
                "renderer changes are Undo-recorded");
            Assert.Contains("Undo.RecordObject(animator", source,
                "controller changes are Undo-recorded");
            Assert.Contains("PrefabUtility.RecordPrefabInstancePropertyModifications", source,
                "prefab instance overrides are registered (persistent across scene saves)");
        }

        [Test("scene application never edits source asset files")]
        public static void SceneApplyNeverWritesSourceAssets()
        {
            var source = ReadApplier();
            Assert.IsFalse(source.Contains("AssetDatabase.CreateAsset("),
                "no new assets in source folders (only CopyAsset into the generated root)");
            Assert.IsFalse(source.Contains("SaveAssetIfDirty"), "no in-place asset writes");
            Assert.Contains("Undo.RecordObject(renderer", source, "the guard target is present");
        }

        private static string ReadApplier()
        {
            var path = Path.Combine(PackageLayout.Root, "Editor", "Apply", "SceneTextureApplier.cs");
            Assert.IsTrue(File.Exists(path), "SceneTextureApplier.cs exists to scan");
            return File.ReadAllText(path, Encoding.UTF8);
        }
    }
}
