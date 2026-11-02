using System;

namespace AvatarTuner.Core
{
    /// <summary>
    /// Pixel-domain quality arithmetic. Operates on flat byte buffers so it can be unit
    /// tested without any rendering or Unity API.
    ///
    /// Buffers are interleaved with 1, 2, 3 or 4 channels per pixel (e.g. 4 = RGBA, 8-bit).
    /// The reference and candidate buffers must have identical dimensions and channel counts.
    /// </summary>
    public static class QualityMetrics
    {
        /// <summary>
        /// Peak signal-to-noise ratio in dB of <paramref name="candidate" /> against
        /// <paramref name="reference" />. Returns <see cref="PositiveInfinity" /> when the
        /// buffers are identical and <see cref="NegativeInfinity" /> is never returned.
        /// </summary>
        public static double Psnr(byte[] reference, byte[] candidate, int width, int height, int channels)
        {
            ValidateBuffers(reference, candidate, width, height, channels);

            var squaredError = 0.0;
            var total = (long)width * height * channels;
            for (var i = 0; i < total; i++)
            {
                var diff = reference[i] - candidate[i];
                squaredError += diff * diff;
            }

            if (squaredError == 0.0)
            {
                return double.PositiveInfinity;
            }

            var mse = squaredError / total;
            return 10.0 * Math.Log10((255.0 * 255.0) / mse);
        }

        /// <summary>
        /// Mean squared error between two buffers.
        /// </summary>
        public static double Mse(byte[] reference, byte[] candidate, int width, int height, int channels)
        {
            ValidateBuffers(reference, candidate, width, height, channels);

            var squaredError = 0.0;
            var total = (long)width * height * channels;
            for (var i = 0; i < total; i++)
            {
                var diff = reference[i] - candidate[i];
                squaredError += diff * diff;
            }

            return squaredError / total;
        }

        /// <summary>
        /// Structural similarity on the luminance plane, using the standard 8x8 windowed
        /// formulation with the usual constants (C1 = (0.01*255)^2, C2 = (0.03*255)^2).
        /// Returns a value in [-1, 1]; 1 means identical.
        ///
        /// A windowed SSIM rather than a global one, because global SSIM is dominated by
        /// overall contrast and hides local block-compression artefacts.
        /// </summary>
        public static double Ssim(byte[] reference, byte[] candidate, int width, int height, int channels)
        {
            ValidateBuffers(reference, candidate, width, height, channels);
            if (width < 8 || height < 8)
            {
                // Too small for a meaningful window; fall back to a global comparison.
                return GlobalSsim(reference, candidate, width, height, channels);
            }

            const double C1 = 6.5025;   // (0.01 * 255)^2
            const double C2 = 58.5225;  // (0.03 * 255)^2
            const int Window = 8;

            var refLum = ToLuminance(reference, width, height, channels);
            var candLum = ToLuminance(candidate, width, height, channels);

            double sum = 0.0;
            var windows = 0;

            for (var y = 0; y + Window <= height; y += Window)
            {
                for (var x = 0; x + Window <= width; x += Window)
                {
                    double sumR = 0, sumC = 0, sumRR = 0, sumCC = 0, sumRC = 0;
                    var count = Window * Window;

                    for (var wy = 0; wy < Window; wy++)
                    {
                        for (var wx = 0; wx < Window; wx++)
                        {
                            var idx = (y + wy) * width + (x + wx);
                            double r = refLum[idx];
                            double c = candLum[idx];
                            sumR += r;
                            sumC += c;
                            sumRR += r * r;
                            sumCC += c * c;
                            sumRC += r * c;
                        }
                    }

                    var meanR = sumR / count;
                    var meanC = sumC / count;
                    var varR = sumRR / count - meanR * meanR;
                    var varC = sumCC / count - meanC * meanC;
                    var covar = sumRC / count - meanR * meanC;

                    var numerator = (2 * meanR * meanC + C1) * (2 * covar + C2);
                    var denominator = (meanR * meanR + meanC * meanC + C1) * (varR + varC + C2);
                    sum += denominator <= 0.0 ? 1.0 : numerator / denominator;
                    windows++;
                }
            }

            return windows == 0 ? 1.0 : sum / windows;
        }

