# editor-api-check — offline Unity API stub compile harness

Type-checks the entire AvatarTuner `Editor/` tree against `UnityStubs.cs`, a minimal mirror
of the Unity 2022.3 API surface, without any Unity installation. The `~` folder suffix keeps
Unity from ever importing it; it is developer tooling only.

```bash
dotnet build Tools~/editor-api-check~/EditorApiCheck.csproj   # zero errors = type-checks clean
```

## Stub fidelity rule

Every stubbed member must exist in the real Unity 2022.3 API with the same signature —
**including members that do not exist**. An invented member turns a real Unity compile error
into a silent offline pass. This happened once: the drag-event stubs declared `Use()`
(IMGUI's `Event.Use()`), so `evt.Use()` on a `DragPerformEvent` passed this harness while
Unity 2022.3 rejected it with CS1061. UI Toolkit events derive from `EventBase`
(`StopPropagation` / `StopImmediatePropagation` / `PreventDefault` only); `Use()` is stubbed
on `UnityEngine.Event` alone, and `VisualElement.RegisterCallback<T>` only accepts
`EventBase`-derived events.

A clean build here means "type-checks against a faithful API mirror", **not** "compiles in
Unity". Only a real Unity compile verifies Unity compatibility.
