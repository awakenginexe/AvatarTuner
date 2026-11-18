using System;
using System.IO;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace AvatarTuner.Tests
{
    public class TextureCompressionTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void CompressImportedTextureAtOriginalResolution(bool sparseAlpha, bool normalMap)
        {
            const int size = 256;
            var folder = AssetDatabase.GUIDToAssetPath(AssetDatabase.CreateFolder("Assets", "AvatarTunerTest-" + Guid.NewGuid().ToString("N")));
            var path = folder + "/color.png";
            var output = "Assets/AvatarTuner-Generated/" + Path.GetFileName(folder);
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[size * size];
                for (var i = 0; i < pixels.Length; i++)
                    pixels[i] = normalMap ? new Color32(191, 64, 218, 255) :
                        i % size < 96 ? new Color32(255, 0, 0, 255) : new Color32(0, 0, 255, 255);
                if (sparseAlpha) pixels[pixels.Length - 1].a = 127;
                image.SetPixels32(pixels);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (normalMap) importer.textureType = TextureImporterType.NormalMap;
                importer.alphaIsTransparency = !normalMap;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                var originalHash = AssetDatabase.GetAssetDependencyHash(path);
                var android = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;
                var source = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var report = new AvatarAnalysisReport();
                report.Textures.Add(new TextureEntry { Path = path, Guid = AssetDatabase.AssetPathToGUID(path),
                    Width = source.width, Height = source.height, CurrentFormatName = source.format.ToString(), CurrentFormatKnown = true,
                    CurrentMipCount = source.mipmapCount, HasAlphaChannel = importer.DoesSourceTextureHaveAlpha(),
                    AlphaIsTransparency = importer.alphaIsTransparency, SrgbTexture = importer.sRGBTexture, IsNormalMap = normalMap,
                    Category = SemanticCategory.Outfit, Usage = normalMap ? TextureUsageKind.NormalMap : TextureUsageKind.AlbedoWithAlpha });
                var settings = new OptimizationSettings().ForCurrentMode();
                settings.Global.Platform = android ? OptimizationPlatform.Android : OptimizationPlatform.Windows;
                using (var evaluator = new CandidateEvaluator(null))
                {
                    var batch = TexturePlanBuilder.BuildAdaptive(report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, settings, evaluator.Measure);
                    var plan = batch.Textures[0].Plan;
                    Assert.IsFalse(plan.IsNoOp);
                    TestContext.WriteLine("Format {0}, {1} bytes, PSNR {2}, sparse alpha {3}",
                        plan.TargetFormat, plan.Candidate.Bytes, plan.EstimatedPsnrDb, sparseAlpha);
                    Assert.AreEqual(size, plan.TargetWidth);
                    if (!sparseAlpha) Assert.AreEqual(android ? "ASTC_12x12" : normalMap ? "BC5" : "BC1", plan.TargetFormat.ToString());
                    else if (!android) Assert.AreNotEqual(TunerTextureFormat.BC1, plan.TargetFormat);
                    var generated = TextureOptimizer.Generate(report, batch, output, null, null);
                    Assert.AreEqual(1, generated.Generated.Count);
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(generated.Generated[0].GeneratedPath);
                    TunerTextureFormat actual;
                    Assert.IsTrue(TextureFormatInfo.TryParse(texture.format.ToString(), out actual));
                    Assert.AreEqual(plan.TargetFormat, actual);
                    Assert.AreEqual(plan.Candidate.Bytes, (long)GraphicsFormatUtility.ComputeMipmapSize(size, size, texture.graphicsFormat));
                    Assert.AreEqual(originalHash, AssetDatabase.GetAssetDependencyHash(path));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
                if (AssetDatabase.IsValidFolder(output)) AssetDatabase.DeleteAsset(output);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
