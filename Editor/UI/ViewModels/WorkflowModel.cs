// AvatarTuner v1.0.0 — workflow view model (Unity free).
//
// The core workflow is: Select Avatar > Analyze > Review Changes > Optimize > Results.
// This model decides which step the user is on, which single action is the primary button,
// and which localized reason explains why an action is unavailable. Keeping the rules out
// of the window means the action hierarchy is unit tested, not eyeballed.

using System;
using AvatarTuner.Editor.UI;

namespace AvatarTuner.Editor.ViewModels
{
    public enum PrimaryAction
    {
        /// <summary>Nothing to do until an avatar is selected.</summary>
        SelectAvatar = 0,
        Analyze,
        ReviewChanges,
        Optimize,
        Regenerate,
        ViewResults
    }

    public static class WorkflowModel
    {
        /// <summary>The five breadcrumb steps, in order.</summary>
        public static readonly string[] StepLabelKeys =
        {
            "ui.workflow.select",
            "ui.workflow.analyze",
            "ui.workflow.review",
            "ui.workflow.optimize",
            "ui.workflow.results"
        };

        /// <summary>
        /// The one action the workflow wants next. <paramref name="planHasChanges" /> false
        /// after analysis means there is nothing to optimize, so reviewing is the honest end
        /// state rather than showing a dead Optimize button.
        /// </summary>
        public static PrimaryAction NextAction(
            WindowState state,
            bool hasAvatar,
            bool hasPlan,
            bool planHasChanges,
            bool hasResults)
        {
            if (!hasAvatar || state == WindowState.NoSelection)
            {
                return PrimaryAction.SelectAvatar;
            }

            if (WindowFlow.IsBusy(state))
            {
                return state == WindowState.Optimizing ? PrimaryAction.Optimize : PrimaryAction.Analyze;
            }

            if (state == WindowState.Ready)
            {
                return PrimaryAction.Analyze;
            }

            if (hasResults)
            {
                return PrimaryAction.ViewResults;
            }

            if (!hasPlan)
            {
                return PrimaryAction.Analyze;
            }

            return planHasChanges ? PrimaryAction.Optimize : PrimaryAction.ReviewChanges;
        }

        /// <summary>
        /// Breadcrumb step index (0..4). Select → 0, Analyze → 1, review window → 2,
        /// optimizing → 3, finished with results → 4.
        /// </summary>
        public static int ActiveStep(WindowState state, bool hasAvatar, bool hasResults)
        {
            if (!hasAvatar || state == WindowState.NoSelection)
            {
                return 0;
            }

            switch (state)
            {
                case WindowState.Ready:
                    return 1;
                case WindowState.Analyzing:
                    return 1;
                case WindowState.Analyzed:
                    return 2;
                case WindowState.Optimizing:
                    return 3;
                case WindowState.Done:
                    return hasResults ? 4 : 2;
                case WindowState.Error:
                    return hasResults ? 4 : 1;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Localized reason (key) why the optimize action is unavailable, or null when it is
        /// available. This is what the UI shows instead of a silently dead button.
        /// </summary>
        public static string OptimizeDisabledReasonKey(
            WindowState state,
            bool hasAvatar,
            bool hasPlan,
            bool planHasChanges,
            bool compatibilityAllows)
        {
            if (WindowFlow.IsBusy(state))
            {
                return "ui.hint.busy";
            }

            if (!hasAvatar || state == WindowState.NoSelection)
            {
                return "ui.hint.selectAvatar";
            }

            if (state == WindowState.Ready || !hasPlan)
            {
                return "ui.hint.analyzeFirst";
            }

            if (!compatibilityAllows)
            {
                return "ui.hint.blocked";
            }

            if (!planHasChanges)
            {
                return "ui.hint.noChanges";
            }

            return null;
        }

        /// <summary>Localized reason why Analyze is unavailable, or null.</summary>
        public static string AnalyzeDisabledReasonKey(WindowState state, bool hasAvatar)
        {
            if (WindowFlow.IsBusy(state))
            {
                return "ui.hint.busy";
            }

            if (!hasAvatar || state == WindowState.NoSelection)
            {
                return "ui.hint.selectAvatar";
            }

            return null;
        }
    }
}
