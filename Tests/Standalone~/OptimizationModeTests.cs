using System;
using System.Collections.Generic;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;
using AvatarTuner.Editor.UI;
using AvatarTuner.Editor.ViewModels;

namespace AvatarTuner.StandaloneTests
{
    public static class OptimizationModeTests
    {
        [Test("editing compression retains the corrected category's inherited settings")]
        public static void CompressionEditUsesCorrectedCategory()
        {
            var settings = new OptimizationSettings { Mode = OptimizationMode.Advanced };
            settings.Categories[SemanticCategory.Body] = new TextureSettings { DesiredReduction = 2, MinPsnr = 30 };
            settings.Categories[SemanticCategory.Hair] = new TextureSettings { DesiredReduction = 3, MinPsnr = 49, KeepResolution = true };
            settings.CategoryCorrections["texture"] = SemanticCategory.Hair;
            var editable = settings.Resolve("texture", SemanticCategory.Body).Settings.Clone();
            editable.Format = TunerTextureFormat.BC7;
            settings.Textures["texture"] = editable;
            Assert.AreEqual(3d, editable.DesiredReduction, "corrected-category ratio is retained");
            Assert.AreEqual(49d, editable.MinPsnr, "corrected-category quality is retained");
            Assert.IsTrue(editable.KeepResolution, "corrected-category resolution protection is retained");
            Assert.AreEqual(TunerTextureFormat.Automatic, settings.Categories[SemanticCategory.Hair].Format, "inherited category is not changed by the texture edit");
        }

        [Test("select all includes filtered textures across every page and preserves hidden selections")]
        public static void SelectAllFilteredTextures()
        {
            var textures = new List<PlannedTexture>();
            for (var i = 0; i < 121; i++) textures.Add(new PlannedTexture { TextureGuid = "texture-" + i });
            var selected = new HashSet<string> { "outside-filter" };
            Assert.IsFalse(TextureRows.AllIncluded(textures, selected), "initially unchecked");
            TextureRows.SetIncluded(textures, selected, true);
            Assert.AreEqual(122, selected.Count, "all pages plus existing hidden selection");
            Assert.IsTrue(TextureRows.AllIncluded(textures, selected), "all filtered textures checked");
            TextureRows.SetIncluded(textures, selected, false);
            Assert.AreEqual(1, selected.Count, "unchecking affects only the filtered result");
            Assert.IsTrue(selected.Contains("outside-filter"), "hidden selection is preserved");
            Assert.IsFalse(TextureRows.AllIncluded(new List<PlannedTexture>(), selected), "empty results are never checked");
        }

