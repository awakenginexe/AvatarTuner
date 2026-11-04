// AvatarTuner v1.0.0 — direct scene application planner (Unity free).
//
// Decides how optimized textures reach the SELECTED SCENE AVATAR. The rules are pure and
// unit tested; the Unity applier (SceneTextureApplier) only gathers these inputs from the
// scene and executes the plan with Undo support.
//   - Source assets are never modified: materials are replaced by generated CLONES whose
//     texture slots point at the generated optimized textures.
//   - No renderer, mesh, bone or avatar root is ever created or duplicated: the plan only
//     reassigns existing material slots.
//   - Material-swap animations are remapped on generated clip/controller COPIES; whatever
//     cannot be remapped safely is reported and marks the result partial - never silently
//     skipped, never claimed as fully applied.
//   - The same plan is idempotent: deterministic generated paths mean re-optimization
//     overwrites in place instead of piling up duplicates.

using System;
using System.Collections.Generic;
using AvatarTuner.Core;

namespace AvatarTuner.Editor.Apply
{
    /// <summary>What a scene application plan can do, and how honestly it can claim it.</summary>
    public enum ApplicationVerdict
    {
        /// <summary>Every material/animation reference can be remapped safely.</summary>
        Ready = 0,

        /// <summary>Applicable, with informational findings.</summary>
        ReadyWithWarnings,

        /// <summary>Some references cannot be remapped safely - the result is partial.</summary>
        PartiallyRemappable,

        /// <summary>No texture remap is needed (nothing to do).</summary>
        NothingToApply
    }

    /// <summary>One texture slot remap inside a material clone.</summary>
    public sealed class TextureSlotRemap
    {
        public string Property;
        public string SourceTextureGuid;
        public string GeneratedTexturePath;
    }

    /// <summary>One material to clone (source asset untouched) with its texture remaps.</summary>
    public sealed class MaterialCloneStep
    {
        public string OriginalMaterialGuid;
        public string OriginalMaterialPath;
        public string MaterialName;
        public string GeneratedMaterialPath;
        public readonly List<TextureSlotRemap> Slots = new List<TextureSlotRemap>();
    }

    /// <summary>One renderer slot to repoint from an original material to its clone.</summary>
    public sealed class RendererAssignStep
    {
        public string RendererPath;
        public int SlotIndex;
        public string OriginalMaterialGuid;
        public string GeneratedMaterialPath;
        public bool RendererIsActive;
    }

    /// <summary>One animation clip to clone with its material references remapped.</summary>
    public sealed class ClipRemapStep
    {
        public string OriginalClipPath;
        public string GeneratedClipPath;
        public int RemapCount;
    }

    /// <summary>One animator controller to clone; states then reference the cloned clips.</summary>
    public sealed class ControllerCloneStep
    {
        public string OriginalControllerPath;
        public string GeneratedControllerPath;
        public string AnimatorPath;
        public readonly List<ClipRemapStep> Clips = new List<ClipRemapStep>();
    }

    /// <summary>Inputs the Unity applier gathers from the scene (pure data form).</summary>
    public sealed class ApplicationInput
    {
        /// <summary>Generated texture path per source texture GUID (from the run).</summary>
        public readonly Dictionary<string, string> GeneratedTexturePathBySourceGuid =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Source GUIDs with conflicting generated outputs (never guessed).</summary>
        public readonly HashSet<string> AmbiguousSourceGuids =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Generated texture paths missing on disk.</summary>
        public readonly HashSet<string> MissingGeneratedPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every texture slot of every material used by the avatar.</summary>
        public readonly List<TextureSlotRow> TextureSlots = new List<TextureSlotRow>();

        /// <summary>Every renderer material slot on the avatar (active and inactive).</summary>
        public readonly List<RendererSlotRow> RendererSlots = new List<RendererSlotRow>();

        /// <summary>Materials referenced by material-swap object-reference curves.</summary>
        public readonly List<AnimationMaterialRow> AnimationMaterials = new List<AnimationMaterialRow>();
    }

    public sealed class TextureSlotRow
    {
        public string MaterialGuid;
        public string MaterialName;
        public string MaterialPath;
        public string Property;
        public string SourceTextureGuid;
    }

