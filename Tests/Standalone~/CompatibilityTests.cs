using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AvatarTuner.Core;

namespace AvatarTuner.StandaloneTests
{
    /// <summary>
    /// Offline tests for the third-party tool compatibility policy.
    ///
    /// The policy is the part of the Compatibility Scanner that decides what detections
    /// mean. Keeping it pure means these tests cover every product decision (what warns,
    /// what blocks, what is only reported) without a Unity installation.
    /// </summary>
    public static class CompatibilityTests
    {
        // ------------------------------------------------------------------ registry

        [Test("tool registry is well formed and its ids are stable")]
        public static void RegistryIsWellFormed()
        {
            // Registry order is match priority ("d4rkAvatarOptimizer" contains the substring
            // "AvatarOptimizer"), so the order itself is a tested contract.
            var expectedOrder = new[]
            {
                "vrcfury", "modular-avatar", "ndmf", "d4rk-avatar-optimizer", "avatar-optimizer"
            };

            Assert.AreEqual(expectedOrder.Length, CompatibilityPolicy.KnownTools.Length,
                "the registry still contains exactly the known tools");

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < CompatibilityPolicy.KnownTools.Length; i++)
            {
                var tool = CompatibilityPolicy.KnownTools[i];
                Assert.AreEqual(expectedOrder[i], tool.ToolId, "registry order at index " + i);
                Assert.IsTrue(seenIds.Add(tool.ToolId), "tool id '" + tool.ToolId + "' is unique");
                Assert.IsTrue(!string.IsNullOrEmpty(tool.DisplayName), tool.ToolId + " has a display name");
                Assert.IsTrue(TranslationValidator.LooksLikeTranslationKey(tool.MessageKey),
                    tool.ToolId + " message key '" + tool.MessageKey + "' is a translation key");

                var fragmentCount = tool.ComponentTypeFragments.Length +
                                    tool.AssemblyNameFragments.Length +
                                    tool.PackageIdFragments.Length;
                Assert.IsGreater(0, fragmentCount, tool.ToolId + " has at least one detection fragment");

                foreach (var fragment in tool.ComponentTypeFragments)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(fragment), tool.ToolId + " component fragments are not empty");
                }

