using System;

namespace AvatarTuner.Core
{
    /// <summary>
    /// Software encoder models for the block-compressed formats AvatarTuner proposes.
    ///
    /// WHAT THIS IS
    ///   A real, deterministic implementation of the published BC1/BC3/BC4/BC5 block layouts,
    ///   plus an approximate model for BC7. Encoding a buffer and decoding it back produces a
    ///   genuine reconstruction that can be compared against the source with PSNR/SSIM.
    ///
    /// WHAT THIS IS NOT
    ///   It is not Unity's compressor, and it is not the GPU's decoder. Real encoders use
    ///   wider endpoint searches and multi-pass refinement, so they normally do *better* than
    ///   these models. Consequently every number derived from here is an ESTIMATE and is
    ///   labelled as such in the UI. When a real generated asset is available, its measured
    ///   figure replaces the estimate and is labelled "measured".
    ///
    /// Deliberately conservative: better to under-promise quality than to claim a number the
    /// real pipeline does not reproduce.
    /// </summary>
    public static class CompressionSimulator
    {
        /// <summary>
        /// Encodes and immediately decodes <paramref name="rgba" /> through a simplified
        /// model of <paramref name="format" />, returning the reconstruction.
        /// </summary>
        /// <param name="rgba">Interleaved 8-bit pixels, 4 channels (RGBA).</param>
        /// <param name="width">Image width; any positive value is accepted (padded internally).</param>
        /// <param name="height">Image height.</param>
        /// <param name="format">Target block format. Uncompressed formats are returned unchanged.</param>
        public static byte[] RoundTrip(byte[] rgba, int width, int height, TunerTextureFormat format)
        {
            if (rgba == null)
            {
                throw new ArgumentNullException("rgba");
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException("width", "dimensions must be positive");
            }

            var expected = (long)width * height * 4;
            if (rgba.Length < expected)
            {
                throw new ArgumentException("buffer is smaller than width*height*4", "rgba");
            }

            switch (format)
            {
                case TunerTextureFormat.BC1:
                    return EncodeBc1(rgba, width, height);
                case TunerTextureFormat.BC3:
                    return EncodeBc3(rgba, width, height);
                case TunerTextureFormat.BC4:
                    return EncodeBc4(rgba, width, height);
                case TunerTextureFormat.BC5:
                    return EncodeBc5(rgba, width, height);
                case TunerTextureFormat.BC7:
                    return EncodeBc7(rgba, width, height);
                default:
                    // Uncompressed formats are reproduced exactly; the caller is responsible
                    // for applying resolution reduction separately.
                    var copy = new byte[rgba.Length];
                    Buffer.BlockCopy(rgba, 0, copy, 0, rgba.Length);
                    return copy;
            }
        }

