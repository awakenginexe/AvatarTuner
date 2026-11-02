using System;
using System.Collections.Generic;

namespace AvatarTuner.Core
{
    /// <summary>The three shipping optimization presets.</summary>
    public enum OptimizationPreset
    {
        /// <summary>Preserve appearance first; only changes that are effectively invisible.</summary>
        QualityFirst = 0,

        /// <summary>Reduce VRAM while keeping differences hard to see.</summary>
        Balanced = 1,

        /// <summary>Reduce VRAM aggressively, with explicit quality warnings.</summary>
        SizeFirst = 2
    }

    /// <summary>How confident the planner is that the proposed change is visually acceptable.</summary>
    public enum QualityConfidence
    {
        /// <summary>No evidence at all. The change may or may not be acceptable.</summary>
        Unknown = 0,

        /// <summary>Heuristics only. Manual inspection is strongly recommended.</summary>
        Low,

        /// <summary>An estimated quality figure was computed from source pixels.</summary>
        Medium,

        /// <summary>A measured comparison against the real compressed output was performed.</summary>
        High
    }

    /// <summary>
    /// Whether a reported saving was derived by arithmetic from the proposed plan or read
    /// back from an actually generated asset. The UI must never blur the two.
    /// </summary>
    public enum MeasurementBasis
    {
        /// <summary>Computed from (dimensions, format, mip chain). Deterministic, not measured.</summary>
        ComputedFromPlan = 0,

        /// <summary>Measured from a generated asset that was really imported and read back.</summary>
        MeasuredFromGeneratedAsset
    }

    /// <summary>
    /// A rule describing one proposed change and why it was proposed. Keys are stable,
    /// machine-friendly identifiers resolved to localized text by the editor layer; the
    /// argument list carries the values interpolated into the translated sentence.
    /// </summary>
    public sealed class PlanNote
    {
        public enum Severity
        {
            Info = 0,
            Warning = 1,
            Error = 2
        }

        public string Key;
        public readonly List<string> Args = new List<string>();
        public Severity Level = Severity.Info;

        public PlanNote()
        {
        }

        public PlanNote(string key, params string[] args)
        {
            Key = key;
            if (args != null)
            {
                Args.AddRange(args);
            }
        }

        public PlanNote WithSeverity(Severity severity)
        {
            Level = severity;
            return this;
        }

        public override string ToString()
        {
            return Level + " " + Key + "(" + string.Join(", ", Args.ToArray()) + ")";
        }
    }

    /// <summary>
    /// Per-texture user override. Every field is optional; a null/absent override means
    /// "follow the preset".
    /// </summary>
    public sealed class TextureOverride
    {
        /// <summary>Never optimize this texture at all.</summary>
        public bool Excluded;

        /// <summary>Keep the current resolution even if the preset would downscale.</summary>
        public bool KeepResolution;

        /// <summary>Keep the current compression format even if the preset would change it.</summary>
        public bool KeepFormat;

        /// <summary>Force a specific output format.</summary>
        public bool ForceFormat;
        public TunerTextureFormat ForcedFormat = TunerTextureFormat.Automatic;

        /// <summary>Force a specific Max Size value.</summary>
        public bool ForceMaxSize;
        public int ForcedMaxSize = 2048;

        /// <summary>Force the mip-map setting on or off.</summary>
        public bool ForceMipMaps;
        public bool ForcedMipMaps = true;

        /// <summary>Free-form user note shown alongside the override.</summary>
        public string Note;

        public bool HasAnyEffect
        {
            get
            {
                return Excluded || KeepResolution || KeepFormat || ForceFormat || ForceMaxSize || ForceMipMaps;
            }
        }

        /// <summary>Serializes to a flat dictionary so it can be hashed and stored as JSON.</summary>
        public Dictionary<string, object> ToDictionary()
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            dict["excluded"] = Excluded;
            dict["keepResolution"] = KeepResolution;
            dict["keepFormat"] = KeepFormat;
            dict["forceFormat"] = ForceFormat;
            dict["forcedFormat"] = ForcedFormat.ToString();
            dict["forceMaxSize"] = ForceMaxSize;
            dict["forcedMaxSize"] = ForcedMaxSize;
            dict["forceMipMaps"] = ForceMipMaps;
            dict["forcedMipMaps"] = ForcedMipMaps;
            if (!string.IsNullOrEmpty(Note))
            {
                dict["note"] = Note;
            }