        [Test("selection ignores missing identities and detects partial selection")]
        public static void PartialTextureSelection()
        {
            var textures = new List<PlannedTexture> { null, new PlannedTexture(), new PlannedTexture { TextureGuid = "a" }, new PlannedTexture { TextureGuid = "b" } };
            var selected = new HashSet<string> { "a" };
            Assert.IsFalse(TextureRows.AllIncluded(textures, selected), "partially selected");
            TextureRows.SetIncluded(textures, selected, true);
            Assert.AreEqual(2, selected.Count, "only persistent texture identities are selected");
            Assert.IsTrue(TextureRows.AllIncluded(textures, selected), "all selectable textures checked");
        }
        [Test("Advanced reduction targets change the chosen candidate while Standard remains automatic")]
        public static void AdvancedReductionTarget()
        {
            var request = new TexturePlanRequest { SourceWidth = 1024, SourceHeight = 1024,
                CurrentFormat = TunerTextureFormat.RGBA32, CurrentFormatKnown = true,
                CurrentMipMaps = false, Usage = TextureUsageKind.AlbedoOpaque };
            var policy = PresetCatalog.GetPolicy(OptimizationPreset.Balanced);
            Func<TextureCandidate, CandidateQuality> quality = candidate => new CandidateQuality { Available = true, Psnr = 100, Ssim = 1 };
            var settings = new TextureSettings { DesiredReduction = 4 };
            var four = AdaptivePlanner.Select(request, policy, null, settings, SemanticCategory.Body, quality, true);
            settings.DesiredReduction = 8;
            var eight = AdaptivePlanner.Select(request, policy, null, settings, SemanticCategory.Body, quality, true);
            Assert.IsFalse(four.IsNoOp || eight.IsNoOp, "both targets have candidates");
            Assert.AreClose(4, four.ReductionAchieved, 0.01, "4x target chooses four times less memory");
            Assert.AreClose(8, eight.ReductionAchieved, 0.01, "8x target chooses eight times less memory");
            var automatic = AdaptivePlanner.Select(request, policy, null, settings, SemanticCategory.Body, quality);
            Assert.IsTrue(automatic.TargetBytes < eight.TargetBytes, "automatic remains smallest acceptable candidate");
            settings.DesiredReduction = 1;
            Assert.IsTrue(AdaptivePlanner.Select(request, policy, null, settings, SemanticCategory.Body, quality, true).IsNoOp,
                "1x target preserves the original without a forced format or resolution");
        }
        [Test("Advanced reduction targets follow SDK RGB24 accounting")]
        public static void AdvancedSdkTarget()
        {
            var report = new AvatarAnalysisReport();
            report.Textures.Add(new TextureEntry { Guid = "rgb", Path = "Assets/rgb.png", Width = 1024, Height = 1024,
                CurrentFormatKnown = true, CurrentFormatName = "RGB24", CurrentMipCount = 1,
                Usage = TextureUsageKind.AlbedoOpaque, Category = SemanticCategory.Body });
            var settings = new OptimizationSettings { Mode = OptimizationMode.Advanced };
            settings.IncludedTextures.Add("rgb");
            var plan = TexturePlanBuilder.BuildAdaptive(report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, settings,
                (entry, candidate) => new CandidateQuality { Available = true, Psnr = 100, Ssim = 1 });
            Assert.AreEqual(4194304L, plan.SourceBytesTotal, "SDK source memory");
            Assert.AreEqual(1048576L, plan.TargetBytesTotal, "4x target uses SDK rather than RGB24 physical bytes");
            Assert.AreClose(4, plan.Textures[0].Plan.ReductionAchieved, 0.01, "displayed achieved reduction uses same basis");
        }

        [Test("an unevaluated avatar can open configuration even before changes are known")]
        public static void UnevaluatedConfiguration()
        {
            Assert.AreEqual(PrimaryAction.ConfigureOptimization,
                WorkflowModel.NextAction(WindowState.Analyzed, true, true, false, false, false), "configuration precedes evaluation");
            Assert.IsNull(WorkflowModel.OptimizeDisabledReasonKey(WindowState.Analyzed, true, true, false, true, false),
                "zero proposed changes before evaluation must not block the mode chooser");
            Assert.AreEqual("ui.hint.noChanges", WorkflowModel.OptimizeDisabledReasonKey(WindowState.Analyzed, true, true, false, true, true),
                "a completed evaluation can report no changes");
        }
        [Test("Standard uses automatic defaults and preserves saved Advanced tuning")]
        public static void StandardDefaults()
        {
            var saved = new OptimizationSettings();
            saved.Global.Format = TunerTextureFormat.BC1;
            saved.Global.DesiredReduction = 12.5;
            saved.Textures["a"] = new TextureSettings { Format = TunerTextureFormat.BC3, DesiredReduction = 2.5 };
            saved.CategoryCorrections["a"] = SemanticCategory.Hair;
            saved.Textures["protected"] = new TextureSettings { Excluded = true, Format = TunerTextureFormat.BC1 };
            var automatic = saved.ForCurrentMode();
            Assert.AreEqual(TunerTextureFormat.Automatic, automatic.Resolve("a", SemanticCategory.Body).Settings.Format, "Standard chooses format");
            Assert.AreEqual(4d, automatic.Global.DesiredReduction, "automatic reduction goal");
            Assert.AreEqual(SemanticCategory.Hair, automatic.CategoryCorrections["a"], "semantic corrections remain valid");
            Assert.AreEqual(2.5, saved.Textures["a"].DesiredReduction, "manual settings are retained");
            Assert.IsTrue(automatic.Resolve("protected", SemanticCategory.Body).Settings.Excluded, "explicit exclusions survive automatic mode");
        }

