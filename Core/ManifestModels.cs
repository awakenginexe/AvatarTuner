using System;
using System.Collections.Generic;
using System.Globalization;

namespace AvatarTuner.Core
{
    public enum MemoryReportBasis { PlannedEstimate, ImportedResult, VRChatVerifiedResult }

    /// <summary>The six states an avatar's optimization can be in.</summary>
    public enum OptimizationStatus
    {
        /// <summary>No optimization has ever been recorded for this avatar.</summary>
        NotOptimized = 0,

        /// <summary>Optimized and every tracked input is unchanged.</summary>
        Optimized = 1,

        /// <summary>Some tracked inputs changed; a subset of outputs is stale.</summary>
        ChangesDetected = 2,

        /// <summary>Something changed that invalidates the whole output set.</summary>
        ReoptimizationRequired = 3,

        /// <summary>The last optimization attempt failed.</summary>
        OptimizationFailed = 4,

        /// <summary>The avatar uses constructs this tool refuses to modify safely.</summary>
        Unsupported = 5
    }

    /// <summary>
    /// One source asset the optimization depends on, with the fingerprints recorded at the
    /// time of the last successful run.
    /// </summary>
    public sealed class SourceAssetState
    {
        /// <summary>Unity asset GUID. The primary key for change detection.</summary>
        public string Guid;

        /// <summary>Project-relative path, recorded for display and for diagnosing renames.</summary>
        public string Path;

        /// <summary>
        /// Result of <c>AssetDatabase.GetAssetDependencyHash</c>, which aggregates the source
        /// asset, its .meta file, the target platform and the importer version. This is the
        /// Unity-supported dependency fingerprint; it is recorded rather than re-derived.
        /// </summary>
        public string AssetDependencyHash;

        /// <summary>
        /// AvatarTuner's own hash over the importer settings that actually affect the
        /// generated output (format, max size, mip maps, sRGB, alpha handling, compression
        /// quality). Recorded separately because <see cref="AssetDependencyHash" /> mixes in
        /// platform and importer version, which would invalidate outputs for unrelated
        /// reasons.
        /// </summary>
        public string ImporterSettingsHash;

        /// <summary>What the asset is used for: texture, material, mesh, animator, clip...</summary>
        public string Role;

        public SourceAssetState()
        {
        }

        public SourceAssetState(string guid, string path, string role)
        {
            Guid = guid;
            Path = path;
            Role = role;
        }

        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            dict["guid"] = Guid ?? string.Empty;
            dict["path"] = Path ?? string.Empty;
            dict["role"] = Role ?? string.Empty;
            dict["assetDependencyHash"] = AssetDependencyHash ?? string.Empty;
            dict["importerSettingsHash"] = ImporterSettingsHash ?? string.Empty;
            return dict;
        }

