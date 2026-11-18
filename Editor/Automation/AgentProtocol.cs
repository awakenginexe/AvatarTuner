using System;
using System.Collections.Generic;
using System.Globalization;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Optimization;
using AvatarTuner.Editor.ViewModels;

namespace AvatarTuner.Editor.Automation
{
    internal sealed class AgentRequest
    {
        internal string Action;
        internal string PreviewToken;
        internal OptimizationSettings Settings;

        internal string FindUnknownTexture(AvatarAnalysisReport report)
        {
            if (Settings == null) return null;
            var guids = new List<string>(Settings.IncludedTextures);
            guids.AddRange(Settings.Textures.Keys);
            guids.AddRange(Settings.CategoryCorrections.Keys);
            foreach (var guid in guids)
            {
                var texture = report.FindTexture(guid);
                if (texture == null || !string.Equals(texture.Guid, guid, StringComparison.Ordinal)) return guid;
            }
            return null;
        }

        internal OptimizationSettings ResolveSettings(OptimizationSettings saved, IDictionary<string, TextureOverride> overrides)
        {
            if (Settings == null) return saved;
            var exclusions = new List<string>();
            foreach (var pair in overrides)
                if (pair.Value != null && pair.Value.Excluded) exclusions.Add(pair.Key);
            overrides.Clear();
            foreach (var guid in exclusions) overrides[guid] = new TextureOverride { Excluded = true };
            return Settings;
        }

