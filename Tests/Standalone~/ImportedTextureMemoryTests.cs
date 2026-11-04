using AvatarTuner.Core;

namespace AvatarTuner.StandaloneTests
{
    public static class ImportedTextureMemoryTests
    {
        [Test("imported mip levels and shape determine memory")]
        public static void ImportedMipLevels()
        {
            long bytes;
            Assert.IsTrue(ImportedTextureMemory.TryEstimateBytes(8, 4, TunerTextureFormat.BC7, 2, 1, out bytes), "known 2D image");
            Assert.AreEqual(48L, bytes, "8x4 and 4x2 BC7 mip blocks");
            Assert.IsTrue(ImportedTextureMemory.TryEstimateBytes(8, 4, TunerTextureFormat.BC7, 2, 6, out bytes), "known cube faces");
            Assert.AreEqual(288L, bytes, "six faces");
            Assert.IsFalse(ImportedTextureMemory.TryEstimateBytes(8, 4, TunerTextureFormat.BC7, 2, 0, out bytes), "unknown shape");
            Assert.IsFalse(ImportedTextureMemory.TryEstimateBytes(8, 4, TunerTextureFormat.Automatic, 2, 1, out bytes), "automatic is unknown");
            Assert.IsFalse(ImportedTextureMemory.TryEstimateBytes(8, 4, TunerTextureFormat.Unknown, 2, 1, out bytes), "unknown format");
            Assert.IsFalse(ImportedTextureMemory.TryEstimateBytes(8, 4, TunerTextureFormat.BC7, 5, 1, out bytes), "invalid mip count");
            Assert.IsFalse(ImportedTextureMemory.TryEstimateBytes(int.MaxValue, int.MaxValue, TunerTextureFormat.RGBAFloat, 1, 1, out bytes), "overflow is unknown");
        }
    }
}
