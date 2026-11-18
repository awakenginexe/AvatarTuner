using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using AvatarTuner.Core;
using AvatarTuner.Editor.Automation;
using AvatarTuner.Editor.UI;
using UnityEngine;
#if AVATARTUNER_OFFLINE_TESTS
using Assert = AvatarTuner.StandaloneTests.Assert;
using TestAttribute = AvatarTuner.StandaloneTests.TestAttribute;
#else
using NUnit.Framework;
#endif

namespace AvatarTuner.Tests
{
    public static class AgentApiTests
    {
        [Test]
        public static void DiscoveryAndReflectionEntryPoint()
        {
            var description = MiniJson.ParseObject(AvatarTunerAgentApi.Describe());
            Assert.AreEqual(1L, description["apiVersion"], "version is discoverable");
            Assert.AreEqual(4, ((Dictionary<string, object>)description["actions"]).Count, "four operations");
            var execute = typeof(AvatarTunerAgentApi).GetMethod("Execute");
            var response = (string)execute.Invoke(null, new object[] { null, "{\"action\":\"analyze\"}", null });
            Assert.AreEqual("agent.sceneAvatarRequired", Error(response), "documented reflection signature works");
        }

        [Test]
        public static void EmptyAvatarAnalysisReturnsAnUnevaluatedPlan()
        {
            WithAvatar(avatar =>
            {
                var result = MiniJson.ParseObject(AvatarTunerAgentApi.Execute(avatar, "{\"action\":\"analyze\"}"));
                Assert.AreEqual(true, result["success"], "empty scene avatar can be analyzed");
                Assert.IsNull(result["error"], "successful result has no error");
                var data = (Dictionary<string, object>)result["data"];
                Assert.AreEqual(false, data["evaluated"], "analysis does not import candidates");
                Assert.IsNull(data["previewToken"], "analysis does not authorize application");
                Assert.AreEqual(0, ((List<object>)data["textures"]).Count, "empty inventory");
            });
        }

        [Test]
        public static void NoChangePreviewDoesNotIssueAnApplyToken()
        {
            WithAvatar(avatar =>
            {
                var result = MiniJson.ParseObject(AvatarTunerAgentApi.Execute(avatar,
                    "{\"action\":\"preview\",\"settings\":{\"mode\":\"Standard\"}}"));
                Assert.AreEqual(true, result["success"], "no-change preview succeeds");
                var data = (Dictionary<string, object>)result["data"];
                Assert.AreEqual(true, data["evaluated"], "preview evaluates the plan");
                Assert.AreEqual(false, data["hasChanges"], "empty avatar has no changes");
                Assert.IsNull(data["previewToken"], "no changes to authorize");
                Assert.IsNull(data["outputFolder"], "no generation run");
            });
        }

        [Test]
        public static void InvalidRequestsAndMissingTargetsAreStructured()
        {
            Assert.AreEqual("agent.invalidRequest", Error(AvatarTunerAgentApi.Execute(null, "{}")), "invalid request");
            Assert.AreEqual("agent.sceneAvatarRequired", Error(AvatarTunerAgentApi.Execute(null, "{\"action\":\"restore\"}")), "no implicit selection");
        }

        [Test]
        public static void OptimizeCannotRunWithoutPreview()
        {
            WithAvatar(avatar => Assert.AreEqual("agent.previewRequired", Error(AvatarTunerAgentApi.Execute(avatar,
                "{\"action\":\"optimize\",\"previewToken\":\"invented\"}")), "no implicit preview or optimization"));
        }

        [Test]
        public static void WrongTokenConsumesThePreview()
        {
            WithAvatar(avatar =>
            {
                SeedPreview(avatar);
                Assert.AreEqual("agent.previewRequired", Error(AvatarTunerAgentApi.Execute(avatar,
                    "{\"action\":\"optimize\",\"previewToken\":\"wrong\"}")), "wrong token rejected");
                Assert.AreEqual("agent.previewRequired", Error(AvatarTunerAgentApi.Execute(avatar,
                    "{\"action\":\"optimize\",\"previewToken\":\"reviewed\"}")), "token cannot be retried");
            });
        }

