using System;

namespace AvatarTuner.Core
{
    /// <summary>Texture reporting arithmetic used by the VRChat SDK performance scanner.</summary>
    public static class VrcTextureMemory
    {
        public const int AccountingVersion = 1;

        public static bool TryEstimateBytes(int width, int height, TunerTextureFormat format,
            int mipCount, int faceCount, out long bytes)
        {
            bytes = 0;
            if (width <= 0 || height <= 0 || (faceCount != 1 && faceCount != 6) ||
                (long)width * height > int.MaxValue || mipCount <= 0 ||
                mipCount > VramCalculator.MipLevelCount(width, height)) return false;
            var bytesPerPixel = format == TunerTextureFormat.RGB24 ? 4f :
                format == TunerTextureFormat.ASTC_6x6 ? 0.445f : (float)TextureFormatInfo.BytesPerPixel(format);
            if (bytesPerPixel <= 0) return false;

            // The SDK shifts the base area and rounds each mip, without block padding or cube faces.
            var area = width * height;
            for (var level = 0; level < mipCount; level++)
            {
                var levelBytes = (float)(area >> (2 * level)) * bytesPerPixel;
                if (levelBytes >= int.MaxValue) { bytes = 0; return false; }
                bytes += (long)Math.Round(levelBytes, MidpointRounding.ToEven);
            }
            return true;
        }
    }
}
