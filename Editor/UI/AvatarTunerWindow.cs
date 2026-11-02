// AvatarTuner v1.0.0 — the editor window.
//
// A localized, fully wired IMGUI window over the existing engines. Design rules:
//   - Every control calls a real implementation (AvatarTunerController and friends);
//     nothing is decorative and nothing is shown that cannot work.
//   - Everything is localized through UiStrings (en / th / zh-Hans), switchable live.
//   - The window never touches source assets: optimize writes generated copies, cleanup
//     only removes what the manifest recorded.
//   - Every OnGUI pass is exception-proof: a failing draw degrades to an error box
//     instead of breaking the editor.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;
using AvatarTuner.Editor.Tracking;
using UnityEditor;
using UnityEngine;

namespace AvatarTuner.Editor.UI
{
    public sealed class AvatarTunerWindow : EditorWindow
    {
        private const int MaxTableRows = 200;

        private readonly AvatarTunerController _controller = new AvatarTunerController();

        private int _tab;
        private Vector2 _scroll;
        private string _filter = string.Empty;
        private bool _showAdvanced;
        private string _noticeKey;
        private readonly List<string> _noticeArgs = new List<string>();

        // ===================================================================== menu & lifecycle

        [MenuItem("Tools/AvatarTuner")]
        public static void Open()
        {
            var window = GetWindow<AvatarTunerWindow>();
            window.titleContent = new GUIContent(UiStrings.T("ui.window.title"));
            window.minSize = new Vector2(560, 640);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(UiStrings.T("ui.window.title"));
        }

        private void OnGUI()
        {
            try
            {
                DrawWindow();
            }
            catch (Exception ex)
            {
                // A draw pass must never take the editor down with it.
                EditorGUILayout.HelpBox(
                    UiStrings.T("error.unexpected", ex.Message), MessageType.Error);
            }
        }

        // ===================================================================== shell

        private void DrawWindow()
        {
            DrawHeader();
            DrawToolbar();
            DrawStateBanner();
            DrawNotice();

            _tab = GUILayout.Toolbar(_tab, TabLabels());

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case 0: DrawOverview(); break;
                case 1: DrawTextures(); break;
                case 2: DrawPreview(); break;
                case 3: DrawResults(); break;
                case 4: DrawTracking(); break;
                case 5: DrawMaterials(); break;
                case 6: DrawSettings(); break;
                default: DrawPreferences(); break;
            }

            EditorGUILayout.EndScrollView();
            DrawError();
        }

        private static string[] TabLabels()
        {
            return new[]
            {
                UiStrings.T("ui.tab.overview"),
                UiStrings.T("ui.tab.textures"),
                UiStrings.T("ui.tab.preview"),
                UiStrings.T("ui.tab.results"),
                UiStrings.T("ui.tab.tracking"),
                UiStrings.T("ui.tab.materials"),
                UiStrings.T("ui.tab.settings"),
                UiStrings.T("ui.tab.preferences")
            };
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField(UiStrings.T("ui.window.title"), EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var avatar = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent(UiStrings.T("ui.label.avatar"), UiStrings.T("tip.avatarField")),
                _controller.Avatar, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                _controller.SelectAvatar(avatar);
                ClearNotice();
            }

            DrawDropZone();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(UiStrings.T("ui.label.avatarPath"));
            EditorGUILayout.LabelField(string.IsNullOrEmpty(_controller.RootKey)
                ? UiStrings.T("ui.label.notAvailable")
                : _controller.RootKey);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent(UiStrings.T("ui.label.language"), UiStrings.T("tip.languageField")));
            var languageNames = new string[UiStrings.Languages.Length];
            var current = 0;
            for (var i = 0; i < UiStrings.Languages.Length; i++)
            {
                languageNames[i] = UiStrings.LanguageName(UiStrings.Languages[i]);
                if (UiStrings.Languages[i] == UiStrings.Language)
                {
                    current = i;
                }
            }

            var next = EditorGUILayout.Popup(current, languageNames);
            if (next != current)
            {
                UiStrings.Language = UiStrings.Languages[next];
                titleContent = new GUIContent(UiStrings.T("ui.window.title"));
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndHorizontal();

            DrawStatusLine();
        }

