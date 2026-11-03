# Third-Party Notices

AvatarTuner is an original implementation developed by Awake Ngine XE.

## Audit summary

An audit of the source tree (performed for the v1.0.0 release) found:

- **No third-party source code is bundled or merged into AvatarTuner.** All code under
  `Core/`, `Editor/`, `Tests/` and `Tools~/` was written for this project. In particular,
  `Core/MiniJson.cs` is an original implementation with its own API surface; it is not a
  copy of the well-known MiniJSON.cs snippet.
- **No third-party assets, libraries or binaries** are included in the package.
- No code carries third-party copyright headers that would require retention.

## External dependencies (referenced, not bundled)

| Package | Role | License | Notes |
| ------- | ---- | ----- | ----- |
| NDMF (`nadena.dev.ndmf`) | Optional. When installed, enables the build-time texture application pass (`Editor/NDMF/`), which calls NDMF's public plugin API. Nothing from NDMF is shipped inside AvatarTuner. | MIT — © the NDMF authors (<https://github.com/bdunderscore/ndmf>) |

`Tools~/editor-api-check~/UnityStubs.cs` contains signature-only re-declarations of the
NDMF public API (and Unity's), used exclusively for offline type-checking. No
implementation code from NDMF is present, and that file is never shipped to Unity.

## Detection-only references (by name, no code)

AvatarTuner detects the following tools by component/package/assembly **names** to avoid
conflicts and to protect their generated state. No code from these projects is used:

- Modular Avatar (`nadena.dev.modular-avatar`, MIT) — <https://github.com/bdunderscore/modular-avatar>
- VRCFury — <https://vrcfury.com/>
- Avatar Optimizer (AAO, `com.anatawa12.avatar-optimizer`, MIT) — <https://github.com/anatawa12/AvatarOptimizer>
- d4rkAvatarOptimizer (`com.d4rkavataroptimizer`, MIT) — <https://github.com/d4rkc0d3r/d4rkAvatarOptimizer>

VRChat and VRC are trademarks of VRChat Inc. AvatarTuner is not affiliated with or endorsed
by VRChat Inc.
