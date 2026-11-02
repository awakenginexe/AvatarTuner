// AvatarTuner v1.0.0 — avatar selection view model (Unity free).
//
// The regression this model exists to prevent: the redesigned window rendered the avatar
// selection panel only after a selection existed, so there was nothing to drag into.
// The rules here make the panel permanent and pin the behavior in tests:
//   - The selection panel renders in EVERY window state; it is never hidden or disabled
//     because of selection state (it is only read-only while an operation runs).
//   - Three selection paths: drag & drop, object picker, and "use current Hierarchy
//     selection" - all funnel into the same validation.
//   - A selected child object resolves to its enclosing avatar root (nearest ancestor
//     carrying an avatar descriptor such as VRCAvatarDescriptor) when one exists.

using System;
using System.Collections.Generic;
using AvatarTuner.Editor.UI;

namespace AvatarTuner.Editor.ViewModels
{
    /// <summary>Outcome of an attempted avatar selection.</summary>
    public enum SelectionVerdict
    {
        /// <summary>A GameObject was selected and used as-is.</summary>
        Accepted = 0,

        /// <summary>A child GameObject was selected; an enclosing avatar root was used.</summary>
        AcceptedChildResolved,

        /// <summary>The object is not a GameObject at all.</summary>
        RejectedNotAGameObject,

        /// <summary>Nothing was available to select.</summary>
        RejectedNothingSelected
    }

    /// <summary>How the selection panel must render in the current window state.</summary>
    public sealed class SelectionPanelState
    {
        /// <summary>Always true: the panel is permanent and not state dependent.</summary>
        public bool ShowObjectField;

        /// <summary>Always true: the drop zone is permanent and not state dependent.</summary>
        public bool ShowDropZone;

        /// <summary>Always true: the Hierarchy-pick button is always offered.</summary>
        public bool ShowUseSelectedButton;

        /// <summary>Only meaningful once something is selected.</summary>
        public bool ShowClearButton;

        /// <summary>Controls are disabled only while an operation is running.</summary>
        public bool Enabled;
    }

    public static class AvatarSelectionModel
    {
        /// <summary>
        /// The panel specification for a window state. The three selection affordances are
        /// unconditional on purpose - a first-time user must always have something to
        /// interact with.
        /// </summary>
        public static SelectionPanelState Build(WindowState state, bool hasAvatar)
        {
            return new SelectionPanelState
            {
                ShowObjectField = true,
                ShowDropZone = true,
                ShowUseSelectedButton = true,
                ShowClearButton = hasAvatar,
                Enabled = !WindowFlow.IsBusy(state)
            };
        }

        /// <summary>
        /// Validates a selection attempt. <paramref name="isGameObject" /> describes the
        /// candidate object kind; <paramref name="hasCandidate" /> distinguishes "nothing to
        /// pick" from "picked something invalid".
        /// </summary>
        public static SelectionVerdict Validate(bool hasCandidate, bool isGameObject)
        {
            if (!hasCandidate)
            {
                return SelectionVerdict.RejectedNothingSelected;
            }

            return isGameObject ? SelectionVerdict.Accepted : SelectionVerdict.RejectedNotAGameObject;
        }

        /// <summary>Localized error key for a rejected verdict, or null when accepted.</summary>
        public static string ErrorKeyFor(SelectionVerdict verdict)
        {
            switch (verdict)
            {
                case SelectionVerdict.RejectedNotAGameObject: return "ui.drop.invalidType";
                case SelectionVerdict.RejectedNothingSelected: return "error.noAvatar";
                default: return null;
            }
        }

        /// <summary>
        /// Picks the avatar root level in a selection chain. The chain is
        /// [selection, parent, grandparent, ...]; each entry says whether that level carries
        /// an avatar descriptor (e.g. VRCAvatarDescriptor). The NEAREST descriptor wins
        /// because it is the immediately containing avatar root; without any descriptor the
        /// selection itself is used as the root. Returns the chain index.
        /// </summary>
        public static int ResolveAvatarRootLevel(IList<bool> chainHasDescriptor)
        {
            if (chainHasDescriptor == null || chainHasDescriptor.Count == 0)
            {
                return 0;
            }

            for (var i = 0; i < chainHasDescriptor.Count; i++)
            {
                if (chainHasDescriptor[i])
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>True when a verdict was accepted (possibly with root resolution).</summary>
        public static bool IsAccepted(SelectionVerdict verdict)
        {
            return verdict == SelectionVerdict.Accepted ||
                   verdict == SelectionVerdict.AcceptedChildResolved;
        }
    }
}
