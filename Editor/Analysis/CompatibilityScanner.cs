// AvatarTuner v1.0.0 — third-party tool compatibility scanner.
//
// This layer is pure observation: it finds evidence that known tools exist (components on
// the avatar, assemblies and packages in the project) and hands it to
// AvatarTuner.Core.CompatibilityPolicy, which owns every product decision. The scanner
// never modifies tool components and never even touches their serialized settings.
//
// All detection is name based (types, assemblies, package ids) so AvatarTuner compiles and
// runs in projects with or without any of these tools installed.

using System;
using System.Collections.Generic;
using AvatarTuner.Core;
using UnityEditor;
using UnityEngine;

namespace AvatarTuner.Editor.Analysis
{
    /// <summary>Fills the <see cref="CompatibilityReport" /> section of an analysis report.</summary>
    public static class CompatibilityScanner
    {
        public static void Scan(GameObject avatarRoot, AvatarAnalysisReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException("report");
            }

            var target = report.Compatibility;
            var onAvatar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inProject = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CollectAvatarMarkers(avatarRoot, target, onAvatar);
            CollectProjectTools(inProject);

            var systems = new List<string>();
            CollectAnimationDrivenSystems(report, systems);

            var assessment = CompatibilityPolicy.Evaluate(onAvatar, inProject, systems);
            ApplyFindings(assessment, target);
        }

        // ===================================================================== avatar evidence

        private static void CollectAvatarMarkers(
            GameObject avatarRoot,
            CompatibilityReport target,
            HashSet<string> onAvatar)
        {
            if (avatarRoot == null)
            {
                return;
            }

            var components = avatarRoot.GetComponentsInChildren<Component>(true);
            var seenMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue; // missing script placeholder: no type to match
                }

                var typeFullName = component.GetType().FullName;
                var tool = CompatibilityPolicy.MatchComponentType(typeFullName);
                if (tool == null)
                {
                    continue;
                }

                var path = AvatarScanner.HierarchyPath(component.transform);
                var markerKey = tool.ToolId + "|" + path + "|" + typeFullName;
                if (!seenMarkers.Add(markerKey))
                {
                    continue;
                }

                onAvatar.Add(tool.ToolId);
                target.DetectedTools.Add(new ToolMarkerEntry
                {
                    ToolId = tool.ToolId,
                    DisplayName = tool.DisplayName,
                    Path = path,
                    ComponentType = typeFullName,
                    MessageKey = tool.MessageKey,
                    Risk = tool.Risk
                });
            }
        }

        // ===================================================================== project evidence

        private static void CollectProjectTools(HashSet<string> inProject)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                string name;
                try
                {
                    name = assemblies[i].GetName().Name;
                }
                catch (Exception)
                {
                    continue; // a dynamic or reflection-only assembly without a usable name
                }

                var tool = CompatibilityPolicy.MatchAssemblyName(name);
                if (tool != null)
                {
                    inProject.Add(tool.ToolId);
                }
            }

            try
            {
                var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
                for (var i = 0; i < packages.Length; i++)
                {
                    var tool = CompatibilityPolicy.MatchPackageId(packages[i].name);
                    if (tool != null)
                    {
                        inProject.Add(tool.ToolId);
                    }
                }
            }
            catch (Exception)
            {
                // Package enumeration is a convenience signal; assemblies and components
                // already cover detection when it is unavailable.
            }
        }

        // ===================================================================== systems & findings

        private static void CollectAnimationDrivenSystems(AvatarAnalysisReport report, List<string> systems)
        {
            for (var i = 0; i < report.DynamicComponents.Count; i++)
            {
                var entry = report.DynamicComponents[i];
                if (!string.Equals(entry.KindKey, "finding.kind.animationDrivenSystem", StringComparison.Ordinal))
                {
                    continue;
                }

                var name = SimpleTypeName(entry.TypeFullName);
                if (!string.IsNullOrEmpty(name))
                {
                    systems.Add(name);
                }
            }
        }

        private static void ApplyFindings(CompatAssessment assessment, CompatibilityReport target)
        {
            for (var i = 0; i < assessment.Findings.Count; i++)
            {
                var finding = assessment.Findings[i];
                var mapped = new AnalysisFinding(finding.MessageKey, finding.Args.ToArray());
                mapped.Risk = finding.Risk;

                switch (finding.Severity)
                {
                    case CompatSeverity.Warning:
                        target.Warnings.Add(mapped);
                        break;
                    case CompatSeverity.Blocker:
                        target.Blockers.Add(mapped);
                        break;
                    default:
                        target.Notices.Add(mapped);
                        break;
                }
            }
        }

        private static string SimpleTypeName(string typeFullName)
        {
            if (string.IsNullOrEmpty(typeFullName))
            {
                return typeFullName;
            }

            var separator = typeFullName.LastIndexOfAny(new[] { '.', '+' });
            return separator < 0 ? typeFullName : typeFullName.Substring(separator + 1);
        }
    }
}
