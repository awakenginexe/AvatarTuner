using System;
using System.Collections.Generic;
using System.Globalization;

namespace AvatarTuner.Core
{
    /// <summary>
    /// Texture formats the planner is able to reason about.
    ///
    /// These are Unity's <c>TextureImporterFormat</c> names, but the enum lives in Core so
    /// that format arithmetic stays testable outside Unity. The editor layer maps this enum
    /// to <c>UnityEditor.TextureImporterFormat</c> one-to-one.
    /// </summary>
    public enum TunerTextureFormat
    {
        /// <summary>Importer chooses; treated as "unknown, do not touch".</summary>
        Automatic = 0,

        // ---- block compressed (desktop / PC target) ----
        /// <summary>BC1 / DXT1. 8 bytes per 4x4 block. RGB, optional 1-bit alpha.</summary>
        BC1,
        /// <summary>BC3 / DXT5. 16 bytes per 4x4 block. RGBA with interpolated alpha.</summary>
        BC3,
        /// <summary>BC4. 8 bytes per 4x4 block. Single channel.</summary>
        BC4,
        /// <summary>BC5. 16 bytes per 4x4 block. Two channels; the standard normal-map format.</summary>
        BC5,
        /// <summary>BC6H. 16 bytes per 4x4 block. HDR RGB.</summary>
        BC6H,
        /// <summary>BC7. 16 bytes per 4x4 block. High quality RGBA.</summary>
        BC7,

        // ---- uncompressed ----
        R8,
        RG16,
        R16,
        RGB24,
        RGB16,
        RGBA32,
        ARGB32,
        RGBA16,
        RHalf,
        RGHalf,
        RGBAHalf,
        RFloat,
        RGFloat,
        RGBAFloat,

        // ---- mobile-only, reported for completeness but never chosen for PC output ----
        ETC2_RGB4,
        ETC2_RGBA8,

        Unknown,
        ASTC_4x4,
        ASTC_6x6,
        ASTC_8x8
    }

    /// <summary>
    /// Static facts about each texture format: block size, alpha capability and whether the
    /// format is a sane target for a Windows PC VRChat avatar build.
    /// </summary>
    public static class TextureFormatInfo
    {
        /// <summary>
        /// Bytes consumed by one texel block for block-compressed formats, or 0 when the
        /// format is not block compressed.
        /// </summary>
        public static int BlockBytes(TunerTextureFormat format)
        {
            switch (format)
            {
                case TunerTextureFormat.BC1:
                case TunerTextureFormat.BC4:
                case TunerTextureFormat.ETC2_RGB4:
                    return 8;
                case TunerTextureFormat.BC3:
                case TunerTextureFormat.BC5:
                case TunerTextureFormat.BC6H:
                case TunerTextureFormat.BC7:
                case TunerTextureFormat.ASTC_4x4:
                case TunerTextureFormat.ASTC_6x6:
                case TunerTextureFormat.ASTC_8x8:
                case TunerTextureFormat.ETC2_RGBA8:
                    return 16;
                default:
                    return 0;
            }
        }

        /// <summary>True when the format stores data in compressed blocks.</summary>
        public static bool IsBlockCompressed(TunerTextureFormat format)
        {
            return BlockBytes(format) > 0;
        }

        /// <summary>
        /// Bytes per texel for non-block formats. For block formats this returns the
        /// average bytes-per-texel which is what callers usually want when
        /// they need a quick figure; use <see cref="VramCalculator" /> for exact totals.
        /// </summary>
        public static double BytesPerPixel(TunerTextureFormat format)
        {
            var blockBytes = BlockBytes(format);
            if (blockBytes > 0)
            {
                var blockWidth = format == TunerTextureFormat.ASTC_6x6 ? 6 : format == TunerTextureFormat.ASTC_8x8 ? 8 : 4;
                return blockBytes / (double)(blockWidth * blockWidth);
            }

            switch (format)
            {
                case TunerTextureFormat.R8:
                    return 1.0;
                case TunerTextureFormat.RG16:
                case TunerTextureFormat.R16:
                case TunerTextureFormat.RGB16:
                case TunerTextureFormat.RGBA16:
                case TunerTextureFormat.RHalf:
                    return 2.0;
                case TunerTextureFormat.RGB24:
                    return 3.0;
                case TunerTextureFormat.RGBA32:
                case TunerTextureFormat.ARGB32:
                case TunerTextureFormat.RGHalf:
                case TunerTextureFormat.RFloat:
                    return 4.0;
                case TunerTextureFormat.RGBAHalf:
                case TunerTextureFormat.RGFloat:
                    return 8.0;
                case TunerTextureFormat.RGBAFloat:
                    return 16.0;
                default:
                    return 0.0;
            }
        }

