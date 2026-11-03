// AvatarTuner v1.0.0 — direct scene application (Unity glue).
//
// Executes ApplicationPlanner plans against the SELECTED SCENE AVATAR. Every renderer and
// animator change is Undo-recorded and prefab-instance-overrides are registered, so the
// user can inspect the optimized references in the Inspector and undo them at will.
//   - Source assets (textures, materials, clips, controllers, prefabs) are read-only.
//     Materials are replaced by generated clones (AssetDatabase.CopyAsset) whose texture
//     slots point at the generated optimized textures.
//   - No renderer, mesh, bone or avatar root is created or duplicated.
//   - Restore reassigns the recorded originals; generated files are kept.
//   - Failures are reported with findings and never claimed as applied.

using System;
using System.Collections.Generic;
using System.IO;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarTuner.Editor.Apply
{
    public static class SceneTextureApplier
    {
        // ===================================================================== apply

        /// <summary>
        /// Applies the generated optimized textures to the scene avatar's own references.
        /// The avatar must be a scene object; a pure asset selection is refused (writing
        /// to it would modify the source prefab).
        /// </summary>
        public static SceneApplicationOutcome Apply(
            GameObject avatar,
            AvatarAnalysisReport report,
            TextureGenerationResult generation,
            string outputRootFolder)
        {
            var outcome = new SceneApplicationOutcome();
            if (avatar == null || report == null)
            {
                outcome.ErrorKey = "apply.error.applyFailed";
                return outcome;
            }

            try
            {
                // Direct application writes scene references; a Project-window asset is not
                // a scene reference and must never be modified (it IS a source asset).
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(avatar)))
                {
                    outcome.Findings.Add(new Build.PlanFinding("apply.warn.sceneOnly"));
                    return outcome;
                }

                var input = new ApplicationInput();
                BuildTextureMap(generation, input);

                // Materials and their texture slots, from the analysis report.
                for (var i = 0; i < report.Materials.Count; i++)
                {
                    var material = report.Materials[i];
                    for (var b = 0; b < material.TextureBindings.Count; b++)
                    {
                        var binding = material.TextureBindings[b];
                        input.TextureSlots.Add(new TextureSlotRow
                        {
                            MaterialGuid = material.Guid,
                            MaterialName = material.Name,
                            MaterialPath = material.Path,
                            Property = binding.Key,
                            SourceTextureGuid = binding.Value
                        });
                    }
                }

                // Renderer slots, from the analysis report; activeness comes from the scene.
                for (var i = 0; i < report.Renderers.Count; i++)
                {
                    var renderer = report.Renderers[i];
                    var sceneRenderer = FindByHierarchyPath<Renderer>(avatar, renderer.Path);
                    var active = sceneRenderer != null && sceneRenderer.gameObject.activeInHierarchy;
                    for (var s = 0; s < renderer.MaterialGuids.Count; s++)
                    {
                        input.RendererSlots.Add(new RendererSlotRow
                        {
                            RendererPath = renderer.Path,
                            SlotIndex = s,
                            MaterialGuid = renderer.MaterialGuids[s],
                            IsActive = active
                        });
                    }
                }

                // Animated material swaps (clip + controller + animator), from controller assets.
                GatherAnimationMaterials(avatar, input);

                var plan = ApplicationPlanner.Plan(input, outputRootFolder);
                outcome.Findings.AddRange(plan.Findings);
                outcome.Partial = plan.Verdict == ApplicationVerdict.PartiallyRemappable;

                if (plan.Verdict == ApplicationVerdict.NothingToApply)
                {
                    return outcome;
                }

                Execute(plan, avatar, outcome);
                // Success means the pass ran without fatal failure; the verification gate
                // (IsVerifiedApplication) decides whether anything counts as applied.
                outcome.Success = string.IsNullOrEmpty(outcome.ErrorKey);
                return outcome;
            }
            catch (Exception ex)
            {
                outcome.ErrorKey = "apply.error.applyFailed";
                outcome.Findings.Add(new Build.PlanFinding("apply.error.applyFailed"));
                Debug.LogError("[AvatarTuner] scene application failed: " + ex);
                return outcome;
            }
        }

        /// <summary>Tracks scene changes so a failed application can roll them back.</summary>
        private sealed class RollbackTracker
        {
            private struct DescriptorRollbackEntry
            {
                public Component Descriptor;
                public string FieldName;
                public int Index;
                public RuntimeAnimatorController Original;
            }

            private readonly List<KeyValuePair<Renderer, KeyValuePair<int, Material>>> _renderers =
                new List<KeyValuePair<Renderer, KeyValuePair<int, Material>>>();
            private readonly List<KeyValuePair<Animator, RuntimeAnimatorController>> _animators =
                new List<KeyValuePair<Animator, RuntimeAnimatorController>>();
            private readonly List<DescriptorRollbackEntry> _descriptors =
                new List<DescriptorRollbackEntry>();

            public bool HasChanges
            {
                get { return _renderers.Count > 0 || _animators.Count > 0 || _descriptors.Count > 0; }
            }

            public void Track(Renderer renderer, int slotIndex, Material original)
            {
                _renderers.Add(new KeyValuePair<Renderer, KeyValuePair<int, Material>>(
                    renderer, new KeyValuePair<int, Material>(slotIndex, original)));
            }

            public void Track(Animator animator, RuntimeAnimatorController original)
            {
                _animators.Add(new KeyValuePair<Animator, RuntimeAnimatorController>(animator, original));
            }

            public void TrackDescriptor(Component descriptor, string fieldName, int index, RuntimeAnimatorController original)
            {
                _descriptors.Add(new DescriptorRollbackEntry
                {
                    Descriptor = descriptor,
                    FieldName = fieldName,
                    Index = index,
                    Original = original
                });
            }

            /// <summary>Reassigns every tracked reference back to its original (Undo-recorded).</summary>
            public void RestoreAll()
            {
                for (var i = 0; i < _renderers.Count; i++)
                {
                    var entry = _renderers[i];
                    var renderer = entry.Key;
                    if (renderer == null)
                    {
                        continue;
                    }

                    var materials = renderer.sharedMaterials;
                    var slot = entry.Value.Key;
                    if (slot < 0 || slot >= materials.Length)
                    {
                        continue;
                    }

                    Undo.RecordObject(renderer, "AvatarTuner: Roll back failed application");
                    materials[slot] = entry.Value.Value;
                    renderer.sharedMaterials = materials;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }

                for (var i = 0; i < _animators.Count; i++)
                {
                    var entry = _animators[i];
                    if (entry.Key == null)
                    {
                        continue;
                    }

                    Undo.RecordObject(entry.Key, "AvatarTuner: Roll back failed application");
                    entry.Key.runtimeAnimatorController = entry.Value;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(entry.Key);
                }

                for (var i = 0; i < _descriptors.Count; i++)
                {
                    var entry = _descriptors[i];
                    if (entry.Descriptor == null)
                    {
                        continue;
                    }

                    var field = entry.Descriptor.GetType().GetField(entry.FieldName,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field == null)
                    {
                        continue;
                    }

                    var array = field.GetValue(entry.Descriptor) as Array;
                    if (array == null || entry.Index < 0 || entry.Index >= array.Length)
                    {
                        continue;
                    }

                    var layer = array.GetValue(entry.Index);
                    if (layer == null)
                    {
                        continue;
                    }

                    var ctrlField = layer.GetType().GetField("animatorController",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (ctrlField == null)
                    {
                        continue;
                    }

                    Undo.RecordObject(entry.Descriptor, "AvatarTuner: Roll back failed application");
                    ctrlField.SetValue(layer, entry.Original);
                    array.SetValue(layer, entry.Index);
                    field.SetValue(entry.Descriptor, array);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(entry.Descriptor);
                }

                Debug.LogWarning("[AvatarTuner] rolled back " + _renderers.Count +
                                 " renderer assignment(s), " + _animators.Count +
                                 " animator change(s) and " + _descriptors.Count +
                                 " descriptor layer change(s) after a failed application.");
            }
        }

        private static void Execute(
            ApplicationPlan plan, GameObject avatar, SceneApplicationOutcome outcome)
        {
            // ---- 0. preflight: AssetDatabase.CopyAsset fails when the destination folder
            // does not exist, so every folder the plan needs is created first. (This is the
            // regression's root cause: only the Textures folder used to exist.)
            for (var i = 0; i < plan.RequiredFolders.Count; i++)
            {
                if (!TextureOptimizer.EnsureFolder(plan.RequiredFolders[i]))
                {
                    outcome.ErrorKey = "apply.error.applyFailed";
                    Debug.LogError("[AvatarTuner] scene application preflight failed: cannot create folder '" +
                                   plan.RequiredFolders[i] + "'.");
                    return;
                }
            }

            var rollback = new RollbackTracker();
            var cloneByGuid = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // ---- 1. material clones (source materials are read-only; re-runs update
                // the existing clone in place so its GUID - and every reference to it -
                // survives re-optimization)
                for (var i = 0; i < plan.MaterialClones.Count; i++)
                {
                    var step = plan.MaterialClones[i];

                    var clone = AssetDatabase.LoadAssetAtPath<Material>(step.GeneratedMaterialPath);
                    if (clone == null)
                    {
                        if (!AssetDatabase.CopyAsset(step.OriginalMaterialPath, step.GeneratedMaterialPath))
                        {
                            outcome.ErrorKey = "apply.error.applyFailed";
                            Debug.LogError("[AvatarTuner] material clone failed: '" + step.OriginalMaterialPath +
                                           "' -> '" + step.GeneratedMaterialPath + "'.");
                            if (rollback.HasChanges)
                            {
                                rollback.RestoreAll();
                            }

                            return;
                        }

                        clone = AssetDatabase.LoadAssetAtPath<Material>(step.GeneratedMaterialPath);
                        if (clone == null)
                        {
                            outcome.ErrorKey = "apply.error.applyFailed";
                            Debug.LogError("[AvatarTuner] material clone could not be loaded after copy: '" +
                                           step.GeneratedMaterialPath + "'.");
                            if (rollback.HasChanges)
                            {
                                rollback.RestoreAll();
                            }

                            return;
                        }
                    }
                    else
                    {
                        // Refresh the existing material clone from the source material so that any changes to the
                        // original material (shader, colors, floats, vectors, keywords, renderQueue) are refreshed,
                        // while keeping the clone's stable asset GUID intact.
                        var sourceMat = AssetDatabase.LoadAssetAtPath<Material>(step.OriginalMaterialPath);
                        if (sourceMat != null)
                        {
                            clone.shader = sourceMat.shader;
                            clone.CopyPropertiesFromMaterial(sourceMat);
                            clone.shaderKeywords = sourceMat.shaderKeywords;
                            clone.renderQueue = sourceMat.renderQueue;
                        }
                    }

                    for (var s = 0; s < step.Slots.Count; s++)
                    {
                        var slot = step.Slots[s];
                        var texture = AssetDatabase.LoadAssetAtPath<Texture>(slot.GeneratedTexturePath);
                        if (texture == null)
                        {
                            outcome.Partial = true;
                            outcome.Findings.Add(new Build.PlanFinding(
                                "build.warn.missingGenerated", step.MaterialName, slot.GeneratedTexturePath));
                            continue;
                        }

                        clone.SetTexture(slot.Property, texture);
                    }

                    EditorUtility.SetDirty(clone);

                    cloneByGuid[step.OriginalMaterialGuid] = clone;
                    outcome.ClonedMaterials++;
                    outcome.MaterialClones.Add(new AppliedMaterialClone
                    {
                        OriginalMaterialGuid = step.OriginalMaterialGuid,
                        OriginalMaterialPath = step.OriginalMaterialPath,
                        GeneratedMaterialPath = step.GeneratedMaterialPath
                    });

                    var generated = new GeneratedAssetState
                    {
                        Path = step.GeneratedMaterialPath,
                        Kind = "material",
                        Guid = AssetDatabase.AssetPathToGUID(step.GeneratedMaterialPath)
                    };
                    generated.DerivedFromGuids.Add(step.OriginalMaterialGuid);
                    outcome.ExtraGenerated.Add(generated);
                }

                if (plan.MaterialClones.Count > 0)
                {
                    AssetDatabase.SaveAssets();
                }

                // ---- 2. renderer assignments (Undo + prefab-instance overrides + rollback)
                for (var i = 0; i < plan.RendererAssignments.Count; i++)
                {
                    var step = plan.RendererAssignments[i];
                    Material clone;
                    if (!cloneByGuid.TryGetValue(step.OriginalMaterialGuid, out clone) || clone == null)
                    {
                        continue;
                    }

                    var renderer = FindByHierarchyPath<Renderer>(avatar, step.RendererPath);
                    if (renderer == null)
                    {
                        outcome.Partial = true;
                        continue;
                    }

                    var materials = renderer.sharedMaterials;
                    if (step.SlotIndex < 0 || step.SlotIndex >= materials.Length)
                    {
                        outcome.Partial = true;
                        continue;
                    }

                    Undo.RecordObject(renderer, "AvatarTuner: Apply optimized materials");
                    rollback.Track(renderer, step.SlotIndex, materials[step.SlotIndex]);
                    materials[step.SlotIndex] = clone;
                    renderer.sharedMaterials = materials;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);

                    outcome.AssignedSlots++;
                    outcome.RendererBindings.Add(new AppliedRendererBinding
                    {
                        RendererPath = step.RendererPath,
                        SlotIndex = step.SlotIndex,
                        OriginalMaterialGuid = step.OriginalMaterialGuid,
                        AppliedMaterialGuid = AssetDatabase.AssetPathToGUID(step.GeneratedMaterialPath),
                        AppliedMaterialPath = step.GeneratedMaterialPath
                    });
                }

                // ---- 3. animation remaps on generated clip/controller copies
                var clipByOriginalPath = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < plan.ControllerClones.Count; i++)
                {
                    RemapController(plan.ControllerClones[i], avatar, outcome, cloneByGuid, clipByOriginalPath, rollback);
                }

                if (plan.ControllerClones.Count > 0)
                {
                    AssetDatabase.SaveAssets();
                }
            }
            catch (Exception ex)
            {
                outcome.ErrorKey = "apply.error.applyFailed";
                // Exception.ToString() carries the full stack trace into the Console.
                Debug.LogError("[AvatarTuner] scene application failed: " + ex);
                rollback.RestoreAll();
                outcome.RendererBindings.Clear();
                outcome.AnimationBindings.Clear();
                outcome.AssignedSlots = 0;
                outcome.ClonedControllers = 0;
                return;
            }

            if (!string.IsNullOrEmpty(outcome.ErrorKey) && rollback.HasChanges)
            {
                rollback.RestoreAll();
                outcome.RendererBindings.Clear();
                outcome.AnimationBindings.Clear();
                outcome.AssignedSlots = 0;
                outcome.ClonedControllers = 0;
            }

            // The avatar's scene changed; without this the user could lose the applied
            // references by simply not noticing the scene is unsaved.
            if (rollback.HasChanges)
            {
                EditorSceneManager.MarkSceneDirty(avatar.scene);
            }
        }

        /// <summary>
        /// Clones the clips that carry material-swap curves (remapped to the material
        /// clones), clones the controller, repoints its motions, then assigns the cloned
        /// controller to the animator with Undo. The original clips and controllers are
        /// read-only.
        /// </summary>
        private static void RemapController(
            ControllerCloneStep step,
            GameObject avatar,
            SceneApplicationOutcome outcome,
            Dictionary<string, Material> cloneByGuid,
            Dictionary<string, AnimationClip> clipByOriginalPath,
            RollbackTracker rollback)
        {
            var clipMap = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < step.Clips.Count; i++)
            {
                var clipStep = step.Clips[i];
                AnimationClip clone;
                if (!clipByOriginalPath.TryGetValue(clipStep.OriginalClipPath, out clone))
                {
                    // Re-run safe: an existing clone is reused and re-remapped in place.
                    clone = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipStep.GeneratedClipPath);
                    if (clone == null)
                    {
                        if (!AssetDatabase.CopyAsset(clipStep.OriginalClipPath, clipStep.GeneratedClipPath))
                        {
                            outcome.Partial = true;
                            outcome.Findings.Add(new Build.PlanFinding(
                                "apply.warn.animUnresolvable", clipStep.OriginalClipPath));
                            continue;
                        }

                        clone = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipStep.GeneratedClipPath);
                        if (clone == null)
                        {
                            outcome.Partial = true;
                            continue;
                        }
                    }

                    var remapped = RemapClipMaterialReferences(clone, cloneByGuid);
                    outcome.ClonedClips++;
                    clipByOriginalPath[clipStep.OriginalClipPath] = clone;

                    var generated = new GeneratedAssetState
                    {
                        Path = clipStep.GeneratedClipPath,
                        Kind = "animation",
                        Guid = AssetDatabase.AssetPathToGUID(clipStep.GeneratedClipPath)
                    };
                    outcome.ExtraGenerated.Add(generated);

                    outcome.AnimationBindings.Add(new AppliedAnimationBinding
                    {
                        OriginalClipPath = clipStep.OriginalClipPath,
                        GeneratedClipPath = clipStep.GeneratedClipPath,
                        OriginalControllerPath = step.OriginalControllerPath,
                        GeneratedControllerPath = step.GeneratedControllerPath,
                        AnimatorPath = step.AnimatorPath,
                        RemappedReferenceCount = remapped
                    });
                }

                clipMap[clipStep.OriginalClipPath] = clone;
            }

            if (clipMap.Count == 0)
            {
                return;
            }

            RuntimeAnimatorController finalClonedRuntimeController = null;
            var isOverride = step.OriginalControllerPath.EndsWith(".overrideController", StringComparison.OrdinalIgnoreCase);
            if (isOverride)
            {
                var overrideClone = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(step.GeneratedControllerPath);
                if (overrideClone == null)
                {
                    if (!AssetDatabase.CopyAsset(step.OriginalControllerPath, step.GeneratedControllerPath))
                    {
                        outcome.Partial = true;
                        outcome.Findings.Add(new Build.PlanFinding(
                            "apply.warn.animUnresolvable", step.OriginalControllerPath));
                        return;
                    }

                    overrideClone = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(step.GeneratedControllerPath);
                    if (overrideClone == null)
                    {
                        outcome.Partial = true;
                        return;
                    }
                }

                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                overrideClone.GetOverrides(overrides);
                var updatedOverrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrides.Count);
                for (var o = 0; o < overrides.Count; o++)
                {
                    var origClip = overrides[o].Key;
                    var currentOverride = overrides[o].Value != null ? overrides[o].Value : origClip;
                    AnimationClip remappedClip;
                    var origPath = AssetDatabase.GetAssetPath(currentOverride);
                    if (!string.IsNullOrEmpty(origPath) && clipMap.TryGetValue(origPath, out remappedClip) && remappedClip != null)
                    {
                        updatedOverrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(origClip, remappedClip));
                    }
                    else
                    {
                        updatedOverrides.Add(overrides[o]);
                    }
                }
                overrideClone.ApplyOverrides(updatedOverrides);
                finalClonedRuntimeController = overrideClone;
            }
            else
            {
                var controllerClone = AssetDatabase.LoadAssetAtPath<AnimatorController>(step.GeneratedControllerPath);
                if (controllerClone == null)
                {
                    if (!AssetDatabase.CopyAsset(step.OriginalControllerPath, step.GeneratedControllerPath))
                    {
                        outcome.Partial = true;
                        outcome.Findings.Add(new Build.PlanFinding(
                            "apply.warn.animUnresolvable", step.OriginalControllerPath));
                        return;
                    }

                    controllerClone = AssetDatabase.LoadAssetAtPath<AnimatorController>(step.GeneratedControllerPath);
                    if (controllerClone == null)
                    {
                        outcome.Partial = true;
                        return;
                    }
                }

                var layers = controllerClone.layers;
                for (var i = 0; i < layers.Length; i++)
                {
                    RemapStateMachine(layers[i].stateMachine, clipMap);
                }
                finalClonedRuntimeController = controllerClone;
            }

            var controllerGenerated = new GeneratedAssetState
            {
                Path = step.GeneratedControllerPath,
                Kind = "animation",
                Guid = AssetDatabase.AssetPathToGUID(step.GeneratedControllerPath)
            };
            outcome.ExtraGenerated.Add(controllerGenerated);
            outcome.ClonedControllers++;

            var animator = FindByHierarchyPath<Animator>(avatar, step.AnimatorPath);
            if (animator != null)
            {
                Undo.RecordObject(animator, "AvatarTuner: Apply remapped controller");
                rollback.Track(animator, animator.runtimeAnimatorController);
                animator.runtimeAnimatorController = finalClonedRuntimeController;
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
            }
            else
            {
                // Check if the controller is bound on an AvatarDescriptor component (e.g. VRChat FX layer)
                var descriptor = FindAvatarDescriptor(avatar, step.AnimatorPath);
                if (descriptor != null && TryReassignDescriptorController(descriptor, step.OriginalControllerPath, finalClonedRuntimeController, rollback))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(descriptor);
                }
                else
                {
                    outcome.Partial = true;
                    outcome.Findings.Add(new Build.PlanFinding(
                        "apply.warn.animUnresolvable", step.AnimatorPath));
                    return;
                }
            }
        }

        private static void RemapStateMachine(
            AnimatorStateMachine stateMachine, Dictionary<string, AnimationClip> clipMap)
        {
            if (stateMachine == null)
            {
                return;
            }

            var states = stateMachine.states;
            for (var i = 0; i < states.Length; i++)
            {
                var state = states[i].state;
                if (state != null)
                {
                    state.motion = RemapMotion(state.motion, clipMap);
                }
            }

            var nested = stateMachine.stateMachines;
            for (var i = 0; i < nested.Length; i++)
            {
                RemapStateMachine(nested[i].stateMachine, clipMap);
            }
        }

        private static Motion RemapMotion(Motion motion, Dictionary<string, AnimationClip> clipMap)
        {
            var clip = motion as AnimationClip;
            if (clip != null)
            {
                AnimationClip clone;
                var path = AssetDatabase.GetAssetPath(clip);
                if (clipMap.TryGetValue(path ?? string.Empty, out clone) && clone != null)
                {
                    return clone;
                }

                return motion;
            }

            var tree = motion as BlendTree;
            if (tree != null)
            {
                var children = tree.children;
                for (var i = 0; i < children.Length; i++)
                {
                    children[i].motion = RemapMotion(children[i].motion, clipMap);
                }

                tree.children = children;
            }

            return motion;
        }

        /// <summary>Rewrites a cloned clip's material object-reference curves to the clones.</summary>
        private static int RemapClipMaterialReferences(
            AnimationClip clone, Dictionary<string, Material> cloneByGuid)
        {
            var remapped = 0;
            var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clone);
            for (var b = 0; b < bindings.Length; b++)
            {
                if (!AvatarScanner.IsMaterialBinding(bindings[b]))
                {
                    continue;
                }

                var keys = AnimationUtility.GetObjectReferenceCurve(clone, bindings[b]);
                var changed = false;
                for (var k = 0; k < keys.Length; k++)
                {
                    var material = keys[k].value as Material;
                    if (material == null)
                    {
                        continue;
                    }

                    var path = AssetDatabase.GetAssetPath(material);
                    var guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
                    Material replacement;
                    if (cloneByGuid.TryGetValue(guid, out replacement) && replacement != null)
                    {
                        keys[k].value = replacement;
                        changed = true;
                        remapped++;
                    }
                }

                if (changed)
                {
                    AnimationUtility.SetObjectReferenceCurve(clone, bindings[b], keys);
                }
            }

            return remapped;
        }

        // ===================================================================== restore

        /// <summary>
        /// Restores the recorded original material and controller references. Undo-recorded;
        /// generated files are kept (they may be re-applied later), source assets untouched.
        /// </summary>
        public static SceneApplicationOutcome Restore(GameObject avatar, AvatarOptimizationState recorded)
        {
            var outcome = new SceneApplicationOutcome();
            if (avatar == null || recorded == null)
            {
                outcome.ErrorKey = "apply.error.applyFailed";
                return outcome;
            }

            try
            {
                var totalTargets = recorded.RendererBindings.Count + recorded.AnimationBindings.Count;
                var restoredCount = 0;
                var remainingRendererBindings = new List<AppliedRendererBinding>();

                for (var i = 0; i < recorded.RendererBindings.Count; i++)
                {
                    var binding = recorded.RendererBindings[i];
                    var renderer = FindByHierarchyPath<Renderer>(avatar, binding.RendererPath);
                    if (renderer == null)
                    {
                        outcome.Partial = true;
                        outcome.Findings.Add(new Build.PlanFinding("restore.warn.rendererMissing", binding.RendererPath));
                        remainingRendererBindings.Add(binding);
                        continue;
                    }

                    var original = AssetDatabase.LoadAssetAtPath<Material>(
                        AssetDatabase.GUIDToAssetPath(binding.OriginalMaterialGuid));
                    if (original == null)
                    {
                        outcome.Partial = true;
                        outcome.Findings.Add(new Build.PlanFinding("restore.warn.materialMissing", binding.OriginalMaterialGuid));
                        remainingRendererBindings.Add(binding);
                        continue;
                    }

                    var materials = renderer.sharedMaterials;
                    if (binding.SlotIndex < 0 || binding.SlotIndex >= materials.Length)
                    {
                        outcome.Partial = true;
                        remainingRendererBindings.Add(binding);
                        continue;
                    }

                    Undo.RecordObject(renderer, "AvatarTuner: Restore original materials");
                    materials[binding.SlotIndex] = original;
                    renderer.sharedMaterials = materials;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    outcome.AssignedSlots++;
                    restoredCount++;
                }

                var remainingAnimationBindings = new List<AppliedAnimationBinding>();
                for (var i = 0; i < recorded.AnimationBindings.Count; i++)
                {
                    var binding = recorded.AnimationBindings[i];
                    if (string.IsNullOrEmpty(binding.OriginalControllerPath))
                    {
                        continue;
                    }

                    var original = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        binding.OriginalControllerPath);
                    if (original == null)
                    {
                        outcome.Partial = true;
                        outcome.Findings.Add(new Build.PlanFinding("restore.warn.controllerMissing", binding.OriginalControllerPath));
                        remainingAnimationBindings.Add(binding);
                        continue;
                    }

                    var animator = FindByHierarchyPath<Animator>(avatar, binding.AnimatorPath);
                    if (animator != null)
                    {
                        Undo.RecordObject(animator, "AvatarTuner: Restore original controller");
                        animator.runtimeAnimatorController = original;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                        outcome.ClonedControllers++;
                        restoredCount++;
                    }
                    else
                    {
                        // Check if it was bound on an AvatarDescriptor
                        var descriptor = FindAvatarDescriptor(avatar, binding.AnimatorPath);
                        if (descriptor != null && TryReassignDescriptorController(descriptor, binding.GeneratedControllerPath, original, null))
                        {
                            PrefabUtility.RecordPrefabInstancePropertyModifications(descriptor);
                            outcome.ClonedControllers++;
                            restoredCount++;
                        }
                        else
                        {
                            outcome.Partial = true;
                            outcome.Findings.Add(new Build.PlanFinding("restore.warn.animatorMissing", binding.AnimatorPath));
                            remainingAnimationBindings.Add(binding);
                        }
                    }
                }

                if (totalTargets > 0 && restoredCount == 0)
                {
                    outcome.Success = false;
                    outcome.ErrorKey = "apply.error.applyFailed";
                }
                else
                {
                    outcome.Success = true;
                    recorded.RendererBindings.Clear();
                    recorded.RendererBindings.AddRange(remainingRendererBindings);
                    recorded.AnimationBindings.Clear();
                    recorded.AnimationBindings.AddRange(remainingAnimationBindings);
                }

                return outcome;
            }
            catch (Exception ex)
            {
                outcome.ErrorKey = "apply.error.applyFailed";
                Debug.LogError("[AvatarTuner] restore failed: " + ex);
                return outcome;
            }
        }

        // ===================================================================== observation

        /// <summary>
        /// Observes the avatar's current renderer material slots and texture references.
        /// Cheap (no asset loads beyond what the scene already references); invoked on
        /// selection, refresh, analyze and optimize - never per frame.
        /// </summary>
        public static void GatherObserved(
            GameObject avatar,
            List<ObservedSlot> slots,
            HashSet<string> observedTextureGuids)
        {
            if (avatar == null)
            {
                return;
            }

            var renderers = avatar.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var path = HierarchyPathOf(renderers[i].transform, avatar.transform);
                var materials = renderers[i].sharedMaterials;
                for (var s = 0; s < materials.Length; s++)
                {
                    var material = materials[s];
                    var materialGuid = string.Empty;
                    if (material != null)
                    {
                        var materialPath = AssetDatabase.GetAssetPath(material);
                        materialGuid = string.IsNullOrEmpty(materialPath)
                            ? string.Empty
                            : AssetDatabase.AssetPathToGUID(materialPath);
                    }

                    slots.Add(new ObservedSlot
                    {
                        RendererPath = path,
                        SlotIndex = s,
                        MaterialGuid = materialGuid
                    });

                    if (material == null || observedTextureGuids == null)
                    {
                        continue;
                    }

                    var properties = material.GetTexturePropertyNames();
                    for (var p = 0; p < properties.Length; p++)
                    {
                        var texture = material.GetTexture(properties[p]);
                        if (texture == null)
                        {
                            continue;
                        }

                        var texturePath = AssetDatabase.GetAssetPath(texture);
                        if (!string.IsNullOrEmpty(texturePath))
                        {
                            observedTextureGuids.Add(AssetDatabase.AssetPathToGUID(texturePath));
                        }
                    }
                }
            }
        }

        // ===================================================================== gathering helpers

        private static void BuildTextureMap(TextureGenerationResult generation, ApplicationInput input)
        {
            for (var i = 0; i < generation.Generated.Count; i++)
            {
                var generated = generation.Generated[i];
                if (string.IsNullOrEmpty(generated.SourceGuid) || string.IsNullOrEmpty(generated.GeneratedPath))
                {
                    continue;
                }

                if (input.GeneratedTexturePathBySourceGuid.ContainsKey(generated.SourceGuid) &&
                    !string.Equals(input.GeneratedTexturePathBySourceGuid[generated.SourceGuid],
                        generated.GeneratedPath, StringComparison.OrdinalIgnoreCase))
                {
                    input.AmbiguousSourceGuids.Add(generated.SourceGuid);
                    input.GeneratedTexturePathBySourceGuid.Remove(generated.SourceGuid);
                    continue;
                }

                input.GeneratedTexturePathBySourceGuid[generated.SourceGuid] = generated.GeneratedPath;
                if (!File.Exists(generated.GeneratedPath))
                {
                    input.MissingGeneratedPaths.Add(generated.GeneratedPath);
                }
            }
        }

        private static void GatherAnimationMaterials(GameObject avatar, ApplicationInput input)
        {
            var animators = avatar.GetComponentsInChildren<Animator>(true);
            var seenControllers = new HashSet<RuntimeAnimatorController>();

            for (var i = 0; i < animators.Length; i++)
            {
                var controller = animators[i].runtimeAnimatorController;
                if (controller == null)
                {
                    continue;
                }

                seenControllers.Add(controller);
                var controllerPath = AssetDatabase.GetAssetPath(controller);
                var animatorPath = HierarchyPathOf(animators[i].transform, avatar.transform);
                GatherControllerAnimationMaterials(controller, controllerPath, animatorPath, input);
            }

            var descriptorControllers = AvatarScanner.CollectDescriptorControllers(avatar);
            var rootPath = HierarchyPathOf(avatar.transform, avatar.transform);
            for (var d = 0; d < descriptorControllers.Count; d++)
            {
                var controller = descriptorControllers[d];
                if (controller == null || seenControllers.Contains(controller))
                {
                    continue;
                }

                seenControllers.Add(controller);
                var controllerPath = AssetDatabase.GetAssetPath(controller);
                GatherControllerAnimationMaterials(controller, controllerPath, rootPath, input);
            }
        }

        private static void GatherControllerAnimationMaterials(
            RuntimeAnimatorController controller,
            string controllerPath,
            string animatorPath,
            ApplicationInput input)
        {
            foreach (var clip in ClipsOf(controller))
            {
                if (clip == null)
                {
                    continue;
                }

                var clipPath = AssetDatabase.GetAssetPath(clip);
                var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                for (var b = 0; b < bindings.Length; b++)
                {
                    if (!AvatarScanner.IsMaterialBinding(bindings[b]))
                    {
                        continue;
                    }

                    var keys = AnimationUtility.GetObjectReferenceCurve(clip, bindings[b]);
                    for (var k = 0; k < keys.Length; k++)
                    {
                        var material = keys[k].value as Material;
                        if (material == null)
                        {
                            continue;
                        }

                        var materialPath = AssetDatabase.GetAssetPath(material);
                        input.AnimationMaterials.Add(new AnimationMaterialRow
                        {
                            ClipPath = clipPath,
                            ControllerPath = controllerPath,
                            AnimatorPath = animatorPath,
                            MaterialGuid = string.IsNullOrEmpty(materialPath)
                                ? string.Empty
                                : AssetDatabase.AssetPathToGUID(materialPath)
                        });
                    }
                }
            }
        }

        private static Component FindAvatarDescriptor(GameObject avatar, string path)
        {
            var target = FindByHierarchyPath<Transform>(avatar, path);
            var go = target != null ? target.gameObject : avatar;
            if (go == null)
            {
                return null;
            }

            var components = go.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] != null &&
                    components[i].GetType().Name.IndexOf("AvatarDescriptor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return components[i];
                }
            }

            return null;
        }

        private static bool TryReassignDescriptorController(
            Component descriptor,
            string originalControllerPath,
            RuntimeAnimatorController newController,
            RollbackTracker rollback)
        {
            if (descriptor == null)
            {
                return false;
            }

            var type = descriptor.GetType();
            var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            for (var f = 0; f < fields.Length; f++)
            {
                var field = fields[f];
                if (field.Name == "baseAnimationLayers" || field.Name == "specialAnimationLayers")
                {
                    var array = field.GetValue(descriptor) as Array;
                    if (array == null)
                    {
                        continue;
                    }

                    for (var a = 0; a < array.Length; a++)
                    {
                        var layer = array.GetValue(a);
                        if (layer == null)
                        {
                            continue;
                        }

                        var ctrlField = layer.GetType().GetField("animatorController",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (ctrlField != null)
                        {
                            var currentCtrl = ctrlField.GetValue(layer) as RuntimeAnimatorController;
                            var currentPath = AssetDatabase.GetAssetPath(currentCtrl);
                            if (string.Equals(currentPath, originalControllerPath, StringComparison.OrdinalIgnoreCase))
                            {
                                Undo.RecordObject(descriptor, "AvatarTuner: Apply remapped controller");
                                if (rollback != null)
                                {
                                    rollback.TrackDescriptor(descriptor, field.Name, a, currentCtrl);
                                }

                                ctrlField.SetValue(layer, newController);
                                array.SetValue(layer, a);
                                field.SetValue(descriptor, array);
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private static IEnumerable<AnimationClip> ClipsOf(RuntimeAnimatorController controller)
        {
            var overrideController = controller as AnimatorOverrideController;
            if (overrideController != null)
            {
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                overrideController.GetOverrides(pairs);
                for (var i = 0; i < pairs.Count; i++)
                {
                    yield return pairs[i].Value != null ? pairs[i].Value : pairs[i].Key;
                }
            }

            var path = AssetDatabase.GetAssetPath(controller);
            if (string.IsNullOrEmpty(path))
            {
                yield break;
            }

            var dependencies = AssetDatabase.GetDependencies(path, true);
            for (var i = 0; i < dependencies.Length; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(dependencies[i]);
                if (clip != null)
                {
                    yield return clip;
                }
            }
        }

        private static T FindByHierarchyPath<T>(GameObject root, string path) where T : Component
        {
            if (root == null || string.IsNullOrEmpty(path))
            {
                return null;
            }

            var segments = path.Split('/');
            var current = root.transform;
            var start = segments.Length > 0 && segments[0] == root.name ? 1 : 0;
            for (var i = start; i < segments.Length; i++)
            {
                Transform next = null;
                for (var c = 0; c < current.childCount; c++)
                {
                    var child = current.GetChild(c);
                    if (child.name == segments[i])
                    {
                        next = child;
                        break;
                    }
                }

                if (next == null)
                {
                    return null;
                }

                current = next;
            }

            return current.GetComponent<T>();
        }

        private static string HierarchyPathOf(Transform target, Transform root)
        {
            if (target == null || root == null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            var current = target;
            while (current != null)
            {
                parts.Insert(0, current.name);
                if (current == root)
                {
                    break;
                }

                current = current.parent;
            }

            return string.Join("/", parts.ToArray());
        }
    }
}