    public sealed class RendererSlotRow
    {
        public string RendererPath;
        public int SlotIndex;
        public string MaterialGuid;
        public bool IsActive;
    }

    /// <summary>One material referenced by one clip of one controller on one animator.</summary>
    public sealed class AnimationMaterialRow
    {
        public string ClipPath;
        public string ControllerPath;
        public string AnimatorPath;
        public string MaterialGuid;
    }

    public sealed class ApplicationPlan
    {
        public ApplicationVerdict Verdict;
        public readonly List<MaterialCloneStep> MaterialClones = new List<MaterialCloneStep>();
        public readonly List<RendererAssignStep> RendererAssignments = new List<RendererAssignStep>();
        public readonly List<ControllerCloneStep> ControllerClones = new List<ControllerCloneStep>();
        public readonly List<Build.PlanFinding> Findings = new List<Build.PlanFinding>();

        /// <summary>
        /// Every destination folder the applier must create BEFORE copying clone assets.
        /// AssetDatabase.CopyAsset fails when the destination folder does not exist, so
        /// these are a mandatory preflight - the original root cause of the "copy failed"
        /// regression.
        /// </summary>
        public readonly List<string> RequiredFolders = new List<string>();

        /// <summary>Always false: the plan only reassigns material slots.</summary>
        public bool CreatesRenderersOrMeshes
        {
            get { return false; }
        }
    }

    /// <summary>What a scene application (or restore) produced.</summary>
    public sealed class SceneApplicationOutcome
    {
        /// <summary>True when the pass ran without fatal failure (no ErrorKey).</summary>
        public bool Success;

        /// <summary>True when some references could not be safely remapped.</summary>
        public bool Partial;

        /// <summary>Localized error key when the application failed, or empty.</summary>
        public string ErrorKey;

        public int AssignedSlots;
        public int ClonedMaterials;
        public int ClonedClips;
        public int ClonedControllers;

        public readonly List<AppliedRendererBinding> RendererBindings = new List<AppliedRendererBinding>();
        public readonly List<AppliedMaterialClone> MaterialClones = new List<AppliedMaterialClone>();
        public readonly List<AppliedAnimationBinding> AnimationBindings = new List<AppliedAnimationBinding>();
        public readonly List<GeneratedAssetState> ExtraGenerated = new List<GeneratedAssetState>();
        public readonly List<Build.PlanFinding> Findings = new List<Build.PlanFinding>();
    }

    /// <summary>One observed renderer material slot on the scene avatar.</summary>
    public sealed class ObservedSlot
    {
        public string RendererPath;
        public int SlotIndex;
        public string MaterialGuid;
    }

    /// <summary>Reference-level health of a direct scene application.</summary>
    public enum ApplicationHealth
    {
        NotApplied = 0,
        Applied,
        PartiallyApplied,

        /// <summary>Some recorded reference was changed (reverted, reassigned, removed).</summary>
        Drifted
    }

    public static class ApplicationPlanner
    {
        // ===================================================================== plan

        /// <summary>
        /// Builds the scene application plan. Deterministic; no Unity APIs, no side effects.
        /// </summary>
        public static ApplicationPlan Plan(ApplicationInput input, string generatedRootFolder)
        {
            if (input == null)
            {
                throw new ArgumentNullException("input");
            }

            var plan = new ApplicationPlan();

            // ---- material clones: one per material with at least one remappable texture
            var cloneByMaterial = new Dictionary<string, MaterialCloneStep>(StringComparer.OrdinalIgnoreCase);
            var warnedAmbiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < input.TextureSlots.Count; i++)
            {
                var slot = input.TextureSlots[i];
                if (string.IsNullOrEmpty(slot.SourceTextureGuid))
                {
                    continue;
                }

                if (input.AmbiguousSourceGuids.Contains(slot.SourceTextureGuid))
                {
                    if (warnedAmbiguous.Add(slot.SourceTextureGuid))
                    {
                        plan.Findings.Add(new Build.PlanFinding(
                            "build.warn.ambiguousMapping", slot.MaterialName, slot.SourceTextureGuid));
                    }

                    continue;
                }

                string generatedTexture;
                if (!input.GeneratedTexturePathBySourceGuid.TryGetValue(
                        slot.SourceTextureGuid, out generatedTexture))
                {
                    continue; // not optimized: unchanged is the normal case
                }

                if (string.IsNullOrEmpty(generatedTexture) ||
                    input.MissingGeneratedPaths.Contains(generatedTexture))
                {
                    plan.Findings.Add(new Build.PlanFinding(
                        "build.warn.missingGenerated", slot.MaterialName, generatedTexture ?? string.Empty));
                    continue;
                }

                MaterialCloneStep clone;
                if (!cloneByMaterial.TryGetValue(slot.MaterialGuid, out clone))
                {
                    clone = new MaterialCloneStep
                    {
                        OriginalMaterialGuid = slot.MaterialGuid,
                        OriginalMaterialPath = slot.MaterialPath,
                        MaterialName = slot.MaterialName,
                        GeneratedMaterialPath = MaterialClonePath(generatedRootFolder, slot)
                    };
                    cloneByMaterial[slot.MaterialGuid] = clone;
                }

                clone.Slots.Add(new TextureSlotRemap
                {
                    Property = slot.Property,
                    SourceTextureGuid = slot.SourceTextureGuid,
                    GeneratedTexturePath = generatedTexture
                });
            }

