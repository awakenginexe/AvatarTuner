using System;
using System.Collections.Generic;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Offline tests for the texture plan builder: the protection rules and the plumbing
    /// around Core's CompressionPlanner. The planner's own decisions are covered by
    /// CompressionPlannerTests; these tests prove the builder supplies evidence correctly
    /// and never lets an optimization reach a third-party tool's or animation-driven
    /// system's materials.
    /// </summary>
    public static class PlanBuilderTests
    {
        // ------------------------------------------------------------------ protection

        [Test("tool marker subtrees pin their materials' textures")]
        public static void ToolMarkerSubtreeIsProtected()
        {
            var report = MakeReport();
            report.ToolMarkers.Add(new ToolMarkerEntry { ToolId = "vrcfury", Path = "Arm/Prop" });

            var protectedMaterials = TexturePlanBuilder.ProtectedMaterials(report);
            Assert.AreEqual(1, protectedMaterials.Count, "only the pinned subtree's material is protected");
            Assert.IsTrue(protectedMaterials.ContainsKey("mat-2"), "the material under the marker is protected");
            Assert.AreEqual("Arm/Prop", protectedMaterials["mat-2"], "the owner path is recorded");

            var plan = TexturePlanBuilder.Build(report, SizeFirst(), null, null);
            var pinned = Find(plan, "tex-b");
            var free = Find(plan, "tex-a");

            Assert.IsTrue(pinned.ProtectedByThirdParty, "the subtree texture is protected");
            Assert.AreEqual("Arm/Prop", pinned.ProtectionSourcePath, "the pinning component is identified");
            Assert.IsTrue(pinned.Plan.IsNoOp, "a protected texture is never changed");
            Assert.IsTrue(pinned.Plan.IsExcluded, "a protected texture is excluded");
            Assert.AreEqual("plan.skip.protectedThirdParty", pinned.Plan.SkipReasonKey, "the skip reason explains the pin");
            Assert.IsTrue(HasNote(pinned.Plan, "plan.note.protectedThirdParty"), "the plan carries the pin note");

            Assert.IsFalse(free.ProtectedByThirdParty, "a texture outside every subtree is free");
            Assert.IsFalse(free.Plan.IsExcluded, "a free texture is planned normally");
        }

        [Test("animation-driven systems pin their subtrees, ordinary dynamics do not")]
        public static void AnimationDrivenSystemsAreProtected()
        {
            var report = MakeReport();
            report.DynamicComponents.Add(new DynamicComponentEntry
            {
                TypeFullName = "SomeTool.SPSConfigurer",
                Path = "Arm/Prop",
                KindKey = "finding.kind.animationDrivenSystem"
            });
            report.DynamicComponents.Add(new DynamicComponentEntry
            {
                TypeFullName = "VRC.SDK3.Dynamics.PhysBone.VRCPhysBone",
                Path = "Hair",
                KindKey = "finding.kind.physBone"
            });

            var plan = TexturePlanBuilder.Build(report, SizeFirst(), null, null);
            Assert.IsTrue(Find(plan, "tex-b").ProtectedByThirdParty, "the system's subtree is pinned");
            Assert.IsFalse(Find(plan, "tex-a").ProtectedByThirdParty, "PhysBones alone pin nothing");
        }

        [Test("subtree matching respects the path boundary")]
        public static void SubtreeMatchingRespectsBoundaries()
        {
            Assert.IsTrue(TexturePlanBuilder.IsUnder("Arm/Prop/Mesh", "Arm/Prop"), "children are under the root");
            Assert.IsTrue(TexturePlanBuilder.IsUnder("Arm/Prop", "Arm/Prop"), "the root is under itself");
            Assert.IsFalse(TexturePlanBuilder.IsUnder("Arm/Prop2", "Arm/Prop"), "a sibling with a shared prefix is not");
            Assert.IsFalse(TexturePlanBuilder.IsUnder("Arm", "Arm/Prop"), "a parent is not under its child");
            Assert.IsFalse(TexturePlanBuilder.IsUnder("", "Arm"), "an empty path is under nothing");
            Assert.IsFalse(TexturePlanBuilder.IsUnder("Arm/Prop", ""), "nothing is under an empty root");
            Assert.IsFalse(TexturePlanBuilder.IsUnder("Arm/Prop", null), "nothing is under a null root");
        }

        [Test("protection wins over user overrides")]
        public static void ProtectionWinsOverOverrides()
        {
            var report = MakeReport();
            report.ToolMarkers.Add(new ToolMarkerEntry { ToolId = "vrcfury", Path = "Arm/Prop" });

            var overrides = new Dictionary<string, TextureOverride>(StringComparer.OrdinalIgnoreCase);
            overrides["tex-b"] = new TextureOverride
            {
                ForceFormat = true,
                ForcedFormat = TunerTextureFormat.BC1,
                ForceMaxSize = true,
                ForcedMaxSize = 256
            };

            var plan = TexturePlanBuilder.Build(report, SizeFirst(), overrides, null);
            var pinned = Find(plan, "tex-b");

            Assert.IsTrue(pinned.Plan.IsNoOp, "a forced format is still refused for a pinned texture");
            Assert.AreEqual("plan.skip.protectedThirdParty", pinned.Plan.SkipReasonKey, "the pin reason wins");
            Assert.IsNotNull(pinned.Override, "the user override is still reported for the UI");
            Assert.IsTrue(pinned.Override.ForcedFormat == TunerTextureFormat.BC1, "the override is preserved as given");
        }

        [Test("no markers means no protection")]
        public static void NoMarkersMeansNoProtection()
        {
            var report = MakeReport();
            var plan = TexturePlanBuilder.Build(report, SizeFirst(), null, null);
            Assert.AreEqual(0, plan.ProtectedCount, "nothing is pinned");
            Assert.AreEqual(0, plan.Findings.Count, "no protection finding is emitted");
        }

        // ------------------------------------------------------------------ planning

        [Test("the builder plans every texture and classifies it exactly once")]
        public static void EveryTextureIsPlanned()
        {
            var report = MakeReport();
            report.ToolMarkers.Add(new ToolMarkerEntry { ToolId = "vrcfury", Path = "Arm/Prop" });

            var plan = TexturePlanBuilder.Build(report, SizeFirst(), null, null);
            Assert.AreEqual(2, plan.Textures.Count, "both textures are planned");
            Assert.AreEqual(1, plan.ProtectedCount, "one texture is pinned");
            Assert.AreEqual(1, plan.ChangedCount, "the free texture has real changes under the size preset");

            var classified = plan.ChangedCount + plan.ProtectedCount + plan.ExcludedCount + plan.UnchangedCount;
            Assert.AreEqual(plan.Textures.Count, classified, "every texture is classified exactly once");

            foreach (var planned in plan.Textures)
            {
                Assert.IsNotNull(planned.Plan, "every planned texture carries a plan");
                Assert.AreEqual(planned.HasChanges, !planned.Plan.IsNoOp, "HasChanges agrees with the plan");
            }
        }

        [Test("user exclusions are honored and counted")]
        public static void UserExclusionsAreHonored()
        {
            var report = MakeReport();
            var overrides = new Dictionary<string, TextureOverride>(StringComparer.OrdinalIgnoreCase);
            overrides["tex-a"] = new TextureOverride { Excluded = true };

            var plan = TexturePlanBuilder.Build(report, SizeFirst(), overrides, null);
            var excluded = Find(plan, "tex-a");

            Assert.IsTrue(excluded.Plan.IsExcluded, "the texture is excluded");
            Assert.AreEqual("plan.skip.excluded", excluded.Plan.SkipReasonKey, "the reason is the user exclusion");
            Assert.AreEqual(1, plan.ExcludedCount, "the exclusion is counted");
            Assert.AreEqual(1, plan.ChangedCount, "the other texture is still changed");
        }

        [Test("aggregate savings equal the sum of the per-texture plans")]
        public static void AggregatesMatchPlans()
        {
            var report = MakeReport();
            var plan = TexturePlanBuilder.Build(report, SizeFirst(), null, null);

            long source = 0;
            long target = 0;
            foreach (var planned in plan.Textures)
            {
                source += planned.Plan.SourceBytes;
                target += planned.Plan.TargetBytes;
            }

            Assert.AreEqual(source, plan.SourceBytesTotal, "source total is the sum of its parts");
            Assert.AreEqual(target, plan.TargetBytesTotal, "target total is the sum of its parts");
            Assert.AreEqual(source - target, plan.SavingBytes, "the saving is the difference");
            Assert.IsTrue(plan.SavingBytes >= 0, "optimization never increases memory");
            Assert.AreEqual(plan.HasChanges, plan.ChangedCount > 0, "HasChanges agrees with the count");
        }

        [Test("protected textures get a localized finding")]
        public static void ProtectionProducesAFinding()
        {
            var report = MakeReport();
            report.ToolMarkers.Add(new ToolMarkerEntry { ToolId = "vrcfury", Path = "Arm/Prop" });

            var plan = TexturePlanBuilder.Build(report, SizeFirst(), null, null);
            Assert.AreEqual(1, plan.Findings.Count, "one aggregate finding is produced");
            Assert.AreEqual("opt.protectedTextures", plan.Findings[0].MessageKey, "the finding key");
            Assert.AreEqual("1", plan.Findings[0].Args[0], "the finding counts the pinned textures");
        }

        [Test("planning is deterministic")]
        public static void PlanningIsDeterministic()
        {
            var report = MakeReport();
            report.ToolMarkers.Add(new ToolMarkerEntry { ToolId = "vrcfury", Path = "Arm/Prop" });

            var first = TexturePlanBuilder.Build(report, SizeFirst(), null, null);
            var second = TexturePlanBuilder.Build(report, SizeFirst(), null, null);

            for (var i = 0; i < first.Textures.Count; i++)
            {
                Assert.AreEqual(first.Textures[i].TextureGuid, second.Textures[i].TextureGuid,
                    "textures are planned in the same order");
                Assert.AreEqual(first.Textures[i].Plan.ToHashToken(), second.Textures[i].Plan.ToHashToken(),
                    "the same decisions are reached");
            }
        }

        [Test("invalid arguments are refused")]
        public static void InvalidArgumentsAreRefused()
        {
            Assert.Throws<ArgumentNullException>(
                () => TexturePlanBuilder.Build(null, SizeFirst(), null, null),
                "a missing report is refused");
            Assert.Throws<ArgumentNullException>(
                () => TexturePlanBuilder.Build(new AvatarAnalysisReport(), null, null, null),
                "a missing policy is refused");
        }

        [Test("request building translates scan data faithfully")]
        public static void RequestBuildingTranslatesScanData()
        {
            var entry = new TextureEntry
            {
                Guid = "tex-d",
                Path = "Assets/Tex/d.png",
                Name = "body_skin",
                Width = 2048,
                Height = 1024,
                CurrentFormatName = "DXT1",
                CurrentFormatKnown = true,
                HasAlphaChannel = true,
                AlphaIsTransparency = true,
                IsNormalMap = true,
                MipMapsEnabled = false,
                SrgbTexture = false,
                MaxSizeOverride = 1024,
                Usage = TextureUsageKind.NormalMap
            };
            entry.BoundProperties.Add("_BumpMap");

            var request = TexturePlanBuilder.BuildRequest(entry);

            Assert.AreEqual(2048, request.SourceWidth, "width is copied");
            Assert.AreEqual(1024, request.SourceHeight, "height is copied");
            Assert.AreEqual(TunerTextureFormat.BC1, request.CurrentFormat, "Unity's DXT1 alias maps to BC1");
            Assert.IsTrue(request.CurrentFormatKnown, "a parsed format is known");
            Assert.AreEqual(1024, request.CurrentMaxSize, "the max size override is copied");
            Assert.IsFalse(request.CurrentMipMaps, "the mip setting is copied");
            Assert.IsFalse(request.CurrentSRGB, "the colour space is copied");
            Assert.AreEqual(TextureUsageKind.NormalMap, request.Usage, "the merged usage is kept");
            Assert.IsTrue(request.SourceHasAlpha, "alpha presence is copied");
            Assert.IsTrue(request.AlphaChannelUsed, "alpha is flagged as used");
            Assert.IsTrue(request.ImporterTypeIsNormalMap, "the importer type is copied");
            Assert.AreEqual(1, request.FaceCount, "plain textures have one face");
            Assert.IsTrue(request.IsValid, "the request is valid");
            Assert.IsNotNull(request.Evidence, "name evidence is re-derived for detail preservation");
        }

        // ------------------------------------------------------------------ format mapping

        [Test("every block format the planner can choose is materializable")]
        public static void BlockFormatsAreMaterializable()
        {
            foreach (TunerTextureFormat format in Enum.GetValues(typeof(TunerTextureFormat)))
            {
                if (TextureFormatInfo.IsBlockCompressed(format))
                {
                    Assert.IsTrue(TextureFormatMapping.IsMaterializable(format),
                        format + " can be written to a generated asset");
                }
            }
        }

        [Test("inexpressible formats map to nothing instead of being approximated")]
        public static void InexpressibleFormatsMapToNothing()
        {
            Assert.IsNull(TextureFormatMapping.ImporterFormatName(TunerTextureFormat.Automatic),
                "Automatic has no importer equivalent");
            Assert.IsNull(TextureFormatMapping.ImporterFormatName(TunerTextureFormat.Unknown),
                "Unknown has no importer equivalent");
            Assert.IsFalse(TextureFormatMapping.IsMaterializable(TunerTextureFormat.RGBA16),
                "RGBA16 cannot be expressed and is never approximated");
        }

        [Test("importer format names are the Unity member names")]
        public static void ImporterFormatNamesAreUnityNames()
        {
            Assert.AreEqual("DXT1", TextureFormatMapping.ImporterFormatName(TunerTextureFormat.BC1), "BC1 is DXT1");
            Assert.AreEqual("DXT5", TextureFormatMapping.ImporterFormatName(TunerTextureFormat.BC3), "BC3 is DXT5");
            Assert.AreEqual("BC7", TextureFormatMapping.ImporterFormatName(TunerTextureFormat.BC7), "BC7 is BC7");
            Assert.AreEqual("ETC2_RGB4", TextureFormatMapping.ImporterFormatName(TunerTextureFormat.ETC2_RGB4),
                "Unity 2022.3 uses ETC2_RGB4 in TextureImporterFormat");

            var materializable = TextureFormatMapping.MaterializableFormats();
            Assert.IsGreater(15, materializable.Count, "the table covers the real formats");
            for (var i = 1; i < materializable.Count; i++)
            {
                Assert.IsGreater((int)materializable[i - 1], (int)materializable[i],
                    "the list is in enum order");
            }
        }

        // ------------------------------------------------------------------ helpers

        private static TexturePolicy SizeFirst()
        {
            return PresetCatalog.GetPolicy(OptimizationPreset.SizeFirst);
        }

        private static PlannedTexture Find(TextureOptimizationPlan plan, string guid)
        {
            for (var i = 0; i < plan.Textures.Count; i++)
            {
                if (string.Equals(plan.Textures[i].TextureGuid, guid, StringComparison.OrdinalIgnoreCase))
                {
                    return plan.Textures[i];
                }
            }

            Assert.Fail("texture '" + guid + "' is not in the plan");
            return null;
        }

        private static bool HasNote(TexturePlan plan, string key)
        {
            for (var i = 0; i < plan.Notes.Count; i++)
            {
                if (string.Equals(plan.Notes[i].Key, key, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A two-texture avatar: tex-a on Body (free), tex-b on Arm/Prop (pin-able).
        /// </summary>
        private static AvatarAnalysisReport MakeReport()
        {
            var report = new AvatarAnalysisReport();
            report.AvatarName = "TestAvatar";
            report.AvatarRootGuid = "0123456789abcdef";

            var texA = new TextureEntry
            {
                Guid = "tex-a",
                Path = "Assets/Tex/body_skin.png",
                Name = "body_skin",
                Width = 2048,
                Height = 2048,
                CurrentFormatName = "BC7",
                CurrentFormatKnown = true,
                HasAlphaChannel = false,
                MipMapsEnabled = true,
                SrgbTexture = true,
                MaxSizeOverride = 2048,
                Usage = TextureUsageKind.AlbedoOpaque
            };
            texA.UsedByMaterialGuids.Add("mat-1");

            var texB = new TextureEntry
            {
                Guid = "tex-b",
                Path = "Assets/Tex/prop_metal.png",
                Name = "prop_metal",
                Width = 2048,
                Height = 2048,
                CurrentFormatName = "BC7",
                CurrentFormatKnown = true,
                HasAlphaChannel = false,
                MipMapsEnabled = true,
                SrgbTexture = true,
                MaxSizeOverride = 2048,
                Usage = TextureUsageKind.AlbedoOpaque
            };
            texB.UsedByMaterialGuids.Add("mat-2");

            report.Textures.Add(texA);
            report.Textures.Add(texB);

            var body = new RendererEntry { Path = "Body", IsSkinned = true };
            body.MaterialGuids.Add("mat-1");
            report.Renderers.Add(body);

            var prop = new RendererEntry { Path = "Arm/Prop" };
            prop.MaterialGuids.Add("mat-2");
            report.Renderers.Add(prop);

            return report;
        }
    }
}
