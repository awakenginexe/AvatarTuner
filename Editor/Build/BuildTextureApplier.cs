// AvatarTuner v1.0.0 — build-time texture application (Unity glue).
//
// Executes a BuildTexturePlanner plan against the AVATAR CLONE the build pipeline is
// assembling (NDMF hands us the clone). Safety rules enforced here:
//   - only material texture slots on the clone are touched; materials are cloned with
//     Instantiate before any change, so no shared source material asset is ever written;
//   - no renderer, mesh or scene object is created or duplicated;
//   - source textures, source materials, prefabs, animations and animator controllers
//     are read-only inputs;
//   - every refusal or failure is recorded (manifest + console) - never a silent success,
//     and a recorded build application is never claimed to be a VRChat upload.

using System;
using System.Collections.Generic;
using AvatarTuner.Core;
using AvatarTuner.Editor.Tracking;
using UnityEditor;
using UnityEngine;

namespace AvatarTuner.Editor.Build
{
    /// <summary>Outcome of one build-time application attempt.</summary>
    public sealed class BuildApplyResult
    {
        public BuildRemapVerdict Verdict;
        public readonly List<PlanFinding> Findings = new List<PlanFinding>();
        public int AppliedMaterials;
        public int AppliedTextures;

        /// <summary>Localized error key when the application failed, or empty.</summary>
        public string ErrorKey;

        public bool Succeeded
        {
            get
            {
                return Verdict == BuildRemapVerdict.Ready ||
                       Verdict == BuildRemapVerdict.ReadyWithWarnings ||
                       Verdict == BuildRemapVerdict.NotEnabled;
            }
        }
    }

