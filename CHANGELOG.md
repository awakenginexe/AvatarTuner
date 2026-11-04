# Changelog

All notable changes to AvatarTuner are documented here, newest version first.
Release notes are written for users; the GitHub Release description for each version is
extracted from the matching section below.

## v2.0.0

### Added and changed

- Fixed the workspace preview's Unity 2022.3 scroll-mode enum so importing the Editor assembly does not fail with CS0117; the offline API stub and regression guard now reject the invalid alias.

- Quick and Advanced workspace with adaptive candidate evaluation, texture inventory, inherited Global, Category and Custom settings, and selected-texture preview.
- Classifies textures using their usage and reports the evidence; users can correct categories. Shared textures are counted once, and unknown memory remains explicitly incomplete.
- Chooses the smallest candidate meeting quality constraints; the memory ratio is a goal and may not be reached. Standard uses Unity Mitchell resizing; Best Quality also compares Unity Bilinear and, for PNG/JPEG, Area, Lanczos3 and Mitchell. Native importer compression quality is 50 or 100 respectively.
- Reports planned and imported-property-based memory estimates separately; neither is a VRChat measurement. ASTC 4x4, 6x6 and 8x8 are Android-only and require an active Android target and decoding support.
- Standard Minimum, Normal and Maximum now give clearly different results and compress much more. Uncompressed RGB24/RGBA32 textures are converted to BC formats, textures without a recognized category (for example an outfit named "Costume") are optimized with default thresholds, LDR emission maps are treated as color, and lilToon layer and mask slots (`_Main2ndTex`, `*BlendMask`, `_ShadowBorderMask`) are recognized. Minimum may drop one resolution step only when strict full-resolution checks show no visible loss; Normal and Maximum judge masks at the same viewing scale as color, with stricter limits.
- Normal maps are now evaluated by decoding them the way Unity shaders do (BC5 and DXT5nm compare correctly) and judged by average lighting-angle change: at most 2° for Normal and 3.5° for Maximum. Minimum keeps strict full-resolution checks.
- Settings persist per avatar in the project Library. Category and Custom overrides copy inherited settings when created; reset returns to inheritance. Quick quality changes preserve Custom overrides.
- Rechecks scene bindings, materials, shaders and protection evidence before applying a reviewed plan. Failed generation, application or manifest persistence retains the previous recovery state and rolls back generated files; duplicate and empty material slots keep their indices.

### Migration and limitations

- Legacy manifests remain readable and restore remains available. Their measured basis is unknown, and outputs from older tool versions are stale and require re-optimization.
- HDR, normal maps converted from height maps and ambiguous shapes are preserved when imported candidates cannot be validated. BC6H is recognized for memory arithmetic and reporting; no new HDR transformation is provided.
- Original assets remain unchanged; generated copies, Undo, restore and optional NDMF build application remain available.

## v1.0.1

### Fixed

- **Fixed textures appearing reddish or displaying incorrect colors**:
  - MatCap and ramp textures are now recognized as full-color images and are no longer compressed into single-channel formats (such as BC4) that stripped color channels.
  - Multi-channel packed masks (such as metallic/smoothness and mask maps) now properly retain all color and alpha data.
  - Textures used across multiple materials now resolve safely to preserve full color instead of being forced into grayscale or mask formats.
  - Importer settings now faithfully match original source texture color spaces (sRGB vs. linear), alpha settings, and filtering.
- **Fixed material-swap animations in VRChat FX layers**:
  - Fixed an issue where animation curves changing materials on avatar meshes were skipped. AvatarTuner now properly detects and remaps material swaps on renderers and across VRChat descriptor playable layers (FX).
- **Preserved texture references during repeated optimization**:
  - Regenerating textures now updates files in place instead of deleting them, keeping asset IDs (GUIDs) stable and preventing missing or broken material references when re-optimizing.
- **Updated material properties on re-optimization**:
  - Re-optimizing an avatar now refreshes generated materials with any recent changes made to the original material's colors, shader keywords, or render queues.
- **Safe and non-destructive Analyze and Restore**:
  - Clicking "Analyze" is now strictly read-only and no longer modifies scene references or resets tracking state.
  - Restoring original textures now safely preserves recovery information if any avatar parts were renamed or missing, allowing you to retry without losing track of modified materials.

## v1.0.0

### What's New

- Optimize avatar textures directly inside Unity: analyze an avatar, review the planned
  changes, and confirm — optimized textures are generated and applied to the avatar in the
  scene.
- Choose between three presets: **Quality First** (no resolution reduction), **Balanced**
  (default; one resolution step down, never below 1024), and **Size First** (largest
  savings; review flagged textures).
- Per-texture overrides: exclude textures, keep resolution or format, force a maximum size
  or a specific format.
- Optimized materials and textures are applied directly to the scene avatar (with Undo
  support and prefab-instance overrides), including material-swap animations where the
  clips and controllers can be safely remapped.
- One-click **Restore Original Textures** returns every material reference to the original
  assets. Original textures, materials, prefabs and animations are never modified.
- Change detection: reverting a material, adding an unoptimized renderer, or editing source
  assets is detected and reported, with a Re-optimize path.
- Optional build-time application via NDMF (when installed) for users who prefer not to
  change the scene.
- Full transparency: texture memory and savings are always labelled as computed estimates;
  the results page shows source, output and current texture memory, per-texture results,
  counts, warnings and the output folder.
- Use AvatarTuner in English, Thai, or Simplified Chinese (switchable live in the window).

### Important Notes

- AvatarTuner optimizes texture assets and material references only. It does not reduce
  triangle counts, mesh counts, or material slot counts, and it does not resolve every
  VRChat performance warning.
- Texture memory estimates may differ from VRChat's reported values; see the README for
  the measurement notes. Always review your avatar's appearance and animation behavior in
  VRChat before uploading.
- Optimization results depend on the avatar, its textures, materials, shaders and settings.
- Direct application requires the avatar to be a scene object (a prefab instance in the
  scene is fine); selecting a prefab asset from the Project window generates outputs
  without applying them.
- Requires Unity 2022.3 (verified on 2022.3.22f1). The build-time application additionally
  requires the NDMF package (bundled with Modular Avatar and VRCFury).
