// AvatarTuner v1.0.0 — manifest state assembly (Unity free).
//
// Turns the outputs of an optimization run into the recorded state the manifest stores and
// the change tracker compares against. Kept free of Unity types so the provenance rules —
// which are what makes partial invalidation and tamper detection trustworthy — can be unit
// tested without an editor.

using System;
using System.Collections.Generic;
using System.Globalization;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;

namespace AvatarTuner.Editor.Tracking
{
    /// <summary>Everything about a run that the state records verbatim.</summary>
    public sealed class OptimizationRunInfo
    {
        public string ConfigHash = string.Empty;
        public string EngineVersion = string.Empty;
        public string ToolVersion = string.Empty;
        public string PresetId = string.Empty;
        public DateTime UtcNow = DateTime.UtcNow;

        // ---- recorded metrics of this run (see AvatarOptimizationState)
        public long SourceBytesTotal;
        public long OutputBytesTotal;
        public int OptimizedCount;
        public int SkippedCount;
        public int ExcludedCount;
        public int ReviewCount;
    }

    public static class ManifestStateBuilder
    {
        /// <summary>
        /// Assembles the recorded state for one avatar after a successful run. The result is
        /// deterministic for identical inputs except for the timestamps taken from
        /// <see cref="OptimizationRunInfo.UtcNow" />.
        /// </summary>
        public static AvatarOptimizationState Build(
            AvatarAnalysisReport report,
            TextureGenerationResult generation,
            IList<SourceAssetState> sources,
            OptimizationRunInfo info)
        {
            if (report == null)
            {
                throw new ArgumentNullException("report");
            }

            if (generation == null)
            {
                throw new ArgumentNullException("generation");
            }

            if (info == null)
            {
                throw new ArgumentNullException("info");
            }

            var state = new AvatarOptimizationState();
            state.AvatarName = report.AvatarName ?? string.Empty;
            state.SourceRootGuid = report.AvatarRootGuid ?? string.Empty;
            state.SourceRootPath = report.AvatarRootPath ?? string.Empty;
            state.ConfigHash = info.ConfigHash ?? string.Empty;
            state.EngineVersionAtOptimization = info.EngineVersion ?? string.Empty;
            state.ToolVersionAtOptimization = info.ToolVersion ?? string.Empty;
            state.PresetId = info.PresetId ?? string.Empty;
            state.SourceBytesTotal = info.SourceBytesTotal;
            state.OutputBytesTotal = info.OutputBytesTotal;
            state.OptimizedCount = info.OptimizedCount;
            state.SkippedCount = info.SkippedCount;
            state.ExcludedCount = info.ExcludedCount;
            state.ReviewCount = info.ReviewCount;

            var stamp = info.UtcNow.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            state.LastAttemptUtc = stamp;

            if (generation.Succeeded)
            {
                state.Status = OptimizationStatus.Optimized;
                state.LastError = string.Empty;
                state.LastSuccessUtc = stamp;
            }
            else
            {
                state.Status = OptimizationStatus.OptimizationFailed;
                state.LastError = generation.Cancelled ? "error.cancelled" : "error.optimizeFailed";
                state.LastSuccessUtc = string.Empty;
            }

            if (sources != null)
            {
                for (var i = 0; i < sources.Count; i++)
                {
                    if (sources[i] != null && !string.IsNullOrEmpty(sources[i].Guid))
                    {
                        state.Sources.Add(sources[i]);
                    }
                }
            }

            for (var i = 0; i < generation.Generated.Count; i++)
            {
                var generated = FromGeneratedAsset(generation.Generated[i]);
                if (generated != null)
                {
                    state.Generated.Add(generated);
                }
            }

            return state;
        }

        /// <summary>One generated texture asset becomes one recorded output with provenance.</summary>
        public static GeneratedAssetState FromGeneratedAsset(GeneratedTextureAsset asset)
        {
            if (asset == null || string.IsNullOrEmpty(asset.GeneratedPath))
            {
                return null;
            }

            var state = new GeneratedAssetState();
            state.Guid = asset.GeneratedGuid ?? string.Empty;
            state.Path = asset.GeneratedPath;
            state.Kind = "texture";
            state.TargetBytes = asset.PlannedTargetBytes;
            state.ContentHash = asset.ContentHash ?? string.Empty;
            state.PlanHash = asset.PlanHashToken ?? string.Empty;
            if (!string.IsNullOrEmpty(asset.SourceGuid))
            {
                state.DerivedFromGuids.Add(asset.SourceGuid);
            }

            return state;
        }

        /// <summary>A scan texture becomes a tracked source with its role.</summary>
        public static SourceAssetState FromTexture(TextureEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Guid))
            {
                return null;
            }

            return new SourceAssetState(entry.Guid, entry.Path ?? string.Empty, "texture");
        }

        /// <summary>
        /// The hash of the optimization configuration (preset policy + per-texture
        /// overrides). Changing either must invalidate every output, so the token covers
        /// every field that can change a decision.
        /// </summary>
        public static string ConfigHash(
            TexturePolicy policy,
            IDictionary<string, TextureOverride> overrides)
        {
            if (policy == null)
            {
                throw new ArgumentNullException("policy");
            }

            var tokens = new List<string>();
            tokens.Add(PresetCatalog.ToHashToken(policy));

            if (overrides != null)
            {
                var guids = new List<string>(overrides.Keys);
                guids.Sort(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < guids.Count; i++)
                {
                    var ovr = overrides[guids[i]];
                    if (ovr != null && ovr.HasAnyEffect)
                    {
                        // The key is hashed with the value so swapping overrides between
                        // textures changes the configuration hash.
                        tokens.Add(StableHash.NormalizePathToken(guids[i]) + ":" + ovr.ToHashToken());
                    }
                }
            }

            return StableHash.CombineOrdered(tokens);
        }
    }
}