        [Test("Advanced evaluates only explicitly included textures and keeps individual rates")]
        public static void AdvancedSelection()
        {
            var settings = new OptimizationSettings { Mode = OptimizationMode.Advanced };
            settings.IncludedTextures.Add("a");
            settings.Textures["a"] = new TextureSettings { DesiredReduction = 3.5 };
            Assert.IsFalse(settings.ResolveForOptimization("a", SemanticCategory.Body).Settings.Excluded, "included texture");
            Assert.IsTrue(settings.ResolveForOptimization("b", SemanticCategory.Body).Settings.Excluded, "unchecked texture stays unchanged");
            settings.Textures["b"] = settings.Resolve("b", SemanticCategory.Body).Settings.Clone();
            Assert.IsFalse(settings.Textures["b"].Excluded, "creating custom settings before inclusion does not snapshot selection as an exclusion");
            Assert.AreEqual(3.5, settings.Resolve("a", SemanticCategory.Body).Settings.DesiredReduction, "custom fractional rate");
            var report = new AvatarAnalysisReport();
            foreach (var guid in new[] { "a", "b" }) report.Textures.Add(new TextureEntry
            {
                Guid = guid, Path = "Assets/" + guid + ".png", Name = guid, Width = 256, Height = 256,
                CurrentFormatKnown = true, CurrentFormatName = "RGBA32", CurrentMipCount = 1,
                Usage = TextureUsageKind.AlbedoOpaque, Category = SemanticCategory.Body
            });
            var evaluated = new HashSet<string>();
            TexturePlanBuilder.BuildAdaptive(report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), null, settings,
                (entry, candidate) => { evaluated.Add(entry.Guid); return new CandidateQuality { Available = true, Psnr = 100, Ssim = 1 }; });
            Assert.IsTrue(evaluated.Contains("a"), "included candidates are evaluated");
            Assert.IsFalse(evaluated.Contains("b"), "unchecked candidates never import");
            settings.IncludedTextures.Remove("a");
            Assert.IsTrue(settings.ResolveForOptimization("a", SemanticCategory.Body).Settings.Excluded, "deselection takes effect");
            Assert.AreEqual(3.5, settings.Textures["a"].DesiredReduction, "deselection retains tuning");
        }

        [Test("mode and inclusion persist and invalidate reviewed plans")]
        public static void ModePersistence()
        {
            var settings = new OptimizationSettings { Mode = OptimizationMode.Advanced };
            var empty = settings.ToHashToken();
            settings.IncludedTextures.Add("a");
            Assert.AreNotEqual(empty, settings.ToHashToken(), "selection invalidates review");
            settings.Textures["a"] = new TextureSettings { DesiredReduction = 64.25 };
            var copy = OptimizationSettings.FromDictionary(settings.ToDictionary());
            Assert.AreEqual(settings.ToHashToken(), copy.ToHashToken(), "mode and selection round trip");
            Assert.IsTrue(copy.IncludedTextures.Contains("a"), "included texture persists");
            Assert.AreEqual(64.25, copy.Textures["a"].DesiredReduction, "custom ratios are not restricted to preset rungs");
            var advanced = copy.ToHashToken();
            copy.Mode = OptimizationMode.Standard;
            Assert.AreNotEqual(advanced, copy.ToHashToken(), "mode switch invalidates review");
            var legacy = settings.ToDictionary();
            legacy.Remove("mode"); legacy.Remove("includedTextures");
            copy = OptimizationSettings.FromDictionary(legacy);
            Assert.AreEqual(OptimizationMode.Standard, copy.Mode, "legacy starts automatic");
            Assert.AreEqual(0, copy.IncludedTextures.Count, "legacy does not opt in manual textures");
            legacy["mode"] = "invalid";
            Assert.AreEqual(OptimizationMode.Standard, OptimizationSettings.FromDictionary(legacy).Mode, "invalid mode falls back");
        }
    }
}
