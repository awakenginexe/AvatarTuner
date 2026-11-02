# AvatarTuner v1.0.0 — Progress

Status label: **Milestones 1–7 implemented | UI/UX redesign complete | 211 offline tests green | Unity compile clean (round 2) | redesigned UI awaiting user's manual visual test** (target for release)

Development workspace: `C:/Users/ANXE/Desktop/Coding/AvatarTuner`
Hard rules honored: Unity is never launched, no Unity project is opened or modified,
no batch-mode compile, no VRChat SDK build. Only offline verification is claimed as passed.

## Milestones (8)

| # | Milestone | State |
|---|-----------|-------|
| 1 | Package structure & architecture (UPM layout, asmdefs) | DONE |
| 2 | Core pure-C# logic layer (no UnityEngine/UnityEditor) | DONE — 211/211 offline tests pass |
| 3 | Localization en / th / zh-Hans | DONE — 311 keys × 3 languages, generated + validated |
| 4 | Avatar scanning, dependency tracking & compatibility (Editor) | DONE — scanner + compatibility scanner + tool detection complete |
| 5 | Texture optimization integration (Editor) | DONE — plan builder + generated texture materializer + third-party protection |
| 6 | Generated-asset management & change-tracking integration | DONE — manifest persistence, safe cleanup, tamper detection wired to ChangeAnalyzer |
| 7 | Unity Editor UI (window, menus, progress, errors) | DONE — UI Toolkit window, redesigned UX (visual test pending) |
| 8 | Docs, Unity test stubs, verification report, package finalization | pending |

## What exists so far

- `package.json`, `Core/AvatarTuner.Core.asmdef` (`noEngineReferences: true`),
  `Editor/AvatarTuner.Editor.asmdef`, `Tests/Editor/AvatarTuner.Editor.Tests.asmdef`
- `Core/` — MiniJson, StableHash, EngineVersion, TextureFormatInfo, TextureUsageClassifier,
  OptimizationPolicy, CompressionPlanner, CompressionSimulator, QualityMetrics, VramCalculator,
  ChangeAnalyzer, ManifestModels, TranslationCatalog, **CompatibilityPolicy** (14 files)
- `Editor/Analysis/` — AvatarAnalysisModel (report model), AvatarScanner (dependency aware
  avatar scan), **CompatibilityScanner** (third-party tool evidence → CompatibilityReport)
- `Editor/Optimization/` — **TexturePlanBuilder** (Unity free; scan report + policy +
  overrides → per-texture plans, with third-party protection rules), **TextureFormatMapping**
  (Unity free; tuner format → TextureImporterFormat name table), **TextureGenerationModels**
  (Unity free; generated-asset result models), **TextureOptimizer** (materializes plans into
  generated texture assets; originals never touched)
- `Editor/Tracking/` — **ManifestStateBuilder** (Unity free; run → recorded
  `AvatarOptimizationState` with provenance + deterministic config hash), **CleanupPlanner**
  (Unity free; the deletion policy: recorded paths only, under the generated root only,
  never the manifest), **GeneratedAssetStore** (manifest persistence at
  `Assets/AvatarTuner-Generated/avatartuner-manifest.json`, regeneration sync, cleanup),
  **UnityFingerprintProvider** (`AssetDatabase.GetAssetDependencyHash` + importer-settings
  hash), **ChangeTrackingService** (fresh scan + observed output hashes → `ChangeAnalyzer`)
