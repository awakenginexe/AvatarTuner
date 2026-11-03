# Changelog

All notable changes to AvatarTuner are documented here, newest version first.
Release notes are written for users; the GitHub Release description for each version is
extracted from the matching section below.

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