        private void DrawDropZone()
        {
            var rect = GUILayoutUtility.GetRect(0f, 40f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, UiStrings.T("ui.drop.hint"), EditorStyles.helpBox);

            var evt = Event.current;
            if (evt == null ||
                (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform))
            {
                return;
            }

            if (!rect.Contains(evt.mousePosition))
            {
                return;
            }

            var dragged = DraggedGameObject();
            if (dragged == null)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                if (evt.type == EventType.DragPerform)
                {
                    ShowNotice("ui.drop.invalidType");
                    evt.Use();
                }

                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                _controller.SelectAvatar(dragged);
                ClearNotice();
                evt.Use();
            }
        }

        private static GameObject DraggedGameObject()
        {
            var references = DragAndDrop.objectReferences;
            if (references == null)
            {
                return null;
            }

            for (var i = 0; i < references.Length; i++)
            {
                var gameObject = references[i] as GameObject;
                if (gameObject != null)
                {
                    return gameObject;
                }
            }

            return null;
        }

        private void DrawStatusLine()
        {
            var status = _controller.ChangeStatus != null
                ? _controller.ChangeStatus.Status
                : (_controller.RecordedState != null
                    ? _controller.RecordedState.Status
                    : OptimizationStatus.NotOptimized);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(UiStrings.T("ui.label.status"));
            EditorGUILayout.LabelField(UiStrings.T(StatusNameKey(status)), EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(UiStrings.T(StatusSummaryKey(status)), EditorStyles.miniLabel);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(!WindowFlow.CanAnalyze(_controller.State) ||
                                        _controller.Avatar == null);
            if (GUILayout.Button(new GUIContent(UiStrings.T("ui.button.analyze"), UiStrings.T("tip.analyzeButton"))))
            {
                RunAnalyze();
            }

            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!WindowFlow.CanPreview(_controller.State, _controller.Plan != null));
            if (GUILayout.Button(UiStrings.T("ui.button.preview")))
            {
                _tab = 2;
            }

            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!WindowFlow.CanOptimize(
                _controller.State, _controller.Plan != null,
                _controller.Plan != null && _controller.Plan.HasChanges,
                _controller.CompatibilityAllows));
            var optimizeLabel = _controller.HasRecordedOutputs
                ? UiStrings.T("ui.button.regenerate")
                : UiStrings.T("ui.button.optimize");
            if (GUILayout.Button(new GUIContent(optimizeLabel, UiStrings.T("tip.optimizeButton"))))
            {
                RunOptimize();
            }

            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!WindowFlow.CanCleanup(_controller.State, _controller.HasRecordedOutputs));
            if (GUILayout.Button(new GUIContent(UiStrings.T("ui.button.cleanup"), UiStrings.T("tip.cleanup"))))
            {
                RunCleanup();
            }

            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button(UiStrings.T("ui.button.refresh")))
            {
                _controller.RefreshStatus();
            }

            if (GUILayout.Button(UiStrings.T("ui.button.reset")))
            {
                _controller.ClearOverrides();
                _controller.SelectAvatar(_controller.Avatar);
                ClearNotice();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStateBanner()
        {
            switch (_controller.State)
            {
                case WindowState.NoSelection:
                    EditorGUILayout.HelpBox(
                        UiStrings.T("ui.state.noSelection.title") + "\n" + UiStrings.T("ui.state.noSelection.body"),
                        MessageType.Info);
                    break;
                case WindowState.Ready:
                    EditorGUILayout.HelpBox(
                        UiStrings.T("ui.state.empty.title") + "\n" + UiStrings.T("ui.state.empty.body"),
                        MessageType.Info);
                    break;
                case WindowState.Analyzing:
                case WindowState.Optimizing:
                    EditorGUILayout.HelpBox(
                        UiStrings.T("ui.state.loading.title") + "\n" + UiStrings.T("ui.state.loading.body"),
                        MessageType.Info);
                    break;
                case WindowState.Done:
                    EditorGUILayout.HelpBox(
                        UiStrings.T("ui.state.success.title") + "\n" + UiStrings.T("ui.state.success.body"),
                        MessageType.Info);
                    break;
                case WindowState.Error:
                    EditorGUILayout.HelpBox(
                        UiStrings.T("ui.state.error.title") + "\n" + UiStrings.T("ui.state.error.body"),
                        MessageType.Warning);
                    break;
            }
        }

        private void DrawError()
        {
            if (string.IsNullOrEmpty(_controller.ErrorKey))
            {
                return;
            }

            EditorGUILayout.HelpBox(UiStrings.T(_controller.ErrorKey, _controller.ErrorArgs.ToArray()), MessageType.Error);
        }

        private void DrawNotice()
        {
            if (string.IsNullOrEmpty(_noticeKey))
            {
                return;
            }

            EditorGUILayout.HelpBox(UiStrings.T(_noticeKey, _noticeArgs.ToArray()), MessageType.Info);
        }

        private void ShowNotice(string key, params string[] args)
        {
            _noticeKey = key;
            _noticeArgs.Clear();
            if (args != null)
            {
                _noticeArgs.AddRange(args);
            }
        }

        private void ClearNotice()
        {
            _noticeKey = null;
            _noticeArgs.Clear();
        }

        // ===================================================================== actions

        private void RunAnalyze()
        {
            ClearNotice();
            try
            {
                EditorUtility.DisplayProgressBar(
                    UiStrings.T("ui.window.title"), UiStrings.T("opt.phase.scan"), 0.5f);
            }
            catch (Exception)
            {
                // progress display is cosmetic; analysis must run either way
            }

            try
            {
                _controller.Analyze();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void RunOptimize()
        {
            ClearNotice();
            var outputFolder = TextureOptimizer.DefaultOutputFolder(_controller.Report);
            if (!EditorUtility.DisplayDialog(
                    UiStrings.T("dlg.optimize.title"),
                    UiStrings.T("dlg.optimize.message", outputFolder),
                    UiStrings.T("dlg.yes"),
                    UiStrings.T("dlg.no")))
            {
                return;
            }

            if (!_controller.OverwriteGenerated && _controller.HasRecordedOutputs)
            {
                var existing = _controller.RecordedState != null
                    ? _controller.RecordedState.Generated.Count.ToString(CultureInfo.InvariantCulture)
                    : "0";
                if (!EditorUtility.DisplayDialog(
                        UiStrings.T("dlg.overwrite.title"),
                        UiStrings.T("dlg.overwrite.message", existing),
                        UiStrings.T("dlg.yes"),
                        UiStrings.T("dlg.no")))
                {
                    return;
                }
            }

            _cancelRequested = false;
            var succeeded = false;
            try
            {
                succeeded = _controller.RunOptimization(OnOptimizationProgress, () => _cancelRequested);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (succeeded)
            {
                ShowNotice("opt.done");
                _tab = 3;
            }
            else if (_cancelRequested)
            {
                ShowNotice("opt.cancelled");
            }
        }

        private bool _cancelRequested;

        private void OnOptimizationProgress(float value, string phaseKey)
        {
            var plan = _controller.Plan;
            var total = plan != null && plan.ChangedCount > 0 ? plan.ChangedCount : 1;
            var current = (int)Math.Round(value * total);
            if (current < 1)
            {
                current = 1;
            }

            if (current > total)
            {
                current = total;
            }

            if (EditorUtility.DisplayCancelableProgressBar(
                    UiStrings.T("ui.window.title"),
                    UiStrings.T("opt.progress", UiStrings.T(phaseKey),
                        current.ToString(CultureInfo.InvariantCulture),
                        total.ToString(CultureInfo.InvariantCulture)),
                    value))
            {
                // Confirm before stopping: written files are kept and recorded either way.
                if (EditorUtility.DisplayDialog(
                        UiStrings.T("dlg.cancelRun.title"),
                        UiStrings.T("dlg.cancelRun.message"),
                        UiStrings.T("dlg.yes"),
                        UiStrings.T("dlg.no")))
                {
                    _cancelRequested = true;
                }
            }
        }

        private void RunCleanup()
        {
            ClearNotice();
            var count = _controller.RecordedState != null
                ? _controller.RecordedState.Generated.Count.ToString(CultureInfo.InvariantCulture)
                : "0";
            if (!EditorUtility.DisplayDialog(
                    UiStrings.T("dlg.cleanup.title"),
                    UiStrings.T("dlg.cleanup.message", count, GeneratedAssetStore.GeneratedRoot),
                    UiStrings.T("dlg.yes"),
                    UiStrings.T("dlg.no")))
            {
                return;
            }

            _controller.CleanupGenerated();
            ShowNotice("opt.done");
        }

        // ===================================================================== tab: overview

        private void DrawOverview()
        {
            Section("ui.section.scanSummary");
            if (_controller.Report == null)
            {
                EditorGUILayout.LabelField(UiStrings.T("ui.label.tableEmpty"));
            }
            else
            {
                DrawScanSummaries(_controller.Report);
            }

            Section("ui.section.compatibility");
            DrawCompatibility();

            Section("ui.section.recommendations");
            DrawFindings();
        }

        private void DrawScanSummaries(AvatarAnalysisReport report)
        {
            var plan = _controller.Plan;
            var gpuBefore = plan != null ? plan.SourceBytesTotal : 0L;
            EditorGUILayout.LabelField(UiStrings.T("analysis.textureSummary",
                report.Textures.Count.ToString(CultureInfo.InvariantCulture),
                report.Materials.Count.ToString(CultureInfo.InvariantCulture),
                TextureFormatInfo.FormatBytes(gpuBefore)));

            var uniqueTriangles = report.UniqueMeshTriangles;
            EditorGUILayout.LabelField(UiStrings.T("analysis.meshSummary",
                CountUniqueMeshes(report).ToString(CultureInfo.InvariantCulture),
                report.Renderers.Count.ToString(CultureInfo.InvariantCulture),
                uniqueTriangles.ToString(CultureInfo.InvariantCulture),
                FirstBoneCount(report).ToString(CultureInfo.InvariantCulture),
                report.TotalBlendShapes.ToString(CultureInfo.InvariantCulture)));

            var swapCurves = 0;
            for (var i = 0; i < report.Animators.Count; i++)
            {
                swapCurves += report.Animators[i].MaterialSwapTargets.Count;
            }

            EditorGUILayout.LabelField(UiStrings.T("analysis.animatorSummary",
                report.Animators.Count.ToString(CultureInfo.InvariantCulture),
                CountClips(report).ToString(CultureInfo.InvariantCulture),
                swapCurves.ToString(CultureInfo.InvariantCulture)));

            var physBones = 0;
            var contacts = 0;
            for (var i = 0; i < report.DynamicComponents.Count; i++)
            {
                var kind = report.DynamicComponents[i].KindKey ?? string.Empty;
                if (kind == "finding.kind.physBone" || kind == "finding.kind.physBoneCollider")
                {
                    physBones++;
                }
                else if (kind == "finding.kind.contactSender" || kind == "finding.kind.contactReceiver")
                {
                    contacts++;
                }
            }

            EditorGUILayout.LabelField(UiStrings.T("analysis.dynamicSummary",
                physBones.ToString(CultureInfo.InvariantCulture),
                contacts.ToString(CultureInfo.InvariantCulture)));

            if (report.InactiveObjects.Count > 0)
            {
                EditorGUILayout.LabelField(UiStrings.T("analysis.inactiveSummary",
                    report.InactiveObjects.Count.ToString(CultureInfo.InvariantCulture)));
            }

            var shared = 0;
            for (var i = 0; i < report.Textures.Count; i++)
            {
                if (report.Textures[i].Sharing != SharingKind.Unique)
                {
                    shared++;
                }
            }

            if (shared > 0)
            {
                EditorGUILayout.LabelField(UiStrings.T("analysis.sharedSummary",
                    shared.ToString(CultureInfo.InvariantCulture)));
            }

            if (report.PossibleBuildTimeDependencies.Count > 0)
            {
                EditorGUILayout.LabelField(UiStrings.T("analysis.buildTimeSummary",
                    report.PossibleBuildTimeDependencies.Count.ToString(CultureInfo.InvariantCulture)));
            }

            if (plan != null)
            {
                var shaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < report.Materials.Count; i++)
                {
                    if (!string.IsNullOrEmpty(report.Materials[i].ShaderName))
                    {
                        shaders.Add(report.Materials[i].ShaderName);
                    }
                }

                EditorGUILayout.LabelField(UiStrings.T("analysis.shaderSummary",
                    shaders.Count.ToString(CultureInfo.InvariantCulture),
                    report.Materials.Count.ToString(CultureInfo.InvariantCulture)));

                var large = 0;
                var threshold = 4L * 1024L * 1024L;
                for (var i = 0; i < plan.Textures.Count; i++)
                {
                    if (plan.Textures[i].Plan != null && plan.Textures[i].Plan.SourceBytes > threshold)
                    {
                        large++;
                    }
                }

                if (large > 0)
                {
                    EditorGUILayout.LabelField(UiStrings.T("analysis.largeTextures",
                        large.ToString(CultureInfo.InvariantCulture),
                        TextureFormatInfo.FormatBytes(threshold)));
                }
            }
        }

        private void DrawCompatibility()
        {
            var report = _controller.Report;
            if (report == null)
            {
                EditorGUILayout.LabelField(UiStrings.T("ui.label.tableEmpty"));
                return;
            }

            var compat = report.Compatibility;
            if (compat.DetectedTools.Count == 0 && compat.Notices.Count == 0 &&
                compat.Warnings.Count == 0 && compat.Blockers.Count == 0)
            {
                EditorGUILayout.HelpBox(UiStrings.T("compat.noneFound"), MessageType.Info);
                return;
            }

            if (compat.DetectedTools.Count > 0)
            {
                var names = new List<string>();
                for (var i = 0; i < compat.DetectedTools.Count; i++)
                {
                    names.Add(compat.DetectedTools[i].DisplayName);
                }

                EditorGUILayout.LabelField(UiStrings.T("compat.detected", string.Join(", ", names.ToArray())),
                    EditorStyles.boldLabel);
            }

            EditorGUILayout.HelpBox(UiStrings.T("compat.notModified"), MessageType.None);

            for (var i = 0; i < compat.DetectedTools.Count; i++)
            {
                var tool = compat.DetectedTools[i];
                EditorGUILayout.LabelField(
                    tool.DisplayName + "  [" + UiStrings.T(RiskKey(tool.Risk)) + "]",
                    string.IsNullOrEmpty(tool.Path) ? tool.ComponentType : tool.Path);
                EditorGUILayout.LabelField(UiStrings.T(tool.MessageKey), EditorStyles.miniLabel);
            }

            DrawFindings(compat.Blockers, MessageType.Error);
            DrawFindings(compat.Warnings, MessageType.Warning);
            DrawFindings(compat.Notices, MessageType.None);

            if (compat.Warnings.Count > 0 || compat.Blockers.Count > 0)
            {
                EditorGUILayout.HelpBox(UiStrings.T("compat.manualInspection"), MessageType.Warning);
            }
        }

        private void DrawFindings()
        {
            if (_controller.Report == null)
            {
                EditorGUILayout.LabelField(UiStrings.T("ui.label.tableEmpty"));
                return;
            }

            if (_controller.Report.Findings.Count == 0)
            {
                EditorGUILayout.LabelField(UiStrings.T("ui.label.tableEmpty"));
                return;
            }

            EditorGUILayout.LabelField(UiStrings.T("analysis.recommendationTitle"), EditorStyles.boldLabel);
            DrawFindings(_controller.Report.Findings, MessageType.None);
            if (_controller.Plan != null)
            {
                DrawFindings(_controller.Plan.Findings, MessageType.None);
            }
        }

        private void DrawFindings(List<AnalysisFinding> findings, MessageType type)
        {
            for (var i = 0; i < findings.Count; i++)
            {
                EditorGUILayout.HelpBox(
                    UiStrings.T(findings[i].MessageKey, findings[i].Args.ToArray()), type);
            }
        }

        // ===================================================================== tab: textures

        private void DrawTextures()
        {
            if (_controller.Report == null)
            {
                EditorGUILayout.LabelField(UiStrings.T("ui.label.tableEmpty"));
                return;
            }

            _filter = EditorGUILayout.TextField(
                new GUIContent(UiStrings.T("ui.label.searchPlaceholder")), _filter);

            DrawTextureHeader();
            var shown = 0;
            for (var i = 0; i < _controller.Report.Textures.Count; i++)
            {
                var entry = _controller.Report.Textures[i];
                if (!PassesFilter(entry.Name, entry.Path))
                {
                    continue;
                }

                if (shown >= MaxTableRows)
                {
                    EditorGUILayout.LabelField(UiStrings.T("ui.label.moreItems",
                        (_controller.Report.Textures.Count - shown).ToString(CultureInfo.InvariantCulture)));
                    break;
                }

                DrawTextureRow(entry);
                shown++;
            }

            if (shown == 0)
            {
                EditorGUILayout.LabelField(UiStrings.T("ui.label.tableEmpty"));
            }
        }

        private void DrawTextureHeader()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(UiStrings.T("ui.label.sourceAsset"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(UiStrings.T("ui.label.resolution"), EditorStyles.boldLabel, GUILayout.Width(90));
            EditorGUILayout.LabelField(UiStrings.T("ui.label.format"), EditorStyles.boldLabel, GUILayout.Width(70));
            EditorGUILayout.LabelField(UiStrings.T("ui.label.usage"), EditorStyles.boldLabel, GUILayout.Width(110));
            EditorGUILayout.LabelField(UiStrings.T("ui.label.sharedAssets"), EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField(UiStrings.T("ui.label.include"), EditorStyles.boldLabel, GUILayout.Width(56));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTextureRow(TextureEntry entry)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(entry.Name ?? entry.Guid);
            EditorGUILayout.LabelField(entry.Width + " × " + entry.Height, GUILayout.Width(90));
            EditorGUILayout.LabelField(
                entry.CurrentFormatKnown ? entry.CurrentFormatName : UiStrings.T("ui.label.unknown"),
                GUILayout.Width(70));
            EditorGUILayout.LabelField(entry.Usage.ToString(), GUILayout.Width(110));
            EditorGUILayout.LabelField(
                entry.Sharing == SharingKind.Unique ? UiStrings.T("ui.label.no") : UiStrings.T("ui.label.yes"),
                GUILayout.Width(60));

            var planned = FindPlanned(entry.Guid);
            var isProtected = planned != null && planned.ProtectedByThirdParty;
            var included = planned == null || planned.Plan == null || !planned.Plan.IsExcluded;

            EditorGUI.BeginDisabledGroup(isProtected);
            var next = EditorGUILayout.Toggle(included, GUILayout.Width(56));
            if (next != included && entry.Guid != null)
            {
                var ovr = _controller.Overrides.ContainsKey(entry.Guid)
                    ? _controller.Overrides[entry.Guid]
                    : new TextureOverride();
                ovr.Excluded = !next;
                _controller.SetOverride(entry.Guid, ovr);
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (isProtected)
            {
                EditorGUILayout.LabelField(UiStrings.T("plan.note.protectedThirdParty"), EditorStyles.miniLabel);
            }
        }

        private PlannedTexture FindPlanned(string guid)
        {
            if (_controller.Plan == null || string.IsNullOrEmpty(guid))
            {
                return null;
            }

            for (var i = 0; i < _controller.Plan.Textures.Count; i++)
            {
                if (string.Equals(_controller.Plan.Textures[i].TextureGuid, guid, StringComparison.OrdinalIgnoreCase))
                {
                    return _controller.Plan.Textures[i];
                }
            }

            return null;
        }

        // ===================================================================== tab: preview

        private void DrawPreview()
        {
            var plan = _controller.Plan;
            if (plan == null)
            {
                EditorGUILayout.LabelField(UiStrings.T("ui.label.tableEmpty"));
                return;
            }

            Section("ui.section.texturePlan");
            EditorGUILayout.LabelField(UiStrings.T("results.texturePlanSummary",
                plan.ChangedCount.ToString(CultureInfo.InvariantCulture),
                plan.UnchangedCount.ToString(CultureInfo.InvariantCulture),
                plan.ExcludedCount.ToString(CultureInfo.InvariantCulture)));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(UiStrings.T("ui.label.sourceAsset"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(UiStrings.T("ui.label.format"), EditorStyles.boldLabel, GUILayout.Width(110));
            EditorGUILayout.LabelField(UiStrings.T("ui.label.resolution"), EditorStyles.boldLabel, GUILayout.Width(110));
            EditorGUILayout.LabelField(UiStrings.T("ui.label.size"), EditorStyles.boldLabel, GUILayout.Width(110));
            EditorGUILayout.LabelField(UiStrings.T("ui.label.saving"), EditorStyles.boldLabel, GUILayout.Width(64));
            EditorGUILayout.EndHorizontal();

            for (var i = 0; i < plan.Textures.Count; i++)
            {
                DrawPlanRow(plan.Textures[i]);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                UiStrings.T("results.gpuMemoryBefore") + ": " + TextureFormatInfo.FormatBytes(plan.SourceBytesTotal));
            EditorGUILayout.LabelField(
                UiStrings.T("results.gpuMemoryAfter") + ": " + TextureFormatInfo.FormatBytes(plan.TargetBytesTotal));
            EditorGUILayout.LabelField(
                UiStrings.T("results.gpuMemorySaved") + ": " + TextureFormatInfo.FormatBytes(plan.SavingBytes) +
                " (" + plan.SavingPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%)");
            EditorGUILayout.HelpBox(UiStrings.T("results.basisComputed"), MessageType.None);
        }

        private void DrawPlanRow(PlannedTexture planned)
        {
            var plan = planned.Plan;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(planned.TextureName ?? planned.TextureGuid);
            EditorGUILayout.LabelField(
                FormatFormat(plan.SourceFormat) + " → " + FormatFormat(plan.TargetFormat),
                GUILayout.Width(110));
            EditorGUILayout.LabelField(
                plan.SourceWidth + "×" + plan.SourceHeight + " → " + plan.TargetWidth + "×" + plan.TargetHeight,
                GUILayout.Width(110));
            EditorGUILayout.LabelField(
                TextureFormatInfo.FormatBytes(plan.SourceBytes) + " → " + TextureFormatInfo.FormatBytes(plan.TargetBytes),
                GUILayout.Width(110));
            EditorGUILayout.LabelField(
                plan.SavingPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%",
                GUILayout.Width(64));
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(plan.SkipReasonKey))
            {
                EditorGUILayout.LabelField(
                    UiStrings.T("ui.label.reason") + ": " + UiStrings.T(plan.SkipReasonKey),
                    EditorStyles.miniLabel);
            }

            for (var n = 0; n < plan.Notes.Count; n++)
            {
                var note = plan.Notes[n];
                if (note.Level == PlanNote.Severity.Info)
                {
                    EditorGUILayout.LabelField("• " + UiStrings.T(note.Key, note.Args.ToArray()), EditorStyles.miniLabel);
                }
            }

            DrawPlanWarnings(planned);
        }

        private void DrawPlanWarnings(PlannedTexture planned)
        {
            var plan = planned.Plan;
            for (var n = 0; n < plan.Notes.Count; n++)
            {
                if (plan.Notes[n].Level != PlanNote.Severity.Info)
                {
                    EditorGUILayout.HelpBox(
                        UiStrings.T(plan.Notes[n].Key, plan.Notes[n].Args.ToArray()),
                        plan.Notes[n].Level == PlanNote.Severity.Error ? MessageType.Error : MessageType.Warning);
                }
            }
        }

        private static string FormatFormat(TunerTextureFormat format)
        {
            return TextureFormatInfo.DisplayName(format);
        }

        // ===================================================================== tab: results

        private void DrawResults()
        {
            Section("ui.section.output");
            var generation = _controller.Generation;
            if (generation == null)
            {
                EditorGUILayout.HelpBox(UiStrings.T("results.noResultsYet"), MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(UiStrings.T("results.title"), EditorStyles.boldLabel);
            var plan = _controller.Plan;
            if (plan != null)
            {
                EditorGUILayout.LabelField(UiStrings.T("results.gpuMemoryBefore") + ": " +
                                           TextureFormatInfo.FormatBytes(plan.SourceBytesTotal));
                EditorGUILayout.LabelField(UiStrings.T("results.gpuMemoryAfter") + ": " +
                                           TextureFormatInfo.FormatBytes(plan.TargetBytesTotal));
                EditorGUILayout.LabelField(UiStrings.T("results.gpuMemorySaved") + ": " +
                                           TextureFormatInfo.FormatBytes(plan.SavingBytes));
            }

            long diskBefore = 0;
            if (plan != null)
            {
                for (var i = 0; i < plan.Textures.Count; i++)
                {
                    var entry = _controller.Report != null ? _controller.Report.FindTexture(plan.Textures[i].TextureGuid) : null;
                    if (entry != null)
                    {
                        diskBefore += entry.SourceFileSizeBytes;
                    }
                }
            }

            long diskAfter = 0;
            for (var i = 0; i < generation.Generated.Count; i++)
            {
                diskAfter += generation.Generated[i].GeneratedFileSizeBytes;
            }

            EditorGUILayout.LabelField(UiStrings.T("results.diskBefore") + ": " + TextureFormatInfo.FormatBytes(diskBefore));
            EditorGUILayout.LabelField(UiStrings.T("results.diskAfter") + ": " + TextureFormatInfo.FormatBytes(diskAfter));
            EditorGUILayout.LabelField(UiStrings.T("results.generatedDependencies",
                generation.Generated.Count.ToString(CultureInfo.InvariantCulture),
                generation.Generated.Count.ToString(CultureInfo.InvariantCulture)));
            EditorGUILayout.HelpBox(UiStrings.T("results.basisComputed"), MessageType.None);
            EditorGUILayout.HelpBox(UiStrings.T("results.disclaimer"), MessageType.None);

            if (generation.SkippedCount > 0)
            {
                EditorGUILayout.HelpBox(UiStrings.T("opt.skippedUnsupported",
                    generation.SkippedCount.ToString(CultureInfo.InvariantCulture)), MessageType.Warning);
            }

            if (!string.IsNullOrEmpty(generation.OutputFolder))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(UiStrings.T("ui.label.outputFolder"));
                EditorGUILayout.LabelField(generation.OutputFolder);
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button(UiStrings.T("ui.button.openOutputFolder")))
            {
                EditorUtility.RevealInFinder(GeneratedAssetStore.GeneratedRoot);
            }
        }

        // ===================================================================== tab: tracking

        private void DrawTracking()
        {
            Section("ui.section.changeTracking");
            EditorGUILayout.HelpBox(UiStrings.T("tip.changeTracking"), MessageType.None);

            if (_controller.ChangeStatus == null)
            {
                EditorGUILayout.LabelField(UiStrings.T("ui.label.tableEmpty"));
                return;
            }

            var status = _controller.ChangeStatus;
            DrawStatusLine();

            EditorGUILayout.LabelField(UiStrings.T("ui.label.changedFiles") + ": " +
                                       status.Changes.Count.ToString(CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField(UiStrings.T("ui.label.staleOutputs") + ": " +
                                       status.StaleGeneratedPaths.Count.ToString(CultureInfo.InvariantCulture));
            EditorGUILayout.LabelField(UiStrings.T("ui.label.validOutputs") + ": " +
                                       status.ValidGeneratedPaths.Count.ToString(CultureInfo.InvariantCulture));

            for (var i = 0; i < status.Changes.Count; i++)
            {
                var change = status.Changes[i];
                EditorGUILayout.HelpBox(
                    UiStrings.T(change.DetailKey, change.Args.ToArray()),
                    change.InvalidatesEverything ? MessageType.Warning : MessageType.None);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(UiStrings.T("ui.button.viewManifest")))
            {
                var manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(GeneratedAssetStore.ManifestPath);
                if (manifest != null)
                {
                    AssetDatabase.OpenAsset(manifest);
                }
                else
                {
                    EditorUtility.RevealInFinder(GeneratedAssetStore.GeneratedRoot);
                }
            }

            if (GUILayout.Button(UiStrings.T("ui.button.openOutputFolder")))
            {
                EditorUtility.RevealInFinder(GeneratedAssetStore.GeneratedRoot);
            }

            EditorGUILayout.EndHorizontal();

            if (_controller.RecordedState != null && !string.IsNullOrEmpty(_controller.RecordedState.LastSuccessUtc))
            {
                EditorGUILayout.LabelField(UiStrings.T("ui.label.lastOptimized"),
                    _controller.RecordedState.LastSuccessUtc);
                EditorGUILayout.LabelField(UiStrings.T("ui.label.toolVersion"),
                    _controller.RecordedState.ToolVersionAtOptimization);
                EditorGUILayout.LabelField(UiStrings.T("ui.label.engineVersion"),
                    _controller.RecordedState.EngineVersionAtOptimization);
            }
        }

        // ===================================================================== tab: materials

        private void DrawMaterials()
        {
            var report = _controller.Report;
            if (report == null)
            {
                EditorGUILayout.LabelField(UiStrings.T("ui.label.tableEmpty"));
                return;
            }

            Section("ui.tab.materials");

            EditorGUILayout.LabelField(UiStrings.T("ui.label.materials"), EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(UiStrings.T("ui.label.sourceAsset"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(UiStrings.T("ui.label.shader"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(UiStrings.T("ui.label.textures"), EditorStyles.boldLabel, GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();

            for (var i = 0; i < report.Materials.Count; i++)
            {
                var material = report.Materials[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(material.Name ?? material.Guid);
                EditorGUILayout.LabelField(string.IsNullOrEmpty(material.ShaderName)
                    ? UiStrings.T("ui.label.unknown")
                    : material.ShaderName + (material.ShaderIsCustom ? " (*)" : string.Empty));
                EditorGUILayout.LabelField(
                    material.TextureBindings.Count.ToString(CultureInfo.InvariantCulture), GUILayout.Width(70));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(UiStrings.T("ui.label.meshes"), EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(UiStrings.T("ui.label.sourceAsset"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(UiStrings.T("ui.label.triangles"), EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.LabelField(UiStrings.T("ui.label.bones"), EditorStyles.boldLabel, GUILayout.Width(60));
            EditorGUILayout.LabelField(UiStrings.T("ui.label.blendShapes"), EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            for (var i = 0; i < report.Renderers.Count; i++)
            {
                var renderer = report.Renderers[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(renderer.Path);
                EditorGUILayout.LabelField(renderer.TriangleCount.ToString(CultureInfo.InvariantCulture), GUILayout.Width(80));
                EditorGUILayout.LabelField(renderer.BoneCount.ToString(CultureInfo.InvariantCulture), GUILayout.Width(60));
                EditorGUILayout.LabelField(renderer.BlendShapeCount.ToString(CultureInfo.InvariantCulture), GUILayout.Width(80));
                EditorGUILayout.EndHorizontal();
            }
        }

        // ===================================================================== tab: settings

        private void DrawSettings()
        {
            EditorGUI.BeginDisabledGroup(!WindowFlow.CanEditSettings(_controller.State));

            Section("ui.section.quality");
            EditorGUILayout.LabelField(
                new GUIContent(UiStrings.T("ui.label.preset"), UiStrings.T("tip.presetField")));
            var preset = (OptimizationPreset)EditorGUILayout.EnumPopup(_controller.Preset);
            if (preset != _controller.Preset)
            {
                _controller.Preset = preset;
                _controller.RebuildPlan();
            }

            EditorGUILayout.HelpBox(UiStrings.T(PresetDescriptionKey(preset)), MessageType.None);

            Section("ui.section.exclusions");
            EditorGUILayout.HelpBox(UiStrings.T("tip.exclusionList"), MessageType.None);
            var excluded = 0;
            if (_controller.Plan != null)
            {
                for (var i = 0; i < _controller.Plan.Textures.Count; i++)
                {
                    if (_controller.Plan.Textures[i].Plan != null &&
                        _controller.Plan.Textures[i].Plan.IsExcluded)
                    {
                        excluded++;
                    }
                }
            }

            EditorGUILayout.LabelField(UiStrings.T("ui.label.excluded") + ": " +
                                       excluded.ToString(CultureInfo.InvariantCulture));

            Section("ui.section.overrides");
            DrawOverrideControls();

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(UiStrings.T("ui.button.revertOverrides")))
            {
                _controller.ClearOverrides();
            }

            if (GUILayout.Button(UiStrings.T("ui.button.clearExclusions")))
            {
                ClearExclusions();
            }

            if (GUILayout.Button(UiStrings.T("ui.button.selectAll")))
            {
                SetAllIncluded(true);
            }

            if (GUILayout.Button(UiStrings.T("ui.button.selectNone")))
            {
                SetAllIncluded(false);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawOverrideControls()
        {
            var plan = _controller.Plan;
            if (plan == null)
            {
                EditorGUILayout.LabelField(UiStrings.T("ui.label.tableEmpty"));
                return;
            }

            var shown = 0;
            for (var i = 0; i < plan.Textures.Count; i++)
            {
                var planned = plan.Textures[i];
                if (!PassesFilter(planned.TextureName, planned.TexturePath))
                {
                    continue;
                }

                if (shown >= MaxTableRows)
                {
                    EditorGUILayout.LabelField(UiStrings.T("ui.label.moreItems",
                        (plan.Textures.Count - shown).ToString(CultureInfo.InvariantCulture)));
                    break;
                }

                DrawOverrideRow(planned);
                shown++;
            }
        }

        private void DrawOverrideRow(PlannedTexture planned)
        {
            var guid = planned.TextureGuid;
            if (string.IsNullOrEmpty(guid))
            {
                return;
            }

            TextureOverride ovr;
            if (!_controller.Overrides.TryGetValue(guid, out ovr))
            {
                ovr = null;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(planned.TextureName ?? guid);

            EditorGUI.BeginDisabledGroup(planned.ProtectedByThirdParty);

            var keepResolution = ovr != null && ovr.KeepResolution;
            var nextKeepResolution = EditorGUILayout.ToggleLeft(
                UiStrings.T("ui.label.resolution"), keepResolution, GUILayout.Width(90));
            var keepFormat = ovr != null && ovr.KeepFormat;
            var nextKeepFormat = EditorGUILayout.ToggleLeft(
                UiStrings.T("ui.label.format"), keepFormat, GUILayout.Width(80));

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (nextKeepResolution != keepResolution || nextKeepFormat != keepFormat)
            {
                var updated = ovr ?? new TextureOverride();
                updated.KeepResolution = nextKeepResolution;
                updated.KeepFormat = nextKeepFormat;
                _controller.SetOverride(guid, updated);
            }
        }

        private void ClearExclusions()
        {
            var guids = new List<string>(_controller.Overrides.Keys);
            for (var i = 0; i < guids.Count; i++)
            {
                var ovr = _controller.Overrides[guids[i]];
                ovr.Excluded = false;
                _controller.SetOverride(guids[i], ovr);
            }
        }

        private void SetAllIncluded(bool included)
        {
            if (_controller.Report == null)
            {
                return;
            }

            for (var i = 0; i < _controller.Report.Textures.Count; i++)
            {
                var guid = _controller.Report.Textures[i].Guid;
                if (string.IsNullOrEmpty(guid))
                {
                    continue;
                }

                TextureOverride ovr;
                if (!_controller.Overrides.TryGetValue(guid, out ovr))
                {
                    ovr = new TextureOverride();
                }

                ovr.Excluded = !included;
                _controller.SetOverride(guid, ovr);
            }
        }

        // ===================================================================== tab: preferences

        private void DrawPreferences()
        {
            EditorGUI.BeginDisabledGroup(!WindowFlow.CanEditSettings(_controller.State));

            Section("ui.section.preferences");

            var includeInactive = EditorGUILayout.Toggle(
                new GUIContent(UiStrings.T("ui.toggle.includeInactive"), UiStrings.T("tip.includeInactive")),
                _controller.IncludeInactive);
            if (includeInactive != _controller.IncludeInactive)
            {
                _controller.IncludeInactive = includeInactive;
                if (_controller.Report != null)
                {
                    _controller.Analyze();
                }
            }

            _controller.MeasureQuality = EditorGUILayout.Toggle(
                new GUIContent(UiStrings.T("ui.toggle.measureQuality"), UiStrings.T("tip.measureQuality")),
                _controller.MeasureQuality);

            _controller.OverwriteGenerated = EditorGUILayout.Toggle(
                new GUIContent(UiStrings.T("ui.toggle.overwriteGenerated")),
                _controller.OverwriteGenerated);

            EditorGUILayout.HelpBox(UiStrings.T("tip.qualityEstimate"), MessageType.None);
            EditorGUILayout.HelpBox(UiStrings.T("tip.qualityMeasure"), MessageType.None);

            EditorGUI.EndDisabledGroup();

            Section("ui.section.output");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(UiStrings.T("ui.label.outputFolder"));
            EditorGUILayout.LabelField(GeneratedAssetStore.GeneratedRoot);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(UiStrings.T("tip.outputFolder"), MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(UiStrings.T("ui.button.openOutputFolder")))
            {
                EditorUtility.RevealInFinder(GeneratedAssetStore.GeneratedRoot);
            }

            if (GUILayout.Button(UiStrings.T("ui.button.exportReport")))
            {
                ExportReport();
            }

            if (GUILayout.Button(UiStrings.T("ui.button.copy")))
            {
                CopySummary();
            }

            EditorGUILayout.EndHorizontal();

            Section("ui.section.advanced");
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, UiStrings.T("ui.section.advanced"));
            if (_showAdvanced)
            {
                EditorGUILayout.LabelField(UiStrings.T("ui.label.toolVersion"), GeneratedAssetStore.ToolVersion);
                EditorGUILayout.LabelField(UiStrings.T("ui.label.engineVersion"), Application.unityVersion);
                EditorGUILayout.LabelField(UiStrings.T("ui.label.dependencies"),
                    _controller.Report != null
                        ? _controller.Report.PossibleBuildTimeDependencies.Count.ToString(CultureInfo.InvariantCulture)
                        : UiStrings.T("ui.label.notAvailable"));
            }
        }

        private void ExportReport()
        {
            try
            {
                var path = EditorUtility.SaveFilePanel(
                    UiStrings.T("report.title"), string.Empty, "avatartuner-report", "txt");
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                File.WriteAllText(path, BuildReportText(), new UTF8Encoding(false));
                ShowNotice("report.fileSaved", path);
            }
            catch (Exception ex)
            {
                _controller.ErrorKey = "error.ioFailed";
                _controller.ErrorArgs.Clear();
                _controller.ErrorArgs.Add(ex.Message);
            }
        }

        private string BuildReportText()
        {
            var builder = new StringBuilder();
            builder.AppendLine(UiStrings.T("report.title"));
            builder.AppendLine(UiStrings.T("report.generatedAt") + ": " +
                               DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture));
            builder.AppendLine(UiStrings.T("report.avatar") + ": " +
                               (_controller.Avatar != null ? _controller.Avatar.name : UiStrings.T("ui.label.notAvailable")));
            builder.AppendLine();
            builder.AppendLine(UiStrings.T("report.summary"));

            var plan = _controller.Plan;
            if (plan != null)
            {
                builder.AppendLine(UiStrings.T("results.gpuMemoryBefore") + ": " +
                                   TextureFormatInfo.FormatBytes(plan.SourceBytesTotal));
                builder.AppendLine(UiStrings.T("results.gpuMemoryAfter") + ": " +
                                   TextureFormatInfo.FormatBytes(plan.TargetBytesTotal));
                builder.AppendLine(UiStrings.T("results.gpuMemorySaved") + ": " +
                                   TextureFormatInfo.FormatBytes(plan.SavingBytes));
            }

            builder.AppendLine();
            builder.AppendLine(UiStrings.T("report.details"));
            if (_controller.Report != null)
            {
                for (var i = 0; i < _controller.Report.Findings.Count; i++)
                {
                    builder.AppendLine("- " + UiStrings.T(
                        _controller.Report.Findings[i].MessageKey,
                        _controller.Report.Findings[i].Args.ToArray()));
                }
            }

            if (_controller.ChangeStatus != null)
            {
                for (var i = 0; i < _controller.ChangeStatus.Changes.Count; i++)
                {
                    builder.AppendLine("- " + UiStrings.T(
                        _controller.ChangeStatus.Changes[i].DetailKey,
                        _controller.ChangeStatus.Changes[i].Args.ToArray()));
                }
            }

            return builder.ToString();
        }

        private void CopySummary()
        {
            EditorGUIUtility.systemCopyBuffer = BuildReportText();
            ShowNotice("opt.done");
        }

        // ===================================================================== shared helpers

        private void Section(string titleKey)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(UiStrings.T(titleKey), EditorStyles.boldLabel);
        }

        private bool PassesFilter(string name, string path)
        {
            if (string.IsNullOrEmpty(_filter))
            {
                return true;
            }

            return (!string.IsNullOrEmpty(name) &&
                    name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (!string.IsNullOrEmpty(path) &&
                    path.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string RiskKey(CompatRisk risk)
        {
            switch (risk)
            {
                case CompatRisk.High: return "compat.riskHigh";
                case CompatRisk.Medium: return "compat.riskMedium";
                default: return "compat.riskLow";
            }
        }

        private static string PresetDescriptionKey(OptimizationPreset preset)
        {
            switch (preset)
            {
                case OptimizationPreset.QualityFirst: return "preset.quality-first.description";
                case OptimizationPreset.SizeFirst: return "preset.size-first.description";
                default: return "preset.balanced.description";
            }
        }

        private static string StatusNameKey(OptimizationStatus status)
        {
            switch (status)
            {
                case OptimizationStatus.Optimized: return "status.optimized.name";
                case OptimizationStatus.ChangesDetected: return "status.changesDetected.name";
                case OptimizationStatus.ReoptimizationRequired: return "status.reoptimizationRequired.name";
                case OptimizationStatus.OptimizationFailed: return "status.optimizationFailed.name";
                case OptimizationStatus.Unsupported: return "status.unsupported.name";
                default: return "status.notOptimized.name";
            }
        }

        private static string StatusSummaryKey(OptimizationStatus status)
        {
            switch (status)
            {
                case OptimizationStatus.Optimized: return "status.optimized.summary";
                case OptimizationStatus.ChangesDetected: return "status.changesDetected.summary";
                case OptimizationStatus.ReoptimizationRequired: return "status.reoptimizationRequired.summary";
                case OptimizationStatus.OptimizationFailed: return "status.optimizationFailed.summary";
                case OptimizationStatus.Unsupported: return "status.unsupported.summary";
                default: return "status.notOptimized.summary";
            }
        }

        private static int CountUniqueMeshes(AvatarAnalysisReport report)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < report.Renderers.Count; i++)
            {
                if (!string.IsNullOrEmpty(report.Renderers[i].MeshGuid))
                {
                    seen.Add(report.Renderers[i].MeshGuid);
                }
            }

            return seen.Count;
        }

        private static int CountClips(AvatarAnalysisReport report)
        {
            var total = 0;
            for (var i = 0; i < report.Animators.Count; i++)
            {
                total += report.Animators[i].ClipCount;
            }

            return total;
        }

        private static int FirstBoneCount(AvatarAnalysisReport report)
        {
            for (var i = 0; i < report.Renderers.Count; i++)
            {
                if (report.Renderers[i].IsSkinned)
                {
                    return report.Renderers[i].BoneCount;
                }
            }

            return 0;
        }
    }
}
