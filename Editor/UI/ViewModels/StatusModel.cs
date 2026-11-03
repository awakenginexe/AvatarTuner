// AvatarTuner v1.0.0 — display status derivation (Unity free).
//
// The status the UI shows must describe what really exists, not what was planned. The
// rules are pure and unit tested:
//   - "Optimized Output Generated" only ever follows really generated output files.
//   - "Applied to Avatar" is only ever reported for outputs that were really applied to
//     the avatar in the scene. AvatarTuner v1.0.0 never applies anything (it generates
//     standalone copies), so Derive never returns it - and tests prove that.
//   - Window-state statuses (analysis complete, ready to optimize) come before any run,
//     and change tracking statuses (changes detected, re-optimization required) describe
//     recorded outputs that went stale.

using AvatarTuner.Core;

namespace AvatarTuner.Editor.ViewModels
{
    /// <summary>The honest status of one avatar's optimization, for display.</summary>
    public enum AvatarDisplayStatus
    {
        /// <summary>Nothing analyzed, nothing recorded.</summary>
        NotOptimized = 0,

        /// <summary>Analysis finished; no plan with changes exists yet.</summary>
        AnalysisComplete,

        /// <summary>A plan with real changes exists; optimization has not run.</summary>
        ReadyToOptimize,

        /// <summary>Optimized output was generated (standalone files, not applied).</summary>
        OutputGenerated,

        /// <summary>Output was generated but textures were skipped or need review.</summary>
        OutputGeneratedWithWarnings,

        /// <summary>
        /// Generated outputs were applied to the avatar in the scene. Only ever reported
        /// for a real application step; AvatarTuner v1.0.0 has no such step.
        /// </summary>
        AppliedToAvatar,

        /// <summary>Tracked inputs changed since the recorded run.</summary>
        ChangesDetected,

        /// <summary>Something changed that invalidates the whole recorded output set.</summary>
        ReoptimizationRequired,

        /// <summary>The last optimization attempt failed.</summary>
        OptimizationFailed,

        /// <summary>The avatar uses constructs this tool refuses to modify safely.</summary>
        Unsupported,

        /// <summary>Output generated and "Use Optimized Textures During Build" is enabled.</summary>
        BuildTimeEnabled,

        /// <summary>Output generated but build-time application was explicitly disabled.</summary>
        BuildTimeDisabled,

        /// <summary>The last build-time application attempt failed (recorded in the manifest).</summary>
        BuildTimeFailed,

        /// <summary>Applied, but some references could not be safely remapped.</summary>
        PartiallyOptimized
    }

    public static class StatusModel
    {
        /// <summary>
        /// Derives the display status from the real state of the working set.
        /// </summary>
        /// <param name="hasReport">A successful analysis exists.</param>
        /// <param name="planHasChanges">The current plan contains real changes.</param>
        /// <param name="ranThisSession">A generation run happened in this window session.</param>
        /// <param name="runSucceeded">That run completed successfully.</param>
        /// <param name="runHasWarnings">That run skipped textures or flagged reviews.</param>
        /// <param name="hasRecordedOutputs">The manifest records generated outputs.</param>
        /// <param name="liveStatus">Change tracking's verdict over the recorded state.</param>
        public static AvatarDisplayStatus Derive(
            bool hasReport,
            bool planHasChanges,
            bool ranThisSession,
            bool runSucceeded,
            bool runHasWarnings,
            bool hasRecordedOutputs,
            OptimizationStatus liveStatus)
        {
            if (ranThisSession)
            {
                if (!runSucceeded)
                {
                    return AvatarDisplayStatus.OptimizationFailed;
                }

                return runHasWarnings
                    ? AvatarDisplayStatus.OutputGeneratedWithWarnings
                    : AvatarDisplayStatus.OutputGenerated;
            }

            if (liveStatus == OptimizationStatus.Unsupported)
            {
                return AvatarDisplayStatus.Unsupported;
            }

            if (hasRecordedOutputs)
            {
                switch (liveStatus)
                {
                    case OptimizationStatus.ChangesDetected:
                        return AvatarDisplayStatus.ChangesDetected;
                    case OptimizationStatus.ReoptimizationRequired:
                        return AvatarDisplayStatus.ReoptimizationRequired;
                    case OptimizationStatus.OptimizationFailed:
                        return AvatarDisplayStatus.OptimizationFailed;
                    case OptimizationStatus.Unsupported:
                        return AvatarDisplayStatus.Unsupported;
                    default:
                        // Recorded outputs exist and tracking is happy: output was generated.
                        return AvatarDisplayStatus.OutputGenerated;
                }
            }

            if (planHasChanges)
            {
                return AvatarDisplayStatus.ReadyToOptimize;
            }

            return hasReport ? AvatarDisplayStatus.AnalysisComplete : AvatarDisplayStatus.NotOptimized;
        }

        /// <summary>
        /// Refines an output-bearing status with the build-time application setting and
        /// its recorded evidence. Only ever applied when generated outputs really exist.
        /// </summary>
        public static AvatarDisplayStatus DeriveBuildTime(
            AvatarDisplayStatus baseStatus,
            bool configured,
            bool enabled,
            bool lastApplicationFailed)
        {
            if (lastApplicationFailed)
            {
                return AvatarDisplayStatus.BuildTimeFailed;
            }

            if (baseStatus == AvatarDisplayStatus.OutputGenerated ||
                baseStatus == AvatarDisplayStatus.OutputGeneratedWithWarnings)
            {
                if (!configured)
                {
                    return baseStatus; // Output Generated, Not Applied
                }

                return enabled
                    ? AvatarDisplayStatus.BuildTimeEnabled
                    : AvatarDisplayStatus.BuildTimeDisabled;
            }

            return baseStatus;
        }