    public static class BuildTextureApplier
    {
        /// <summary>
        /// True when the NDMF build integration is present (our NDMF assembly only
        /// compiles when the NDMF package is installed).
        /// </summary>
        public static bool IsBuildIntegrationAvailable()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                var name = assemblies[i].GetName().Name;
                if (string.Equals(name, "AvatarTuner.Editor.NDMF", StringComparison.Ordinal) ||
                    name.IndexOf("nadena.dev.ndmf", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Applies the recorded optimized textures to the avatar being built. The
        /// <paramref name="cloneRoot" /> must be the build clone, never the scene avatar.
        /// </summary>
        public static BuildApplyResult Apply(GameObject cloneRoot)
        {
            var result = new BuildApplyResult();
            try
            {
                if (cloneRoot == null)
                {
                    result.Verdict = BuildRemapVerdict.Failed;
                    result.ErrorKey = "build.error.applyFailed";
                    return result;
                }

                var load = GeneratedAssetStore.Load();
                AvatarOptimizationState recorded;
                var match = BuildTexturePlanner.SelectAvatar(
                    SourceRootGuidOf(cloneRoot), cloneRoot.name,
                    load.Manifest != null ? load.Manifest.Avatars : null, out recorded);

                if (match != AvatarMatchVerdict.Matched || recorded == null)
                {
                    // Enabled but nothing trustworthy to apply: report, never guess.
                    result.Verdict = match == AvatarMatchVerdict.Ambiguous
                        ? BuildRemapVerdict.PartialFailure
                        : BuildRemapVerdict.NoOutputs;
                    result.Findings.Add(new PlanFinding(match == AvatarMatchVerdict.Ambiguous
                        ? "build.warn.ambiguousMapping"
                        : "build.noManifestEntry", cloneRoot.name, string.Empty));
                    Record(result, recorded == null ? null : recorded.SourceRootGuid, "build.error.applyFailed");
                    return result;
                }

                var request = new BuildRemapRequest
                {
                    BuildEnabled = recorded.BuildApplicationConfigured && recorded.UseOptimizedTexturesOnBuild,
                    HasRecordedOutputs = recorded.Generated.Count > 0,
                    OptimizerConflictDetected = HasOptimizerConflict(cloneRoot),
                    SourcesAreUnchanged = SourcesAreUnchanged(recorded),
                    AlreadyApplied = recorded.AppliedToAvatar
                };

                BuildTexturePlanner.BuildGeneratedMap(recorded, request);
                GatherMaterials(cloneRoot, request);
                var plan = BuildTexturePlanner.Plan(request);
                result.Verdict = plan.Verdict;
                result.Findings.AddRange(plan.Findings);

                if (plan.Verdict == BuildRemapVerdict.Ready ||
                    plan.Verdict == BuildRemapVerdict.ReadyWithWarnings ||
                    plan.Verdict == BuildRemapVerdict.PartialFailure)
                {
                    ExecutePlan(cloneRoot, plan, result);
                }

                if (plan.Verdict == BuildRemapVerdict.PartialFailure || plan.FailedReplacementCount > 0)
                {
                    // Required replacements failed: this must never be reported as success.
                    result.ErrorKey = "build.error.partialApplication";
                }

                if (plan.Verdict != BuildRemapVerdict.NotEnabled)
                {
                    Record(result, recorded.SourceRootGuid, result.ErrorKey);
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Verdict = BuildRemapVerdict.Failed;
                result.ErrorKey = "build.error.applyFailed";
                result.Findings.Add(new PlanFinding("build.error.applyFailed"));
                Debug.LogError("[AvatarTuner] build-time texture application failed: " + ex);
                Record(result, cloneRoot != null ? SourceRootGuidOf(cloneRoot) : null, result.ErrorKey);
                return result;
            }
        }

        // ===================================================================== plan execution

        /// <summary>
        /// Executes remaps on the clone: one material clone per material, texture slots
        /// set on the clone, renderer slots (active and inactive) repointed. Source
        /// material assets are only read.
        /// </summary>
        private static void ExecutePlan(GameObject cloneRoot, BuildRemapPlan plan, BuildApplyResult result)
        {
            var materialById = new Dictionary<string, Material>(StringComparer.Ordinal);
            CollectMaterials(cloneRoot, materialById);

            for (var i = 0; i < plan.Replacements.Count; i++)
            {
                var replacement = plan.Replacements[i];
                Material source;
                if (!materialById.TryGetValue(replacement.MaterialId, out source) || source == null)
                {
                    result.ErrorKey = "build.error.partialApplication";
                    result.Findings.Add(new PlanFinding(
                        "build.warn.missingGenerated", replacement.MaterialName, string.Empty));
                    continue;
                }

                // Clone first: the shared source material asset is never modified.
                var clone = UnityEngine.Object.Instantiate(source);
                clone.name = source.name + " (AvatarTuner)";

                var applied = 0;
                for (var s = 0; s < replacement.Slots.Count; s++)
                {
                    var slot = replacement.Slots[s];
                    var texture = AssetDatabase.LoadAssetAtPath<Texture>(slot.GeneratedPath);
                    if (texture == null)
                    {
                        result.Findings.Add(new PlanFinding(
                            "build.warn.missingGenerated", replacement.MaterialName, slot.GeneratedPath));
                        result.ErrorKey = "build.error.partialApplication";
                        continue;
                    }

                    clone.SetTexture(slot.Property, texture);
                    applied++;
                }

                if (applied == 0)
                {
                    continue;
                }

                RepointRenderers(cloneRoot, source, clone);
                result.AppliedMaterials++;
                result.AppliedTextures += applied;
            }
        }

        private static void RepointRenderers(GameObject cloneRoot, Material source, Material clone)
        {
            var renderers = cloneRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var materials = renderers[i].sharedMaterials;
                var changed = false;
                for (var m = 0; m < materials.Length; m++)
                {
                    if (materials[m] == source)
                    {
                        materials[m] = clone;
                        changed = true;
                    }
                }

                if (changed)
                {
                    renderers[i].sharedMaterials = materials;
                }
            }
        }

        private static void CollectMaterials(GameObject cloneRoot, Dictionary<string, Material> into)
        {
            var renderers = cloneRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var materials = renderers[i].sharedMaterials;
                for (var m = 0; m < materials.Length; m++)
                {
                    var material = materials[m];
                    if (material == null)
                    {
                        continue;
                    }

                    var id = MaterialIdOf(material);
                    if (!into.ContainsKey(id))
                    {
                        into[id] = material;
                    }
                }
            }
        }

        // ===================================================================== input gathering

        /// <summary>
        /// Gathers material usages from the clone's FINAL dependency graph: materials
        /// generated or modified by Modular Avatar / VRCFury during earlier build phases
        /// are seen here, the scene's references are not assumed to be complete.
        /// </summary>
        internal static void GatherMaterials(GameObject cloneRoot, BuildRemapRequest request)
        {
            var animatedGuids = CollectAnimatedMaterialGuids(cloneRoot);
            var byId = new Dictionary<string, MaterialUsage>(StringComparer.Ordinal);

            var renderers = cloneRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var active = renderers[i].gameObject.activeInHierarchy;
                var materials = renderers[i].sharedMaterials;
                for (var m = 0; m < materials.Length; m++)
                {
                    var material = materials[m];
                    if (material == null)
                    {
                        continue;
                    }

                    var id = MaterialIdOf(material);
                    MaterialUsage usage;
                    if (!byId.TryGetValue(id, out usage))
                    {
                        var guid = MaterialGuidOf(material);
                        usage = new MaterialUsage
                        {
                            MaterialId = id,
                            MaterialName = material.name,
                            IsAnimationTarget = animatedGuids.Contains(guid),
                            UsedByInactiveRenderer = !active
                        };

                        var properties = material.GetTexturePropertyNames();
                        for (var p = 0; p < properties.Length; p++)
                        {
                            var texture = material.GetTexture(properties[p]);
                            if (texture == null)
                            {
                                continue;
                            }

                            usage.Textures.Add(new MaterialTextureRef
                            {
                                Property = properties[p],
                                SourceTextureGuid = TextureGuidOf(texture)
                            });
                        }

                        byId[id] = usage;
                    }

                    usage.RendererCount++;
                    if (!active)
                    {
                        usage.UsedByInactiveRenderer = true;
                    }
                }
            }

            foreach (var pair in byId)
            {
                request.Materials.Add(pair.Value);
            }
        }

