**English** | [ไทย](README.th.md) | [简体中文](README.zh-Hans.md)

# AvatarTuner

AvatarTuner analyzes and optimizes VRChat avatar texture memory in the Unity Editor. It generates copies of textures, materials and, where needed, animation assets; it does not modify original assets. It does not reduce mesh triangles or merge meshes or materials.

## Installation and status

Version 2.0.0 is in preparation and has not been published. For local validation, install this checkout as a local package in a VCC project using Unity 2022.3.22f1. Existing published versions are available through the [VCC repository](https://awakenginexe.github.io/AvatarTuner/index.json) and [GitHub Releases](https://github.com/awakenginexe/AvatarTuner/releases). Do not expect those sources to contain 2.0.0 yet. The VRChat SDK is not a compile-time dependency; NDMF is optional for build-time application.

## Usage

1. Open **Tools → AvatarTuner**, select a scene avatar and analyze it.
2. Open the optimization workspace. Quick settings adjust quality thresholds; Advanced exposes Global, Category and per-texture Custom settings, classification evidence and overrides. Check protected and excluded textures before proceeding.
3. Explicitly evaluate candidates, inspect the selected texture preview and differences, and review the estimated memory summary before confirming optimization. Evaluation can be cancelled.
4. Generated assets are stored under `Assets/AvatarTuner-Generated/`. Scene application uses generated material copies and supports Undo and restore. Safely remappable material-swap animations use generated clips and controllers; unsafe cases retain originals and report limitations. Optional NDMF application operates at build time.

Settings are stored per avatar in the project's `Library`. Creating a Category or Custom override takes a snapshot of its inherited settings; subsequent parent changes do not update that override. Reset returns it to inheritance. Changing Quick quality thresholds preserves Custom overrides.

## Candidate selection and safety

The adaptive planner chooses the smallest candidate that satisfies quality constraints. The memory ratio is an advisory goal and may remain unmet. Standard uses Unity Mitchell resizing and importer compression quality 50. Best Quality uses quality 100 and compares Unity Mitchell and Bilinear; PNG/JPEG candidates can also use Area, Lanczos3 and Mitchell resampling. Unity's native importer performs compression; no external encoder or private maximum-effort BC7 API is used.

Windows candidates include BC1, BC3, BC4, BC5 and BC7 when their channels and color interpretation fit the texture. Changing the scene bindings, source materials/shaders or protection evidence invalidates a reviewed plan. Generated files and recovery records are committed only after scene application and manifest persistence succeed.

HDR, normal maps and ambiguous shapes are preserved when imported candidates cannot be validated safely. BC6H is recognized for memory arithmetic and reporting, not new HDR transforms. ASTC 4x4, 6x6 and 8x8 are Android-only, requiring an active Android build target and decoding support. Existing optimizer protections and exclusions remain in effect.

## Understanding memory

Planned numbers and estimates calculated from imported dimensions, format and mip count have different bases. Unknown formats or shapes make totals incomplete; shared textures are deduplicated. Neither estimate is a measurement of the built avatar, download size or disk size. Compare with VRChat's actual performance metrics and check units (MiB versus MB). Legacy manifests can be read and restored, but their measured basis is unknown and older-version outputs are stale; re-optimize to use the current version.

The 2.0.0 Unity Editor and VRChat behavior has not yet been tested. Follow the [manual testing checklist](docs/Manual-Testing-2.0.0.md) and record actual results before relying on it. Offline tests can be run with `dotnet run --project Tests/Standalone~/AvatarTuner.StandaloneTests.csproj`.

See [changelog](CHANGELOG.md), [third-party notices](THIRD_PARTY_NOTICES.md), and [MIT license](LICENSE).
