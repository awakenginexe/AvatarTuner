// AvatarTuner v1.0.0 — build-time texture application planner (Unity free).
//
// Decides what the NDMF build pass may do to the AVATAR CLONE being built. The rules are
// pure and unit tested; the Unity pass only gathers inputs and executes the plan.
//   - Only material texture slots are remapped: no renderers, meshes, materials-in-scene
//     or source assets are ever created or modified.
//   - One replacement per material: renderers that shared a material keep sharing one
//     (cloned) material after the remap - no duplicated materials.
//   - Missing, stale, ambiguous or optimizer-conflicted state is never a silent success:
//     every refusal carries an actionable finding.

using System;
using System.Collections.Generic;
using AvatarTuner.Core;

namespace AvatarTuner.Editor.Build
{
    /// <summary>What the build pass is allowed / not allowed to do, and why.</summary>
    public enum BuildRemapVerdict
    {
        /// <summary>The user did not consent ("Use Optimized Textures During Build" off).</summary>
        NotEnabled = 0,

        /// <summary>Consent given, but no optimized output is recorded for this avatar.</summary>
        NoOutputs,

        /// <summary>Outputs are stale: sources changed since the run. Re-optimization required.</summary>
        SkippedStale,

        /// <summary>A known optimizer (AAO / d4rkAvatarOptimizer) would conflict; skipped.</summary>
        SkippedOptimizerConflict,

        /// <summary>Every required replacement can be applied cleanly.</summary>
        Ready,

        /// <summary>Applied, but with findings worth showing (animation limits, unknown textures).</summary>
        ReadyWithWarnings,

        /// <summary>Some required replacements cannot be applied (missing/ambiguous outputs).</summary>
        PartialFailure,

        /// <summary>The application pass itself failed (recorded by the applier).</summary>
        Failed
    }

    /// <summary>One texture slot that will be remapped on a cloned material.</summary>
    public sealed class SlotReplacement
    {
        public string Property;
        public string SourceTextureGuid;
        public string GeneratedPath;
    }

    /// <summary>One material clone with its texture slot remaps.</summary>
    public sealed class MaterialReplacement
    {
        public string MaterialId;
        public string MaterialName;

        /// <summary>True when object-reference animation swaps this material at runtime.</summary>
        public bool IsAnimationTarget;

        /// <summary>How many renderer slots reference this material (shared when &gt; 1).</summary>
        public int SharedRendererCount;

        public readonly List<SlotReplacement> Slots = new List<SlotReplacement>();
    }

    /// <summary>A localized finding (key + args) for the UI and the Unity console.</summary>
    public sealed class PlanFinding
    {
        public string MessageKey;
        public readonly List<string> Args = new List<string>();

        public PlanFinding(string messageKey, params string[] args)
        {
            MessageKey = messageKey;
            if (args != null)
            {
                Args.AddRange(args);
            }
        }
    }

    /// <summary>One texture reference of one material as gathered from the build clone.</summary>
    public sealed class MaterialTextureRef
    {
        public string Property;
        public string SourceTextureGuid;
    }

    /// <summary>One material referenced by the build avatar.</summary>
    public sealed class MaterialUsage
    {
        public string MaterialId;
        public string MaterialName;

        /// <summary>True when a material-swap animation targets this material asset.</summary>
        public bool IsAnimationTarget;

        /// <summary>Number of renderer slots using this material (shared materials included).</summary>
        public int RendererCount;

        /// <summary>True when at least one referencing renderer is on an inactive GameObject.</summary>
        public bool UsedByInactiveRenderer;

        public readonly List<MaterialTextureRef> Textures = new List<MaterialTextureRef>();
    }

    /// <summary>Everything the build pass observed, in pure data form.</summary>
    public sealed class BuildRemapRequest
    {
        /// <summary>The per-avatar "Use Optimized Textures During Build" consent.</summary>
        public bool BuildEnabled;

        /// <summary>Whether optimized outputs are recorded for this avatar.</summary>
        public bool HasRecordedOutputs;

        /// <summary>Whether AAO / d4rkAvatarOptimizer style optimizers were detected on the avatar.</summary>
        public bool OptimizerConflictDetected;

        /// <summary>
        /// Whether the optimized materials are already applied directly to the scene avatar.
        /// When true, the build pass must not remap again (double application prevention).
        /// </summary>
        public bool AlreadyApplied;

