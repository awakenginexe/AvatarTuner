using System;

namespace AvatarTuner.Core
{
    /// <summary>Estimates GPU bytes from the imported mip levels and known texture shape.</summary>
    public static class ImportedTextureMemory
    {
        public static bool TryEstimateBytes(int width, int height, TunerTextureFormat format,
            int mipCount, int faceCount, out long bytes)
        {
            bytes = 0;
            if (width <= 0 || height <= 0 || (faceCount != 1 && faceCount != 6) ||
                format == TunerTextureFormat.Automatic || format == TunerTextureFormat.Unknown ||
                TextureFormatInfo.BytesPerPixel(format) <= 0 || mipCount <= 0 ||
                mipCount > VramCalculator.MipLevelCount(width, height)) return false;
            try
            {
                long total = 0;
                for (int level = 0; level < mipCount; level++)
                {
                    int w, h;
                    VramCalculator.MipDimensions(width, height, level, out w, out h);
                    if (TextureFormatInfo.BlockBytes(format) == 0 &&
                        w * (double)h * TextureFormatInfo.BytesPerPixel(format) >= long.MaxValue) return false;
                    total = checked(total + VramCalculator.LevelBytes(w, h, format));
                }
                bytes = checked(total * faceCount);
                return true;
            }
            catch (OverflowException) { return false; }
        }
    }
}
