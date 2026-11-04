// AvatarTuner v1.0.0 — texture optimization plan builder.
//
// Turns an avatar analysis report plus the user's configuration into concrete per-texture
// plans by delegating every decision to Core's CompressionPlanner. This class only supplies
// evidence and enforces the protection rules the compatibility analysis demands:
//
//   - Textures used by materials under third-party tool components (VRCFury, Modular
//     Avatar, ...) or animation-driven systems (SPS/PCS style) are pinned. Their materials
//     must keep looking exactly as they do now, and this tool never gambles about what a
//     build-time component does to them.
//   - Everything else is planned normally, with the user's per-texture overrides.
//
// The file is Unity free on purpose: it is compiled both into the editor assembly and into
// the offline test suite, where the rules are exercised without a Unity installation.

using System;
using System.Collections.Generic;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Tracking;

namespace AvatarTuner.Editor.Optimization
{
    /// <summary>The planned outcome for one texture.</summary>
    public sealed class PlannedTexture
    {
        public string TextureGuid;
        public string TexturePath;
        public string TextureName;

        /// <summary>The planner's decision. Never null after a successful build.</summary>
        public TexturePlan Plan;

        /// <summary>The user override that was supplied, if any (not the effective one).</summary>
        public TextureOverride Override;
        public SemanticCategory Category;
        public EffectiveTextureSettings EffectiveSettings;
        public string SourceRevision;

        /// <summary>True when a third-party tool or animation-driven system pins this texture.</summary>
        public bool ProtectedByThirdParty;

        /// <summary>Hierarchy path of the component that pinned it (null when not protected).</summary>
        public string ProtectionSourcePath;

        public bool HasChanges
        {
            get { return Plan != null && !Plan.IsNoOp; }
        }
    }

    /// <summary>The full plan for one avatar.</summary>
    public sealed class TextureOptimizationPlan
    {
        public readonly List<PlannedTexture> Textures = new List<PlannedTexture>();
        public readonly List<AnalysisFinding> Findings = new List<AnalysisFinding>();

        public long SourceBytesTotal;
        public long TargetBytesTotal;
        public long SavingBytes;
        public double SavingPercent;

        public int ChangedCount;
        public int ProtectedCount;
        public int ExcludedCount;
        public int UnchangedCount;

        /// <summary>
        /// Hash of the configuration (preset policy + per-texture overrides) this plan was
        /// built with. A plan whose token differs from the current configuration is stale:
        /// it must be rebuilt before anything based on it is executed, and a stale plan is
        /// never run.
        /// </summary>
        public string ConfigHashToken;

        public bool HasChanges
        {
            get { return ChangedCount > 0; }
        }
    }

    public static class TexturePlanBuilder
    {
        /// <summary>
        /// Builds the optimization plan. Deterministic: textures are processed in path
        /// order, so the same report and configuration always produce the same plan.
        /// </summary>
        /// <param name="report">The avatar analysis report from AvatarScanner.</param>
        /// <param name="policy">The preset policy to plan with.</param>
        /// <param name="overrides">Optional user overrides keyed by texture GUID.</param>
        /// <param name="qualityByGuid">Optional pixel-derived quality evidence per texture.</param>
        public static TextureOptimizationPlan Build(
            AvatarAnalysisReport report,
            TexturePolicy policy,
            IDictionary<string, TextureOverride> overrides,
            IDictionary<string, QualityEvidence> qualityByGuid)
        {
            return BuildInternal(report, policy, overrides, qualityByGuid, null, null);
        }

        public static string AdaptiveConfigHash(TexturePolicy policy, IDictionary<string, TextureOverride> overrides,
            OptimizationSettings settings)
        {
            return StableHash.CombineOrdered(new[] { CandidateQuality.ComparisonVersion, ManifestStateBuilder.ConfigHash(policy, overrides), settings.ToHashToken() });
        }

