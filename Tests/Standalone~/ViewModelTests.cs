using System;
using System.Collections.Generic;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;
using AvatarTuner.Editor.UI;
using AvatarTuner.Editor.ViewModels;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Offline tests for the redesigned UI's view models: dashboard metrics and insights,
    /// texture row classification (the five status chips), compatibility grouping (the
    /// repetition fix) and the workflow action hierarchy. These decide what the window
    /// shows, so they are pinned here instead of eyeballed in Unity.
    /// </summary>
    public static class ViewModelTests
    {
        // ------------------------------------------------------------------ dashboard

        [Test("dashboard metrics carry the honest status and only real counts")]
        public static void DashboardMetricsAreRealAndOrdered()
        {
            var report = MakeReport();

            var metrics = DashboardModel.BuildMetrics(report, AvatarDisplayStatus.ChangesDetected);

            Assert.AreEqual(7, metrics.Count, "status plus six scan counts are shown");
            Assert.AreEqual("ui.label.status", metrics[0].LabelKey, "status is the hero metric");
            Assert.AreEqual(MetricEmphasis.Primary, metrics[0].Emphasis, "status is a hero metric");
            Assert.AreEqual("status.changesDetected.name", metrics[0].ValueKey, "status resolves a key");
            Assert.AreEqual("status.changesDetected.summary", metrics[0].SubTextKey,
                "the status explains itself");
            Assert.AreEqual(SemanticTone.Warning, metrics[0].Tone, "stale status reads as warning");

            Assert.AreEqual("2", metrics[1].Value, "texture count comes from the report");
            Assert.AreEqual("1", metrics[2].Value, "material count comes from the report");
            Assert.AreEqual("2", metrics[3].Value, "renderer count comes from the report");
            Assert.AreEqual("3000", metrics[4].Value, "triangle count comes from the report");
            Assert.AreEqual("7", metrics[5].Value, "blend shape count comes from the report");
            Assert.AreEqual("1", metrics[6].Value, "phys bone count comes from the report");
        }

        [Test("dashboard status wording never claims more than exists")]
        public static void DashboardStatusIsHonest()
        {
            // Ready to optimize is not "Optimized"; generating output is "Optimized Output
            // Generated"; nothing here says "Applied to Avatar" because nothing is applied.
            var metrics = DashboardModel.BuildMetrics(MakeReport(), AvatarDisplayStatus.ReadyToOptimize);
            Assert.AreEqual("status.readyToOptimize.name", metrics[0].ValueKey,
                "a pending plan reads as ready to optimize");
            Assert.AreEqual(SemanticTone.Warning, metrics[0].Tone, "and asks for attention");

            metrics = DashboardModel.BuildMetrics(MakeReport(), AvatarDisplayStatus.OutputGenerated);
            Assert.AreEqual("status.optimized.name", metrics[0].ValueKey,
                "generated output reads as generated output");
            Assert.AreEqual("status.appliedToAvatar.name",
                StatusModel.NameKey(AvatarDisplayStatus.AppliedToAvatar),
                "the applied status exists with its own honest wording");

            Assert.AreEqual(0, DashboardModel.BuildMetrics(null, AvatarDisplayStatus.NotOptimized).Count,
                "no report means no metrics at all");
        }

        [Test("insights report savings, risks and compatibility in severity order")]
        public static void InsightsAreActionable()
        {
            var report = MakeReport();
            var plan = MakePlan(out var protectedPlanned);
            plan.SavingBytes = 5L * 1024L * 1024L;
            plan.SavingPercent = 31.5;

            var compat = new CompatibilityReport();
            compat.Blockers.Add(new AnalysisFinding("compat.conflictMultipleOptimizers"));

            var insights = DashboardModel.BuildInsights(report, plan, compat);

            Assert.AreEqual("compat.conflictMultipleOptimizers", insights[0].MessageKey,
                "blockers come first");
            Assert.AreEqual(SemanticTone.Error, insights[0].Tone, "blockers read as errors");

            var foundSaving = false;
            var foundLarge = false;
            var foundProtected = false;
            for (var i = 0; i < insights.Count; i++)
            {
                if (insights[i].MessageKey == "ui.insight.potentialSaving")
                {
                    foundSaving = true;
                    Assert.AreEqual("5 MiB", insights[i].Args[0], "the saving is formatted in MiB");
                    Assert.AreEqual("31.5%", insights[i].Args[1], "the percent is exact");
                }

                if (insights[i].MessageKey == "analysis.largeTextures")
                {
                    foundLarge = true;
                    Assert.AreEqual("1", insights[i].Args[0], "one texture exceeds the threshold");
                }

                if (insights[i].MessageKey == "opt.protectedTextures")
                {
                    foundProtected = true;
                }
            }

            Assert.IsTrue(foundSaving, "a real saving becomes an insight");
            Assert.IsTrue(foundLarge, "large textures become an insight");
            Assert.IsTrue(foundProtected, "protected textures become an insight");
            Assert.IsNotNull(protectedPlanned, "the fixture contains the protected texture");
        }

        [Test("a calm avatar reports a calm insight instead of noise")]
        public static void CalmInsightsAreCalm()
        {
            var report = MakeReport();
            report.Textures.Clear();
            report.PossibleBuildTimeDependencies.Clear();

            var insights = DashboardModel.BuildInsights(report, null, null);
            Assert.AreEqual(1, insights.Count, "exactly one line when there is nothing to do");
            Assert.AreEqual("ui.insight.clear", insights[0].MessageKey, "and it says so");
            Assert.AreEqual(SemanticTone.Success, insights[0].Tone, "in a calm tone");
        }

        // ------------------------------------------------------------------ texture rows

        [Test("texture rows classify exactly into the five documented states")]
        public static void TextureRowsClassifyHonestStates()
        {
            var plan = MakePlan(out _);

            var rows = TextureRows.BuildAll(MakeReport(), plan);
            Assert.AreEqual(2, rows.Count, "one row per texture");

            var free = FindRow(rows, "tex-a");
            Assert.AreEqual(TextureRowStatus.OptimizationAvailable, free.Status,
                "a changed, unwarned texture is optimizable");
            Assert.AreEqual("ui.textureStatus.available", TextureRowModel.StatusLabelKey(free.Status),
                "and has the matching chip key");

            var pinned = FindRow(rows, "tex-b");
            Assert.AreEqual(TextureRowStatus.RequiresManualReview, pinned.Status,
                "third-party protected textures need review");
            Assert.IsTrue(pinned.IsProtected, "the row knows why");

            // user exclusion
            var excludedPlan = new TextureOptimizationPlan();
            var excludedPlanned = new PlannedTexture
            {
                TextureGuid = "tex-a",
                TextureName = "a",
                Plan = NoOpPlan("plan.skip.excluded")
            };
            excludedPlanned.Plan.IsExcluded = true;
            excludedPlan.Textures.Add(excludedPlanned);
            var excludedRow = TextureRows.Build(MakeReport().Textures[0], excludedPlanned);
            Assert.AreEqual(TextureRowStatus.Excluded, excludedRow.Status, "user exclusions are their own state");

            // already optimal
            var optimalRow = TextureRows.Build(MakeReport().Textures[0],
                new PlannedTexture { TextureGuid = "tex-a", Plan = NoOpPlan("plan.skip.alreadyOptimal") });
            Assert.AreEqual(TextureRowStatus.AlreadyOptimized, optimalRow.Status, "nothing to do reads as done");

            // invalid request
            var invalidRow = TextureRows.Build(MakeReport().Textures[0],
                new PlannedTexture { TextureGuid = "tex-a", Plan = NoOpPlan("plan.skip.invalidRequest") });
            Assert.AreEqual(TextureRowStatus.Unsupported, invalidRow.Status, "refused requests read as unsupported");

            // no plan at all
            var missingRow = TextureRows.Build(MakeReport().Textures[0], null);
            Assert.AreEqual(TextureRowStatus.Unsupported, missingRow.Status, "no plan, no claim");
        }

        [Test("quality risk comes from the planner's own evidence")]
        public static void QualityRiskIsDerivedNotInvented()
        {
            var warned = ChangedPlan();
            warned.AddWarning("plan.warning.alphaLost");
            var warnedRow = TextureRows.Build(MakeReport().Textures[0],
                new PlannedTexture { TextureGuid = "tex-a", Plan = warned });
            Assert.AreEqual(CompatRisk.High, warnedRow.Risk, "a planner warning is high risk");
            Assert.AreEqual(TextureRowStatus.RequiresManualReview, warnedRow.Status,
                "and the row asks for review");

            var unmeasured = ChangedPlan();
            unmeasured.Confidence = QualityConfidence.Unknown;
            var unmeasuredRow = TextureRows.Build(MakeReport().Textures[0],
                new PlannedTexture { TextureGuid = "tex-a", Plan = unmeasured });
            Assert.AreEqual(CompatRisk.Medium, unmeasuredRow.Risk,
                "a format change without quality evidence is medium risk");

            var evidence = ChangedPlan();
            evidence.Confidence = QualityConfidence.Medium;
            var evidenceRow = TextureRows.Build(MakeReport().Textures[0],
                new PlannedTexture { TextureGuid = "tex-a", Plan = evidence });
            Assert.AreEqual(CompatRisk.Low, evidenceRow.Risk, "with evidence the risk is low");
        }

        [Test("filtering and sorting are deterministic and complete")]
        public static void FilterAndSortAreDeterministic()
        {
            var rows = new List<TextureRowModel>
            {
                Row("b_tex", "Assets/Tex/b_tex.png", 4096, 1024),
                Row("a_tex", "Assets/Tex/a_tex.png", 1024, 512),
                Row("c_tex", "Assets/Other/c_tex.png", 2048, 0)
            };

            var filtered = TextureRows.Filter(rows, "other");
            Assert.AreEqual(1, filtered.Count, "filtering matches paths");
            Assert.AreEqual("c_tex", filtered[0].Name, "and finds the right row");

            filtered = TextureRows.Filter(rows, "A_TEX");
            Assert.AreEqual(1, filtered.Count, "filtering matches names case insensitively");

            TextureRows.Sort(rows, TextureSortMode.Name);
            Assert.AreEqual("a_tex", rows[0].Name, "name sort is alphabetical");
            Assert.AreEqual("c_tex", rows[2].Name, "name sort is complete");

            TextureRows.Sort(rows, TextureSortMode.Vram);
            Assert.AreEqual("b_tex", rows[0].Name, "vram sort is largest first");

            TextureRows.Sort(rows, TextureSortMode.Savings);
            Assert.AreEqual("b_tex", rows[0].Name, "savings sort is biggest saving first");
        }

        [Test("the review counter matches the row classifier")]
        public static void ReviewCounterMatchesClassifier()
        {
            var plan = MakePlan(out _);
            Assert.AreEqual(1, TextureRows.CountNeedingReview(plan),
                "exactly the protected texture needs review");
            Assert.AreEqual(0, TextureRows.CountNeedingReview(null), "no plan, no review work");
        }

        // ------------------------------------------------------------------ compatibility grouping

        [Test("tools are grouped once no matter how many components were seen")]
        public static void ToolsAreGroupedOnce()
        {
            var report = new CompatibilityReport();
            report.DetectedTools.Add(Marker("modular-avatar", "Modular Avatar", "Root/Outfit1"));
            report.DetectedTools.Add(Marker("modular-avatar", "Modular Avatar", "Root/Outfit2"));
            report.DetectedTools.Add(Marker("modular-avatar", "Modular Avatar", "Root/Armature"));
            report.Notices.Add(new AnalysisFinding("compat.modularAvatarPresent"));
            report.Notices.Add(new AnalysisFinding("compat.modularAvatarPresent"));

            var model = CompatibilityGrouper.Build(report);

            Assert.AreEqual(1, model.Tools.Count, "one group for one tool");
            Assert.AreEqual(3, model.Tools[0].ComponentCount, "all components counted");
            Assert.AreEqual(3, model.Tools[0].Markers.Count, "all paths kept");
            Assert.AreEqual("Root/Armature", model.Tools[0].Markers[0].Path, "markers sort by path");
            Assert.AreEqual(0, model.Systems.Count, "no system notices");
            Assert.AreEqual(0, model.GeneralWarnings.Count,
                "the tool's explanation is not repeated outside its group");
            Assert.AreEqual(3, model.TotalComponents, "the total counts every component");
        }

        [Test("a project-only tool gets one presence card instead of loose messages")]
        public static void ProjectOnlyToolGetsOneCard()
        {
            var report = new CompatibilityReport();
            report.Notices.Add(new AnalysisFinding("compat.ndmfPresent"));
            report.Notices.Add(new AnalysisFinding("compat.ndmfPresent"));

            var model = CompatibilityGrouper.Build(report);
            Assert.AreEqual(1, model.Tools.Count, "NDMF gets exactly one card");
            Assert.AreEqual(0, model.Tools[0].ComponentCount, "with no components claimed");
            Assert.AreEqual(0, model.GeneralWarnings.Count, "and no loose duplicates");
        }

        [Test("systems, conflicts and warnings land in the right groups")]
        public static void FindingsRouteToGroups()
        {
            var report = new CompatibilityReport();
            report.DetectedTools.Add(Marker("avatar-optimizer", "Avatar Optimizer (AAO)", "Root"));
            report.Warnings.Add(new AnalysisFinding("compat.conflictAvatarOptimizer"));
            report.Warnings.Add(new AnalysisFinding("compat.conflictD4rk"));
            report.Blockers.Add(new AnalysisFinding("compat.conflictMultipleOptimizers"));
            report.Notices.Add(new AnalysisFinding("compat.animatorDrivenSystem", "SPSConfigurer"));
            report.Notices.Add(new AnalysisFinding("compat.animatorDrivenSystem", "SPSConfigurer"));
            report.Notices.Add(new AnalysisFinding("compat.animatorDrivenSystem", "Penetrator"));

            var model = CompatibilityGrouper.Build(report);

            Assert.AreEqual(1, model.Tools.Count, "AAO has its group");
            Assert.AreEqual(1, model.Tools[0].Warnings.Count,
                "the AAO warning attaches to the AAO group");
            Assert.AreEqual(1, model.GeneralWarnings.Count,
                "the d4rk warning (no group) is general");
            Assert.AreEqual(2, model.Systems.Count, "system notices dedupe by system");
            Assert.AreEqual("SPSConfigurer", model.Systems[0].Args[0], "first seen order is kept");
            Assert.AreEqual(1, model.Blockers.Count, "blockers are their own group");
            Assert.IsTrue(model.HasAnything, "the model knows it has content");
        }

        [Test("grouping preserves registry order and risk escalation")]
        public static void GroupingIsOrderedAndRiskAware()
        {
            var report = new CompatibilityReport();
            report.DetectedTools.Add(Marker("avatar-optimizer", "Avatar Optimizer (AAO)", "Root"));
            report.DetectedTools.Add(Marker("vrcfury", "VRCFury", "Root/Outfit"));

            var model = CompatibilityGrouper.Build(report);
            Assert.AreEqual("vrcfury", model.Tools[0].ToolId, "registry order wins over discovery order");
            Assert.AreEqual("avatar-optimizer", model.Tools[1].ToolId, "and is stable");

            var highRisk = Marker("vrcfury", "VRCFury", "Root/Outfit");
            highRisk.Risk = CompatRisk.High;
            var escalated = new CompatibilityReport();
            escalated.DetectedTools.Add(highRisk);
            var escalatedModel = CompatibilityGrouper.Build(escalated);
            Assert.AreEqual(CompatRisk.High, escalatedModel.Tools[0].Risk,
                "an observed higher risk escalates the group");
        }

        // ------------------------------------------------------------------ workflow

        [Test("the workflow offers exactly one sensible next action")]
        public static void WorkflowNextActionIsSingular()
        {
            Assert.AreEqual(PrimaryAction.SelectAvatar,
                WorkflowModel.NextAction(WindowState.NoSelection, false, false, false, false),
                "nothing is selected first");
            Assert.AreEqual(PrimaryAction.Analyze,
                WorkflowModel.NextAction(WindowState.Ready, true, false, false, false),
                "then analyze");
            Assert.AreEqual(PrimaryAction.ConfigureOptimization,
                WorkflowModel.NextAction(WindowState.Analyzed, true, true, true, false),
                "then configure the optimization (Optimize opens the dialog, it never runs)");
            Assert.AreEqual(PrimaryAction.ReviewChanges,
                WorkflowModel.NextAction(WindowState.Analyzed, true, true, false, false),
                "an optimal avatar ends at review, not at a dead button");
            Assert.AreEqual(PrimaryAction.ViewResults,
                WorkflowModel.NextAction(WindowState.Done, true, true, true, true),
                "results come last");
            Assert.AreEqual(PrimaryAction.Analyze,
                WorkflowModel.NextAction(WindowState.Analyzing, true, true, true, true),
                "while busy the running action stays primary");
            Assert.AreEqual(PrimaryAction.ConfigureOptimization,
                WorkflowModel.NextAction(WindowState.Optimizing, true, true, true, true),
                "optimizing reports under the configure/confirm step");
        }

        [Test("disabled actions always explain themselves")]
        public static void DisabledActionsExplainThemselves()
        {
            Assert.AreEqual("ui.hint.busy",
                WorkflowModel.OptimizeDisabledReasonKey(WindowState.Optimizing, true, true, true, true),
                "busy wins");
            Assert.AreEqual("ui.hint.selectAvatar",
                WorkflowModel.OptimizeDisabledReasonKey(WindowState.Ready, false, false, false, true),
                "no avatar is next");
            Assert.AreEqual("ui.hint.analyzeFirst",
                WorkflowModel.OptimizeDisabledReasonKey(WindowState.Ready, true, false, false, true),
                "not analyzed is next");
            Assert.AreEqual("ui.hint.blocked",
                WorkflowModel.OptimizeDisabledReasonKey(WindowState.Analyzed, true, true, true, false),
                "compatibility blockers are named");
            Assert.AreEqual("ui.hint.noChanges",
                WorkflowModel.OptimizeDisabledReasonKey(WindowState.Analyzed, true, true, false, true),
                "nothing to do is named");
            Assert.IsNull(
                WorkflowModel.OptimizeDisabledReasonKey(WindowState.Analyzed, true, true, true, true),
                "an available action has no excuse to show");
            Assert.AreEqual("ui.hint.selectAvatar",
                WorkflowModel.AnalyzeDisabledReasonKey(WindowState.Ready, false),
                "analyze explains itself too");
        }

        [Test("the breadcrumb steps follow the workflow order")]
        public static void BreadcrumbStepsFollowWorkflow()
        {
            Assert.AreEqual(6, WorkflowModel.StepLabelKeys.Length, "six steps");
            Assert.AreEqual(0, WorkflowModel.ActiveStep(WindowState.NoSelection, false, false, false), "select");
            Assert.AreEqual(1, WorkflowModel.ActiveStep(WindowState.Ready, true, false, false), "analyze");
            Assert.AreEqual(2, WorkflowModel.ActiveStep(WindowState.Analyzed, true, false, false), "configure");
            Assert.AreEqual(3, WorkflowModel.ActiveStep(WindowState.Analyzed, true, false, true),
                "the open dialog reviews the planned changes");
            Assert.AreEqual(4, WorkflowModel.ActiveStep(WindowState.Optimizing, true, false, false), "confirm");
            Assert.AreEqual(5, WorkflowModel.ActiveStep(WindowState.Done, true, true, false), "results");
            Assert.AreEqual(3, WorkflowModel.ActiveStep(WindowState.Done, true, true, true),
                "an open dialog reviews planned changes whatever is behind it");

            foreach (var key in WorkflowModel.StepLabelKeys)
            {
                Assert.IsTrue(TranslationValidator.LooksLikeTranslationKey(key), key + " is a key");
            }
        }

        // ------------------------------------------------------------------ helpers

        private static TextureRowModel Row(string name, string path, long sourceBytes, long savingBytes)
        {
            var row = new TextureRowModel();
            row.Name = name;
            row.Path = path;
            row.SourceBytes = sourceBytes;
            row.SavingBytes = savingBytes;
            return row;
        }

        private static TextureRowModel FindRow(List<TextureRowModel> rows, string guid)
        {
            foreach (var row in rows)
            {
                if (row.Guid == guid)
                {
                    return row;
                }
            }

            Assert.Fail("row for '" + guid + "' not found");
            return null;
        }

        private static TexturePlan NoOpPlan(string skipKey)
        {
            var plan = new TexturePlan();
            plan.IsNoOp = true;
            plan.SkipReasonKey = skipKey;
            return plan;
        }

        private static TexturePlan ChangedPlan()
        {
            var plan = new TexturePlan();
            plan.IsNoOp = false;
            plan.FormatChanged = true;
            plan.SourceFormat = TunerTextureFormat.BC7;
            plan.TargetFormat = TunerTextureFormat.BC1;
            plan.Confidence = QualityConfidence.Low;
            return plan;
        }

        private static ToolMarkerEntry Marker(string toolId, string displayName, string path)
        {
            return new ToolMarkerEntry
            {
                ToolId = toolId,
                DisplayName = displayName,
                Path = path,
                ComponentType = "Some.Type"
            };
        }

        private static AvatarAnalysisReport MakeReport()
        {
            var report = new AvatarAnalysisReport();
            report.AvatarName = "TestAvatar";
            report.ScanSucceeded = true;

            var texA = new TextureEntry { Guid = "tex-a", Path = "Assets/Tex/a.png", Name = "body_skin" };
            texA.UsedByMaterialGuids.Add("mat-1");
            var texB = new TextureEntry { Guid = "tex-b", Path = "Assets/Tex/b.png", Name = "prop_metal" };
            texB.UsedByMaterialGuids.Add("mat-2");
            report.Textures.Add(texA);
            report.Textures.Add(texB);

            report.Materials.Add(new MaterialEntry { Guid = "mat-1" });

            var body = new RendererEntry { Path = "Body", IsSkinned = true };
            body.MaterialGuids.Add("mat-1");
            body.TriangleCount = 1800;
            body.BlendShapeCount = 5;
            body.BoneCount = 30;
            report.Renderers.Add(body);

            var prop = new RendererEntry { Path = "Arm/Prop" };
            prop.MaterialGuids.Add("mat-2");
            prop.TriangleCount = 1200;
            prop.BlendShapeCount = 2;
            report.Renderers.Add(prop);

            report.DynamicComponents.Add(new DynamicComponentEntry
            {
                TypeFullName = "VRC.SDK3.Dynamics.PhysBone.VRCPhysBone",
                KindKey = "finding.kind.physBone",
                Path = "Hair"
            });

            report.PossibleBuildTimeDependencies.Add("guid-1");
            return report;
        }

        private static TextureOptimizationPlan MakePlan(out PlannedTexture protectedPlanned)
        {
            var plan = new TextureOptimizationPlan();

            var freePlan = ChangedPlan();
            freePlan.SourceBytes = 8L * 1024L * 1024L;
            freePlan.TargetBytes = 3L * 1024L * 1024L;
            freePlan.SavingBytes = 5L * 1024L * 1024L;
            freePlan.SavingPercent = 62.5;
            var free = new PlannedTexture
            {
                TextureGuid = "tex-a",
                TextureName = "body_skin",
                TexturePath = "Assets/Tex/a.png",
                Plan = freePlan
            };

            var pinnedPlan = new TexturePlan();
            pinnedPlan.IsNoOp = true;
            pinnedPlan.IsExcluded = true;
            pinnedPlan.SkipReasonKey = "plan.skip.protectedThirdParty";
            pinnedPlan.SourceBytes = 2L * 1024L * 1024L;
            pinnedPlan.TargetBytes = 2L * 1024L * 1024L;
            protectedPlanned = new PlannedTexture
            {
                TextureGuid = "tex-b",
                TextureName = "prop_metal",
                TexturePath = "Assets/Tex/b.png",
                Plan = pinnedPlan,
                ProtectedByThirdParty = true
            };

            plan.Textures.Add(free);
            plan.Textures.Add(protectedPlanned);
            plan.SourceBytesTotal = 10L * 1024L * 1024L;
            plan.TargetBytesTotal = 5L * 1024L * 1024L;
            plan.SavingBytes = 5L * 1024L * 1024L;
            plan.SavingPercent = 50.0;
            plan.ChangedCount = 1;
            plan.ProtectedCount = 1;
            return plan;
        }
    }
}