        /// <summary>
        /// Applies the direct scene application health on top of the base status. When the
        /// optimized references are verified on the scene avatar the status is honestly
        /// "Applied to Avatar"; drift reads as Changes Detected; partial stays partial.
        /// </summary>
        public static AvatarDisplayStatus DeriveApplication(
            AvatarDisplayStatus baseStatus,
            Apply.ApplicationHealth health)
        {
            switch (health)
            {
                case Apply.ApplicationHealth.Applied:
                    return AvatarDisplayStatus.AppliedToAvatar;
                case Apply.ApplicationHealth.PartiallyApplied:
                    return AvatarDisplayStatus.PartiallyOptimized;
                case Apply.ApplicationHealth.Drifted:
                    return AvatarDisplayStatus.ChangesDetected;
                default:
                    return baseStatus;
            }
        }

        /// <summary>True when this status describes really generated output files.</summary>
        public static bool DescribesGeneratedOutput(AvatarDisplayStatus status)
        {
            return status == AvatarDisplayStatus.OutputGenerated ||
                   status == AvatarDisplayStatus.OutputGeneratedWithWarnings ||
                   status == AvatarDisplayStatus.AppliedToAvatar ||
                   status == AvatarDisplayStatus.ChangesDetected ||
                   status == AvatarDisplayStatus.ReoptimizationRequired;
        }

        public static string NameKey(AvatarDisplayStatus status)
        {
            switch (status)
            {
                case AvatarDisplayStatus.AnalysisComplete: return "status.analysisComplete.name";
                case AvatarDisplayStatus.ReadyToOptimize: return "status.readyToOptimize.name";
                case AvatarDisplayStatus.OutputGenerated: return "status.optimized.name";
                case AvatarDisplayStatus.OutputGeneratedWithWarnings: return "status.outputGeneratedWarnings.name";
                case AvatarDisplayStatus.AppliedToAvatar: return "status.appliedToAvatar.name";
                case AvatarDisplayStatus.BuildTimeEnabled: return "status.buildTimeEnabled.name";
                case AvatarDisplayStatus.BuildTimeDisabled: return "status.buildTimeDisabled.name";
                case AvatarDisplayStatus.BuildTimeFailed: return "status.buildTimeFailed.name";
                case AvatarDisplayStatus.PartiallyOptimized: return "status.partiallyOptimized.name";
                case AvatarDisplayStatus.ChangesDetected: return "status.changesDetected.name";
                case AvatarDisplayStatus.ReoptimizationRequired: return "status.reoptimizationRequired.name";
                case AvatarDisplayStatus.OptimizationFailed: return "status.optimizationFailed.name";
                case AvatarDisplayStatus.Unsupported: return "status.unsupported.name";
                default: return "status.notOptimized.name";
            }
        }

        public static string SummaryKey(AvatarDisplayStatus status)
        {
            switch (status)
            {
                case AvatarDisplayStatus.AnalysisComplete: return "status.analysisComplete.summary";
                case AvatarDisplayStatus.ReadyToOptimize: return "status.readyToOptimize.summary";
                case AvatarDisplayStatus.OutputGenerated: return "status.optimized.summary";
                case AvatarDisplayStatus.OutputGeneratedWithWarnings: return "status.outputGeneratedWarnings.summary";
                case AvatarDisplayStatus.AppliedToAvatar: return "status.appliedToAvatar.summary";
                case AvatarDisplayStatus.BuildTimeEnabled: return "status.buildTimeEnabled.summary";
                case AvatarDisplayStatus.BuildTimeDisabled: return "status.buildTimeDisabled.summary";
                case AvatarDisplayStatus.BuildTimeFailed: return "status.buildTimeFailed.summary";
                case AvatarDisplayStatus.PartiallyOptimized: return "status.partiallyOptimized.summary";
                case AvatarDisplayStatus.ChangesDetected: return "status.changesDetected.summary";
                case AvatarDisplayStatus.ReoptimizationRequired: return "status.reoptimizationRequired.summary";
                case AvatarDisplayStatus.OptimizationFailed: return "status.optimizationFailed.summary";
                case AvatarDisplayStatus.Unsupported: return "status.unsupported.summary";
                default: return "status.notOptimized.summary";
            }
        }

        public static SemanticTone Tone(AvatarDisplayStatus status)
        {
            switch (status)
            {
                case AvatarDisplayStatus.OutputGenerated:
                case AvatarDisplayStatus.AppliedToAvatar:
                case AvatarDisplayStatus.BuildTimeEnabled:
                    return SemanticTone.Success;
                case AvatarDisplayStatus.OutputGeneratedWithWarnings:
                case AvatarDisplayStatus.ChangesDetected:
                case AvatarDisplayStatus.ReoptimizationRequired:
                case AvatarDisplayStatus.ReadyToOptimize:
                case AvatarDisplayStatus.PartiallyOptimized:
                    return SemanticTone.Warning;
                case AvatarDisplayStatus.OptimizationFailed:
                case AvatarDisplayStatus.BuildTimeFailed:
                case AvatarDisplayStatus.Unsupported:
                    return SemanticTone.Error;
                case AvatarDisplayStatus.AnalysisComplete:
                    return SemanticTone.Info;
                default:
                    return SemanticTone.Neutral;
            }
        }
    }
}
