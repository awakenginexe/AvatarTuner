// AvatarTuner v1.0.0 — window controller (the pipeline behind the UI).
//
// Owns the working set of the AvatarTuner window and sequences the existing engines:
//   scan (AvatarScanner) → plan (TexturePlanBuilder) → generate (TextureOptimizer)
//   → record (GeneratedAssetStore / ManifestStateBuilder) → status (ChangeTrackingService).
//
// Every public operation is defensive: a failure becomes a localized error key plus the
// window state that allows retrying, never an exception into IMGUI and never a half-shown
// window. Nothing here modifies source assets; the whole pipeline writes generated copies
// only.

using System;
using System.Collections.Generic;
using System.IO;
using AvatarTuner.Core;
using AvatarTuner.Editor.Analysis;
using AvatarTuner.Editor.Apply;
using AvatarTuner.Editor.Optimization;
using AvatarTuner.Editor.Tracking;
using AvatarTuner.Editor.ViewModels;
using UnityEditor;
using UnityEngine;

namespace AvatarTuner.Editor.UI
{
    public sealed class AvatarTunerController
    {
        // ===================================================================== configuration

        public GameObject Avatar;
        public OptimizationPreset Preset = OptimizationPreset.Balanced;

        /// <summary>Whether inactive GameObjects take part in the analysis.</summary>
        public bool IncludeInactive = true;

        /// <summary>Estimate quality from source pixels where the texture is readable.</summary>
        public bool MeasureQuality;
        public OptimizationSettings Settings = new OptimizationSettings();
        private OptimizationSettings PlanningSettings
        {
            get
            {
                var settings = Settings.ForCurrentMode();
                if (Settings.Mode == OptimizationMode.Standard)
                    settings.Global.Platform = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android
                        ? OptimizationPlatform.Android : OptimizationPlatform.Windows;
                return settings;
            }
        }
        private IDictionary<string, TextureOverride> PlanningOverrides
        {
            get
            {
                if (Settings.Mode == OptimizationMode.Advanced) return Overrides;
                var exclusions = new Dictionary<string, TextureOverride>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in Overrides) if (pair.Value != null && pair.Value.Excluded) exclusions[pair.Key] = pair.Value;
                return exclusions;
            }
        }
        public bool HasEvaluatedPlan;
        private readonly Dictionary<string, CandidateQuality> _candidateCache = new Dictionary<string, CandidateQuality>();
        private string _evaluatedRevision;

        /// <summary>Replace existing generated files without an extra confirmation.</summary>
        public bool OverwriteGenerated = true;

        /// <summary>Per-texture user overrides keyed by texture GUID.</summary>
        public readonly Dictionary<string, TextureOverride> Overrides =
            new Dictionary<string, TextureOverride>(StringComparer.OrdinalIgnoreCase);

        // ===================================================================== state

        public WindowState State = WindowState.NoSelection;

        /// <summary>Localized error key for the current state, or null.</summary>
        public string ErrorKey;
        public readonly List<string> ErrorArgs = new List<string>();

        public AvatarAnalysisReport Report;
        public TextureOptimizationPlan Plan;
        public TextureGenerationResult Generation;
        public ChangeReport ChangeStatus;
        public AvatarOptimizationState RecordedState;

        /// <summary>True once a generation run happened in this window session.</summary>
        public bool RunHappened;

        /// <summary>Whether that run completed successfully (false for fail/cancel).</summary>
        public bool LastRunSucceeded;

        /// <summary>Whether that run skipped textures or flagged textures for review.</summary>
        public bool LastRunHasWarnings;

        // ---- direct scene application state (reference-level)

        /// <summary>Reference-level health of the scene application, refreshed on observation.</summary>
        public ApplicationHealth ApplyHealth = ApplicationHealth.NotApplied;

        /// <summary>Renderer slots whose material reference drifted since application.</summary>
        public readonly List<string> ApplicationDriftEntries = new List<string>();

        /// <summary>Findings from the last scene application attempt.</summary>
        public readonly List<Build.PlanFinding> ApplicationFindings = new List<Build.PlanFinding>();

        /// <summary>Texture memory from what the avatar references right now (computed).</summary>
        public long CurrentTextureBytes;

        /// <summary>True when every currently referenced texture could be valued.</summary>
        public bool HasCurrentTextureBytes;
        public bool CurrentTextureBytesFromSdk;

        /// <summary>True when the last analysis restored original references first.</summary>
        public bool LastAnalyzeRestored;

        /// <summary>Stable manifest key for the selected avatar (asset GUID or scene id).</summary>
        public string RootKey = string.Empty;

        public bool HasRecordedOutputs
        {
            get { return RecordedState != null && RecordedState.Generated.Count > 0; }
        }