        /// <summary>True when the format is representable without loss on a PC build target.</summary>
        public static bool IsSupportedOnPc(TunerTextureFormat format)
        {
            switch (format)
            {
                case TunerTextureFormat.ETC2_RGB4:
                case TunerTextureFormat.ETC2_RGBA8:
                case TunerTextureFormat.ASTC_4x4:
                case TunerTextureFormat.ASTC_6x6:
                case TunerTextureFormat.ASTC_8x8:
                case TunerTextureFormat.Automatic:
                case TunerTextureFormat.Unknown:
                    return false;
                default:
                    return BytesPerPixel(format) > 0;
            }
        }

        /// <summary>True when the format can store an alpha channel.</summary>
        public static bool SupportsAlpha(TunerTextureFormat format)
        {
            switch (format)
            {
                case TunerTextureFormat.ASTC_4x4:
                case TunerTextureFormat.ASTC_6x6:
                case TunerTextureFormat.ASTC_8x8:
                case TunerTextureFormat.BC3:
                case TunerTextureFormat.BC7:
                case TunerTextureFormat.BC1: // 1-bit alpha only
                case TunerTextureFormat.ETC2_RGBA8:
                case TunerTextureFormat.RGBA32:
                case TunerTextureFormat.ARGB32:
                case TunerTextureFormat.RGBA16:
                case TunerTextureFormat.RGBAHalf:
                case TunerTextureFormat.RGBAFloat:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Number of color/data channels represented by the format.</summary>
        public static int ChannelCount(TunerTextureFormat format)
        {
            switch (format)
            {
                case TunerTextureFormat.BC4:
                case TunerTextureFormat.R8:
                case TunerTextureFormat.R16:
                case TunerTextureFormat.RHalf:
                case TunerTextureFormat.RFloat:
                    return 1;

                case TunerTextureFormat.BC5:
                case TunerTextureFormat.RG16:
                case TunerTextureFormat.RGHalf:
                case TunerTextureFormat.RGFloat:
                    return 2;

                case TunerTextureFormat.BC1:
                case TunerTextureFormat.BC6H:
                case TunerTextureFormat.RGB24:
                case TunerTextureFormat.RGB16:
                case TunerTextureFormat.ETC2_RGB4:
                    return 3;

                case TunerTextureFormat.BC3:
                case TunerTextureFormat.BC7:
                case TunerTextureFormat.ASTC_4x4:
                case TunerTextureFormat.ASTC_6x6:
                case TunerTextureFormat.ASTC_8x8:
                case TunerTextureFormat.RGBA32:
                case TunerTextureFormat.ARGB32:
                case TunerTextureFormat.RGBA16:
                case TunerTextureFormat.RGBAHalf:
                case TunerTextureFormat.RGBAFloat:
                case TunerTextureFormat.ETC2_RGBA8:
                    return 4;

                default:
                    return 0;
            }
        }

        /// <summary>True when the format stores three or more channels (RGB / RGBA color data).</summary>
        public static bool HasColorChannels(TunerTextureFormat format)
        {
            return ChannelCount(format) >= 3;
        }

        /// <summary>
        /// True when the format stores smooth, gradient-rich alpha. BC1's single-bit alpha is
        /// excluded because it produces hard cut-outs, and BC7/BC3 are the only block formats
        /// with interpolated alpha.
        /// </summary>
        public static bool SupportsSmoothAlpha(TunerTextureFormat format)
        {
            switch (format)
            {
                case TunerTextureFormat.BC3:
                case TunerTextureFormat.BC7:
                case TunerTextureFormat.ASTC_4x4:
                case TunerTextureFormat.ASTC_6x6:
                case TunerTextureFormat.ASTC_8x8:
                case TunerTextureFormat.ETC2_RGBA8:
                case TunerTextureFormat.RGBA32:
                case TunerTextureFormat.ARGB32:
                case TunerTextureFormat.RGBA16:
                case TunerTextureFormat.RGBAHalf:
                case TunerTextureFormat.RGBAFloat:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True when the format is lossy relative to 8-bit RGBA.</summary>
        public static bool IsLossy(TunerTextureFormat format)
        {
            return IsBlockCompressed(format);
        }

        /// <summary>
        /// A short, human-readable name for display in the UI. Deliberately not localized -
        /// format names are technical identifiers shared across languages.
        /// </summary>
        public static string DisplayName(TunerTextureFormat format)
        {
            switch (format)
            {
                case TunerTextureFormat.Automatic:
                    return "Automatic";
                case TunerTextureFormat.BC1:
                    return "BC1 (DXT1)";
                case TunerTextureFormat.BC3:
                    return "BC3 (DXT5)";
                case TunerTextureFormat.BC4:
                    return "BC4";
                case TunerTextureFormat.BC5:
                    return "BC5";
                case TunerTextureFormat.BC6H:
                    return "BC6H (HDR)";
                case TunerTextureFormat.BC7:
                    return "BC7";
                case TunerTextureFormat.R8:
                    return "R8";
                case TunerTextureFormat.RG16:
                    return "RG16";
                case TunerTextureFormat.R16:
                    return "R16";
                case TunerTextureFormat.RGB24:
                    return "RGB24";
                case TunerTextureFormat.RGB16:
                    return "RGB16";
                case TunerTextureFormat.RGBA32:
                    return "RGBA32";
                case TunerTextureFormat.ARGB32:
                    return "ARGB32";
                case TunerTextureFormat.RGBA16:
                    return "RGBA16";
                case TunerTextureFormat.RHalf:
                    return "R Half";
                case TunerTextureFormat.RGHalf:
                    return "RG Half";
                case TunerTextureFormat.RGBAHalf:
                    return "RGBA Half";
                case TunerTextureFormat.RFloat:
                    return "R Float";
                case TunerTextureFormat.RGFloat:
                    return "RG Float";
                case TunerTextureFormat.RGBAFloat:
                    return "RGBA Float";
                case TunerTextureFormat.ETC2_RGB4:
                    return "ETC2 RGB4 (mobile)";
                case TunerTextureFormat.ETC2_RGBA8:
                    return "ETC2 RGBA8 (mobile)";
                default:
                    return "Unknown";
            }
        }

        /// <summary>
        /// Parses a format name, accepting the enum name, the display name, and Unity's own
        /// spellings from both worlds: <c>TextureFormat</c> names (what Texture2D.format
        /// reports) and <c>GraphicsFormat</c> names (what Texture.graphicsFormat reports
        /// for every other Texture subclass). Used to read per-texture overrides out of
        /// persisted configuration and to understand scanner observations.
        /// </summary>
        public static bool TryParse(string text, out TunerTextureFormat format)
        {
            format = TunerTextureFormat.Unknown;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            var trimmed = text.Trim();
            foreach (TunerTextureFormat candidate in Enum.GetValues(typeof(TunerTextureFormat)))
            {
                if (string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    format = candidate;
                    return true;
                }

                var display = DisplayName(candidate);
                if (string.Equals(display, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    format = candidate;
                    return true;
                }
            }

            // Only unambiguous block-format aliases are accepted here: the parsed format
            // feeds VRAM arithmetic, so an alias whose block size could differ is refused
            // (Unknown) instead of guessed at.
            switch (trimmed.ToUpperInvariant())
            {
                case "DXT1":
                case "DXT1CRUNCHED":
                case "BC1_UNORM":
                case "BC1_UNORM_SRGB":
                    format = TunerTextureFormat.BC1;
                    return true;
                case "DXT5":
                case "DXT5CRUNCHED":
                case "DXT5_CRUNCH":
                case "BC3_UNORM":
                case "BC3_UNORM_SRGB":
                    format = TunerTextureFormat.BC3;
                    return true;
                case "BC4_UNORM":
                    format = TunerTextureFormat.BC4;
                    return true;
                case "BC5_UNORM":
                    format = TunerTextureFormat.BC5;
                    return true;
                case "BC6H_UF16":
                case "BC6H_SF16":
                    format = TunerTextureFormat.BC6H;
                    return true;
                case "BC7_SRGB":
                case "BC7_UNORM":
                case "BC7_UNORM_SRGB":
                    format = TunerTextureFormat.BC7;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The block-compressed candidates worth considering for a given alpha requirement,
        /// best first. Used by the planner to iterate options in a fixed, documented order.
        /// </summary>
        public static List<TunerTextureFormat> BlockCandidates(bool needsSmoothAlpha)
        {
            var result = new List<TunerTextureFormat>();
            if (needsSmoothAlpha)
            {
                result.Add(TunerTextureFormat.BC7);
                result.Add(TunerTextureFormat.BC3);
            }
            else
            {
                result.Add(TunerTextureFormat.BC7);
                result.Add(TunerTextureFormat.BC1);
            }

            return result;
        }

        /// <summary>
        /// Serializes the format into a stable token for hash inputs.
        /// </summary>
        public static string ToToken(TunerTextureFormat format)
        {
            return format.ToString().ToUpperInvariant();
        }

        /// <summary>
        /// Reports whether the format requires dimensions to be multiples of 4 to avoid
        /// padding waste. Block-compressed formats always pad up to the next multiple of 4.
        /// </summary>
        public static bool RequiresMultipleOfFour(TunerTextureFormat format)
        {
            return IsBlockCompressed(format);
        }

        /// <summary>
        /// Formats a byte count for UI display using binary units. Kept in Core so that unit
        /// tests can assert the exact strings the UI will show.
        /// </summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes < 0)
            {
                throw new ArgumentOutOfRangeException("bytes", "byte count must not be negative");
            }

            string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
            double value = bytes;
            var unit = 0;
            while (value >= 1024.0 && unit < units.Length - 1)
            {
                value /= 1024.0;
                unit++;
            }

            return unit == 0
                ? bytes.ToString(CultureInfo.InvariantCulture) + " B"
                : value.ToString("0.##", CultureInfo.InvariantCulture) + " " + units[unit];
        }
    }
}
