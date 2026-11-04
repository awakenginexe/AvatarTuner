using System;
using System.Collections.Generic;
using System.IO;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Apply;
using AvatarTuner.Editor.Optimization;

namespace AvatarTuner.StandaloneTests
{
    public static class ReviewedPlanTests
    {
        [Test("changed scene slots, dependency content and new protection markers invalidate review")]
        public static void SceneAndDependenciesInvalidateReview()
        {
            var report = new AvatarAnalysisReport { ScanSucceeded = true };
            report.Materials.Add(new MaterialEntry { Guid = "material", ShaderGuid = "shader" });
            var slots = new List<ObservedSlot> { new ObservedSlot { RendererPath = "Body", SlotIndex = 1, MaterialGuid = "material" } };
            var revision = "before";
            var reviewed = AnalysisRevision.Compute(report, slots, guid => revision);
            slots[0].MaterialGuid = "replacement";
            Assert.AreNotEqual(reviewed, AnalysisRevision.Compute(report, slots, guid => revision), "scene material slot changed");
            slots[0].MaterialGuid = "material";
            revision = "after";
            Assert.AreNotEqual(reviewed, AnalysisRevision.Compute(report, slots, guid => revision), "material/shader content changed");
            revision = "before";
            report.ToolMarkers.Add(new ToolMarkerEntry { ToolId = "vrcfury", Path = "Body" });
            Assert.AreNotEqual(reviewed, AnalysisRevision.Compute(report, slots, guid => revision), "new protected subtree changed");
        }

        [Test("failed persistence restores every generated role and removes newly applied assets")]
        public static void FailedPersistenceRestoresPreviousApplication()
        {
            var root = Path.Combine(Path.GetTempPath(), "AvatarTuner-" + Guid.NewGuid().ToString("N"));
            var output = Path.Combine(root, "generated");
            Directory.CreateDirectory(output);
            try
            {
                var roles = new[] { "texture.png", "material.mat", "controller.controller", "clip.anim", "manifest.json" };
                foreach (var role in roles) { File.WriteAllText(Path.Combine(output, role), "old " + role); File.WriteAllText(Path.Combine(output, role + ".meta"), "guid: " + role); }
                try
                {
                    using (var transaction = new GeneratedFileTransaction(output, Path.Combine(root, "backup")))
                    {
                        transaction.CaptureDirectory(output);
                        foreach (var role in roles) File.WriteAllText(Path.Combine(output, role), "new");
                        File.WriteAllText(Path.Combine(output, "new-material.mat"), "new");
                        throw new IOException("Simulated manifest persistence failure");
                    }
                }
                catch (IOException) { }
                foreach (var role in roles)
                    Assert.AreEqual("old " + role, File.ReadAllText(Path.Combine(output, role)), "restored " + role);
                Assert.IsFalse(File.Exists(Path.Combine(output, "new-material.mat")), "new unrecorded material removed");
            }
            finally { Directory.Delete(root, true); }
        }

        [Test("fresh settings use the remembered preset's actual quality constraints")]
        public static void RememberedPresetMatchesConstraints()
        {
            var quality = TextureSettings.ForPreset(OptimizationPreset.QualityFirst);
            var size = TextureSettings.ForPreset(OptimizationPreset.SizeFirst);
            Assert.AreEqual(50.0, quality.MinPsnr, "Quality First PSNR");
            Assert.AreEqual(0.995, quality.MinSsim, "Quality First SSIM");
            Assert.AreEqual(38.0, size.MinPsnr, "Size First PSNR");
        }
    }
}