        /// <summary>Whether every recorded source dependency hash is still current.</summary>
        public bool SourcesAreUnchanged;

        /// <summary>Manifest mapping: source texture GUID → generated texture path.</summary>
        public readonly Dictionary<string, string> GeneratedPathBySourceGuid =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Source GUIDs with more than one generated output (never guessed).</summary>
        public readonly HashSet<string> AmbiguousSourceGuids =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Generated paths recorded in the manifest but missing on disk.</summary>
        public readonly HashSet<string> MissingGeneratedPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The materials referenced by the build avatar (final dependency graph).</summary>
        public readonly List<MaterialUsage> Materials = new List<MaterialUsage>();
    }

    /// <summary>The decided remaps plus findings. Contains no other operation kinds.</summary>
    public sealed class BuildRemapPlan
    {
        public BuildRemapVerdict Verdict;
        public readonly List<MaterialReplacement> Replacements = new List<MaterialReplacement>();
        public readonly List<PlanFinding> Findings = new List<PlanFinding>();

        public int TextureReplacementCount;
        public int FailedReplacementCount;
        public int UntouchedUnknownCount;

        /// <summary>
        /// Always false: this milestone is strictly texture application. No renderer or
        /// mesh is ever created, duplicated or modified by a build remap plan.
        /// </summary>
        public bool CreatesRenderersOrMeshes
        {
            get { return false; }
        }
    }

    public static class BuildTexturePlanner
    {
        /// <summary>
        /// Decides the remaps for one avatar build. Deterministic and side-effect free.
        /// </summary>
        public static BuildRemapPlan Plan(BuildRemapRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            var plan = new BuildRemapPlan();

            if (!request.BuildEnabled)
            {
                plan.Verdict = BuildRemapVerdict.NotEnabled;
                return plan;
            }

            if (request.AlreadyApplied)
            {
                // Direct scene application is active; the build clone already carries the
                // optimized materials. Remapping again would be a double application.
                plan.Verdict = BuildRemapVerdict.NotEnabled;
                return plan;
            }

            if (!request.HasRecordedOutputs)
            {
                plan.Verdict = BuildRemapVerdict.NoOutputs;
                plan.Findings.Add(new PlanFinding("build.noOutputs"));
                return plan;
            }

            if (request.OptimizerConflictDetected)
            {
                // Skipping an unsafe transformation is the safe outcome; the warning is
                // actionable (disable the optimizer or verify the build manually).
                plan.Verdict = BuildRemapVerdict.SkippedOptimizerConflict;
                plan.Findings.Add(new PlanFinding("build.warn.optimizerConflict"));
                return plan;
            }

            if (!request.SourcesAreUnchanged)
            {
                plan.Verdict = BuildRemapVerdict.SkippedStale;
                plan.Findings.Add(new PlanFinding("build.warn.reoptimizationRequired"));
                return plan;
            }

            for (var i = 0; i < request.Materials.Count; i++)
            {
                PlanMaterial(request, plan, request.Materials[i]);
            }

            if (plan.UntouchedUnknownCount > 0)
            {
                plan.Findings.Add(new PlanFinding(
                    "build.info.unknownTexturesLeft",
                    plan.UntouchedUnknownCount.ToString()));
            }

            plan.Verdict = plan.FailedReplacementCount > 0
                ? BuildRemapVerdict.PartialFailure
                : (plan.Findings.Count > 0
                    ? BuildRemapVerdict.ReadyWithWarnings
                    : BuildRemapVerdict.Ready);
            return plan;
        }

