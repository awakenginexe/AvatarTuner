// AvatarTuner v1.0.0 — optimization dialog rules (Unity free).
//
// Opening configuration stays passive. Confirmation evaluates and generates in one action;
// generation still requires the freshly evaluated plan to match current inputs.

using System;
using AvatarTuner.Core;

namespace AvatarTuner.Editor.ViewModels
{
    /// <summary>Preset persistence across Unity editor sessions.</summary>
    public static class PresetMemory
    {
        /// <summary>SessionState key holding the preset id of the last choice.</summary>
        public const string SessionKey = "AvatarTuner.OptimizationPreset";

        /// <summary>The preset to fall back to on first run (the recommended one).</summary>
        public const OptimizationPreset Fallback = OptimizationPreset.Balanced;

        /// <summary>Loads the remembered preset; invalid or missing data falls back.</summary>
        public static OptimizationPreset Load(Func<string, string, string> get)
        {
            if (get == null)
            {
                return Fallback;
            }

            return ParsePreset(get(SessionKey, string.Empty), Fallback);
        }

        /// <summary>Persists the chosen preset id.</summary>
        public static void Save(Action<string, string> set, OptimizationPreset preset)
        {
            if (set == null)
            {
                return;
            }

            set(SessionKey, PresetCatalog.GetId(preset));
        }

        /// <summary>Maps a stored preset id to its enum; unknown ids fall back safely.</summary>
        public static OptimizationPreset ParsePreset(string storedId, OptimizationPreset fallback)
        {
            foreach (OptimizationPreset preset in Enum.GetValues(typeof(OptimizationPreset)))
            {
                if (string.Equals(PresetCatalog.GetId(preset), storedId, StringComparison.OrdinalIgnoreCase))
                {
                    return preset;
                }
            }

            return fallback;
        }
    }

    public static class OptimizationDialogModel
    {
        /// <summary>
        /// A plan may only back a confirmed run while it was built with the configuration
        /// that is current right now. Anything else is stale and must be rebuilt first -
        /// a stale plan is never executed.
        /// </summary>
        public static bool IsPlanFresh(string planConfigToken, string currentConfigToken)
        {
            return !string.IsNullOrEmpty(planConfigToken) &&
                   string.Equals(planConfigToken, currentConfigToken, StringComparison.Ordinal);
        }

        /// <summary>
        /// Localized reason (key) why confirming the optimization is unavailable, or null
        /// when it can be confirmed. Mirrors the workflow gates so the dialog's confirm
        /// button explains itself instead of silently doing nothing.
        /// </summary>
        public static string ConfirmDisabledReasonKey(
            bool busy,
            bool hasAnalysis,
            bool compatibilityAllows)
        {
            if (busy)
            {
                return "ui.hint.busy";
            }

            if (!hasAnalysis)
            {
                return "ui.hint.analyzeFirst";
            }

            if (!compatibilityAllows)
            {
                return "ui.hint.blocked";
            }

            return null;
        }
    }
}
