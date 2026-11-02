using System;
using System.Collections.Generic;

namespace AvatarTuner.Core
{
    /// <summary>Classification of a single detected change.</summary>
    public enum ChangeKind
    {
        /// <summary>An asset the optimization depends on changed.</summary>
        SourceModified,

        /// <summary>A new dependency appeared (for example a material now references a new texture).</summary>
        DependencyAdded,

        /// <summary>A dependency disappeared.</summary>
        DependencyRemoved,

        /// <summary>The importer settings relevant to the generated output changed.</summary>
        ImporterSettingsChanged,

        /// <summary>The optimization configuration changed (preset, overrides, settings).</summary>
        ConfigChanged,

        /// <summary>The Unity engine version differs from the one that produced the output.</summary>
        EngineVersionChanged,

        /// <summary>The AvatarTuner version differs from the one that produced the output.</summary>
        ToolVersionChanged,

        /// <summary>A generated file is gone.</summary>
        GeneratedFileMissing,

        /// <summary>A generated file was edited by hand after generation.</summary>
        GeneratedFileModified,

        /// <summary>A generated file is no longer produced by any current plan.</summary>
        GeneratedFileOrphaned
    }

    /// <summary>One detected change, with enough context to explain itself to the user.</summary>
    public sealed class ChangeEntry
    {
        public ChangeKind Kind;

        /// <summary>Localization key for the explanation shown in the UI.</summary>
        public string DetailKey;

        /// <summary>Arguments interpolated into the localized sentence.</summary>
        public readonly List<string> Args = new List<string>();

        /// <summary>Asset or file this change concerns. May be empty for global changes.</summary>
        public string Guid;

        /// <summary>Display path. May be empty.</summary>
        public string Path;

        /// <summary>True when this change invalidates every generated output.</summary>
        public bool InvalidatesEverything;

        /// <summary>Generated outputs invalidated by this change specifically.</summary>
        public readonly List<string> StalePaths = new List<string>();

        public ChangeEntry(ChangeKind kind, string detailKey, params string[] args)
        {
            Kind = kind;
            DetailKey = detailKey;
            if (args != null)
            {
                Args.AddRange(args);
            }
        }

        public override string ToString()
        {
            return Kind + " " + DetailKey + " " + Path;
        }
    }

    /// <summary>Outcome of comparing recorded state with what is on disk now.</summary>
    public sealed class ChangeReport
    {
        public OptimizationStatus Status = OptimizationStatus.NotOptimized;

        /// <summary>Summary key for the overall verdict.</summary>
        public string SummaryKey;

        public readonly List<ChangeEntry> Changes = new List<ChangeEntry>();

        /// <summary>Generated files that must be produced again.</summary>
        public readonly List<string> StaleGeneratedPaths = new List<string>();

        /// <summary>Generated files that are still valid and can be kept.</summary>
        public readonly List<string> ValidGeneratedPaths = new List<string>();

        /// <summary>True when at least one output is stale.</summary>
        public bool RequiresReoptimization;

        public bool HasChanges
        {
            get { return Changes.Count > 0; }
        }

        public void Add(ChangeEntry entry)
        {
            if (entry != null)
            {
                Changes.Add(entry);
            }
        }