        public bool CompatibilityAllows
        {
            get { return Report == null || Report.Compatibility.IsOptimizationAllowed; }
        }

        // ===================================================================== selection

        public void SelectAvatar(GameObject avatar)
        {
            Avatar = avatar;
            Report = null;
            Plan = null;
            Generation = null;
            ChangeStatus = null;
            RecordedState = null;
            RootKey = string.Empty;
            Settings = new OptimizationSettings { Global = TextureSettings.ForPreset(Preset) };
            Overrides.Clear();
            HasEvaluatedPlan = false;
            _candidateCache.Clear();
            ResetRunInfo();
            ApplyHealth = ApplicationHealth.NotApplied;
            ApplicationDriftEntries.Clear();
            ApplicationFindings.Clear();
            HasCurrentTextureBytes = false;
            CurrentTextureBytesFromSdk = false;
            CurrentTextureBytes = 0;
            LastAnalyzeRestored = false;
            ClearError();

            State = avatar == null
                ? WindowFlow.AfterDeselectingAvatar()
                : WindowFlow.AfterSelectingAvatar();

            if (avatar != null)
            {
                // Computed before any scan: prefabs have an asset GUID, scene objects use
                // their GlobalObjectId, so previously recorded runs show up immediately.
                RootKey = ComputeRootKey(avatar, null);
                LoadSettings();
                RefreshStatus();
            }
        }

        /// <summary>
        /// Accepts a user selection from any of the three input paths (drag &amp; drop,
        /// object picker, current Hierarchy selection). A child GameObject resolves to its
        /// enclosing avatar root when one exists (a root carrying an avatar descriptor such
        /// as VRCAvatarDescriptor). Selection never modifies the avatar, never optimizes,
        /// and drops every stale result belonging to the previously selected avatar.
        /// </summary>
        public SelectionVerdict Select(GameObject candidate)
        {
            if (candidate == null)
            {
                return SelectionVerdict.RejectedNothingSelected;
            }

            var resolved = ResolveAvatarRoot(candidate);
            SelectAvatar(resolved);
            return resolved == candidate
                ? SelectionVerdict.Accepted
                : SelectionVerdict.AcceptedChildResolved;
        }

        /// <summary>
        /// The avatar root for a selected object: the nearest ancestor carrying an avatar
        /// descriptor, or the object itself when none exists. Detection is type-name based,
        /// so AvatarTuner keeps no compile-time dependency on the VRChat SDK.
        /// </summary>
        internal static GameObject ResolveAvatarRoot(GameObject selected)
        {
            if (selected == null)
            {
                return null;
            }

            var chain = new List<Transform>();
            var flags = new List<bool>();
            var current = selected.transform;
            while (current != null && chain.Count < 128)
            {
                // Depth guard: absurd hierarchies must not hang the editor.
                chain.Add(current);
                flags.Add(HasAvatarDescriptor(current));
                current = current.parent;
            }

            var level = AvatarSelectionModel.ResolveAvatarRootLevel(flags);
            return level >= 0 && level < chain.Count ? chain[level].gameObject : selected;
        }