        /// <summary>
        /// True when <see cref="RoundTrip" /> models this format. For formats it does not
        /// model, quality figures must not be produced at all.
        /// </summary>
        public static bool CanSimulate(TunerTextureFormat format)
        {
            switch (format)
            {
                case TunerTextureFormat.BC1:
                case TunerTextureFormat.BC3:
                case TunerTextureFormat.BC4:
                case TunerTextureFormat.BC5:
                case TunerTextureFormat.BC7:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// How faithful the model is for a given format. Surfaced in the UI so that a BC7
        /// estimate is never presented with the same authority as a BC1 estimate.
        /// </summary>
        public static string ModelLabel(TunerTextureFormat format)
        {
            switch (format)
            {
                case TunerTextureFormat.BC7:
                    return "BC7 endpoint model (approximate)";
                case TunerTextureFormat.BC1:
                case TunerTextureFormat.BC3:
                case TunerTextureFormat.BC4:
                case TunerTextureFormat.BC5:
                    return format + " block layout (simplified encoder)";
                default:
                    return "no compression model";
            }
        }

        // --------------------------------------------------------------------- BC1

        private static byte[] EncodeBc1(byte[] rgba, int width, int height)
        {
            var output = new byte[rgba.Length];
            ForEachBlock(width, height, delegate(int blockX, int blockY, int blockWidth, int blockHeight)
            {
                ushort c0, c1;
                byte[] indices;
                FindColourEndpoints(rgba, width, blockX, blockY, blockWidth, blockHeight, out c0, out c1, out indices);

                // Four-colour mode requires c0 > c1 as packed 565 values.
                if (c0 <= c1)
                {
                    var swap = c0;
                    c0 = c1;
                    c1 = swap;
                    for (var i = 0; i < indices.Length; i++)
                    {
                        indices[i] = RemapIndex4(indices[i]);
                    }
                }

                var palette = BuildBc1Palette(c0, c1);
                WriteBlock(rgba, output, width, blockX, blockY, blockWidth, blockHeight, palette, indices);
            });

            return output;
        }

        private static byte[] BuildBc1Palette(ushort c0, ushort c1)
        {
            var palette = new byte[4 * 3];
            WriteRgb565(palette, 0, c0);
            WriteRgb565(palette, 3, c1);
            for (var channel = 0; channel < 3; channel++)
            {
                palette[6 + channel] = (byte)((2 * palette[0 + channel] + palette[3 + channel] + 1) / 3);
                palette[9 + channel] = (byte)((palette[0 + channel] + 2 * palette[3 + channel] + 1) / 3);
            }

            return palette;
        }

        /// <summary>Swaps the 4-colour palette indices when endpoints are exchanged (2&lt;-&gt;3).</summary>
        private static byte RemapIndex4(byte index)
        {
            switch (index)
            {
                case 2:
                    return 3;
                case 3:
                    return 2;
                default:
                    return index;
            }
        }

        // --------------------------------------------------------------------- BC3

        private static byte[] EncodeBc3(byte[] rgba, int width, int height)
        {
            var output = new byte[rgba.Length];
            ForEachBlock(width, height, delegate(int blockX, int blockY, int blockWidth, int blockHeight)
            {
                byte a0, a1;
                byte[] alphaIndices;
                FindAlphaEndpoints(rgba, width, 3, blockX, blockY, blockWidth, blockHeight, out a0, out a1, out alphaIndices);

                ushort c0, c1;
                byte[] colourIndices;
                FindColourEndpoints(rgba, width, blockX, blockY, blockWidth, blockHeight, out c0, out c1, out colourIndices);

                if (c0 <= c1)
                {
                    var swap = c0;
                    c0 = c1;
                    c1 = swap;
                    for (var i = 0; i < colourIndices.Length; i++)
                    {
                        colourIndices[i] = RemapIndex4(colourIndices[i]);
                    }
                }

                var palette = BuildBc1Palette(c0, c1);
                WriteBlock(rgba, output, width, blockX, blockY, blockWidth, blockHeight, palette, colourIndices);

                var alphaPalette = BuildAlphaPalette(a0, a1);
                for (var i = 0; i < alphaIndices.Length; i++)
                {
                    var px = blockX + (i % blockWidth);
                    var py = blockY + (i / blockWidth);
                    output[((long)py * width + px) * 4 + 3] = alphaPalette[alphaIndices[i]];
                }
            });

            return output;
        }

        private static byte[] BuildAlphaPalette(byte a0, byte a1)
        {
            var palette = new byte[8];
            palette[0] = a0;
            palette[1] = a1;

            if (a0 > a1)
            {
                for (var i = 1; i <= 6; i++)
                {
                    palette[1 + i] = (byte)(((7 - i) * a0 + i * a1 + 3) / 7);
                }
            }
            else
            {
                for (var i = 1; i <= 4; i++)
                {
                    palette[1 + i] = (byte)(((5 - i) * a0 + i * a1 + 2) / 5);
                }

                palette[6] = 0;
                palette[7] = 255;
            }

            return palette;
        }

        // ---------------------------------------------------------------- BC4 / BC5

        private static byte[] EncodeBc4(byte[] rgba, int width, int height)
        {
            return EncodeSingleChannel(rgba, width, height, 0);
        }

        private static byte[] EncodeBc5(byte[] rgba, int width, int height)
        {
            var red = EncodeSingleChannel(rgba, width, height, 0);
            var green = EncodeSingleChannel(rgba, width, height, 1);

            var output = new byte[rgba.Length];
            for (var i = 0; i < (long)width * height; i++)
            {
                output[i * 4 + 0] = red[i * 4 + 0];
                output[i * 4 + 1] = green[i * 4 + 1];

                // BC5 stores only two channels. The third is reconstructed from the unit
                // sphere at sample time; for comparison purposes it is rebuilt here so the
                // caller sees a normal map rather than a red/green pair.
                var r = output[i * 4 + 0] / 255.0 * 2.0 - 1.0;
                var g = output[i * 4 + 1] / 255.0 * 2.0 - 1.0;
                var bSquared = 1.0 - r * r - g * g;
                var b = bSquared <= 0.0 ? 0.0 : Math.Sqrt(bSquared);
                output[i * 4 + 2] = (byte)Math.Min(255, Math.Max(0, Math.Round((b * 0.5 + 0.5) * 255.0)));
                output[i * 4 + 3] = rgba[i * 4 + 3];
            }

            return output;
        }

        private static byte[] EncodeSingleChannel(byte[] rgba, int width, int height, int channel)
        {
            var output = new byte[rgba.Length];
            ForEachBlock(width, height, delegate(int blockX, int blockY, int blockWidth, int blockHeight)
            {
                byte a0, a1;
                byte[] indices;
                FindAlphaEndpoints(rgba, width, channel, blockX, blockY, blockWidth, blockHeight, out a0, out a1, out indices);
                var palette = BuildAlphaPalette(a0, a1);

                for (var i = 0; i < indices.Length; i++)
                {
                    var px = blockX + (i % blockWidth);
                    var py = blockY + (i / blockWidth);
                    var value = palette[indices[i]];
                    var baseIndex = ((long)py * width + px) * 4;
                    output[baseIndex + 0] = value;
                    output[baseIndex + 1] = value;
                    output[baseIndex + 2] = value;
                    output[baseIndex + 3] = rgba[baseIndex + 3];
                }
            });

            return output;
        }

        // --------------------------------------------------------------------- BC7

        private static byte[] EncodeBc7(byte[] rgba, int width, int height)
        {
            // Approximate model: per 4x4 block, two RGBA endpoints interpolated with 4-bit
            // indices, endpoints quantised to 7 bits per component (mode 6 precision).
            // Real BC7 adds partitioned modes and per-endpoint subset fitting, so this is a
            // lower bound on achievable quality.
            var output = new byte[rgba.Length];

            ForEachBlock(width, height, delegate(int blockX, int blockY, int blockWidth, int blockHeight)
            {
                var count = blockWidth * blockHeight;
                var luminances = new double[count];
                double minLum = double.MaxValue, maxLum = double.MinValue;
                var minIndex = 0;
                var maxIndex = 0;

                for (var i = 0; i < count; i++)
                {
                    var px = blockX + (i % blockWidth);
                    var py = blockY + (i / blockWidth);
                    var baseIndex = ((long)py * width + px) * 4;
                    double lum = 0.2126 * rgba[baseIndex] + 0.7152 * rgba[baseIndex + 1] + 0.0722 * rgba[baseIndex + 2];
                    luminances[i] = lum;
                    if (lum < minLum)
                    {
                        minLum = lum;
                        minIndex = i;
                    }

                    if (lum > maxLum)
                    {
                        maxLum = lum;
                        maxIndex = i;
                    }
                }

                var endA = Sample(rgba, width, blockX, blockY, blockWidth, blockHeight, maxIndex);
                var endB = Sample(rgba, width, blockX, blockY, blockWidth, blockHeight, minIndex);

                for (var c = 0; c < 4; c++)
                {
                    endA[c] = Quantize7(endA[c]);
                    endB[c] = Quantize7(endB[c]);
                }

                for (var i = 0; i < count; i++)
                {
                    var px = blockX + (i % blockWidth);
                    var py = blockY + (i / blockWidth);
                    var baseIndex = ((long)py * width + px) * 4;

                    var bestIndex = 0;
                    var bestError = double.MaxValue;
                    for (var candidate = 0; candidate < 16; candidate++)
                    {
                        var t = candidate / 15.0;
                        double error = 0;
                        for (var c = 0; c < 3; c++)
                        {
                            var value = endA[c] * t + endB[c] * (1.0 - t);
                            var diff = value - rgba[baseIndex + c];
                            error += diff * diff;
                        }

                        if (error < bestError)
                        {
                            bestError = error;
                            bestIndex = candidate;
                        }
                    }

                    var blend = bestIndex / 15.0;
                    for (var c = 0; c < 4; c++)
                    {
                        var value = endA[c] * blend + endB[c] * (1.0 - blend);
                        output[baseIndex + c] = (byte)Math.Min(255, Math.Max(0, Math.Round(value)));
                    }
                }
            });

            return output;
        }

        private static byte Quantize7(byte value)
        {
            // Round-trip through 7-bit precision: quantise then expand back to 8-bit.
            var q = (value * 127 + 127) / 255;
            return (byte)((q * 255 + 63) / 127);
        }

        // ---------------------------------------------------------------- helpers

        private delegate void BlockVisitor(int blockX, int blockY, int blockWidth, int blockHeight);

        private static void ForEachBlock(int width, int height, BlockVisitor visitor)
        {
            for (var blockY = 0; blockY < height; blockY += 4)
            {
                var blockHeight = Math.Min(4, height - blockY);
                for (var blockX = 0; blockX < width; blockX += 4)
                {
                    var blockWidth = Math.Min(4, width - blockX);
                    visitor(blockX, blockY, blockWidth, blockHeight);
                }
            }
        }

        private static byte[] Sample(byte[] rgba, int width, int blockX, int blockY, int blockWidth, int blockHeight, int index)
        {
            var px = blockX + (index % blockWidth);
            var py = blockY + (index / blockWidth);
            var baseIndex = ((long)py * width + px) * 4;
            return new[] { rgba[baseIndex], rgba[baseIndex + 1], rgba[baseIndex + 2], rgba[baseIndex + 3] };
        }

        private static void FindColourEndpoints(
            byte[] rgba, int width,
            int blockX, int blockY, int blockWidth, int blockHeight,
            out ushort endpoint0, out ushort endpoint1, out byte[] indices)
        {
            var count = blockWidth * blockHeight;
            var luminances = new double[count];
            double minLum = double.MaxValue, maxLum = double.MinValue;
            var minIndex = 0;
            var maxIndex = 0;

            for (var i = 0; i < count; i++)
            {
                var px = blockX + (i % blockWidth);
                var py = blockY + (i / blockWidth);
                var baseIndex = ((long)py * width + px) * 4;
                double lum = 0.2126 * rgba[baseIndex] + 0.7152 * rgba[baseIndex + 1] + 0.0722 * rgba[baseIndex + 2];
                luminances[i] = lum;
                if (lum < minLum)
                {
                    minLum = lum;
                    minIndex = i;
                }

                if (lum > maxLum)
                {
                    maxLum = lum;
                    maxIndex = i;
                }
            }

            endpoint0 = PackRgb565(Sample(rgba, width, blockX, blockY, blockWidth, blockHeight, maxIndex));
            endpoint1 = PackRgb565(Sample(rgba, width, blockX, blockY, blockWidth, blockHeight, minIndex));

            var palette = BuildBc1Palette(endpoint0, endpoint1);
            indices = new byte[count];
            for (var i = 0; i < count; i++)
            {
                var px = blockX + (i % blockWidth);
                var py = blockY + (i / blockWidth);
                var baseIndex = ((long)py * width + px) * 4;

                var bestIndex = 0;
                var bestError = long.MaxValue;
                for (var p = 0; p < 4; p++)
                {
                    long error = 0;
                    for (var c = 0; c < 3; c++)
                    {
                        var diff = rgba[baseIndex + c] - palette[p * 3 + c];
                        error += diff * diff;
                    }

                    if (error < bestError)
                    {
                        bestError = error;
                        bestIndex = p;
                    }
                }

                indices[i] = (byte)bestIndex;
            }
        }

        private static void FindAlphaEndpoints(
            byte[] rgba, int width, int channel,
            int blockX, int blockY, int blockWidth, int blockHeight,
            out byte endpoint0, out byte endpoint1, out byte[] indices)
        {
            var count = blockWidth * blockHeight;
            byte min = 255, max = 0;

            for (var i = 0; i < count; i++)
            {
                var px = blockX + (i % blockWidth);
                var py = blockY + (i / blockWidth);
                var value = rgba[((long)py * width + px) * 4 + channel];
                if (value < min)
                {
                    min = value;
                }

                if (value > max)
                {
                    max = value;
                }
            }

            // a0 must be the larger endpoint for the 8-value interpolated palette.
            endpoint0 = max;
            endpoint1 = min;

            var palette = BuildAlphaPalette(endpoint0, endpoint1);
            indices = new byte[count];
            for (var i = 0; i < count; i++)
            {
                var px = blockX + (i % blockWidth);
                var py = blockY + (i / blockWidth);
                var value = rgba[((long)py * width + px) * 4 + channel];

                var bestIndex = 0;
                var bestError = int.MaxValue;
                for (var p = 0; p < 8; p++)
                {
                    var diff = value - palette[p];
                    var error = diff * diff;
                    if (error < bestError)
                    {
                        bestError = error;
                        bestIndex = p;
                    }
                }

                indices[i] = (byte)bestIndex;
            }
        }

        private static void WriteBlock(
            byte[] source, byte[] output, int width,
            int blockX, int blockY, int blockWidth, int blockHeight,
            byte[] palette, byte[] indices)
        {
            for (var i = 0; i < indices.Length; i++)
            {
                var px = blockX + (i % blockWidth);
                var py = blockY + (i / blockWidth);
                var baseIndex = ((long)py * width + px) * 4;
                var paletteIndex = indices[i] * 3;
                output[baseIndex + 0] = palette[paletteIndex + 0];
                output[baseIndex + 1] = palette[paletteIndex + 1];
                output[baseIndex + 2] = palette[paletteIndex + 2];
                output[baseIndex + 3] = source[baseIndex + 3];
            }
        }

        private static ushort PackRgb565(byte[] rgba)
        {
            var r = (ushort)(rgba[0] >> 3);
            var g = (ushort)(rgba[1] >> 2);
            var b = (ushort)(rgba[2] >> 3);
            return (ushort)((r << 11) | (g << 5) | b);
        }

        private static void WriteRgb565(byte[] target, int offset, ushort packed)
        {
            var r = (packed >> 11) & 0x1F;
            var g = (packed >> 5) & 0x3F;
            var b = packed & 0x1F;
            target[offset + 0] = (byte)((r << 3) | (r >> 2));
            target[offset + 1] = (byte)((g << 2) | (g >> 4));
            target[offset + 2] = (byte)((b << 3) | (b >> 2));
        }
    }

    /// <summary>
    /// Derives <see cref="QualityEvidence" /> from real source pixels.
    ///
    /// Everything returned here is computed from the supplied buffer. When the target format
    /// cannot be modelled, no PSNR is returned at all - a missing figure is reported as
    /// missing rather than replaced with a plausible-looking number.
    /// </summary>
    public static class QualityEstimator
    {
        /// <summary>
        /// Computes pixel-derived evidence for a texture.
        /// </summary>
        /// <param name="rgba">Interleaved RGBA bytes of the source at the analysed resolution.</param>
        /// <param name="width">Width of the analysed image.</param>
        /// <param name="height">Height of the analysed image.</param>
        /// <param name="targetFormat">Format being considered. PSNR is only produced when the format can be simulated.</param>
        /// <param name="simulateFormatChange">False to skip the PSNR pass and report structure only.</param>
        public static QualityEvidence Estimate(
            byte[] rgba,
            int width,
            int height,
            TunerTextureFormat targetFormat,
            bool simulateFormatChange)
        {
            if (rgba == null)
            {
                throw new ArgumentNullException("rgba");
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException("width", "dimensions must be positive");
            }

            var expected = (long)width * height * 4;
            if (rgba.Length < expected)
            {
                throw new ArgumentException("buffer is smaller than width*height*4", "rgba");
            }

            var evidence = new QualityEvidence();
            evidence.DetailScore = QualityMetrics.DetailScore(rgba, width, height, 4);
            evidence.HasDetailScore = true;
            evidence.EdgeEnergy = QualityMetrics.EdgeEnergy(rgba, width, height, 4);
            evidence.AlphaCoverage = QualityMetrics.AlphaCoverage(rgba, width, height, 4, 0.5);
            evidence.LuminanceSpread = QualityMetrics.LuminanceSpread(rgba, width, height, 4);

            if (simulateFormatChange && CompressionSimulator.CanSimulate(targetFormat))
            {
                var reconstructed = CompressionSimulator.RoundTrip(rgba, width, height, targetFormat);
                evidence.EstimatedPsnrDb = QualityMetrics.Psnr(rgba, reconstructed, width, height, 4);
                evidence.HasEstimatedPsnr = true;
            }

            return evidence;
        }

        /// <summary>
        /// Combines evidence for a resolution change with an already computed format
        /// estimate. Resolution loss is *not* given a PSNR figure here on purpose: without
        /// rendering the downsampled result there is no honest number to report.
        /// </summary>
        public static QualityEvidence CombineResolutionChange(QualityEvidence formatEvidence)
        {
            return formatEvidence ?? new QualityEvidence();
        }
    }
}
