using System;
using System.Collections.Generic;
using AvatarTuner.Editor.UI;
using AvatarTuner.Editor.ViewModels;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Regression tests for the avatar selection workflow. The bug they pin: the redesigned
    /// window rendered its selection panel (ObjectField + drop zone) only after a selection
    /// existed, so a first-time user had nothing to drag into. The panel must be permanent,
    /// the three selection paths must share one validation, and a selected child object must
    /// resolve to its enclosing avatar root when one exists.
    /// </summary>
    public static class AvatarSelectionTests
    {
        [Test("the selection panel renders in every window state")]
        public static void SelectionPanelIsPermanent()
        {
            foreach (WindowState state in Enum.GetValues(typeof(WindowState)))
            {
                foreach (var hasAvatar in new[] { false, true })
                {
                    var panel = AvatarSelectionModel.Build(state, hasAvatar);

                    Assert.IsTrue(panel.ShowObjectField,
                        state + " / hasAvatar=" + hasAvatar + " shows the ObjectField");
                    Assert.IsTrue(panel.ShowDropZone,
                        state + " / hasAvatar=" + hasAvatar + " shows the drop zone");
                    Assert.IsTrue(panel.ShowUseSelectedButton,
                        state + " / hasAvatar=" + hasAvatar + " offers the Hierarchy pick button");
                    Assert.AreEqual(hasAvatar, panel.ShowClearButton,
                        state + " shows Clear Selection exactly when something is selected");
                }
            }
        }

        [Test("selection controls only pause while an operation is running")]
        public static void OnlyBusyDisablesSelection()
        {
            foreach (WindowState state in Enum.GetValues(typeof(WindowState)))
            {
                var panel = AvatarSelectionModel.Build(state, false);
                Assert.AreEqual(!WindowFlow.IsBusy(state), panel.Enabled,
                    state + " disables selection exactly while busy");

                // The critical acceptance rule: a missing selection never disables the panel.
                var unselected = AvatarSelectionModel.Build(WindowState.NoSelection, false);
                Assert.IsTrue(unselected.Enabled, "the empty state keeps selection enabled");
                Assert.IsTrue(unselected.ShowDropZone, "the empty state keeps the drop zone visible");
            }
        }

        [Test("all three selection paths share one validation")]
        public static void ValidationIsShared()
        {
            Assert.AreEqual(SelectionVerdict.Accepted,
                AvatarSelectionModel.Validate(true, true), "a GameObject is accepted");
            Assert.AreEqual(SelectionVerdict.RejectedNotAGameObject,
                AvatarSelectionModel.Validate(true, false), "a Material is rejected");
            Assert.AreEqual(SelectionVerdict.RejectedNothingSelected,
                AvatarSelectionModel.Validate(false, true), "nothing to pick is its own verdict");

            Assert.IsNull(AvatarSelectionModel.ErrorKeyFor(SelectionVerdict.Accepted),
                "accepted selections have no error");
            Assert.IsNull(AvatarSelectionModel.ErrorKeyFor(SelectionVerdict.AcceptedChildResolved),
                "resolved selections have no error");
            Assert.AreEqual("ui.drop.invalidType",
                AvatarSelectionModel.ErrorKeyFor(SelectionVerdict.RejectedNotAGameObject),
                "wrong kinds explain themselves");
            Assert.AreEqual("error.noAvatar",
                AvatarSelectionModel.ErrorKeyFor(SelectionVerdict.RejectedNothingSelected),
                "empty picks explain themselves");

            Assert.IsTrue(AvatarSelectionModel.IsAccepted(SelectionVerdict.Accepted), "accepted is accepted");
            Assert.IsTrue(AvatarSelectionModel.IsAccepted(SelectionVerdict.AcceptedChildResolved),
                "resolved is accepted");
            Assert.IsFalse(AvatarSelectionModel.IsAccepted(SelectionVerdict.RejectedNotAGameObject),
                "rejections are not accepted");
        }

        [Test("a selected child resolves to the nearest avatar root")]
        public static void ChildResolvesToNearestAvatarRoot()
        {
            Assert.AreEqual(0,
                AvatarSelectionModel.ResolveAvatarRootLevel(new List<bool> { true }),
                "a descriptor on the selection itself wins");
            Assert.AreEqual(2,
                AvatarSelectionModel.ResolveAvatarRootLevel(new List<bool> { false, false, true }),
                "the enclosing descriptor becomes the avatar root");
            Assert.AreEqual(1,
                AvatarSelectionModel.ResolveAvatarRootLevel(new List<bool> { false, true, true }),
                "the NEAREST descriptor wins over outer ones");
            Assert.AreEqual(0,
                AvatarSelectionModel.ResolveAvatarRootLevel(new List<bool> { false, false }),
                "without a descriptor the selection itself is the root");
            Assert.AreEqual(0, AvatarSelectionModel.ResolveAvatarRootLevel(null),
                "an empty chain is safe");
            Assert.AreEqual(0, AvatarSelectionModel.ResolveAvatarRootLevel(new List<bool>()),
                "an empty list is safe");
        }
    }
}
