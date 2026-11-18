using System;
using System.Collections.Generic;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Automation;
using AvatarTuner.Editor.Optimization;
using AvatarTuner.Editor.ViewModels;

namespace AvatarTuner.StandaloneTests
{
    public static class AgentProtocolTests
    {
        [Test]
        public static void CommandsAreExplicit()
        {
            Assert.AreEqual("analyze", AgentRequest.Parse("{\"action\":\"analyze\"}").Action, "analysis is explicit");
            Assert.IsNull(AgentRequest.Parse("{\"action\":\"preview\"}").Settings, "omission uses saved settings");
            Assert.AreEqual("reviewed", AgentRequest.Parse("{\"action\":\"optimize\",\"previewToken\":\"reviewed\"}").PreviewToken, "token retained");
            foreach (var json in new[] { "null", "[]", "{}", "{", "{\"action\":\"delete\"}",
                "{\"action\":\"optimize\"}", "{\"action\":\"optimize\",\"previewToken\":true}",
                "{\"action\":\"analyze\",\"settings\":{}}", "{\"action\":\"restore\",\"previewToken\":\"x\"}",
                "{\"action\":\"preview\",\"unexpected\":true}" })
                Assert.Throws<ArgumentException>(() => AgentRequest.Parse(json), "reject invalid request: " + json);
        }

        [Test]
        public static void SettingsUseTheExistingSchema()
        {
            var request = AgentRequest.Parse("{\"action\":\"preview\",\"settings\":{\"mode\":\"Advanced\",\"includedTextures\":[\"b\",\"a\"],\"global\":{\"desiredReduction\":3.5,\"format\":\"BC7\"},\"textures\":{\"a\":{\"protectQuality\":true}},\"categoryCorrections\":{\"b\":\"Hair\"}}}");
            Assert.AreEqual(OptimizationMode.Advanced, request.Settings.Mode, "advanced mode");
            Assert.AreEqual(3.5, request.Settings.Global.DesiredReduction, "fractional ratios survive");
            Assert.AreEqual(TunerTextureFormat.BC7, request.Settings.Global.Format, "format survives");
            Assert.IsTrue(request.Settings.IncludedTextures.Contains("a"), "unordered selections accepted");
            Assert.IsTrue(request.Settings.Textures["a"].ProtectQuality, "protection survives");
            Assert.AreEqual(SemanticCategory.Hair, request.Settings.CategoryCorrections["b"], "correction survives");
            var report = new AvatarAnalysisReport();
            report.Textures.Add(new TextureEntry { Guid = "a" });
            report.Textures.Add(new TextureEntry { Guid = "b" });
            Assert.IsNull(request.FindUnknownTexture(report), "known texture settings accepted");
            request.Settings.IncludedTextures.Add("A");
            Assert.AreEqual("A", request.FindUnknownTexture(report), "wrong-case GUID cannot silently skip inclusion");
        }

        [Test]
        public static void ExplicitSettingsReplaceLegacyTuningButPreserveExclusions()
        {
            var saved = new OptimizationSettings { Mode = OptimizationMode.Advanced };
            var overrides = new Dictionary<string, TextureOverride>
            {
                { "forced", new TextureOverride { ForceFormat = true, ForcedFormat = TunerTextureFormat.BC1 } },
                { "excluded", new TextureOverride { Excluded = true, ForceMaxSize = true, ForcedMaxSize = 128 } }
            };
            var savedResult = AgentRequest.Parse("{\"action\":\"preview\"}").ResolveSettings(saved, overrides);
            Assert.AreEqual(saved, savedResult, "omission retains saved settings");
            Assert.AreEqual(2, overrides.Count, "omission retains saved overrides");
            var request = AgentRequest.Parse("{\"action\":\"preview\",\"settings\":{\"mode\":\"Advanced\"}}");
            var explicitResult = request.ResolveSettings(saved, overrides);
            Assert.AreEqual(request.Settings, explicitResult, "explicit settings used");
            Assert.AreEqual(1, overrides.Count, "legacy format override removed");
            Assert.IsTrue(overrides["excluded"].Excluded, "saved exclusion stays enforced");
            Assert.IsFalse(overrides["excluded"].ForceMaxSize, "excluded entry loses unrelated tuning");
        }

