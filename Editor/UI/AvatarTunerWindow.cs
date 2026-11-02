// AvatarTuner v1.0.0 — the editor window (UI Toolkit).
//
// A polished, localized editor tool over the existing engines. Design rules:
//   - Sidebar navigation (compact wrap layout when the window is narrow): no truncated or
//     overlapping tabs in any dock configuration.
//   - A dashboard of real metrics and actionable insights replaces raw summary text.
//   - Semantic colors (see UiTheme) always pair with a text label; nothing is color-only.
//   - One clear primary action per workflow step: Select Avatar > Analyze > Review Changes
//     > Optimize > Results. Disabled actions explain themselves instead of being dead.
//   - Every control calls a real implementation; nothing decorative, nothing fake.
//   - Non-destructive by construction: generated copies only, source assets never touched.
//   - Fully localized (en / th / zh-Hans) with no hardcoded display text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;
using AvatarTuner.Editor.Tracking;
using AvatarTuner.Editor.ViewModels;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarTuner.Editor.UI
{
    public sealed class AvatarTunerWindow : EditorWindow
    {
        private const int ModuleOverview = 0;
        private const int ModuleTextures = 1;
        private const int ModuleMaterials = 2;
        private const int ModuleCompatibility = 3;
        private const int ModuleTracking = 4;
        private const int ModuleResults = 5;
        private const int ModuleSettings = 6;

        private static readonly string[] ModuleLabelKeys =
        {
            "ui.tab.overview",
            "ui.tab.textures",
            "ui.tab.materials",
            "compat.title",
            "ui.tab.tracking",
            "ui.tab.results",
            "ui.tab.settings"
        };

        private readonly AvatarTunerController _controller = new AvatarTunerController();

        private int _module = ModuleOverview;
        private string _filter = string.Empty;
        private TextureSortMode _sortMode = TextureSortMode.Vram;
        private bool _cancelRequested;
        private bool _compactNav;

        private string _noticeKey;
        private readonly List<string> _noticeArgs = new List<string>();

        private VisualElement _root;
        private VisualElement _nav;
        private VisualElement _navItems;
        private VisualElement _content;
        private VisualElement _statusArea;
        private VisualElement _actionArea;

        // ===================================================================== entry point

        [MenuItem("Tools/AvatarTuner")]
        public static void Open()
        {
            var window = GetWindow<AvatarTunerWindow>();
            window.titleContent = new GUIContent(UiStrings.T("ui.window.title"));
            window.minSize = new Vector2(420, 520);
            window.Show();
        }

        private void CreateGUI()
        {
            BuildShell();
            RefreshView();
        }

        // ===================================================================== shell

        private void BuildShell()
        {
            _root = rootVisualElement;
            _root.Clear();
            _root.AddToClassList("at-root");
            _root.style.color = UiTheme.Text;

            TryLoadStylesheet();

            _root.Add(BuildWindowHeader());

            _statusArea = new VisualElement();
            _root.Add(_statusArea);

            _actionArea = new VisualElement();
            _root.Add(_actionArea);

            var body = new VisualElement();
            body.AddToClassList("at-row-top");
            body.style.flexGrow = 1;

            _nav = new VisualElement();
            _nav.AddToClassList("at-nav");
            _navItems = new VisualElement();
            _nav.Add(_navItems);
            body.Add(_nav);

            var contentScroll = new ScrollView();
            contentScroll.AddToClassList("at-grow");
            _content = new VisualElement();
            _content.AddToClassList("at-content");
            contentScroll.Add(_content);
            body.Add(contentScroll);

            _root.Add(body);

            // Responsive navigation: sidebar while wide, wrapping chip bar when narrow, so
            // labels never overlap or get cut off.
            _root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        private void TryLoadStylesheet()
        {
            try
            {
                var guids = AssetDatabase.FindAssets("AvatarTunerWindow t:StyleSheet");
                for (var i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!path.EndsWith("Editor/UI/AvatarTunerWindow.uss", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                    if (sheet != null)
                    {
                        _root.styleSheets.Add(sheet);
                    }

                    return;
                }
            }
            catch (Exception)
            {
                // Styling is presentation only; the window stays fully usable without it.
            }
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            var narrow = evt.newRect.width > 0 && evt.newRect.width < 720;
            if (narrow == _compactNav)
            {
                return;
            }

            _compactNav = narrow;
            _nav.EnableInClassList("compact", narrow);
            if (narrow)
            {
                _nav.style.width = StyleKeyword.Auto;
                _nav.style.marginRight = 0;
            }
            else
            {
                _nav.style.width = 186;
                _nav.style.marginRight = 8;
            }
        }

        private VisualElement BuildWindowHeader()
        {
            var header = new VisualElement();
            header.AddToClassList("at-window-header");

            var title = new Label(UiStrings.T("ui.window.title"));
            title.AddToClassList("at-app-title");
            header.Add(title);

            var version = new Label("v" + GeneratedAssetStore.ToolVersion);
            version.AddToClassList("at-app-tag");
            version.style.color = UiTheme.TextSecondary;
            version.style.backgroundColor = UiTheme.ChipBackground;
            header.Add(version);

            header.Add(Spacer());

            header.Add(BuildLanguagePicker());
            return header;
        }

        private VisualElement BuildLanguagePicker()
        {
            var row = new VisualElement();
            row.AddToClassList("at-row");

            var label = new Label(UiStrings.T("ui.label.language"));
            label.AddToClassList("at-text");
            label.style.color = UiTheme.TextSecondary;
            row.Add(label);

            var names = new List<string>();
            var current = 0;
            for (var i = 0; i < UiStrings.Languages.Length; i++)
            {
                names.Add(UiStrings.LanguageName(UiStrings.Languages[i]));
                if (UiStrings.Languages[i] == UiStrings.Language)
                {
                    current = i;
                }
            }

            var picker = new DropdownField();
            picker.choices = names;
            picker.value = names[current];
            picker.AddToClassList("at-field");
            picker.RegisterValueChangedCallback(evt =>
            {
                for (var i = 0; i < UiStrings.Languages.Length; i++)
                {
                    if (UiStrings.LanguageName(UiStrings.Languages[i]) == evt.newValue)
                    {
                        UiStrings.Language = UiStrings.Languages[i];
                        titleContent = new GUIContent(UiStrings.T("ui.window.title"));
                        RebuildEverything();
                        return;
                    }
                }
            });
            row.Add(picker);
            return row;
        }

        private void RebuildEverything()
        {
            BuildShell();
            RefreshView();
        }

        // ===================================================================== refresh

        private void RefreshView()
        {
            if (_root == null)
            {
                return;
            }

            BuildNav();
            BuildActions();
            BuildStatus();

            _content.Clear();
            try
            {
                switch (_module)
                {
                    case ModuleTextures: BuildTexturesModule(_content); break;
                    case ModuleMaterials: BuildMaterialsModule(_content); break;
                    case ModuleCompatibility: BuildCompatibilityModule(_content); break;
                    case ModuleTracking: BuildTrackingModule(_content); break;
                    case ModuleResults: BuildResultsModule(_content); break;
                    case ModuleSettings: BuildSettingsModule(_content); break;
                    default: BuildOverviewModule(_content); break;
                }
            }
            catch (Exception ex)
            {
                _content.Add(Message(UiStrings.T("error.unexpected", ex.Message), UiTheme.Error));
            }
        }

        // ===================================================================== navigation

        private void BuildNav()
        {
            _navItems.Clear();
            var badgeCounts = new[] { -1, RowCount(ModuleTextures), -1, -1, -1, -1, -1 };

            for (var i = 0; i < ModuleLabelKeys.Length; i++)
            {
                var index = i;
                var selected = index == _module;

                var item = new Button(() =>
                {
                    _module = index;
                    RefreshView();
                });
                item.AddToClassList("at-nav-item");
                item.style.backgroundColor = selected ? UiTheme.SelectedBackground : Color.clear;

                var dot = new VisualElement();
                dot.AddToClassList("at-dot");
                dot.style.backgroundColor = selected ? UiTheme.Accent : UiTheme.Neutral;
                item.Add(dot);

                var text = new Label(UiStrings.T(ModuleLabelKeys[i]));
                text.AddToClassList("at-nav-item-label");
                text.style.color = selected ? UiTheme.Text : UiTheme.TextSecondary;
                text.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
                item.Add(text);

                if (badgeCounts[i] >= 0)
                {
                    var badge = new Label(badgeCounts[i].ToString(CultureInfo.InvariantCulture));
                    badge.AddToClassList("at-nav-item-count");
                    badge.style.color = UiTheme.TextSecondary;
                    item.Add(badge);
                }

                _navItems.Add(item);
            }
        }

        private int RowCount(int module)
        {
            if (_controller.Report == null)
            {
                return -1;
            }

            switch (module)
            {
                case ModuleTextures: return _controller.Report.Textures.Count;
                default: return -1;
            }
        }

        // ===================================================================== actions & workflow

        private void BuildActions()
        {
            _actionArea.Clear();
            _actionArea.Add(BuildWorkflowBar());
        }

        private VisualElement BuildWorkflowBar()
        {
            var card = Card(null);

            card.Add(BuildBreadcrumb());

            var row = new VisualElement();
            row.AddToClassList("at-row");
            row.style.marginTop = 6;

            var state = _controller.State;
            var hasPlan = _controller.Plan != null;
            var planHasChanges = _controller.Plan != null && _controller.Plan.HasChanges;
            var hasResults = _controller.Generation != null;
            var hasAvatar = _controller.Avatar != null;

            var primary = WorkflowModel.NextAction(state, hasAvatar, hasPlan, planHasChanges, hasResults);
            var optimizeReason = WorkflowModel.OptimizeDisabledReasonKey(
                state, hasAvatar, hasPlan, planHasChanges, _controller.CompatibilityAllows);
            var analyzeReason = WorkflowModel.AnalyzeDisabledReasonKey(state, hasAvatar);

            row.Add(BuildPrimaryButton(primary, optimizeReason, analyzeReason));
            row.Add(Spacer());
            row.Add(SecondaryButton("ui.button.refresh", () => { _controller.RefreshStatus(); RefreshView(); }));
            row.Add(SecondaryButton("ui.button.exportReport", ExportReport));
            row.Add(SecondaryButton("ui.button.copy", CopySummary));
            row.Add(BuildCleanupButton());

            card.Add(row);

            var hint = WorkflowHint(primary, optimizeReason, analyzeReason);
            if (!string.IsNullOrEmpty(hint))
            {
                var hintLabel = new Label(UiStrings.T(hint));
                hintLabel.AddToClassList("at-hint");
                hintLabel.style.color = UiTheme.TextSecondary;
                card.Add(hintLabel);
            }

            return card;
        }

        private VisualElement BuildBreadcrumb()
        {
            var row = new VisualElement();
            row.AddToClassList("at-row");

            var active = WorkflowModel.ActiveStep(_controller.State, _controller.Avatar != null,
                _controller.Generation != null);

            for (var i = 0; i < WorkflowModel.StepLabelKeys.Length; i++)
            {
                if (i > 0)
                {
                    var sep = new Label("›");
                    sep.AddToClassList("at-step-sep");
                    sep.style.color = UiTheme.Neutral;
                    row.Add(sep);
                }

                var step = new Label(UiStrings.T(WorkflowModel.StepLabelKeys[i]));
                step.AddToClassList("at-step");
                if (i == active)
                {
                    step.style.backgroundColor = UiTheme.Accent;
                    step.style.color = UiTheme.TextOnAccent;
                    step.style.unityFontStyleAndWeight = FontStyle.Bold;
                }
                else if (i < active)
                {
                    step.style.backgroundColor = UiTheme.ChipBackground;
                    step.style.color = UiTheme.Success;
                }
                else
                {
                    step.style.backgroundColor = UiTheme.ChipBackground;
                    step.style.color = UiTheme.TextSecondary;
                }

                row.Add(step);
            }

            return row;
        }

        private VisualElement BuildPrimaryButton(PrimaryAction action, string optimizeReason, string analyzeReason)
        {
            if (action == PrimaryAction.SelectAvatar)
            {
                var note = new Label(UiStrings.T("ui.hint.selectAvatar"));
                note.AddToClassList("at-text-strong");
                note.style.color = UiTheme.Accent;
                return note;
            }

            string labelKey;
            Action onClick;
            var enabled = true;
            string reasonKey = null;

            switch (action)
            {
                case PrimaryAction.Analyze:
                    labelKey = "ui.button.analyze";
                    onClick = RunAnalyze;
                    enabled = analyzeReason == null;
                    reasonKey = analyzeReason;
                    break;
                case PrimaryAction.ReviewChanges:
                    labelKey = "ui.button.preview";
                    onClick = () => { _module = ModuleTextures; RefreshView(); };
                    break;
                case PrimaryAction.Optimize:
                case PrimaryAction.Regenerate:
                    labelKey = action == PrimaryAction.Regenerate ? "ui.button.regenerate" : "ui.button.optimize";
                    onClick = RunOptimize;
                    enabled = optimizeReason == null;
                    reasonKey = optimizeReason;
                    break;
                default:
                    labelKey = "ui.tab.results";
                    onClick = () => { _module = ModuleResults; RefreshView(); };
                    break;
            }

            var button = new Button(onClick) { text = UiStrings.T(labelKey) };
            button.AddToClassList("at-primary-btn");
            button.style.backgroundColor = UiTheme.Accent;
            button.style.color = UiTheme.TextOnAccent;
            button.SetEnabled(enabled);
            if (!enabled && !string.IsNullOrEmpty(reasonKey))
            {
                button.tooltip = UiStrings.T(reasonKey);
            }
            else if (action == PrimaryAction.Analyze)
            {
                button.tooltip = UiStrings.T("tip.analyzeButton");
            }
            else if (action == PrimaryAction.Optimize || action == PrimaryAction.Regenerate)
            {
                button.tooltip = UiStrings.T("tip.optimizeButton");
            }

            return button;
        }

        private static string WorkflowHint(PrimaryAction primary, string optimizeReason, string analyzeReason)
        {
            switch (primary)
            {
                case PrimaryAction.Analyze:
                    return analyzeReason;
                case PrimaryAction.Optimize:
                case PrimaryAction.Regenerate:
                    return optimizeReason;
                default:
                    return null;
            }
        }

        private VisualElement BuildCleanupButton()
        {
            var enabled = WindowFlow.CanCleanup(_controller.State, _controller.HasRecordedOutputs);
            var button = new Button(RunCleanup) { text = UiStrings.T("ui.button.cleanup") };
            button.AddToClassList("at-danger-btn");
            button.style.backgroundColor = UiTheme.Error;
            button.style.color = UiTheme.TextOnAccent;
            button.SetEnabled(enabled);
            button.tooltip = UiStrings.T("tip.cleanup");
            return button;
        }

        private Button SecondaryButton(string labelKey, Action onClick)
        {
            var button = new Button(onClick) { text = UiStrings.T(labelKey) };
            button.AddToClassList("at-btn");
            button.style.backgroundColor = UiTheme.ChipBackground;
            button.style.color = UiTheme.Text;
            button.SetEnabled(WindowFlow.CanEditSettings(_controller.State));
            return button;
        }

        // ===================================================================== status area

        private void BuildStatus()
        {
            _statusArea.Clear();

            if (!string.IsNullOrEmpty(_controller.ErrorKey))
            {
                _statusArea.Add(Message(
                    UiStrings.T(_controller.ErrorKey, _controller.ErrorArgs.ToArray()), UiTheme.Error));
            }

            if (!string.IsNullOrEmpty(_noticeKey))
            {
                _statusArea.Add(Message(UiStrings.T(_noticeKey, _noticeArgs.ToArray()), UiTheme.Success));
            }

            _statusArea.Add(BuildStateBanner());
        }

        private VisualElement BuildStateBanner()
        {
            string titleKey;
            string bodyKey;
            Color tone;

            switch (_controller.State)
            {
                case WindowState.NoSelection:
                    titleKey = "ui.state.noSelection.title";
                    bodyKey = "ui.state.noSelection.body";
                    tone = UiTheme.Info;
                    break;
                case WindowState.Ready:
                    titleKey = "ui.state.empty.title";
                    bodyKey = "ui.state.empty.body";
                    tone = UiTheme.Info;
                    break;
                case WindowState.Analyzing:
                case WindowState.Optimizing:
                    titleKey = "ui.state.loading.title";
                    bodyKey = "ui.state.loading.body";
                    tone = UiTheme.Accent;
                    break;
                case WindowState.Done:
                    titleKey = "ui.state.success.title";
                    bodyKey = "ui.state.success.body";
                    tone = UiTheme.Success;
                    break;
                case WindowState.Error:
                    titleKey = "ui.state.error.title";
                    bodyKey = "ui.state.error.body";
                    tone = UiTheme.Warning;
                    break;
                default:
                    return null;
            }

            var card = Card(null);
            card.style.backgroundColor = UiTheme.CardHeaderBackground;
            var row = new VisualElement();
            row.AddToClassList("at-insight");
            row.Add(Dot(tone));
            var col = new VisualElement();
            col.AddToClassList("at-col");
            var title = new Label(UiStrings.T(titleKey));
            title.AddToClassList("at-text-strong");
            col.Add(title);
            var body = new Label(UiStrings.T(bodyKey));
            body.AddToClassList("at-text-secondary");
            body.style.color = UiTheme.TextSecondary;
            col.Add(body);
            row.Add(col);
            card.Add(row);
            return card;
        }

        // ===================================================================== module: overview

        private void BuildOverviewModule(VisualElement container)
        {
            if (_controller.Avatar == null && _controller.Report == null)
            {
                container.Add(EmptyState());
                return;
            }

            container.Add(BuildAvatarCard());
            container.Add(BuildDashboardCard());

            var insights = DashboardModel.BuildInsights(
                _controller.Report, _controller.Plan,
                _controller.Report != null ? _controller.Report.Compatibility : null);
            container.Add(BuildInsightsCard(insights));

            if (_controller.Plan != null)
            {
                container.Add(BuildPlanSummaryCard());
            }
        }

        private VisualElement BuildAvatarCard()
        {
            var card = Card("ui.section.avatarSelection");

            var nameRow = new VisualElement();
            nameRow.AddToClassList("at-row");
            var nameLabel = new Label(_controller.Avatar != null
                ? _controller.Avatar.name
                : UiStrings.T("ui.label.notAvailable"));
            nameLabel.AddToClassList("at-text-strong");
            nameLabel.style.color = UiTheme.Accent;
            nameRow.Add(nameLabel);
            nameRow.Add(Spacer());

            var analysisTone = _controller.Report != null && _controller.Report.ScanSucceeded
                ? SemanticTone.Success
                : (_controller.State == WindowState.Analyzing ? SemanticTone.Info : SemanticTone.Neutral);
            nameRow.Add(Chip(
                UiStrings.T("ui.label.analysisStatus") + ": " + UiStrings.T(
                    _controller.Report != null && _controller.Report.ScanSucceeded
                        ? "ui.label.analyzed"
                        : "ui.label.notAnalyzed"),
                analysisTone));

            var lastStatus = _controller.RecordedState != null
                ? DashboardModel.StatusNameKey(_controller.RecordedState.Status)
                : "status.notOptimized.name";
            nameRow.Add(Chip(UiStrings.T("ui.label.lastOptimized") + ": " + UiStrings.T(lastStatus),
                DashboardModel.StatusTone(_controller.RecordedState != null
                    ? _controller.RecordedState.Status
                    : OptimizationStatus.NotOptimized)));
            card.Add(nameRow);

            var fieldRow = new VisualElement();
            fieldRow.AddToClassList("at-row");
            fieldRow.style.marginTop = 6;

            var field = new ObjectField();
            field.label = UiStrings.T("ui.label.avatar");
            field.objectType = typeof(GameObject);
            field.allowSceneObjects = true;
            field.value = _controller.Avatar;
            field.tooltip = UiStrings.T("tip.avatarField");
            field.AddToClassList("at-grow");
            field.RegisterValueChangedCallback(evt =>
            {
                _controller.SelectAvatar(evt.newValue as GameObject);
                ClearNotice();
                RefreshView();
            });
            fieldRow.Add(field);

            var advanced = new Foldout();
            advanced.text = UiStrings.T("ui.section.advanced");
            advanced.value = false;
            advanced.AddToClassList("at-details");

            var isScene = _controller.Avatar != null && string.IsNullOrEmpty(
                AssetDatabase.GetAssetPath(_controller.Avatar));
            advanced.Add(DetailRow("ui.label.status", isScene
                ? UiStrings.T("ui.label.sceneObject")
                : UiStrings.T("ui.label.avatarPath")));
            advanced.Add(DetailRow("ui.label.avatarPath",
                string.IsNullOrEmpty(_controller.RootKey)
                    ? UiStrings.T("ui.label.notAvailable")
                    : _controller.RootKey));
            if (_controller.RecordedState != null)
            {
                advanced.Add(DetailRow("ui.label.toolVersion",
                    _controller.RecordedState.ToolVersionAtOptimization));
                advanced.Add(DetailRow("ui.label.engineVersion",
                    _controller.RecordedState.EngineVersionAtOptimization));
                advanced.Add(DetailRow("ui.label.lastOptimized",
                    string.IsNullOrEmpty(_controller.RecordedState.LastSuccessUtc)
                        ? UiStrings.T("ui.label.notAvailable")
                        : _controller.RecordedState.LastSuccessUtc));
            }

            card.Add(fieldRow);
            card.Add(advanced);
            return card;
        }

        private VisualElement BuildDashboardCard()
        {
            var card = Card("ui.section.dashboard");
            var metrics = DashboardModel.BuildMetrics(
                _controller.Report,
                _controller.Plan,
                _controller.ChangeStatus != null
                    ? _controller.ChangeStatus.Status
                    : (_controller.RecordedState != null
                        ? _controller.RecordedState.Status
                        : OptimizationStatus.NotOptimized));

            var grid = new VisualElement();
            grid.AddToClassList("at-metric-grid");
            for (var i = 0; i < metrics.Count; i++)
            {
                grid.Add(Metric(metrics[i]));
            }

            card.Add(grid);
            return card;
        }

        private VisualElement Metric(MetricCard metric)
        {
            var card = new VisualElement();
            card.AddToClassList("at-metric-card");
            card.style.backgroundColor = UiTheme.CardBackground;
            card.style.borderLeftColor = UiTheme.ForTone(metric.Tone);
            card.style.borderRightColor = UiTheme.CardBorder;
            card.style.borderTopColor = UiTheme.CardBorder;
            card.style.borderBottomColor = UiTheme.CardBorder;
            card.style.borderLeftWidth = 3;

            var label = new Label(UiStrings.T(metric.LabelKey));
            label.AddToClassList("at-metric-label");
            label.style.color = UiTheme.TextSecondary;
            card.Add(label);

            var value = new Label(string.IsNullOrEmpty(metric.ValueKey) ? metric.Value : UiStrings.T(metric.ValueKey));
            value.AddToClassList(metric.Emphasis == MetricEmphasis.Primary ? "at-metric-value" : "at-metric-value-sm");
            value.style.color = metric.Emphasis == MetricEmphasis.Primary
                ? UiTheme.ForTone(metric.Tone)
                : UiTheme.Text;
            card.Add(value);

            var subText = metric.SubTextKey != null ? UiStrings.T(metric.SubTextKey) : metric.SubText;
            if (!string.IsNullOrEmpty(subText))
            {
                var sub = new Label(subText);
                sub.AddToClassList("at-metric-sub");
                sub.style.color = UiTheme.TextSecondary;
                card.Add(sub);
            }

            return card;
        }

        private VisualElement BuildInsightsCard(List<InsightItem> insights)
        {
            var card = Card("ui.section.insights");
            for (var i = 0; i < insights.Count; i++)
            {
                var insight = insights[i];
                var row = new VisualElement();
                row.AddToClassList("at-insight");
                row.Add(Dot(UiTheme.ForTone(insight.Tone)));

                var text = new Label(UiStrings.T(insight.MessageKey, insight.Args.ToArray()));
                text.AddToClassList("at-text");
                text.style.color = insight.Tone == SemanticTone.Neutral
                    ? UiTheme.TextSecondary
                    : UiTheme.Text;
                row.Add(text);
                card.Add(row);
            }

            return card;
        }

        private VisualElement BuildPlanSummaryCard()
        {
            var plan = _controller.Plan;
            var card = Card("ui.section.texturePlan");

            card.Add(Line(UiStrings.T("results.texturePlanSummary",
                plan.ChangedCount.ToString(CultureInfo.InvariantCulture),
                plan.UnchangedCount.ToString(CultureInfo.InvariantCulture),
                plan.ExcludedCount.ToString(CultureInfo.InvariantCulture))));

            var savings = new VisualElement();
            savings.AddToClassList("at-row");
            savings.style.marginTop = 4;
            savings.Add(BigValue(UiStrings.T("results.gpuMemoryBefore"),
                TextureFormatInfo.FormatBytes(plan.SourceBytesTotal), UiTheme.TextSecondary));
            savings.Add(BigValue("→", string.Empty, UiTheme.Neutral));
            savings.Add(BigValue(UiStrings.T("results.gpuMemoryAfter"),
                TextureFormatInfo.FormatBytes(plan.TargetBytesTotal), UiTheme.Accent));
            savings.Add(BigValue(UiStrings.T("results.gpuMemorySaved"),
                TextureFormatInfo.FormatBytes(plan.SavingBytes) + " (" +
                plan.SavingPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%)",
                plan.SavingBytes > 0 ? UiTheme.Success : UiTheme.TextSecondary));
            card.Add(savings);

            card.Add(Message(UiStrings.T("results.basisComputed"), UiTheme.Neutral));
            return card;
        }

        // ===================================================================== module: textures

        private void BuildTexturesModule(VisualElement container)
        {
            if (_controller.Report == null)
            {
                container.Add(EmptyState());
                return;
            }

            container.Add(BuildAvatarCard());

            var toolbar = new VisualElement();
            toolbar.AddToClassList("at-row");
            toolbar.style.marginBottom = 6;

            var filterField = new TextField();
            filterField.label = UiStrings.T("ui.label.searchPlaceholder");
            filterField.value = _filter;
            filterField.tooltip = UiStrings.T("tip.exclusionList");
            filterField.AddToClassList("at-field");
            filterField.style.minWidth = 160;
            filterField.RegisterValueChangedCallback(evt =>
            {
                _filter = evt.newValue ?? string.Empty;
                RefreshView();
            });
            toolbar.Add(filterField);

            var sortPicker = new DropdownField();
            sortPicker.label = UiStrings.T("ui.label.sort");
            sortPicker.choices = new List<string>
            {
                UiStrings.T("ui.label.fileName"),
                UiStrings.T("ui.label.gpuMemory"),
                UiStrings.T("ui.label.saving"),
                UiStrings.T("ui.label.status")
            };
            sortPicker.value = sortPicker.choices[(int)_sortMode];
            sortPicker.AddToClassList("at-field");
            sortPicker.RegisterValueChangedCallback(evt =>
            {
                for (var i = 0; i < sortPicker.choices.Count; i++)
                {
                    if (sortPicker.choices[i] == evt.newValue)
                    {
                        _sortMode = (TextureSortMode)i;
                        RefreshView();
                        return;
                    }
                }
            });
            toolbar.Add(sortPicker);

            container.Add(toolbar);

            var rows = TextureRows.BuildAll(_controller.Report, _controller.Plan);
            rows = TextureRows.Filter(rows, _filter);
            TextureRows.Sort(rows, _sortMode);

            container.Add(BuildTextureTableHeader());

            var shown = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                if (shown >= 200)
                {
                    container.Add(Line(UiStrings.T("ui.label.moreItems",
                        (rows.Count - shown).ToString(CultureInfo.InvariantCulture))));
                    break;
                }

                container.Add(BuildTextureRow(rows[i]));
                shown++;
            }

            if (rows.Count == 0)
            {
                container.Add(Line(UiStrings.T("ui.label.tableEmpty")));
            }
        }

        private VisualElement BuildTextureTableHeader()
        {
            var row = new VisualElement();
            row.AddToClassList("at-row");
            row.Add(HeaderCell("at-col-name", UiStrings.T("ui.label.sourceAsset")));
            row.Add(HeaderCell("at-col-w70", UiStrings.T("ui.label.resolution")));
            row.Add(HeaderCell("at-col-w110", UiStrings.T("ui.label.format")));
            row.Add(HeaderCell("at-col-w95", UiStrings.T("ui.label.gpuMemory")));
            row.Add(HeaderCell("at-col-w80", UiStrings.T("ui.label.saving")));
            row.Add(HeaderCell("at-col-w110", UiStrings.T("ui.label.qualityRisk")));
            row.Add(HeaderCell("at-col-w110", UiStrings.T("ui.label.status")));
            row.Add(HeaderCell("at-col-w60", UiStrings.T("ui.label.include")));
            return row;
        }

        private VisualElement BuildTextureRow(TextureRowModel model)
        {
            var row = new VisualElement();
            row.AddToClassList("at-row-card");
            row.style.backgroundColor = UiTheme.CardBackground;
            row.style.borderLeftColor = UiTheme.ForTone(TextureRowModel.StatusTone(model.Status));
            row.style.borderRightColor = UiTheme.CardBorder;
            row.style.borderTopColor = UiTheme.CardBorder;
            row.style.borderBottomColor = UiTheme.CardBorder;
            row.style.borderLeftWidth = 3;

            row.Add(Thumbnail(model.Path));

            var nameCol = new VisualElement();
            nameCol.AddToClassList("at-col-name");
            var name = new Label(model.Name);
            name.AddToClassList("at-text-strong");
            nameCol.Add(name);
            var path = new Label(model.Path ?? string.Empty);
            path.AddToClassList("at-text-secondary");
            path.style.color = UiTheme.TextSecondary;
            nameCol.Add(path);
            row.Add(nameCol);

            row.Add(Cell("at-col-w70", model.Width + "×" + model.Height, UiTheme.TextSecondary));
            row.Add(Cell("at-col-w110", FormatCell(model), UiTheme.Text));
            row.Add(Cell("at-col-w95", TextureFormatInfo.FormatBytes(model.SourceBytes), UiTheme.Text));
            row.Add(Cell("at-col-w80", SavingCell(model),
                model.SavingBytes > 0 ? UiTheme.Success : UiTheme.TextSecondary));
            row.Add(RiskCell("at-col-w110", model.Risk));
            row.Add(StatusCell("at-col-w110", model.Status));
            row.Add(IncludeCell(model));

            var detail = new Foldout();
            detail.text = UiStrings.T("ui.label.detail");
            detail.value = false;
            detail.AddToClassList("at-details");
            detail.Add(DetailRow("ui.label.usage", model.Usage.ToString()));
            detail.Add(DetailRow("ui.label.materials",
                model.MaterialCount.ToString(CultureInfo.InvariantCulture)));
            if (!string.IsNullOrEmpty(model.SkipReasonKey))
            {
                detail.Add(DetailRow("ui.label.reason", UiStrings.T(model.SkipReasonKey)));
            }

            for (var n = 0; n < model.Notes.Count; n++)
            {
                detail.Add(Line("• " + UiStrings.T(model.Notes[n].Key, model.Notes[n].Args.ToArray())));
            }

            if (model.IsProtected)
            {
                detail.Add(Message(UiStrings.T("plan.note.protectedThirdParty"), UiTheme.Warning));
            }

            var ping = SecondaryButton("ui.button.ping", () => PingPath(model.Path));
            ping.SetEnabled(true);
            detail.Add(ping);
            row.Add(detail);
            return row;
        }

        private static string FormatCell(TextureRowModel model)
        {
            var current = model.FormatKnown && !string.IsNullOrEmpty(model.FormatText)
                ? model.FormatText
                : "—";
            return string.IsNullOrEmpty(model.TargetFormatText)
                ? current
                : current + " → " + model.TargetFormatText;
        }

        private static string SavingCell(TextureRowModel model)
        {
            if (!model.HasChanges || model.SavingBytes <= 0)
            {
                return "—";
            }

            return TextureFormatInfo.FormatBytes(model.SavingBytes) + " (" +
                   model.SavingPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%)";
        }

        private VisualElement Thumbnail(string path)
        {
            var holder = new VisualElement();
            holder.AddToClassList("at-thumb");
            try
            {
                if (!string.IsNullOrEmpty(path))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    var preview = asset != null ? AssetPreview.GetMiniThumbnail(asset) : null;
                    if (preview != null)
                    {
                        var image = new Image();
                        image.image = preview;
                        image.scaleMode = ScaleMode.ScaleToFit;
                        image.style.width = 30;
                        image.style.height = 30;
                        holder.Add(image);
                    }
                }
            }
            catch (Exception)
            {
                // A missing thumbnail never breaks the list.
            }

            return holder;
        }

        // ===================================================================== module: materials

        private void BuildMaterialsModule(VisualElement container)
        {
            if (_controller.Report == null)
            {
                container.Add(EmptyState());
                return;
            }

            var report = _controller.Report;

            var materials = Card("ui.label.materials");
            materials.Add(HeaderRow(new[]
            {
                ("at-col-name", UiStrings.T("ui.label.sourceAsset")),
                ("at-col-w130", UiStrings.T("ui.label.shader")),
                ("at-col-w70", UiStrings.T("ui.label.textures"))
            }));
            for (var i = 0; i < report.Materials.Count; i++)
            {
                var material = report.Materials[i];
                var row = new VisualElement();
                row.AddToClassList("at-row");
                row.Add(Cell("at-col-name", material.Name ?? material.Guid, UiTheme.Text));
                row.Add(Cell("at-col-w130",
                    string.IsNullOrEmpty(material.ShaderName) ? UiStrings.T("ui.label.unknown") : material.ShaderName,
                    material.ShaderIsCustom ? UiTheme.Warning : UiTheme.TextSecondary));
                row.Add(Cell("at-col-w70",
                    material.TextureBindings.Count.ToString(CultureInfo.InvariantCulture), UiTheme.TextSecondary));
                materials.Add(row);
            }

            if (report.Materials.Count == 0)
            {
                materials.Add(Line(UiStrings.T("ui.label.tableEmpty")));
            }

            container.Add(materials);

            var meshes = Card("ui.label.meshes");
            meshes.Add(HeaderRow(new[]
            {
                ("at-col-name", UiStrings.T("ui.label.sourceAsset")),
                ("at-col-w80", UiStrings.T("ui.label.triangles")),
                ("at-col-w70", UiStrings.T("ui.label.bones")),
                ("at-col-w80", UiStrings.T("ui.label.blendShapes"))
            }));
            for (var i = 0; i < report.Renderers.Count; i++)
            {
                var renderer = report.Renderers[i];
                var row = new VisualElement();
                row.AddToClassList("at-row");
                row.Add(Cell("at-col-name", renderer.Path, UiTheme.Text));
                row.Add(Cell("at-col-w80", renderer.TriangleCount.ToString(CultureInfo.InvariantCulture), UiTheme.Text));
                row.Add(Cell("at-col-w70", renderer.BoneCount.ToString(CultureInfo.InvariantCulture), UiTheme.TextSecondary));
                row.Add(Cell("at-col-w80", renderer.BlendShapeCount.ToString(CultureInfo.InvariantCulture), UiTheme.TextSecondary));
                meshes.Add(row);
            }

            if (report.Renderers.Count == 0)
            {
                meshes.Add(Line(UiStrings.T("ui.label.tableEmpty")));
            }

            container.Add(meshes);
        }

        // ===================================================================== module: compatibility

        private void BuildCompatibilityModule(VisualElement container)
        {
            if (_controller.Report == null)
            {
                container.Add(EmptyState());
                return;
            }

            var model = CompatibilityGrouper.Build(_controller.Report.Compatibility);
            if (!model.HasAnything)
            {
                var card = Card("compat.title");
                card.Add(Message(UiStrings.T("compat.noneFound"), UiTheme.Success));
                container.Add(card);
                return;
            }

            if (model.Blockers.Count > 0)
            {
                var conflicts = Card("ui.group.conflicts");
                for (var i = 0; i < model.Blockers.Count; i++)
                {
                    conflicts.Add(Message(
                        UiStrings.T(model.Blockers[i].MessageKey, model.Blockers[i].Args.ToArray()),
                        UiTheme.Error));
                }

                container.Add(conflicts);
            }

            for (var i = 0; i < model.Tools.Count; i++)
            {
                container.Add(BuildToolGroup(model.Tools[i]));
            }

            if (model.Systems.Count > 0)
            {
                var systems = Card("ui.group.systems");
                for (var i = 0; i < model.Systems.Count; i++)
                {
                    systems.Add(Message(
                        UiStrings.T(model.Systems[i].MessageKey, model.Systems[i].Args.ToArray()),
                        UiTheme.Info));
                }

                container.Add(systems);
            }

            if (model.GeneralWarnings.Count > 0)
            {
                var general = Card("compat.title");
                for (var i = 0; i < model.GeneralWarnings.Count; i++)
                {
                    general.Add(Message(
                        UiStrings.T(model.GeneralWarnings[i].MessageKey, model.GeneralWarnings[i].Args.ToArray()),
                        UiTheme.Warning));
                }

                container.Add(general);
            }

            var footer = Card(null);
            footer.Add(Message(UiStrings.T("compat.notModified"), UiTheme.Neutral));
            if (model.Blockers.Count > 0 || model.GeneralWarnings.Count > 0 ||
                HasToolWarnings(model))
            {
                footer.Add(Message(UiStrings.T("compat.manualInspection"), UiTheme.Warning));
            }

            container.Add(footer);
        }

        private static bool HasToolWarnings(CompatibilityViewModel model)
        {
            for (var i = 0; i < model.Tools.Count; i++)
            {
                if (model.Tools[i].Warnings.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private VisualElement BuildToolGroup(ToolGroup group)
        {
            var card = Card(null);

            var header = new VisualElement();
            header.AddToClassList("at-row");

            var riskDot = Dot(UiTheme.ForRisk(group.Risk));
            header.Add(riskDot);

            var title = new Label(group.DisplayName);
            title.AddToClassList("at-text-strong");
            header.Add(title);

            header.Add(Chip(UiStrings.T(RiskKey(group.Risk)), SemanticToneForRisk(group.Risk)));

            if (group.ComponentCount > 0)
            {
                header.Add(Chip(UiStrings.T("ui.label.components") + ": " +
                                group.ComponentCount.ToString(CultureInfo.InvariantCulture),
                    SemanticTone.Neutral));
            }
            else
            {
                header.Add(Chip(UiStrings.T("ui.label.notAvailable"), SemanticTone.Neutral));
            }

            card.Add(header);

            // The tool's explanation appears exactly once per tool group.
            var message = new Label(UiStrings.T(group.MessageKey));
            message.AddToClassList("at-text");
            message.style.color = UiTheme.TextSecondary;
            card.Add(message);

            for (var w = 0; w < group.Warnings.Count; w++)
            {
                card.Add(Message(
                    UiStrings.T(group.Warnings[w].MessageKey, group.Warnings[w].Args.ToArray()),
                    UiTheme.Warning));
            }

            if (group.Markers.Count > 0)
            {
                var detail = new Foldout();
                detail.text = UiStrings.T("ui.label.components");
                detail.value = false;
                detail.AddToClassList("at-details");

                if (group.ComponentTypes.Count > 0)
                {
                    detail.Add(Line(UiStrings.T("ui.label.kind") + ": " +
                                    string.Join(", ", group.ComponentTypes.ToArray())));
                }

                for (var m = 0; m < group.Markers.Count; m++)
                {
                    var markerRow = new VisualElement();
                    markerRow.AddToClassList("at-row");
                    markerRow.Add(Cell("at-col-name", group.Markers[m].Path ?? string.Empty, UiTheme.TextSecondary));

                    var markerType = new Label(group.Markers[m].ComponentType ?? string.Empty);
                    markerType.AddToClassList("at-text-secondary");
                    markerType.style.color = UiTheme.TextSecondary;
                    markerRow.Add(markerType);

                    var capturedPath = group.Markers[m].Path;
                    var ping = new Button(() => PingPath(capturedPath)) { text = UiStrings.T("ui.button.ping") };
                    ping.AddToClassList("at-btn");
                    ping.style.backgroundColor = UiTheme.ChipBackground;
                    ping.style.color = UiTheme.Text;
                    markerRow.Add(ping);
                    detail.Add(markerRow);
                }

                card.Add(detail);
            }

            return card;
        }

        // ===================================================================== module: tracking

        private void BuildTrackingModule(VisualElement container)
        {
            container.Add(BuildAvatarCard());

            var status = _controller.ChangeStatus;
            if (status == null)
            {
                var card = Card("ui.section.changeTracking");
                card.Add(Line(UiStrings.T("ui.label.tableEmpty")));
                container.Add(card);
                return;
            }

            var statusCard = Card("ui.section.changeTracking");
            var tone = DashboardModel.StatusTone(status.Status);
            var row = new VisualElement();
            row.AddToClassList("at-insight");
            row.Add(Dot(UiTheme.ForTone(tone)));
            var col = new VisualElement();
            col.AddToClassList("at-col");
            var name = new Label(UiStrings.T(DashboardModel.StatusNameKey(status.Status)));
            name.AddToClassList("at-text-strong");
            col.Add(name);
            var summary = new Label(UiStrings.T(status.SummaryKey));
            summary.AddToClassList("at-text-secondary");
            summary.style.color = UiTheme.TextSecondary;
            col.Add(summary);
            row.Add(col);
            statusCard.Add(row);

            var counts = new VisualElement();
            counts.AddToClassList("at-row");
            counts.style.marginTop = 6;
            counts.Add(Chip(UiStrings.T("ui.label.changedFiles") + ": " +
                            status.Changes.Count.ToString(CultureInfo.InvariantCulture), SemanticTone.Neutral));
            counts.Add(Chip(UiStrings.T("ui.label.staleOutputs") + ": " +
                            status.StaleGeneratedPaths.Count.ToString(CultureInfo.InvariantCulture),
                status.StaleGeneratedPaths.Count > 0 ? SemanticTone.Warning : SemanticTone.Success));
            counts.Add(Chip(UiStrings.T("ui.label.validOutputs") + ": " +
                            status.ValidGeneratedPaths.Count.ToString(CultureInfo.InvariantCulture),
                SemanticTone.Success));
            statusCard.Add(counts);
            container.Add(statusCard);

            if (status.Changes.Count > 0)
            {
                var changes = Card("ui.label.changedFiles");
                for (var i = 0; i < status.Changes.Count; i++)
                {
                    var change = status.Changes[i];
                    changes.Add(Message(
                        UiStrings.T(change.DetailKey, change.Args.ToArray()),
                        change.InvalidatesEverything ? UiTheme.Error : UiTheme.Warning));
                }

                container.Add(changes);
            }

            if (_controller.RecordedState != null)
            {
                var recorded = Card("ui.label.detail");
                recorded.Add(DetailRow("ui.label.toolVersion",
                    _controller.RecordedState.ToolVersionAtOptimization));
                recorded.Add(DetailRow("ui.label.engineVersion",
                    _controller.RecordedState.EngineVersionAtOptimization));
                recorded.Add(DetailRow("ui.label.lastOptimized",
                    string.IsNullOrEmpty(_controller.RecordedState.LastSuccessUtc)
                        ? UiStrings.T("ui.label.notAvailable")
                        : _controller.RecordedState.LastSuccessUtc));
                recorded.Add(DetailRow("ui.label.outputFolder", GeneratedAssetStore.GeneratedRoot));

                var buttons = new VisualElement();
                buttons.AddToClassList("at-row");
                buttons.style.marginTop = 6;
                buttons.Add(SecondaryButton("ui.button.viewManifest", OpenManifest));
                buttons.Add(SecondaryButton("ui.button.openOutputFolder",
                    () => EditorUtility.RevealInFinder(GeneratedAssetStore.GeneratedRoot)));
                recorded.Add(buttons);
                container.Add(recorded);
            }
        }

        // ===================================================================== module: results

        private void BuildResultsModule(VisualElement container)
        {
            var generation = _controller.Generation;
            var card = Card("ui.section.output");

            if (generation == null)
            {
                card.Add(Message(UiStrings.T("results.noResultsYet"), UiTheme.Info));
                container.Add(card);
                return;
            }

            var plan = _controller.Plan;
            var row = new VisualElement();
            row.AddToClassList("at-row");
            if (plan != null)
            {
                row.Add(BigValue(UiStrings.T("results.gpuMemoryBefore"),
                    TextureFormatInfo.FormatBytes(plan.SourceBytesTotal), UiTheme.TextSecondary));
                row.Add(BigValue("→", string.Empty, UiTheme.Neutral));
                row.Add(BigValue(UiStrings.T("results.gpuMemoryAfter"),
                    TextureFormatInfo.FormatBytes(plan.TargetBytesTotal), UiTheme.Accent));
                row.Add(BigValue(UiStrings.T("results.gpuMemorySaved"),
                    TextureFormatInfo.FormatBytes(plan.SavingBytes),
                    plan.SavingBytes > 0 ? UiTheme.Success : UiTheme.TextSecondary));
            }

            card.Add(row);

            long diskBefore = 0;
            if (plan != null && _controller.Report != null)
            {
                for (var i = 0; i < plan.Textures.Count; i++)
                {
                    var entry = _controller.Report.FindTexture(plan.Textures[i].TextureGuid);
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

            var diskRow = new VisualElement();
            diskRow.AddToClassList("at-row");
            diskRow.Add(BigValue(UiStrings.T("results.diskBefore"),
                TextureFormatInfo.FormatBytes(diskBefore), UiTheme.TextSecondary));
            diskRow.Add(BigValue("→", string.Empty, UiTheme.Neutral));
            diskRow.Add(BigValue(UiStrings.T("results.diskAfter"),
                TextureFormatInfo.FormatBytes(diskAfter), UiTheme.Text));
            diskRow.Add(Chip(UiStrings.T("ui.label.measured"), SemanticTone.Success));
            card.Add(diskRow);

            card.Add(Message(UiStrings.T("results.generatedDependencies",
                generation.Generated.Count.ToString(CultureInfo.InvariantCulture),
                generation.Generated.Count.ToString(CultureInfo.InvariantCulture)), UiTheme.Neutral));
            card.Add(Message(UiStrings.T("results.basisComputed"), UiTheme.Neutral));
            card.Add(Message(UiStrings.T("results.disclaimer"), UiTheme.Neutral));

            if (generation.SkippedCount > 0)
            {
                card.Add(Message(UiStrings.T("opt.skippedUnsupported",
                    generation.SkippedCount.ToString(CultureInfo.InvariantCulture)), UiTheme.Warning));
            }

            card.Add(DetailRow("ui.label.outputFolder", generation.OutputFolder ?? string.Empty));

            var buttons = new VisualElement();
            buttons.AddToClassList("at-row");
            buttons.Add(SecondaryButton("ui.button.openOutputFolder",
                () => EditorUtility.RevealInFinder(GeneratedAssetStore.GeneratedRoot)));
            buttons.Add(SecondaryButton("ui.button.exportReport", ExportReport));
            card.Add(buttons);

            container.Add(card);
        }

        // ===================================================================== module: settings

        private void BuildSettingsModule(VisualElement container)
        {
            container.Add(BuildPresetCard());
            container.Add(BuildOverrideCard());
            container.Add(BuildPreferencesCard());
        }

        private VisualElement BuildPresetCard()
        {
            var card = Card("ui.section.quality");
            card.Add(Message(UiStrings.T("tip.presetField"), UiTheme.Neutral));

            var row = new VisualElement();
            row.AddToClassList("at-row-top");
            foreach (OptimizationPreset preset in Enum.GetValues(typeof(OptimizationPreset)))
            {
                var captured = preset;
                var selected = _controller.Preset == preset;
                var presetCard = new Button(() =>
                {
                    _controller.Preset = captured;
                    _controller.RebuildPlan();
                    RefreshView();
                });
                presetCard.AddToClassList("at-card");
                presetCard.style.backgroundColor = selected
                    ? UiTheme.SelectedBackground
                    : UiTheme.CardBackground;
                presetCard.style.borderLeftColor = selected ? UiTheme.Accent : UiTheme.CardBorder;
                presetCard.style.borderRightColor = UiTheme.CardBorder;
                presetCard.style.borderTopColor = UiTheme.CardBorder;
                presetCard.style.borderBottomColor = UiTheme.CardBorder;
                presetCard.style.borderLeftWidth = selected ? 3 : 1;
                presetCard.style.flexGrow = 1;
                presetCard.style.minWidth = 150;

                var titleRow = new VisualElement();
                titleRow.AddToClassList("at-row");
                titleRow.Add(Dot(selected ? UiTheme.Accent : UiTheme.Neutral));
                var name = new Label(UiStrings.T(PresetNameKey(preset)));
                name.AddToClassList("at-text-strong");
                titleRow.Add(name);
                presetCard.Add(titleRow);

                var description = new Label(UiStrings.T(PresetDescriptionKey(preset)));
                description.AddToClassList("at-text-secondary");
                description.style.color = UiTheme.TextSecondary;
                presetCard.Add(description);

                row.Add(presetCard);
            }

            card.Add(row);
            return card;
        }

        private VisualElement BuildOverrideCard()
        {
            var card = Card("ui.section.overrides");

            var buttons = new VisualElement();
            buttons.AddToClassList("at-row");
            buttons.Add(SecondaryButton("ui.button.revertOverrides",
                () => { _controller.ClearOverrides(); RefreshView(); }));
            buttons.Add(SecondaryButton("ui.button.clearExclusions",
                () => { ClearExclusions(); RefreshView(); }));
            buttons.Add(SecondaryButton("ui.button.selectAll",
                () => { SetAllIncluded(true); RefreshView(); }));
            buttons.Add(SecondaryButton("ui.button.selectNone",
                () => { SetAllIncluded(false); RefreshView(); }));
            card.Add(buttons);

            card.Add(Message(UiStrings.T("tip.exclusionList"), UiTheme.Neutral));

            if (_controller.Plan == null)
            {
                card.Add(Line(UiStrings.T("ui.hint.analyzeFirst")));
                return card;
            }

            // Advanced per-texture controls stay collapsed until the user asks for them.
            var list = new Foldout();
            list.text = UiStrings.T("ui.section.advanced");
            list.value = false;
            list.AddToClassList("at-details");

            var shown = 0;
            for (var i = 0; i < _controller.Plan.Textures.Count; i++)
            {
                var planned = _controller.Plan.Textures[i];
                if (shown >= 100)
                {
                    list.Add(Line(UiStrings.T("ui.label.moreItems",
                        (_controller.Plan.Textures.Count - shown).ToString(CultureInfo.InvariantCulture))));
                    break;
                }

                var row = new VisualElement();
                row.AddToClassList("at-row");
                row.Add(Cell("at-col-name", planned.TextureName ?? planned.TextureGuid, UiTheme.Text));

                row.Add(OverrideToggle(planned, "ui.label.resolution",
                    planned.Override != null && planned.Override.KeepResolution, value =>
                    {
                        var ovr = planned.Override ?? new TextureOverride();
                        ovr.KeepResolution = value;
                        _controller.SetOverride(planned.TextureGuid, ovr);
                    }));
                row.Add(OverrideToggle(planned, "ui.label.format",
                    planned.Override != null && planned.Override.KeepFormat, value =>
                    {
                        var ovr = planned.Override ?? new TextureOverride();
                        ovr.KeepFormat = value;
                        _controller.SetOverride(planned.TextureGuid, ovr);
                    }));

                list.Add(row);
                shown++;
            }

            card.Add(list);
            return card;
        }

        private VisualElement OverrideToggle(
            PlannedTexture planned, string labelKey, bool value, Action<bool> onChanged)
        {
            var toggle = new Toggle(UiStrings.T(labelKey));
            toggle.value = value;
            toggle.SetEnabled(!planned.ProtectedByThirdParty);
            if (planned.ProtectedByThirdParty)
            {
                toggle.tooltip = UiStrings.T("plan.note.protectedThirdParty");
            }

            toggle.AddToClassList("at-col-w95");
            toggle.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            return toggle;
        }

        private VisualElement BuildPreferencesCard()
        {
            var card = Card("ui.section.preferences");

            var includeInactive = new Toggle(UiStrings.T("ui.toggle.includeInactive"));
            includeInactive.value = _controller.IncludeInactive;
            includeInactive.tooltip = UiStrings.T("tip.includeInactive");
            includeInactive.RegisterValueChangedCallback(evt =>
            {
                _controller.IncludeInactive = evt.newValue;
                if (_controller.Report != null)
                {
                    RunAnalyze();
                }
            });
            card.Add(includeInactive);

            var measureQuality = new Toggle(UiStrings.T("ui.toggle.measureQuality"));
            measureQuality.value = _controller.MeasureQuality;
            measureQuality.tooltip = UiStrings.T("tip.measureQuality");
            measureQuality.RegisterValueChangedCallback(evt => _controller.MeasureQuality = evt.newValue);
            card.Add(measureQuality);

            var overwrite = new Toggle(UiStrings.T("ui.toggle.overwriteGenerated"));
            overwrite.value = _controller.OverwriteGenerated;
            overwrite.tooltip = UiStrings.T("tip.outputFolder");
            overwrite.RegisterValueChangedCallback(evt => _controller.OverwriteGenerated = evt.newValue);
            card.Add(overwrite);

            card.Add(Message(UiStrings.T("tip.qualityEstimate"), UiTheme.Neutral));
            card.Add(Message(UiStrings.T("tip.qualityMeasure"), UiTheme.Neutral));
            card.Add(DetailRow("ui.label.outputFolder", GeneratedAssetStore.GeneratedRoot));

            var advanced = new Foldout();
            advanced.text = UiStrings.T("ui.section.advanced");
            advanced.value = false;
            advanced.AddToClassList("at-details");
            advanced.Add(DetailRow("ui.label.toolVersion", GeneratedAssetStore.ToolVersion));
            advanced.Add(DetailRow("ui.label.engineVersion", Application.unityVersion));
            advanced.Add(DetailRow("ui.label.dependencies",
                _controller.Report != null
                    ? _controller.Report.PossibleBuildTimeDependencies.Count.ToString(CultureInfo.InvariantCulture)
                    : UiStrings.T("ui.label.notAvailable")));
            card.Add(advanced);

            return card;
        }

        // ===================================================================== actions (operations)

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

            RefreshView();
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
            bool succeeded;
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
                _module = ModuleResults;
            }
            else if (_cancelRequested)
            {
                ShowNotice("opt.cancelled");
            }

            RefreshView();
        }

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
            RefreshView();
        }

        private void OpenManifest()
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

            RefreshView();
        }

        private void CopySummary()
        {
            EditorGUIUtility.systemCopyBuffer = BuildReportText();
            ShowNotice("opt.done");
            RefreshView();
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

        // ===================================================================== shared helpers

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

        /// <summary>Locates a hierarchy path or asset path and highlights it in the editor.</summary>
        private void PingPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    if (asset != null)
                    {
                        Selection.activeObject = asset;
                        EditorGUIUtility.PingObject(asset);
                        return;
                    }
                }

                if (_controller.Avatar == null)
                {
                    return;
                }

                var target = FindByHierarchyPath(_controller.Avatar.transform, path);
                if (target != null)
                {
                    Selection.activeObject = target.gameObject;
                    EditorGUIUtility.PingObject(target.gameObject);
                }
            }
            catch (Exception)
            {
                // Pinging is a convenience; a failure must never disturb the workflow.
            }
        }

        private static Transform FindByHierarchyPath(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path))
            {
                return null;
            }

            var segments = path.Split('/');
            var current = root;
            var start = 0;
            if (segments.Length > 0 && segments[0] == root.name)
            {
                start = 1;
            }

            for (var i = start; i < segments.Length; i++)
            {
                Transform next = null;
                for (var c = 0; c < current.childCount; c++)
                {
                    var child = current.GetChild(c);
                    if (child.name == segments[i])
                    {
                        next = child;
                        break;
                    }
                }

                if (next == null)
                {
                    return null;
                }

                current = next;
            }

            return current;
        }

        // ===================================================================== element factories

        private static VisualElement Spacer()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList("at-grow");
            return spacer;
        }

        private static VisualElement Dot(Color color)
        {
            var dot = new VisualElement();
            dot.AddToClassList("at-dot");
            dot.style.backgroundColor = color;
            return dot;
        }

        private static VisualElement Card(string titleKey)
        {
            var card = new VisualElement();
            card.AddToClassList("at-card");
            card.style.backgroundColor = UiTheme.CardBackground;
            card.style.borderLeftColor = UiTheme.CardBorder;
            card.style.borderRightColor = UiTheme.CardBorder;
            card.style.borderTopColor = UiTheme.CardBorder;
            card.style.borderBottomColor = UiTheme.CardBorder;

            if (!string.IsNullOrEmpty(titleKey))
            {
                var title = new Label(UiStrings.T(titleKey));
                title.AddToClassList("at-card-title");
                card.Add(title);
            }

            return card;
        }

        private static VisualElement Chip(string text, SemanticTone tone)
        {
            var chip = new Label(text);
            chip.AddToClassList("at-chip");
            chip.style.backgroundColor = UiTheme.ChipBackground;
            chip.style.color = UiTheme.ForTone(tone);
            return chip;
        }

        private static Label Line(string text)
        {
            var label = new Label(text);
            label.AddToClassList("at-text");
            return label;
        }

        private static VisualElement Cell(string widthClass, string text, Color color)
        {
            var label = new Label(text);
            label.AddToClassList(widthClass);
            label.AddToClassList("at-text");
            label.style.color = color;
            return label;
        }

        private static Label HeaderCell(string widthClass, string text)
        {
            var label = new Label(text);
            label.AddToClassList(widthClass);
            label.AddToClassList("at-table-header");
            label.style.color = UiTheme.TextSecondary;
            return label;
        }

        private static VisualElement HeaderRow((string widthClass, string text)[] columns)
        {
            var row = new VisualElement();
            row.AddToClassList("at-row");
            for (var i = 0; i < columns.Length; i++)
            {
                row.Add(HeaderCell(columns[i].widthClass, columns[i].text));
            }

            return row;
        }

        private static VisualElement StatusCell(string widthClass, TextureRowStatus status)
        {
            var holder = new VisualElement();
            holder.AddToClassList(widthClass);
            holder.AddToClassList("at-row");
            holder.Add(Dot(UiTheme.ForTone(TextureRowModel.StatusTone(status))));
            var label = new Label(UiStrings.T(TextureRowModel.StatusLabelKey(status)));
            label.AddToClassList("at-text-secondary");
            label.style.color = UiTheme.ForTone(TextureRowModel.StatusTone(status));
            holder.Add(label);
            return holder;
        }

        private static VisualElement RiskCell(string widthClass, CompatRisk risk)
        {
            var holder = new VisualElement();
            holder.AddToClassList(widthClass);
            holder.AddToClassList("at-row");
            holder.Add(Dot(UiTheme.ForRisk(risk)));
            var label = new Label(UiStrings.T(RiskKey(risk)));
            label.AddToClassList("at-text-secondary");
            label.style.color = UiTheme.ForRisk(risk);
            holder.Add(label);
            return holder;
        }

        private VisualElement IncludeCell(TextureRowModel model)
        {
            var holder = new VisualElement();
            holder.AddToClassList("at-col-w60");

            var included = !model.IsExcluded && !model.IsProtected;
            var toggle = new Toggle(string.Empty);
            toggle.value = included;
            toggle.SetEnabled(!model.IsProtected && WindowFlow.CanEditSettings(_controller.State));
            if (model.IsProtected)
            {
                toggle.tooltip = UiStrings.T("plan.note.protectedThirdParty");
            }
            else
            {
                toggle.tooltip = UiStrings.T("ui.label.include");
            }

            var guid = model.Guid;
            toggle.RegisterValueChangedCallback(evt =>
            {
                if (string.IsNullOrEmpty(guid))
                {
                    return;
                }

                TextureOverride ovr;
                if (!_controller.Overrides.TryGetValue(guid, out ovr))
                {
                    ovr = new TextureOverride();
                }

                ovr.Excluded = !evt.newValue;
                _controller.SetOverride(guid, ovr);
                RefreshView();
            });
            holder.Add(toggle);
            return holder;
        }

        private static VisualElement DetailRow(string labelKey, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("at-row");
            var label = new Label(UiStrings.T(labelKey));
            label.AddToClassList("at-text-secondary");
            label.style.color = UiTheme.TextSecondary;
            label.style.minWidth = 110;
            row.Add(label);
            var valueLabel = new Label(value ?? string.Empty);
            valueLabel.AddToClassList("at-text");
            valueLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(valueLabel);
            return row;
        }

        private static VisualElement BigValue(string labelKey, string value, Color valueColor)
        {
            var holder = new VisualElement();
            holder.AddToClassList("at-col");
            holder.style.marginRight = 18;
            var label = new Label(labelKey);
            label.AddToClassList("at-metric-label");
            label.style.color = UiTheme.TextSecondary;
            holder.Add(label);
            if (!string.IsNullOrEmpty(value))
            {
                var valueLabel = new Label(value);
                valueLabel.AddToClassList("at-metric-value-sm");
                valueLabel.style.color = valueColor;
                holder.Add(valueLabel);
            }

            return holder;
        }

        private static VisualElement Message(string text, Color tone)
        {
            var card = new VisualElement();
            card.AddToClassList("at-row-card");
            card.style.backgroundColor = UiTheme.CardHeaderBackground;
            card.style.borderLeftColor = tone;
            card.style.borderRightColor = UiTheme.CardBorder;
            card.style.borderTopColor = UiTheme.CardBorder;
            card.style.borderBottomColor = UiTheme.CardBorder;
            card.style.borderLeftWidth = 3;

            var row = new VisualElement();
            row.AddToClassList("at-insight");
            row.Add(Dot(tone));
            var label = new Label(text);
            label.AddToClassList("at-text");
            row.Add(label);
            card.Add(row);
            return card;
        }

        private static VisualElement EmptyState()
        {
            var card = Card(null);
            card.Add(Message(UiStrings.T("ui.state.noSelection.title"), UiTheme.Info));
            card.Add(Line(UiStrings.T("ui.state.empty.body")));
            return card;
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

        private static SemanticTone SemanticToneForRisk(CompatRisk risk)
        {
            switch (risk)
            {
                case CompatRisk.High: return SemanticTone.Error;
                case CompatRisk.Medium: return SemanticTone.Warning;
                default: return SemanticTone.Success;
            }
        }

        private static string PresetNameKey(OptimizationPreset preset)
        {
            switch (preset)
            {
                case OptimizationPreset.QualityFirst: return "preset.quality-first.name";
                case OptimizationPreset.SizeFirst: return "preset.size-first.name";
                default: return "preset.balanced.name";
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
    }
}