        private static void PlanMaterial(
            BuildRemapRequest request,
            BuildRemapPlan plan,
            MaterialUsage material)
        {
            var replacement = new MaterialReplacement
            {
                MaterialId = material.MaterialId,
                MaterialName = material.MaterialName,
                IsAnimationTarget = material.IsAnimationTarget,
                SharedRendererCount = material.RendererCount
            };

            for (var i = 0; i < material.Textures.Count; i++)
            {
                var textureRef = material.Textures[i];
                var sourceGuid = textureRef.SourceTextureGuid;

                if (string.IsNullOrEmpty(sourceGuid))
                {
                    // Texture generated at build time (MA/VRCFury/...) or runtime: unknown
                    // provenance, so it is left exactly as it is.
                    plan.UntouchedUnknownCount++;
                    continue;
                }

                if (request.AmbiguousSourceGuids.Contains(sourceGuid))
                {
                    plan.FailedReplacementCount++;
                    plan.Findings.Add(new PlanFinding(
                        "build.warn.ambiguousMapping", material.MaterialName, sourceGuid));
                    continue;
                }

                string generatedPath;
                if (!request.GeneratedPathBySourceGuid.TryGetValue(sourceGuid, out generatedPath))
                {
                    // Source texture was never optimized: unchanged is the normal case.
                    continue;
                }

                if (string.IsNullOrEmpty(generatedPath) ||
                    request.MissingGeneratedPaths.Contains(generatedPath))
                {
                    plan.FailedReplacementCount++;
                    plan.Findings.Add(new PlanFinding(
                        "build.warn.missingGenerated", material.MaterialName, generatedPath ?? string.Empty));
                    continue;
                }

                replacement.Slots.Add(new SlotReplacement
                {
                    Property = textureRef.Property,
                    SourceTextureGuid = sourceGuid,
                    GeneratedPath = generatedPath
                });
                plan.TextureReplacementCount++;
            }

            if (replacement.Slots.Count == 0)
            {
                return;
            }

            if (replacement.IsAnimationTarget)
            {
                // The base material is still remapped (safe); states driven by material-swap
                // animation keep referencing the original material assets.
                plan.Findings.Add(new PlanFinding("build.warn.animSwapKept", material.MaterialName));
            }

            plan.Replacements.Add(replacement);
        }

        /// <summary>
        /// Picks the manifest entry for the avatar being built. A prefab root GUID match
        /// wins; otherwise the recorded avatar name must match exactly ONCE - two avatars
        /// with the same name are ambiguous and never guessed between.
        /// </summary>
        public static AvatarMatchVerdict SelectAvatar(
            string sourceRootGuid,
            string avatarName,
            IEnumerable<AvatarOptimizationState> candidates,
            out AvatarOptimizationState chosen)
        {
            chosen = null;
            if (candidates == null)
            {
                return AvatarMatchVerdict.NotFound;
            }

            var entries = new List<AvatarOptimizationState>(candidates);
            if (!string.IsNullOrEmpty(sourceRootGuid))
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    if (string.Equals(entries[i].SourceRootGuid, sourceRootGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = entries[i];
                        return AvatarMatchVerdict.Matched;
                    }
                }
            }

            AvatarOptimizationState nameMatch = null;
            var nameMatches = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                if (string.IsNullOrEmpty(avatarName) ||
                    !string.Equals(entries[i].AvatarName, avatarName, StringComparison.Ordinal))
                {
                    continue;
                }

                nameMatches++;
                nameMatch = entries[i];
            }

            if (nameMatches == 1)
            {
                chosen = nameMatch;
                return AvatarMatchVerdict.Matched;
            }

            return nameMatches == 0 ? AvatarMatchVerdict.NotFound : AvatarMatchVerdict.Ambiguous;
        }

        /// <summary>
        /// Builds the source GUID → generated path map from the manifest. A source GUID
        /// claimed by two generated outputs is ambiguous and is excluded from the map.
        /// </summary>
        public static void BuildGeneratedMap(
            AvatarOptimizationState recorded,
            BuildRemapRequest request)
        {
            if (recorded == null || request == null)
            {
                return;
            }

            for (var i = 0; i < recorded.Generated.Count; i++)
            {
                var output = recorded.Generated[i];
                if (output == null || string.IsNullOrEmpty(output.Path) || output.DerivedFromGuids.Count == 0)
                {
                    continue;
                }

                var sourceGuid = output.DerivedFromGuids[0];
                if (string.IsNullOrEmpty(sourceGuid))
                {
                    continue;
                }

                if (request.GeneratedPathBySourceGuid.ContainsKey(sourceGuid) &&
                    !string.Equals(request.GeneratedPathBySourceGuid[sourceGuid], output.Path,
                        StringComparison.OrdinalIgnoreCase))
                {
                    request.AmbiguousSourceGuids.Add(sourceGuid);
                    request.GeneratedPathBySourceGuid.Remove(sourceGuid);
                    continue;
                }

                request.GeneratedPathBySourceGuid[sourceGuid] = output.Path;
            }
        }
    }

    public enum AvatarMatchVerdict
    {
        Matched = 0,
        NotFound,
        Ambiguous
    }
}