        private static double GlobalSsim(byte[] reference, byte[] candidate, int width, int height, int channels)
        {
            const double C1 = 6.5025;
            const double C2 = 58.5225;

            var total = (long)width * height;
            double sumR = 0, sumC = 0, sumRR = 0, sumCC = 0, sumRC = 0;
            for (var i = 0; i < total; i++)
            {
                double r = LuminanceAt(reference, i, channels);
                double c = LuminanceAt(candidate, i, channels);
                sumR += r;
                sumC += c;
                sumRR += r * r;
                sumCC += c * c;
                sumRC += r * c;
            }

            var meanR = sumR / total;
            var meanC = sumC / total;
            var varR = sumRR / total - meanR * meanR;
            var varC = sumCC / total - meanC * meanC;
            var covar = sumRC / total - meanR * meanC;

            var numerator = (2 * meanR * meanC + C1) * (2 * covar + C2);
            var denominator = (meanR * meanR + meanC * meanC + C1) * (varR + varC + C2);
            return denominator <= 0.0 ? 1.0 : numerator / denominator;
        }

        /// <summary>
        /// Mean magnitude of the Sobel gradient over the luminance plane, normalised so that
        /// 1.0 corresponds to the strongest possible response. Used as the "edge energy" of
        /// an image: high values mean sharp transitions that compression and downscaling are
        /// most likely to damage.
        /// </summary>
        public static double EdgeEnergy(byte[] pixels, int width, int height, int channels)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException("pixels");
            }

            if (width < 3 || height < 3)
            {
                return 0.0;
            }

            double total = 0.0;
            var samples = 0;

            for (var y = 1; y < height - 1; y++)
            {
                for (var x = 1; x < width - 1; x++)
                {
                    double tl = LuminanceAt(pixels, (y - 1) * width + (x - 1), channels);
                    double tc = LuminanceAt(pixels, (y - 1) * width + x, channels);
                    double tr = LuminanceAt(pixels, (y - 1) * width + (x + 1), channels);
                    double ml = LuminanceAt(pixels, y * width + (x - 1), channels);
                    double mr = LuminanceAt(pixels, y * width + (x + 1), channels);
                    double bl = LuminanceAt(pixels, (y + 1) * width + (x - 1), channels);
                    double bc = LuminanceAt(pixels, (y + 1) * width + x, channels);
                    double br = LuminanceAt(pixels, (y + 1) * width + (x + 1), channels);

                    var gx = -tl - 2 * ml - bl + tr + 2 * mr + br;
                    var gy = -tl - 2 * tc - tr + bl + 2 * bc + br;

                    total += Math.Sqrt(gx * gx + gy * gy);
                    samples++;
                }
            }

            if (samples == 0)
            {
                return 0.0;
            }

