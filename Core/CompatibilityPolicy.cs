// AvatarTuner v1.0.0 — third-party tool compatibility policy.
//
// The editor layer discovers *evidence* (components, assemblies, packages); this file owns
// the *meaning* of that evidence. Keeping the rules here pure serves two goals:
//   1. The policy can be unit tested without a Unity installation.
//   2. Detection heuristics and product decisions stay in one reviewable place, so the
//      scanner itself remains a thin observation layer that never decides anything.
//
// The guiding principle is the same as everywhere in AvatarTuner: observe, report, never
// modify. A detected tool is never a defect on its own; it is information the user needs
// before trusting any generated result.

using System;
using System.Collections.Generic;

namespace AvatarTuner.Core
{
    /// <summary>How risky it is to run AvatarTuner alongside a tool.</summary>
    public enum CompatRisk
    {
        /// <summary>The tool is read or reported only; output is unaffected.</summary>
        Low = 0,

        /// <summary>The tool can change what the avatar contains at build time.</summary>
        Medium = 1,

        /// <summary>The tool makes the editor state diverge from the build result.</summary>
        High = 2
    }

    /// <summary>How strongly a finding constrains the user.</summary>
    public enum CompatSeverity
    {
        /// <summary>Informational. Shown, never blocks, never demands acknowledgement.</summary>
        Info = 0,

        /// <summary>Must be shown before optimizing; the user should review before uploading.</summary>
        Warning = 1,

        /// <summary>Optimization is refused; the avatar stays read-only for AvatarTuner.</summary>
        Blocker = 2
    }

    /// <summary>A known third-party tool and every way AvatarTuner can recognize it.</summary>
    public sealed class CompatToolDefinition
    {
        /// <summary>Stable machine id, e.g. "vrcfury". Never localized.</summary>
        public string ToolId;

        /// <summary>Human name shown in the UI, e.g. "VRCFury".</summary>
        public string DisplayName;

        /// <summary>Translation key describing what this tool's presence means.</summary>
        public string MessageKey;

        public CompatRisk Risk;
        public CompatSeverity Severity;

        /// <summary>Case-insensitive substrings matched against component type full names.</summary>
        public string[] ComponentTypeFragments = new string[0];

        /// <summary>Case-insensitive substrings matched against assembly simple names.</summary>
        public string[] AssemblyNameFragments = new string[0];

        /// <summary>Case-insensitive substrings matched against package ids.</summary>
        public string[] PackageIdFragments = new string[0];
    }

    /// <summary>One localized, explainable compatibility observation.</summary>
    public sealed class CompatFinding
    {
        public string MessageKey;
        public readonly List<string> Args = new List<string>();
        public CompatSeverity Severity;
        public CompatRisk Risk;

        /// <summary>The tool this finding is about; null for findings about systems.</summary>
        public string ToolId;

        public CompatFinding()
        {
        }

        public CompatFinding(string messageKey, params string[] args)
        {
            MessageKey = messageKey;
            if (args != null)
            {
                Args.AddRange(args);
            }
        }
    }

    /// <summary>The verdict produced by <see cref="CompatibilityPolicy.Evaluate" />.</summary>
    public sealed class CompatAssessment
    {
        /// <summary>Detected tools in <see cref="CompatibilityPolicy.KnownTools" /> order.</summary>
        public readonly List<CompatToolDefinition> DetectedTools = new List<CompatToolDefinition>();

        /// <summary>Animation-driven system names in the order they were first reported.</summary>
        public readonly List<string> AnimationDrivenSystems = new List<string>();

        /// <summary>Every finding, tool findings first (registry order), then systems.</summary>
        public readonly List<CompatFinding> Findings = new List<CompatFinding>();

        /// <summary>Findings that must be shown as warnings before optimizing.</summary>
        public readonly List<CompatFinding> Warnings = new List<CompatFinding>();

        /// <summary>Findings that make the avatar read-only for AvatarTuner.</summary>
        public readonly List<CompatFinding> Blockers = new List<CompatFinding>();

        public bool IsOptimizationAllowed
        {
            get { return Blockers.Count == 0; }
        }

        /// <summary>True when the user must inspect the generated result manually.</summary>
        public bool NeedsManualInspection
        {
            get { return Warnings.Count > 0 || Blockers.Count > 0; }
        }