        public static SourceAssetState FromDictionary(Dictionary<string, object> dict)
        {
            var state = new SourceAssetState();
            if (dict == null)
            {
                return state;
            }

            state.Guid = MiniJson.GetString(dict, "guid", string.Empty);
            state.Path = MiniJson.GetString(dict, "path", string.Empty);
            state.Role = MiniJson.GetString(dict, "role", string.Empty);
            state.AssetDependencyHash = MiniJson.GetString(dict, "assetDependencyHash", string.Empty);
            state.ImporterSettingsHash = MiniJson.GetString(dict, "importerSettingsHash", string.Empty);
            return state;
        }

    }

    /// <summary>
    /// One file AvatarTuner produced. Generated files are always stored outside the source
    /// asset's own folder and are fully described here so that they can be regenerated,
    /// verified and cleaned up without touching the original.
    /// </summary>
    public sealed class GeneratedAssetState
    {
        public string Guid;
        public string Path;

        /// <summary>texture, material, mesh, animation, prefab, manifest.</summary>
        public string Kind;

        /// <summary>SHA-256 of the file content, used to detect manual edits after generation.</summary>
        public string ContentHash;

        /// <summary>Hash of the plan/decision that produced this file.</summary>
        public string PlanHash;

        /// <summary>Planned GPU memory of this output (0 when not a texture).</summary>
        public long TargetBytes;
        public int ImportedWidth, ImportedHeight, ImportedMipCount;
        public string ImportedFormat;
        public bool ImportedMipmaps, ImportedBytesKnown;
        public long ImportedBytes;

        /// <summary>Source GUIDs this file was derived from. Drives partial invalidation.</summary>
        public readonly List<string> DerivedFromGuids = new List<string>();

        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            dict["guid"] = Guid ?? string.Empty;
            dict["path"] = Path ?? string.Empty;
            dict["kind"] = Kind ?? string.Empty;
            dict["contentHash"] = ContentHash ?? string.Empty;
            dict["planHash"] = PlanHash ?? string.Empty;
            dict["targetBytes"] = TargetBytes;
            dict["importedWidth"] = (long)ImportedWidth;
            dict["importedHeight"] = (long)ImportedHeight;
            dict["importedFormat"] = ImportedFormat ?? string.Empty;
            dict["importedMipmaps"] = ImportedMipmaps;
            dict["importedMipCount"] = (long)ImportedMipCount;
            dict["importedBytesKnown"] = ImportedBytesKnown;
            dict["importedBytes"] = ImportedBytes;
            dict["derivedFrom"] = new List<object>(DerivedFromGuids.ToArray());
            return dict;
        }

        public static GeneratedAssetState FromDictionary(Dictionary<string, object> dict)
        {
            var state = new GeneratedAssetState();
            if (dict == null)
            {
                return state;
            }

            state.Guid = MiniJson.GetString(dict, "guid", string.Empty);
            state.Path = MiniJson.GetString(dict, "path", string.Empty);
            state.Kind = MiniJson.GetString(dict, "kind", string.Empty);
            state.ContentHash = MiniJson.GetString(dict, "contentHash", string.Empty);
            state.PlanHash = MiniJson.GetString(dict, "planHash", string.Empty);
            state.TargetBytes = MiniJson.GetLong(dict, "targetBytes", 0);
            state.ImportedWidth = (int)MiniJson.GetLong(dict, "importedWidth", 0);
            state.ImportedHeight = (int)MiniJson.GetLong(dict, "importedHeight", 0);
            state.ImportedFormat = MiniJson.GetString(dict, "importedFormat", string.Empty);
            state.ImportedMipmaps = MiniJson.GetBool(dict, "importedMipmaps", false);
            state.ImportedMipCount = (int)MiniJson.GetLong(dict, "importedMipCount", 0);
            state.ImportedBytesKnown = MiniJson.GetBool(dict, "importedBytesKnown", false);
            state.ImportedBytes = MiniJson.GetLong(dict, "importedBytes", 0);

            var derived = MiniJson.GetList(dict, "derivedFrom");
            if (derived != null)
            {
                for (var i = 0; i < derived.Count; i++)
                {
                    var guid = derived[i] as string;
                    if (!string.IsNullOrEmpty(guid))
                    {
                        state.DerivedFromGuids.Add(guid);
                    }
                }
            }

            return state;
        }
    }

    /// <summary>One renderer material slot that was repointed during direct scene application.</summary>
    public sealed class AppliedRendererBinding
    {
        /// <summary>Hierarchy path of the renderer under the avatar root.</summary>
        public string RendererPath;

        public int SlotIndex;

        /// <summary>GUID of the original (untouched) source material asset.</summary>
        public string OriginalMaterialGuid;

        /// <summary>GUID of the generated optimized material clone that was assigned.</summary>
        public string AppliedMaterialGuid;

        /// <summary>Asset path of the generated material clone.</summary>
        public string AppliedMaterialPath;

        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            dict["rendererPath"] = RendererPath ?? string.Empty;
            dict["slotIndex"] = (long)SlotIndex;
            dict["originalMaterialGuid"] = OriginalMaterialGuid ?? string.Empty;
            dict["appliedMaterialGuid"] = AppliedMaterialGuid ?? string.Empty;
            dict["appliedMaterialPath"] = AppliedMaterialPath ?? string.Empty;
            return dict;
        }

        public static AppliedRendererBinding FromDictionary(Dictionary<string, object> dict)
        {
            var state = new AppliedRendererBinding();
            if (dict == null)
            {
                return state;
            }

            state.RendererPath = MiniJson.GetString(dict, "rendererPath", string.Empty);
            state.SlotIndex = (int)MiniJson.GetLong(dict, "slotIndex", 0);
            state.OriginalMaterialGuid = MiniJson.GetString(dict, "originalMaterialGuid", string.Empty);
            state.AppliedMaterialGuid = MiniJson.GetString(dict, "appliedMaterialGuid", string.Empty);
            state.AppliedMaterialPath = MiniJson.GetString(dict, "appliedMaterialPath", string.Empty);
            return state;
        }
    }

    /// <summary>A generated optimized material clone and its untouched original.</summary>
    public sealed class AppliedMaterialClone
    {
        public string OriginalMaterialGuid;
        public string OriginalMaterialPath;
        public string GeneratedMaterialPath;

        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            dict["originalMaterialGuid"] = OriginalMaterialGuid ?? string.Empty;
            dict["originalMaterialPath"] = OriginalMaterialPath ?? string.Empty;
            dict["generatedMaterialPath"] = GeneratedMaterialPath ?? string.Empty;
            return dict;
        }

        public static AppliedMaterialClone FromDictionary(Dictionary<string, object> dict)
        {
            var state = new AppliedMaterialClone();
            if (dict == null)
            {
                return state;
            }

            state.OriginalMaterialGuid = MiniJson.GetString(dict, "originalMaterialGuid", string.Empty);
            state.OriginalMaterialPath = MiniJson.GetString(dict, "originalMaterialPath", string.Empty);
            state.GeneratedMaterialPath = MiniJson.GetString(dict, "generatedMaterialPath", string.Empty);
            return state;
        }
    }

    /// <summary>An animation asset remapped during direct scene application.</summary>
    public sealed class AppliedAnimationBinding
    {
        public string OriginalClipPath;
        public string GeneratedClipPath;
        public string OriginalControllerPath;
        public string GeneratedControllerPath;

        /// <summary>Hierarchy path of the animator that received the cloned controller.</summary>
        public string AnimatorPath;

        public int RemappedReferenceCount;

        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            dict["originalClipPath"] = OriginalClipPath ?? string.Empty;
            dict["generatedClipPath"] = GeneratedClipPath ?? string.Empty;
            dict["originalControllerPath"] = OriginalControllerPath ?? string.Empty;
            dict["generatedControllerPath"] = GeneratedControllerPath ?? string.Empty;
            dict["animatorPath"] = AnimatorPath ?? string.Empty;
            dict["remappedReferenceCount"] = (long)RemappedReferenceCount;
            return dict;
        }

        public static AppliedAnimationBinding FromDictionary(Dictionary<string, object> dict)
        {
            var state = new AppliedAnimationBinding();
            if (dict == null)
            {
                return state;
            }

            state.OriginalClipPath = MiniJson.GetString(dict, "originalClipPath", string.Empty);
            state.GeneratedClipPath = MiniJson.GetString(dict, "generatedClipPath", string.Empty);
            state.OriginalControllerPath = MiniJson.GetString(dict, "originalControllerPath", string.Empty);
            state.GeneratedControllerPath = MiniJson.GetString(dict, "generatedControllerPath", string.Empty);
            state.AnimatorPath = MiniJson.GetString(dict, "animatorPath", string.Empty);
            state.RemappedReferenceCount = (int)MiniJson.GetLong(dict, "remappedReferenceCount", 0);
            return state;
        }
    }

    /// <summary>Everything AvatarTuner knows about one avatar's optimization.</summary>
    public sealed class AvatarOptimizationState
    {
        public string AvatarName;
        public string SourceRootGuid;
        public string SourceRootPath;

        /// <summary>Hash of the optimization configuration (preset + overrides + settings).</summary>
        public string ConfigHash;

        /// <summary>Unity engine version at the time of the last successful run.</summary>
        public string EngineVersionAtOptimization;

        /// <summary>AvatarTuner version at the time of the last successful run.</summary>
        public string ToolVersionAtOptimization;

        public string PresetId;
        public OptimizationStatus Status;
        public string LastError;

        /// <summary>ISO-8601 UTC timestamp of the last successful run, or empty.</summary>
        public string LastSuccessUtc;

        /// <summary>ISO-8601 UTC timestamp of the last attempt, successful or not.</summary>
        public string LastAttemptUtc;

        // ---- recorded metrics of the last run (0 when unknown; older manifests omit them)
        // so the results can be restored after reopening the tool. Source/Output bytes are
        // GPU texture memory totals computed at run time from the same shared numbers the
        // UI showed; they are computed values, never measurements.

        /// <summary>GPU texture memory of the source textures at the last run.</summary>
        public long SourceBytesTotal;

        /// <summary>GPU texture memory of the optimized output at the last run.</summary>
        public long OutputBytesTotal;
        public bool HasSource, HasOutput;
        public int UnknownSourceCount, UnknownOutputCount;
        public MemoryReportBasis Basis;
        public int MemoryAccountingVersion = VrcTextureMemory.AccountingVersion;

        /// <summary>Textures that received a generated output.</summary>
        public int OptimizedCount;

        /// <summary>Textures the generator skipped (kept at source).</summary>
        public int SkippedCount;

        /// <summary>Textures the user excluded.</summary>
        public int ExcludedCount;

        /// <summary>Textures flagged for manual review.</summary>
        public int ReviewCount;

        // ---- build-time application ("Use Optimized Textures During Build")
        // The setting is per avatar and requires explicit user consent. The build-time
        // pass records its own evidence here; a successful build application is never
        // confused with a successful VRChat upload - we never see the upload at all.

        /// <summary>True once the user explicitly chose the build-application setting.</summary>
        public bool BuildApplicationConfigured;

        /// <summary>The user's consent to apply generated textures at build time.</summary>
        public bool UseOptimizedTexturesOnBuild;

        /// <summary>ISO-8601 UTC timestamp of the last build-time application attempt.</summary>
        public string LastBuildApplicationUtc;

        /// <summary>Error key of the last failed build application, or empty when it succeeded.</summary>
        public string LastBuildApplicationError;

        // ---- direct scene application (optimized materials assigned to the scene avatar)

        /// <summary>True when optimized materials were assigned to the scene avatar.</summary>
        public bool AppliedToAvatar;

        /// <summary>True when some references could not be safely remapped (partial apply).</summary>
        public bool AppliedPartially;

        /// <summary>ISO-8601 UTC timestamp of the last scene application, or empty.</summary>
        public string AppliedUtc;

        /// <summary>Restore info: which renderer slot originally referenced which material.</summary>
        public readonly List<AppliedRendererBinding> RendererBindings =
            new List<AppliedRendererBinding>();

        /// <summary>Source material → generated optimized material clone mapping.</summary>
        public readonly List<AppliedMaterialClone> MaterialClones = new List<AppliedMaterialClone>();

        /// <summary>Remapped animation assets (material-swap object references).</summary>
        public readonly List<AppliedAnimationBinding> AnimationBindings =
            new List<AppliedAnimationBinding>();

        public readonly List<SourceAssetState> Sources = new List<SourceAssetState>();
        public readonly List<GeneratedAssetState> Generated = new List<GeneratedAssetState>();

        /// <summary>Finds a source entry by GUID, or null.</summary>
        public SourceAssetState FindSource(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            for (var i = 0; i < Sources.Count; i++)
            {
                if (string.Equals(Sources[i].Guid, guid, StringComparison.OrdinalIgnoreCase))
                {
                    return Sources[i];
                }
            }

            return null;
        }

        /// <summary>Finds a generated entry by path, or null.</summary>
        public GeneratedAssetState FindGenerated(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

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

        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            dict["avatarName"] = AvatarName ?? string.Empty;
            dict["sourceRootGuid"] = SourceRootGuid ?? string.Empty;
            dict["sourceRootPath"] = SourceRootPath ?? string.Empty;
            dict["configHash"] = ConfigHash ?? string.Empty;
            dict["engineVersion"] = EngineVersionAtOptimization ?? string.Empty;
            dict["toolVersion"] = ToolVersionAtOptimization ?? string.Empty;
            dict["presetId"] = PresetId ?? string.Empty;
            dict["status"] = Status.ToString();
            dict["lastError"] = LastError ?? string.Empty;
            dict["lastSuccessUtc"] = LastSuccessUtc ?? string.Empty;
            dict["lastAttemptUtc"] = LastAttemptUtc ?? string.Empty;
            dict["sourceBytesTotal"] = SourceBytesTotal;
            dict["outputBytesTotal"] = OutputBytesTotal;
            dict["hasSourceMemory"] = HasSource;
            dict["hasOutputMemory"] = HasOutput;
            dict["memoryAccountingVersion"] = (long)MemoryAccountingVersion;
            dict["unknownSourceCount"] = (long)UnknownSourceCount;
            dict["unknownOutputCount"] = (long)UnknownOutputCount;
            dict["memoryBasis"] = Basis.ToString();
            dict["optimizedCount"] = (long)OptimizedCount;
            dict["skippedCount"] = (long)SkippedCount;
            dict["excludedCount"] = (long)ExcludedCount;
            dict["reviewCount"] = (long)ReviewCount;
            dict["buildApplicationConfigured"] = BuildApplicationConfigured;
            dict["useOptimizedTexturesOnBuild"] = UseOptimizedTexturesOnBuild;
            dict["lastBuildApplicationUtc"] = LastBuildApplicationUtc ?? string.Empty;
            dict["lastBuildApplicationError"] = LastBuildApplicationError ?? string.Empty;
            dict["appliedToAvatar"] = AppliedToAvatar;
            dict["appliedPartially"] = AppliedPartially;
            dict["appliedUtc"] = AppliedUtc ?? string.Empty;

            var bindings = new List<object>();
            for (var i = 0; i < RendererBindings.Count; i++)
            {
                bindings.Add(RendererBindings[i].ToDictionary());
            }

            dict["rendererBindings"] = bindings;

            var clones = new List<object>();
            for (var i = 0; i < MaterialClones.Count; i++)
            {
                clones.Add(MaterialClones[i].ToDictionary());
            }

            dict["materialClones"] = clones;

            var animations = new List<object>();
            for (var i = 0; i < AnimationBindings.Count; i++)
            {
                animations.Add(AnimationBindings[i].ToDictionary());
            }

            dict["animationBindings"] = animations;

            var sources = new List<object>();
            for (var i = 0; i < Sources.Count; i++)
            {
                sources.Add(Sources[i].ToDictionary());
            }

            dict["sources"] = sources;

            var generated = new List<object>();
            for (var i = 0; i < Generated.Count; i++)
            {
                generated.Add(Generated[i].ToDictionary());
            }

            dict["generated"] = generated;
            return dict;
        }

        public static AvatarOptimizationState FromDictionary(Dictionary<string, object> dict)
        {
            var state = new AvatarOptimizationState();
            if (dict == null)
            {
                return state;
            }

            state.AvatarName = MiniJson.GetString(dict, "avatarName", string.Empty);
            state.SourceRootGuid = MiniJson.GetString(dict, "sourceRootGuid", string.Empty);
            state.SourceRootPath = MiniJson.GetString(dict, "sourceRootPath", string.Empty);
            state.ConfigHash = MiniJson.GetString(dict, "configHash", string.Empty);
            state.EngineVersionAtOptimization = MiniJson.GetString(dict, "engineVersion", string.Empty);
            state.ToolVersionAtOptimization = MiniJson.GetString(dict, "toolVersion", string.Empty);
            state.PresetId = MiniJson.GetString(dict, "presetId", string.Empty);
            state.LastError = MiniJson.GetString(dict, "lastError", string.Empty);
            state.LastSuccessUtc = MiniJson.GetString(dict, "lastSuccessUtc", string.Empty);
            state.LastAttemptUtc = MiniJson.GetString(dict, "lastAttemptUtc", string.Empty);
            state.SourceBytesTotal = MiniJson.GetLong(dict, "sourceBytesTotal", 0);
            state.OutputBytesTotal = MiniJson.GetLong(dict, "outputBytesTotal", 0);
            state.HasSource = MiniJson.GetBool(dict, "hasSourceMemory", false);
            state.HasOutput = MiniJson.GetBool(dict, "hasOutputMemory", false);
            state.MemoryAccountingVersion = (int)MiniJson.GetLong(dict, "memoryAccountingVersion", 0);
            state.UnknownSourceCount = (int)MiniJson.GetLong(dict, "unknownSourceCount", state.SourceBytesTotal > 0 && !dict.ContainsKey("hasSourceMemory") ? 1 : 0);
            state.UnknownOutputCount = (int)MiniJson.GetLong(dict, "unknownOutputCount", state.OutputBytesTotal > 0 && !dict.ContainsKey("hasOutputMemory") ? 1 : 0);
            MemoryReportBasis basis;
            state.Basis = Enum.TryParse(MiniJson.GetString(dict, "memoryBasis", "PlannedEstimate"), out basis) &&
                Enum.IsDefined(typeof(MemoryReportBasis), basis) ? basis : MemoryReportBasis.PlannedEstimate;
            state.OptimizedCount = (int)MiniJson.GetLong(dict, "optimizedCount", 0);
            state.SkippedCount = (int)MiniJson.GetLong(dict, "skippedCount", 0);
            state.ExcludedCount = (int)MiniJson.GetLong(dict, "excludedCount", 0);
            state.ReviewCount = (int)MiniJson.GetLong(dict, "reviewCount", 0);
            state.BuildApplicationConfigured = MiniJson.GetBool(dict, "buildApplicationConfigured", false);
            state.UseOptimizedTexturesOnBuild = MiniJson.GetBool(dict, "useOptimizedTexturesOnBuild", false);
            state.LastBuildApplicationUtc = MiniJson.GetString(dict, "lastBuildApplicationUtc", string.Empty);
            state.LastBuildApplicationError = MiniJson.GetString(dict, "lastBuildApplicationError", string.Empty);
            state.AppliedToAvatar = MiniJson.GetBool(dict, "appliedToAvatar", false);
            state.AppliedPartially = MiniJson.GetBool(dict, "appliedPartially", false);
            state.AppliedUtc = MiniJson.GetString(dict, "appliedUtc", string.Empty);

            var bindings = MiniJson.GetList(dict, "rendererBindings");
            if (bindings != null)
            {
                for (var i = 0; i < bindings.Count; i++)
                {
                    state.RendererBindings.Add(
                        AppliedRendererBinding.FromDictionary(bindings[i] as Dictionary<string, object>));
                }
            }

            var clones = MiniJson.GetList(dict, "materialClones");
            if (clones != null)
            {
                for (var i = 0; i < clones.Count; i++)
                {
                    state.MaterialClones.Add(
                        AppliedMaterialClone.FromDictionary(clones[i] as Dictionary<string, object>));
                }
            }

            var animations = MiniJson.GetList(dict, "animationBindings");
            if (animations != null)
            {
                for (var i = 0; i < animations.Count; i++)
                {
                    state.AnimationBindings.Add(
                        AppliedAnimationBinding.FromDictionary(animations[i] as Dictionary<string, object>));
                }
            }

            OptimizationStatus status;
            var statusText = MiniJson.GetString(dict, "status", "NotOptimized");
            state.Status = Enum.TryParse(statusText, true, out status) ? status : OptimizationStatus.NotOptimized;

            var sources = MiniJson.GetList(dict, "sources");
            if (sources != null)
            {
                for (var i = 0; i < sources.Count; i++)
                {
                    state.Sources.Add(SourceAssetState.FromDictionary(sources[i] as Dictionary<string, object>));
                }
            }

            var generated = MiniJson.GetList(dict, "generated");
            if (generated != null)
                for (var i = 0; i < generated.Count; i++)
                {
                    state.Generated.Add(GeneratedAssetState.FromDictionary(generated[i] as Dictionary<string, object>));
                }

            return state;
        }
    }

    /// <summary>The persisted, version-controlled optimization manifest.</summary>
    public sealed class Manifest
    {
        /// <summary>
        /// Schema version of the manifest format. Bumped whenever the meaning of an existing
        /// field changes so that older manifests can be migrated instead of misread.
        /// </summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public string ToolVersion = string.Empty;
        public string ModifiedUtc = string.Empty;
        public readonly List<AvatarOptimizationState> Avatars = new List<AvatarOptimizationState>();

        /// <summary>Finds the state for an avatar root GUID, or null.</summary>
        public AvatarOptimizationState FindAvatar(string sourceRootGuid)
        {
            if (string.IsNullOrEmpty(sourceRootGuid))
            {
                return null;
            }

            for (var i = 0; i < Avatars.Count; i++)
            {
                if (string.Equals(Avatars[i].SourceRootGuid, sourceRootGuid, StringComparison.OrdinalIgnoreCase))
                {
                    return Avatars[i];
                }
            }

            return null;
        }

        /// <summary>Replaces or appends an avatar state, keeping the list keyed by root GUID.</summary>
        public void Upsert(AvatarOptimizationState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }

            for (var i = 0; i < Avatars.Count; i++)
            {
                if (string.Equals(Avatars[i].SourceRootGuid, state.SourceRootGuid, StringComparison.OrdinalIgnoreCase))
                {
                    Avatars[i] = state;
                    return;
                }
            }

            Avatars.Add(state);
        }

        /// <summary>Removes an avatar state, returning true when one was removed.</summary>
        public bool Remove(string sourceRootGuid)
        {
            for (var i = 0; i < Avatars.Count; i++)
            {
                if (string.Equals(Avatars[i].SourceRootGuid, sourceRootGuid, StringComparison.OrdinalIgnoreCase))
                {
                    Avatars.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        public string ToJson(bool pretty)
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            dict["schemaVersion"] = (long)SchemaVersion;
            dict["toolVersion"] = ToolVersion ?? string.Empty;
            dict["modifiedUtc"] = ModifiedUtc ?? string.Empty;

            var avatars = new List<object>();
            for (var i = 0; i < Avatars.Count; i++)
            {
                avatars.Add(Avatars[i].ToDictionary());
            }

            dict["avatars"] = avatars;
            return MiniJson.Serialize(dict, pretty);
        }

        /// <summary>
        /// Parses a manifest. A schema newer than this build understands is rejected with a
        /// clear error rather than silently mis-parsed.
        /// </summary>
        public static Manifest FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new Manifest();
            }

            var root = MiniJson.ParseObject(json);
            var manifest = new Manifest();

            var schema = (int)MiniJson.GetLong(root, "schemaVersion", CurrentSchemaVersion);
            if (schema > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    "AvatarTuner manifest schema " + schema.ToString(CultureInfo.InvariantCulture) +
                    " is newer than this build understands (" +
                    CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture) +
                    "). Update AvatarTuner before using this project.");
            }

            manifest.SchemaVersion = schema;
            manifest.ToolVersion = MiniJson.GetString(root, "toolVersion", string.Empty);
            manifest.ModifiedUtc = MiniJson.GetString(root, "modifiedUtc", string.Empty);

            var avatars = MiniJson.GetList(root, "avatars");
            if (avatars != null)
            {
                for (var i = 0; i < avatars.Count; i++)
                {
                    manifest.Avatars.Add(AvatarOptimizationState.FromDictionary(avatars[i] as Dictionary<string, object>));
                }
            }

            return manifest;
        }
    }

    /// <summary>
    /// The set of assets an avatar depends on, de-duplicated by GUID.
    ///
    /// The analysis engine must not double count a texture shared by twelve materials, so
    /// referential data is tracked through counts and referencer lists while identity is
    /// keyed by GUID alone.
    /// </summary>
    public sealed class DependencyRegistry
    {
        private sealed class Entry
        {
            public SourceAssetState State;
            public readonly HashSet<string> Referencers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Number of distinct assets registered.</summary>
        public int Count
        {
            get { return _entries.Count; }
        }

        /// <summary>
        /// Registers a use of an asset. Calling this repeatedly with the same GUID adds
        /// referencers but never adds a second entry.
        /// </summary>
        public void Add(string guid, string path, string role, string referencer)
        {
            if (string.IsNullOrEmpty(guid))
            {
                throw new ArgumentException("guid must not be empty", "guid");
            }

            Entry entry;
            if (!_entries.TryGetValue(guid, out entry))
            {
                entry = new Entry();
                entry.State = new SourceAssetState(guid, path, role);
                _entries[guid] = entry;
            }
            else if (!string.IsNullOrEmpty(path) && string.IsNullOrEmpty(entry.State.Path))
            {
                entry.State.Path = path;
            }

            if (!string.IsNullOrEmpty(role))
            {
                entry.Roles.Add(role);
                if (string.IsNullOrEmpty(entry.State.Role))
                {
                    entry.State.Role = role;
                }
            }

            if (!string.IsNullOrEmpty(referencer))
            {
                entry.Referencers.Add(referencer);
            }
        }

        /// <summary>True when the GUID has been registered.</summary>
        public bool Contains(string guid)
        {
            return !string.IsNullOrEmpty(guid) && _entries.ContainsKey(guid);
        }

        /// <summary>Number of distinct assets that reference the given asset.</summary>
        public int ReferenceCount(string guid)
        {
            Entry entry;
            return !string.IsNullOrEmpty(guid) && _entries.TryGetValue(guid, out entry) ? entry.Referencers.Count : 0;
        }

        /// <summary>Names of the assets that reference the given asset.</summary>
        public List<string> GetReferencers(string guid)
        {
            Entry entry;
            if (string.IsNullOrEmpty(guid) || !_entries.TryGetValue(guid, out entry))
            {
                return new List<string>();
            }

            return new List<string>(entry.Referencers);
        }

        /// <summary>All roles the asset was registered under.</summary>
        public List<string> GetRoles(string guid)
        {
            Entry entry;
            if (string.IsNullOrEmpty(guid) || !_entries.TryGetValue(guid, out entry))
            {
                return new List<string>();
            }

            return new List<string>(entry.Roles);
        }

        /// <summary>Snapshot of the de-duplicated asset states, ordered by GUID for determinism.</summary>
        public List<SourceAssetState> ToStates()
        {
            var guids = new List<string>(_entries.Keys);
            guids.Sort(StringComparer.OrdinalIgnoreCase);

            var result = new List<SourceAssetState>(guids.Count);
            for (var i = 0; i < guids.Count; i++)
            {
                result.Add(_entries[guids[i]].State);
            }

            return result;
        }

        /// <summary>
        /// Fills in the fingerprint fields of every registered asset using the supplied
        /// provider. Keeps the fingerprinting policy in one place instead of scattering it
        /// across the analysis code.
        /// </summary>
        public void PopulateFingerprints(IFingerprintProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException("provider");
            }

            foreach (var pair in _entries)
            {
                var state = pair.Value.State;
                state.AssetDependencyHash = provider.GetAssetDependencyHash(state.Guid, state.Path) ?? string.Empty;
                state.ImporterSettingsHash = provider.GetImporterSettingsHash(state.Guid, state.Path) ?? string.Empty;
            }
        }
    }

    /// <summary>
    /// Supplies the two fingerprints recorded for each source asset. Implemented by the
    /// editor layer so the de-duplication and storage logic stays Unity free.
    /// </summary>
    public interface IFingerprintProvider
    {
        string GetAssetDependencyHash(string guid, string path);
        string GetImporterSettingsHash(string guid, string path);
    }
}