            foreach (var pair in cloneByMaterial)
            {
                plan.MaterialClones.Add(pair.Value);
            }

            // ---- renderer assignments: every slot (active or inactive) using a cloned material
            for (var i = 0; i < input.RendererSlots.Count; i++)
            {
                var slot = input.RendererSlots[i];
                MaterialCloneStep clone;
                if (!cloneByMaterial.TryGetValue(slot.MaterialGuid ?? string.Empty, out clone))
                {
                    continue;
                }

                plan.RendererAssignments.Add(new RendererAssignStep
                {
                    RendererPath = slot.RendererPath,
                    SlotIndex = slot.SlotIndex,
                    OriginalMaterialGuid = slot.MaterialGuid,
                    GeneratedMaterialPath = clone.GeneratedMaterialPath,
                    RendererIsActive = slot.IsActive
                });
            }

            // ---- animation references: only clips/controllers that are real assets can be
            // cloned; anything else is reported and the avatar is marked partial.
            var clipsByPath = new Dictionary<string, ClipRemapStep>(StringComparer.OrdinalIgnoreCase);
            var controllersByPath = new Dictionary<string, ControllerCloneStep>(StringComparer.OrdinalIgnoreCase);
            var warnedAnimMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < input.AnimationMaterials.Count; i++)
            {
                var row = input.AnimationMaterials[i];
                MaterialCloneStep clone;
                if (!cloneByMaterial.TryGetValue(row.MaterialGuid ?? string.Empty, out clone))
                {
                    continue; // the animated material needs no remap - nothing to do
                }

                if (string.IsNullOrEmpty(row.ClipPath) || string.IsNullOrEmpty(row.ControllerPath))
                {
                    if (warnedAnimMaterials.Add(row.MaterialGuid))
                    {
                        plan.Findings.Add(new Build.PlanFinding(
                            "apply.warn.animUnresolvable", clone.MaterialName));
                    }

                    continue;
                }

                ClipRemapStep clip;
                if (!clipsByPath.TryGetValue(row.ClipPath, out clip))
                {
                    clip = new ClipRemapStep
                    {
                        OriginalClipPath = row.ClipPath,
                        GeneratedClipPath = ClonePath(generatedRootFolder, "Animations", row.ClipPath)
                    };
                    clipsByPath[row.ClipPath] = clip;
                }

                clip.RemapCount++;

                ControllerCloneStep controller;
                if (!controllersByPath.TryGetValue(row.ControllerPath, out controller))
                {
                    controller = new ControllerCloneStep
                    {
                        OriginalControllerPath = row.ControllerPath,
                        GeneratedControllerPath = ClonePath(generatedRootFolder, "Animations", row.ControllerPath),
                        AnimatorPath = row.AnimatorPath
                    };
                    controllersByPath[row.ControllerPath] = controller;
                }

                if (!controller.Clips.Contains(clip))
                {
                    controller.Clips.Add(clip);
                }
            }

            foreach (var pair in controllersByPath)
            {
                plan.ControllerClones.Add(pair.Value);
            }