        /// <summary>Changes of a single kind.</summary>
        public List<ChangeEntry> OfKind(ChangeKind kind)
        {
            var result = new List<ChangeEntry>();
            for (var i = 0; i < Changes.Count; i++)
            {
                if (Changes[i].Kind == kind)
                {
                    result.Add(Changes[i]);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// The observed state of the world, as opposed to the recorded state in the manifest.
    /// </summary>
    public sealed class CurrentSnapshot
    {
        public string ConfigHash = string.Empty;
        public string EngineVersion = string.Empty;
        public string ToolVersion = string.Empty;

        public readonly List<SourceAssetState> Sources = new List<SourceAssetState>();

        /// <summary>
        /// Generated files as they currently exist on disk. The <c>ContentHash</c> field is
        /// filled with the hash of the file that is really there.
        /// </summary>
        public readonly List<GeneratedAssetState> Generated = new List<GeneratedAssetState>();

        public SourceAssetState FindSource(string guid)
        {
            for (var i = 0; i < Sources.Count; i++)
            {
                if (string.Equals(Sources[i].Guid, guid, StringComparison.OrdinalIgnoreCase))
                {
                    return Sources[i];
                }
            }

            return null;
        }

        public GeneratedAssetState FindGenerated(string path)
        {
            var normalized = StableHash.NormalizePathToken(path);
            for (var i = 0; i < Generated.Count; i++)
            {
                if (StableHash.NormalizePathToken(Generated[i].Path) == normalized)
                {
                    return Generated[i];
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Compares the manifest's recorded state against the current snapshot and works out
    /// what is stale and why.
    ///
    /// Invalidation is deliberately *partial*: when only one texture changed, only the
    /// generated files derived from that texture are marked stale. Unrelated project changes
    /// never trigger a full regeneration. Global invalidation is reserved for the three
    /// inputs that genuinely affect every output: the optimization configuration, the Unity
    /// engine version and the tool version.
    /// </summary>
    public static class ChangeAnalyzer
    {
        public static ChangeReport Analyze(AvatarOptimizationState recorded, CurrentSnapshot current)
        {
            if (current == null)
            {
                throw new ArgumentNullException("current");
            }

            var report = new ChangeReport();

            if (recorded == null)
            {
                report.Status = OptimizationStatus.NotOptimized;
                report.SummaryKey = "status.notOptimized.summary";
                return report;
            }

            if (recorded.Status == OptimizationStatus.Unsupported)
            {
                report.Status = OptimizationStatus.Unsupported;
                report.SummaryKey = "status.unsupported.summary";
                return report;
            }

            // ---- global invalidators -------------------------------------------
            var configChanged = !HashesEqual(recorded.ConfigHash, current.ConfigHash);
            if (configChanged)
            {
                var entry = new ChangeEntry(ChangeKind.ConfigChanged, "change.configChanged");
                entry.InvalidatesEverything = true;
                report.Add(entry);
            }

            if (!VersionsEqual(recorded.EngineVersionAtOptimization, current.EngineVersion))
            {
                var entry = new ChangeEntry(
                    ChangeKind.EngineVersionChanged,
                    "change.engineVersionChanged",
                    recorded.EngineVersionAtOptimization ?? "?",
                    current.EngineVersion ?? "?");
                entry.InvalidatesEverything = true;
                report.Add(entry);
            }

            if (!VersionsEqual(recorded.ToolVersionAtOptimization, current.ToolVersion))
            {
                var entry = new ChangeEntry(
                    ChangeKind.ToolVersionChanged,
                    "change.toolVersionChanged",
                    recorded.ToolVersionAtOptimization ?? "?",
                    current.ToolVersion ?? "?");
                entry.InvalidatesEverything = true;
                report.Add(entry);
            }

            var globalInvalidation = report.Changes.Count > 0;

            // ---- per-source changes -------------------------------------------
            var changedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < current.Sources.Count; i++)
            {
                var now = current.Sources[i];
                var before = recorded.FindSource(now.Guid);

                if (before == null)
                {
                    var added = new ChangeEntry(ChangeKind.DependencyAdded, "change.dependencyAdded", now.Path ?? now.Guid);
                    added.Guid = now.Guid;
                    added.Path = now.Path;
                    report.Add(added);
                    changedGuids.Add(now.Guid);
                    continue;
                }

                var dependencyChanged = !HashesEqual(before.AssetDependencyHash, now.AssetDependencyHash);
                var importerChanged = !HashesEqual(before.ImporterSettingsHash, now.ImporterSettingsHash);

                if (dependencyChanged)
                {
                    var entry = new ChangeEntry(ChangeKind.SourceModified, "change.sourceModified", now.Path ?? now.Guid);
                    entry.Guid = now.Guid;
                    entry.Path = now.Path;
                    report.Add(entry);
                    changedGuids.Add(now.Guid);
                }
                else if (importerChanged)
                {
                    var entry = new ChangeEntry(ChangeKind.ImporterSettingsChanged, "change.importerSettingsChanged", now.Path ?? now.Guid);
                    entry.Guid = now.Guid;
                    entry.Path = now.Path;
                    report.Add(entry);
                    changedGuids.Add(now.Guid);
                }
            }

            for (var i = 0; i < recorded.Sources.Count; i++)
            {
                var before = recorded.Sources[i];
                if (current.FindSource(before.Guid) == null)
                {
                    var entry = new ChangeEntry(ChangeKind.DependencyRemoved, "change.dependencyRemoved", before.Path ?? before.Guid);
                    entry.Guid = before.Guid;
                    entry.Path = before.Path;
                    // Partial invalidation on purpose: only the generated outputs whose
                    // provenance includes the vanished GUID go stale. Unrelated outputs must
                    // survive so the user can do a targeted refresh instead of a full rebuild.
                    entry.InvalidatesEverything = false;
                    report.Add(entry);
                    changedGuids.Add(before.Guid);
                }
            }

            // ---- generated file integrity -------------------------------------
            var tamperedOrMissing = false;
            for (var i = 0; i < recorded.Generated.Count; i++)
            {
                var expected = recorded.Generated[i];
                var observed = current.FindGenerated(expected.Path);

                if (observed == null)
                {
                    var entry = new ChangeEntry(ChangeKind.GeneratedFileMissing, "change.generatedFileMissing", expected.Path);
                    entry.Path = expected.Path;
                    entry.StalePaths.Add(expected.Path);
                    report.Add(entry);
                    tamperedOrMissing = true;
                    continue;
                }

                if (!HashesEqual(expected.ContentHash, observed.ContentHash))
                {
                    var entry = new ChangeEntry(ChangeKind.GeneratedFileModified, "change.generatedFileModified", expected.Path);
                    entry.Path = expected.Path;
                    entry.StalePaths.Add(expected.Path);
                    report.Add(entry);
                    tamperedOrMissing = true;
                }
            }

            for (var i = 0; i < current.Generated.Count; i++)
            {
                if (recorded.FindGenerated(current.Generated[i].Path) == null)
                {
                    var entry = new ChangeEntry(ChangeKind.GeneratedFileOrphaned, "change.generatedFileOrphaned", current.Generated[i].Path);
                    entry.Path = current.Generated[i].Path;
                    report.Add(entry);
                }
            }

            // ---- work out which outputs are stale ------------------------------
            for (var i = 0; i < recorded.Generated.Count; i++)
            {
                var output = recorded.Generated[i];

                if (globalInvalidation || tamperedOrMissing)
                {
                    if (!report.StaleGeneratedPaths.Contains(output.Path))
                    {
                        report.StaleGeneratedPaths.Add(output.Path);
                    }

                    continue;
                }

                var stale = DependsOnAny(output, changedGuids);
                if (stale)
                {
                    if (!report.StaleGeneratedPaths.Contains(output.Path))
                    {
                        report.StaleGeneratedPaths.Add(output.Path);
                    }
                }
                else if (!report.ValidGeneratedPaths.Contains(output.Path))
                {
                    report.ValidGeneratedPaths.Add(output.Path);
                }
            }

            report.RequiresReoptimization = report.StaleGeneratedPaths.Count > 0;

            report.Status = Classify(recorded, report, globalInvalidation, tamperedOrMissing);
            report.SummaryKey = SummaryKeyFor(report.Status);
            return report;
        }

        private static OptimizationStatus Classify(
            AvatarOptimizationState recorded,
            ChangeReport report,
            bool globalInvalidation,
            bool tamperedOrMissing)
        {
            if (tamperedOrMissing || globalInvalidation)
            {
                return OptimizationStatus.ReoptimizationRequired;
            }

            if (recorded.Status == OptimizationStatus.OptimizationFailed)
            {
                return OptimizationStatus.OptimizationFailed;
            }

            if (!report.HasChanges)
            {
                return OptimizationStatus.Optimized;
            }

            if (report.StaleGeneratedPaths.Count == 0)
            {
                // Changes exist but nothing the tool produces is affected.
                return OptimizationStatus.Optimized;
            }

            if (report.ValidGeneratedPaths.Count > 0)
            {
                // Part of the output survives: report the milder state so the user can do a
                // targeted refresh rather than a full rebuild.
                return OptimizationStatus.ChangesDetected;
            }

            return OptimizationStatus.ReoptimizationRequired;
        }

        private static string SummaryKeyFor(OptimizationStatus status)
        {
            switch (status)
            {
                case OptimizationStatus.NotOptimized:
                    return "status.notOptimized.summary";
                case OptimizationStatus.Optimized:
                    return "status.optimized.summary";
                case OptimizationStatus.ChangesDetected:
                    return "status.changesDetected.summary";
                case OptimizationStatus.ReoptimizationRequired:
                    return "status.reoptimizationRequired.summary";
                case OptimizationStatus.OptimizationFailed:
                    return "status.optimizationFailed.summary";
                case OptimizationStatus.Unsupported:
                    return "status.unsupported.summary";
                default:
                    return "status.notOptimized.summary";
            }
        }

        private static bool DependsOnAny(GeneratedAssetState output, HashSet<string> changedGuids)
        {
            if (changedGuids.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < output.DerivedFromGuids.Count; i++)
            {
                if (changedGuids.Contains(output.DerivedFromGuids[i]))
                {
                    return true;
                }
            }

            // An output with no recorded provenance cannot be reasoned about; treat it as
            // stale rather than silently declaring it valid.
            return output.DerivedFromGuids.Count == 0;
        }

        private static bool HashesEqual(string left, string right)
        {
            var a = left ?? string.Empty;
            var b = right ?? string.Empty;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool VersionsEqual(string left, string right)
        {
            // Tolerate "2022.3" vs "2022.3.0": parse both and compare the numeric triple,
            // falling back to a raw comparison when either side does not parse.
            EngineVersion a, b;
            var parsedA = EngineVersion.TryParse(left, out a);
            var parsedB = EngineVersion.TryParse(right, out b);
            if (parsedA && parsedB)
            {
                return a == b;
            }

            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
