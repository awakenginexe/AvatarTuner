# AvatarTuner agent API

Use this API through a Unity editor C# execution bridge, such as Unity MCP. Desktop control is optional. Install AvatarTuner in the intended Unity 2022.3 project first, wait for compilation, and call on Unity's main thread in Edit Mode. This is an in-editor API, not a standalone CLI or MCP server.

## Discover and call

Public type: `AvatarTuner.Editor.Automation.AvatarTunerAgentApi`, assembly `AvatarTuner.Editor`. `Describe()` returns the API version, actions, side effects and default settings as JSON. `Execute(GameObject avatar, string requestJson, Func<bool> cancelled = null)` returns a JSON string.

Both package assemblies have `autoReferenced: false`. An editor integration can explicitly reference `AvatarTuner.Editor` and `AvatarTuner.Core` in its asmdef. A Unity MCP snippet can use reflection without those references:

```csharp
var api = System.Type.GetType(
    "AvatarTuner.Editor.Automation.AvatarTunerAgentApi, AvatarTuner.Editor", true);
return (string)api.GetMethod("Describe").Invoke(null, null);
```

Resolve the intended scene avatar root through your bridge's scene query first. Copy its current Unity instance ID; do not guess by name or use the user's current selection. Substitute that ID for `12345`:

```csharp
var avatar = UnityEditor.EditorUtility.InstanceIDToObject(12345) as UnityEngine.GameObject;
var api = System.Type.GetType(
    "AvatarTuner.Editor.Automation.AvatarTunerAgentApi, AvatarTuner.Editor", true);
var request = "{\"action\":\"analyze\"}";
return (string)api.GetMethod("Execute").Invoke(null, new object[] { avatar, request, null });
```

Use the same snippet with a different request for each step. The third reflection argument is the cancellation delegate; `null` means no cancellation. Calls are synchronous. Use the bridge's background-job/wait support for long optimization runs, while keeping C# execution on Unity's main thread. A bridge timeout does not prove the operation stopped; inspect the running job and editor state before retrying. If the type cannot be found, check package installation and Unity compilation errors.

## Workflow and requests

1. `Describe()`, then `{"action":"analyze"}`. Inspect the inventory, compatibility findings and current memory. Analysis does not import candidates or change scene references. Its initial plan has `evaluated: false`.
2. `{"action":"preview","settings":{"mode":"Standard","standardLevel":"Normal"}}`. Inspect per-texture decisions, warnings and memory estimates. Preview imports temporary candidates under `Assets/AvatarTuner-Generated/` and removes those scratch assets afterward; it does not apply changes or save preferences. Existing generated output is retained.
3. If changes are acceptable and optimization is requested, send `{"action":"optimize","previewToken":"COPY_FROM_DATA_PREVIEWTOKEN"}`. This applies the evaluated plan, without silently evaluating a replacement plan. It writes generated textures/materials/animation copies and the recovery manifest, and records scene reference changes with Undo. Existing generated output for this avatar can be replaced. Original assets stay unchanged. The scene is left unsaved.
4. `{"action":"restore"}` restores recorded original references with Undo and updates the manifest. Generated files are kept. A missing record or partial restoration returns failure with details. The scene is left unsaved.

All requests require an explicit scene avatar root. Prefab assets, Prefab Mode, preview scenes and descriptor-bearing avatars' child objects are rejected. No action deletes outputs, saves scenes, changes packages, switches build targets or uploads to VRChat.

Only the latest preview is retained across the editor. A new valid preview or restore request invalidates it, including cancelled requests. Every valid optimize attempt consumes its token, including wrong-token, wrong-avatar, cancelled and failed attempts. Domain reload or editor restart loses it. After any of these, preview again. The build target and recovery manifest must match; the existing engine also rechecks scene bindings, materials, dependencies and protection evidence before applying. Do not share a preview token between avatars or concurrent workflows. A no-change preview succeeds with `hasChanges: false` and `previewToken: null`; stop there.

## Settings

Omitting `settings` in preview uses the avatar's saved settings and exclusions. Providing it creates call-local settings using the existing settings schema and defaults; omitted fields take defaults. It does not overwrite saved UI preferences. Legacy saved exclusions remain enforced by the controller. Unknown fields, misspelled enum names, wrong types, duplicates in `includedTextures`, out-of-range values and texture GUIDs outside the avatar are rejected. Names are case-sensitive; copy GUIDs from analysis.

Standard uses the active build target and preserves exclusions/protections. Levels: `Minimum`, `Normal`, `Maximum`. Advanced only processes explicitly included textures:

