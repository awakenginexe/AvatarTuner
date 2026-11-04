using System;
using System.Collections.Generic;
using System.Globalization;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AvatarTuner.Editor.UI
{
    public sealed class OptimizationWorkspace : EditorWindow
    {
        private const int PageSize = 50;
        private AvatarTunerController controller;
        private Action changed;
        private CandidatePreview preview;
        private readonly HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string currentGuid;
        private string search = string.Empty;
        private int categoryFilter;
        private int sort;
        private int page;
        private SemanticCategory category = SemanticCategory.Face;
        private float zoom = 1f;
        private bool cancelled;

        public static void Open(AvatarTunerController controller, Action changed)
        {
            var window = GetWindow<OptimizationWorkspace>();
            window.titleContent = new GUIContent(UiStrings.T("workspace.title"));
            window.minSize = new Vector2(420, 400);
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
            parent.Add(label);
        }
        private void Refresh()
        {
            if (rootVisualElement == null) return;
            rootVisualElement.Clear();
            if (controller == null || controller.Settings == null || controller.Report == null)
            {
                rootVisualElement.Add(Text(T("reopen")));
                return;
            }
            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            rootVisualElement.Add(scroll);
            BuildActions(scroll);
            BuildSettings(scroll);
            BuildInventory(scroll);
            BuildDetails(scroll);
        }
        private void Mutated()
        {
            DisposePreview();
            controller.SaveSettings();
            controller.RebuildPlan();
            if (changed != null) changed();
            Refresh();
        }
        private void BuildActions(VisualElement parent)
        {
            var row = Row();
            var presets = (OptimizationPreset[])Enum.GetValues(typeof(OptimizationPreset));
            var labels = new List<string>();
            foreach (var value in presets) labels.Add(EnumText(value));
            var preset = new PopupField<string>(T("preset"), labels, Math.Max(0, Array.IndexOf(presets, controller.Preset)));
            preset.RegisterValueChangedCallback(e => { controller.ApplyPreset(presets[labels.IndexOf(e.newValue)]); Mutated(); });
            row.Add(preset);
            var evaluate = Button("evaluate", () => Execute(false));
            evaluate.style.color = UiTheme.Accent;
            row.Add(evaluate);
            var apply = Button("apply", () => Execute(true));
            apply.SetEnabled(controller.HasEvaluatedPlan && controller.Plan != null && controller.Plan.HasChanges && controller.CompatibilityAllows);
            row.Add(apply);
            parent.Add(row);
            parent.Add(Text(T("evaluateHint")));
            if (!string.IsNullOrEmpty(controller.ErrorKey)) parent.Add(Text(UiStrings.T(controller.ErrorKey)));
            if (controller.Plan != null)
            {
                var p = controller.Plan;
                var memory = controller.PlanMemoryNumbers();
                parent.Add(Text(UiStrings.T("workspace.summary", memory.HasSource ? Bytes(memory.SourceBytes) : T("unknown"),
                    memory.HasOutput ? Bytes(memory.OutputBytes) : T("unknown"), p.ChangedCount.ToString(CultureInfo.InvariantCulture))));
                parent.Add(Text(UiStrings.T("workspace.memory.PlannedEstimate")));
                if (!memory.HasSource || !memory.HasOutput)
                    parent.Add(Text(UiStrings.T("workspace.memoryUnknown", memory.UnknownSourceCount.ToString(), memory.UnknownOutputCount.ToString())));
            }
        }
        private void Execute(bool apply)
        {
            if (apply)
            {
                if (!controller.HasEvaluatedPlan || controller.Plan == null || !controller.Plan.HasChanges || !controller.CompatibilityAllows) return;
                var p = controller.Plan;
                var memory = controller.PlanMemoryNumbers();
                if (!EditorUtility.DisplayDialog(T("confirmTitle"), UiStrings.T("workspace.confirmBody",
                    memory.HasSource ? Bytes(memory.SourceBytes) : T("unknown"), memory.HasOutput ? Bytes(memory.OutputBytes) : T("unknown"),
                    p.ChangedCount.ToString(CultureInfo.InvariantCulture)), T("apply"), T("cancel"))) return;
            }
            cancelled = false;
            DisposePreview();
            try
            {
                Action<float, string> progress = (value, message) =>
                {
                    if (EditorUtility.DisplayCancelableProgressBar(T(apply ? "applying" : "evaluating"), UiStrings.T(message ?? "opt.phase.candidates"), Mathf.Clamp01(value))) cancelled = true;
                };
                if (apply) controller.RunOptimization(progress, () => cancelled);
                else controller.EvaluateRecommendations(progress, () => cancelled);
            }
            finally { EditorUtility.ClearProgressBar(); }
            if (changed != null) changed();
            Refresh();
        }
        private static string Bytes(long value) { return TextureFormatInfo.FormatBytes(value); }

        private void BuildSettings(VisualElement parent)
        {
            var global = new Foldout { text = T("global"), value = false };
            parent.Add(global);
            SettingsFields(global, controller.Settings.Global, null, Mutated);
            var group = new Foldout { text = T("categorySettings"), value = false };
            parent.Add(group);
            EnumPopup(group, "category", category, value => { category = value; Refresh(); });
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
            field.RegisterValueChangedCallback(e => onChange(values[labels.IndexOf(e.newValue)]));
            parent.Add(field);
        }
        private void SettingsFields(VisualElement parent, TextureSettings settings, TextureEntry texture, Action commit)
        {
            var ratios = new List<string> { "1x", "2x", "4x", "8x", "16x" };
            var values = new[] { 1d, 2d, 4d, 8d, 16d };
            var index = Array.IndexOf(values, settings.DesiredReduction);
            var ratio = new PopupField<string>(T("ratio"), ratios, Math.Max(0, index));
            ratio.tooltip = T("ratioHelp");
            ratio.RegisterValueChangedCallback(e => { settings.DesiredReduction = values[ratios.IndexOf(e.newValue)]; commit(); });
            parent.Add(ratio);
            var formats = texture == null ? new List<TunerTextureFormat> { TunerTextureFormat.Automatic, TunerTextureFormat.BC1, TunerTextureFormat.BC3, TunerTextureFormat.BC4, TunerTextureFormat.BC5, TunerTextureFormat.BC7, TunerTextureFormat.ASTC_4x4, TunerTextureFormat.ASTC_6x6, TunerTextureFormat.ASTC_8x8 } : controller.AllowedFormats(texture);
            if (formats == null) formats = new List<TunerTextureFormat>();
            if (!formats.Contains(TunerTextureFormat.Automatic)) formats.Insert(0, TunerTextureFormat.Automatic);
            if (!formats.Contains(settings.Format)) formats.Add(settings.Format);
            var formatLabels = new List<string>();
            foreach (var format in formats) formatLabels.Add(format == TunerTextureFormat.Automatic ? T("automatic") : format.ToString());
            var formatField = new PopupField<string>(T("format"), formatLabels, formats.IndexOf(settings.Format));
            formatField.RegisterValueChangedCallback(e => { settings.Format = formats[formatLabels.IndexOf(e.newValue)]; commit(); });
            parent.Add(formatField);
            if (texture == null) parent.Add(Text(T("formatHint")));
            Toggle(parent, "keepResolution", settings.KeepResolution, value => { settings.KeepResolution = value; commit(); });
            var max = new IntegerField(T("maxResolution")) { value = settings.MaxResolution, isDelayed = true };
            max.RegisterValueChangedCallback(e => { settings.MaxResolution = Math.Max(0, e.newValue); commit(); }); parent.Add(max);
            Toggle(parent, "protectQuality", settings.ProtectQuality, value => { settings.ProtectQuality = value; commit(); });
            Toggle(parent, "excluded", settings.Excluded, value => { settings.Excluded = value; commit(); });
            EnumPopup(parent, "effort", settings.Effort, value => { settings.Effort = value; commit(); });
            EnumPopup(parent, "platform", settings.Platform, value => { settings.Platform = value; commit(); });
            var psnr = new FloatField(T("minPsnr")) { value = (float)settings.MinPsnr, isDelayed = true };
            psnr.RegisterValueChangedCallback(e => { settings.MinPsnr = Math.Max(0, Math.Min(200, e.newValue)); commit(); }); parent.Add(psnr);
            var ssim = new FloatField(T("minSsim")) { value = (float)settings.MinSsim, isDelayed = true };
            ssim.RegisterValueChangedCallback(e => { settings.MinSsim = Math.Max(0, Math.Min(1, e.newValue)); commit(); }); parent.Add(ssim);
        }
        private static void Toggle(VisualElement parent, string key, bool value, Action<bool> change)
        {
            var field = new Toggle(T(key)) { value = value };
            field.RegisterValueChangedCallback(e => change(e.newValue)); parent.Add(field);
        }
        private void BuildInventory(VisualElement parent)
        {
            Heading(parent, "inventory");
            var controls = Row();
            var query = new TextField(T("search")) { value = search, isDelayed = true };
            query.RegisterValueChangedCallback(e => { search = e.newValue ?? string.Empty; page = 0; Refresh(); }); controls.Add(query);
            var categories = new List<string> { T("allCategories") };
            foreach (SemanticCategory value in Enum.GetValues(typeof(SemanticCategory))) categories.Add(EnumText(value));
            var filter = new PopupField<string>(T("filter"), categories, categoryFilter);
            filter.RegisterValueChangedCallback(e => { categoryFilter = categories.IndexOf(e.newValue); page = 0; Refresh(); }); controls.Add(filter);
            var sorts = new List<string> { T("sortName"), T("sortMemory"), T("sortSavings") };
            var order = new PopupField<string>(T("sort"), sorts, sort);
            order.RegisterValueChangedCallback(e => { sort = sorts.IndexOf(e.newValue); page = 0; Refresh(); }); controls.Add(order);
            parent.Add(controls);
            var list = new List<PlannedTexture>();
            if (controller.Plan != null)
                foreach (var item in controller.Plan.Textures)
                    if ((categoryFilter == 0 || (int)item.Category == categoryFilter - 1) && (string.IsNullOrEmpty(search) || (item.TextureName ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || (item.TexturePath ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)) list.Add(item);
            list.Sort((a, b) =>
            {
                var compare = 0;
                if (sort == 1) compare = (b.Plan == null ? 0 : b.Plan.SourceBytes).CompareTo(a.Plan == null ? 0 : a.Plan.SourceBytes);
                if (sort == 2) compare = (b.Plan == null ? 0 : b.Plan.SavingBytes).CompareTo(a.Plan == null ? 0 : a.Plan.SavingBytes);
                return compare != 0 ? compare : string.Compare(a.TextureName, b.TextureName, StringComparison.OrdinalIgnoreCase);
            });
            var pages = Math.Max(1, (list.Count + PageSize - 1) / PageSize);
            page = Math.Max(0, Math.Min(page, pages - 1));
            var paging = Row();
            var previous = Button("previous", () => { page--; Refresh(); }); previous.SetEnabled(page > 0); paging.Add(previous);
            paging.Add(Text(UiStrings.T("workspace.page", (page + 1).ToString(), pages.ToString(), list.Count.ToString())));
            var next = Button("next", () => { page++; Refresh(); }); next.SetEnabled(page + 1 < pages); paging.Add(next);
            var selectPage = Button("selectPage", () => { for (var i = page * PageSize; i < Math.Min(list.Count, (page + 1) * PageSize); i++) selected.Add(list[i].TextureGuid); Refresh(); }); paging.Add(selectPage);
            paging.Add(Button("clearSelection", () => { selected.Clear(); Refresh(); }));
            parent.Add(paging);
            if (list.Count == 0) parent.Add(Text(T("noTextures")));
            for (var i = page * PageSize; i < Math.Min(list.Count, (page + 1) * PageSize); i++) AddTextureRow(parent, list[i]);
            BuildBulk(parent);
        }
        private void AddTextureRow(VisualElement parent, PlannedTexture item)
        {
            var row = Row();
            if (string.Equals(currentGuid, item.TextureGuid, StringComparison.OrdinalIgnoreCase)) row.style.backgroundColor = UiTheme.SelectedBackground;
            var check = new Toggle { value = selected.Contains(item.TextureGuid) };
            check.tooltip = T("selectTexture");
            check.RegisterValueChangedCallback(e => { if (e.newValue) selected.Add(item.TextureGuid); else selected.Remove(item.TextureGuid); });
            row.Add(check);
            var asset = AssetDatabase.LoadAssetAtPath<Texture>(item.TexturePath);
            var thumbnail = asset == null ? null : AssetPreview.GetMiniThumbnail(asset);
            if (thumbnail != null)
            {
                var image = new Image { image = thumbnail };
                image.style.width = 32; image.style.height = 32; row.Add(image);
            }
            var choose = new Button(() => { DisposePreview(); currentGuid = item.TextureGuid; Refresh(); }) { text = item.TextureName ?? item.TexturePath };
            row.Add(choose);
            var texture = controller.Report.FindTexture(item.TextureGuid);
            var confidence = texture == null ? T("unknown") : EnumText(texture.CategoryConfidence);
            var source = item.EffectiveSettings == null ? T("unknown") : EnumText(item.EffectiveSettings.Source);
            row.Add(Text(EnumText(item.Category) + " · " + confidence + " · " + source));
            if (item.Plan != null)
            {
                var plan = item.Plan;
                row.Add(Text(plan.SourceWidth + "×" + plan.SourceHeight + " → " + plan.TargetWidth + "×" + plan.TargetHeight));
                row.Add(Text(plan.SourceFormat + " → " + plan.TargetFormat));
                row.Add(Text(plan.SavingUnknown ? T("unknown") : Bytes(plan.SourceBytes) + " → " + Bytes(plan.TargetBytes) + " (" + Bytes(plan.SavingBytes) + ")"));
                if (item.ProtectedByThirdParty) row.Add(Text(T("protected")));
                else if (plan.IsExcluded) row.Add(Text(T("excluded")));
                else if (plan.IsNoOp) row.Add(Text(T("unchanged")));
            }
            parent.Add(row);
        }
        private void BuildBulk(VisualElement parent)
        {
            var bulk = new Foldout { text = T("bulk"), value = false };
            parent.Add(bulk);
            bulk.Add(Text(T("bulkHint")));
            var keep = Button("bulkKeep", () => Bulk(s => s.KeepResolution = true));
            var resize = Button("bulkAllowResize", () => Bulk(s => s.KeepResolution = false));
            var exclude = Button("bulkExclude", () => Bulk(s => s.Excluded = true));
            var include = Button("bulkInclude", () => Bulk(s => s.Excluded = false));
            var row = Row(); row.Add(keep); row.Add(resize); row.Add(exclude); row.Add(include); bulk.Add(row);
            var ratios = new List<string> { T("chooseRatio"), "1x", "2x", "4x", "8x", "16x" };
            var values = new[] { 0d, 1d, 2d, 4d, 8d, 16d };
            var ratio = new PopupField<string>(T("ratio"), ratios, 0);
            ratio.RegisterValueChangedCallback(e => { var i = ratios.IndexOf(e.newValue); if (i > 0) Bulk(s => s.DesiredReduction = values[i]); }); bulk.Add(ratio);
            var formats = new[] { TunerTextureFormat.Automatic, TunerTextureFormat.BC1, TunerTextureFormat.BC3, TunerTextureFormat.BC4, TunerTextureFormat.BC5, TunerTextureFormat.BC7, TunerTextureFormat.ASTC_4x4, TunerTextureFormat.ASTC_6x6, TunerTextureFormat.ASTC_8x8 };
            var choices = new List<string> { T("chooseFormat") };
            foreach (var format in formats) choices.Add(format == TunerTextureFormat.Automatic ? T("automatic") : format.ToString());
            var formatField = new PopupField<string>(T("format"), choices, 0);
            formatField.RegisterValueChangedCallback(e => { var i = choices.IndexOf(e.newValue); if (i > 0) Bulk(s => s.Format = formats[i - 1]); }); bulk.Add(formatField);
            bulk.Add(Text(T("formatHint")));
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
        private void BuildDetails(VisualElement parent)
        {
            if (string.IsNullOrEmpty(currentGuid)) return;
            var texture = controller.Report.FindTexture(currentGuid);
            if (texture == null) return;
            PlannedTexture planned = null;
            if (controller.Plan != null) foreach (var item in controller.Plan.Textures) if (string.Equals(item.TextureGuid, currentGuid, StringComparison.OrdinalIgnoreCase)) { planned = item; break; }
            Heading(parent, "details");
            parent.Add(Text(texture.Name));
            var corrected = controller.Settings.CategoryCorrections.ContainsKey(currentGuid);
            var detected = corrected ? controller.Settings.CategoryCorrections[currentGuid] : texture.Category;
            EnumPopup(parent, "category", detected, value => { controller.Settings.CategoryCorrections[currentGuid] = value; Mutated(); });
            if (corrected) parent.Add(Button("resetCategoryCorrection", () => { controller.Settings.CategoryCorrections.Remove(currentGuid); Mutated(); }));
            parent.Add(Text(UiStrings.T("workspace.detected", EnumText(texture.Category), EnumText(texture.CategoryConfidence))));
            foreach (var reason in texture.CategoryReasons) parent.Add(Text(UiStrings.T(reason.Key, reason.Evidence)));
            if (planned != null && planned.EffectiveSettings != null) parent.Add(Text(UiStrings.T("workspace.settingsSource", EnumText(planned.EffectiveSettings.Source))));
            TextureSettings custom;
            if (!controller.Settings.Textures.TryGetValue(currentGuid, out custom))
                parent.Add(Button("createCustom", () => { controller.Settings.Textures[currentGuid] = controller.Settings.Resolve(currentGuid, texture.Category).Settings.Clone(); Mutated(); }));
            else
            {
                parent.Add(Button("resetCustom", () => { controller.Settings.Textures.Remove(currentGuid); Mutated(); }));
                SettingsFields(parent, custom, texture, Mutated);
            }
            if (planned != null && planned.Plan != null)
            {
                var plan = planned.Plan;
                parent.Add(Text(UiStrings.T("workspace.reduction", plan.RequestedReduction.ToString("0.##", CultureInfo.InvariantCulture), plan.SavingUnknown || plan.ReductionAchieved <= 0 ? T("unknown") : plan.ReductionAchieved.ToString("0.##", CultureInfo.InvariantCulture))));
                if (!string.IsNullOrEmpty(plan.SkipReasonKey)) parent.Add(Text(UiStrings.T(plan.SkipReasonKey)));
                foreach (var note in plan.Notes) parent.Add(Text(UiStrings.T(note.Key, note.Args.ToArray())));
            }
            Heading(parent, "references");
            AddAsset(parent, texture.Name, texture.Path);
            foreach (var guid in texture.UsedByMaterialGuids)
            {
                var material = controller.Report.FindMaterial(guid);
                if (material == null) continue;
                AddAsset(parent, material.Name, material.Path);
                if (!string.IsNullOrEmpty(material.ShaderName)) parent.Add(Text(UiStrings.T("workspace.shader", material.ShaderName)));
            }
            foreach (var path in texture.RendererPaths)
            {
                var row = Row();
                row.Add(Text(UiStrings.T("workspace.renderer", path)));
                GameObject target = null;
                if (controller.Avatar != null)
                    foreach (var transform in controller.Avatar.GetComponentsInChildren<Transform>(true))
                    {
                        var actualPath = AvatarScanner.HierarchyPath(transform);
                        if (string.Equals(actualPath, path, StringComparison.OrdinalIgnoreCase) || actualPath.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase)) { target = transform.gameObject; break; }
                    }
                var captured = target;
                var ping = Button("ping", () => EditorGUIUtility.PingObject(captured)); ping.SetEnabled(captured != null); row.Add(ping);
                var select = Button("select", () => Selection.activeObject = captured); select.SetEnabled(captured != null); row.Add(select);
                parent.Add(row);
            }
            foreach (var property in texture.BoundProperties) parent.Add(Text(UiStrings.T("workspace.property", property)));
            BuildPreview(parent);
        }
        private static void AddAsset(VisualElement parent, string name, string path)
        {
            var row = Row(); row.Add(Text(name));
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            var ping = Button("ping", () => EditorGUIUtility.PingObject(asset)); ping.SetEnabled(asset != null); row.Add(ping);
            var select = Button("select", () => Selection.activeObject = asset); select.SetEnabled(asset != null); row.Add(select);
            parent.Add(row);
        }
        private void BuildPreview(VisualElement parent)
        {
            Heading(parent, "preview");
            var generate = Button("generatePreview", () => { DisposePreview(); preview = controller.GetPreview(currentGuid); Refresh(); });
            generate.SetEnabled(controller.HasEvaluatedPlan);
            parent.Add(generate);
            if (preview == null) { parent.Add(Text(T("previewHint"))); return; }
            if (!string.IsNullOrEmpty(preview.ErrorKey)) { parent.Add(Text(UiStrings.T(preview.ErrorKey))); return; }
            if (preview.Original == null || preview.Optimized == null) { parent.Add(Text(T("previewUnavailable"))); return; }
            var slider = new Slider(T("zoom"), 0.25f, 4f) { value = zoom };
            slider.RegisterValueChangedCallback(e => { zoom = e.newValue; Refresh(); }); parent.Add(slider);
            var scroll = new ScrollView(ScrollViewMode.HorizontalAndVertical);
            scroll.style.height = 300;
            var row = Row(); row.style.flexWrap = Wrap.NoWrap;
            AddPreviewImage(row, "original", preview.Original, preview.Original.width, preview.Original.height);
            AddPreviewImage(row, "optimized", preview.Optimized, preview.Original.width, preview.Original.height);
            if (preview.Difference != null) AddPreviewImage(row, "difference", preview.Difference, preview.Original.width, preview.Original.height);
            scroll.Add(row); parent.Add(scroll);
        }
        private void AddPreviewImage(VisualElement parent, string key, Texture2D texture, int width, int height)
        {
            var column = new VisualElement(); column.Add(Text(T(key)));
            var image = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit };
            image.style.width = width * zoom; image.style.height = height * zoom;
            column.Add(image); parent.Add(column);
        }
    }
}
