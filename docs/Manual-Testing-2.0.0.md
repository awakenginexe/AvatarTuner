# AvatarTuner 2.0.0 manual test checklist

All items are **pending** until you perform them and supply results. No Unity or VRChat test has been run for 2.0.0, and no release has been published. Work in a backed-up test project and record the Unity version, target platform, SDK and package versions, avatar, settings, and outcome for every run.

## Install and compile

- [ ] In VCC create or copy a test project using Unity 2022.3.22f1. Install this checkout as a local package via Unity Package Manager's **Add package from disk**, selecting its `package.json`. Avoid installing a second copy via VCC or `.unitypackage`.
- [ ] Open the project yourself, wait for imports and compilation, and record all Console errors and warnings. Open **Tools → AvatarTuner**, select a scene avatar, analyze it and open the workspace.

## Content and settings

- [ ] Include color/sRGB and linear textures, HDR, normal maps, packed masks, MatCaps, ramps, transparent hair, detailed face and eyes, mixed/shared usage and an unknown or ambiguous case. Record categories, evidence, protections and corrections. Compare colors, mask channels, transparency edges and shader appearance before and after; HDR/normal/ambiguous candidates that cannot be validated should remain unchanged.
- [ ] Check Global → Category → Custom precedence. Create overrides, change the parent and confirm the snapshot stays fixed; reset and confirm inheritance returns. Test selection, bulk edits, exclusions and protected textures. Change Quick presets and confirm Custom overrides persist. Close/reopen the project and confirm per-avatar settings persist; test a second avatar.
- [ ] Run each Quick quality mode and Advanced settings with Standard and Best Quality. Explicitly evaluate candidates and inspect choices, unmet ratio goals and estimates before applying. Cancel during evaluation and verify temporary assets are cleaned up and source files remain unchanged. Repeat optimization, including a forced failure if safely reproducible, and check rollback and generated asset references.

## Preview, apply and compatibility

- [ ] Check actual generated imports, dimensions, formats, mip counts, color space and alpha. Compare selected source/candidate thumbnails and difference view at multiple zoom levels; switch selection and close/reopen. Exercise docked narrow and wide layouts, keyboard navigation and focus.
- [ ] Apply to a scene avatar, inspect material assignments and generated animation clip/controller copies, then test Undo and Restore. Test animated material swaps in relevant playable layers and verify behavior before/after restore. Check source texture, material, prefab, animation and controller files remain unchanged.
- [ ] Test representative VRCFury, Modular Avatar, lilToon, NonToon, Avatar Optimizer and d4rkAvatarOptimizer setups. Verify protections, warnings, shader appearance and animation behavior. With NDMF installed, compare builds with the optional pass disabled and enabled; confirm source assets remain unchanged and record any conflicts.
- [ ] Test Windows target and Android target separately. For Android, activate the Android build target and confirm decoding support before testing ASTC 4x4, 6x6 and 8x8; check unsupported cases are safely retained. Inspect generated imports on each target.

## Memory and VRChat

- [ ] Record source and generated imported dimensions, formats and actual mip counts, including shared textures, skipped textures and unknown formats/shapes. Independently compare block/mip arithmetic and deduplication with the displayed source and generated totals; note incomplete totals and the reported basis. Record units (MiB versus MB).
- [ ] Build and inspect the same avatar in VRChat before and after each relevant mode. Record the actual texture-memory metric and visual behavior under the same conditions. Record download/bundle size separately: it is not GPU texture memory or generated disk size. Explain any platform or build-pipeline differences observed rather than treating an estimate as a VRChat measurement.

## Return evidence

For each item report pass/fail/blocked, actual numbers and units, settings, platform, steps and expected versus observed behavior. Provide exact Unity Console and Editor logs, AvatarTuner report/manifest where relevant, and screenshots of settings, preview, imported texture Inspector, VRChat metrics or visual differences when useful. Do not mark a check passed without its result.

Offline-only check: `dotnet run --project Tests/Standalone~/AvatarTuner.StandaloneTests.csproj`. This does not replace Unity or VRChat testing.
