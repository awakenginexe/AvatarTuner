using System;
using System.Collections.Generic;
using System.Globalization;

namespace AvatarTuner.Core
{
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
