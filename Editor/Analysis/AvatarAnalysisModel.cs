// AvatarTuner v1.0.0 — avatar analysis report model.
//
// The model is deliberately Unity free: the scanner fills it from live objects, but every
// summary, dedup and reporting rule operates on plain data so it can be reasoned about (and
// unit tested) without an editor. Asset references are GUID based so the report survives
// domain reloads and can be compared against change tracking state.

using System;
using System.Collections.Generic;

namespace AvatarTuner.Editor.Analysis
{
    /// <summary>Where a dependency was observed.</summary>
    public enum DependencyConfidence
    {
        /// <summary>Seen directly on a renderer, material or animator in the avatar hierarchy.</summary>
        Confirmed = 0,

        /// <summary>Present in the asset dependency graph but not directly referenced. Build-time
        /// tools (VRCFury, Modular Avatar, NDMF) may add or remove it at build time.</summary>
        PossibleBuildTime
    }

    /// <summary>How an asset is used across the avatar.</summary>
    public enum SharingKind
    {
        Unique = 0,

        /// <summary>Referenced by more than one place in this avatar.</summary>
        SharedInAvatar,

        /// <summary>Also referenced by assets outside this avatar. Never modified in place.</summary>
        SharedOutsideAvatar
    }

    /// <summary>One texture and everything the analyzer knows about it.</summary>
    public sealed class TextureEntry
    {
        public string Guid;
        public string Path;
        public string Name;

        public int Width;
        public int Height;
        public int SourceWidth;
        public int SourceHeight;
        public int CurrentMipCount;
        // Zero means the imported texture shape cannot be safely valued.
        public int CurrentFaceCount = 1;

        /// <summary>Format name as reported by the importer for the active build target, e.g. "BC7".</summary>
        public string CurrentFormatName;
        public bool CurrentFormatKnown;

        public bool HasAlphaChannel;
        public bool AlphaIsTransparency;
        public bool IsNormalMap;
        public bool MipMapsEnabled;
        public bool SrgbTexture;

        public int MaxSizeOverride;

        /// <summary>Material property names that bind this texture.</summary>
        public readonly List<string> BoundProperties = new List<string>();

        /// <summary>Shaders that sample this texture.</summary>
        public readonly List<string> UsedByShaders = new List<string>();

        /// <summary>Materials in this avatar that reference the texture.</summary>
        public readonly List<string> UsedByMaterialGuids = new List<string>();

        /// <summary>Every avatar reference plus outside references (GUID paths).</summary>
        public SharingKind Sharing = SharingKind.Unique;

        /// <summary>Raw source file size on disk in bytes (0 when unknown).</summary>
        public long SourceFileSizeBytes;

        /// <summary>SHA-256 of the source file, used for duplicate detection.</summary>
        public string ContentHash;

        /// <summary>Detected usage, merged across every material binding that touches the texture.</summary>
        public AvatarTuner.Core.TextureUsageKind Usage = AvatarTuner.Core.TextureUsageKind.Unknown;

        public AvatarTuner.Core.SemanticCategory Category = AvatarTuner.Core.SemanticCategory.Unknown;
        public AvatarTuner.Core.SemanticConfidence CategoryConfidence = AvatarTuner.Core.SemanticConfidence.Low;
        public readonly List<AvatarTuner.Core.SemanticReason> CategoryReasons = new List<AvatarTuner.Core.SemanticReason>();
        public readonly List<string> RendererPaths = new List<string>();

        /// <summary>Why the usage was assigned. Kept so the UI can explain the decision.</summary>
        public readonly List<string> UsageReasons = new List<string>();
    }

    /// <summary>One material and its texture bindings.</summary>
    public sealed class MaterialEntry
    {
        public string Guid;
        public string Path;
        public string Name;

        public string ShaderName;
        public string ShaderGuid;

        /// <summary>True when the shader is not shipped with Unity and not recognized.</summary>
        public bool ShaderIsCustom;

        /// <summary>Texture property name to texture GUID.</summary>
        public readonly List<KeyValuePair<string, string>> TextureBindings = new List<KeyValuePair<string, string>>();

        /// <summary>Renderers in this avatar that use the material.</summary>
        public readonly List<string> UsedByRendererPaths = new List<string>();

        public bool UsedInMaterialSwapAnimation;

        public SharingKind Sharing = SharingKind.Unique;
    }

    /// <summary>One renderer (mesh or skinned mesh) in the avatar hierarchy.</summary>
    public sealed class RendererEntry
    {
        /// <summary>Hierarchy path of the renderer GameObject.</summary>
        public string Path;
        public bool IsSkinned;
        public bool GameObjectActive;
        public bool GameObjectActiveInHierarchy;

        public string MeshGuid;
        public string MeshName;
        public string RootBoneName;
        public int TriangleCount;
        public int VertexCount;
        public int SubMeshCount;

        public int BlendShapeCount;
        public int BoneCount;

        public readonly List<string> MaterialGuids = new List<string>();

        /// <summary>True when this renderer is the only one using its mesh.</summary>
        public bool MeshIsUnique;
    }

    /// <summary>One animator controller found in the avatar.</summary>
    public sealed class AnimatorEntry
    {
        public string Guid;
        public string Path;
        public string Name;
        public bool IsAnimatorOnAvatarRoot;

        public int LayerCount;
        public int StateCount;
        public int ClipCount;

        public bool HasFxLayer;

        /// <summary>Material object references found in animation curves (material swaps).</summary>
        public readonly List<string> MaterialSwapTargets = new List<string>();

        /// <summary>Paths of GameObjects referenced by animation curves.</summary>
        public readonly List<string> AnimatedObjectPaths = new List<string>();
    }

