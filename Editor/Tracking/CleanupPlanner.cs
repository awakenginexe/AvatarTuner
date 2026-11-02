// AvatarTuner v1.0.0 — generated-asset cleanup policy (Unity free).
//
// Deleting files is the most dangerous thing AvatarTuner does, so the rules live here,
// isolated and unit tested:
//   1. Only paths recorded in the manifest may be deleted as an avatar's outputs.
//   2. Only paths under the generated root may ever be deleted, period.
//   3. The manifest file itself is never a deletion candidate.
//   4. "Orphan" means a file under the generated root that no manifest entry records.

using System;
using System.Collections.Generic;
using AvatarTuner.Core;

namespace AvatarTuner.Editor.Tracking
{
    public static class CleanupPlanner
    {
        /// <summary>Every generated file recorded for one avatar.</summary>
        public static List<string> AvatarOutputs(AvatarOptimizationState state)
        {
            var paths = new List<string>();
            if (state == null)
            {
                return paths;
            }

            for (var i = 0; i < state.Generated.Count; i++)
            {
                if (!string.IsNullOrEmpty(state.Generated[i].Path))
                {
                    paths.Add(state.Generated[i].Path);
                }
            }

            return paths;
        }

        /// <summary>
        /// Recorded outputs of the previous run that a regeneration no longer produces.
        /// These are the stale copies that must be removed when a run completes.
        /// </summary>
        public static List<string> RemovedOutputs(
            AvatarOptimizationState oldState,
            AvatarOptimizationState newState)
        {
            var removed = new List<string>();
            if (oldState == null)
            {
                return removed;
            }

            var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (newState != null)
            {
                for (var i = 0; i < newState.Generated.Count; i++)
                {
                    if (!string.IsNullOrEmpty(newState.Generated[i].Path))
                    {
                        kept.Add(StableHash.NormalizePathToken(newState.Generated[i].Path));
                    }
                }
            }

            for (var i = 0; i < oldState.Generated.Count; i++)
            {
                var path = oldState.Generated[i].Path;
                if (!string.IsNullOrEmpty(path) &&
                    !kept.Contains(StableHash.NormalizePathToken(path)))
                {
                    removed.Add(path);
                }
            }

            return removed;
        }

        /// <summary>
        /// Files present under the generated root that no avatar in the manifest records.
        /// The manifest file itself is never reported.
        /// </summary>
        public static List<string> Orphans(
            IEnumerable<string> existingFiles,
            Manifest manifest,
            string manifestPath)
        {
            var orphans = new List<string>();
            if (existingFiles == null)
            {
                return orphans;
            }

            var recorded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (manifest != null)
            {
                for (var a = 0; a < manifest.Avatars.Count; a++)
                {
                    var outputs = AvatarOutputs(manifest.Avatars[a]);
                    for (var i = 0; i < outputs.Count; i++)
                    {
                        recorded.Add(StableHash.NormalizePathToken(outputs[i]));
                    }
                }
            }

            var normalizedManifest = string.IsNullOrEmpty(manifestPath)
                ? null
                : StableHash.NormalizePathToken(manifestPath);

            foreach (var file in existingFiles)
            {
                if (string.IsNullOrEmpty(file))
                {
                    continue;
                }

                var normalized = StableHash.NormalizePathToken(file);
                if (normalized == normalizedManifest || recorded.Contains(normalized))
                {
                    continue;
                }

                orphans.Add(file);
            }

            return orphans;
        }

        /// <summary>
        /// The hard safety gate for every deletion: non-empty, under the generated root,
        /// never the manifest file. Callers must pass every path through this.
        /// </summary>
        public static bool IsSafeToDelete(string path, string generatedRoot, string manifestPath)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(generatedRoot))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(manifestPath) &&
                string.Equals(StableHash.NormalizePathToken(path), StableHash.NormalizePathToken(manifestPath),
                    StringComparison.Ordinal))
            {
                return false;
            }

            return IsUnderRoot(path, generatedRoot);
        }

        /// <summary>
        /// Path containment with a separator boundary: "Assets/Gen/a.png" is under
        /// "Assets/Gen", but "Assets/Gen-Evil/a.png" is not.
        /// </summary>
        public static bool IsUnderRoot(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root))
            {
                return false;
            }

            var normalizedPath = StableHash.NormalizePathToken(path);
            var normalizedRoot = StableHash.NormalizePathToken(root).TrimEnd('/');

            return normalizedPath.Length > normalizedRoot.Length &&
                   normalizedPath.StartsWith(normalizedRoot, StringComparison.Ordinal) &&
                   normalizedPath[normalizedRoot.Length] == '/';
        }

        /// <summary>
        /// The deepest folder that contains every given file. Used to scope tamper
        /// detection to one avatar's outputs: as more output kinds are added next to the
        /// textures folder, the common ancestor automatically widens to the avatar folder.
        /// Case of the first path is preserved for filesystem use.
        /// </summary>
        public static string CommonFolder(IList<string> paths)
        {
            if (paths == null || paths.Count == 0)
            {
                return string.Empty;
            }

            string common = null;
            for (var i = 0; i < paths.Count; i++)
            {
                var folder = ParentFolder(paths[i]);
                if (folder == null)
                {
                    continue;
                }

                if (common == null)
                {
                    common = folder;
                    continue;
                }

                var left = common.Split('/');
                var right = folder.Split('/');
                var shared = 0;
                while (shared < left.Length && shared < right.Length &&
                       string.Equals(left[shared], right[shared], StringComparison.OrdinalIgnoreCase))
                {
                    shared++;
                }

                common = string.Join("/", left, 0, shared);
            }

            return common ?? string.Empty;
        }

        private static string ParentFolder(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var normalized = path.Replace('\\', '/');
            var separator = normalized.LastIndexOf('/');
            return separator <= 0 ? null : normalized.Substring(0, separator);
        }
    }
}
