using System;
namespace AvatarTuner.Core
{
    public static class TextureResampler
    {
        public static byte[] Resize(byte[] rgba, int width, int height, int targetWidth, int targetHeight, ResizeFilter filter, bool srgb, bool premultiplyAlpha = false, bool normalMap = false)
        {
            if (rgba == null) throw new ArgumentNullException("rgba");
            if (width <= 0 || height <= 0 || targetWidth <= 0 || targetHeight <= 0 || (long)width * height * 4 > rgba.Length || (long)targetWidth * targetHeight * 4 > int.MaxValue) throw new ArgumentException("Invalid texture dimensions or buffer");
            if (filter == ResizeFilter.UnityBilinear || filter == ResizeFilter.UnityMitchell) throw new ArgumentException("Unity filters require Unity importer", "filter");
            if (normalMap && (srgb || premultiplyAlpha)) throw new ArgumentException("Normal vectors require linear, unassociated channels");
            if ((long)targetWidth * height * 4 > int.MaxValue) throw new ArgumentException("Intermediate image is too large");
            var output = new byte[targetWidth * targetHeight * 4];
            var horizontal = new double[targetWidth * height * 4];
            double sx = (double)width / targetWidth, sy = (double)height / targetHeight;
            double rx = Radius(sx, filter), ry = Radius(sy, filter);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < targetWidth; x++)
                {
                    double center = (x + 0.5) * sx - 0.5, total = 0;
                    int dst = (y * targetWidth + x) * 4;
                    for (int ix = (int)Math.Floor(center - rx); ix <= (int)Math.Ceiling(center + rx); ix++)
                    {
                        double weight = Weight(ix, center, sx, filter);
                        if (weight == 0) continue;
                        int source = (y * width + Math.Max(0, Math.Min(width - 1, ix))) * 4;
                        double alpha = rgba[source + 3] / 255.0;
                        total += weight;
                        horizontal[dst + 3] += alpha * weight;
                        for (int c = 0; c < 3; c++)
                        {
                            double value = rgba[source + c] / 255.0;
                            if (srgb) value = ToLinear(value);
                            horizontal[dst + c] += (premultiplyAlpha ? value * alpha : value) * weight;
                        }
                    }
                    if (total != 0) for (int c = 0; c < 4; c++) horizontal[dst + c] /= total;
                }
            }
            for (int y = 0; y < targetHeight; y++)
            {
                double center = (y + 0.5) * sy - 0.5;
                for (int x = 0; x < targetWidth; x++)
                {
                    double red = 0, green = 0, blue = 0, alpha = 0, total = 0;
                    for (int iy = (int)Math.Floor(center - ry); iy <= (int)Math.Ceiling(center + ry); iy++)
                    {
                        double weight = Weight(iy, center, sy, filter);
                        if (weight == 0) continue;
                        int source = (Math.Max(0, Math.Min(height - 1, iy)) * targetWidth + x) * 4;
                        red += horizontal[source] * weight;
                        green += horizontal[source + 1] * weight;
                        blue += horizontal[source + 2] * weight;
                        alpha += horizontal[source + 3] * weight;
                        total += weight;
                    }
                    if (total != 0) { red /= total; green /= total; blue /= total; alpha /= total; }
                    if (premultiplyAlpha)
                    {
                        if (alpha > 1e-9) { red /= alpha; green /= alpha; blue /= alpha; }
                        else { red = 0; green = 0; blue = 0; }
                    }
                    if (normalMap)
                    {
                        double nx = red * 2 - 1, ny = green * 2 - 1, nz = blue * 2 - 1;
                        double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (length > 1e-9) { red = (nx / length + 1) / 2; green = (ny / length + 1) / 2; blue = (nz / length + 1) / 2; }
                    }
                    int dst = (y * targetWidth + x) * 4;
                    output[dst] = Byte(srgb ? ToSrgb(red) : red);
                    output[dst + 1] = Byte(srgb ? ToSrgb(green) : green);
                    output[dst + 2] = Byte(srgb ? ToSrgb(blue) : blue);
                    output[dst + 3] = Byte(alpha);
                }
            }
            return output;
        }
        static double Radius(double scale, ResizeFilter filter)
        {
            return filter == ResizeFilter.Area ? Math.Max(0.5, scale / 2) :
                Math.Max(1, scale) * (filter == ResizeFilter.Lanczos3 ? 3 : 2);
        }
        static byte Byte(double x)
        {
            return (byte)Math.Round(Math.Max(0, Math.Min(1, x)) * 255);
        }
        static double ToLinear(double v)
        {
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        static double ToSrgb(double v)
        {
            v = Math.Max(0, v);
            return v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
        }
        static double Weight(int i, double center, double scale, ResizeFilter filter)
        {
            if (filter == ResizeFilter.Area)
            {
                double half = Math.Max(1, scale) / 2;
                return Math.Max(0, Math.Min(i + 0.5, center + half) - Math.Max(i - 0.5, center - half));
            }
            double t = Math.Abs((i - center) / Math.Max(1, scale));
            if (filter == ResizeFilter.Lanczos3)
            {
                if (t >= 3) return 0;
                if (t < 1e-12) return 1;
                return Math.Sin(Math.PI * t) * Math.Sin(Math.PI * t / 3) / (Math.PI * Math.PI * t * t / 3);
            }
            if (t >= 2) return 0;
            const double b = 1.0 / 3, c = 1.0 / 3;
            if (t < 1) return ((12 - 9 * b - 6 * c) * t * t * t + (-18 + 12 * b + 6 * c) * t * t + (6 - 2 * b)) / 6;
            return ((-b - 6 * c) * t * t * t + (6 * b + 30 * c) * t * t + (-12 * b - 48 * c) * t + (8 * b + 24 * c)) / 6;
        }
    }
}