    /// <summary>A dynamic-bone style component (VRChat PhysBone / Contact or similar).</summary>
    public sealed class DynamicComponentEntry
    {
        public string TypeFullName;
        public string Path;
        public string KindKey;

        /// <summary>Transforms affected by the component.</summary>
        public int AffectedTransformCount;
    }

    /// <summary>A third-party tool marker found on the avatar.</summary>
    public sealed class ToolMarkerEntry
    {
        public string ToolId;
        public string DisplayName;
        public string Path;
        public string ComponentType;

        /// <summary>Translation key describing what this tool's presence means.</summary>
        public string MessageKey;

        /// <summary>How risky it is to run AvatarTuner alongside this tool.</summary>
        public AvatarTuner.Core.CompatRisk Risk;
    }

    /// <summary>Full report produced by <see cref="AvatarScanner" />.</summary>
    public sealed class AvatarAnalysisReport
    {
        public string AvatarName;
        public string AvatarRootPath;
        public string AvatarRootGuid;

        public bool ScanSucceeded;
        public string ErrorMessage;

        public readonly List<TextureEntry> Textures = new List<TextureEntry>();
        public readonly List<MaterialEntry> Materials = new List<MaterialEntry>();
        public readonly List<RendererEntry> Renderers = new List<RendererEntry>();
        public readonly List<AnimatorEntry> Animators = new List<AnimatorEntry>();
        public readonly List<DynamicComponentEntry> DynamicComponents = new List<DynamicComponentEntry>();
        public readonly List<ToolMarkerEntry> ToolMarkers = new List<ToolMarkerEntry>();

        /// <summary>GameObject paths that are disabled anywhere in the hierarchy.</summary>
        public readonly List<string> InactiveObjects = new List<string>();

        /// <summary>Dependency GUIDs seen in the graph but not directly referenced.</summary>
        public readonly List<string> PossibleBuildTimeDependencies = new List<string>();

        /// <summary>Read-only findings: key + arguments, resolved through the translation catalog.</summary>
        public readonly List<AnalysisFinding> Findings = new List<AnalysisFinding>();

        /// <summary>Third-party tool compatibility assessment, filled during the scan.</summary>
        public readonly CompatibilityReport Compatibility = new CompatibilityReport();

        // ------------------------------------------------------------- aggregates

        public int TotalTriangles
        {
            get
            {
                var total = 0;
                for (var i = 0; i < Renderers.Count; i++)
                {
                    total += Renderers[i].TriangleCount;
                }

                return total;
            }
        }

        /// <summary>Triangles counted once per unique mesh, not once per renderer.</summary>
        public int UniqueMeshTriangles
        {
            get
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var total = 0;
                for (var i = 0; i < Renderers.Count; i++)
                {
                    var mesh = Renderers[i].MeshGuid;
                    if (string.IsNullOrEmpty(mesh) || !seen.Add(mesh))
                    {
                        continue;
                    }

                    total += Renderers[i].TriangleCount;
                }

                return total;
            }
        }

        public int SkinnedRendererCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < Renderers.Count; i++)
                {
                    if (Renderers[i].IsSkinned)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int TotalBlendShapes
        {
            get
            {
                var total = 0;
                for (var i = 0; i < Renderers.Count; i++)
                {
                    total += Renderers[i].BlendShapeCount;
                }

                return total;
            }
        }

        public TextureEntry FindTexture(string guid)
        {
            for (var i = 0; i < Textures.Count; i++)
            {
                if (string.Equals(Textures[i].Guid, guid, StringComparison.OrdinalIgnoreCase))
                {
                    return Textures[i];
                }
            }

            return null;
        }

        public MaterialEntry FindMaterial(string guid)
        {
            for (var i = 0; i < Materials.Count; i++)
            {
                if (string.Equals(Materials[i].Guid, guid, StringComparison.OrdinalIgnoreCase))
                {
                    return Materials[i];
                }
            }

            return null;
        }

        public void AddFinding(string messageKey, params string[] args)
        {
            Findings.Add(new AnalysisFinding(messageKey, args));
        }
    }

    /// <summary>A localized, explainable observation.</summary>
    public sealed class AnalysisFinding
    {
        public string MessageKey;
        public readonly List<string> Args = new List<string>();

        /// <summary>Risk badge for compatibility findings; low for ordinary analysis notes.</summary>
        public AvatarTuner.Core.CompatRisk Risk;

        public AnalysisFinding(string messageKey, params string[] args)
        {
            MessageKey = messageKey;
            if (args != null)
            {
                Args.AddRange(args);
            }
        }
    }

    /// <summary>Per-avatar compatibility report for third-party tools, filled by
    /// <see cref="CompatibilityScanner" />.</summary>
    public sealed class CompatibilityReport
    {
        /// <summary>Tool markers observed on the avatar itself (evidence rows).</summary>
        public readonly List<ToolMarkerEntry> DetectedTools = new List<ToolMarkerEntry>();

        /// <summary>Informational findings: tool presence notes, animation-driven systems.</summary>
        public readonly List<AnalysisFinding> Notices = new List<AnalysisFinding>();

        /// <summary>Findings that must be shown as warnings before optimizing.</summary>
        public readonly List<AnalysisFinding> Warnings = new List<AnalysisFinding>();

        /// <summary>Findings that make the avatar read-only for AvatarTuner.</summary>
        public readonly List<AnalysisFinding> Blockers = new List<AnalysisFinding>();

        public bool IsOptimizationAllowed
        {
            get { return Blockers.Count == 0; }
        }
    }
}
