// AvatarTuner v1.0.0 — change-tracking integration.
//
// Builds the "current world" view the change analyzer compares the recorded manifest
// against, and wires the result to the UI:
//   - sources come from a fresh avatar scan, de-duplicated through Core's
//     DependencyRegistry and fingerprinted through IFingerprintProvider;
//   - generated outputs are observed by hashing the files that really exist, which is
//     what turns ChangeAnalyzer into tamper, missing-file and orphan detection.

using System;
using System.Collections.Generic;
using System.IO;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using UnityEngine;

namespace AvatarTuner.Editor.Tracking
{
    public static class ChangeTrackingService
    {
        /// <summary>
        /// Assembles the current snapshot for one avatar. <paramref name="configHash" />
        /// must be the hash of the configuration the user would optimize with now (see
        /// <see cref="ManifestStateBuilder.ConfigHash" />).
        /// </summary>
        public static CurrentSnapshot BuildSnapshot(
            AvatarOptimizationState recorded,
            AvatarAnalysisReport report,
            string configHash,
            IFingerprintProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException("provider");
            }

            var snapshot = new CurrentSnapshot();
            snapshot.ConfigHash = configHash ?? string.Empty;
            snapshot.EngineVersion = Application.unityVersion;
            snapshot.ToolVersion = GeneratedAssetStore.ToolVersion;

            var registry = new DependencyRegistry();
            if (report != null)
            {
                CollectSources(report, registry);
            }
            else if (recorded != null)
            {
                // Without a fresh scan the recorded inputs are compared against their
                // current fingerprints, so the status still reflects source/importer
                // changes and generated-file tampering before any analysis runs.
                for (var i = 0; i < recorded.Sources.Count; i++)
                {
                    var source = recorded.Sources[i];
                    if (!string.IsNullOrEmpty(source.Guid))
                    {
                        registry.Add(source.Guid, source.Path, source.Role, "recorded");
                    }
                }
            }

            registry.PopulateFingerprints(provider);
            snapshot.Sources.AddRange(registry.ToStates());

            CollectObservedGenerated(recorded, snapshot);
            return snapshot;
        }

        /// <summary>Runs the analyzer with a live snapshot.</summary>
        public static ChangeReport Analyze(
            AvatarOptimizationState recorded,
            AvatarAnalysisReport report,
            string configHash)
        {
            var snapshot = BuildSnapshot(recorded, report, configHash, new UnityFingerprintProvider());
            return ChangeAnalyzer.Analyze(recorded, snapshot);
        }

        // ===================================================================== sources

        private static void CollectSources(AvatarAnalysisReport report, DependencyRegistry registry)
        {
            for (var i = 0; i < report.Textures.Count; i++)
            {
                var entry = report.Textures[i];
                if (!string.IsNullOrEmpty(entry.Guid))
                {
                    registry.Add(entry.Guid, entry.Path, "texture", "scan");
                }
            }

            for (var i = 0; i < report.Materials.Count; i++)
            {
                var entry = report.Materials[i];
                if (!string.IsNullOrEmpty(entry.Guid))
                {
                    registry.Add(entry.Guid, entry.Path, "material", "scan");
                }
            }

            for (var i = 0; i < report.Renderers.Count; i++)
            {
                var meshGuid = report.Renderers[i].MeshGuid;
                if (!string.IsNullOrEmpty(meshGuid))
                {
                    registry.Add(
                        meshGuid,
                        UnityEditor.AssetDatabase.GUIDToAssetPath(meshGuid),
                        "mesh",
                        report.Renderers[i].Path);
                }
            }

            for (var i = 0; i < report.Animators.Count; i++)
            {
                var entry = report.Animators[i];
                if (!string.IsNullOrEmpty(entry.Guid))
                {
                    registry.Add(entry.Guid, entry.Path, "animator", "scan");
                }
            }
        }

        // ===================================================================== generated

        private static void CollectObservedGenerated(AvatarOptimizationState recorded, CurrentSnapshot snapshot)
        {
            if (recorded == null)
            {
                return;
            }

            var outputs = CleanupPlanner.AvatarOutputs(recorded);
            var folder = CleanupPlanner.CommonFolder(outputs);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                // Every recorded file missing is then reported by the analyzer itself.
                return;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                return;
            }

            for (var i = 0; i < files.Length; i++)
            {
                if (files[i].EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var observed = new GeneratedAssetState();
                observed.Path = files[i].Replace('\\', '/');
                try
                {
                    observed.ContentHash = StableHash.Sha256Hex(File.ReadAllBytes(files[i]));
                }
                catch (Exception)
                {
                    observed.ContentHash = string.Empty;
                }

                snapshot.Generated.Add(observed);
            }
        }
    }
}
