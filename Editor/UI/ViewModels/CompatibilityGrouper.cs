// AvatarTuner v1.0.0 — compatibility view model (Unity free).
//
// The scanner reports one ToolMarkerEntry per component it saw, which used to surface the
// same "Modular Avatar is present" message once per component. This model groups evidence
// per tool instead: one message per tool, a component count, the distinct component types,
// and the full path list behind an expander. Animation-driven systems and cross-tool
// conflicts get their own groups.

using System;
using System.Collections.Generic;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;

namespace AvatarTuner.Editor.ViewModels
{
    /// <summary>One detected tool with all of its evidence.</summary>
    public sealed class ToolGroup
    {
        public string ToolId;
        public string DisplayName;

        /// <summary>The tool's single explanation message (never repeated per component).</summary>
        public string MessageKey;

        public CompatRisk Risk;

        /// <summary>Number of components actually seen.</summary>
        public int ComponentCount;

        /// <summary>Distinct component type names, sorted.</summary>
        public readonly List<string> ComponentTypes = new List<string>();

        /// <summary>Every marker path with its component type, sorted by path.</summary>
        public readonly List<ToolMarkerEntry> Markers = new List<ToolMarkerEntry>();

        /// <summary>Warnings about this tool (key + args).</summary>
        public readonly List<AnalysisFinding> Warnings = new List<AnalysisFinding>();
    }

    /// <summary>The grouped compatibility report the UI renders.</summary>
    public sealed class CompatibilityViewModel
    {
        /// <summary>Detected tools in stable (registry) order.</summary>
        public readonly List<ToolGroup> Tools = new List<ToolGroup>();

        /// <summary>Animation-driven systems, one entry per distinct system.</summary>
        public readonly List<AnalysisFinding> Systems = new List<AnalysisFinding>();

        /// <summary>Cross-tool conflicts and other blockers (not tool specific).</summary>
        public readonly List<AnalysisFinding> Blockers = new List<AnalysisFinding>();

        /// <summary>Warnings that belong to no single tool group.</summary>
        public readonly List<AnalysisFinding> GeneralWarnings = new List<AnalysisFinding>();

        public bool HasAnything
        {
            get
            {
                return Tools.Count > 0 || Systems.Count > 0 ||
                       Blockers.Count > 0 || GeneralWarnings.Count > 0;
            }
        }

        public int TotalComponents
        {
            get
            {
                var total = 0;
                for (var i = 0; i < Tools.Count; i++)
                {
                    total += Tools[i].ComponentCount;
                }

                return total;
            }
        }
    }

    public static class CompatibilityGrouper
    {
        /// <summary>
        /// Groups the scanner's evidence by tool. Deterministic: tools follow the policy
        /// registry order, markers sort by path, systems keep first-seen order. A tool that
        /// is only installed in the project (presence notice, no components on the avatar)
        /// still gets exactly one group card - the repetition fix.
        /// </summary>
        public static CompatibilityViewModel Build(CompatibilityReport report)
        {
            var model = new CompatibilityViewModel();
            if (report == null)
            {
                return model;
            }

            var consumedNotices = new HashSet<string>(StringComparer.Ordinal);

            for (var t = 0; t < CompatibilityPolicy.KnownTools.Length; t++)
            {
                var definition = CompatibilityPolicy.KnownTools[t];
                var group = new ToolGroup();
                group.ToolId = definition.ToolId;
                group.DisplayName = definition.DisplayName;
                group.MessageKey = definition.MessageKey;
                group.Risk = definition.Risk;

                for (var i = 0; i < report.DetectedTools.Count; i++)
                {
                    var marker = report.DetectedTools[i];
                    if (!string.Equals(marker.ToolId, definition.ToolId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    group.ComponentCount++;
                    group.Markers.Add(marker);

                    if (!string.IsNullOrEmpty(marker.ComponentType) &&
                        !group.ComponentTypes.Contains(marker.ComponentType))
                    {
                        group.ComponentTypes.Add(marker.ComponentType);
                    }

                    // Risk observed on the marker wins when it is higher than the default.
                    if ((int)marker.Risk > (int)group.Risk)
                    {
                        group.Risk = marker.Risk;
                    }
                }

                var presentOnly = false;
                for (var i = 0; i < report.Notices.Count; i++)
                {
                    if (string.Equals(report.Notices[i].MessageKey, definition.MessageKey,
                            StringComparison.Ordinal))
                    {
                        presentOnly = true;
                        consumedNotices.Add(definition.MessageKey);
                    }
                }

                if (group.ComponentCount == 0 && !presentOnly)
                {
                    continue;
                }

                group.ComponentTypes.Sort(StringComparer.Ordinal);
                group.Markers.Sort((left, right) =>
                    string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));

                model.Tools.Add(group);
            }

            var systemKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < report.Notices.Count; i++)
            {
                var notice = report.Notices[i];
                if (consumedNotices.Contains(notice.MessageKey))
                {
                    // The tool's own explanation lives on its group card exactly once.
                    continue;
                }

                if (notice.MessageKey == "compat.animatorDrivenSystem")
                {
                    if (systemKeys.Add(Describe(notice)))
                    {
                        model.Systems.Add(notice);
                    }

                    continue;
                }

                model.GeneralWarnings.Add(notice);
            }

            for (var i = 0; i < report.Warnings.Count; i++)
            {
                var warning = report.Warnings[i];
                var tool = FindToolByMessage(model, warning.MessageKey);
                if (tool != null)
                {
                    tool.Warnings.Add(warning);
                }
                else
                {
                    model.GeneralWarnings.Add(warning);
                }
            }

            model.Blockers.AddRange(report.Blockers);
            return model;
        }

        private static ToolGroup FindToolByMessage(CompatibilityViewModel model, string messageKey)
        {
            for (var i = 0; i < model.Tools.Count; i++)
            {
                if (string.Equals(model.Tools[i].MessageKey, messageKey, StringComparison.Ordinal))
                {
                    return model.Tools[i];
                }
            }

            return null;
        }

        private static string Describe(AnalysisFinding finding)
        {
            return finding.MessageKey + "|" + string.Join(",", finding.Args.ToArray());
        }
    }
}
