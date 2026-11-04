using AvatarTuner.Editor.Analysis;
using AvatarTuner.Core;

namespace AvatarTuner.StandaloneTests
{
    public static class VrcPerformanceReaderTests
    {
        [Test("optional SDK reader preserves the SDK float MB value and forwards avatar/platform")]
        public static void SdkReadout()
        {
            var avatar = new object();
            VRC.SDKBase.Validation.Performance.AvatarPerformance.Value = 221.0729f;
            long bytes;
            Assert.IsTrue(VrcPerformanceReader.TryRead(avatar, "avatar", true, out bytes), "supported API");
            Assert.AreEqual("221.0729 MB", TextureFormatInfo.FormatTextureMemory(bytes), "SDK value round trips without unit drift");
            Assert.AreEqual(avatar, VRC.SDKBase.Validation.Performance.AvatarPerformance.Root, "correct avatar");
            Assert.IsTrue(VRC.SDKBase.Validation.Performance.AvatarPerformance.Mobile, "correct platform");
            Assert.AreEqual("avatar", VRC.SDKBase.Validation.Performance.AvatarPerformance.Name, "correct avatar name");
        }

        [Test("SDK failure or invalid values do not become a zero-memory success")]
        public static void SdkFailures()
        {
            long bytes;
            foreach (var value in new float?[] { null, float.NaN, float.PositiveInfinity, -1 })
            {
                VRC.SDKBase.Validation.Performance.AvatarPerformance.Value = value;
                Assert.IsFalse(VrcPerformanceReader.TryRead(new object(), "avatar", false, out bytes), "unavailable or invalid SDK metric");
                Assert.AreEqual(0L, bytes, "no partial value leaks");
            }
            VRC.SDKBase.Validation.Performance.AvatarPerformance.Throw = true;
            try { Assert.IsFalse(VrcPerformanceReader.TryRead(new object(), "avatar", false, out bytes), "optional SDK exception is isolated"); }
            finally { VRC.SDKBase.Validation.Performance.AvatarPerformance.Throw = false; }
            Assert.IsFalse(VrcPerformanceReader.TryRead(null, "avatar", false, out bytes), "missing avatar");
            VRC.SDKBase.Validation.Performance.AvatarPerformance.Value = 0;
            Assert.IsTrue(VrcPerformanceReader.TryRead(new object(), "empty", false, out bytes), "a real zero is valid");
        }
    }
}

// SDK stand-in for the reflection contract; no Unity or VRChat code is executed by these tests.
namespace VRC.SDKBase.Validation.Performance.Stats
{
    public sealed class AvatarPerformanceStats
    {
        public AvatarPerformanceStats(bool mobile) { }
        public float? textureMegabytes;
    }
}
namespace VRC.SDKBase.Validation.Performance
{
    public static class AvatarPerformance
    {
        public static float? Value;
        public static object Root;
        public static bool Mobile, Throw;
        public static string Name;
        public static void CalculatePerformanceStats(string name, object root, Stats.AvatarPerformanceStats stats, bool mobile)
        {
            if (Throw) throw new System.InvalidOperationException("test SDK failure");
            Root = root; Name = name; Mobile = mobile;
            stats.textureMegabytes = Value;
        }
    }
}
