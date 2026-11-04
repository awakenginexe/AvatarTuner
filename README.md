**English** | [ไทย](README.th.md) | [简体中文](README.zh-Hans.md)

# AvatarTuner

AvatarTuner analyzes and optimizes VRChat avatar texture memory in the Unity Editor. It generates copies of textures, materials and, where needed, animation assets; it does not modify original assets. It does not reduce mesh triangles or merge meshes or materials.

## Installation and status

Version 2.0.0 is in preparation and has not been published. For local validation, install this checkout as a local package in a VCC project using Unity 2022.3.22f1. Existing published versions are available through the [VCC repository](https://awakenginexe.github.io/AvatarTuner/index.json) and [GitHub Releases](https://github.com/awakenginexe/AvatarTuner/releases). Do not expect those sources to contain 2.0.0 yet. The VRChat SDK is not a compile-time dependency; NDMF is optional for build-time application.

## Usage

1. Open **Tools → AvatarTuner**, select a scene avatar and analyze it.
2. Click **Optimize Avatar** and choose **Standard** for automatic texture/format/resolution choices or **Advanced** to include specific textures and set their own targets. Opening either mode and changing settings does not process textures.
3. Click **Confirm & Optimize**. AvatarTuner evaluates candidates, then generates and applies acceptable copies in the same action. Advanced preview is optional and evaluates candidates as needed without applying changes. Processing can be cancelled; originals remain unchanged.
4. Generated assets are stored under `Assets/AvatarTuner-Generated/`. Scene application uses generated material copies and supports Undo and restore. Safely remappable material-swap animations use generated clips and controllers; unsafe cases retain originals and report limitations. Optional NDMF application operates at build time.

Advanced selections and settings are stored per avatar in the project's `Library`. Only checked textures are evaluated and optimized in Advanced. Creating a Category or Custom override takes a snapshot of inherited settings; Reset returns to inheritance. Switching modes retains Advanced tuning and explicit exclusions/protections.

Standard offers **Minimum**, **Normal** (default), and **Maximum**. Minimum keeps strict full-resolution quality checks and may drop one resolution step only when those checks show no visible loss; uncompressed RGB24/RGBA32 textures are moved to BC formats. Normal permits up to two resolution steps down, such as 2048 to 512, with more compression-friendly visual thresholds. Maximum uses stronger compression thresholds and lower long-edge floors for visual textures: 512 for face/eyes, 256 for hair and 128 for other known color textures. Mask/data floors and channel protections remain in place. These are search limits, not forced output sizes; excluded and protected textures remain unchanged. The level is saved per avatar, and choosing it does not start processing.

Maximum searches for each texture's smallest acceptable result instead of forcing every texture to its category floor. Normal compares visual structure at a 512px long edge, Maximum at 256px; both reference and candidate are filtered to the same viewing scale. This permits fine texel detail to soften while retaining local color and used-alpha features at that scale. Masks and other data textures are judged at the same viewing scale, with stricter thresholds than color. Normal maps are judged by how far the decoded lighting direction moves (at most 2° on average for Normal, 3.5° for Maximum). Full-resolution alpha coverage checks remain, and Minimum and Advanced retain their full-resolution behavior. Detailed textures can retain higher resolution while simpler textures shrink further. These viewing scales are quality budgets, not a measurement of the texture's size on the avatar; close-up appearance still needs a visual check.

Advanced's **Select all** checkbox includes every texture matching the current search and category across all pages. The visible compression controls apply a format and reduction target to checked textures. Each texture also exposes its own format and **1x–8x / 16x** presets, including **3x**, plus custom ratios without needing to create settings first. Editing remains passive until **Confirm & Optimize**.

## Candidate selection and safety

Standard chooses the smallest candidate that satisfies quality constraints. Advanced accepts custom memory-reduction ratios of 1 or greater and chooses the closest acceptable SDK-memory estimate; formats, resolution steps and quality limits may prevent an exact match. A 4x target means one-quarter of the original memory. The default encoding effort uses Unity Mitchell and importer compression quality 50. Advanced's Best Quality effort uses quality 100 and compares additional filters. Unity's native importer performs compression; no external encoder or private maximum-effort BC7 API is used.

Windows candidates include BC1, BC3, BC4, BC5 and BC7 when their channels and color interpretation fit the texture. Changing the scene bindings, source materials/shaders or protection evidence invalidates a reviewed plan. Generated files and recovery records are committed only after scene application and manifest persistence succeed.

Normal maps are compared after decoding them the way Unity shaders do, so BC5 and DXT5nm encodings compare correctly; height maps converted on import are not candidates. Textures without a recognized category (for example an outfit named "Costume") are still optimized with the default thresholds, and LDR emission maps are treated as color. HDR and ambiguous shapes are preserved when imported candidates cannot be validated safely. BC6H is recognized for memory arithmetic and reporting, not new HDR transforms. ASTC 4x4, 6x6 and 8x8 are Android-only, requiring an active Android build target and decoding support. Existing optimizer protections and exclusions remain in effect.

## Understanding memory

Texture memory follows the VRChat SDK's format/mip arithmetic and MB convention (bytes / 1,048,576). Current Avatar Texture Memory uses the installed SDK's performance calculation when available; otherwise the UI labels it as an SDK-compatible estimate. Planned and imported-output totals remain estimates, with shared textures deduplicated and unknown formats or shapes marked incomplete. The planning inventory can include animation-only references beyond the SDK's current renderer references. These values are not built-avatar, download or disk measurements. Older saved memory totals require a fresh analysis; generated assets and restore information are retained.

Results compare the full set of referenced texture files before and after, including unchanged sources. Importer compression can reduce GPU memory without shrinking a copied PNG/JPEG file. Originals stay on disk; these file totals are not project disk usage or VRChat download/bundle size.

The 2.0.0 Unity Editor and VRChat behavior has not yet been tested. Follow the [manual testing checklist](docs/Manual-Testing-2.0.0.md) and record actual results before relying on it. Offline tests can be run with `dotnet run --project Tests/Standalone~/AvatarTuner.StandaloneTests.csproj`.

See [changelog](CHANGELOG.md), [third-party notices](THIRD_PARTY_NOTICES.md), and [MIT license](LICENSE).