        [Test]
        public static void TokenCannotApplyToAnotherAvatar()
        {
            WithAvatar(first => WithAvatar(second =>
            {
                SeedPreview(first);
                Assert.AreEqual("agent.previewRequired", Error(AvatarTunerAgentApi.Execute(second,
                    "{\"action\":\"optimize\",\"previewToken\":\"reviewed\"}")), "identity must match");
            }));
        }

        [Test]
        public static void ReplacingPreviewAndRestoreInvalidateTokensEvenOnCancellation()
        {
            WithAvatar(avatar =>
            {
                foreach (var action in new[] { "preview", "restore" })
                {
                    SeedPreview(avatar);
                    Assert.AreEqual("error.cancelled", Error(AvatarTunerAgentApi.Execute(avatar, "{\"action\":\"" + action + "\"}", () => true)), "cancel before processing");
                    Assert.AreEqual("agent.previewRequired", Error(AvatarTunerAgentApi.Execute(avatar,
                        "{\"action\":\"optimize\",\"previewToken\":\"reviewed\"}")), "old preview invalidated");
                }
            });
        }

        [Test]
        public static void PlatformAndManifestChangesInvalidatePreview()
        {
            WithAvatar(avatar =>
            {
                var current = UnityEditor.EditorUserBuildSettings.activeBuildTarget;
                SeedPreview(avatar);
                SetPreviewField("_previewBuildTarget", current == UnityEditor.BuildTarget.Android ? UnityEditor.BuildTarget.StandaloneWindows : UnityEditor.BuildTarget.Android);
                Assert.AreEqual("agent.previewStale", Error(AvatarTunerAgentApi.Execute(avatar,
                    "{\"action\":\"optimize\",\"previewToken\":\"reviewed\"}")), "platform changed");
                SeedPreview(avatar);
                SetPreviewField("_previewBuildTarget", current);
                SetPreviewField("_previewManifestRevision", "different revision");
                Assert.AreEqual("agent.previewStale", Error(AvatarTunerAgentApi.Execute(avatar,
                    "{\"action\":\"optimize\",\"previewToken\":\"reviewed\"}")), "recovery manifest changed");
            });
        }

        [Test]
        public static void ReentrantCallsAndWorkerThreadsAreRejected()
        {
            AvatarTunerAgentApi.Describe();
            Assert.AreEqual("agent.mainThreadRequired", Error(Task.Run(() => AvatarTunerAgentApi.Execute(null,
                "{\"action\":\"analyze\"}")).GetAwaiter().GetResult()), "background thread cannot touch Unity");
            WithAvatar(avatar =>
            {
                string nested = null;
                AvatarTunerAgentApi.Execute(avatar, "{\"action\":\"preview\"}", () =>
                {
                    nested = AvatarTunerAgentApi.Execute(avatar, "{\"action\":\"analyze\"}");
                    return true;
                });
                Assert.AreEqual("agent.busy", Error(nested), "nested calls cannot replace state");
            });
        }

        private static string Error(string json)
        {
            var response = MiniJson.ParseObject(json);
            Assert.AreEqual(false, response["success"], "failure must be explicit");
            return (string)((Dictionary<string, object>)response["error"])["code"];
        }

        private static void SeedPreview(GameObject avatar)
        {
            SetPreviewField("_preview", new AvatarTunerController { Avatar = avatar });
            SetPreviewField("_previewToken", "reviewed");
        }

        private static void SetPreviewField(string name, object value)
        {
            typeof(AvatarTunerAgentApi).GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
        }

        private static void WithAvatar(Action<GameObject> test)
        {
            var avatar = new GameObject { name = "Agent API boundary test" };
            try { test(avatar); }
            finally
            {
                SetPreviewField("_preview", null);
                SetPreviewField("_previewToken", null);
                UnityEngine.Object.DestroyImmediate(avatar);
            }
        }
    }
}
