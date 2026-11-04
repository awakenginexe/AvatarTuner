// AvatarTuner v1.0.0 — generated-asset manifest persistence.
//
// The manifest is the single source of truth about what AvatarTuner produced. Everything
// destructive in this file goes through CleanupPlanner's safety gate: only paths recorded
// in the manifest, only under the generated root, never the manifest itself.
//
// Persistence policy is deliberately strict: a corrupt or unreadable manifest is never
// overwritten (that would lose the record of other avatars' outputs and make safe cleanup
// impossible), and a manifest written by a newer AvatarTuner is left untouched.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AvatarTuner.Core;
using AvatarTuner.Editor.Optimization;
using UnityEditor;

namespace AvatarTuner.Editor.Tracking
{
    /// <summary>Result of loading the manifest; <see cref="ErrorKey" /> is null on success.</summary>
    public sealed class ManifestLoadResult
    {
        public Manifest Manifest;
        public string ErrorKey;
    }

    /// <summary>What a cleanup actually removed.</summary>
    public sealed class CleanupOutcome
    {
        public readonly List<string> Deleted = new List<string>();
        public readonly List<string> Failed = new List<string>();
    }

    public static class GeneratedAssetStore
    {
        public const string GeneratedRoot = "Assets/AvatarTuner-Generated";
        public const string ManifestPath = GeneratedRoot + "/avatartuner-manifest.json";

        /// <summary>The AvatarTuner version stamped into manifests and snapshots.</summary>
        public const string ToolVersion = "2.0.0";

        // ===================================================================== load / save

        public static ManifestLoadResult Load()
        {
            var result = new ManifestLoadResult();

            if (!File.Exists(ManifestPath))
            {
                result.Manifest = new Manifest();
                return result;
            }

            try
            {
                var json = File.ReadAllText(ManifestPath, Encoding.UTF8);

                // Probe the schema before handing the document to Manifest.FromJson so a
                // manifest from a newer build is reported as such instead of mis-parsed.
                var root = MiniJson.ParseObject(json);
                var schema = (int)MiniJson.GetLong(root, "schemaVersion", Manifest.CurrentSchemaVersion);
                if (schema > Manifest.CurrentSchemaVersion)
                {
                    result.ErrorKey = "error.manifestNewer";
                    return result;
                }

                result.Manifest = Manifest.FromJson(json);
            }
            catch (Exception)
            {
                result.ErrorKey = "error.manifestCorrupt";
            }

            return result;
        }

        public static bool Save(Manifest manifest, out string errorKey)
        {
            errorKey = null;
            if (manifest == null)
            {
                throw new ArgumentNullException("manifest");
            }

            manifest.ToolVersion = ToolVersion;
            manifest.ModifiedUtc = Stamp(DateTime.UtcNow);

            var temporary = Path.Combine(GeneratedRoot, ".manifest-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                if (!TextureOptimizer.EnsureFolder(GeneratedRoot))
                {
                    errorKey = "error.ioFailed";
                    return false;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(temporary));
                File.WriteAllText(temporary, manifest.ToJson(true), new UTF8Encoding(false));
                if (File.Exists(ManifestPath)) File.Replace(temporary, ManifestPath, null);
                else File.Move(temporary, ManifestPath);
                AssetDatabase.ImportAsset(ManifestPath);
                return true;
            }
            catch (Exception)
            {
                errorKey = "error.ioFailed";
                return false;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        // ===================================================================== build application

        /// <summary>
        /// Stores the direct-scene-application state (bindings for restore + verification).
        /// Never deletes or rewrites generated outputs.
        /// </summary>
        public static bool SetApplicationState(
            string sourceRootGuid,
            bool applied,
            bool partial,
            string appliedUtc,
            IList<AppliedRendererBinding> bindings,
            IList<AppliedMaterialClone> clones,
            IList<AppliedAnimationBinding> animations,
            out string errorKey)
        {
            errorKey = string.Empty;
            try
            {
                var load = Load();
                if (!string.IsNullOrEmpty(load.ErrorKey))
                {
                    errorKey = load.ErrorKey;
                    return false;
                }

                var avatar = load.Manifest == null ? null : load.Manifest.FindAvatar(sourceRootGuid);
                if (avatar == null)
                {
                    errorKey = "build.error.noOutputs";
                    return false;
                }

                avatar.AppliedToAvatar = applied;
                avatar.AppliedPartially = partial;
                avatar.AppliedUtc = appliedUtc ?? string.Empty;
                avatar.RendererBindings.Clear();
                if (bindings != null)
                {
                    avatar.RendererBindings.AddRange(bindings);
                }

                avatar.MaterialClones.Clear();
                if (clones != null)
                {
                    avatar.MaterialClones.AddRange(clones);
                }

                avatar.AnimationBindings.Clear();
                if (animations != null)
                {
                    avatar.AnimationBindings.AddRange(animations);
                }

                return Save(load.Manifest, out errorKey);
            }
            catch (Exception)
            {
                errorKey = "error.unexpected";
                return false;
            }
        }

        /// <summary>
        /// Updates only the per-avatar "Use Optimized Textures During Build" setting.
        /// Unlike RecordRun this never deletes or rewrites generated outputs. Changing the
        /// setting clears previous build-application failure evidence.
        /// </summary>
        public static bool SetBuildApplicationSetting(
            string sourceRootGuid, bool configured, bool enabled, out string errorKey)
        {
            errorKey = string.Empty;
            try
            {
                var load = Load();
                if (!string.IsNullOrEmpty(load.ErrorKey))
                {
                    errorKey = load.ErrorKey;
                    return false;
                }

                var avatar = load.Manifest == null ? null : load.Manifest.FindAvatar(sourceRootGuid);
                if (avatar == null)
                {
                    errorKey = "build.error.noOutputs";
                    return false;
                }

                avatar.BuildApplicationConfigured = configured;
                avatar.UseOptimizedTexturesOnBuild = enabled;
                avatar.LastBuildApplicationError = string.Empty;
                return Save(load.Manifest, out errorKey);
            }
            catch (Exception)
            {
                errorKey = "error.unexpected";
                return false;
            }
        }

        /// <summary>
        /// Records build-time application evidence (timestamp + error key) on the manifest.
        /// This is evidence of a build application only - a VRChat upload is never observed
        /// and never claimed.
        /// </summary>
        public static bool RecordBuildApplication(
            string sourceRootGuid, string applicationErrorKey, string utcStamp, out string errorKey)
        {
            errorKey = string.Empty;
            try
            {
                var load = Load();
                if (!string.IsNullOrEmpty(load.ErrorKey))
                {
                    errorKey = load.ErrorKey;
                    return false;
                }

                var avatar = load.Manifest == null ? null : load.Manifest.FindAvatar(sourceRootGuid);
                if (avatar == null)
                {
                    return false;
                }

                avatar.LastBuildApplicationUtc = utcStamp ?? string.Empty;
                avatar.LastBuildApplicationError = applicationErrorKey ?? string.Empty;
                return Save(load.Manifest, out errorKey);
            }
            catch (Exception)
            {
                errorKey = "error.unexpected";
                return false;
            }
        }

        // ===================================================================== regeneration

        /// <summary>
        /// Records a completed run for one avatar, replacing its previous record. Outputs
        /// from the previous run are removed only after the new recovery record is saved.
        /// </summary>
        public static CleanupOutcome RecordRun(AvatarOptimizationState state, out string errorKey)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }

            var outcome = new CleanupOutcome();
            var load = Load();
            if (load.ErrorKey != null)
            {
                // Never overwrite a manifest we could not fully understand.
                errorKey = load.ErrorKey;
                return outcome;
            }

            var previous = load.Manifest.FindAvatar(state.SourceRootGuid);
            load.Manifest.Upsert(state);
            if (!Save(load.Manifest, out errorKey)) return outcome;
            if (previous != null) DeleteAll(CleanupPlanner.RemovedOutputs(previous, state), outcome);
            return outcome;
        }

