using System;
using System.Collections.Generic;
using AvatarTuner.Core;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>Tests for the deterministic hashing helpers used by change tracking.</summary>
    public static class StableHashTests
    {
        [Test("sha256 produces the published digest for a known input")]
        public static void Sha256MatchesKnownVector()
        {
            // Known SHA-256 of the ASCII string "abc".
            Assert.AreEqual(
                "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                StableHash.Sha256Hex("abc"),
                "sha256 of \"abc\"");
        }

        [Test("empty input hashes to the documented empty digest")]
        public static void EmptyInputHashesConsistently()
        {
            Assert.AreEqual(
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                StableHash.Sha256Hex(string.Empty),
                "sha256 of the empty string");
        }

        [Test("combine is order independent")]
        public static void CombineIsOrderIndependent()
        {
            var a = StableHash.Combine(new[] { "alpha", "beta", "gamma" });
            var b = StableHash.Combine(new[] { "gamma", "alpha", "beta" });

            Assert.AreEqual(a, b, "reordering tokens must not change the digest");
        }

        [Test("combine avoids concatenation ambiguity")]
        public static void CombineAvoidsConcatenationAmbiguity()
        {
            var a = StableHash.Combine(new[] { "ab", "c" });
            var b = StableHash.Combine(new[] { "a", "bc" });

            Assert.AreNotEqual(a, b, "[\"ab\",\"c\"] and [\"a\",\"bc\"] must not collide");
        }

        [Test("combineOrdered preserves order")]
        public static void CombineOrderedPreservesOrder()
        {
            var a = StableHash.CombineOrdered(new[] { "a", "b" });
            var b = StableHash.CombineOrdered(new[] { "b", "a" });

            Assert.AreNotEqual(a, b, "ordered combination must reflect sequence");
        }

        [Test("ofPairs ignores insertion order but not values")]
        public static void OfPairsIsOrderIndependent()
        {
            var first = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("maxSize", "2048"),
                new KeyValuePair<string, string>("format", "BC7"),
                new KeyValuePair<string, string>("mipMaps", "1")
            };

            var second = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("mipMaps", "1"),
                new KeyValuePair<string, string>("format", "BC7"),
                new KeyValuePair<string, string>("maxSize", "2048")
            };

            Assert.AreEqual(StableHash.OfPairs(first), StableHash.OfPairs(second), "insertion order must not matter");

            var changed = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("maxSize", "1024"),
                new KeyValuePair<string, string>("format", "BC7"),
                new KeyValuePair<string, string>("mipMaps", "1")
            };

            Assert.AreNotEqual(StableHash.OfPairs(first), StableHash.OfPairs(changed), "a changed value must change the digest");
        }

        [Test("ofPairs does not confuse a key with a value")]
        public static void OfPairsKeysAndValuesDoNotCollide()
        {
            var a = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("ab", "c")
            };
            var b = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("a", "bc")
            };

            Assert.AreNotEqual(StableHash.OfPairs(a), StableHash.OfPairs(b), "key/value boundaries must be unambiguous");
        }

        [Test("path tokens normalise separators and case")]
        public static void PathTokensNormalise()
        {
            Assert.AreEqual(
                StableHash.NormalizePathToken("Assets\\Tex\\A.png"),
                StableHash.NormalizePathToken("assets/tex/a.png"),
                "path tokens must be separator and case insensitive");

            Assert.AreEqual(string.Empty, StableHash.NormalizePathToken(null), "null path becomes empty token");
            Assert.AreEqual(string.Empty, StableHash.NormalizePathToken("   "), "whitespace path becomes empty token");
        }

        [Test("hex output is lowercase and two digits per byte")]
        public static void HexEncoding()
        {
            Assert.AreEqual("0001ff", StableHash.ToHex(new byte[] { 0x00, 0x01, 0xFF }), "hex encoding");
            Assert.AreEqual(64, StableHash.Sha256Hex("x").Length, "sha256 hex digest is 64 characters");
        }
    }

    /// <summary>Tests for the Unity version parser used by change tracking.</summary>
    public static class EngineVersionTests
    {
        [Test("parses a full Unity version string")]
        public static void ParsesFullVersion()
        {
            EngineVersion version;
            Assert.IsTrue(EngineVersion.TryParse("2022.3.22f1", out version), "2022.3.22f1 parses");
            Assert.AreEqual(2022, version.Major, "major");
            Assert.AreEqual(3, version.Minor, "minor");
            Assert.AreEqual(22, version.Patch, "patch");
            Assert.AreEqual("f1", version.Suffix, "suffix");
            Assert.IsTrue(version.IsFinal, "f1 is a final build");
        }

        [Test("parses a partial version string")]
        public static void ParsesPartialVersion()
        {
            EngineVersion version;
            Assert.IsTrue(EngineVersion.TryParse("2022.3", out version), "2022.3 parses");
            Assert.AreEqual(2022, version.Major, "major");
            Assert.AreEqual(3, version.Minor, "minor");
            Assert.AreEqual(0, version.Patch, "patch defaults to zero");
            Assert.AreEqual("2022.3.0", version.ToCanonicalString(), "canonical form is three part");
        }

        [Test("2022.3 and 2022.3.0 compare equal")]
        public static void CanonicalEquality()
        {
            EngineVersion a, b;
            Assert.IsTrue(EngineVersion.TryParse("2022.3", out a), "2022.3 parses");
            Assert.IsTrue(EngineVersion.TryParse("2022.3.0", out b), "2022.3.0 parses");
            Assert.IsTrue(a == b, "2022.3 must equal 2022.3.0");
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode(), "equal versions must hash equally");
        }

        [Test("ordering follows major then minor then patch")]
        public static void OrdersCorrectly()
        {
            EngineVersion a, b, c;
            EngineVersion.TryParse("2022.3.22f1", out a);
            EngineVersion.TryParse("2022.3.23f1", out b);
            EngineVersion.TryParse("2023.1.1f1", out c);

            Assert.IsTrue(a < b, "patch ordering");
            Assert.IsTrue(b < c, "major ordering");
            Assert.IsTrue(c > a, "reverse comparison");
            Assert.IsTrue(a.CompareTo(a) == 0, "self comparison");
        }

        [Test("rejects malformed input without throwing")]
        public static void RejectsMalformedInput()
        {
            EngineVersion version;

            Assert.IsFalse(EngineVersion.TryParse(null, out version), "null");
            Assert.IsFalse(EngineVersion.TryParse("", out version), "empty");
            Assert.IsFalse(EngineVersion.TryParse("abc", out version), "letters only");
            Assert.IsFalse(EngineVersion.TryParse("2022.3.22.4", out version), "too many parts");
            Assert.IsFalse(EngineVersion.TryParse("2022.x.1", out version), "non numeric part");
            Assert.IsFalse(EngineVersion.TryParse("-1.0.0", out version), "negative major");
            Assert.IsFalse(EngineVersion.TryParse(".3.1", out version), "leading dot");
        }

        [Test("suffix detection distinguishes alpha and beta builds")]
        public static void SuffixDetection()
        {
            EngineVersion version;
            Assert.IsTrue(EngineVersion.TryParse("2022.3.0a12", out version), "alpha parses");
            Assert.IsFalse(version.IsFinal, "alpha is not final");

            Assert.IsTrue(EngineVersion.TryParse("2022.3.0b3", out version), "beta parses");
            Assert.IsFalse(version.IsFinal, "beta is not final");

            Assert.IsTrue(EngineVersion.TryParse("2022.3.0f1", out version), "final parses");
            Assert.IsTrue(version.IsFinal, "f1 is final");

            Assert.IsTrue(EngineVersion.TryParse("2022.3.0", out version), "suffixless parses");
            Assert.IsTrue(version.IsFinal, "no suffix is treated as final");
        }

        [Test("ParseOrZero never throws")]
        public static void ParseOrZeroIsSafe()
        {
            Assert.AreEqual(0, EngineVersion.ParseOrZero("nonsense").Major, "garbage becomes zero");
            Assert.AreEqual(2022, EngineVersion.ParseOrZero("2022.3").Major, "valid input is parsed");
        }
    }

    /// <summary>Tests for the exact texture memory arithmetic.</summary>
    public static class VramCalculatorTests
    {
        [Test("uncompressed levels are exact")]
        public static void UncompressedLevelsAreExact()
        {
            Assert.AreEqual(4096L, VramCalculator.LevelBytes(32, 32, TunerTextureFormat.RGBA32), "32x32 RGBA32");
            Assert.AreEqual(3072L, VramCalculator.LevelBytes(32, 32, TunerTextureFormat.RGB24), "32x32 RGB24");
            Assert.AreEqual(1024L, VramCalculator.LevelBytes(32, 32, TunerTextureFormat.R8), "32x32 R8");
        }

        [Test("block compressed levels round up to whole blocks")]
        public static void BlockCompressedRoundsUp()
        {
            Assert.AreEqual(32768L, VramCalculator.LevelBytes(256, 256, TunerTextureFormat.BC1), "256x256 BC1 = 64*64*8");
            Assert.AreEqual(65536L, VramCalculator.LevelBytes(256, 256, TunerTextureFormat.BC7), "256x256 BC7 = 64*64*16");
            Assert.AreEqual(8L, VramCalculator.LevelBytes(2, 2, TunerTextureFormat.BC1), "a 2x2 BC1 image still costs one block");
            Assert.AreEqual(32L, VramCalculator.LevelBytes(5, 3, TunerTextureFormat.BC7), "5x3 BC7 is 2x1 blocks");
        }

        [Test("mip level count reaches 1x1")]
        public static void MipLevelCount()
        {
            Assert.AreEqual(1, VramCalculator.MipLevelCount(1, 1), "1x1 has one level");
            Assert.AreEqual(2, VramCalculator.MipLevelCount(2, 2), "2x2 has two levels");
            Assert.AreEqual(9, VramCalculator.MipLevelCount(256, 256), "256x256 has nine levels");
            Assert.AreEqual(11, VramCalculator.MipLevelCount(1024, 1024), "1024x1024 has eleven levels");
            Assert.AreEqual(9, VramCalculator.MipLevelCount(256, 64), "level count follows the longest side");
        }

        [Test("mip dimensions clamp at one")]
        public static void MipDimensionsClamp()
        {
            int w, h;
            VramCalculator.MipDimensions(256, 4, 0, out w, out h);
            Assert.AreEqual(256, w, "level 0 width");
            Assert.AreEqual(4, h, "level 0 height");

            VramCalculator.MipDimensions(256, 4, 4, out w, out h);
            Assert.AreEqual(16, w, "level 4 width");
            Assert.AreEqual(1, h, "level 4 height clamps at 1");

            VramCalculator.MipDimensions(256, 4, 12, out w, out h);
            Assert.AreEqual(1, w, "deep level width clamps at 1");
            Assert.AreEqual(1, h, "deep level height clamps at 1");
        }

        [Test("a full mip chain costs roughly a third more than the base level")]
        public static void MipChainOverhead()
        {
            var baseLevel = VramCalculator.LevelBytes(1024, 1024, TunerTextureFormat.RGBA32);
            var withMips = VramCalculator.EstimateBytes(1024, 1024, TunerTextureFormat.RGBA32, true);

            Assert.IsGreater(baseLevel, withMips, "mip chain must be larger than the base level alone");
            Assert.IsLess(baseLevel * 1.5, withMips, "mip chain overhead stays under 50%");

            var withoutMips = VramCalculator.EstimateBytes(1024, 1024, TunerTextureFormat.RGBA32, false);
            Assert.AreEqual(baseLevel, withoutMips, "without mips the total is just the base level");

            var factor = VramCalculator.MipOverheadFactor(1024, 1024, TunerTextureFormat.RGBA32);
            Assert.IsGreater(1.30, factor, "the theoretical limit is 4/3");
            Assert.IsLess(1.35, factor, "the theoretical limit is 4/3");
        }

        [Test("cubemap face count multiplies the total")]
        public static void CubemapFaces()
        {
            var single = VramCalculator.EstimateBytes(128, 128, TunerTextureFormat.BC7, true, 1);
            var cube = VramCalculator.EstimateBytes(128, 128, TunerTextureFormat.BC7, true, 6);
            Assert.AreEqual(single * 6, cube, "six faces cost six times as much");
        }

        [Test("rejects invalid arguments")]
        public static void RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(delegate { VramCalculator.LevelBytes(0, 4, TunerTextureFormat.RGBA32); }, "zero width");
            Assert.Throws<ArgumentOutOfRangeException>(delegate { VramCalculator.LevelBytes(4, -1, TunerTextureFormat.RGBA32); }, "negative height");
            Assert.Throws<ArgumentOutOfRangeException>(delegate { VramCalculator.MipLevelCount(0, 4); }, "zero width mip count");
            Assert.Throws<ArgumentOutOfRangeException>(delegate { VramCalculator.EstimateBytes(4, 4, TunerTextureFormat.RGBA32, true, 0); }, "zero faces");
            Assert.Throws<ArgumentOutOfRangeException>(delegate { VramCalculator.ApplyMaxSize(4, 4, 0, out _, out _); }, "zero max size");
        }

        [Test("floorToPowerOfTwo rounds down")]
        public static void FloorToPowerOfTwo()
        {
            Assert.AreEqual(1, VramCalculator.FloorToPowerOfTwo(0), "zero clamps to one");
            Assert.AreEqual(1, VramCalculator.FloorToPowerOfTwo(1), "one stays one");
            Assert.AreEqual(256, VramCalculator.FloorToPowerOfTwo(256), "exact power of two is unchanged");
            Assert.AreEqual(256, VramCalculator.FloorToPowerOfTwo(300), "300 rounds down to 256");
            Assert.AreEqual(256, VramCalculator.FloorToPowerOfTwo(511), "511 rounds down to 256, never up to 512");
            Assert.AreEqual(512, VramCalculator.FloorToPowerOfTwo(512), "exact boundaries are preserved");
        }

        [Test("ApplyMaxSize never upscales and keeps the aspect ratio")]
        public static void ApplyMaxSizeSemantics()
        {
            int w, h;

            VramCalculator.ApplyMaxSize(1024, 512, 2048, out w, out h);
            Assert.AreEqual(1024, w, "no upscaling when the source already fits");
            Assert.AreEqual(512, h, "no upscaling height");

            VramCalculator.ApplyMaxSize(2048, 1024, 1024, out w, out h);
            Assert.AreEqual(1024, w, "largest dimension is clamped to max size");
            Assert.AreEqual(512, h, "the other dimension scales proportionally");

            VramCalculator.ApplyMaxSize(300, 300, 256, out w, out h);
            Assert.AreEqual(256, w, "square image clamps");
            Assert.AreEqual(256, h, "square image clamps both axes");

            VramCalculator.ApplyMaxSize(3, 2048, 1024, out w, out h);
            Assert.IsTrue(w >= 1 && w <= 2, "very thin images round to at least one texel, got " + w);
            Assert.AreEqual(1024, h, "the long side is clamped");
        }

        [Test("nearestLosslessMaxSize picks the smallest fitting rung")]
        public static void NearestLosslessMaxSizePicksSmallestRung()
        {
            Assert.AreEqual(2048, VramCalculator.NearestLosslessMaxSize(2000, 1200), "2000 needs the 2048 rung");
            Assert.AreEqual(2048, VramCalculator.NearestLosslessMaxSize(2048, 2048), "exactly 2048 needs 2048");
            Assert.AreEqual(4096, VramCalculator.NearestLosslessMaxSize(3000, 3000), "3000 needs the 4096 rung");
            Assert.AreEqual(32, VramCalculator.NearestLosslessMaxSize(8, 8), "tiny images clamp to the smallest rung");
        }

        [Test("tryStepDownMaxSize walks the ladder")]
        public static void TryStepDownWalksTheLadder()
        {
            int next;
            Assert.IsTrue(VramCalculator.TryStepDownMaxSize(2048, out next), "2048 can step down");
            Assert.AreEqual(1024, next, "2048 steps down to 1024");

            Assert.IsTrue(VramCalculator.TryStepDownMaxSize(3000, out next), "non ladder values step down too");
            Assert.AreEqual(2048, next, "3000 steps down to the next lower rung, 2048");

            Assert.IsFalse(VramCalculator.TryStepDownMaxSize(32, out next), "the bottom rung cannot step down");
            Assert.AreEqual(32, next, "value is unchanged at the bottom");
        }

        [Test("savingPercent guards against a zero baseline")]
        public static void SavingPercent()
        {
            Assert.AreEqual(50.0, VramCalculator.SavingPercent(100, 50), "halving is 50%");
            Assert.AreEqual(0.0, VramCalculator.SavingPercent(0, 50), "zero baseline reports zero rather than infinity");
            Assert.AreEqual(-100.0, VramCalculator.SavingPercent(100, 200), "negative savings are reported honestly");
        }

        [Test("formatBytes renders binary units")]
        public static void FormatBytesRenders()
        {
            Assert.AreEqual("0 B", TextureFormatInfo.FormatBytes(0), "zero bytes");
            Assert.AreEqual("512 B", TextureFormatInfo.FormatBytes(512), "sub-kilobyte values keep the byte unit");
            Assert.AreEqual("1 KiB", TextureFormatInfo.FormatBytes(1024), "exactly one kibibyte");
            Assert.AreEqual("1.5 KiB", TextureFormatInfo.FormatBytes(1536), "fractional kibibytes");
            Assert.AreEqual("1 MiB", TextureFormatInfo.FormatBytes(1024 * 1024), "one mebibyte");
            Assert.AreEqual("2 GiB", TextureFormatInfo.FormatBytes(2L * 1024 * 1024 * 1024), "two gibibytes");
        }
    }
}