            return dict;
        }

        /// <summary>Rebuilds an override from its dictionary form; missing fields keep defaults.</summary>
        public static TextureOverride FromDictionary(Dictionary<string, object> dict)
        {
            var result = new TextureOverride();
            if (dict == null)
            {
                return result;
            }

            result.Excluded = MiniJson.GetBool(dict, "excluded", false);
            result.KeepResolution = MiniJson.GetBool(dict, "keepResolution", false);
            result.KeepFormat = MiniJson.GetBool(dict, "keepFormat", false);
            result.ForceFormat = MiniJson.GetBool(dict, "forceFormat", false);
            result.ForceMaxSize = MiniJson.GetBool(dict, "forceMaxSize", false);
            result.ForceMipMaps = MiniJson.GetBool(dict, "forceMipMaps", false);
            result.ForcedMipMaps = MiniJson.GetBool(dict, "forcedMipMaps", true);

            var forcedSize = MiniJson.GetLong(dict, "forcedMaxSize", 2048);
            if (forcedSize > 0 && forcedSize <= int.MaxValue)
            {
                result.ForcedMaxSize = (int)forcedSize;
            }

            var forcedFormatText = MiniJson.GetString(dict, "forcedFormat", "Automatic");
            TunerTextureFormat parsed;
            result.ForcedFormat = TextureFormatInfo.TryParse(forcedFormatText, out parsed) ? parsed : TunerTextureFormat.Automatic;

            result.Note = MiniJson.GetString(dict, "note", null);
            return result;
        }

