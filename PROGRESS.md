# AvatarTuner v1.0.0 — Progress

Status label: **Milestones 1–4 implemented & verified offline | Unity Integration Verification Pending** (target for release)

Development workspace: `C:/Users/ANXE/Desktop/Coding/AvatarTuner`
Hard rules honored: Unity is never launched, no Unity project is opened or modified,
no batch-mode compile, no VRChat SDK build. Only offline verification is claimed as passed.

## Milestones (8)

| # | Milestone | State |
|---|-----------|-------|
| 1 | Package structure & architecture (UPM layout, asmdefs) | DONE |
| 2 | Core pure-C# logic layer (no UnityEngine/UnityEditor) | DONE — 189/189 offline tests pass |
| 3 | Localization en / th / zh-Hans | DONE — 311 keys × 3 languages, generated + validated |
| 4 | Avatar scanning, dependency tracking & compatibility (Editor) | DONE — scanner + compatibility scanner + tool detection complete |
| 5 | Texture optimization integration (Editor) | DONE — plan builder + generated texture materializer + third-party protection |
| 6 | Generated-asset management & change-tracking integration | DONE — manifest persistence, safe cleanup, tamper detection wired to ChangeAnalyzer |
| 7 | Unity Editor UI (window, menus, progress, errors) | pending |
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

## Test command (offline, no Unity)

```bash
cd Tests/Standalone~
dotnet run --project AvatarTuner.StandaloneTests.csproj
```

Last result: **189 passed, 0 failed, 0 skipped** (148 inherited + 15 CompatibilityTests +
15 PlanBuilderTests + 11 TrackingTests).

Editor glue (CompatibilityScanner, TextureOptimizer, GeneratedAssetStore,
ChangeTrackingService, UnityFingerprintProvider, AvatarAnalysisModel) is additionally
type-checked offline against Unity API stubs (`dotnet build` in a scratch project, no Unity
involved); this is not the same as real Unity compilation.

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

1. Milestone 7: `Editor/UI/AvatarTunerWindow.cs`, menu item, progress/cancel, all states.
   Wire the pipeline: scan → plan → generate → `GeneratedAssetStore.RecordRun`; change
   status via `ChangeTrackingService.Analyze`; cleanup via `RemoveAvatar` / `CleanupOrphans`.
   Compatibility section renders `CompatibilityReport` (DetectedTools with `compat.risk*`
   badges, Notices, Warnings, Blockers); disable Optimize when `IsOptimizationAllowed` is
   false and show `compat.manualInspection` when warnings exist.
2. Milestone 8: README, CHANGELOG, install guide, user guide, limitations, verification report,
   manual Unity checklist, NUnit EditMode stubs (execution pending), `.meta` files.

## Pending Unity-dependent verification (never run, never claimed)

- Unity 2022.3 compilation of `Editor/` and `Tests/Editor/`
- EditMode tests execution
- Actual texture re-import / VRAM measurement inside the editor
- VRChat SDK 3 interaction, PhysBones/Contacts live scanning
- UI rendering incl. Thai script shaping (system font dependent)
- `AnimatorOverrideController.clips` is obsolete in 2022.3 (returns `AnimationClipPair[]`;
  the scan code compiles with a warning) — migrate to `GetOverrides` when verified in Unity
