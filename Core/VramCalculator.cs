using System;

namespace AvatarTuner.Core
{
    /// <summary>
    /// Exact GPU-resident memory arithmetic for textures.
    ///
    /// These are deterministic calculations from (dimensions, format, mip chain), not
    /// measurements. The UI must label results produced here as "computed", and must not
    /// present them as measured values. Nothing in this class talks to a GPU.
    /// </summary>
    public static class VramCalculator
    {
        /// <summary>
        /// Bytes occupied by a single mip level of a 2D image.
        ///
        /// Block-compressed formats pad the image up to a whole number of blocks, so a
        /// 2x2 BC7 image still costs one 16-byte block. Uncompressed formats are exact.
        /// </summary>
        public static long LevelBytes(int width, int height, TunerTextureFormat format)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException("width", "width must be positive");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException("height", "height must be positive");
            }

            var blockBytes = TextureFormatInfo.BlockBytes(format);
            if (blockBytes > 0)
            {
                int blockWidth = format == TunerTextureFormat.ASTC_6x6 ? 6 : format == TunerTextureFormat.ASTC_8x8 ? 8 : 4;
                long blocksWide = ((long)width + blockWidth - 1) / blockWidth;
                long blocksHigh = ((long)height + blockWidth - 1) / blockWidth;
                return blocksWide * blocksHigh * blockBytes;
            }

            var bytesPerPixel = TextureFormatInfo.BytesPerPixel(format);
            if (bytesPerPixel <= 0.0)
            {
                throw new ArgumentOutOfRangeException("format", "format '" + format + "' has no known size");
            }

            return (long)Math.Round(width * (double)height * bytesPerPixel);
        }

        /// <summary>
        /// Number of mip levels in a full chain, down to (and including) 1x1.
        /// A 1x1 texture has exactly one level.
        /// </summary>
        public static int MipLevelCount(int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException("width", "width must be positive");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException("height", "height must be positive");
            }

            var largest = Math.Max(width, height);
            var levels = 1;
            while (largest > 1)
            {
                largest >>= 1;
                levels++;
            }

            return levels;
        }

        /// <summary>
        /// Dimensions of a given mip level. Level 0 is the full size; each further level
        /// halves both dimensions, clamping at 1.
        /// </summary>
        public static void MipDimensions(int width, int height, int level, out int levelWidth, out int levelHeight)
        {
            if (level < 0)
            {
                throw new ArgumentOutOfRangeException("level", "level must not be negative");
            }

            levelWidth = Math.Max(1, width >> level);
            levelHeight = Math.Max(1, height >> level);
        }

        /// <summary>
        /// Total GPU memory for a texture, including the mip chain when requested.
        /// <paramref name="faceCount" /> is 6 for a cubemap and 1 for a normal 2D texture.
        /// </summary>
        public static long EstimateBytes(int width, int height, TunerTextureFormat format, bool mipMaps, int faceCount)
        {
            if (faceCount <= 0)
            {
                throw new ArgumentOutOfRangeException("faceCount", "faceCount must be positive");
            }

            var total = 0L;
            var levels = mipMaps ? MipLevelCount(width, height) : 1;
            for (var level = 0; level < levels; level++)
            {
                int levelWidth, levelHeight;
                MipDimensions(width, height, level, out levelWidth, out levelHeight);
                total += LevelBytes(levelWidth, levelHeight, format);
            }

            return total * faceCount;
        }

        /// <summary>Convenience overload for ordinary 2D textures.</summary>
        public static long EstimateBytes(int width, int height, TunerTextureFormat format, bool mipMaps)
        {
            return EstimateBytes(width, height, format, mipMaps, 1);
        }

        /// <summary>
        /// The ratio between the mip-chain total and the base level alone. For a full chain
        /// this converges to roughly 1.33; the exact figure is returned so that the UI can
        /// quote the real number for the real dimensions instead of the approximation.
        /// </summary>
        public static double MipOverheadFactor(int width, int height, TunerTextureFormat format)
        {
            var baseLevel = (double)LevelBytes(width, height, format);
            if (baseLevel <= 0.0)
            {
                return 1.0;
            }

            return EstimateBytes(width, height, format, true) / baseLevel;
        }

        /// <summary>
        /// Squares a dimension down to the nearest power of two, clamping at 1. The planner
        /// only ever downsizes to powers of two because that is what Unity's max-size
        /// control selects and it keeps mip chains clean.
        /// </summary>
        public static int FloorToPowerOfTwo(int value)
        {
            if (value <= 1)
            {
                return 1;
            }

            var result = 1;
            while (result <= value / 2)
            {
                result <<= 1;
            }

            return result;
        }

        /// <summary>
        /// Resolves Unity's "Max Size" semantics: the source is scaled down uniformly so that
        /// neither dimension exceeds <paramref name="maxSize" />, and never scaled up.
        /// Returns the dimensions the imported texture will actually have.
        /// </summary>
        public static void ApplyMaxSize(int sourceWidth, int sourceHeight, int maxSize, out int outWidth, out int outHeight)
        {
            if (maxSize <= 0)
            {
                throw new ArgumentOutOfRangeException("maxSize", "maxSize must be positive");
            }

            var largest = Math.Max(sourceWidth, sourceHeight);
            if (largest <= maxSize)
            {
                outWidth = sourceWidth;
                outHeight = sourceHeight;
                return;
            }

            // Unity scales both dimensions by the same factor and rounds to the nearest
            // integer, keeping the aspect ratio.
            var scale = (double)maxSize / largest;
            outWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            outHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        }

        /// <summary>
        /// Picks the smallest Unity Max Size value that still fits the source unchanged.
        /// Unity's importer exposes a fixed ladder of accepted values; the smallest rung
        /// that does not downscale is the one that keeps the texture at full resolution.
        /// </summary>
        public static int NearestLosslessMaxSize(int sourceWidth, int sourceHeight)
        {
            var largest = Math.Max(sourceWidth, sourceHeight);
            foreach (var rung in MaxSizeLadder)
            {
                if (rung >= largest)
                {
                    return rung;
                }
            }

            return MaxSizeLadder[MaxSizeLadder.Length - 1];
        }

        /// <summary>
        /// The Max Size values Unity's texture importer accepts, ascending.
        /// </summary>
        public static readonly int[] MaxSizeLadder =
        {
            32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384
        };

        /// <summary>
        /// Steps a Max Size value one rung down the ladder, or returns false when already at
        /// the bottom. Used by the planner when a preset permits downscaling.
        /// </summary>
        public static bool TryStepDownMaxSize(int currentMaxSize, out int nextMaxSize)
        {
            nextMaxSize = currentMaxSize;
            for (var i = MaxSizeLadder.Length - 1; i >= 0; i--)
            {
                if (MaxSizeLadder[i] < currentMaxSize)
                {
                    nextMaxSize = MaxSizeLadder[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Formats a percentage saving, guarding against a zero baseline.
        /// </summary>
        public static double SavingPercent(long beforeBytes, long afterBytes)
        {
            if (beforeBytes <= 0)
            {
                return 0.0;
            }

            return (beforeBytes - afterBytes) * 100.0 / beforeBytes;
        }
    }
}