                foreach (var fragment in tool.AssemblyNameFragments)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(fragment), tool.ToolId + " assembly fragments are not empty");
                }

                foreach (var fragment in tool.PackageIdFragments)
                {
                    Assert.IsFalse(string.IsNullOrEmpty(fragment), tool.ToolId + " package fragments are not empty");
                }
            }
        }

        [Test("every policy message key exists in all three languages")]
        public static void PolicyMessageKeysAreLocalized()
        {
            var keys = new List<string>
            {
                "compat.conflictMultipleOptimizers",
                "compat.animatorDrivenSystem"
            };
            for (var i = 0; i < CompatibilityPolicy.KnownTools.Length; i++)
            {
                keys.Add(CompatibilityPolicy.KnownTools[i].MessageKey);
            }

            foreach (var code in new[] { "en", "th", "zh-Hans" })
            {
                var catalog = LoadCatalog(code);
                foreach (var key in keys)
                {
                    Assert.IsTrue(catalog.Contains(key),
                        code + " translates the policy key '" + key + "'");
                }
            }
        }

        // ------------------------------------------------------------------ detection

        [Test("component type heuristics recognize known tools")]
        public static void ComponentMatching()
        {
            AssertTool("vrcfury", CompatibilityPolicy.MatchComponentType("VRCFury.VRCFuryPlug"));
            AssertTool("modular-avatar", CompatibilityPolicy.MatchComponentType(
                "nadena.dev.modular_avatar.core.ModularAvatarMergeArmature"));
            AssertTool("ndmf", CompatibilityPolicy.MatchComponentType("nadena.dev.ndmf.NDMFPlugin"));
            AssertTool("avatar-optimizer", CompatibilityPolicy.MatchComponentType(
                "Anatawa12.AvatarOptimizer.MergeSkinnedMesh"));

            // Regression: "d4rkAvatarOptimizer" contains "AvatarOptimizer" as a substring and
            // must never resolve to Avatar Optimizer.
            AssertTool("d4rk-avatar-optimizer", CompatibilityPolicy.MatchComponentType("d4rkAvatarOptimizer"));

            Assert.IsNull(CompatibilityPolicy.MatchComponentType("SkinnedMeshRenderer"),
                "Unity components match nothing");
            Assert.IsNull(CompatibilityPolicy.MatchComponentType("SomeUnrelated.Optimizer"),
                "unknown optimizer-like types match nothing");
            Assert.IsNull(CompatibilityPolicy.MatchComponentType(null), "null input is safe");
            Assert.IsNull(CompatibilityPolicy.MatchComponentType(""), "empty input is safe");
        }

        [Test("assembly and package names are recognized")]
        public static void AssemblyAndPackageMatching()
        {
            AssertTool("vrcfury", CompatibilityPolicy.MatchAssemblyName("VRCFury-Editor"));
            AssertTool("avatar-optimizer", CompatibilityPolicy.MatchAssemblyName("AvatarOptimizer"));
            AssertTool("d4rk-avatar-optimizer", CompatibilityPolicy.MatchAssemblyName("d4rkAvatarOptimizer"));
            AssertTool("modular-avatar", CompatibilityPolicy.MatchAssemblyName("ModularAvatar"));
            AssertTool("ndmf", CompatibilityPolicy.MatchAssemblyName("nadena.dev.ndmf"));

            Assert.IsNull(CompatibilityPolicy.MatchAssemblyName("Assembly-CSharp"), "project assemblies match nothing");
            Assert.IsNull(CompatibilityPolicy.MatchAssemblyName(null), "null assembly input is safe");

            AssertTool("vrcfury", CompatibilityPolicy.MatchPackageId("com.vrcfury.vrcfury"));
            AssertTool("avatar-optimizer", CompatibilityPolicy.MatchPackageId("com.anatawa12.avatar-optimizer"));
            AssertTool("d4rk-avatar-optimizer", CompatibilityPolicy.MatchPackageId("com.d4rkavataroptimizer"));
            AssertTool("modular-avatar", CompatibilityPolicy.MatchPackageId("nadena.dev.modular-avatar"));
            AssertTool("ndmf", CompatibilityPolicy.MatchPackageId("nadena.dev.ndmf"));

            Assert.IsNull(CompatibilityPolicy.MatchPackageId("com.unity.collab-proxy"), "Unity packages match nothing");
            Assert.IsNull(CompatibilityPolicy.MatchPackageId(null), "null package input is safe");
        }

        [Test("matching is case insensitive")]
        public static void MatchingIsCaseInsensitive()
        {
            AssertTool("vrcfury", CompatibilityPolicy.MatchComponentType("vrcfury"));
            AssertTool("avatar-optimizer", CompatibilityPolicy.MatchAssemblyName("avataroptimizer"));
            AssertTool("ndmf", CompatibilityPolicy.MatchPackageId("NADENA.DEV.NDMF"));
        }

        // ------------------------------------------------------------------ evaluation

        [Test("no detections produce an empty, permissive verdict")]
        public static void EmptyEvaluation()
        {
            var assessment = CompatibilityPolicy.Evaluate(null, null, null);

            Assert.AreEqual(0, assessment.Findings.Count, "no findings without evidence");
            Assert.AreEqual(0, assessment.DetectedTools.Count, "no tools without evidence");
            Assert.AreEqual(0, assessment.AnimationDrivenSystems.Count, "no systems without evidence");
            Assert.IsTrue(assessment.IsOptimizationAllowed, "nothing detected means nothing blocks");
            Assert.IsFalse(assessment.NeedsManualInspection, "nothing detected means no manual inspection");
            Assert.IsFalse(assessment.HasDetections, "the verdict knows it is empty");
        }

        [Test("unknown tool ids are ignored")]
        public static void UnknownIdsIgnored()
        {
            var assessment = CompatibilityPolicy.Evaluate(
                new[] { "some-future-tool" }, new[] { "another-tool" }, null);

            Assert.AreEqual(0, assessment.Findings.Count, "unknown ids produce no findings");
            Assert.AreEqual(0, assessment.DetectedTools.Count, "unknown ids produce no tool rows");
            Assert.IsTrue(assessment.IsOptimizationAllowed, "unknown ids do not block");
        }

        [Test("a tool seen on the avatar and in the project is reported once")]
        public static void DetectionDeduplicatesAcrossSources()
        {
            var assessment = CompatibilityPolicy.Evaluate(
                new[] { "vrcfury" }, new[] { "vrcfury", "VRCFURY" }, null);

            Assert.AreEqual(1, assessment.Findings.Count, "one finding for one tool");
            Assert.AreEqual(1, assessment.DetectedTools.Count, "one tool row for one tool");
            Assert.AreEqual("vrcfury", assessment.DetectedTools[0].ToolId, "the right tool is reported");
        }

        [Test("informational tools are reported without warnings")]
        public static void InformationalToolsStayInformational()
        {
            var assessment = CompatibilityPolicy.Evaluate(
                new[] { "vrcfury", "modular-avatar" }, new[] { "ndmf" }, null);

            Assert.AreEqual(3, assessment.DetectedTools.Count, "all three tools are detected");
            Assert.AreEqual(3, assessment.Findings.Count, "each tool yields one finding");
            Assert.AreEqual(0, assessment.Warnings.Count, "read-only tools never warn");
            Assert.AreEqual(0, assessment.Blockers.Count, "read-only tools never block");
            Assert.IsTrue(assessment.IsOptimizationAllowed, "read-only tools allow optimization");
            Assert.IsFalse(assessment.NeedsManualInspection, "read-only tools need no manual inspection");

            var keys = FindingKeys(assessment);
            Assert.AreEqual("compat.vrcFuryPresent,compat.modularAvatarPresent,compat.ndmfPresent",
                string.Join(",", keys.ToArray()), "findings follow registry order");
        }

        [Test("a single mesh optimizer warns but still allows optimization")]
        public static void SingleOptimizerWarns()
        {
            var aao = CompatibilityPolicy.Evaluate(new[] { "avatar-optimizer" }, null, null);
            Assert.AreEqual(1, aao.Warnings.Count, "AAO presence warns");
            Assert.AreEqual(0, aao.Blockers.Count, "AAO alone does not block");
            Assert.IsTrue(aao.IsOptimizationAllowed, "AAO alone still allows optimization");
            Assert.IsTrue(aao.NeedsManualInspection, "AAO presence demands manual inspection");
            Assert.AreEqual(CompatRisk.Medium, aao.Warnings[0].Risk, "AAO is a medium risk");
            Assert.AreEqual("compat.conflictAvatarOptimizer", aao.Warnings[0].MessageKey, "the AAO warning key");

            var d4rk = CompatibilityPolicy.Evaluate(new[] { "d4rk-avatar-optimizer" }, null, null);
            Assert.AreEqual(1, d4rk.Warnings.Count, "d4rk presence warns");
            Assert.AreEqual(0, d4rk.Blockers.Count, "d4rk alone does not block");
            Assert.IsTrue(d4rk.IsOptimizationAllowed, "d4rk alone still allows optimization");
            Assert.AreEqual(CompatRisk.High, d4rk.Warnings[0].Risk, "d4rk is a high risk");
            Assert.AreEqual("compat.conflictD4rk", d4rk.Warnings[0].MessageKey, "the d4rk warning key");
        }

        [Test("two optimizers on the same avatar make it read-only")]
        public static void TwoOptimizersBlock()
        {
            var assessment = CompatibilityPolicy.Evaluate(
                new[] { "avatar-optimizer", "d4rk-avatar-optimizer" }, null, null);

            Assert.AreEqual(2, assessment.Warnings.Count, "each optimizer still contributes its warning");
            Assert.AreEqual(1, assessment.Blockers.Count, "the combination adds one blocker");
            Assert.IsFalse(assessment.IsOptimizationAllowed, "the avatar is read-only");
            Assert.IsTrue(assessment.NeedsManualInspection, "a blocker demands manual inspection");

            var blocker = assessment.Blockers[0];
            Assert.AreEqual("compat.conflictMultipleOptimizers", blocker.MessageKey, "the blocker key");
            Assert.AreEqual(CompatSeverity.Blocker, blocker.Severity, "the finding is a blocker");
            Assert.AreEqual(CompatRisk.High, blocker.Risk, "the combination is high risk");
        }

        [Test("an optimizer merely installed in the project does not trigger the blocker")]
        public static void ProjectOnlyOptimizerDoesNotBlock()
        {
            var assessment = CompatibilityPolicy.Evaluate(
                new[] { "avatar-optimizer" }, new[] { "d4rk-avatar-optimizer" }, null);

            Assert.AreEqual(2, assessment.DetectedTools.Count, "both tools are still reported");
            Assert.AreEqual(0, assessment.Blockers.Count, "project presence alone does not block");
            Assert.IsTrue(assessment.IsOptimizationAllowed, "optimization stays allowed");
        }

        [Test("animation-driven systems are reported once each, in order")]
        public static void AnimationDrivenSystemsAreReported()
        {
            var assessment = CompatibilityPolicy.Evaluate(
                null, null, new[] { "PPP", "SPSConfigurer", "PPP", "penetrator" });

            Assert.AreEqual(3, assessment.AnimationDrivenSystems.Count, "duplicates collapse");
            Assert.AreEqual("PPP", assessment.AnimationDrivenSystems[0], "input order is kept");
            Assert.AreEqual("SPSConfigurer", assessment.AnimationDrivenSystems[1], "input order is kept");
            Assert.AreEqual("penetrator", assessment.AnimationDrivenSystems[2], "input order is kept");

            Assert.AreEqual(0, assessment.Warnings.Count, "systems never warn");
            Assert.AreEqual(0, assessment.Blockers.Count, "systems never block");
            Assert.IsTrue(assessment.IsOptimizationAllowed, "systems never block optimization");

            foreach (var finding in assessment.Findings)
            {
                Assert.AreEqual("compat.animatorDrivenSystem", finding.MessageKey, "system findings use one key");
                Assert.AreEqual(1, finding.Args.Count, "the system name is the only argument");
                Assert.AreEqual(CompatSeverity.Info, finding.Severity, "system findings are informational");
            }
        }

        [Test("findings are routed to exactly one list by severity")]
        public static void FindingsAreRoutedBySeverity()
        {
            var assessment = CompatibilityPolicy.Evaluate(
                new[] { "vrcfury", "avatar-optimizer", "d4rk-avatar-optimizer" },
                new[] { "ndmf" },
                new[] { "SPSConfigurer" });

            Assert.AreEqual(6, assessment.Findings.Count, "four tools, one blocker, one system");
            Assert.AreEqual(2, assessment.Warnings.Count, "exactly the two optimizer warnings");
            Assert.AreEqual(1, assessment.Blockers.Count, "exactly the combination blocker");
            Assert.AreEqual(3,
                assessment.Findings.Count - assessment.Warnings.Count - assessment.Blockers.Count,
                "the remaining findings are informational");

            foreach (var warning in assessment.Warnings)
            {
                Assert.AreEqual(CompatSeverity.Warning, warning.Severity, "warnings only hold warnings");
            }

            foreach (var blocker in assessment.Blockers)
            {
                Assert.AreEqual(CompatSeverity.Blocker, blocker.Severity, "blockers only hold blockers");
            }
        }

        [Test("evaluation is deterministic")]
        public static void EvaluationIsDeterministic()
        {
            var avatar = new[] { "d4rk-avatar-optimizer", "vrcfury", "avatar-optimizer" };
            var project = new[] { "ndmf", "modular-avatar" };
            var systems = new[] { "Penetrator", "Orifice" };

            var first = CompatibilityPolicy.Evaluate(avatar, project, systems);
            var second = CompatibilityPolicy.Evaluate(avatar, project, systems);

            Assert.AreEqual(string.Join(",", FindingKeys(first).ToArray()),
                string.Join(",", FindingKeys(second).ToArray()),
                "two evaluations agree on finding order");
            Assert.AreEqual(
                string.Join(",", first.AnimationDrivenSystems.ToArray()),
                string.Join(",", second.AnimationDrivenSystems.ToArray()),
                "two evaluations agree on system order");
        }

        // ------------------------------------------------------------------ helpers

        private static void AssertTool(string expectedToolId, CompatToolDefinition tool)
        {
            Assert.IsNotNull(tool, "a known identifier matches a tool, expected " + expectedToolId);
            Assert.AreEqualIgnoreCase(expectedToolId, tool.ToolId, "the match resolves to " + expectedToolId);
        }

        private static List<string> FindingKeys(CompatAssessment assessment)
        {
            var keys = new List<string>();
            for (var i = 0; i < assessment.Findings.Count; i++)
            {
                keys.Add(assessment.Findings[i].MessageKey);
            }

            return keys;
        }

        private static TranslationCatalog LoadCatalog(string code)
        {
            var path = PackageLayout.TranslationPath(code);
            Assert.IsTrue(File.Exists(path), "the translation file for '" + code + "' exists at " + path);
            return TranslationCatalog.FromJson(File.ReadAllText(path, Encoding.UTF8), code);
        }
    }
}