            var hasWarnings = false;
            for (var i = 0; i < plan.Findings.Count; i++)
            {
                var key = plan.Findings[i].MessageKey;
                if (key.StartsWith("apply.warn.", StringComparison.Ordinal) ||
                    key.StartsWith("build.warn.", StringComparison.Ordinal))
                {
                    hasWarnings = true;
                    break;
                }
            }

            if (plan.MaterialClones.Count == 0 && plan.ControllerClones.Count == 0 && !hasWarnings)
            {
                plan.Verdict = ApplicationVerdict.NothingToApply;
                return plan;
            }

            // A failed required replacement (missing/ambiguous output) must never look like
            // "nothing to do" or a clean success: warnings make it partial.
            plan.Verdict = hasWarnings
                ? ApplicationVerdict.PartiallyRemappable
                : ApplicationVerdict.Ready;

            // Preflight contract: every folder the applier copies clone assets into must
            // exist first - AssetDatabase.CopyAsset fails on missing destination folders.
            if (plan.MaterialClones.Count > 0)
            {
                plan.RequiredFolders.Add(generatedRootFolder + "/Materials");
            }

            if (plan.ControllerClones.Count > 0)
            {
                plan.RequiredFolders.Add(generatedRootFolder + "/Animations");
            }

            return plan;
        }

        // ===================================================================== restore

        /// <summary>
        /// The restore plan: every recorded renderer slot goes back to its original
        /// material and every animator back to its original controller. Generated files
        /// are kept; originals are only referenced, never modified.
        /// </summary>
        public static List<RendererAssignStep> BuildRestoreAssignments(AvatarOptimizationState recorded)
        {
            var steps = new List<RendererAssignStep>();
            if (recorded == null)
            {
                return steps;
            }

            for (var i = 0; i < recorded.RendererBindings.Count; i++)
            {
                var binding = recorded.RendererBindings[i];
                steps.Add(new RendererAssignStep
                {
                    RendererPath = binding.RendererPath,
                    SlotIndex = binding.SlotIndex,
                    OriginalMaterialGuid = binding.OriginalMaterialGuid,
                    GeneratedMaterialPath = string.Empty // empty = restore the original
                });
            }

            return steps;
        }

        // ===================================================================== evaluation

        /// <summary>
        /// Reference-level health of the scene application: compares what the scene avatar
        /// references NOW with what was recorded at application time. Unrelated changes
        /// (moving the model, editing other components) never appear here because only
        /// material references are compared.
        /// </summary>
        public static ApplicationHealth Evaluate(
            AvatarOptimizationState recorded,
            IList<ObservedSlot> observedSlots,
            List<string> changedEntries)
        {
            if (recorded == null || !recorded.AppliedToAvatar)
            {
                return ApplicationHealth.NotApplied;
            }

            var health = recorded.AppliedPartially
                ? ApplicationHealth.PartiallyApplied
                : ApplicationHealth.Applied;

            var observed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (observedSlots != null)
            {
                for (var i = 0; i < observedSlots.Count; i++)
                {
                    if (!string.IsNullOrEmpty(observedSlots[i].RendererPath))
                    {
                        observed[observedSlots[i].RendererPath + "#" + observedSlots[i].SlotIndex] =
                            observedSlots[i].MaterialGuid ?? string.Empty;
                    }
                }
            }

            for (var i = 0; i < recorded.RendererBindings.Count; i++)
            {
                var binding = recorded.RendererBindings[i];
                string current;
                var key = binding.RendererPath + "#" + binding.SlotIndex;
                if (!observed.TryGetValue(key, out current))
                {
                    changedEntries.Add(binding.RendererPath);
                    health = ApplicationHealth.Drifted;
                    continue;
                }

                if (!string.Equals(current, binding.AppliedMaterialGuid, StringComparison.OrdinalIgnoreCase))
                {
                    // Reverted to the original, or reassigned to something else entirely.
                    changedEntries.Add(binding.RendererPath);
                    health = ApplicationHealth.Drifted;
                }
            }

            // A new renderer slot that reuses an ORIGINAL material (one with a generated
            // clone) means unoptimized references came back - drift, not a false alarm for
            // slots that were never optimizable.
            var recordedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var originalGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < recorded.RendererBindings.Count; i++)
            {
                recordedKeys.Add(recorded.RendererBindings[i].RendererPath + "#" +
                                 recorded.RendererBindings[i].SlotIndex);
                if (!string.IsNullOrEmpty(recorded.RendererBindings[i].OriginalMaterialGuid))
                {
                    originalGuids.Add(recorded.RendererBindings[i].OriginalMaterialGuid);
                }
            }

            if (observedSlots != null)
            {
                for (var i = 0; i < observedSlots.Count; i++)
                {
                    var slot = observedSlots[i];
                    var key = slot.RendererPath + "#" + slot.SlotIndex;
                    if (!recordedKeys.Contains(key) &&
                        originalGuids.Contains(slot.MaterialGuid ?? string.Empty))
                    {
                        if (changedEntries != null)
                        {
                            changedEntries.Add(slot.RendererPath);
                        }

                        health = ApplicationHealth.Drifted;
                    }
                }
            }

            return health;
        }

        /// <summary>
        /// Current avatar texture memory from what the avatar references RIGHT NOW:
        /// generated textures count at their planned target bytes, original textures at
        /// their source bytes. Computed, never measured; unknown references make the value
        /// unavailable rather than invented.
        /// </summary>
        public static long ComputeCurrentBytes(
            IDictionary<string, long> sourceBytesByGuid,
            IDictionary<string, long> targetBytesByGeneratedGuid,
            IEnumerable<string> observedTextureGuids,
            out bool allKnown)
        {
            allKnown = true;
            if (observedTextureGuids == null)
            {
                return 0;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var guid in observedTextureGuids)
            {
                if (string.IsNullOrEmpty(guid))
                {
                    allKnown = false;
                    continue;
                }
                if (!seen.Add(guid))
                {
                    continue;
                }

                long bytes;
                if (sourceBytesByGuid != null && sourceBytesByGuid.TryGetValue(guid, out bytes))
                {
                    total += bytes;
                    continue;
                }

                if (targetBytesByGeneratedGuid != null &&
                    targetBytesByGeneratedGuid.TryGetValue(guid, out bytes))
                {
                    total += bytes;
                    continue;
                }

                allKnown = false;
            }

            return total;
        }

        /// <summary>
        /// True only when the application verifiably reached the scene avatar (at least one
        /// renderer slot or controller was assigned). The success timestamp is only ever
        /// recorded for a verified application - never for a failed or empty one.
        /// </summary>
        public static bool IsVerifiedApplication(SceneApplicationOutcome outcome)
        {
            return outcome != null && outcome.Success &&
                   (outcome.AssignedSlots > 0 || outcome.ClonedControllers > 0);
        }

        /// <summary>
        /// Marks the recorded state as failed after an application failure: the manifest
        /// must not claim an optimized run whose application did not succeed.
        /// </summary>
        public static void MarkApplicationFailed(AvatarOptimizationState state, string errorKey)
        {
            if (state == null)
            {
                return;
            }

            state.Status = OptimizationStatus.OptimizationFailed;
            state.LastError = errorKey ?? string.Empty;
            state.LastSuccessUtc = string.Empty;
        }

        // ===================================================================== helpers

        private static string MaterialClonePath(string root, TextureSlotRow slot)
        {
            var guid = string.IsNullOrEmpty(slot.MaterialGuid)
                ? "noguid"
                : slot.MaterialGuid;
            var shortGuid = guid.Length > 8 ? guid.Substring(0, 8) : guid;
            var name = string.IsNullOrEmpty(slot.MaterialName) ? "material" : slot.MaterialName;
            return root + "/Materials/" + name + "-" + shortGuid + ".mat";
        }

        private static string ClonePath(string root, string subFolder, string originalPath)
        {
            var fileName = originalPath;
            var slash = fileName.LastIndexOf('/');
            if (slash >= 0)
            {
                fileName = fileName.Substring(slash + 1);
            }

            var dot = fileName.LastIndexOf('.');
            var stem = dot > 0 ? fileName.Substring(0, dot) : fileName;
            var ext = dot > 0 ? fileName.Substring(dot) : ".anim";
            return root + "/" + subFolder + "/" + stem + "-opt" + ext;
        }
    }
}