        public bool HasDetections
        {
            get { return DetectedTools.Count > 0 || AnimationDrivenSystems.Count > 0; }
        }
    }

    /// <summary>
    /// The registry of tools AvatarTuner knows how to recognize, plus the rules that turn
    /// detections into findings.
    /// </summary>
    public static class CompatibilityPolicy
    {
        // ===================================================================== registry

        /// <summary>
        /// Registry order is output order: every consumer (UI, reports, tests) relies on
        /// evaluation being deterministic, so tools are always listed in this sequence.
        /// </summary>
        public static readonly CompatToolDefinition[] KnownTools =
        {
            new CompatToolDefinition
            {
                ToolId = "vrcfury",
                DisplayName = "VRCFury",
                MessageKey = "compat.vrcFuryPresent",
                Risk = CompatRisk.Low,
                Severity = CompatSeverity.Info,
                ComponentTypeFragments = new[] { "VRCFury" },
                AssemblyNameFragments = new[] { "VRCFury" },
                PackageIdFragments = new[] { "com.vrcfury.vrcfury" }
            },
            new CompatToolDefinition
            {
                ToolId = "modular-avatar",
                DisplayName = "Modular Avatar",
                MessageKey = "compat.modularAvatarPresent",
                Risk = CompatRisk.Low,
                Severity = CompatSeverity.Info,
                ComponentTypeFragments = new[] { "ModularAvatar", "nadena.dev.modular" },
                AssemblyNameFragments = new[] { "ModularAvatar" },
                PackageIdFragments = new[] { "nadena.dev.modular-avatar" }
            },
            new CompatToolDefinition
            {
                ToolId = "ndmf",
                DisplayName = "NDMF",
                MessageKey = "compat.ndmfPresent",
                Risk = CompatRisk.Low,
                Severity = CompatSeverity.Info,
                ComponentTypeFragments = new[] { "nadena.dev.ndmf" },
                AssemblyNameFragments = new[] { "ndmf" },
                PackageIdFragments = new[] { "nadena.dev.ndmf" }
            },
            new CompatToolDefinition
            {
                // Must be registered before Avatar Optimizer: "d4rkAvatarOptimizer" contains
                // the substring "AvatarOptimizer", and registry order resolves that overlap.
                ToolId = "d4rk-avatar-optimizer",
                DisplayName = "d4rkAvatarOptimizer",
                MessageKey = "compat.conflictD4rk",
                Risk = CompatRisk.High,
                Severity = CompatSeverity.Warning,
                ComponentTypeFragments = new[] { "d4rkAvatarOptimizer" },
                AssemblyNameFragments = new[] { "d4rkAvatarOptimizer" },
                PackageIdFragments = new[] { "com.d4rkavataroptimizer" }
            },
            new CompatToolDefinition
            {
                ToolId = "avatar-optimizer",
                DisplayName = "Avatar Optimizer (AAO)",
                MessageKey = "compat.conflictAvatarOptimizer",
                Risk = CompatRisk.Medium,
                Severity = CompatSeverity.Warning,
                ComponentTypeFragments = new[] { "Anatawa12.AvatarOptimizer", "AvatarOptimizer" },
                AssemblyNameFragments = new[] { "AvatarOptimizer" },
                PackageIdFragments = new[] { "com.anatawa12.avatar-optimizer" }
            }
        };

        public static CompatToolDefinition FindTool(string toolId)
        {
            if (string.IsNullOrEmpty(toolId))
            {
                return null;
            }

            for (var i = 0; i < KnownTools.Length; i++)
            {
                if (string.Equals(KnownTools[i].ToolId, toolId, StringComparison.OrdinalIgnoreCase))
                {
                    return KnownTools[i];
                }
            }

            return null;
        }

        // ===================================================================== detection

        /// <summary>Matches a component type full name against the registry.</summary>
        public static CompatToolDefinition MatchComponentType(string typeFullName)
        {
            return MatchFirst(typeFullName, 0);
        }

        /// <summary>Matches an assembly simple name against the registry.</summary>
        public static CompatToolDefinition MatchAssemblyName(string assemblyName)
        {
            return MatchFirst(assemblyName, 1);
        }