        public static TextureOptimizationPlan BuildAdaptive(AvatarAnalysisReport report, TexturePolicy policy,
            IDictionary<string, TextureOverride> overrides, OptimizationSettings settings,
            Func<TextureEntry, TextureCandidate, CandidateQuality> evaluate)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            return BuildInternal(report, policy, overrides, null, settings, evaluate);
        }

        private static TextureOptimizationPlan BuildInternal(AvatarAnalysisReport report, TexturePolicy policy,
            IDictionary<string, TextureOverride> overrides, IDictionary<string, QualityEvidence> qualityByGuid,
            OptimizationSettings settings, Func<TextureEntry, TextureCandidate, CandidateQuality> evaluate)
        {
            if (report == null)
            {
                throw new ArgumentNullException("report");
            }

            if (policy == null)
            {
                throw new ArgumentNullException("policy");
            }

            var plan = new TextureOptimizationPlan();
            plan.ConfigHashToken = settings == null ? ManifestStateBuilder.ConfigHash(policy, overrides)
                : AdaptiveConfigHash(policy, overrides, settings);
            var protectedMaterials = ProtectedMaterials(report);

            var entries = new List<TextureEntry>(report.Textures);
            entries.Sort(CompareByPath);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var identity = string.IsNullOrEmpty(entry.Guid) ? entry.Path : entry.Guid;
                if (!string.IsNullOrEmpty(identity) && !seen.Add(identity)) continue;
                var planned = new PlannedTexture();
                planned.TextureGuid = entry.Guid;
                planned.TexturePath = entry.Path;
                planned.TextureName = entry.Name;
                planned.Category = entry.Category;
                SemanticCategory corrected;
                if (settings != null && settings.CategoryCorrections.TryGetValue(entry.Guid ?? string.Empty, out corrected))
                    planned.Category = corrected;
                planned.EffectiveSettings = settings == null ? null : settings.ResolveForOptimization(entry.Guid, planned.Category);

                TextureOverride userOverride = null;
                if (overrides != null && !string.IsNullOrEmpty(entry.Guid))
                {
                    overrides.TryGetValue(entry.Guid, out userOverride);
                }

                planned.Override = userOverride;

                string protectionPath;
                if (!string.IsNullOrEmpty(entry.Guid) && protectedMaterials.Count > 0 &&
                    IsTextureProtected(entry, protectedMaterials, out protectionPath))
                {
                    planned.ProtectedByThirdParty = true;
                    planned.ProtectionSourcePath = protectionPath;
                }

                QualityEvidence quality = null;
                if (qualityByGuid != null && !string.IsNullOrEmpty(entry.Guid))
                {
                    qualityByGuid.TryGetValue(entry.Guid, out quality);
                }

                var effectiveOverride = userOverride;
                if (planned.ProtectedByThirdParty)
                {
                    // Protection is absolute: it wins over any user override, because the
                    // material must keep looking exactly as it does today.
                    effectiveOverride = new TextureOverride { Excluded = true };
                }

                var texturePlan = settings == null
                    ? CompressionPlanner.Plan(BuildRequest(entry), policy, effectiveOverride, quality)
                    : AdaptivePlanner.Select(BuildRequest(entry), policy, effectiveOverride,
                        planned.EffectiveSettings.Settings, planned.Category,
                        evaluate == null ? null : candidate => evaluate(entry, candidate),
                        useReductionTarget: settings.Mode == OptimizationMode.Advanced,
                        standardLevel: settings.Mode == OptimizationMode.Standard ? (StandardOptimizationLevel?)settings.StandardLevel : null);
                // Candidate selection retains physical GPU arithmetic; displayed totals follow the SDK.
                var sourceMipCount = entry.CurrentMipCount > 0 ? entry.CurrentMipCount :
                    entry.Width > 0 && entry.Height > 0 ? (entry.MipMapsEnabled ? VramCalculator.MipLevelCount(entry.Width, entry.Height) : 1) : 0;
                long reportedBytes = 0;
                var sourceKnown = entry.CurrentFormatKnown && VrcTextureMemory.TryEstimateBytes(entry.Width, entry.Height,
                    texturePlan.SourceFormat, sourceMipCount, entry.CurrentFaceCount, out reportedBytes);
                texturePlan.SourceBytes = sourceKnown ? reportedBytes : 0;
                texturePlan.SourceBytesKnown = sourceKnown;
                if (texturePlan.IsNoOp)
                {
                    texturePlan.TargetBytes = texturePlan.SourceBytes;
                    texturePlan.TargetBytesKnown = sourceKnown;
                }
                else
                {
                    var targetMipCount = texturePlan.TargetMipMaps ? VramCalculator.MipLevelCount(texturePlan.TargetWidth, texturePlan.TargetHeight) : 1;
                    texturePlan.TargetBytesKnown = VrcTextureMemory.TryEstimateBytes(texturePlan.TargetWidth, texturePlan.TargetHeight,
                        texturePlan.TargetFormat, targetMipCount, 1, out reportedBytes);
                    texturePlan.TargetBytes = texturePlan.TargetBytesKnown ? reportedBytes : 0;
                }
                texturePlan.SavingUnknown = !sourceKnown || !texturePlan.TargetBytesKnown;
                texturePlan.SavingBytes = texturePlan.SourceBytes - texturePlan.TargetBytes;
                texturePlan.SavingPercent = SavingPercentOf(texturePlan.SourceBytes, texturePlan.TargetBytes);
                if (settings != null && sourceKnown && texturePlan.TargetBytesKnown && texturePlan.TargetBytes > 0)
                    texturePlan.ReductionAchieved = (double)texturePlan.SourceBytes / texturePlan.TargetBytes;

                if (planned.ProtectedByThirdParty)
                {
                    texturePlan.IsExcluded = true;
                    texturePlan.SkipReasonKey = "plan.skip.protectedThirdParty";
                    texturePlan.Notes.Clear();
                    texturePlan.AddNote("plan.note.protectedThirdParty");
                }

                planned.Plan = texturePlan;
                plan.Textures.Add(planned);

                if (planned.ProtectedByThirdParty)
                {
                    plan.ProtectedCount++;
                }
                else if (texturePlan.IsExcluded)
                {
                    plan.ExcludedCount++;
                }
                else if (texturePlan.IsNoOp)
                {
                    plan.UnchangedCount++;
                }
                else
                {
                    plan.ChangedCount++;
                }

                plan.SourceBytesTotal += texturePlan.SourceBytes;
                plan.TargetBytesTotal += texturePlan.TargetBytes;
            }