        [Test]
        public static void InvalidSettingsNeverSilentlyBecomeDefaults()
        {
            foreach (var settings in new[] { "null", "[]", "{\"mode\":\"Advnced\"}", "{\"mode\":1}",
                "{\"standardLevel\":\"2\"}", "{\"typo\":true}", "{\"global\":{\"desiredReduction\":0}}",
                "{\"global\":{\"minSsim\":2}}", "{\"global\":{\"excluded\":\"true\"}}",
                "{\"global\":{\"maxResolution\":1.5}}", "{\"global\":{\"format\":\"bad\"}}",
                "{\"global\":{\"typo\":true}}", "{\"categories\":{\"Hiar\":{}}}",
                "{\"includedTextures\":[1]}", "{\"includedTextures\":[\"a\",\"a\"]}" })
                Assert.Throws<ArgumentException>(() => AgentRequest.Parse("{\"action\":\"preview\",\"settings\":" + settings + "}"), "reject settings: " + settings);
        }

        [Test]
        public static void MemoryUnknownIsNotReportedAsZero()
        {
            var data = AgentReport.Build(new AvatarAnalysisReport(), null, null, new MemoryNumbers { SourceBytes = 32, CurrentBytes = 16 });
            var memory = (Dictionary<string, object>)data["memory"];
            Assert.IsNull(memory["sourceBytes"], "unknown source total");
            Assert.IsNull(memory["outputBytes"], "unknown output total");
            Assert.IsNull(memory["currentBytes"], "unknown current total");
            Assert.AreEqual(32L, memory["knownSourceBytes"], "retain known subtotal");
            data = AgentReport.Build(new AvatarAnalysisReport(), null, null, new MemoryNumbers { HasSource = true, HasOutput = true, HasCurrent = true });
            memory = (Dictionary<string, object>)data["memory"];
            Assert.AreEqual(0L, memory["sourceBytes"], "known zero survives");
            Assert.AreEqual(0L, memory["outputBytes"], "known output zero survives");
            MiniJson.ParseObject(MiniJson.Serialize(data));
        }

        [Test]
        public static void ReportsIncludeDecisionsOutputsAndBlockers()
        {
            var report = new AvatarAnalysisReport { ScanSucceeded = true };
            report.Textures.Add(new TextureEntry { Guid = "face", Path = "Assets/face.png", Width = 1024, Height = 1024 });
            report.Compatibility.Blockers.Add(new AnalysisFinding("compat.blocked", "tool"));
            var plan = new TextureOptimizationPlan();
            var texture = new TexturePlan { TargetWidth = 512, TargetHeight = 512, TargetFormat = TunerTextureFormat.BC7, TargetBytesKnown = true, TargetBytes = 262144 };
            texture.Notes.Add(new PlanNote("opt.note.review").WithSeverity(PlanNote.Severity.Warning));
            plan.Textures.Add(new PlannedTexture { TextureGuid = "face", Plan = texture });
            var generation = new TextureGenerationResult();
            generation.Generated.Add(new GeneratedTextureAsset { SourceGuid = "face", GeneratedPath = "Assets/AvatarTuner-Generated/face.png" });
            var data = AgentReport.Build(report, plan, generation, new MemoryNumbers());
            Assert.AreEqual(false, data["optimizationAllowed"], "blockers visible");
            var findings = (List<object>)data["findings"];
            Assert.AreEqual("Blocker", ((Dictionary<string, object>)findings[0])["severity"], "blocker severity");
            var row = (Dictionary<string, object>)((List<object>)data["textures"])[0];
            var decision = (Dictionary<string, object>)row["plan"];
            Assert.AreEqual(512, decision["width"], "planned dimensions");
            Assert.AreEqual(262144L, decision["bytes"], "planned memory");
            Assert.AreEqual("BC7", decision["format"], "planned format");
            Assert.AreEqual("Assets/AvatarTuner-Generated/face.png", row["generatedPath"], "output path");
            Assert.AreEqual(1, ((List<object>)decision["notes"]).Count, "quality notes visible");
        }
    }
}
