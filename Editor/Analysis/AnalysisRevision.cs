using System;
using System.Collections.Generic;
using System.Globalization;
using AvatarTuner.Core;
using AvatarTuner.Editor.Apply;

namespace AvatarTuner.Editor.Analysis
{
    public static class AnalysisRevision
    {
        public static string Compute(AvatarAnalysisReport report, IEnumerable<ObservedSlot> slots,
            Func<string, string> dependencyHash)
        {
            if (report == null || !report.ScanSucceeded) return string.Empty;
            var tokens = new List<string>();
            var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Add(tokens, "avatar", report.AvatarName, report.AvatarRootGuid, report.Compatibility.IsOptimizationAllowed.ToString());
            foreach (var entry in report.Textures)
            {
                dependencies.Add(entry.Guid ?? string.Empty);
                Add(tokens, "texture", entry.Guid, entry.Name, entry.Path, entry.Width.ToString(CultureInfo.InvariantCulture),
                    entry.Height.ToString(CultureInfo.InvariantCulture), entry.CurrentFormatName, entry.CurrentMipCount.ToString(CultureInfo.InvariantCulture),
                    entry.Usage.ToString(), entry.Category.ToString(), StableHash.Combine(entry.BoundProperties));
            }
            foreach (var entry in report.Materials)
            {
                dependencies.Add(entry.Guid ?? string.Empty);
                dependencies.Add(entry.ShaderGuid ?? string.Empty);
                Add(tokens, "material", entry.Guid, entry.Name, entry.ShaderName, entry.UsedInMaterialSwapAnimation.ToString());
                foreach (var binding in entry.TextureBindings) Add(tokens, "binding", entry.Guid, binding.Key, binding.Value);
            }
            foreach (var entry in report.Renderers)
            {
                dependencies.Add(entry.MeshGuid ?? string.Empty);
                Add(tokens, "renderer", entry.Path, entry.MeshGuid, entry.MeshName, entry.RootBoneName, entry.GameObjectActiveInHierarchy.ToString(),
                    StableHash.CombineOrdered(entry.MaterialGuids));
            }
            foreach (var entry in report.Animators)
            {
                dependencies.Add(entry.Guid ?? string.Empty);
                Add(tokens, "animator", entry.Guid, entry.Path, StableHash.Combine(entry.AnimatedObjectPaths), StableHash.Combine(entry.MaterialSwapTargets));
            }
            foreach (var entry in report.ToolMarkers) Add(tokens, "tool", entry.ToolId, entry.Path, entry.ComponentType, entry.Risk.ToString());
            foreach (var entry in report.DynamicComponents) Add(tokens, "dynamic", entry.Path, entry.TypeFullName, entry.KindKey);
            foreach (var guid in report.PossibleBuildTimeDependencies) dependencies.Add(guid);
            if (slots != null)
                foreach (var slot in slots)
                    Add(tokens, "slot", slot.RendererPath, slot.SlotIndex.ToString(CultureInfo.InvariantCulture), slot.MaterialGuid);
            foreach (var guid in dependencies)
                if (!string.IsNullOrEmpty(guid)) Add(tokens, "dependency", guid, dependencyHash == null ? string.Empty : dependencyHash(guid));
            return StableHash.Combine(tokens);
        }

        private static void Add(List<string> tokens, params string[] values)
        {
            tokens.Add(StableHash.CombineOrdered(values));
        }
    }
}