        /// <summary>Matches a package id against the registry.</summary>
        public static CompatToolDefinition MatchPackageId(string packageId)
        {
            return MatchFirst(packageId, 2);
        }

        private static CompatToolDefinition MatchFirst(string value, int fragmentSet)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            // Registry order is also match priority, so overlapping heuristics resolve the
            // same way everywhere instead of depending on the caller's evidence source.
            for (var i = 0; i < KnownTools.Length; i++)
            {
                var fragments = fragmentSet == 0
                    ? KnownTools[i].ComponentTypeFragments
                    : fragmentSet == 1
                        ? KnownTools[i].AssemblyNameFragments
                        : KnownTools[i].PackageIdFragments;

                if (ContainsAny(value, fragments))
                {
                    return KnownTools[i];
                }
            }

            return null;
        }

        private static bool ContainsAny(string value, string[] fragments)
        {
            for (var i = 0; i < fragments.Length; i++)
            {
                if (value.IndexOf(fragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        // ===================================================================== evaluation

        /// <summary>
        /// Turns detections into findings.
        ///
        /// <paramref name="avatarToolIds" /> are tools with components on the avatar itself,
        /// <paramref name="projectToolIds" /> are tools merely installed in the project, and
        /// <paramref name="animationDrivenSystemNames" /> are display names of systems such
        /// as SPS/PCS that must only ever be reported.
        ///
        /// Blocker rule: when two build-time mesh optimizers are both active on the same
        /// avatar, their merge and strip rules can disagree and the editor state no longer
        /// predicts the build result. AvatarTuner refuses to modify such an avatar and
        /// offers read-only analysis only.
        /// </summary>
        public static CompatAssessment Evaluate(
            IEnumerable<string> avatarToolIds,
            IEnumerable<string> projectToolIds,
            IEnumerable<string> animationDrivenSystemNames)
        {
            var assessment = new CompatAssessment();

            var onAvatar = ToIdSet(avatarToolIds);
            var inProject = ToIdSet(projectToolIds);

            for (var i = 0; i < KnownTools.Length; i++)
            {
                var tool = KnownTools[i];
                var seen = onAvatar.Contains(tool.ToolId) || inProject.Contains(tool.ToolId);
                if (!seen)
                {
                    continue;
                }

                assessment.DetectedTools.Add(tool);
                var finding = new CompatFinding(tool.MessageKey)
                {
                    Severity = tool.Severity,
                    Risk = tool.Risk,
                    ToolId = tool.ToolId
                };
                assessment.Findings.Add(finding);
                Route(assessment, finding);
            }

            if (onAvatar.Contains("avatar-optimizer") && onAvatar.Contains("d4rk-avatar-optimizer"))
            {
                var blocker = new CompatFinding("compat.conflictMultipleOptimizers")
                {
                    Severity = CompatSeverity.Blocker,
                    Risk = CompatRisk.High,
                    ToolId = null
                };
                assessment.Findings.Add(blocker);
                Route(assessment, blocker);
            }

            if (animationDrivenSystemNames != null)
            {
                var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in animationDrivenSystemNames)
                {
                    if (string.IsNullOrEmpty(name) || !seenNames.Add(name))
                    {
                        continue;
                    }

                    assessment.AnimationDrivenSystems.Add(name);
                    var finding = new CompatFinding("compat.animatorDrivenSystem", name)
                    {
                        Severity = CompatSeverity.Info,
                        Risk = CompatRisk.Low,
                        ToolId = null
                    };
                    assessment.Findings.Add(finding);
                    Route(assessment, finding);
                }
            }

            return assessment;
        }

        private static void Route(CompatAssessment assessment, CompatFinding finding)
        {
            switch (finding.Severity)
            {
                case CompatSeverity.Warning:
                    assessment.Warnings.Add(finding);
                    break;
                case CompatSeverity.Blocker:
                    assessment.Blockers.Add(finding);
                    break;
            }
        }

        private static HashSet<string> ToIdSet(IEnumerable<string> toolIds)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (toolIds == null)
            {
                return set;
            }

            foreach (var id in toolIds)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    set.Add(id);
                }
            }

            return set;
        }
    }
}
