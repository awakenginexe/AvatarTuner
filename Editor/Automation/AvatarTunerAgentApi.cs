using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AvatarTuner.Core;
using AvatarTuner.Editor.Tracking;
using AvatarTuner.Editor.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarTuner.Editor.Automation
{
    [InitializeOnLoad]
    public static class AvatarTunerAgentApi
    {
        public const int ApiVersion = 1;
        private static readonly int MainThreadId;
        private static bool _busy;
        // shortcut: retain one preview per editor, use per-avatar sessions if concurrent workflows are needed.
        private static AvatarTunerController _preview;
        private static string _previewToken;
        private static BuildTarget _previewBuildTarget;
        private static string _previewManifestRevision;

        static AvatarTunerAgentApi() { MainThreadId = Thread.CurrentThread.ManagedThreadId; }

        public static string Describe()
        {
            return MiniJson.Serialize(new Dictionary<string, object>
            {
                { "apiVersion", ApiVersion }, { "type", typeof(AvatarTunerAgentApi).FullName + ", AvatarTuner.Editor" },
                { "executeSignature", "string Execute(GameObject avatar, string requestJson, Func<bool> cancelled = null)" },
                { "guide", "Editor/Automation/README.md" }, { "requires", "Unity Editor main thread, Edit Mode, explicit scene avatar root" },
                { "actions", new Dictionary<string, object>
                    {
                        { "analyze", "Read-only scan and initial plan; no candidate imports." },
                        { "preview", "Evaluate candidates using optional settings; temporary asset imports, no scene application. Returns previewToken when changes exist." },
                        { "optimize", "Requires previewToken. Writes generated copies and recovery manifest, changes scene references with Undo. Replaces previous generated outputs. Does not save the scene." },
                        { "restore", "Restore recorded original scene references with Undo; keep generated files. Does not save the scene." }
                    }
                },
                { "requestFields", new[] { "action", "settings (preview only)", "previewToken (optimize only)" } },
                { "settingsDefaults", new OptimizationSettings().ToDictionary() },
                { "modes", Enum.GetNames(typeof(OptimizationMode)) }, { "standardLevels", Enum.GetNames(typeof(StandardOptimizationLevel)) },
                { "previewLifetime", "Only latest preview retained. Re-preview after domain reload, restore, another preview or any accepted optimize attempt. Avatar, platform, manifest and dependencies must still match." }
            });
        }

        public static string Execute(GameObject avatar, string requestJson, Func<bool> cancelled = null)
        {
            if (Thread.CurrentThread.ManagedThreadId != MainThreadId)
                return Reply(null, null, false, "agent.mainThreadRequired", "Call through Unity's main-thread editor execution bridge.");
            if (_busy) return Reply(null, null, false, "agent.busy", "Another AvatarTuner agent request is running.");
            _busy = true;
            AgentRequest request = null;
            AvatarTunerController controller = null;
            try
            {
                request = AgentRequest.Parse(requestJson);
                var token = _previewToken;
                var target = _previewBuildTarget;
                var manifest = _previewManifestRevision;
                if (request.Action == "optimize") controller = _preview;
                if (request.Action != "analyze") InvalidatePreview();
                if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                    return Reply(request.Action, null, false, "agent.editorNotReady", "Wait for Edit Mode with compilation and asset imports complete.");
                if (avatar == null || EditorUtility.IsPersistent(avatar) || !avatar.scene.IsValid() || EditorSceneManager.IsPreviewScene(avatar.scene))
                    return Reply(request.Action, null, false, "agent.sceneAvatarRequired", "Pass an explicit scene avatar root, outside Prefab Mode and preview scenes.");
                if (AvatarTunerController.ResolveAvatarRoot(avatar) != avatar)
                    return Reply(request.Action, null, false, "agent.avatarRootRequired", "Pass the avatar root rather than a child object.");
                if (cancelled != null && cancelled())
                    return Reply(request.Action, null, false, "error.cancelled", "Request cancelled before processing.");
                if (request.Action == "optimize")
                {
                    if (controller == null || controller.Avatar != avatar || token != request.PreviewToken)
                        return Reply(request.Action, null, false, "agent.previewRequired", "Run preview for this avatar and use its latest token.");
                    if (target != EditorUserBuildSettings.activeBuildTarget || manifest != ManifestRevision())
                        return Reply(request.Action, null, false, "agent.previewStale", "The build target or recovery manifest changed; run preview again.");
                    var succeeded = controller.RunOptimization(null, cancelled);
                    return ControllerReply(request.Action, controller, succeeded);
                }

                controller = new AvatarTunerController();
                controller.SelectAvatar(avatar);
                if (!string.IsNullOrEmpty(controller.ErrorKey)) return ControllerReply(request.Action, controller, false);
                if (request.Action == "restore")
                {
                    var error = controller.RestoreOriginalTextures();
                    return Reply(request.Action, controller, string.IsNullOrEmpty(error), error, error == null ? null : UiStrings.T(error));
                }

                controller.Settings = request.ResolveSettings(controller.Settings, controller.Overrides);
                controller.Analyze();
                if (controller.Report == null || !controller.Report.ScanSucceeded || !string.IsNullOrEmpty(controller.ErrorKey))
                    return ControllerReply(request.Action, controller, false);
                if (request.Action == "analyze") return ControllerReply(request.Action, controller, true);

                var unknownGuid = request.FindUnknownTexture(controller.Report);
                if (unknownGuid != null)
                    return Reply(request.Action, controller, false, "agent.unknownTexture", "Copy the exact texture GUID from this avatar's analysis: " + unknownGuid);
                if (!controller.CompatibilityAllows)
                    return Reply(request.Action, controller, false, "agent.compatibilityBlocked", "Resolve the compatibility blockers reported in data.findings.");
                if (!controller.EvaluateRecommendations(null, cancelled)) return ControllerReply(request.Action, controller, false);
                if (controller.Plan.HasChanges)
                {
                    _preview = controller;
                    _previewToken = Guid.NewGuid().ToString("N");
                    _previewBuildTarget = EditorUserBuildSettings.activeBuildTarget;
                    _previewManifestRevision = ManifestRevision();
                }
                return Reply(request.Action, controller, true, previewToken: _previewToken);
            }
            catch (ArgumentException ex) { return Reply(request == null ? null : request.Action, controller, false, "agent.invalidRequest", ex.Message); }
            catch (OperationCanceledException) { return Reply(request == null ? null : request.Action, controller, false, "error.cancelled", "Request cancelled."); }
            catch (Exception ex)
            {
                InvalidatePreview();
                return Reply(request == null ? null : request.Action, null, false, "agent.unexpected", ex.GetType().Name + ": " + ex.Message);
            }
            finally { _busy = false; }
        }

        private static string ControllerReply(string action, AvatarTunerController controller, bool succeeded)
        {
            var error = succeeded ? null : controller.ErrorKey ?? "agent.operationFailed";
            return Reply(action, controller, succeeded, error, error == null ? null : UiStrings.T(error, controller.ErrorArgs.ToArray()));
        }

        private static string Reply(string action, AvatarTunerController controller, bool success,
            string errorCode = null, string message = null, string previewToken = null)
        {
            Dictionary<string, object> data = null;
            if (controller != null)
            {
                var memory = action == "analyze" || action == "preview" ? controller.PlanMemoryNumbers() : controller.CurrentMemoryNumbers();
                memory.HasCurrent = controller.HasCurrentTextureBytes;
                memory.CurrentBytes = controller.CurrentTextureBytes;
                memory.CurrentFromSdk = controller.CurrentTextureBytesFromSdk;
                data = AgentReport.Build(controller.Report, controller.Plan, controller.Generation, memory);
                data["avatar"] = new Dictionary<string, object> { { "name", controller.Avatar == null ? null : controller.Avatar.name }, { "rootKey", controller.RootKey } };
                data["settings"] = controller.Settings.ToDictionary();
                var overrides = new Dictionary<string, object>();
                foreach (var pair in controller.Overrides) if (pair.Value != null) overrides[pair.Key] = pair.Value.ToDictionary();
                data["legacyOverrides"] = overrides;
                data["buildTarget"] = EditorUserBuildSettings.activeBuildTarget.ToString();
                data["evaluated"] = controller.HasEvaluatedPlan;
                data["applicationHealth"] = controller.ApplyHealth.ToString();
                data["previewToken"] = previewToken;
                var applicationFindings = new List<object>();
                foreach (var finding in controller.ApplicationFindings)
                    applicationFindings.Add(new Dictionary<string, object> { { "key", finding.MessageKey }, { "args", finding.Args } });
                data["applicationFindings"] = applicationFindings;
                Explain((List<object>)data["findings"]);
                Explain(applicationFindings);
                foreach (Dictionary<string, object> texture in (List<object>)data["textures"])
                    if (texture["plan"] is Dictionary<string, object> plan) Explain((List<object>)plan["notes"]);
            }
            return MiniJson.Serialize(new Dictionary<string, object>
            {
                { "apiVersion", ApiVersion }, { "action", action }, { "success", success },
                { "error", errorCode == null ? null : new Dictionary<string, object> { { "code", errorCode }, { "message", message }, { "args", controller == null ? new List<string>() : controller.ErrorArgs } } },
                { "data", data }
            });
        }

        private static string ManifestRevision()
        {
            return File.Exists(GeneratedAssetStore.ManifestPath) ? StableHash.Sha256Hex(File.ReadAllBytes(GeneratedAssetStore.ManifestPath)) : string.Empty;
        }

        private static void Explain(List<object> findings)
        {
            foreach (Dictionary<string, object> finding in findings)
                finding["message"] = UiStrings.T((string)finding["key"], ((List<string>)finding["args"]).ToArray());
        }

        private static void InvalidatePreview() { _preview = null; _previewToken = null; _previewManifestRevision = null; }
    }
}