            // The maximum Sobel response for a step edge is 4 * 255 per axis, so
            // sqrt(2) * 4 * 255 is the ceiling of the gradient magnitude.
            const double MaxResponse = 1.0 * 4.0 * 255.0 * 1.4142135623730951;
            return Clamp01((total / samples) / MaxResponse);
        }

        /// <summary>
        /// Normalised high-frequency energy, 0..1.
        ///
        /// Computed as the energy remaining after subtracting a 3x3 box-blurred version of
        /// the luminance plane. Smooth images score near 0; images with fine texture, hair
        /// strands, small text or noise score higher. This is the figure the planner uses to
        /// decide whether a texture tolerates resolution reduction.
        /// </summary>
        public static double DetailScore(byte[] pixels, int width, int height, int channels)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException("pixels");
            }

            if (width < 3 || height < 3)
            {
                return 0.0;
            }

            double residualEnergy = 0.0;
            double totalEnergy = 0.0;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = y * width + x;
                    var value = LuminanceAt(pixels, index, channels);

                    double sum = 0.0;
                    var count = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var sx = x + dx;
                            var sy = y + dy;
                            if (sx < 0 || sy < 0 || sx >= width || sy >= height)
                            {
                                continue;
                            }

                            sum += LuminanceAt(pixels, sy * width + sx, channels);
                            count++;
                        }
                    }

                    var blurred = count == 0 ? value : sum / count;
                    var residual = value - blurred;
                    residualEnergy += residual * residual;
                    totalEnergy += value * value;
                }
            }

            if (totalEnergy <= 0.0)
            {
                return 0.0;
            }

            return Clamp01(Math.Sqrt(residualEnergy / totalEnergy));
        }

        /// <summary>
        /// Fraction of pixels whose alpha exceeds <paramref name="threshold" /> (0..1).
        /// Returns 1.0 when the buffer has fewer than four channels, since such a texture
        /// is entirely opaque by definition.
        /// </summary>
        public static double AlphaCoverage(byte[] pixels, int width, int height, int channels, double threshold)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException("pixels");
            }

            if (channels < 4)
            {
                return 1.0;
            }

            if (threshold < 0.0 || threshold > 1.0)
            {
                throw new ArgumentOutOfRangeException("threshold", "threshold must be within 0..1");
            }

            var pixelCount = (long)width * height;
            var limit = (byte)Math.Round(threshold * 255.0);
            long above = 0;

            for (var i = 0; i < pixelCount; i++)
            {
                if (pixels[i * channels + 3] >= limit)
                {
                    above++;
                }
            }

            return pixelCount == 0 ? 0.0 : (double)above / pixelCount;
        }

        /// <summary>
        /// Standard deviation of the luminance plane, normalised by 255 so the result is 0..1.
        /// </summary>
        public static double LuminanceSpread(byte[] pixels, int width, int height, int channels)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException("pixels");
            }

            var pixelCount = (long)width * height;
            if (pixelCount == 0)
            {
                return 0.0;
            }

            double sum = 0;
            for (var i = 0; i < pixelCount; i++)
            {
                sum += LuminanceAt(pixels, i, channels);
            }

            var mean = sum / pixelCount;
            double varianceSum = 0;
            for (var i = 0; i < pixelCount; i++)
            {
                var d = LuminanceAt(pixels, i, channels) - mean;
                varianceSum += d * d;
            }

            return Clamp01(Math.Sqrt(varianceSum / pixelCount) / 255.0);
        }

        /// <summary>
        /// Box-filtered downsampling of an interleaved byte buffer. Used to build preview
        /// comparisons and to feed the quality estimator a reduced-resolution candidate.
        /// </summary>
        public static byte[] DownsampleBox(byte[] pixels, int width, int height, int channels, int newWidth, int newHeight)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException("pixels");
            }

            if (newWidth <= 0 || newHeight <= 0)
            {
                throw new ArgumentOutOfRangeException("newWidth", "target dimensions must be positive");
            }

            if (newWidth > width || newHeight > height)
            {
                throw new ArgumentOutOfRangeException("newWidth", "DownsampleBox only reduces resolution");
            }

            var expected = (long)width * height * channels;
            if (pixels.Length < expected)
            {
                throw new ArgumentException("buffer is smaller than width*height*channels", "pixels");
            }

            var result = new byte[(long)newWidth * newHeight * channels];

            for (var y = 0; y < newHeight; y++)
            {
                var srcY0 = y * height / newHeight;
                var srcY1 = Math.Max(srcY0 + 1, (y + 1) * height / newHeight);

                for (var x = 0; x < newWidth; x++)
                {
                    var srcX0 = x * width / newWidth;
                    var srcX1 = Math.Max(srcX0 + 1, (x + 1) * width / newWidth);

                    for (var c = 0; c < channels; c++)
                    {
                        long sum = 0;
                        var count = 0;
                        for (var sy = srcY0; sy < srcY1; sy++)
                        {
                            for (var sx = srcX0; sx < srcX1; sx++)
                            {
                                sum += pixels[((long)sy * width + sx) * channels + c];
                                count++;
                            }
                        }

                        result[((long)y * newWidth + x) * channels + c] =
                            (byte)Math.Min(255, Math.Max(0, (sum + count / 2) / Math.Max(1, count)));
                    }
                }
            }

            return result;
        }

        /// <summary>Extracts the luminance plane as doubles (Rec. 709 weights).</summary>
        public static double[] ToLuminance(byte[] pixels, int width, int height, int channels)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException("pixels");
            }

            var count = (long)width * height;
            var result = new double[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = LuminanceAt(pixels, i, channels);
            }

            return result;
        }

        private static double LuminanceAt(byte[] pixels, long pixelIndex, int channels)
        {
            var baseIndex = pixelIndex * channels;
            if (channels >= 3)
            {
                return 0.2126 * pixels[baseIndex] + 0.7152 * pixels[baseIndex + 1] + 0.0722 * pixels[baseIndex + 2];
            }

            return pixels[baseIndex];
        }

        private static void ValidateBuffers(byte[] reference, byte[] candidate, int width, int height, int channels)
        {
            if (reference == null)
            {
                throw new ArgumentNullException("reference");
            }

            if (candidate == null)
            {
                throw new ArgumentNullException("candidate");
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException("width", "dimensions must be positive");
            }

            if (channels <= 0)
            {
                throw new ArgumentOutOfRangeException("channels", "channel count must be positive");
            }

            var expected = (long)width * height * channels;
            if (reference.Length < expected)
            {
                throw new ArgumentException("reference buffer is smaller than width*height*channels", "reference");
            }

            if (candidate.Length < expected)
            {
                throw new ArgumentException("candidate buffer is smaller than width*height*channels", "candidate");
            }
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0)
            {
                return 0.0;
            }

            return value > 1.0 ? 1.0 : value;
        }
    }
}
