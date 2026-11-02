using System;
using System.Collections.Generic;
using AvatarTuner.Core;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Tests for the pixel-domain quality arithmetic and the block-compression encoder
    /// models. The product rule under test is that quality figures are derived from real
    /// pixel data, and that a format which cannot be modelled yields no figure at all.
    /// </summary>
    public static class QualityMetricTests
    {
        private static byte[] Solid(int width, int height, int channels, byte value)
        {
            var buffer = new byte[width * height * channels];
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = value;
            }

            return buffer;
        }

        private static byte[] Gradient(int width, int height, int channels)
        {
            var buffer = new byte[width * height * channels];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var value = (byte)((x * 255) / Math.Max(1, width - 1));
                    for (var c = 0; c < channels; c++)
                    {
                        buffer[(y * width + x) * channels + c] = value;
                    }
                }
            }

            return buffer;
        }

        private static byte[] Checkerboard(int width, int height, int channels, byte low, byte high)
        {
            var buffer = new byte[width * height * channels];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var value = ((x / 2) + (y / 2)) % 2 == 0 ? low : high;
                    for (var c = 0; c < channels; c++)
                    {
                        buffer[(y * width + x) * channels + c] = value;
                    }
                }
            }

            return buffer;
        }

        private static byte[] Noise(int width, int height, int channels, int seed)
        {
            // Deterministic linear congruential generator so the test never flakes.
            var buffer = new byte[width * height * channels];
            var state = unchecked((uint)seed);
            for (var i = 0; i < buffer.Length; i++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                buffer[i] = (byte)(state >> 24);
            }

            return buffer;
        }

        [Test("identical buffers have infinite PSNR")]
        public static void IdenticalBuffersHaveInfinitePsnr()
        {
            var a = Gradient(16, 16, 4);
            var b = (byte[])a.Clone();

            Assert.IsTrue(double.IsPositiveInfinity(QualityMetrics.Psnr(a, b, 16, 16, 4)), "identical buffers score infinitely well");
            Assert.AreEqual(0.0, QualityMetrics.Mse(a, b, 16, 16, 4), "identical buffers have zero error");
        }

        [Test("PSNR decreases as the error grows")]
        public static void PsnrMonotonicallyDecreases()
        {
            var reference = Gradient(16, 16, 4);

            var smallError = (byte[])reference.Clone();
            var largeError = (byte[])reference.Clone();
            for (var i = 0; i < smallError.Length; i++)
            {
                smallError[i] = (byte)Math.Min(255, smallError[i] + 1);
                largeError[i] = (byte)Math.Min(255, largeError[i] + 20);
            }

            var goodPsnr = QualityMetrics.Psnr(reference, smallError, 16, 16, 4);
            var badPsnr = QualityMetrics.Psnr(reference, largeError, 16, 16, 4);

            Assert.IsGreater(badPsnr, goodPsnr, "a smaller error must score higher");
            Assert.IsGreater(30.0, goodPsnr, "a one-level error is still a very high PSNR");
        }

        [Test("PSNR rejects mismatched buffers")]
        public static void PsnrRejectsMismatchedBuffers()
        {
            var a = Solid(8, 8, 4, 128);
            var b = Solid(8, 8, 3, 128);

            Assert.Throws<ArgumentException>(delegate { QualityMetrics.Psnr(a, b, 8, 8, 4); }, "channel mismatch must be refused");
            Assert.Throws<ArgumentNullException>(delegate { QualityMetrics.Psnr(null, b, 8, 8, 4); }, "null reference");
            Assert.Throws<ArgumentOutOfRangeException>(delegate { QualityMetrics.Psnr(a, a, 0, 8, 4); }, "zero width");
        }

        [Test("SSIM of identical buffers is one")]
        public static void SsimOfIdenticalBuffersIsOne()
        {
            var a = Gradient(32, 32, 4);
            var b = (byte[])a.Clone();
            Assert.AreClose(1.0, QualityMetrics.Ssim(a, b, 32, 32, 4), 0.0001, "identical buffers score one");
        }

        [Test("SSIM drops when structure is corrupted")]
        public static void SsimDetectsStructuralDamage()
        {
            var reference = Checkerboard(32, 32, 4, 0, 255);
            var damaged = Solid(32, 32, 4, 128);

            var score = QualityMetrics.Ssim(reference, damaged, 32, 32, 4);
            Assert.IsLess(0.5, score, "replacing a checkerboard with flat grey is a large structural loss, got " + score);
        }

        [Test("SSIM falls back sensibly for tiny images")]
        public static void SsimHandlesTinyImages()
        {
            var a = Solid(2, 2, 4, 10);
            var b = Solid(2, 2, 4, 10);
            Assert.AreClose(1.0, QualityMetrics.Ssim(a, b, 2, 2, 4), 0.0001, "tiny identical buffers still score one");
        }

        [Test("edge energy is zero for a flat image and high for a checkerboard")]
        public static void EdgeEnergyDiscriminates()
        {
            var flat = Solid(32, 32, 4, 128);
            var sharp = Checkerboard(32, 32, 4, 0, 255);

            Assert.AreClose(0.0, QualityMetrics.EdgeEnergy(flat, 32, 32, 4), 0.0001, "a flat image has no edges");
            Assert.IsGreater(0.2, QualityMetrics.EdgeEnergy(sharp, 32, 32, 4), "a checkerboard has strong edges");
        }

        [Test("detail score is zero for a flat image and high for a checkerboard")]
        public static void DetailScoreDiscriminates()
        {
            var flat = Solid(32, 32, 4, 128);
            var smooth = Gradient(32, 32, 4);
            var sharp = Checkerboard(32, 32, 4, 0, 255);

            Assert.AreClose(0.0, QualityMetrics.DetailScore(flat, 32, 32, 4), 0.0001, "a flat image has no high frequency energy");
            Assert.IsGreater(
                QualityMetrics.DetailScore(smooth, 32, 32, 4),
                QualityMetrics.DetailScore(sharp, 32, 32, 4),
                "a checkerboard has more high frequency energy than a smooth gradient");
        }

        [Test("alpha coverage measures the opaque fraction")]
        public static void AlphaCoverageMeasures()
        {
            var buffer = new byte[8 * 8 * 4];
            for (var i = 0; i < 64; i++)
            {
                buffer[i * 4 + 3] = (byte)(i < 32 ? 255 : 0);
            }

            Assert.AreClose(0.5, QualityMetrics.AlphaCoverage(buffer, 8, 8, 4, 0.5), 0.0001, "half the pixels are opaque");
            Assert.AreEqual(1.0, QualityMetrics.AlphaCoverage(Solid(8, 8, 3, 10), 8, 8, 3, 0.5), "a buffer without alpha is fully opaque");
        }

        [Test("luminance spread is zero for a flat image")]
        public static void LuminanceSpreadMeasures()
        {
            Assert.AreClose(0.0, QualityMetrics.LuminanceSpread(Solid(8, 8, 4, 100), 8, 8, 4), 0.0001, "flat image has no spread");
            Assert.IsGreater(0.1, QualityMetrics.LuminanceSpread(Checkerboard(8, 8, 4, 0, 255), 8, 8, 4), "a checkerboard has spread");
        }

        [Test("downsampling preserves a solid colour exactly")]
        public static void DownsamplePreservesSolidColour()
        {
            var source = Solid(64, 64, 4, 137);
            var reduced = QualityMetrics.DownsampleBox(source, 64, 64, 4, 16, 16);

            Assert.AreEqual(16 * 16 * 4, reduced.Length, "output size");
            for (var i = 0; i < reduced.Length; i++)
            {
                if (reduced[i] != 137)
                {
                    Assert.Fail("box filtering a solid colour must reproduce the colour, got " + reduced[i] + " at " + i);
                }
            }
        }

        [Test("downsampling averages neighbouring pixels")]
        public static void DownsampleAverages()
        {
            // A 2x2 block of [0, 255, 255, 0] averages to 127.5 -> 128 with round-half-up.
            var source = new byte[2 * 2 * 4];
            source[3] = 255;   // pixel 0 alpha, but we read channel 0 below
            for (var i = 0; i < 4; i++)
            {
                source[0 * 4 + i] = 0;
                source[1 * 4 + i] = 255;
                source[2 * 4 + i] = 255;
                source[3 * 4 + i] = 0;
            }

            var reduced = QualityMetrics.DownsampleBox(source, 2, 2, 4, 1, 1);
            Assert.AreEqual(128, reduced[0], "the four samples average to 128");
        }

        [Test("downsampling refuses to upscale or accept bad arguments")]
        public static void DownsampleRejectsBadArguments()
        {
            var source = Solid(8, 8, 4, 10);
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { QualityMetrics.DownsampleBox(source, 8, 8, 4, 16, 16); }, "upscaling is refused");
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { QualityMetrics.DownsampleBox(source, 8, 8, 4, 0, 4); }, "zero target size is refused");
        }

        // ------------------------------------------------------- encoder simulations

        [Test("a solid colour survives every simulated format")]
        public static void SolidColourSurvivesEveryFormat()
        {
            var source = Solid(16, 16, 4, 128);

            foreach (var format in new[]
                     {
                         TunerTextureFormat.BC1, TunerTextureFormat.BC3,
                         TunerTextureFormat.BC4, TunerTextureFormat.BC5, TunerTextureFormat.BC7
                     })
            {
                var rebuilt = CompressionSimulator.RoundTrip(source, 16, 16, format);
                Assert.AreEqual(source.Length, rebuilt.Length, format + " produces a same-sized buffer");
            }
        }

        [Test("block formats degrade smooth gradients far less than high frequency noise")]
        public static void SmoothContentCompressesBetterThanNoise()
        {
            // A two-colour checkerboard is the wrong probe here: every BC1 block can hold
            // exactly those two colours, so it round-trips losslessly. Genuine per-pixel
            // pseudo-random noise is what BC1 cannot represent.
            var smooth = Gradient(64, 64, 4);
            var noisy = Noise(64, 64, 4, 20260101);

            var smoothPsnr = QualityMetrics.Psnr(smooth, CompressionSimulator.RoundTrip(smooth, 64, 64, TunerTextureFormat.BC1), 64, 64, 4);
            var noisyPsnr = QualityMetrics.Psnr(noisy, CompressionSimulator.RoundTrip(noisy, 64, 64, TunerTextureFormat.BC1), 64, 64, 4);

            Assert.IsGreater(noisyPsnr, smoothPsnr,
                "smooth content must survive BC1 better than a high frequency checkerboard");
        }

        [Test("BC7 always beats BC1 on the same content")]
        public static void Bc7BeatsBc1()
        {
            var source = Gradient(64, 64, 4);

            var bc1 = QualityMetrics.Psnr(source, CompressionSimulator.RoundTrip(source, 64, 64, TunerTextureFormat.BC1), 64, 64, 4);
            var bc7 = QualityMetrics.Psnr(source, CompressionSimulator.RoundTrip(source, 64, 64, TunerTextureFormat.BC7), 64, 64, 4);

            Assert.IsGreater(bc1, bc7, "BC7 is the higher quality format and must not score lower than BC1");
        }

        [Test("formats that cannot be simulated report no PSNR at all")]
        public static void UnmodelledFormatsReportNoPsnr()
        {
            var source = Gradient(32, 32, 4);

            Assert.IsFalse(CompressionSimulator.CanSimulate(TunerTextureFormat.RGBA32), "RGBA32 is not modelled");
            Assert.IsFalse(CompressionSimulator.CanSimulate(TunerTextureFormat.BC6H), "BC6H is not modelled");
            Assert.IsTrue(CompressionSimulator.CanSimulate(TunerTextureFormat.BC1), "BC1 is modelled");

            var evidence = QualityEstimator.Estimate(source, 32, 32, TunerTextureFormat.RGBA32, true);
            Assert.IsFalse(evidence.HasEstimatedPsnr, "no PSNR is produced for an unmodelled format");
            Assert.IsTrue(evidence.HasDetailScore, "structural evidence is still produced");
        }

        [Test("the estimator produces structural evidence for every texture")]
        public static void EstimatorProducesStructuralEvidence()
        {
            var source = Checkerboard(32, 32, 4, 10, 200);
            var evidence = QualityEstimator.Estimate(source, 32, 32, TunerTextureFormat.BC1, true);

            Assert.IsTrue(evidence.HasDetailScore, "detail score is always computed");
            Assert.IsTrue(evidence.HasEstimatedPsnr, "BC1 can be simulated so an estimate is produced");
            Assert.IsFalse(evidence.HasMeasuredPsnr, "an estimate is never reported as a measurement");
            Assert.IsTrue(evidence.HasAnyEvidence, "evidence is present");
            Assert.IsLess(256.0, evidence.EstimatedPsnrDb + 1, "the estimate is a finite number");
            Assert.IsFalse(double.IsNaN(evidence.EstimatedPsnrDb), "the estimate is not NaN");
            Assert.IsFalse(double.IsInfinity(evidence.EstimatedPsnrDb), "the estimate is finite");
        }

        [Test("an alpha-free buffer is fully covered")]
        public static void AlphaCoverageDefaultsToOpaque()
        {
            Assert.AreEqual(1.0, QualityMetrics.AlphaCoverage(Solid(4, 4, 3, 255), 4, 4, 3, 0.5), "three channel buffers are opaque");
        }

        [Test("the estimator refuses bad arguments")]
        public static void EstimatorRejectsBadArguments()
        {
            Assert.Throws<ArgumentNullException>(delegate { QualityEstimator.Estimate(null, 4, 4, TunerTextureFormat.BC1, true); }, "null buffer");
            Assert.Throws<ArgumentOutOfRangeException>(
                delegate { QualityEstimator.Estimate(Solid(4, 4, 4, 1), 0, 4, TunerTextureFormat.BC1, true); }, "zero width");
        }

        [Test("format information is self consistent")]
        public static void FormatInformationIsConsistent()
        {
            foreach (TunerTextureFormat format in Enum.GetValues(typeof(TunerTextureFormat)))
            {
                var name = TextureFormatInfo.DisplayName(format);
                Assert.IsNotNull(name, format + " has a display name");
                Assert.IsTrue(name.Length > 0, format + " has a non-empty display name");

                if (TextureFormatInfo.IsBlockCompressed(format))
                {
                    Assert.AreClose(0.0, TextureFormatInfo.BytesPerPixel(format) % 0.5, 0.0001,
                        format + " block formats have a fractional bytes-per-pixel that is a multiple of 0.5");
                }
            }
        }

        [Test("format names round trip through the parser")]
        public static void FormatNamesRoundTrip()
        {
            foreach (TunerTextureFormat format in Enum.GetValues(typeof(TunerTextureFormat)))
            {
                TunerTextureFormat parsed;
                Assert.IsTrue(TextureFormatInfo.TryParse(format.ToString(), out parsed), format + " parses from its enum name");
                Assert.AreEqual(format, parsed, format + " round trips through the parser");
            }

            TunerTextureFormat alias;
            Assert.IsTrue(TextureFormatInfo.TryParse("DXT1", out alias), "DXT1 alias parses");
            Assert.AreEqual(TunerTextureFormat.BC1, alias, "DXT1 means BC1");
            Assert.IsTrue(TextureFormatInfo.TryParse("dxt5", out alias), "DXT5 alias parses case insensitively");
            Assert.AreEqual(TunerTextureFormat.BC3, alias, "DXT5 means BC3");
            Assert.IsFalse(TextureFormatInfo.TryParse("nonsense-format", out alias), "unknown names are rejected");
        }

        [Test("alpha capability matches the format specifications")]
        public static void AlphaCapabilityMatchesSpec()
        {
            Assert.IsTrue(TextureFormatInfo.SupportsSmoothAlpha(TunerTextureFormat.BC7), "BC7 has smooth alpha");
            Assert.IsTrue(TextureFormatInfo.SupportsSmoothAlpha(TunerTextureFormat.BC3), "BC3 has smooth alpha");
            Assert.IsFalse(TextureFormatInfo.SupportsSmoothAlpha(TunerTextureFormat.BC1), "BC1 alpha is a single bit");
            Assert.IsTrue(TextureFormatInfo.SupportsAlpha(TunerTextureFormat.BC1), "BC1 does store an alpha bit");
            Assert.IsFalse(TextureFormatInfo.SupportsAlpha(TunerTextureFormat.BC5), "BC5 stores no alpha");

            Assert.IsTrue(TextureFormatInfo.IsSupportedOnPc(TunerTextureFormat.BC7), "BC7 is a PC format");
            Assert.IsFalse(TextureFormatInfo.IsSupportedOnPc(TunerTextureFormat.ETC2_RGBA8), "ETC2 is a mobile format");
        }
    }
}
