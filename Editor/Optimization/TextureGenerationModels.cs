// AvatarTuner v1.0.0 — generated texture asset result models.
//
// Unity free on purpose: these models describe what a generation run produced and are
// consumed by the manifest/change-tracking layer and by the offline tests.

using System.Collections.Generic;
using AvatarTuner.Editor.Analysis;

namespace AvatarTuner.Editor.Optimization
{
    /// <summary>One generated texture asset and its provenance.</summary>
    public sealed class GeneratedTextureAsset
    {
        public string SourceGuid;
        public string SourcePath;
        public string GeneratedGuid;
        public string GeneratedPath;

        /// <summary>Hash token of the plan that produced it; used for change tracking.</summary>
        public string PlanHashToken;

        /// <summary>SHA-256 of the generated file as written, used for tamper detection.</summary>
        public string ContentHash;

        /// <summary>On-disk size of the generated file (0 when unreadable).</summary>
        public long GeneratedFileSizeBytes;

        /// <summary>Planned GPU memory of the generated texture.</summary>
        public long PlannedTargetBytes;
        public int ImportedWidth;
        public int ImportedHeight;
        public string ImportedFormat;
        public bool ImportedMipmaps;
        public int ImportedMipCount;
        public bool ImportedBytesKnown;
        public long ImportedBytes;
    }

    /// <summary>Outcome of a generation run.</summary>
    public sealed class TextureGenerationResult
    {
        public string OutputFolder;
        public bool Succeeded;
        public bool Cancelled;
        public int SkippedCount;

        public readonly List<GeneratedTextureAsset> Generated = new List<GeneratedTextureAsset>();
        public readonly List<AnalysisFinding> Findings = new List<AnalysisFinding>();

        /// <summary>Total planned GPU memory after optimization.</summary>
        public long PlannedTargetBytesTotal;

        internal int ErrorCount = 0;
    }
}
