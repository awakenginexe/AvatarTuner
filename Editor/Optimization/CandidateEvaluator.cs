using System;
using System.Collections.Generic;
using System.IO;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using UnityEditor;
using UnityEngine;

namespace AvatarTuner.Editor.Optimization
{
    public sealed class CandidatePreview : IDisposable
    {
        public Texture2D Original, Optimized, Difference;
        public string ErrorKey;
        public void Dispose()
        {
            if (Original != null) UnityEngine.Object.DestroyImmediate(Original);
            if (Optimized != null) UnityEngine.Object.DestroyImmediate(Optimized);
            if (Difference != null) UnityEngine.Object.DestroyImmediate(Difference);
        }
    }

    // Runs only from explicit Editor actions, on Unity's main thread. Sources stay read-only.
    public sealed class CandidateEvaluator : IDisposable
    {
        private readonly string _folder;
        private readonly Dictionary<string, CandidateQuality> _cache;
        private readonly Action<float, string> _progress;
        private readonly Func<bool> _cancelled;
        private string _baselineSource;
        private byte[] _baseline;
        private int _evaluated;

        public CandidateEvaluator(Dictionary<string, CandidateQuality> cache, Action<float, string> progress = null,
            Func<bool> cancelled = null)
        {
            _cache = cache;
            _progress = progress;
            _cancelled = cancelled;
            _folder = "Assets/AvatarTuner-Generated/Candidates-" + Guid.NewGuid().ToString("N");
        }

        public static string PlatformName
        {
            get { return EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android ? "Android" : "Standalone"; }
        }

        public static string Revision(string path)
        {
            return StableHash.CombineOrdered(new[] { path, AssetDatabase.GetAssetDependencyHash(path).ToString(),
                Application.unityVersion, EditorUserBuildSettings.activeBuildTarget.ToString() });
        }

        public static string CacheKey(TextureEntry entry, TextureCandidate candidate)
        {
            return StableHash.CombineOrdered(new[] { Revision(entry.Path), candidate.ToHashToken() });
        }

        public CandidateQuality Measure(TextureEntry entry, TextureCandidate candidate)
        {
            CheckCancelled();
            var key = CacheKey(entry, candidate);
            CandidateQuality cached;
            if (_cache != null && _cache.TryGetValue(key, out cached)) return cached;
            var quality = new CandidateQuality();
            var cacheable = true;
            try
            {
                if (entry.IsNormalMap || !SupportedSource(entry.Path)) return quality;
                if (_progress != null) _progress((float)(++_evaluated % 100) / 100, "opt.phase.candidates");
                CheckCancelled();
                EnsureBaseline(entry);
                var path = ImportCandidate(entry.Path, candidate, _folder + "/candidate");
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                TunerTextureFormat actual;
                if (texture == null || texture.width != candidate.Width || texture.height != candidate.Height ||
                    !TextureFormatInfo.TryParse(texture.format.ToString(), out actual) || actual != candidate.Format)
                    return quality;
                var pixels = Bytes(texture);
                if (texture.width != entry.Width || texture.height != entry.Height)
                    pixels = TextureResampler.Resize(pixels, texture.width, texture.height, entry.Width, entry.Height,
                        ResizeFilter.Mitchell, entry.SrgbTexture);
                quality = CandidateQuality.Compare(_baseline, pixels, entry.Width, entry.Height,
                    entry.HasAlphaChannel || entry.AlphaIsTransparency);
            }
            catch (OperationCanceledException) { cacheable = false; throw; }
            catch (Exception ex)
            {
                Debug.LogWarning("AvatarTuner: " + entry.Path + " (" + ex.Message + ")");
            }
            finally
            {
                if (_cache != null && cacheable)
                {
                    if (_cache.Count >= 2048) _cache.Clear();
                    _cache[key] = quality;
                }
            }
            return quality;
        }

        private void EnsureBaseline(TextureEntry entry)
        {
            if (_baselineSource == entry.Path && _baseline != null) return;
            if (!TextureOptimizer.EnsureFolder(_folder)) throw new IOException(_folder);
            var path = _folder + "/baseline" + Path.GetExtension(entry.Path);
            DeleteIfPresent(path);
            if (!AssetDatabase.CopyAsset(entry.Path, path)) throw new IOException(entry.Path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var settings = importer.GetPlatformTextureSettings(PlatformName);
            settings.overridden = true;
            settings.maxTextureSize = Math.Max(entry.Width, entry.Height);
            settings.format = TextureImporterFormat.RGBA32;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            settings.crunchedCompression = false;
            importer.SetPlatformTextureSettings(settings);
            importer.isReadable = true;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null || texture.width != entry.Width || texture.height != entry.Height)
                throw new InvalidOperationException("Source dimensions changed; analyze again.");
            _baseline = Bytes(texture);
            _baselineSource = entry.Path;
        }

        internal static bool SupportedSource(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            TunerTextureFormat format;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return importer != null && importer.textureType == TextureImporterType.Default &&
                importer.textureShape == TextureImporterShape.Texture2D && texture != null &&
                extension != ".exr" && extension != ".hdr" &&
                TextureFormatInfo.TryParse(texture.format.ToString(), out format) &&
                format != TunerTextureFormat.BC6H && format != TunerTextureFormat.R16 &&
                format != TunerTextureFormat.RHalf && format != TunerTextureFormat.RGHalf && format != TunerTextureFormat.RGBAHalf &&
                format != TunerTextureFormat.RFloat && format != TunerTextureFormat.RGFloat && format != TunerTextureFormat.RGBAFloat;
        }