        /// <summary>
        /// Material GUIDs referenced by object-reference animation curves ("supported
        /// animated material swaps"): those materials are remapped on the base slot only,
        /// swapped states keep the original material assets (reported as a finding).
        /// </summary>
        internal static HashSet<string> CollectAnimatedMaterialGuids(GameObject cloneRoot)
        {
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var animators = cloneRoot.GetComponentsInChildren<Animator>(true);
            for (var i = 0; i < animators.Length; i++)
            {
                var controller = animators[i].runtimeAnimatorController;
                if (controller == null)
                {
                    continue;
                }

                foreach (var clip in ClipsOf(controller))
                {
                    if (clip == null)
                    {
                        continue;
                    }

                    var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                    for (var b = 0; b < bindings.Length; b++)
                    {
                        if (bindings[b].type != typeof(Material))
                        {
                            continue;
                        }

                        var keys = AnimationUtility.GetObjectReferenceCurve(clip, bindings[b]);
                        for (var k = 0; k < keys.Length; k++)
                        {
                            var material = keys[k].value as Material;
                            if (material != null)
                            {
                                guids.Add(MaterialGuidOf(material));
                            }
                        }
                    }
                }
            }

            return guids;
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

        // ===================================================================== validation & identity

        /// <summary>
        /// Source immutability guarantee, part 1: every recorded source must still hash
        /// to the dependency and importer-settings hashes recorded at optimization time.
        /// </summary>
        internal static bool SourcesAreUnchanged(AvatarOptimizationState recorded)
        {
            if (recorded == null)
            {
                return false;
            }

            var provider = new UnityFingerprintProvider();
            for (var i = 0; i < recorded.Sources.Count; i++)
            {
                var source = recorded.Sources[i];
                if (source == null || string.IsNullOrEmpty(source.Guid))
                {
                    continue;
                }

                var dependency = provider.GetAssetDependencyHash(source.Guid, source.Path ?? string.Empty);
                var importer = provider.GetImporterSettingsHash(source.Guid, source.Path ?? string.Empty);
                if (!string.Equals(dependency, source.AssetDependencyHash, StringComparison.Ordinal) ||
                    !string.Equals(importer, source.ImporterSettingsHash, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>AAO / d4rkAvatarOptimizer style optimizers detected on the avatar.</summary>
        internal static bool HasOptimizerConflict(GameObject cloneRoot)
        {
            var components = cloneRoot.GetComponentsInChildren<Component>(true);
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    continue;
                }

                var typeName = components[i].GetType().Name;
                if (typeName.IndexOf("d4rkAvatarOptimizer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("TraceAndOptimize", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string SourceRootGuidOf(GameObject cloneRoot)
        {
            var path = AssetDatabase.GetAssetPath(cloneRoot);
            return string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
        }

        private static string MaterialIdOf(Material material)
        {
            var guid = MaterialGuidOf(material);
            return string.IsNullOrEmpty(guid) ? "runtime:" + material.name : guid;
        }

        private static string MaterialGuidOf(Material material)
        {
            var path = AssetDatabase.GetAssetPath(material);
            return string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
        }

        private static string TextureGuidOf(Texture texture)
        {
            var path = AssetDatabase.GetAssetPath(texture);
            return string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
        }

        /// <summary>
        /// Records build-evidence in the manifest. This is build application evidence
        /// only - a VRChat upload is never observed and never claimed.
        /// </summary>
        private static void Record(BuildApplyResult result, string sourceRootGuid, string errorKey)
        {
            try
            {
                if (string.IsNullOrEmpty(sourceRootGuid))
                {
                    return;
                }

                var stamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
                string saveError;
                GeneratedAssetStore.RecordBuildApplication(
                    sourceRootGuid, errorKey ?? string.Empty, stamp, out saveError);
            }
            catch (Exception)
            {
                // Recording evidence must never break the build itself.
            }

            for (var i = 0; i < result.Findings.Count; i++)
            {
                var finding = result.Findings[i];
                var text = "[AvatarTuner] " + finding.MessageKey +
                           (finding.Args.Count > 0 ? " (" + string.Join(", ", finding.Args.ToArray()) + ")" : string.Empty);
                if (result.ErrorKey != null)
                {
                    Debug.LogWarning(text);
                }
                else
                {
                    Debug.Log(text);
                }
            }
        }
    }
}
