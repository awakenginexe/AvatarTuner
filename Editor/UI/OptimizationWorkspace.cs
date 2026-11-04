using System;
using System.Collections.Generic;
using System.Globalization;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;
using AvatarTuner.Editor.ViewModels;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarTuner.Editor.UI
{
    public sealed class OptimizationWorkspace : EditorWindow
    {
        private const int PageSize = 50;
        private static readonly double[] ReductionPresets = { 1, 2, 3, 4, 5, 6, 7, 8, 16 };
        private AvatarTunerController controller;
        private Action changed;
        private CandidatePreview preview;
        private HashSet<string> selected { get { return controller.Settings.IncludedTextures; } }
        private string currentGuid;
        private string search = string.Empty;
        private int categoryFilter;
        private int sort;
        private int page;
        private SemanticCategory category = SemanticCategory.Face;
        private float zoom = 1f;
        private bool cancelled;
        private bool showBulk;
        private bool showDefaults;

        private VisualElement headerContainer;
        private VisualElement metricsContainer;
        private VisualElement compressionContainer;
        private Button confirmButton;
        private VisualElement drawerContainer;
        private VisualElement masterHeaderContainer;
        private ScrollView masterScroll;
        private VisualElement masterListContainer;
        private ScrollView detailScroll;
        private readonly Dictionary<string, VisualElement> cardElements =
            new Dictionary<string, VisualElement>(StringComparer.OrdinalIgnoreCase);

        public static void Open(AvatarTunerController controller, Action changed)
        {
            controller.SetOptimizationMode(OptimizationMode.Advanced);
            var window = GetWindow<OptimizationWorkspace>();
            window.titleContent = new GUIContent(UiStrings.T("workspace.title"));
            window.minSize = new Vector2(760, 480);
            window.controller = controller;
            window.changed = changed;
            window.Refresh();
            window.Show();
        }

        private void CreateGUI() { Refresh(); }
        private void OnDisable() { DisposePreview(); }

        private void DisposePreview()
        {
            if (preview != null) preview.Dispose();
            preview = null;
        }

        private static string T(string key) { return UiStrings.T("workspace." + key); }
        private static string EnumText<TEnum>(TEnum value) { return T(typeof(TEnum).Name + "." + value); }

        private static Label Text(string value)
        {
            var label = new Label(value ?? string.Empty);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.flexWrap = Wrap.Wrap;
            return row;
        }

        private static Button Button(string key, Action action)
        {
            return new Button(action) { text = T(key) };
        }

        private static void Heading(VisualElement parent, string key)
        {
            var label = Text(T(key));
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 8;
            label.style.marginBottom = 4;
            parent.Add(label);
        }

        private static string Bytes(long value) { return TextureFormatInfo.FormatTextureMemory(value); }

        private static void SetCardBorders(VisualElement elem, Color left, Color others)
        {
            elem.style.borderLeftColor = left;
            elem.style.borderRightColor = others;
            elem.style.borderTopColor = others;
            elem.style.borderBottomColor = others;
        }

        private void TryLoadStylesheet()
        {
            try
            {
                var guids = AssetDatabase.FindAssets("AvatarTunerWindow t:StyleSheet");
                for (var i = 0; i < guids.Length; i++)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (!path.EndsWith("Editor/UI/AvatarTunerWindow.uss", StringComparison.OrdinalIgnoreCase)) continue;
                    var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                    if (sheet != null) rootVisualElement.styleSheets.Add(sheet);
                    return;
                }
            }
            catch (Exception) { }
        }

        private void Refresh()
        {
            if (rootVisualElement == null) return;
            rootVisualElement.Clear();
            cardElements.Clear();

            if (controller == null || controller.Settings == null || controller.Report == null)
            {
                var emptyContainer = new VisualElement();
                emptyContainer.style.paddingTop = 20;
                emptyContainer.style.paddingLeft = 20;
                emptyContainer.Add(Text(T("reopen")));
                rootVisualElement.Add(emptyContainer);
                return;
            }

            TryLoadStylesheet();

            rootVisualElement.AddToClassList("at-workspace-root");
            rootVisualElement.style.backgroundColor = UiTheme.CardBackground;
            rootVisualElement.style.color = UiTheme.Text;

            // 1. Fixed Top Header
            headerContainer = new VisualElement();
            headerContainer.AddToClassList("at-workspace-header");
            headerContainer.style.borderBottomColor = UiTheme.CardBorder;
            BuildHeader(headerContainer);
            rootVisualElement.Add(headerContainer);

            // 2. Collapsible Drawer (Bulk edit & Defaults)
            drawerContainer = new VisualElement();
            drawerContainer.AddToClassList("at-workspace-drawer");
            drawerContainer.style.borderBottomColor = UiTheme.CardBorder;
            drawerContainer.style.backgroundColor = UiTheme.CardHeaderBackground;
            drawerContainer.style.display = (showBulk || showDefaults) ? DisplayStyle.Flex : DisplayStyle.None;
            RebuildDrawer();
            rootVisualElement.Add(drawerContainer);

            // 3. Split Master-Detail Body
            var body = new VisualElement();
            body.AddToClassList("at-workspace-body");
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;

            // Left Master Pane
            var masterPane = new VisualElement();
            masterPane.AddToClassList("at-workspace-master");
            masterPane.style.borderRightColor = UiTheme.CardBorder;
            masterPane.style.paddingTop = 6;
            masterPane.style.paddingLeft = 8;
            masterPane.style.paddingRight = 8;
            masterPane.style.paddingBottom = 6;

            masterHeaderContainer = new VisualElement();
            masterPane.Add(masterHeaderContainer);

            masterScroll = new ScrollView(ScrollViewMode.Vertical);
            masterScroll.AddToClassList("at-grow");
            masterListContainer = new VisualElement();
            masterScroll.Add(masterListContainer);
            masterPane.Add(masterScroll);
            body.Add(masterPane);

            // Right Detail Inspector Pane
            var detailPane = new VisualElement();
            detailPane.AddToClassList("at-workspace-detail");
            detailPane.style.paddingTop = 6;
            detailPane.style.paddingLeft = 12;
            detailPane.style.paddingRight = 12;
            detailPane.style.paddingBottom = 6;

            detailScroll = new ScrollView(ScrollViewMode.Vertical);
            detailScroll.AddToClassList("at-grow");
            detailPane.Add(detailScroll);
            body.Add(detailPane);

            rootVisualElement.Add(body);

            RebuildMasterList();
            RebuildDetailPane();
        }

        private void Mutated()
        {
            DisposePreview();
            controller.SetOptimizationMode(OptimizationMode.Advanced);
            controller.SaveSettings();
            controller.RebuildPlan();
            if (changed != null) changed();

            var savedOffset = masterScroll != null ? masterScroll.scrollOffset : new Vector2(0, 0);

            UpdateHeaderMetrics();
            RebuildCompressionControls();
            if (showBulk) RebuildDrawer();
            RebuildMasterList();

            if (masterScroll != null && (savedOffset.x != 0 || savedOffset.y != 0))
            {
                masterScroll.scrollOffset = savedOffset;
            }

            RebuildDetailPane();
        }

        // ===================================================================== header & toolbar

        private void BuildHeader(VisualElement parent)
        {
            // Row 1: Brand, Mode, Metrics Summary, and Actions
            var topRow = Row();
            topRow.style.justifyContent = Justify.SpaceBetween;

            var leftBrand = Row();
            var title = new Label("AvatarTuner");
            title.AddToClassList("at-app-title");
            leftBrand.Add(title);

            var badge = new Label(UiStrings.T("workspace.open"));
            badge.AddToClassList("at-app-tag");
            badge.style.backgroundColor = UiTheme.Accent;
            badge.style.color = UiTheme.TextOnAccent;
            leftBrand.Add(badge);

            if (controller.Avatar != null)
            {
                var avatarLabel = new Label("· " + controller.Avatar.name);
                avatarLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                avatarLabel.style.fontSize = 12;
                leftBrand.Add(avatarLabel);
            }
            topRow.Add(leftBrand);

            metricsContainer = Row();
            UpdateHeaderMetrics();
            topRow.Add(metricsContainer);

            var actions = Row();

            var defaultsBtn = new Button(() =>
            {
                showDefaults = !showDefaults;
                if (showDefaults) showBulk = false;
                RebuildDrawer();
            }) { text = (showDefaults ? "▲ " : "▼ ") + T("global") };
            defaultsBtn.AddToClassList("at-btn");
            if (showDefaults) defaultsBtn.style.backgroundColor = UiTheme.SelectedBackground;
            actions.Add(defaultsBtn);

            var bulkBtn = new Button(() =>
            {
                showBulk = !showBulk;
                if (showBulk) showDefaults = false;
                RebuildDrawer();
            }) { text = (showBulk ? "▲ " : "▼ ") + T("bulk") };
            bulkBtn.AddToClassList("at-btn");
            if (showBulk) bulkBtn.style.backgroundColor = UiTheme.SelectedBackground;
            actions.Add(bulkBtn);

            var apply = new Button(() => Execute(true)) { text = UiStrings.T("ui.button.confirmOptimize") };
            confirmButton = apply;
            apply.AddToClassList("at-primary-btn");
            apply.style.backgroundColor = UiTheme.Accent;
            apply.style.color = UiTheme.TextOnAccent;
            apply.SetEnabled(selected.Count > 0 && controller.Report != null && controller.CompatibilityAllows && !WindowFlow.IsBusy(controller.State));
            apply.tooltip = T("evaluateHint");
            actions.Add(apply);

            topRow.Add(actions);
            parent.Add(topRow);

            if (!string.IsNullOrEmpty(controller.ErrorKey))
            {
                var error = Text(UiStrings.T(controller.ErrorKey));
                error.style.color = UiTheme.Error;
                error.style.marginTop = 3;
                parent.Add(error);
            }

            // Row 2: Search, Category Filter, Sort, and Batch Inclusion
            var toolbar = Row();
            toolbar.AddToClassList("at-workspace-toolbar");

            var query = new TextField { value = search, isDelayed = true };
            query.label = T("search");
            query.AddToClassList("at-field");
            query.style.minWidth = 170;
            query.RegisterValueChangedCallback(e => { search = e.newValue ?? string.Empty; page = 0; RebuildMasterList(); });
            toolbar.Add(query);

            var categories = new List<string> { T("allCategories") };
            foreach (SemanticCategory value in Enum.GetValues(typeof(SemanticCategory))) categories.Add(EnumText(value));
            var filter = new PopupField<string>(T("filter"), categories, categoryFilter);
            filter.AddToClassList("at-field");
            filter.RegisterValueChangedCallback(e => { categoryFilter = categories.IndexOf(e.newValue); page = 0; RebuildMasterList(); });
            toolbar.Add(filter);

            var sorts = new List<string> { T("sortName"), T("sortMemory"), T("sortSavings") };
            var order = new PopupField<string>(T("sort"), sorts, sort);
            order.AddToClassList("at-field");
            order.RegisterValueChangedCallback(e => { sort = sorts.IndexOf(e.newValue); page = 0; RebuildMasterList(); });
            toolbar.Add(order);

            var clearAll = new Button(() => { selected.Clear(); Mutated(); }) { text = T("clearSelection") };
            clearAll.AddToClassList("at-btn");
            toolbar.Add(clearAll);

            parent.Add(toolbar);
            compressionContainer = new VisualElement();
            parent.Add(compressionContainer);
            RebuildCompressionControls();
        }

        private void RebuildCompressionControls()
        {
            if (compressionContainer == null) return;
            compressionContainer.Clear();
            compressionContainer.Add(Text(T("checkedCompression")));
            BuildBulkCompression(compressionContainer);
            compressionContainer.SetEnabled(selected.Count > 0);
        }

        private void UpdateHeaderMetrics()
        {
            if (confirmButton != null)
                confirmButton.SetEnabled(selected.Count > 0 && controller.Report != null && controller.CompatibilityAllows && !WindowFlow.IsBusy(controller.State));
            if (metricsContainer == null) return;
            metricsContainer.Clear();

            var pill = Row();
            pill.AddToClassList("at-chip");
            pill.style.backgroundColor = UiTheme.ChipBackground;

            var countText = new Label(UiStrings.T("workspace.includedCount", selected.Count.ToString(CultureInfo.InvariantCulture)));
            countText.style.fontSize = 11;
            countText.style.color = selected.Count > 0 ? UiTheme.Text : UiTheme.TextSecondary;
            pill.Add(countText);

            if (controller.Plan != null)
            {
                var p = controller.Plan;
                var memory = controller.PlanMemoryNumbers();
                var summaryText = new Label(" · " + UiStrings.T("workspace.summary",
                    memory.HasSource ? Bytes(memory.SourceBytes) : T("unknown"),
                    memory.HasOutput ? Bytes(memory.OutputBytes) : T("unknown"),
                    p.ChangedCount.ToString(CultureInfo.InvariantCulture)));
                summaryText.style.fontSize = 11;
                summaryText.style.unityFontStyleAndWeight = FontStyle.Bold;
                summaryText.style.color = p.ChangedCount > 0 ? UiTheme.Success : UiTheme.TextSecondary;
                pill.Add(summaryText);
            }

            metricsContainer.Add(pill);
        }

        // ===================================================================== drawer (bulk & defaults)

        private void RebuildDrawer()
        {
            if (drawerContainer == null) return;
            drawerContainer.Clear();
            drawerContainer.style.display = (showBulk || showDefaults) ? DisplayStyle.Flex : DisplayStyle.None;
            drawerContainer.SetEnabled(!showBulk || selected.Count > 0);

            if (showBulk)
            {
                BuildBulk(drawerContainer);
            }
            else if (showDefaults)
            {
                BuildSettings(drawerContainer);
            }
        }

        private void BuildBulk(VisualElement parent)
        {
            var title = Text(T("bulk"));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(title);

            var hint = Text(T("bulkHint"));
            hint.style.color = UiTheme.TextSecondary;
            hint.style.marginBottom = 6;
            parent.Add(hint);

            var row1 = Row();
            var keep = new Button(() => Bulk(s => s.KeepResolution = true)) { text = T("bulkKeep") };
            keep.AddToClassList("at-btn");
            var resize = new Button(() => Bulk(s => s.KeepResolution = false)) { text = T("bulkAllowResize") };
            resize.AddToClassList("at-btn");
            var exclude = new Button(() => Bulk(s => s.Excluded = true)) { text = T("bulkExclude") };
            exclude.AddToClassList("at-btn");
            var include = new Button(() => Bulk(s => s.Excluded = false)) { text = T("bulkInclude") };
            include.AddToClassList("at-btn");
            row1.Add(keep); row1.Add(resize); row1.Add(exclude); row1.Add(include);
            parent.Add(row1);
        }

        private void BuildBulkCompression(VisualElement parent)
        {
            var row2 = Row();
            row2.style.marginTop = 4;

            TextureSettings common = null;
            var sameRatio = true;
            var sameFormat = true;
            foreach (var guid in selected)
            {
                var texture = controller.Report.FindTexture(guid);
                if (texture == null) continue;
                var settings = controller.Settings.Resolve(guid, texture.Category).Settings;
                if (common == null) common = settings;
                else { sameRatio &= settings.DesiredReduction == common.DesiredReduction; sameFormat &= settings.Format == common.Format; }
            }
            var ratios = new List<string> { T("chooseRatio") };
            foreach (var value in ReductionPresets) ratios.Add(value.ToString("0.#", CultureInfo.InvariantCulture) + "x");
            var ratio = new PopupField<string>(T("ratio"), ratios, 0);
            if (sameRatio && common != null)
            {
                var index = Array.IndexOf(ReductionPresets, common.DesiredReduction);
                if (index >= 0) ratio.value = ratios[index + 1];
            }
            ratio.AddToClassList("at-field");
            ratio.RegisterValueChangedCallback(e => { var i = ratios.IndexOf(e.newValue); if (i > 0) Bulk(s => s.DesiredReduction = ReductionPresets[i - 1]); });

            var customRatio = new FloatField(T("customRatio")) { value = common != null && sameRatio ? (float)common.DesiredReduction : 4, isDelayed = true };
            customRatio.tooltip = T("ratioHelp");
            customRatio.AddToClassList("at-field");

            var applyCustomRatio = new Button(() =>
            {
                var value = customRatio.value;
                if (!float.IsNaN(value) && !float.IsInfinity(value) && value >= 1) Bulk(s => s.DesiredReduction = value);
            }) { text = T("applyCustomRatio") };
            applyCustomRatio.AddToClassList("at-btn");
            applyCustomRatio.tooltip = T("ratioHelp");
            applyCustomRatio.SetEnabled(!float.IsNaN(customRatio.value) && !float.IsInfinity(customRatio.value) && customRatio.value >= 1);
            customRatio.RegisterValueChangedCallback(e => applyCustomRatio.SetEnabled(!float.IsNaN(e.newValue) && !float.IsInfinity(e.newValue) && e.newValue >= 1));

            var formats = new[] { TunerTextureFormat.Automatic, TunerTextureFormat.BC1, TunerTextureFormat.BC3, TunerTextureFormat.BC4, TunerTextureFormat.BC5, TunerTextureFormat.BC7, TunerTextureFormat.ASTC_4x4, TunerTextureFormat.ASTC_6x6, TunerTextureFormat.ASTC_8x8 };
            var choices = new List<string> { T("chooseFormat") };
            foreach (var format in formats) choices.Add(format == TunerTextureFormat.Automatic ? T("automatic") : format.ToString());
            var formatField = new PopupField<string>(T("format"), choices, 0);
            if (sameFormat && common != null)
            {
                var index = Array.IndexOf(formats, common.Format);
                if (index >= 0) formatField.value = choices[index + 1];
            }
            formatField.AddToClassList("at-field");
            formatField.tooltip = T("formatHint");
            formatField.RegisterValueChangedCallback(e => { var i = choices.IndexOf(e.newValue); if (i > 0) Bulk(s => s.Format = formats[i - 1]); });
            row2.Add(formatField);
            row2.Add(ratio);
            row2.Add(customRatio);
            row2.Add(applyCustomRatio);

            parent.Add(row2);
            var hint = Text(T("formatHint"));
            hint.style.color = UiTheme.TextSecondary;
            parent.Add(hint);
        }

        private void Bulk(Action<TextureSettings> mutate)
        {
            foreach (var guid in selected)
            {
                var texture = controller.Report.FindTexture(guid);
                if (texture == null) continue;
                TextureSettings settings;
                if (!controller.Settings.Textures.TryGetValue(guid, out settings))
                    settings = controller.Settings.Resolve(guid, texture.Category).Settings.Clone();
                mutate(settings);
                controller.Settings.Textures[guid] = settings;
            }
            Mutated();
        }

        private void BuildSettings(VisualElement parent)
        {
            var title = Text(T("global"));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(title);

            var global = new Foldout { text = T("global"), value = true };
            parent.Add(global);
            SettingsFields(global, controller.Settings.Global, null, Mutated);

            var group = new Foldout { text = T("categorySettings"), value = false };
            parent.Add(group);
            EnumPopup(group, "category", category, value => { category = value; RebuildDrawer(); });
            TextureSettings categorySettings;
            if (!controller.Settings.Categories.TryGetValue(category, out categorySettings))
            {
                group.Add(Button("createCategory", () => { controller.Settings.Categories[category] = controller.Settings.Global.Clone(); Mutated(); }));
            }
            else
            {
                group.Add(Button("resetCategory", () => { controller.Settings.Categories.Remove(category); Mutated(); }));
                SettingsFields(group, categorySettings, null, Mutated);
            }
        }

        private static void EnumPopup<TEnum>(VisualElement parent, string key, TEnum current, Action<TEnum> onChange)
        {
            var values = (TEnum[])Enum.GetValues(typeof(TEnum));
            var labels = new List<string>();
            foreach (var value in values) labels.Add(EnumText(value));
            var field = new PopupField<string>(T(key), labels, Math.Max(0, Array.IndexOf(values, current)));
            field.AddToClassList("at-field");
            field.RegisterValueChangedCallback(e => onChange(values[labels.IndexOf(e.newValue)]));
            parent.Add(field);
        }

        private void CompressionFields(VisualElement parent, TextureSettings settings, TextureEntry texture, Action commit)
        {
            var presets = Row();
            foreach (var value in ReductionPresets)
            {
                var reduction = value;
                var button = new Button(() => { settings.DesiredReduction = reduction; commit(); })
                    { text = reduction.ToString("0.#", CultureInfo.InvariantCulture) + "x" };
                button.AddToClassList("at-btn");
                button.tooltip = T("ratioHelp");
                if (settings.DesiredReduction == reduction) button.style.backgroundColor = UiTheme.SelectedBackground;
                presets.Add(button);
            }
            var ratio = new FloatField(T("ratio")) { value = (float)settings.DesiredReduction, isDelayed = true };
            ratio.tooltip = T("ratioHelp");
            ratio.AddToClassList("at-field");
            ratio.RegisterValueChangedCallback(e =>
            {
                settings.DesiredReduction = float.IsNaN(e.newValue) || float.IsInfinity(e.newValue) ? 4 : Math.Max(1, e.newValue);
                commit();
            });

            var formats = texture == null ? new List<TunerTextureFormat> { TunerTextureFormat.Automatic, TunerTextureFormat.BC1, TunerTextureFormat.BC3, TunerTextureFormat.BC4, TunerTextureFormat.BC5, TunerTextureFormat.BC7, TunerTextureFormat.ASTC_4x4, TunerTextureFormat.ASTC_6x6, TunerTextureFormat.ASTC_8x8 } : controller.AllowedFormats(texture);
            if (formats == null) formats = new List<TunerTextureFormat>();
            if (!formats.Contains(TunerTextureFormat.Automatic)) formats.Insert(0, TunerTextureFormat.Automatic);
            if (!formats.Contains(settings.Format)) formats.Add(settings.Format);
            var formatLabels = new List<string>();
            foreach (var format in formats) formatLabels.Add(format == TunerTextureFormat.Automatic ? T("automatic") : format.ToString());
            var formatField = new PopupField<string>(T("format"), formatLabels, formats.IndexOf(settings.Format));
            formatField.AddToClassList("at-field");
            formatField.tooltip = T("formatHint");
            formatField.RegisterValueChangedCallback(e => { settings.Format = formats[formatLabels.IndexOf(e.newValue)]; commit(); });
            parent.Add(formatField);
            parent.Add(presets);
            parent.Add(ratio);

            if (texture == null) parent.Add(Text(T("formatHint")));
        }

        private void SettingsFields(VisualElement parent, TextureSettings settings, TextureEntry texture, Action commit, bool includeCompression = true)
        {
            if (includeCompression) CompressionFields(parent, settings, texture, commit);
            Toggle(parent, "keepResolution", settings.KeepResolution, value => { settings.KeepResolution = value; commit(); });
            var max = new IntegerField(T("maxResolution")) { value = settings.MaxResolution, isDelayed = true };
            max.AddToClassList("at-field");
            max.RegisterValueChangedCallback(e => { settings.MaxResolution = Math.Max(0, e.newValue); commit(); });
            parent.Add(max);

            Toggle(parent, "protectQuality", settings.ProtectQuality, value => { settings.ProtectQuality = value; commit(); });
            Toggle(parent, "excluded", settings.Excluded, value => { settings.Excluded = value; commit(); });
            EnumPopup(parent, "effort", settings.Effort, value => { settings.Effort = value; commit(); });
            EnumPopup(parent, "platform", settings.Platform, value => { settings.Platform = value; commit(); });

            var psnr = new FloatField(T("minPsnr")) { value = (float)settings.MinPsnr, isDelayed = true };
            psnr.AddToClassList("at-field");
            psnr.RegisterValueChangedCallback(e => { settings.MinPsnr = Math.Max(0, Math.Min(200, e.newValue)); commit(); });
            parent.Add(psnr);

            var ssim = new FloatField(T("minSsim")) { value = (float)settings.MinSsim, isDelayed = true };
            ssim.AddToClassList("at-field");
            ssim.RegisterValueChangedCallback(e => { settings.MinSsim = Math.Max(0, Math.Min(1, e.newValue)); commit(); });
            parent.Add(ssim);
        }

        private static void Toggle(VisualElement parent, string key, bool value, Action<bool> change)
        {
            var field = new Toggle(T(key)) { value = value };
            field.RegisterValueChangedCallback(e => change(e.newValue));
            parent.Add(field);
        }

        // ===================================================================== master inventory

        private List<PlannedTexture> GetFilteredTextures()
        {
            var list = new List<PlannedTexture>();
            if (controller.Plan != null)
            {
                foreach (var item in controller.Plan.Textures)
                {
                    if ((categoryFilter == 0 || (int)item.Category == categoryFilter - 1) &&
                        (string.IsNullOrEmpty(search) ||
                         (item.TextureName ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         (item.TexturePath ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        list.Add(item);
                    }
                }
            }

            list.Sort((a, b) =>
            {
                var compare = 0;
                if (sort == 1) compare = (b.Plan == null ? 0 : b.Plan.SourceBytes).CompareTo(a.Plan == null ? 0 : a.Plan.SourceBytes);
                if (sort == 2) compare = (b.Plan == null ? 0 : b.Plan.SavingBytes).CompareTo(a.Plan == null ? 0 : a.Plan.SavingBytes);
                return compare != 0 ? compare : string.Compare(a.TextureName, b.TextureName, StringComparison.OrdinalIgnoreCase);
            });

            return list;
        }

        private void RebuildMasterList()
        {
            if (masterListContainer == null) return;
            masterListContainer.Clear();
            cardElements.Clear();

            var list = GetFilteredTextures();
            var pages = Math.Max(1, (list.Count + PageSize - 1) / PageSize);
            page = Math.Max(0, Math.Min(page, pages - 1));

            // Paging bar
            masterHeaderContainer.Clear();
            var paging = Row();
            paging.style.justifyContent = Justify.SpaceBetween;
            paging.style.marginBottom = 6;

            var leftPaging = Row();
            var previous = Button("previous", () => { page--; RebuildMasterList(); });
            previous.AddToClassList("at-btn");
            previous.SetEnabled(page > 0);
            leftPaging.Add(previous);

            leftPaging.Add(Text(UiStrings.T("workspace.page", (page + 1).ToString(), pages.ToString(), list.Count.ToString())));

            var next = Button("next", () => { page++; RebuildMasterList(); });
            next.AddToClassList("at-btn");
            next.SetEnabled(page + 1 < pages);
            leftPaging.Add(next);
            paging.Add(leftPaging);
            var selectAll = new Toggle(T("selectAll")) { value = TextureRows.AllIncluded(list, selected) };
            selectAll.tooltip = T("selectAllHint");
            selectAll.SetEnabled(list.Exists(item => !string.IsNullOrEmpty(item.TextureGuid)));
            selectAll.RegisterValueChangedCallback(e =>
            {
                TextureRows.SetIncluded(GetFilteredTextures(), selected, e.newValue);
                Mutated();
            });
            paging.Add(selectAll);

            masterHeaderContainer.Add(paging);

            if (list.Count == 0)
            {
                var empty = Text(T("noTextures"));
                empty.style.color = UiTheme.TextSecondary;
                empty.style.marginTop = 12;
                masterListContainer.Add(empty);
                return;
            }

            for (var i = page * PageSize; i < Math.Min(list.Count, (page + 1) * PageSize); i++)
            {
                AddTextureRow(masterListContainer, list[i]);
            }
        }

        private void AddTextureRow(VisualElement parent, PlannedTexture item)
        {
            var isCurrent = string.Equals(currentGuid, item.TextureGuid, StringComparison.OrdinalIgnoreCase);
            var isIncluded = selected.Contains(item.TextureGuid);

            var card = new VisualElement();
            card.AddToClassList("at-workspace-card");
            card.style.backgroundColor = isCurrent ? UiTheme.SelectedBackground : UiTheme.CardBackground;
            SetCardBorders(card, isCurrent ? UiTheme.Accent : (isIncluded ? UiTheme.Success : UiTheme.CardBorder), UiTheme.CardBorder);
            card.style.borderLeftWidth = 3;

            cardElements[item.TextureGuid] = card;

            var topRow = Row();
            topRow.style.alignItems = Align.Center;

            var check = new Toggle { value = isIncluded };
            check.tooltip = T("selectTexture");
            check.RegisterValueChangedCallback(e =>
            {
                if (e.newValue) selected.Add(item.TextureGuid);
                else selected.Remove(item.TextureGuid);
                Mutated();
            });
            topRow.Add(check);

            var asset = AssetDatabase.LoadAssetAtPath<Texture>(item.TexturePath);
            var thumbnail = asset == null ? null : AssetPreview.GetMiniThumbnail(asset);
            if (thumbnail != null)
            {
                var image = new Image { image = thumbnail };
                image.AddToClassList("at-workspace-thumb");
                topRow.Add(image);
            }

            var textCol = new VisualElement();
            textCol.style.flexGrow = 1;
            textCol.style.minWidth = 100;

            var choose = new Button(() => SelectTexture(item.TextureGuid)) { text = item.TextureName ?? item.TexturePath };
            choose.AddToClassList("at-btn");
            choose.style.unityFontStyleAndWeight = FontStyle.Bold;
            choose.style.fontSize = 11;
            textCol.Add(choose);

            var texture = controller.Report != null ? controller.Report.FindTexture(item.TextureGuid) : null;
            var confidence = texture == null ? T("unknown") : EnumText(texture.CategoryConfidence);
            var source = item.EffectiveSettings == null ? T("unknown") : EnumText(item.EffectiveSettings.Source);
            var subText = new Label(EnumText(item.Category) + " · " + confidence + " · " + source);
            subText.style.fontSize = 10;
            subText.style.color = UiTheme.TextSecondary;
            textCol.Add(subText);
            topRow.Add(textCol);

            // Status or Savings Pill
            if (item.ProtectedByThirdParty)
            {
                var pill = new Label(T("protected"));
                pill.AddToClassList("at-workspace-pill");
                pill.style.backgroundColor = UiTheme.ChipBackground;
                pill.style.color = UiTheme.Warning;
                topRow.Add(pill);
            }
            else if (item.Plan != null)
            {
                if (item.Plan.IsExcluded)
                {
                    var pill = new Label(T("excluded"));
                    pill.AddToClassList("at-workspace-pill");
                    pill.style.backgroundColor = UiTheme.ChipBackground;
                    pill.style.color = UiTheme.Neutral;
                    topRow.Add(pill);
                }
                else if (item.Plan.IsNoOp)
                {
                    var pill = new Label(T("unchanged"));
                    pill.AddToClassList("at-workspace-pill");
                    pill.style.backgroundColor = UiTheme.ChipBackground;
                    pill.style.color = UiTheme.Neutral;
                    topRow.Add(pill);
                }
                else if (item.Plan.SavingBytes > 0)
                {
                    var pill = new Label("-" + Bytes(item.Plan.SavingBytes));
                    pill.AddToClassList("at-workspace-pill");
                    pill.style.backgroundColor = UiTheme.ChipBackground;
                    pill.style.color = UiTheme.Success;
                    pill.style.unityFontStyleAndWeight = FontStyle.Bold;
                    topRow.Add(pill);
                }
            }

            card.Add(topRow);

            if (item.Plan != null)
            {
                var bottomRow = Row();
                bottomRow.style.marginTop = 3;
                bottomRow.style.paddingLeft = 24;

                var plan = item.Plan;
                var resLabel = new Label(plan.SourceWidth + "×" + plan.SourceHeight + " → " + plan.TargetWidth + "×" + plan.TargetHeight);
                resLabel.style.fontSize = 10;
                resLabel.style.color = UiTheme.TextSecondary;
                resLabel.style.marginRight = 8;
                bottomRow.Add(resLabel);

                var fmtLabel = new Label(plan.SourceFormat + " → " + plan.TargetFormat);
                fmtLabel.style.fontSize = 10;
                fmtLabel.style.color = UiTheme.TextSecondary;
                fmtLabel.style.marginRight = 8;
                bottomRow.Add(fmtLabel);

                var memLabel = new Label(plan.SavingUnknown ? T("unknown") : Bytes(plan.SourceBytes) + " → " + Bytes(plan.TargetBytes));
                memLabel.style.fontSize = 10;
                memLabel.style.color = UiTheme.Text;
                bottomRow.Add(memLabel);

                card.Add(bottomRow);
            }

            parent.Add(card);
        }

        private void SelectTexture(string guid)
        {
            if (string.Equals(currentGuid, guid, StringComparison.OrdinalIgnoreCase)) return;
            DisposePreview();
            var prevGuid = currentGuid;
            currentGuid = guid;

            VisualElement oldCard;
            if (!string.IsNullOrEmpty(prevGuid) && cardElements.TryGetValue(prevGuid, out oldCard))
            {
                var isIncluded = selected.Contains(prevGuid);
                oldCard.style.backgroundColor = UiTheme.CardBackground;
                SetCardBorders(oldCard, isIncluded ? UiTheme.Success : UiTheme.CardBorder, UiTheme.CardBorder);
            }

            VisualElement newCard;
            if (!string.IsNullOrEmpty(guid) && cardElements.TryGetValue(guid, out newCard))
            {
                newCard.style.backgroundColor = UiTheme.SelectedBackground;
                SetCardBorders(newCard, UiTheme.Accent, UiTheme.CardBorder);
            }

            RebuildDetailPane();
        }

        // ===================================================================== detail inspector

        private void RebuildDetailPane()
        {
            if (detailScroll == null) return;
            detailScroll.Clear();

            if (string.IsNullOrEmpty(currentGuid))
            {
                var empty = new VisualElement();
                empty.style.alignItems = Align.Center;
                empty.style.justifyContent = Justify.Center;
                empty.style.paddingTop = 60;

                var emptyTitle = new Label(T("details"));
                emptyTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                emptyTitle.style.fontSize = 14;
                empty.Add(emptyTitle);

                var hint = Text(T("selectTexture"));
                hint.style.color = UiTheme.TextSecondary;
                hint.style.marginTop = 6;
                empty.Add(hint);

                detailScroll.Add(empty);
                return;
            }

            BuildDetails(detailScroll);
        }

        private void BuildDetails(VisualElement parent)
        {
            if (string.IsNullOrEmpty(currentGuid)) return;
            var texture = controller.Report != null ? controller.Report.FindTexture(currentGuid) : null;
            if (texture == null) return;

            PlannedTexture planned = null;
            if (controller.Plan != null)
                foreach (var item in controller.Plan.Textures)
                    if (string.Equals(item.TextureGuid, currentGuid, StringComparison.OrdinalIgnoreCase)) { planned = item; break; }

            // 1. Header Card (Texture identity, category, path)
            var idCard = new VisualElement();
            idCard.AddToClassList("at-card");
            idCard.style.backgroundColor = UiTheme.CardBackground;
            SetCardBorders(idCard, UiTheme.CardBorder, UiTheme.CardBorder);

            var titleRow = Row();
            titleRow.style.justifyContent = Justify.SpaceBetween;

            var nameLabel = new Label(texture.Name);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.fontSize = 13;
            titleRow.Add(nameLabel);

            var checkToggle = new Toggle(T("selectTexture")) { value = selected.Contains(currentGuid) };
            checkToggle.RegisterValueChangedCallback(e =>
            {
                if (e.newValue) selected.Add(currentGuid);
                else selected.Remove(currentGuid);
                Mutated();
            });
            titleRow.Add(checkToggle);
            idCard.Add(titleRow);

            var corrected = controller.Settings.CategoryCorrections.ContainsKey(currentGuid);
            var detected = corrected ? controller.Settings.CategoryCorrections[currentGuid] : texture.Category;

            var catRow = Row();
            catRow.style.marginTop = 6;
            EnumPopup(catRow, "category", detected, value => { controller.Settings.CategoryCorrections[currentGuid] = value; Mutated(); });
            if (corrected)
            {
                var resetCat = Button("resetCategoryCorrection", () => { controller.Settings.CategoryCorrections.Remove(currentGuid); Mutated(); });
                resetCat.AddToClassList("at-btn");
                catRow.Add(resetCat);
            }
            idCard.Add(catRow);

            var detectedLabel = Text(UiStrings.T("workspace.detected", EnumText(texture.Category), EnumText(texture.CategoryConfidence)));
            detectedLabel.style.color = UiTheme.TextSecondary;
            detectedLabel.style.fontSize = 10;
            idCard.Add(detectedLabel);

            foreach (var reason in texture.CategoryReasons)
            {
                var rLabel = Text(UiStrings.T(reason.Key, reason.Evidence));
                rLabel.style.color = UiTheme.TextSecondary;
                rLabel.style.fontSize = 10;
                idCard.Add(rLabel);
            }

            parent.Add(idCard);

            // 2. Settings Overrides Card
            var settingsCard = new VisualElement();
            settingsCard.AddToClassList("at-card");
            settingsCard.style.backgroundColor = UiTheme.CardBackground;
            SetCardBorders(settingsCard, UiTheme.CardBorder, UiTheme.CardBorder);

            Heading(settingsCard, "details");

            if (planned != null && planned.EffectiveSettings != null)
            {
                var srcLabel = Text(UiStrings.T("workspace.settingsSource", EnumText(planned.EffectiveSettings.Source)));
                srcLabel.style.color = UiTheme.TextSecondary;
                srcLabel.style.marginBottom = 6;
                settingsCard.Add(srcLabel);
            }

            TextureSettings custom;
            var hasCustom = controller.Settings.Textures.TryGetValue(currentGuid, out custom);
            var editable = hasCustom ? custom : controller.Settings.Resolve(currentGuid, texture.Category).Settings.Clone();
            var textureGuid = currentGuid;
            CompressionFields(settingsCard, editable, texture, () =>
            {
                controller.Settings.Textures[textureGuid] = editable;
                Mutated();
            });
            if (!hasCustom)
            {
                var createBtn = Button("createCustom", () =>
                {
                    controller.Settings.Textures[currentGuid] = controller.Settings.Resolve(currentGuid, texture.Category).Settings.Clone();
                    Mutated();
                });
                createBtn.AddToClassList("at-btn");
                settingsCard.Add(createBtn);
            }
            else
            {
                var resetBtn = Button("resetCustom", () => { controller.Settings.Textures.Remove(currentGuid); Mutated(); });
                resetBtn.AddToClassList("at-btn");
                settingsCard.Add(resetBtn);

                SettingsFields(settingsCard, custom, texture, Mutated, false);
            }

            if (planned != null && planned.Plan != null)
            {
                var plan = planned.Plan;
                var redLabel = Text(UiStrings.T("workspace.reduction", plan.RequestedReduction.ToString("0.##", CultureInfo.InvariantCulture), plan.SavingUnknown || plan.ReductionAchieved <= 0 ? T("unknown") : plan.ReductionAchieved.ToString("0.##", CultureInfo.InvariantCulture)));
                redLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                redLabel.style.marginTop = 6;
                settingsCard.Add(redLabel);

                if (!string.IsNullOrEmpty(plan.SkipReasonKey))
                {
                    var skipLabel = Text(UiStrings.T(plan.SkipReasonKey));
                    skipLabel.style.color = UiTheme.Warning;
                    settingsCard.Add(skipLabel);
                }

                foreach (var note in plan.Notes)
                {
                    settingsCard.Add(Text(UiStrings.T(note.Key, note.Args.ToArray())));
                }
            }

            parent.Add(settingsCard);

            // 3. References Card
            var refCard = new VisualElement();
            refCard.AddToClassList("at-card");
            refCard.style.backgroundColor = UiTheme.CardBackground;
            SetCardBorders(refCard, UiTheme.CardBorder, UiTheme.CardBorder);

            Heading(refCard, "references");
            AddAsset(refCard, texture.Name, texture.Path);

            foreach (var guid in texture.UsedByMaterialGuids)
            {
                var material = controller.Report.FindMaterial(guid);
                if (material == null) continue;
                AddAsset(refCard, material.Name, material.Path);
                if (!string.IsNullOrEmpty(material.ShaderName))
                {
                    var sLabel = Text(UiStrings.T("workspace.shader", material.ShaderName));
                    sLabel.style.color = UiTheme.TextSecondary;
                    sLabel.style.fontSize = 10;
                    refCard.Add(sLabel);
                }
            }

            foreach (var path in texture.RendererPaths)
            {
                var row = Row();
                row.Add(Text(UiStrings.T("workspace.renderer", path)));
                GameObject target = null;
                if (controller.Avatar != null)
                {
                    foreach (var transform in controller.Avatar.GetComponentsInChildren<Transform>(true))
                    {
                        var actualPath = AvatarScanner.HierarchyPath(transform);
                        if (string.Equals(actualPath, path, StringComparison.OrdinalIgnoreCase) || actualPath.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase))
                        {
                            target = transform.gameObject;
                            break;
                        }
                    }
                }
                var captured = target;
                var ping = Button("ping", () => EditorGUIUtility.PingObject(captured));
                ping.AddToClassList("at-btn");
                ping.SetEnabled(captured != null);
                row.Add(ping);

                var select = Button("select", () => Selection.activeObject = captured);
                select.AddToClassList("at-btn");
                select.SetEnabled(captured != null);
                row.Add(select);
                refCard.Add(row);
            }

            foreach (var property in texture.BoundProperties)
            {
                var pLabel = Text(UiStrings.T("workspace.property", property));
                pLabel.style.color = UiTheme.TextSecondary;
                pLabel.style.fontSize = 10;
                refCard.Add(pLabel);
            }

            parent.Add(refCard);

            // 4. Preview Comparison Card
            var previewCard = new VisualElement();
            previewCard.AddToClassList("at-card");
            previewCard.style.backgroundColor = UiTheme.CardBackground;
            SetCardBorders(previewCard, UiTheme.CardBorder, UiTheme.CardBorder);
            BuildPreview(previewCard);
            parent.Add(previewCard);
        }

        private static void AddAsset(VisualElement parent, string name, string path)
        {
            var row = Row();
            var nameLabel = Text(name);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.marginRight = 8;
            row.Add(nameLabel);

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            var ping = Button("ping", () => EditorGUIUtility.PingObject(asset));
            ping.AddToClassList("at-btn");
            ping.SetEnabled(asset != null);
            row.Add(ping);

            var select = Button("select", () => Selection.activeObject = asset);
            select.AddToClassList("at-btn");
            select.SetEnabled(asset != null);
            row.Add(select);

            parent.Add(row);
        }

        private void BuildPreview(VisualElement parent)
        {
            Heading(parent, "preview");
            var generate = Button("generatePreview", () =>
            {
                DisposePreview();
                if (Execute(false)) preview = controller.GetPreview(currentGuid);
                RebuildDetailPane();
            });
            generate.AddToClassList("at-btn");
            generate.SetEnabled(selected.Contains(currentGuid) && controller.CompatibilityAllows && !WindowFlow.IsBusy(controller.State));
            parent.Add(generate);

            if (preview == null) { parent.Add(Text(T("previewHint"))); return; }
            if (!string.IsNullOrEmpty(preview.ErrorKey)) { parent.Add(Text(UiStrings.T(preview.ErrorKey))); return; }
            if (preview.Original == null || preview.Optimized == null) { parent.Add(Text(T("previewUnavailable"))); return; }

            var slider = new Slider(T("zoom"), 0.25f, 4f) { value = zoom };
            slider.RegisterValueChangedCallback(e => { zoom = e.newValue; RebuildDetailPane(); });
            parent.Add(slider);

            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.style.height = 300;
            var row = Row();
            row.style.flexWrap = Wrap.NoWrap;
            AddPreviewImage(row, "original", preview.Original, preview.Original.width, preview.Original.height);
            AddPreviewImage(row, "optimized", preview.Optimized, preview.Original.width, preview.Original.height);
            if (preview.Difference != null) AddPreviewImage(row, "difference", preview.Difference, preview.Original.width, preview.Original.height);
            scroll.Add(row);
            parent.Add(scroll);
        }

        private void AddPreviewImage(VisualElement parent, string key, Texture2D texture, int width, int height)
        {
            var column = new VisualElement();
            column.style.marginRight = 10;
            var label = Text(T(key));
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            column.Add(label);

            var image = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit };
            image.style.width = width * zoom;
            image.style.height = height * zoom;
            column.Add(image);
            parent.Add(column);
        }

        // ===================================================================== execution

        private bool Execute(bool apply)
        {
            if (controller.Settings.Mode != OptimizationMode.Advanced)
            {
                controller.SetOptimizationMode(OptimizationMode.Advanced);
                if (changed != null) changed();
                Refresh();
            }

            if (selected.Count == 0 || controller.Report == null || !controller.CompatibilityAllows || WindowFlow.IsBusy(controller.State)) return false;
            cancelled = false;
            DisposePreview();
            bool succeeded;
            try
            {
                Action<float, string> progress = (value, message) =>
                {
                    if (EditorUtility.DisplayCancelableProgressBar(T(apply ? "applying" : "evaluating"), UiStrings.T(message ?? "opt.phase.candidates"), Mathf.Clamp01(value))) cancelled = true;
                };
                succeeded = apply ? controller.ConfirmAndOptimize(progress, () => cancelled)
                    : controller.EvaluateRecommendations(progress, () => cancelled);
            }
            finally { EditorUtility.ClearProgressBar(); }
            if (changed != null) changed();
            Refresh();
            return succeeded;
        }
    }
}