```json
{
  "action": "preview",
  "settings": {
    "mode": "Advanced",
    "includedTextures": ["COPY_TEXTURE_GUID_FROM_ANALYSIS"],
    "global": { "desiredReduction": 3, "format": "Automatic", "platform": "Windows" },
    "textures": { "COPY_TEXTURE_GUID_FROM_ANALYSIS": { "desiredReduction": 4, "format": "BC7" } }
  }
}
```

`global`, `categories` (keyed by semantic category) and `textures` (keyed by GUID) use complete override snapshots with the same defaults as the UI schema; a per-texture object does not merge missing fields from `global`. `categoryCorrections` maps GUIDs to category names. `Describe().settingsDefaults` supplies all fields. Texture fields:

| Field | Accepted values |
|---|---|
| `desiredReduction` | Finite number >= 1; 4 means one-quarter memory |
| `format` | Canonical `TunerTextureFormat` name; use `Automatic` to let the planner choose |
| `keepResolution`, `protectQuality`, `excluded` | Boolean |
| `maxResolution` | Integer 0 through 2147483647; 0 leaves it unspecified |
| `maxDownscaleSteps` | Integer -1 through 30; -1 uses policy limits |
| `effort` | `Standard`, `BestQuality` |
| `platform` | `Windows`, `Android`; activate the corresponding Unity build target yourself |
| `minPsnr`, `minSsim` | Finite numbers in 0..200 and 0..1 respectively |

Categories: `Face`, `Eyes`, `Hair`, `Body`, `Outfit`, `Accessories`, `Effects`, `Masks`, `Mixed`, `Unknown`. Format availability and quality safeguards still depend on texture usage and the active platform. Numeric targets are search limits, not guaranteed outcomes.

## Results and failure recovery

Every result contains `apiVersion: 1`, `action`, `success`, `error` (null or `{code,message,args}`), and `data` (null when no report is available). Invalid requests may return `action: null`. `success: true` does not mean there were no warnings, that every texture changed, or that VRChat runtime quality was verified.

`data` includes `avatar` (`name`, stable `rootKey`), `buildTarget`, `settings`, `legacyOverrides`, `scanSucceeded`, `scanErrorKey`, `evaluated`, `optimizationAllowed`, `hasChanges`, `previewToken`, `applicationHealth`, `applicationFindings`, `outputFolder`, `textures`, `findings` and `memory`. `legacyOverrides` exposes saved tuning when settings are omitted; explicit settings retain only legacy exclusions.

- Textures include GUID, path, category, usage, current dimensions/format, generated path when available, and an optional `plan` with change/exclusion/protection flags, target dimensions/format/mipmaps/bytes, skip reason and quality notes. Analysis plans are unevaluated; preview plans have been evaluated. An optimize result retains the reviewed plan for comparison.
- Findings and quality notes retain stable `key` and `args`, plus a UI-language `message` (or the key if no translation exists). Findings include source, severity and risk. Check compatibility `Blocker`/`Warning` entries, plan notes, `applicationHealth` and `applicationFindings`; partial application requires attention even if generation succeeded.
- Memory is GPU texture memory in bytes, never download/bundle size. Unknown totals are `null`; `knownSourceBytes`/`knownOutputBytes` are subtotals with unknown counts. Known zero remains 0. Read `basis`, `savingsArePotential` and `currentFromSdk`. `sourceBytes`/`outputBytes` describe the plan or last recorded run; `currentBytes` describes the currently bound scene textures, including after restore. Imported totals remain estimates.

API errors use `agent.*` codes, including `invalidRequest`, `sceneAvatarRequired`, `avatarRootRequired`, `mainThreadRequired`, `editorNotReady`, `busy`, `previewRequired`, `previewStale`, `unknownTexture`, `compatibilityBlocked`, `operationFailed` and `unexpected`. Existing engine errors such as `workspace.sourceChanged` and `error.cancelled` pass through. Correct invalid input, wait for readiness, or obtain a fresh preview as appropriate; do not bypass a blocker. On an unexpected error or lost reply, inspect the scene/job/manifest before issuing another write.

Native integrations can supply a fast, side-effect-free cancellation delegate. Evaluation and generation poll it and use the existing rollback path; analysis and restoration only check before starting. Cancellation cannot guarantee rollback after a successful commit.

## Verification status

The standalone suite tests parsing, settings validation and report semantics. `dotnet run --project Tools~/editor-api-check~/EditorApiCheck.csproj` runs API boundary checks against Unity API stubs. The same boundary cases are available in Unity's EditMode tests. These checks do not verify native texture imports, real Unity Undo, rendering or VRChat behavior. Follow the agent section of `docs/Manual-Testing-2.0.0.md` in a test project before relying on the workflow.