        /// <summary>Hash token so that changing an override invalidates the right outputs.</summary>
        public string ToHashToken()
        {
            return string.Join("|", new[]
            {
                Excluded ? "E1" : "E0",
                KeepResolution ? "R1" : "R0",
                KeepFormat ? "K1" : "K0",
                ForceFormat ? "F1" : "F0",
                ForcedFormat.ToString(),
                ForceMaxSize ? "M1" : "M0",
                ForcedMaxSize.ToString(),
                ForceMipMaps ? "P1" : "P0",
                ForcedMipMaps ? "T1" : "T0"
            });
        }
    }

    /// <summary>
    /// The tunable rules a preset supplies to the planner. Presets differ only in these
    /// values; the planner itself has no preset-specific branching, which keeps the decision
    /// path auditable and unit testable.
    /// </summary>
    public sealed class TexturePolicy
    {
        public OptimizationPreset Preset;

        /// <summary>Human-readable identifier used in manifests and logs.</summary>
        public string Id;

        /// <summary>Whether the compression format may be changed at all.</summary>
        public bool AllowFormatChange;

        /// <summary>Whether resolution may be reduced.</summary>
        public bool AllowDownscale;

        /// <summary>Maximum number of ladder rungs the resolution may drop.</summary>
        public int MaxDownscaleSteps;

        /// <summary>Hard floor for the Max Size value, regardless of how many steps remain.</summary>
        public int MinAllowedMaxSize;

        /// <summary>
        /// Whether formats with visibly weaker quality (BC1 for alpha-free colour maps) may
        /// be chosen. Always false for Quality First.
        /// </summary>
        public bool AllowAggressiveFormats;

        /// <summary>
        /// Whether textures identified as faces, eyes, small text or UI may be downscaled at
        /// all. Default is false in every shipping preset: those textures carry recognisable
        /// detail that resolution reduction destroys.
        /// </summary>
        public bool AllowDownscaleOfDetailTextures;

        /// <summary>
        /// Minimum acceptable estimated quality, in dB of PSNR against the source, for a
        /// *format change at unchanged resolution*. A plan is rejected if its estimate falls
        /// below this. Ignored when no pixel-derived estimate exists, in which case the
        /// planner marks the result Low confidence instead of inventing a number.
        /// </summary>
        public double MinEstimatedQualityDb;

        /// <summary>
        /// Highest detail score (0..1, derived from high-frequency energy in the source
        /// pixels) at which resolution reduction is permitted. Images scoring above this are
        /// kept at full resolution because downscaling them removes content the user will
        /// notice. Ignored when no pixel-derived detail score exists.
        /// </summary>
        public double MaxDetailScoreForDownscale;

        /// <summary>
        /// Whether the planner may propose a change when no quality evidence is available at
        /// all. When false, such textures are skipped with an explanation.
        /// </summary>
        public bool AllowChangesWithoutQualityEvidence;

        /// <summary>Whether the generated output should keep its mip chain.</summary>
        public bool KeepMipMaps;

        public TexturePolicy Clone()
        {
            return (TexturePolicy)MemberwiseClone();
        }
    }

    /// <summary>Registry of the shipping presets.</summary>
    public static class PresetCatalog
    {
        public static readonly OptimizationPreset[] All =
        {
            OptimizationPreset.QualityFirst,
            OptimizationPreset.Balanced,
            OptimizationPreset.SizeFirst
        };

        /// <summary>Stable identifier for a preset, used in manifests and config hashes.</summary>
        public static string GetId(OptimizationPreset preset)
        {
            switch (preset)
            {
                case OptimizationPreset.QualityFirst:
                    return "quality-first";
                case OptimizationPreset.Balanced:
                    return "balanced";
                case OptimizationPreset.SizeFirst:
                    return "size-first";
                default:
                    return "unknown";
            }
        }

        /// <summary>Localization key for a preset's display name.</summary>
        public static string GetDisplayNameKey(OptimizationPreset preset)
        {
            return "preset." + GetId(preset) + ".name";
        }

        /// <summary>Localization key for a preset's description.</summary>
        public static string GetDescriptionKey(OptimizationPreset preset)
        {
            return "preset." + GetId(preset) + ".description";
        }

        /// <summary>Returns a fresh policy instance so callers can tune copies safely.</summary>
        public static TexturePolicy GetPolicy(OptimizationPreset preset)
        {
            switch (preset)
            {
                case OptimizationPreset.QualityFirst:
                    return new TexturePolicy
                    {
                        Preset = OptimizationPreset.QualityFirst,
                        Id = GetId(OptimizationPreset.QualityFirst),
                        AllowFormatChange = true,
                        AllowDownscale = false,
                        MaxDownscaleSteps = 0,
                        MinAllowedMaxSize = 16384,
                        AllowAggressiveFormats = false,
                        AllowDownscaleOfDetailTextures = false,
                        MinEstimatedQualityDb = 50.0,
                        MaxDetailScoreForDownscale = 0.0,
                        AllowChangesWithoutQualityEvidence = true,
                        KeepMipMaps = true
                    };

                case OptimizationPreset.SizeFirst:
                    return new TexturePolicy
                    {
                        Preset = OptimizationPreset.SizeFirst,
                        Id = GetId(OptimizationPreset.SizeFirst),
                        AllowFormatChange = true,
                        AllowDownscale = true,
                        MaxDownscaleSteps = 2,
                        MinAllowedMaxSize = 512,
                        AllowAggressiveFormats = true,
                        AllowDownscaleOfDetailTextures = false,
                        MinEstimatedQualityDb = 38.0,
                        MaxDetailScoreForDownscale = 0.65,
                        AllowChangesWithoutQualityEvidence = true,
                        KeepMipMaps = true
                    };

                default:
                    return new TexturePolicy
                    {
                        Preset = OptimizationPreset.Balanced,
                        Id = GetId(OptimizationPreset.Balanced),
                        AllowFormatChange = true,
                        AllowDownscale = true,
                        MaxDownscaleSteps = 1,
                        MinAllowedMaxSize = 1024,
                        AllowAggressiveFormats = true,
                        AllowDownscaleOfDetailTextures = false,
                        MinEstimatedQualityDb = 45.0,
                        MaxDetailScoreForDownscale = 0.35,
                        AllowChangesWithoutQualityEvidence = true,
                        KeepMipMaps = true
                    };
            }
        }

        /// <summary>Builds a hash token for a policy so config changes invalidate outputs.</summary>
        public static string ToHashToken(TexturePolicy policy)
        {
            if (policy == null)
            {
                return "policy:null";
            }

            return StableHash.CombineOrdered(new[]
            {
                policy.Id,
                policy.AllowFormatChange ? "1" : "0",
                policy.AllowDownscale ? "1" : "0",
                policy.MaxDownscaleSteps.ToString(),
                policy.MinAllowedMaxSize.ToString(),
                policy.AllowAggressiveFormats ? "1" : "0",
                policy.AllowDownscaleOfDetailTextures ? "1" : "0",
                policy.MinEstimatedQualityDb.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                policy.MaxDetailScoreForDownscale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                policy.AllowChangesWithoutQualityEvidence ? "1" : "0",
                policy.KeepMipMaps ? "1" : "0"
            });
        }
    }
}