        /// <summary>
        /// True when the object carries an avatar descriptor component (VRCAvatarDescriptor
        /// and friends). Recognized by type name only - never by SDK reference.
        /// </summary>
        internal static bool HasAvatarDescriptor(Transform target)
        {
            if (target == null)
            {
                return false;
            }

            var components = target.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] == null)
                {
                    continue; // missing script placeholder
                }

                var typeName = components[i].GetType().Name;
                if (typeName.IndexOf("AvatarDescriptor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        // ===================================================================== analysis

        public void Analyze()
        {
            if (!WindowFlow.CanAnalyze(State))
            {
                return;
            }

            ClearError();
            if (Avatar == null)
            {
                ErrorKey = "error.noAvatar";
                State = WindowFlow.AfterAnalysis(false);
                return;
            }

            State = WindowFlow.BeginAnalysis();
            try
            {
                LastAnalyzeRestored = false;
                var options = new ScanOptions();
                options.IncludeInactive = IncludeInactive;
                options.RecordedState = RecordedState;
                Report = AvatarScanner.Scan(Avatar, options);
                if (Report == null || !Report.ScanSucceeded)
                {
                    ErrorKey = Report != null && !string.IsNullOrEmpty(Report.ErrorMessage)
                        ? Report.ErrorMessage
                        : "error.scanFailed";
                    State = WindowFlow.AfterAnalysis(false);
                    return;
                }

                RootKey = ComputeRootKey(Avatar, Report);
                Generation = null;
                ResetRunInfo();
                RebuildPlan();
                if (Plan == null)
                {
                    State = WindowFlow.AfterAnalysis(false);
                    return;
                }

                RefreshStatus();
                State = WindowFlow.AfterAnalysis(true);
            }
            catch (Exception ex)
            {
                Fail("error.unexpected", ex.Message);
                State = WindowFlow.AfterAnalysis(false);
            }
        }

        /// <summary>
        /// Rebuilds from cached quality only; importing candidates requires explicit evaluation.
        /// </summary>
        public void RebuildPlan()
        {
            if (Report == null)
            {
                return;
            }

            try
            {
                HasEvaluatedPlan = false;
                Plan = TexturePlanBuilder.BuildAdaptive(Report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), PlanningOverrides, PlanningSettings,
                    (entry, candidate) =>
                    {
                        CandidateQuality quality;
                        return _candidateCache.TryGetValue(CandidateEvaluator.CacheKey(entry, candidate), out quality) ? quality : null;
                    });
            }
            catch (Exception ex)
            {
                Fail("error.unexpected", ex.Message);
                State = WindowFlow.AfterAnalysis(false);
            }
        }

        public void SetOverride(string textureGuid, TextureOverride ovr)
        {
            if (string.IsNullOrEmpty(textureGuid))
            {
                return;
            }

            if (ovr == null || !ovr.HasAnyEffect)
            {
                Overrides.Remove(textureGuid);
            }
            else
            {
                Overrides[textureGuid] = ovr;
            }

            RebuildPlan();
            SaveSettings();
        }

        public void ClearOverrides()
        {
            Overrides.Clear();
            Settings.Textures.Clear();
            RebuildPlan();
            SaveSettings();
        }

        public void ApplyPreset(OptimizationPreset preset)
        {
            Preset = preset;
            var defaults = TextureSettings.ForPreset(preset);
            Settings.Global.MinPsnr = defaults.MinPsnr;
            Settings.Global.MinSsim = defaults.MinSsim;
            SaveSettings();
            RebuildPlan();
        }

        public void SetOptimizationMode(OptimizationMode mode)
        {
            if (Settings.Mode == mode) return;
            Settings.Mode = mode;
            SaveSettings();
            RebuildPlan();
        }

        public void SetStandardLevel(StandardOptimizationLevel level)
        {
            if (!Enum.IsDefined(typeof(StandardOptimizationLevel), level) || Settings.StandardLevel == level) return;
            Settings.StandardLevel = level;
            SaveSettings();
            RebuildPlan();
        }

        public bool EvaluateRecommendations(Action<float, string> progress, Func<bool> cancelled)
        {
            if (Report == null || !CompatibilityAllows) return false;
            HasEvaluatedPlan = false;
            try
            {
                Report = ScanCurrentAvatar();
                if (Report == null || !Report.ScanSucceeded || !CompatibilityAllows) return false;
                var revision = CurrentAnalysisRevision(Report);
                var settings = PlanningSettings;
                var overrides = PlanningOverrides;
                var candidateCount = 0;
                foreach (var entry in Report.Textures)
                {
                    var category = entry.Category;
                    SemanticCategory corrected;
                    if (settings.CategoryCorrections.TryGetValue(entry.Guid, out corrected)) category = corrected;
                    var effective = settings.ResolveForOptimization(entry.Guid, category);
                    TextureOverride ovr;
                    overrides.TryGetValue(entry.Guid, out ovr);
                    candidateCount += AdaptivePlanner.Candidates(TexturePlanBuilder.BuildRequest(entry), PresetCatalog.GetPolicy(OptimizationPreset.Balanced),
                        ovr, effective.Settings, category,
                        settings.Mode == OptimizationMode.Standard ? (StandardOptimizationLevel?)settings.StandardLevel : null).Count;
                }
                using (var evaluator = new CandidateEvaluator(_candidateCache, progress, cancelled, candidateCount))
                    Plan = TexturePlanBuilder.BuildAdaptive(Report, PresetCatalog.GetPolicy(OptimizationPreset.Balanced), overrides, settings,
                        (entry, candidate) =>
                        {
                            var mobile = candidate.Format == TunerTextureFormat.ASTC_4x4 || candidate.Format == TunerTextureFormat.ASTC_6x6 || candidate.Format == TunerTextureFormat.ASTC_8x8;
                            if (mobile != (CandidateEvaluator.PlatformName == "Android")) return null;
                            return evaluator.Measure(entry, candidate);
                        });
                foreach (var texture in Plan.Textures)
                {
                    texture.SourceRevision = CandidateEvaluator.Revision(texture.TexturePath);
                    if (texture.HasChanges && texture.Plan.HasEstimatedPsnr)
                    {
                        texture.Plan.HasMeasuredPsnr = true;
                        texture.Plan.MeasuredPsnrDb = texture.Plan.EstimatedPsnrDb;
                        texture.Plan.HasEstimatedPsnr = false;
                        texture.Plan.Confidence = QualityConfidence.High;
                    }
                }
                if (revision != CurrentAnalysisRevision(Report))
                {
                    ErrorKey = "workspace.sourceChanged";
                    return false;
                }
                _evaluatedRevision = revision;
                if (progress != null) progress(1f, "opt.phase.candidates");
                if (cancelled != null && cancelled()) { ErrorKey = "error.cancelled"; return false; }
                HasEvaluatedPlan = true;
                ClearError();
                return true;
            }
            catch (OperationCanceledException) { ErrorKey = "error.cancelled"; return false; }
            catch (Exception ex) { Fail("error.unexpected", ex.Message); return false; }
        }

        private AvatarAnalysisReport ScanCurrentAvatar()
        {
            return AvatarScanner.Scan(Avatar, new ScanOptions { RecordedState = RecordedState, IncludeInactive = IncludeInactive });
        }

        private string CurrentAnalysisRevision(AvatarAnalysisReport report)
        {
            var slots = new List<ObservedSlot>();
            SceneTextureApplier.GatherObserved(Avatar, slots, null);
            return AnalysisRevision.Compute(report, slots, guid =>
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                return string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.GetAssetDependencyHash(path).ToString();
            });
        }

        public CandidatePreview GetPreview(string guid)
        {
            if (Settings.Mode != OptimizationMode.Advanced || string.IsNullOrEmpty(guid) || !Settings.IncludedTextures.Contains(guid))
                return new CandidatePreview { ErrorKey = "workspace.previewUnavailable" };
            var entry = Report == null ? null : Report.FindTexture(guid);
            if (entry != null && Plan != null && HasEvaluatedPlan)
                foreach (var texture in Plan.Textures)
                    if (texture.TextureGuid == guid && texture.SourceRevision == CandidateEvaluator.Revision(entry.Path))
                        using (var evaluator = new CandidateEvaluator(null)) return evaluator.Preview(entry, texture.Plan);
            return new CandidatePreview { ErrorKey = "workspace.previewUnavailable" };
        }

        public List<TunerTextureFormat> AllowedFormats(TextureEntry entry)
        {
            var result = new List<TunerTextureFormat> { TunerTextureFormat.Automatic };
            var effective = Settings.Resolve(entry.Guid, entry.Category).Settings.Clone();
            effective.Excluded = effective.ProtectQuality = false;
            effective.Format = TunerTextureFormat.Automatic;
            foreach (var candidate in AdaptivePlanner.Candidates(TexturePlanBuilder.BuildRequest(entry),
                PresetCatalog.GetPolicy(Preset), null, effective, entry.Category))
                if (!result.Contains(candidate.Format)) result.Add(candidate.Format);
            return result;
        }

        private string SettingsPath
        {
            get { return "Library/AvatarTuner/Settings/" + StableHash.Sha256Hex(RootKey ?? string.Empty) + ".json"; }
        }

        public void SaveSettings()
        {
            if (string.IsNullOrEmpty(RootKey)) return;
            try
            {
                var overrides = new Dictionary<string, object>();
                foreach (var pair in Overrides) overrides[pair.Key] = pair.Value.ToDictionary();
                var data = new Dictionary<string, object> { { "schema", 2 }, { "preset", PresetCatalog.GetId(Preset) },
                    { "settings", Settings.ToDictionary() }, { "overrides", overrides } };
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var temporary = SettingsPath + ".tmp";
                File.WriteAllText(temporary, MiniJson.Serialize(data));
                if (File.Exists(SettingsPath)) File.Replace(temporary, SettingsPath, null);
                else File.Move(temporary, SettingsPath);
            }
            catch (Exception) { ErrorKey = "workspace.settingsWriteFailed"; }
        }

        private void LoadSettings()
        {
            if (!File.Exists(SettingsPath)) return;
            try
            {
                var data = MiniJson.Parse(File.ReadAllText(SettingsPath)) as Dictionary<string, object>;
                if (data == null) throw new FormatException();
                object value;
                if (data.TryGetValue("settings", out value)) Settings = OptimizationSettings.FromDictionary(value as Dictionary<string, object>);
                Preset = OptimizationPreset.Balanced;
                if (data.TryGetValue("overrides", out value) && value is Dictionary<string, object> overrides)
                    foreach (var pair in overrides) Overrides[pair.Key] = TextureOverride.FromDictionary(pair.Value as Dictionary<string, object>);
            }
            catch (Exception) { ErrorKey = "workspace.settingsReadFailed"; }
        }

        // ===================================================================== optimization

        /// <summary>
        /// Runs the full optimization: generate copies, record the manifest, refresh status.
        /// Returns true when the run completed successfully.
        /// </summary>
        public bool ConfirmAndOptimize(Action<float, string> progress, Func<bool> cancelled)
        {
            if (!EvaluateRecommendations(progress, cancelled)) return false;
            if (Plan == null || !Plan.HasChanges)
            {
                ErrorKey = "ui.hint.noChanges";
                return false;
            }
            return RunOptimization(progress, cancelled);
        }

        public bool RunOptimization(Action<float, string> progress, Func<bool> cancelled)
        {
            if (Report == null || Plan == null)
            {
                ErrorKey = "error.noAvatar";
                return false;
            }

            if (!OverwriteGenerated && HasRecordedOutputs && !EditorUtility.DisplayDialog(
                UiStrings.T("dlg.overwrite.title"), UiStrings.T("dlg.overwrite.message", RecordedState.Generated.Count.ToString()),
                UiStrings.T("dlg.yes"), UiStrings.T("dlg.no"))) return false;

            ClearError();
            State = WindowFlow.BeginOptimization();
            var transaction = new GeneratedFileTransaction(GeneratedAssetStore.GeneratedRoot, "Library/AvatarTuner/Transactions");
            var committed = false;
            var undoGroup = -1;
            try
            {
                if (progress != null)
                {
                    progress(0.0f, "opt.phase.plan");
                }

                // Execute only the reviewed plan after rechecking scene and dependency evidence.
                if (Plan == null || !HasEvaluatedPlan || !CompatibilityAllows)
                {
                    Fail("error.optimizeFailed", "plan");
                    State = WindowFlow.AfterOptimization(false);
                    return false;
                }

                if (!IsPlanFresh())
                {
                    Fail("error.optimizeFailed", "stalePlan");
                    State = WindowFlow.AfterOptimization(false);
                    return false;
                }

                if (string.IsNullOrEmpty(_evaluatedRevision) || _evaluatedRevision != CurrentAnalysisRevision(ScanCurrentAvatar()))
                {
                    ErrorKey = "workspace.sourceChanged";
                    HasEvaluatedPlan = false;
                    State = WindowFlow.AfterOptimization(false);
                    return false;
                }

                foreach (var texture in Plan.Textures)
                    if (texture.SourceRevision != CandidateEvaluator.Revision(texture.TexturePath))
                    {
                        ErrorKey = "workspace.sourceChanged";
                        HasEvaluatedPlan = false;
                        State = WindowFlow.AfterOptimization(false);
                        return false;
                    }

                TextureGenerationResult generation;
                var outputFolder = TextureOptimizer.DefaultOutputFolder(Report);
                var outputRoot = outputFolder.Substring(0, outputFolder.Length - "/Textures".Length);
                transaction.CaptureDirectory(outputRoot);
                transaction.Record(GeneratedAssetStore.ManifestPath);
                if (!Plan.HasChanges)
                {
                    // Nothing to generate is still a completed run: it records the avatar
                    // as optimized with zero outputs so tracking can start.
                    generation = new TextureGenerationResult();
                    generation.Succeeded = true;
                    generation.OutputFolder = TextureOptimizer.DefaultOutputFolder(Report);
                }
                else
                {
                    generation = TextureOptimizer.Generate(Report, Plan, outputFolder, progress, cancelled, transaction);
                }

                Generation = generation;
                if (!generation.Succeeded)
                {
                    Generation = null;
                    ErrorKey = generation.Cancelled ? "error.cancelled" : "error.optimizeFailed";
                    RunHappened = true;
                    LastRunSucceeded = false;
                    State = WindowFlow.AfterOptimization(false);
                    return false;
                }

                // Direct scene application: the selected scene avatar gets the optimized
                // material clones assigned (source assets stay read-only). The outcome is
                // recorded in the same manifest write below.
                SceneApplicationOutcome application = null;
                if (generation.Succeeded && Avatar != null)
                {
                    Undo.IncrementCurrentGroup();
                    undoGroup = Undo.GetCurrentGroup();
                    if (RecordedState != null && RecordedState.AppliedToAvatar)
                    {
                        var previous = AvatarOptimizationState.FromDictionary(RecordedState.ToDictionary());
                        var restored = SceneTextureApplier.Restore(Avatar, previous, true);
                        if (!restored.Success || restored.Partial)
                        {
                            ErrorKey = "apply.error.applyFailed";
                            Generation = null;
                            State = WindowFlow.AfterOptimization(false);
                            return false;
                        }
                    }
                    application = SceneTextureApplier.Apply(Avatar, Report, generation, outputRoot);
                    ApplicationFindings.Clear();
                    ApplicationFindings.AddRange(application.Findings);
                    if (!string.IsNullOrEmpty(application.ErrorKey))
                    {
                        ErrorKey = application.ErrorKey;
                        Generation = null;
                        RunHappened = true;
                        LastRunSucceeded = false;
                        State = WindowFlow.AfterOptimization(false);
                        return false;
                    }
                }

                // Cancelled or failed generation has already rolled back and returned.
                var configHash = ConfigHash();
                var sources = ChangeTrackingService.BuildSnapshot(
                    null, Report, configHash, new UnityFingerprintProvider()).Sources;
                var numbers = MemoryReportModel.Build(Report, Plan, generation);
                var info = new OptimizationRunInfo
                {
                    ConfigHash = configHash,
                    EngineVersion = Application.unityVersion,
                    ToolVersion = GeneratedAssetStore.ToolVersion,
                    PresetId = PresetCatalog.GetId(Preset),
                    SourceBytesTotal = numbers.SourceBytes,
                    OutputBytesTotal = numbers.OutputBytes,
                    HasSource = numbers.HasSource,
                    HasOutput = numbers.HasOutput,
                    Basis = numbers.Basis,
                    UnknownSourceCount = numbers.UnknownSourceCount,
                    UnknownOutputCount = numbers.UnknownOutputCount,
                    OptimizedCount = numbers.OptimizedCount,
                    SkippedCount = numbers.SkippedCount,
                    ExcludedCount = numbers.ExcludedCount,
                    ReviewCount = numbers.ReviewCount
                };

                var state = ManifestStateBuilder.Build(Report, generation, sources, info);
                state.SourceRootGuid = RootKey;
                if (ApplicationPlanner.IsVerifiedApplication(application))
                {
                    // Verified application only: bindings, clones and the success timestamp
                    // are recorded only when references verifiably reached the scene avatar.
                    state.AppliedToAvatar = true;
                    state.AppliedPartially = application.Partial;
                    state.AppliedUtc = state.LastAttemptUtc;
                    state.RendererBindings.AddRange(application.RendererBindings);
                    state.MaterialClones.AddRange(application.MaterialClones);
                    state.AnimationBindings.AddRange(application.AnimationBindings);
                    state.Generated.AddRange(application.ExtraGenerated);
                }
                else if (application != null && !string.IsNullOrEmpty(application.ErrorKey))
                {
                    // An application failure must not read as an optimized run in the manifest.
                    ApplicationPlanner.MarkApplicationFailed(state, application.ErrorKey);
                }

                string manifestError;
                GeneratedAssetStore.RecordRun(state, out manifestError);
                if (!string.IsNullOrEmpty(manifestError))
                {
                    ErrorKey = manifestError;
                    Generation = null;
                    RunHappened = true;
                    LastRunSucceeded = false;
                    State = WindowFlow.AfterOptimization(false);
                    return false;
                }

                transaction.Commit();
                committed = true;
                if (undoGroup >= 0) Undo.CollapseUndoOperations(undoGroup);

                RefreshStatus();
                var succeeded = generation.Succeeded && string.IsNullOrEmpty(manifestError) &&
                                (application == null || string.IsNullOrEmpty(application.ErrorKey));
                if (!succeeded && generation.Cancelled && string.IsNullOrEmpty(ErrorKey))
                {
                    ErrorKey = "error.cancelled";
                }

                RunHappened = true;
                LastRunSucceeded = succeeded;
                LastRunHasWarnings = RunHasWarnings(generation, Plan);

                State = WindowFlow.AfterOptimization(succeeded);
                return succeeded;
            }
            catch (Exception ex)
            {
                Fail("error.unexpected", ex.Message);
                State = WindowFlow.AfterOptimization(false);
                return false;
            }
            finally
            {
                try
                {
                    if (!committed && undoGroup >= 0)
                    {
                        Undo.FlushUndoRecordObjects();
                        Undo.RevertAllDownToGroup(undoGroup);
                    }
                }
                finally
                {
                    transaction.Dispose();
                    if (!committed) AssetDatabase.Refresh();
                }
            }
        }

        // ===================================================================== tracking & cleanup

        /// <summary>
        /// Restores the recorded original material/controller references on the scene avatar
        /// (Undo-recorded). Generated files are kept; source assets untouched. Returns an
        /// error key or null.
        /// </summary>
        public string RestoreOriginalTextures()
        {
            if (Avatar == null || RecordedState == null || !RecordedState.AppliedToAvatar)
            {
                return "build.error.noOutputs";
            }

            var outcome = SceneTextureApplier.Restore(Avatar, RecordedState);
            string storeError = null;

            if (outcome.Success)
            {
                if (outcome.Partial)
                {
                    RecordedState.AppliedToAvatar = true;
                    RecordedState.AppliedPartially = true;
                    GeneratedAssetStore.SetApplicationState(
                        RootKey, true, true, RecordedState.AppliedUtc,
                        RecordedState.RendererBindings, RecordedState.MaterialClones,
                        RecordedState.AnimationBindings, out storeError);
                }
                else
                {
                    RecordedState.AppliedToAvatar = false;
                    RecordedState.AppliedPartially = false;
                    GeneratedAssetStore.SetApplicationState(
                        RootKey, false, false, string.Empty,
                        new List<AppliedRendererBinding>(), new List<AppliedMaterialClone>(),
                        new List<AppliedAnimationBinding>(), out storeError);
                }
            }

            RefreshStatus();
            if (!string.IsNullOrEmpty(outcome.ErrorKey))
            {
                return outcome.ErrorKey;
            }

            if (outcome.Partial)
            {
                return "build.error.partialApplication";
            }

            return string.IsNullOrEmpty(storeError) ? null : storeError;
        }

        /// <summary>Refreshes the recorded state and the change report.</summary>
        public void RefreshStatus()
        {
            try
            {
                var load = GeneratedAssetStore.Load();
                if (!string.IsNullOrEmpty(load.ErrorKey) && string.IsNullOrEmpty(ErrorKey))
                {
                    ErrorKey = load.ErrorKey;
                }

                RecordedState = load.Manifest == null ? null : load.Manifest.FindAvatar(RootKey);
                ChangeStatus = ChangeTrackingService.Analyze(RecordedState, Report, ConfigHash());

                // Reference-level application health + current memory from what the avatar
                // references right now. Gathers scene references only (no asset loads), and
                // runs on selection / refresh / analysis / optimization - never per frame.
                ApplyHealth = ApplicationHealth.NotApplied;
                ApplicationDriftEntries.Clear();
                HasCurrentTextureBytes = false;
                CurrentTextureBytesFromSdk = false;
                CurrentTextureBytes = 0;
                if (Avatar != null)
                {
                    var observedSlots = new List<ObservedSlot>();
                    var observedTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    SceneTextureApplier.GatherObserved(Avatar, observedSlots, observedTextures);

                    if (RecordedState != null && RecordedState.AppliedToAvatar)
                    {
                        ApplyHealth = ApplicationPlanner.Evaluate(
                            RecordedState, observedSlots, ApplicationDriftEntries);
                    }

                    var actualBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                    foreach (var guid in observedTextures)
                    {
                        if (string.IsNullOrEmpty(guid)) continue;
                        long bytes;
                        if (TextureOptimizer.TryImportedBytes(AssetDatabase.GUIDToAssetPath(guid), out bytes)) actualBytes[guid] = bytes;
                    }
                    bool allKnown;
                    CurrentTextureBytes = ApplicationPlanner.ComputeCurrentBytes(
                        actualBytes, new Dictionary<string, long>(), observedTextures, out allKnown);
                    HasCurrentTextureBytes = allKnown;
                    long sdkBytes;
                    if (VrcPerformanceReader.TryRead(Avatar, Avatar.name,
                        EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android, out sdkBytes))
                    {
                        CurrentTextureBytes = sdkBytes;
                        HasCurrentTextureBytes = true;
                        CurrentTextureBytesFromSdk = true;
                    }
                }
            }
            catch (Exception ex)
            {
                ChangeStatus = null;
                if (string.IsNullOrEmpty(ErrorKey))
                {
                    ErrorKey = "error.unexpected";
                    ErrorArgs.Add(ex.Message);
                }
            }
        }

        /// <summary>Deletes this avatar's recorded outputs and its manifest entry.</summary>
        public CleanupOutcome CleanupGenerated()
        {
            try
            {
                string errorKey;
                var outcome = GeneratedAssetStore.RemoveAvatar(RootKey, out errorKey);
                if (!string.IsNullOrEmpty(errorKey))
                {
                    ErrorKey = errorKey;
                }

                Generation = null;
                ResetRunInfo();
                RefreshStatus();
                return outcome;
            }
            catch (Exception ex)
            {
                Fail("error.unexpected", ex.Message);
                return new CleanupOutcome();
            }
        }

        public string ConfigHash()
        {
            try
            {
                return TexturePlanBuilder.AdaptiveConfigHash(PresetCatalog.GetPolicy(OptimizationPreset.Balanced), PlanningOverrides, PlanningSettings);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>The config token of the current plan (empty when no plan exists).</summary>
        public string PlanConfigToken
        {
            get { return Plan == null ? string.Empty : Plan.ConfigHashToken; }
        }

        /// <summary>
        /// True while the current plan was built with the configuration that is current
        /// right now. A stale plan must be rebuilt before it can be confirmed or executed.
        /// </summary>
        public bool IsPlanFresh()
        {
            return OptimizationDialogModel.IsPlanFresh(PlanConfigToken, ConfigHash());
        }

        /// <summary>The honest display status of the working set.</summary>
        public AvatarDisplayStatus CurrentDisplayStatus()
        {
            var live = ChangeStatus != null
                ? ChangeStatus.Status
                : (RecordedState != null ? RecordedState.Status : OptimizationStatus.NotOptimized);
            var baseStatus = StatusModel.Derive(
                Report != null && Report.ScanSucceeded,
                Plan != null && Plan.HasChanges,
                RunHappened,
                LastRunSucceeded,
                LastRunHasWarnings,
                HasRecordedOutputs,
                live);

            if (RecordedState == null)
            {
                return baseStatus;
            }

            var withBuild = StatusModel.DeriveBuildTime(
                baseStatus,
                RecordedState.BuildApplicationConfigured,
                RecordedState.UseOptimizedTexturesOnBuild,
                !string.IsNullOrEmpty(RecordedState.LastBuildApplicationError));

            return StatusModel.DeriveApplication(withBuild, ApplyHealth);
        }

        /// <summary>
        /// Stores the per-avatar "Use Optimized Textures During Build" consent in the
        /// manifest. The setting only exists after a valid optimized output exists; it
        /// never modifies source assets or generated outputs. Returns an error key or null.
        /// </summary>
        public string SetBuildApplicationEnabled(bool enabled)
        {
            if (!HasRecordedOutputs)
            {
                return "build.error.noOutputs";
            }

            string errorKey;
            var ok = GeneratedAssetStore.SetBuildApplicationSetting(
                RootKey, true, enabled, out errorKey);
            RefreshStatus();
            return ok ? null : errorKey;
        }

        /// <summary>
        /// The shared before/after/savings numbers. Realized numbers (from generated
        /// outputs or the recorded manifest run) are preferred over plan estimates, so
        /// every page shows the same values for the same artifact.
        /// </summary>
        public MemoryNumbers CurrentMemoryNumbers()
        {
            MemoryNumbers numbers;
            if (Generation != null && Plan != null)
            {
                numbers = MemoryReportModel.Build(Report, Plan, Generation);
            }
            else if (Plan == null)
            {
                numbers = MemoryReportModel.FromRecordedState(RecordedState);
            }
            else
            {
                // A plan exists but nothing was generated with it yet. When the manifest still
                // holds the previous run's realized numbers, those stay the headline numbers;
                // the pending plan's estimate lives in the plan card, labelled as planned.
                var recorded = MemoryReportModel.FromRecordedState(RecordedState);
                numbers = recorded.HasOutput ? recorded : MemoryReportModel.Build(Report, Plan, null);
            }

            // Prefer the installed SDK's current-avatar calculation over the local fallback.
            numbers.HasCurrent = HasCurrentTextureBytes;
            numbers.CurrentBytes = CurrentTextureBytes;
            numbers.CurrentFromSdk = CurrentTextureBytesFromSdk;
            return numbers;
        }

        /// <summary>The pending plan's estimate (potential, nothing generated yet).</summary>
        public MemoryNumbers PlanMemoryNumbers()
        {
            return MemoryReportModel.Build(Report, Plan, null);
        }

        // ===================================================================== helpers

        private void ResetRunInfo()
        {
            RunHappened = false;
            LastRunSucceeded = false;
            LastRunHasWarnings = false;
        }

        /// <summary>
        /// A completed run with skipped textures or textures flagged for review is a
        /// success with warnings, not a clean success.
        /// </summary>
        internal static bool RunHasWarnings(TextureGenerationResult generation, TextureOptimizationPlan plan)
        {
            if (generation == null)
            {
                return false;
            }

            if (generation.SkippedCount > 0 || generation.Findings.Count > 0)
            {
                return true;
            }

            return plan != null && TextureRows.CountNeedingReview(plan) > 0;
        }

        private void Fail(string key, string detail)
        {
            ErrorKey = key;
            ErrorArgs.Clear();
            ErrorArgs.Add(detail);
        }

        private void ClearError()
        {
            ErrorKey = null;
            ErrorArgs.Clear();
        }

        /// <summary>
        /// A stable identity for the selected avatar: asset GUID when the root is a real
        /// asset (prefab), otherwise the scene object's GlobalObjectId so scene avatars
        /// keep their manifest entry across sessions.
        /// </summary>
        internal static string ComputeRootKey(GameObject avatar, AvatarAnalysisReport report)
        {
            if (report != null && !string.IsNullOrEmpty(report.AvatarRootGuid))
            {
                return report.AvatarRootGuid;
            }

            if (avatar != null)
            {
                try
                {
                    var globalId = GlobalObjectId.GetGlobalObjectIdSlow(avatar).ToString();
                    if (!string.IsNullOrEmpty(globalId) &&
                        globalId.IndexOf("GlobalObjectId_V1-0-00000000000000000000000000000000-0-0",
                            StringComparison.Ordinal) < 0)
                    {
                        return globalId;
                    }
                }
                catch (Exception)
                {
                    // fall through to the name based key below
                }

                return "scene:" + avatar.name;
            }

            return string.Empty;
        }

    }
}