- `Editor/UI/` — **WindowFlow** (Unity free; window state machine + control gating, unit
  tested), **UiStrings** (live language switching en/th/zh-Hans via `TranslationResolver`,
  choice survives domain reloads), **AvatarTunerController** (defensive pipeline:
  scan → plan → generate → record → status, with optional two-pass quality measurement),
  **UiTheme** (semantic palette for both Editor themes), **WindowFlow** + **ViewModels/**
  (pure: DashboardModel, TextureRowModel, CompatibilityGrouper, WorkflowModel — unit
  tested), **AvatarTunerWindow** (`[MenuItem("Tools/AvatarTuner")]`, UI Toolkit window with
  sidebar navigation, dashboard, grouped compatibility and wired controls) +
  **AvatarTunerWindow.uss** (layout stylesheet)
- `Editor/Localization/Translations/{en,th,zh-Hans}.json` + `Tools~/build_translations.py`
  (single source of truth; regenerating keeps all languages in lock-step)
- `Tests/Standalone~/` — dependency-free test runner (net8.0) plus a `core-compat` project that
  compiles Core under **netstandard2.1 + C# 9** to verify the Unity 2022.3 API/language surface

## Compatibility Scanner design (Milestone 4, completed)

- `Core/CompatibilityPolicy.cs` — Unity-free registry of known tools (VRCFury, Modular Avatar,
  NDMF, d4rkAvatarOptimizer, Avatar Optimizer) with detection fragments for component type
  names, assembly names and package ids, plus the evaluation rules. Pure and unit tested.
- `Editor/Analysis/CompatibilityScanner.cs` — observation only: component walk (reflection
  free, name based), `AppDomain` assemblies, `PackageManager.PackageInfo` packages, and
  animation-driven systems (SPS/PCS style) from the dynamic-component scan. Never modifies
  tool components or their settings.
- Policy verdict: informational tools report only; each mesh optimizer warns (AAO medium risk,
  d4rk high risk); **both optimizers active on the same avatar block optimization** and the
  avatar becomes read-only analysis (`status.unsupported` semantics). Animation-driven systems
  are reported and protected, never touched.
- Message keys: `compat.*` (13), `finding.*` + `finding.kind.*` (14), `usage.reason.*` (1) —
  all present in en / th / zh-Hans and enforced by the source-scanner localization test.

## Texture optimization integration (Milestone 5, completed)

- `Editor/Optimization/TexturePlanBuilder.cs` — Unity free (compiled into the offline tests).
  Translates `TextureEntry` scan data into `TexturePlanRequest`s and delegates every decision
  to Core's `CompressionPlanner`. Enforces the protection rules: textures whose materials sit
  in a third-party tool component's or animation-driven system's transform subtree are pinned
  (path-boundary aware: "Arm/Prop" pins "Arm/Prop/Mesh" but never "Arm/Prop2"). Protection
  beats user overrides and is reported via `plan.skip.protectedThirdParty` + `opt.protectedTextures`.
- `Editor/Optimization/TextureFormatMapping.cs` — Unity free name table (BC1→DXT1, BC3→DXT5,
  BC4/BC5/BC6H/BC7, R8/RG16/..., ETC2 aliases). Formats Unity cannot express map to null and
  are skipped with `error.unsupportedConversion`; they are never approximated.
- `Editor/Optimization/TextureOptimizer.cs` — materializes changed plans into
  `Assets/AvatarTuner-Generated/<avatar>-<guid8>/Textures/` using `AssetDatabase.CopyAsset`
  plus importer edits on the COPY only (max size, mip maps, Standalone platform format and
  compression). Records provenance (source GUID/path, generated GUID/path, plan hash token,
  file size) for the Milestone 6 manifest. Per-texture failures become localized findings
  and never abort the run; cancellation keeps what was written.

## Generated-asset management (Milestone 6, completed)

- `Editor/Tracking/ManifestStateBuilder.cs` — Unity free (compiled into the offline tests).
  Assembles the recorded `AvatarOptimizationState` from a run: provenance per output
  (source GUID, plan hash, content hash), deterministic config hash over preset policy +
  per-texture overrides (order independent, effect-less overrides ignored, override
  targets part of the hash), ISO-8601 UTC stamps. A failed or cancelled run never claims
  a success timestamp.
- `Editor/Tracking/CleanupPlanner.cs` — Unity free. The deletion policy, fully tested:
  only manifest-recorded paths, only strictly under the generated root (separator-boundary
  aware, case and slash normalized), never the manifest file. Also computes what a
  regeneration removes, which files are orphans, and the common output folder that scopes
  tamper detection to one avatar.
- `Editor/Tracking/GeneratedAssetStore.cs` — manifest persistence
  (`Assets/AvatarTuner-Generated/avatartuner-manifest.json`, pretty JSON, schema-checked).
  Strict policy: a corrupt or newer manifest is never overwritten (unknown provenance means
  unknown ownership) and is reported via `error.manifestCorrupt` / `error.manifestNewer`.
  `RecordRun` = regeneration (deletes previous outputs the new run no longer produces,
  then upserts), `RemoveAvatar` / `CleanupOrphans` = safe cleanup through the safety gate.
- `Editor/Tracking/UnityFingerprintProvider.cs` + `ChangeTrackingService.cs` — the change
  tracker: sources from a fresh scan via Core `DependencyRegistry` + `IFingerprintProvider`
  (Unity's `GetAssetDependencyHash` plus a separate hash over the importer settings that
  affect output), generated outputs observed by hashing the files that really exist.
  `ChangeAnalyzer.Analyze` then provides source/importer changes, partial invalidation and
  tamper / missing-file / orphan detection with the six `OptimizationStatus` states.

## Editor UI (Milestone 7, completed)

The user's round-2 Unity compile was clean (0 errors) but reported that **AvatarTuner did
not appear in Unity's Tools menu**. Root cause: no `[MenuItem]` existed anywhere in the
package — the Editor UI milestone had not been implemented; only the
`menu.tools.avatarTuner` localization key was present. Fixed by building the real UI:

- **Menu**: `[MenuItem("Tools/AvatarTuner")]` on `AvatarTunerWindow.Open()`
  (Unity 2022.3 IMGUI `MenuItem`, one registration, Editor-only assembly).
- **Window** (`Editor/UI/AvatarTunerWindow.cs`): 8 localized tabs — Overview, Texture
  Analysis, Preview Changes, Optimization Results, Change Tracking, Materials and Meshes,
  Optimization Settings, Language and Preferences. Hierarchy drag-and-drop works through
  both the object field and a dedicated drop zone (`ui.drop.*`). Every shown control is
  functional: Analyze → `AvatarScanner`, Preview → `TexturePlanBuilder` output, Optimize /
  Regenerate → `TextureOptimizer` + `GeneratedAssetStore.RecordRun`, Cleanup →
  `RemoveAvatar`, Refresh → `ChangeTrackingService.Analyze`, Reset / Revert Overrides /
  Clear Exclusions / Select All / None → live override editing with plan rebuild,
  Export Report / Copy → localized text report, Open Output Folder / View Manifest →
  real file operations. Cancellable progress (`opt.phase.*`, `opt.progress`) with the
  `dlg.*` confirmations (optimize, overwrite, cleanup, cancel-run).
- **Status & change tracking**: `status.*` states with summaries, `change.*` entries,
  stale/valid outputs, recorded versions and timestamps — shown before any analysis too
  (recorded inputs are re-fingerprinted against the current project).
- **Localization**: all strings through `UiStrings` + Core `TranslationResolver`;
  language switchable live (en / th / zh-Hans), persisted in `SessionState`. Every key
  literal the UI uses is enforced to exist in all three catalogs by the source-key test.
- **Safety**: no control runs while an operation is in flight (`WindowFlow` gates, unit
  tested); optimization is refused when the compatibility report blocks the avatar; all
  operations are defensive (exceptions become localized error boxes); source assets are
  never modified. Scanner `ScanOptions.IncludeInactive` (default true, behavior preserved)
  now backs the include-inactive preference; quality measurement estimates from readable
  source pixels only (two-pass plan) and invents nothing.

## UI/UX redesign (complete; visual test pending)

The functional IMGUI window was replaced by a polished UI Toolkit window per the design
brief. All existing behavior above is preserved; the changes are presentation, navigation
and new pure view models:

- **UI Toolkit + USS**: `AvatarTunerWindow.cs` builds the tree in C# (`CreateGUI`) with
  `Editor/UI/AvatarTunerWindow.uss` for structure and `Editor/UI/UiTheme.cs` for the
  semantic palette. Colors adapt to the Editor's dark and light themes from one source;
  if the stylesheet ever fails to load the window stays fully usable and semantic colors
  remain (they are applied inline).
- **Navigation**: sidebar module list (Overview, Textures, Materials & Meshes,
  Compatibility, Change Tracking, Results, Settings) that switches to a compact wrapping
  chip bar below 720 px width — labels never overlap or truncate in any dock mode; the
  active module is highlighted with accent color + bold + dot, in both themes.
- **Dashboard**: `DashboardModel` (pure, tested) turns real scan/plan data into hero metric
  cards (total texture memory in MiB, optimization status) plus supporting counts
  (textures, materials, renderers, triangles, blend shapes, PhysBones) and an **Optimization
  Insights** list (real savings from plan arithmetic, large textures, protected textures,
  manual-review count, build-time deps, duplicates, compatibility warnings/blockers).
  Computed values are labelled `results.basisComputed`; measured disk sizes are labelled
  `ui.label.measured`. Nothing is invented — a missing plan shows `ui.label.notAvailable`.
- **Semantic color system** (`UiTheme`): accent (primary/selected/info), success, warning,
  error, neutral — always paired with a text label and a dot shape (never color-only),
  contrast-checked pairs for both themes.
- **Avatar area**: name, drag-and-drop `ObjectField` (Hierarchy drops work natively), two
  status chips (analysis status + last optimization status), scene-object vs asset-path
  distinction, and the long GlobalObjectId hidden inside a collapsed Advanced Details foldout.
- **Textures**: `TextureRowModel` (pure, tested) drives an organized list — mini thumbnail,
  name/path, resolution, format transition, VRAM (MiB), saving, **quality risk** chip and
  the five documented states (Already optimized / Optimization available / Manual review /
  Excluded / Unsupported). Sort (name/VRAM/saving/status) + filter; per-row expander with
  usage, notes and a Show-in-Editor (ping) button; exclusion toggle per row. Analysis
  performs no modifications.
- **Compatibility**: `CompatibilityGrouper` (pure, tested) fixes the repeated "Modular
  Avatar" spam — one card per tool with risk chip, component count, single explanation
  message, and an expander listing every affected object path with ping buttons.
  Animation-driven systems and cross-tool conflicts get their own groups; blockers/warnings
  use error/warning tones plus text.
- **Action hierarchy**: `WorkflowModel` (pure, tested) drives a breadcrumb
  (Select Avatar › Analyze › Review Changes › Optimize › Results) with exactly one primary
  button per state; disabled actions show their localized reason (busy / no avatar /
  analyze first / compatibility blocked / nothing to do). Cleanup is visually destructive
  (red) and confirmed; progress + cancel remain Unity-native modal (works during
  synchronous runs). Optimization is never automatic.
- **Settings**: three preset cards with their quality/VRAM impact text, advanced per-texture
  overrides collapsed by default, preferences toggles with tooltips, output folder info.
- **Localization**: 28 new keys added (341 keys × en/th/zh-Hans): workflow steps, disabled
  hints, texture status chips, compatibility groups, dashboard labels and insights. All
  display text goes through `UiStrings`; symbols (→ › • —) and numbers are the only
  literals. Source-key test enforces completeness in all three languages.

## Test command (offline, no Unity)

```bash
cd Tests/Standalone~
dotnet run --project AvatarTuner.StandaloneTests.csproj
```

Last result: **211 passed, 0 failed, 0 skipped** (148 inherited + 15 CompatibilityTests +
15 PlanBuilderTests + 11 TrackingTests + 1 Unity-format-spelling regression test +
6 WindowFlowTests + 15 ViewModelTests).

The **entire** `Editor/` source tree (AvatarScanner, CompatibilityScanner, TextureOptimizer,
GeneratedAssetStore, ChangeTrackingService, UnityFingerprintProvider, AvatarAnalysisModel,
UiStrings, UiTheme, AvatarTunerController, AvatarTunerWindow, WindowFlow, the ViewModels,
the plan builder and tracking policy files) is additionally type-checked offline against
Unity 2022.3 API stubs whose signatures mirror the real API — including its quirks
(`GetSourceTextureWidthAndHeight` returns void, `Texture` has no `format`, `Transform`
derives from `Component`, `CreateGUI` is a magic method). This harness is how the round-1
Unity errors are pre-verified and how the UI Toolkit window is compile-checked; it is not
the same as real Unity compilation.

## Real Unity compile round 1 (user-run, Unity 2022.3.22f1) — findings & fixes

The user ran the first real manual Unity compile of the package. Unity reported three
problems in `Editor/Analysis/AvatarScanner.cs` (nothing else in the package failed):

| # | Reported | Root cause | Fix (offline, awaiting user re-test) |
|---|----------|------------|--------------------------------------|
| 1 | CS1061 `'Texture' does not contain a definition for 'format'` (L342) | `Texture.format` does not exist; only `Texture2D` has `TextureFormat`. Materials can bind any Texture subclass | `CurrentFormatName(Texture)` helper: `Texture2D.format` for Texture2D, `Texture.graphicsFormat` for every other subclass. Never assumes Texture2D |
| 2 | CS0029 `Cannot implicitly convert type 'void' to 'bool'` (L353) | `TextureImporter.GetSourceTextureWidthAndHeight(out int, out int)` returns **void** in 2022.3 (and throws while an import is in flight) | Call is wrapped in try/catch; success is judged by `width > 0 && height > 0`; live texture dimensions stand in otherwise |
| 3 | CS0618 `AnimatorOverrideController.clips` obsolete (L487) | Deprecated API | Migrated to `GetOverrides(List<KeyValuePair<AnimationClip,AnimationClip>>)` (original = Key, override = Value), pre-sized via `overridesCount` |

Related sweep: every Unity API call in `Editor/` and `Core/` was searched for the same
misuse patterns (`.format` on Texture, bool-assumed void returns, deprecated members).
Only the three reported sites matched; the remaining `.format` uses are
`TextureImporterPlatformSettings.format` (correct) and pure-C# strings.

Pure-logic follow-up: `TextureFormatInfo.TryParse` now also accepts Unity's own spellings
that the fixed scanner feeds it — `TextureFormat` names (`DXT1Crunched`, `DXT5Crunched`)
and unambiguous block-format `GraphicsFormat` names (`BC1_UNORM`, `BC6H_UF16`,
`BC7_UNORM_SRGB`, ...). Ambiguous uncompressed names are refused as Unknown so VRAM
arithmetic is never based on a guess. Regression test: `QualityMetricTests.UnityFormatSpellingsParseSafely`.

Also fixed during the round: an edit to AvatarScanner split `FillTextureImporterData`
mid-method (caught immediately by the offline stub compile harness before any commit).

**Status: CONFIRMED.** The user's round-2 manual Unity 2022.3.22f1 compile reported
**0 compilation errors** for the whole package (plus 1 unrelated VRChat warning), so the
three fixes above are verified in real Unity. Round 2 additionally reported that the
Tools menu entry was missing (see the Milestone 7 section — no MenuItem had been
implemented).

## Defects found and fixed by tests / review (real bugs, not test churn)

1. `TranslationCatalog.Format` / `UnescapeBraces` infinite recursion on templates without arguments.
2. `EngineVersion` parser accepted invalid versions such as `2022.x.1`.
3. `Tools~/build_translations.py` wrote `languageCode: "English"` for the English catalog,
   which silently disabled fallback detection (`IsFallback` derives from the code `en`).
4. `ChangeAnalyzer` flagged `DependencyRemoved` as `InvalidatesEverything` while the analysis
   applied partial invalidation. The spec demands partial invalidation ("invalidate only the
   affected outputs"); the flag and the stale test were corrected to match.
5. A two-colour checkerboard is exactly representable by BC1 (2 endpoints + 2-bit indices), so it
   round-trips losslessly and cannot serve as "high frequency noise" in quality tests. The test
   now uses deterministic pseudo-random noise.
6. 15 message keys emitted by `AvatarScanner` (`finding.*`, `usage.reason.duplicateContent`) were
   missing from all three catalogs, and the source-key localization test silently skipped them
   because `TranslationValidator.KnownKeyNamespaces` lacked `finding.` / `usage.`. The keys were
   added in all three languages and the namespaces extended so the guard now covers them.
7. `CompatibilityPolicy` matching is substring based, and `d4rkAvatarOptimizer` contains
   `AvatarOptimizer`. Registry order is match priority, so d4rkAvatarOptimizer must precede
   Avatar Optimizer or every d4rk component resolves to the wrong tool. Fixed + regression test.

## Next steps (exact)

1. Milestone 8: README, CHANGELOG, install guide, user guide, limitations, verification
   report, manual Unity checklist, NUnit EditMode stubs (execution pending), `.meta` files
   for the newest sources (generated on the user's next Unity import).
2. Remaining UI surface (deliberately not shown because no implementation backs it yet):
   per-texture force-format / force-max-size / force-mipmaps editors (the planner and
   `TextureOverride` support them; the UI exposes exclusion + keep-resolution/keep-format),
   prefab generation (`ui.toggle.generateOptimizedPrefab`), material-swap remapping
   (`ui.toggle.remapMaterialSwaps`), and the materials/meshes/animator generation phases
   (`opt.phase.materials` / `meshes` / `prefab` / `animator`). The Materials and Meshes tab
   currently shows read-only analysis data.

## Pending Unity-dependent verification (never run, never claimed)

- Unity 2022.3 compilation — round 1 found 3 errors in `AvatarScanner.cs`; fixes are
  **confirmed clean by the user's round-2 compile (0 errors)**
- The **redesigned UI Toolkit window** (user's visual test): rendering, dark/light theme
  contrast, sidebar/compact navigation at all dock sizes, dashboard numbers, texture list
  (thumbnails, chips, sorting), grouped compatibility cards + ping, action hierarchy,
  progress/cancel, dialogs, and Thai/Chinese typography — none of this has run in Unity yet
- EditMode tests execution
- Actual texture re-import / VRAM measurement inside the editor
- VRChat SDK 3 interaction, PhysBones/Contacts live scanning