            plan.SavingBytes = plan.SourceBytesTotal - plan.TargetBytesTotal;
            plan.SavingPercent = SavingPercentOf(plan.SourceBytesTotal, plan.TargetBytesTotal);

            if (plan.ProtectedCount > 0)
            {
                plan.Findings.Add(new AnalysisFinding(
                    "opt.protectedTextures", plan.ProtectedCount.ToString()));
            }

            return plan;
        }

        // ===================================================================== protection

        /// <summary>
        /// Material GUIDs used under third-party tool or animation-driven components, mapped
        /// to the component path that pins them. A material anywhere in such a component's
        /// transform subtree is considered driven by it.
        /// </summary>
        public static Dictionary<string, string> ProtectedMaterials(AvatarAnalysisReport report)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (report == null)
            {
                return result;
            }

            var roots = new List<string>();
            for (var i = 0; i < report.ToolMarkers.Count; i++)
            {
                if (!string.IsNullOrEmpty(report.ToolMarkers[i].Path))
                {
                    roots.Add(report.ToolMarkers[i].Path);
                }
            }

            for (var i = 0; i < report.DynamicComponents.Count; i++)
            {
                var entry = report.DynamicComponents[i];
                if (string.Equals(entry.KindKey, "finding.kind.animationDrivenSystem", StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(entry.Path))
                {
                    roots.Add(entry.Path);
                }
            }

            if (roots.Count == 0)
            {
                return result;
            }

            for (var i = 0; i < report.Renderers.Count; i++)
            {
                var renderer = report.Renderers[i];
                var owner = OwnerRoot(renderer.Path, roots);
                if (owner == null)
                {
                    continue;
                }

                for (var m = 0; m < renderer.MaterialGuids.Count; m++)
                {
                    var guid = renderer.MaterialGuids[m];
                    if (!string.IsNullOrEmpty(guid) && !result.ContainsKey(guid))
                    {
                        result[guid] = owner;
                    }
                }
            }

            return result;
        }

        private static string OwnerRoot(string path, List<string> roots)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            for (var i = 0; i < roots.Count; i++)
            {
                if (IsUnder(path, roots[i]))
                {
                    return roots[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Hierarchy containment: "Arm/Prop" is under "Arm", but "Arm2" is not. Comparing
        /// on the raw string without the separator check would pin unrelated subtrees.
        /// </summary>
        internal static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root))
            {
                return false;
            }

            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return path.Length > root.Length &&
                   path.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                   path[root.Length] == '/';
        }

        private static bool IsTextureProtected(
            TextureEntry entry,
            Dictionary<string, string> protectedMaterials,
            out string protectionPath)
        {
            for (var i = 0; i < entry.UsedByMaterialGuids.Count; i++)
            {
                if (protectedMaterials.TryGetValue(entry.UsedByMaterialGuids[i], out protectionPath))
                {
                    return true;
                }
            }

            protectionPath = null;
            return false;
        }

        // ===================================================================== requests

        /// <summary>Translates scan data into the planner's evidence-based request.</summary>
        internal static TexturePlanRequest BuildRequest(TextureEntry entry)
        {
            var request = new TexturePlanRequest();
            request.SourceWidth = entry.Width;
            request.SourceHeight = entry.Height;

            TunerTextureFormat parsed;
            if (!string.IsNullOrEmpty(entry.CurrentFormatName) &&
                TextureFormatInfo.TryParse(entry.CurrentFormatName, out parsed))
            {
                request.CurrentFormat = parsed;
            }

            request.CurrentFormatKnown = entry.CurrentFormatKnown && request.CurrentFormat != TunerTextureFormat.Unknown;
            request.CurrentMaxSize = entry.MaxSizeOverride;
            request.CurrentMipMaps = entry.MipMapsEnabled;
            request.CurrentMipCount = entry.CurrentMipCount;
            request.CurrentSRGB = entry.SrgbTexture;
            request.Usage = entry.Usage;
            request.SourceHasAlpha = entry.HasAlphaChannel;
            request.AlphaChannelUsed = entry.AlphaIsTransparency ||
                                      entry.Usage == TextureUsageKind.AlbedoWithAlpha ||
                                      (entry.HasAlphaChannel && (entry.Usage == TextureUsageKind.DataMask ||
                                                                entry.Usage == TextureUsageKind.Unknown ||
                                                                entry.Usage == TextureUsageKind.Detail ||
                                                                entry.Usage == TextureUsageKind.Emission));
            request.ImporterTypeIsNormalMap = entry.IsNormalMap;
            request.FaceCount = entry.CurrentFaceCount;

            // Re-derives the name evidence the classifier uses for detail preservation
            // (faces, eyes, small text). The verdict itself is already merged into
            // entry.Usage and deliberately discarded here: entry.Usage is the merged,
            // most-conservative verdict across every binding.
            var evidence = new ClassificationEvidence();
            TextureUsageClassifier.Classify(
                entry.BoundProperties.Count > 0 ? entry.BoundProperties[0] : string.Empty,
                entry.UsedByShaders.Count > 0 ? entry.UsedByShaders[0] : string.Empty,
                entry.Name,
                entry.IsNormalMap,
                entry.HasAlphaChannel,
                entry.AlphaIsTransparency,
                evidence);
            request.Evidence = evidence;

            return request;
        }

        private static int CompareByPath(TextureEntry left, TextureEntry right)
        {
            return string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
        }

        private static double SavingPercentOf(long sourceBytes, long targetBytes)
        {
            if (sourceBytes <= 0)
            {
                return 0.0;
            }

            return (sourceBytes - targetBytes) * 100.0 / sourceBytes;
        }
    }
}
