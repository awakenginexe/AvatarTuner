// AvatarTuner v1.0.0 — NDMF build-time texture application plugin.
//
// This assembly only compiles when the NDMF package (nadena.dev.ndmf) is installed:
// the asmdef is gated by the AVATARTUNER_HAS_NDMF version define + define constraint,
// so projects without NDMF simply never build this file and the rest of AvatarTuner
// keeps working (the setting then reports the missing integration).
//
// Execution order (explicitly justified):
//   - BuildPhase.Optimizing runs after Resolving / Generating / Transforming, so every
//     material generated or modified by Modular Avatar, VRCFury or similar tools is
//     already present on the build clone. We resolve textures against the clone's FINAL
//     dependency graph, never against the scene's original references (which are not
//     guaranteed to be complete).
//   - BeforePlugin(...) is a weak constraint against the texture/mesh optimizers
//     (Avatar Optimizer, d4rkAvatarOptimizer): our remap should see original textures
//     before any atlas step. Unknown plugin ids are ignored by NDMF, and if such an
//     optimizer is detected ON the avatar the applier skips the transformation entirely
//     with an actionable warning instead of racing it.
//
// API source of truth: nadena.dev.ndmf (package nadena.dev.ndmf, Unity 2022.3),
// Plugin<T>/InPhase/Sequence/ExportsPlugin/BuildContext.AvatarRootObject.

using System;
using AvatarTuner.Editor.Build;
using nadena.dev.ndmf;
using UnityEngine;

[assembly: ExportsPlugin(typeof(AvatarTuner.Editor.NDMF.AvatarTunerBuildPlugin))]

namespace AvatarTuner.Editor.NDMF
{
    /// <summary>
    /// Applies AvatarTuner's generated optimized textures to the avatar clone during the
    /// NDMF build. Strictly texture application: no renderers or meshes are created, the
    /// scene avatar and every source asset stay untouched.
    /// </summary>
    public sealed class AvatarTunerBuildPlugin : Plugin<AvatarTunerBuildPlugin>
    {
        public override string QualifiedName => "dev.avatartuner.build-texture-application";
        public override string DisplayName => "AvatarTuner: Apply optimized textures";

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing)
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .BeforePlugin("jp.d4rk.avataroptimizer")
                .Run("Apply AvatarTuner optimized textures", ctx =>
                {
                    // ctx.AvatarRootObject is the build clone; the scene avatar is never
                    // handed to us. Failures are recorded, never silently swallowed.
                    try
                    {
                        var result = BuildTextureApplier.Apply(ctx.AvatarRootObject);
                        for (var i = 0; i < result.Findings.Count; i++)
                        {
                            Debug.LogWarning("[AvatarTuner] " + result.Findings[i].MessageKey);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("[AvatarTuner] build-time texture application failed: " + ex);
                        throw;
                    }
                });
        }
    }
}