        internal static AgentRequest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 65536)
                throw new ArgumentException("Request must be a JSON object of at most 65536 characters.");
            Dictionary<string, object> data;
            try { data = MiniJson.ParseObject(json); }
            catch (MiniJsonException ex) { throw new ArgumentException(ex.Message); }
            object actionValue;
            var action = data.TryGetValue("action", out actionValue) ? actionValue as string : null;
            if (action != "analyze" && action != "preview" && action != "optimize" && action != "restore")
                throw new ArgumentException("action must be analyze, preview, optimize or restore.");
            foreach (var key in data.Keys)
                if (key != "action" && !(action == "preview" && key == "settings") && !(action == "optimize" && key == "previewToken"))
                    throw new ArgumentException("Unexpected request field: " + key);
            var request = new AgentRequest { Action = action };
            object value;
            if (data.TryGetValue("settings", out value))
            {
                var settings = value as Dictionary<string, object>;
                if (settings == null) throw new ArgumentException("settings must be an object.");
                request.Settings = OptimizationSettings.FromDictionary(settings);
                ValidateSupplied(settings, request.Settings.ToDictionary(), "settings");
            }
            if (action == "optimize")
            {
                request.PreviewToken = data.TryGetValue("previewToken", out value) ? value as string : null;
                if (string.IsNullOrWhiteSpace(request.PreviewToken)) throw new ArgumentException("optimize requires the previewToken returned by preview.");
            }
            return request;
        }

        // Saved settings are normalized; agent requests must report invalid values instead.
        private static void ValidateSupplied(object supplied, object normalized, string path)
        {
            var fields = supplied as Dictionary<string, object>;
            if (fields != null && normalized is Dictionary<string, object> output)
            {
                foreach (var field in fields)
                {
                    object actual;
                    if (!output.TryGetValue(field.Key, out actual)) throw new ArgumentException("Unknown field: " + path + "." + field.Key);
                    ValidateSupplied(field.Value, actual, path + "." + field.Key);
                }
                return;
            }
            var items = supplied as List<object>;
            if (items != null && normalized is List<object> normalizedItems && items.Count == normalizedItems.Count)
            {
                foreach (var item in items)
                    if (!(item is string) || !normalizedItems.Contains(item)) throw new ArgumentException("Invalid value: " + path);
                return;
            }
            if ((supplied is long || supplied is double) && (normalized is int || normalized is long || normalized is double))
            {
                var number = Convert.ToDouble(supplied, CultureInfo.InvariantCulture);
                if (!double.IsNaN(number) && !double.IsInfinity(number) && number == Convert.ToDouble(normalized, CultureInfo.InvariantCulture)) return;
            }
            else if (supplied != null && supplied.Equals(normalized)) return;
            throw new ArgumentException("Invalid value: " + path + ". See Editor/Automation/README.md.");
        }
    }

    internal static class AgentReport
    {
        internal static Dictionary<string, object> Build(AvatarAnalysisReport report, TextureOptimizationPlan plan,
            TextureGenerationResult generation, MemoryNumbers memory)
        {
            var findings = new List<object>();
            var textures = new List<object>();
            var plans = new Dictionary<string, PlannedTexture>(StringComparer.OrdinalIgnoreCase);
            var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (plan != null)
            {
                foreach (var texture in plan.Textures) plans[texture.TextureGuid] = texture;
                AddFindings(findings, plan.Findings, "plan", "Notice");
            }
            if (generation != null)
            {
                foreach (var output in generation.Generated) outputs[output.SourceGuid] = output.GeneratedPath;
                AddFindings(findings, generation.Findings, "generation", "Warning");
            }
            if (report != null)
            {
                AddFindings(findings, report.Findings, "analysis", "Notice");
                AddFindings(findings, report.Compatibility.Notices, "compatibility", "Notice");
                AddFindings(findings, report.Compatibility.Warnings, "compatibility", "Warning");
                AddFindings(findings, report.Compatibility.Blockers, "compatibility", "Blocker");
                foreach (var texture in report.Textures)
                {
                    PlannedTexture planned;
                    string generatedPath;
                    textures.Add(new Dictionary<string, object>
                    {
                        { "guid", texture.Guid }, { "path", texture.Path }, { "name", texture.Name },
                        { "category", texture.Category.ToString() }, { "usage", texture.Usage.ToString() },
                        { "width", texture.Width }, { "height", texture.Height }, { "format", texture.CurrentFormatName },
                        { "plan", plans.TryGetValue(texture.Guid, out planned) && planned.Plan != null ? Decision(planned) : null },
                        { "generatedPath", outputs.TryGetValue(texture.Guid, out generatedPath) ? generatedPath : null }
                    });
                }
            }
            return new Dictionary<string, object>
            {
                { "scanSucceeded", report != null && report.ScanSucceeded }, { "scanErrorKey", report == null ? null : report.ErrorMessage },
                { "optimizationAllowed", report != null && report.Compatibility.IsOptimizationAllowed },
                { "hasChanges", plan != null && plan.HasChanges }, { "textures", textures }, { "findings", findings },
                { "outputFolder", generation == null ? null : generation.OutputFolder },
                { "memory", new Dictionary<string, object>
                    {
                        { "unit", "bytes" }, { "basis", memory.Basis.ToString() },
                        { "sourceBytes", memory.HasSource ? (object)memory.SourceBytes : null },
                        { "outputBytes", memory.HasOutput ? (object)memory.OutputBytes : null },
                        { "currentBytes", memory.HasCurrent ? (object)memory.CurrentBytes : null },
                        { "knownSourceBytes", memory.SourceBytes }, { "knownOutputBytes", memory.OutputBytes },
                        { "unknownSourceCount", memory.UnknownSourceCount }, { "unknownOutputCount", memory.UnknownOutputCount },
                        { "savingsArePotential", memory.SavingsArePotential }, { "currentFromSdk", memory.CurrentFromSdk },
                        { "optimizedCount", memory.OptimizedCount }, { "skippedCount", memory.SkippedCount },
                        { "excludedCount", memory.ExcludedCount }, { "reviewCount", memory.ReviewCount }
                    }
                }
            };
        }

        private static Dictionary<string, object> Decision(PlannedTexture texture)
        {
            var plan = texture.Plan;
            var notes = new List<object>();
            foreach (var note in plan.Notes)
                notes.Add(new Dictionary<string, object> { { "key", note.Key }, { "args", note.Args }, { "severity", note.Level.ToString() } });
            return new Dictionary<string, object>
            {
                { "changed", texture.HasChanges }, { "excluded", plan.IsExcluded }, { "protectedByThirdParty", texture.ProtectedByThirdParty },
                { "skipReasonKey", plan.SkipReasonKey }, { "width", plan.TargetWidth }, { "height", plan.TargetHeight },
                { "format", plan.TargetFormat.ToString() }, { "mipmaps", plan.TargetMipMaps },
                { "bytes", plan.TargetBytesKnown ? (object)plan.TargetBytes : null }, { "notes", notes }
            };
        }

        private static void AddFindings(List<object> result, List<AnalysisFinding> findings, string source, string severity)
        {
            foreach (var finding in findings)
                result.Add(new Dictionary<string, object> { { "key", finding.MessageKey }, { "args", finding.Args },
                    { "source", source }, { "severity", severity }, { "risk", finding.Risk.ToString() } });
        }
    }
}
