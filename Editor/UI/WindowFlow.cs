// AvatarTuner v1.0.0 — window state and control gating (Unity free).
//
// The rules for which control is enabled when live here, not in the window code, so the
// behavior of every button can be unit tested without an editor. The principle behind the
// gates: no control may run while another operation is in flight, optimization requires a
// compatibility-approved plan with real changes, and cleanup requires recorded outputs.

namespace AvatarTuner.Editor.UI
{
    /// <summary>The lifecycle of the AvatarTuner window's working set.</summary>
    public enum WindowState
    {
        /// <summary>No avatar selected yet.</summary>
        NoSelection = 0,

        /// <summary>Avatar selected, not analyzed yet.</summary>
        Ready,

        /// <summary>A scan is running.</summary>
        Analyzing,

        /// <summary>Analysis (and plan) available.</summary>
        Analyzed,

        /// <summary>Optimization is running.</summary>
        Optimizing,

        /// <summary>An optimization run finished successfully.</summary>
        Done,

        /// <summary>The last operation failed or was cancelled.</summary>
        Error
    }

    public static class WindowFlow
    {
        public static bool IsBusy(WindowState state)
        {
            return state == WindowState.Analyzing || state == WindowState.Optimizing;
        }

        public static bool CanAnalyze(WindowState state)
        {
            return !IsBusy(state);
        }

        /// <summary>Previewing the plan requires an analysis to have produced one.</summary>
        public static bool CanPreview(WindowState state, bool hasPlan)
        {
            return !IsBusy(state) &&
                   hasPlan &&
                   (state == WindowState.Analyzed || state == WindowState.Done || state == WindowState.Error);
        }

        /// <summary>
        /// Optimization (and regeneration) demands all of: nothing in flight, a built plan,
        /// real changes in it, and a compatibility verdict that allows modifying this
        /// avatar at all.
        /// </summary>
        public static bool CanOptimize(
            WindowState state,
            bool hasPlan,
            bool planHasChanges,
            bool compatibilityAllows)
        {
            return !IsBusy(state) &&
                   hasPlan &&
                   planHasChanges &&
                   compatibilityAllows &&
                   (state == WindowState.Analyzed || state == WindowState.Done || state == WindowState.Error);
        }

        public static bool CanCleanup(WindowState state, bool hasRecordedOutputs)
        {
            return !IsBusy(state) && hasRecordedOutputs;
        }

        public static bool CanEditSettings(WindowState state)
        {
            return !IsBusy(state);
        }

        public static WindowState AfterSelectingAvatar()
        {
            return WindowState.Ready;
        }

        public static WindowState AfterDeselectingAvatar()
        {
            return WindowState.NoSelection;
        }

        public static WindowState BeginAnalysis()
        {
            return WindowState.Analyzing;
        }

        public static WindowState AfterAnalysis(bool succeeded)
        {
            return succeeded ? WindowState.Analyzed : WindowState.Error;
        }

        public static WindowState BeginOptimization()
        {
            return WindowState.Optimizing;
        }

        public static WindowState AfterOptimization(bool succeeded)
        {
            return succeeded ? WindowState.Done : WindowState.Error;
        }
    }
}