        // ===================================================================== cleanup

        /// <summary>Removes one avatar's outputs and its manifest record.</summary>
        public static CleanupOutcome RemoveAvatar(string sourceRootGuid, out string errorKey)
        {
            var outcome = new CleanupOutcome();
            var load = Load();
            if (load.ErrorKey != null)
            {
                errorKey = load.ErrorKey;
                return outcome;
            }

            var state = load.Manifest.FindAvatar(sourceRootGuid);
            if (state != null)
            {
                DeleteAll(CleanupPlanner.AvatarOutputs(state), outcome);
                load.Manifest.Remove(sourceRootGuid);
            }

            Save(load.Manifest, out errorKey);
            return outcome;
        }

        /// <summary>
        /// Deletes files under the generated root that the manifest does not record. With a
        /// corrupt or newer manifest this refuses to act: unknown provenance means unknown
        /// ownership.
        /// </summary>
        public static CleanupOutcome CleanupOrphans(out string errorKey)
        {
            var outcome = new CleanupOutcome();
            var load = Load();
            if (load.ErrorKey != null)
            {
                errorKey = load.ErrorKey;
                return outcome;
            }

            errorKey = null;
            if (!Directory.Exists(GeneratedRoot))
            {
                return outcome;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(GeneratedRoot, "*", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                errorKey = "error.ioFailed";
                return outcome;
            }

            var candidates = new List<string>();
            for (var i = 0; i < files.Length; i++)
            {
                // Unity manages .meta files together with their assets.
                if (!files[i].EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(files[i].Replace('\\', '/'));
                }
            }

            DeleteAll(CleanupPlanner.Orphans(candidates, load.Manifest, ManifestPath), outcome);
            return outcome;
        }

        // ===================================================================== helpers

        private static void DeleteAll(List<string> paths, CleanupOutcome outcome)
        {
            for (var i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                if (!CleanupPlanner.IsSafeToDelete(path, GeneratedRoot, ManifestPath))
                {
                    outcome.Failed.Add(path);
                    continue;
                }

                try
                {
                    if (File.Exists(path) && AssetDatabase.DeleteAsset(path))
                    {
                        outcome.Deleted.Add(path);
                    }
                    else if (File.Exists(path))
                    {
                        outcome.Failed.Add(path);
                    }
                }
                catch (Exception)
                {
                    outcome.Failed.Add(path);
                }
            }
        }

        internal static string Stamp(DateTime utc)
        {
            return utc.ToUniversalTime().ToString(
                "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