        internal static bool UsesCustomResize(TextureCandidate candidate)
        {
            return candidate != null && candidate.Filter != ResizeFilter.UnityMitchell && candidate.Filter != ResizeFilter.UnityBilinear;
        }

        internal static string ImportCandidate(string sourcePath, TextureCandidate candidate, string stem)
        {
            if (!SupportedSource(sourcePath)) throw new InvalidOperationException("Unsupported texture semantics.");
            var custom = UsesCustomResize(candidate);
            var path = stem + (custom ? ".png" : Path.GetExtension(sourcePath));
            DeleteIfPresent(path);
            if (custom)
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
                    throw new NotSupportedException("Custom resizing requires PNG or JPEG input.");
                var sourceImporter = (TextureImporter)AssetImporter.GetAtPath(sourcePath);
                Texture2D decoded = null;
                Texture2D resized = null;
                try
                {
                    decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                    if (!ImageConversion.LoadImage(decoded, File.ReadAllBytes(sourcePath))) throw new IOException(sourcePath);
                    var bytes = TextureResampler.Resize(Bytes(decoded), decoded.width, decoded.height,
                        candidate.Width, candidate.Height, candidate.Filter, sourceImporter.sRGBTexture,
                        sourceImporter.alphaIsTransparency && sourceImporter.alphaSource == TextureImporterAlphaSource.FromInput);
                    resized = MakeTexture(bytes, candidate.Width, candidate.Height);
                    File.WriteAllBytes(path, ImageConversion.EncodeToPNG(resized));
                    AssetDatabase.ImportAsset(path);
                }
                finally
                {
                    if (decoded != null) UnityEngine.Object.DestroyImmediate(decoded);
                    if (resized != null) UnityEngine.Object.DestroyImmediate(resized);
                }
            }
            else if (!AssetDatabase.CopyAsset(sourcePath, path)) throw new IOException(sourcePath);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new IOException(path);
            TextureImporterFormat format;
            if (!Enum.TryParse(TextureFormatMapping.ImporterFormatName(candidate.Format), out format))
                throw new NotSupportedException(candidate.Format.ToString());
            var plan = new TexturePlan { Candidate = candidate, TargetMaxSize = candidate.MaxSize,
                TargetFormat = candidate.Format, TargetMipMaps = candidate.MipMaps };
            TextureOptimizer.ApplySettings(importer, (TextureImporter)AssetImporter.GetAtPath(sourcePath), plan, format);
            importer.isReadable = true;
            importer.SaveAndReimport();
            return path;
        }

        internal string Materialize(string sourcePath, TextureCandidate candidate)
        {
            if (!TextureOptimizer.EnsureFolder(_folder)) throw new IOException(_folder);
            return ImportCandidate(sourcePath, candidate, _folder + "/output");
        }

        public CandidatePreview Preview(TextureEntry entry, TexturePlan plan)
        {
            var result = new CandidatePreview();
            try
            {
                if (plan == null || plan.Candidate == null || !SupportedSource(entry.Path))
                    throw new NotSupportedException();
                EnsureBaseline(entry);
                var path = ImportCandidate(entry.Path, plan.Candidate, _folder + "/preview");
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var width = Math.Min(512, entry.Width);
                var height = Math.Max(1, (int)Math.Round((double)entry.Height * width / entry.Width));
                if (height > 512) { width = Math.Max(1, width * 512 / height); height = 512; }
                var original = TextureResampler.Resize(_baseline, entry.Width, entry.Height, width, height, ResizeFilter.Area, entry.SrgbTexture);
                var optimized = TextureResampler.Resize(Bytes(texture), texture.width, texture.height, width, height, ResizeFilter.Area, entry.SrgbTexture);
                var difference = new byte[original.Length];
                for (var i = 0; i < difference.Length; i++)
                    difference[i] = i % 4 == 3 ? (byte)255 : (byte)Math.Abs(original[i] - optimized[i]);
                result.Original = MakeTexture(original, width, height);
                result.Optimized = MakeTexture(optimized, width, height);
                result.Difference = MakeTexture(difference, width, height);
            }
            catch (Exception)
            {
                result.Dispose();
                result.Original = result.Optimized = result.Difference = null;
                result.ErrorKey = "workspace.previewUnavailable";
            }
            return result;
        }

        private static byte[] Bytes(Texture2D texture)
        {
            var pixels = texture.GetPixels32();
            var bytes = new byte[checked(pixels.Length * 4)];
            for (var i = 0; i < pixels.Length; i++)
            {
                bytes[i * 4] = pixels[i].r; bytes[i * 4 + 1] = pixels[i].g;
                bytes[i * 4 + 2] = pixels[i].b; bytes[i * 4 + 3] = pixels[i].a;
            }
            return bytes;
        }

        private static Texture2D MakeTexture(byte[] bytes, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3]);
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static void DeleteIfPresent(string path)
        {
            if (File.Exists(path) && !AssetDatabase.DeleteAsset(path)) throw new IOException(path);
        }

        private void CheckCancelled()
        {
            if (_cancelled != null && _cancelled()) throw new OperationCanceledException();
        }

        public void Dispose()
        {
            _baseline = null;
            if (AssetDatabase.IsValidFolder(_folder)) AssetDatabase.DeleteAsset(_folder);
        }
    }
}
