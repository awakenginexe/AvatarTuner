// AvatarTuner v1.0.0 — dependency aware avatar scanner.
//
// The scanner observes, it never modifies. It answers four questions for the UI:
//   1. What does this avatar actually use (textures, materials, meshes, animators, dynamics)?
//   2. How is each asset used (usage kind, alpha, sharing)?
//   3. What could change at build time without appearing in the editor?
//   4. What must be preserved by the optimizer (material swaps, dynamics, blend shapes)?
//
// VRChat components (PhysBones, Contacts) are discovered through reflection so AvatarTuner
// has no compile-time dependency on the VRChat SDK and keeps working in projects without it.

using System;
using System.Collections.Generic;
using System.IO;
using AvatarTuner.Core;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarTuner.Editor.Analysis
{
    /// <summary>Knobs for a scan. Defaults are safe for any project size.</summary>
    public sealed class ScanOptions
    {
        /// <summary>Hash source files so byte-identical duplicates can be reported.</summary>
        public bool ComputeContentHashes = true;

        /// <summary>Look outside the avatar for assets that share its textures.</summary>
        public bool DetectExternalSharing = true;

        /// <summary>
        /// Whether inactive GameObjects take part in the analysis. When false the scan
        /// walks active objects only; inactive ones are neither counted nor modified.
        /// </summary>
        public bool IncludeInactive = true;

        /// <summary>Hard cap on how many project materials the external sharing check may open.</summary>
        public int ExternalSharingScanCap = 5000;

        /// <summary>Files above this size are hashed as "too large" instead of being read.</summary>
        public long MaxHashedFileBytes = 64L * 1024L * 1024L;

        /// <summary>Optional recorded optimization state, used to resolve applied clones back to source assets.</summary>
        public AvatarOptimizationState RecordedState;
    }

    public static class AvatarScanner
    {
        // ===================================================================== entry point

        public static AvatarAnalysisReport Scan(GameObject avatarRoot, ScanOptions options)
        {
            var report = new AvatarAnalysisReport();
            options = options ?? new ScanOptions();

            if (avatarRoot == null)
            {
                report.ScanSucceeded = false;
                report.ErrorMessage = "scan.error.noAvatar";
                return report;
            }

            try
            {
                report.AvatarName = avatarRoot.name;
                report.AvatarRootPath = AssetDatabase.GetAssetPath(avatarRoot);
                if (!string.IsNullOrEmpty(report.AvatarRootPath))
                {
                    report.AvatarRootGuid = AssetDatabase.AssetPathToGUID(report.AvatarRootPath);
                }

                ScanRenderers(avatarRoot, report, options);
                ScanAnimators(avatarRoot, report, options);
                ScanDynamicComponents(avatarRoot, report, options);
                ScanInactiveObjects(avatarRoot, report, options);
                ClassifyUsages(report);
                ClassifySemantics(report);
                AnalyzeSharing(avatarRoot, report, options);
                if (options.ComputeContentHashes)
                {
                    ComputeContentHashes(report, options);
                }

                DetectDuplicateTextures(report);
                DetectIndirectDependencies(avatarRoot, report);
                CompatibilityScanner.Scan(avatarRoot, report);

                report.ScanSucceeded = true;
            }
            catch (Exception ex)
            {
                // A scanner must never take the editor window down with it. The message key is
                // localized by the UI; the raw exception text is carried as an argument.
                report.ScanSucceeded = false;
                report.ErrorMessage = "scan.error.exception";
                report.AddFinding("scan.error.exceptionDetail", ex.GetType().Name, ex.Message);
            }

            return report;
        }

        // ===================================================================== renderers

        private static void ScanRenderers(GameObject root, AvatarAnalysisReport report, ScanOptions options)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(options.IncludeInactive);
            var materialUseCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var textureBindingCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var cloneToOriginal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (options.RecordedState != null && options.RecordedState.AppliedToAvatar)
            {
                for (var i = 0; i < options.RecordedState.MaterialClones.Count; i++)
                {
                    var clone = options.RecordedState.MaterialClones[i];
                    if (!string.IsNullOrEmpty(clone.GeneratedMaterialPath) && !string.IsNullOrEmpty(clone.OriginalMaterialGuid))
                    {
                        cloneToOriginal[clone.GeneratedMaterialPath] = clone.OriginalMaterialGuid;
                    }
                }

                for (var i = 0; i < options.RecordedState.RendererBindings.Count; i++)
                {
                    var binding = options.RecordedState.RendererBindings[i];
                    if (!string.IsNullOrEmpty(binding.AppliedMaterialGuid) && !string.IsNullOrEmpty(binding.OriginalMaterialGuid))
                    {
                        cloneToOriginal[binding.AppliedMaterialGuid] = binding.OriginalMaterialGuid;
                    }

                    if (!string.IsNullOrEmpty(binding.AppliedMaterialPath) && !string.IsNullOrEmpty(binding.OriginalMaterialGuid))
                    {
                        cloneToOriginal[binding.AppliedMaterialPath] = binding.OriginalMaterialGuid;
                    }
                }
            }

            for (var r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (renderer == null)
                {
                    continue;
                }

                var entry = new RendererEntry();
                entry.Path = HierarchyPath(renderer.transform);
                entry.IsSkinned = renderer is SkinnedMeshRenderer;
                entry.GameObjectActive = renderer.gameObject.activeSelf;
                entry.GameObjectActiveInHierarchy = renderer.gameObject.activeInHierarchy;

                Mesh mesh = null;
                var skinned = renderer as SkinnedMeshRenderer;
                if (skinned != null)
                {
                    mesh = skinned.sharedMesh;
                    entry.BoneCount = skinned.bones != null ? skinned.bones.Length : 0;
                }
                else
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null)
                    {
                        mesh = filter.sharedMesh;
                    }
                }

                if (mesh != null)
                {
                    entry.MeshName = mesh.name;
                    entry.VertexCount = mesh.vertexCount;
                    entry.SubMeshCount = mesh.subMeshCount;
                    entry.TriangleCount = CountTriangles(mesh);

                    var meshPath = AssetDatabase.GetAssetPath(mesh);
                    entry.MeshGuid = string.IsNullOrEmpty(meshPath)
                        ? string.Empty
                        : AssetDatabase.AssetPathToGUID(meshPath);

                    if (skinned != null)
                    {
                        entry.BlendShapeCount = mesh.blendShapeCount;
                    }
                }

                var materials = renderer.sharedMaterials;
                for (var m = 0; m < materials.Length; m++)
                {
                    var material = materials[m];
                    if (material == null)
                    {
                        continue;
                    }

                    if (cloneToOriginal.Count > 0)
                    {
                        var matPath = AssetDatabase.GetAssetPath(material);
                        var matGuid = string.IsNullOrEmpty(matPath) ? string.Empty : AssetDatabase.AssetPathToGUID(matPath);
                        string origGuid;
                        if ((!string.IsNullOrEmpty(matPath) && cloneToOriginal.TryGetValue(matPath, out origGuid)) ||
                            (!string.IsNullOrEmpty(matGuid) && cloneToOriginal.TryGetValue(matGuid, out origGuid)))
                        {
                            var origMat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(origGuid));
                            if (origMat != null)
                            {
                                material = origMat;
                            }
                        }
                    }

                    var materialGuid = RegisterMaterial(material, entry.Path, report);
                    if (string.IsNullOrEmpty(materialGuid))
                    {
                        continue;
                    }

                    if (!entry.MaterialGuids.Contains(materialGuid))
                    {
                        entry.MaterialGuids.Add(materialGuid);
                    }

                    int used;
                    materialUseCount.TryGetValue(materialGuid, out used);
                    materialUseCount[materialGuid] = used + 1;

                    CollectMaterialTextures(material, materialGuid, textureBindingCount, report);
                }

                report.Renderers.Add(entry);
            }

            // Sharing as seen from inside the avatar.
            foreach (var pair in materialUseCount)
            {
                var material = report.FindMaterial(pair.Key);
                if (material != null && pair.Value > 1)
                {
                    material.Sharing = SharingKind.SharedInAvatar;
                }
            }

            foreach (var pair in textureBindingCount)
            {
                var texture = report.FindTexture(pair.Key);
                if (texture != null && pair.Value > 1)
                {
                    texture.Sharing = SharingKind.SharedInAvatar;
                }
            }
        }

        private static int CountTriangles(Mesh mesh)
        {
            // GetIndexCount avoids allocating the full index array that Mesh.triangles creates.
            long indices = 0;
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                indices += mesh.GetIndexCount(s);
            }

            return (int)(indices / 3L);
        }

        private static string RegisterMaterial(Material material, string usedByPath, AvatarAnalysisReport report)
        {
            var path = AssetDatabase.GetAssetPath(material);
            var guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                // A material that exists only in memory (embedded in a scene) has no GUID.
                // It is still reported so the user knows it exists.
                report.AddFinding("finding.inlineMaterial", material.name);
                return string.Empty;
            }

            var entry = report.FindMaterial(guid);
            if (entry == null)
            {
                entry = new MaterialEntry();
                entry.Guid = guid;
                entry.Path = path;
                entry.Name = material.name;

                if (material.shader != null)
                {
                    entry.ShaderName = material.shader.name;
                    var shaderPath = AssetDatabase.GetAssetPath(material.shader);
                    entry.ShaderGuid = string.IsNullOrEmpty(shaderPath)
                        ? string.Empty
                        : AssetDatabase.AssetPathToGUID(shaderPath);
                    entry.ShaderIsCustom = IsCustomShader(entry.ShaderName, shaderPath);
                }

                report.Materials.Add(entry);
            }

            if (!string.IsNullOrEmpty(usedByPath) && !entry.UsedByRendererPaths.Contains(usedByPath))
            {
                entry.UsedByRendererPaths.Add(usedByPath);
            }

            return guid;
        }

        private static bool IsCustomShader(string shaderName, string shaderPath)
        {
            if (string.IsNullOrEmpty(shaderName))
            {
                return false;
            }

            // Built-in and package shaders live under Unity's own folders; anything else may be
            // a third-party shader whose property conventions we can only guess at.
            if (!string.IsNullOrEmpty(shaderPath) &&
                (shaderPath.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase) ||
                 shaderPath.StartsWith("Library/", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return true;
        }

        private static void CollectMaterialTextures(
            Material material,
            string materialGuid,
            Dictionary<string, int> textureBindingCount,
            AvatarAnalysisReport report)
        {
            var propertyNames = material.GetTexturePropertyNames();
            for (var p = 0; p < propertyNames.Length; p++)
            {
                var propertyName = propertyNames[p];
                var texture = material.GetTexture(propertyName);
                if (texture == null)
                {
                    continue;
                }

                var texturePath = AssetDatabase.GetAssetPath(texture);
                var textureGuid = string.IsNullOrEmpty(texturePath)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(texturePath);
                if (string.IsNullOrEmpty(textureGuid))
                {
                    continue;
                }

                var entry = report.FindTexture(textureGuid);
                if (entry == null)
                {
                    entry = new TextureEntry();
                    entry.Guid = textureGuid;
                    entry.Path = texturePath;
                    entry.Name = texture.name;
                    FillTextureImporterData(texture, entry);
                    report.Textures.Add(entry);
                }

                if (!entry.BoundProperties.Contains(propertyName))
                {
                    entry.BoundProperties.Add(propertyName);
                }

                if (material.shader != null && !entry.UsedByShaders.Contains(material.shader.name))
                {
                    entry.UsedByShaders.Add(material.shader.name);
                }

                if (!entry.UsedByMaterialGuids.Contains(materialGuid))
                {
                    entry.UsedByMaterialGuids.Add(materialGuid);
                }

                int used;
                textureBindingCount.TryGetValue(textureGuid, out used);
                textureBindingCount[textureGuid] = used + 1;

                var materialEntry = report.FindMaterial(materialGuid);
                if (materialEntry != null)
                {
                    var binding = new KeyValuePair<string, string>(propertyName, textureGuid);
                    if (!materialEntry.TextureBindings.Contains(binding))
                    {
                        materialEntry.TextureBindings.Add(binding);
                    }
                }
            }
        }

        private static void FillTextureImporterData(Texture texture, TextureEntry entry)
        {
            // Imported dimensions are the GPU texture dimensions, not the source file dimensions.
            entry.Width = texture.width;
            entry.Height = texture.height;
            entry.CurrentFormatName = CurrentFormatName(texture);
            TunerTextureFormat importedFormat;
            entry.CurrentFormatKnown = TextureFormatInfo.TryParse(entry.CurrentFormatName, out importedFormat) &&
                importedFormat != TunerTextureFormat.Automatic && importedFormat != TunerTextureFormat.Unknown &&
                TextureFormatInfo.BytesPerPixel(importedFormat) > 0;
            var texture2D = texture as Texture2D;
            entry.CurrentFaceCount = texture2D != null ? 1 : 0;
            entry.CurrentMipCount = texture2D != null ? texture2D.mipmapCount : 0;
            entry.MipMapsEnabled = entry.CurrentMipCount > 1;

            var importer = AssetImporter.GetAtPath(entry.Path) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            try
            {
                // Unity reports source dimensions only once the import has finished and
                // throws otherwise; a texture still importing must not abort the scan.
                // The method returns void in 2022.3, so success is judged by the values.
                int sourceWidth;
                int sourceHeight;
                importer.GetSourceTextureWidthAndHeight(out sourceWidth, out sourceHeight);
                if (sourceWidth > 0 && sourceHeight > 0)
                {
                    entry.SourceWidth = sourceWidth;
                    entry.SourceHeight = sourceHeight;
                }
            }
            catch (Exception)
            {
                // The source dimensions remain unknown.
            }

            entry.HasAlphaChannel = importer.DoesSourceTextureHaveAlpha();
            entry.AlphaIsTransparency = importer.alphaIsTransparency;
            entry.IsNormalMap = importer.textureType == TextureImporterType.NormalMap;
            entry.SrgbTexture = importer.sRGBTexture;
            entry.MaxSizeOverride = importer.maxTextureSize;

            try
            {
                var fileInfo = new FileInfo(entry.Path);
                if (fileInfo.Exists)
                {
                    entry.SourceFileSizeBytes = fileInfo.Length;
                }
            }
            catch (IOException)
            {
                // Unreadable source file: keep 0 and carry on. Analysis must not fail on IO.
            }
        }

        /// <summary>
        /// The best available format name for any Texture subclass. Only Texture2D exposes
        /// a TextureFormat; every other subclass (Cubemap, Texture3D, RenderTexture, ...)
        /// reports its GraphicsFormat instead. An unrecognized name is fine: the planner
        /// treats an unknown current format as unknown and says so rather than guessing.
        /// </summary>
        private static string CurrentFormatName(Texture texture)
        {
            if (texture == null)
            {
                return string.Empty;
            }

            var texture2D = texture as Texture2D;
            return texture2D != null
                ? texture2D.format.ToString()
                : texture.graphicsFormat.ToString();
        }

        // ===================================================================== usage classification

        private static void ClassifyUsages(AvatarAnalysisReport report)
        {
            for (var t = 0; t < report.Textures.Count; t++)
            {
                var texture = report.Textures[t];

                // Gather every actual (propertyName, shaderName) binding across all materials
                var bindings = new List<KeyValuePair<string, string>>();
                for (var m = 0; m < texture.UsedByMaterialGuids.Count; m++)
                {
                    var matGuid = texture.UsedByMaterialGuids[m];
                    var material = report.FindMaterial(matGuid);
                    if (material == null)
                    {
                        continue;
                    }

                    for (var b = 0; b < material.TextureBindings.Count; b++)
                    {
                        if (string.Equals(material.TextureBindings[b].Value, texture.Guid, StringComparison.OrdinalIgnoreCase))
                        {
                            bindings.Add(new KeyValuePair<string, string>(
                                material.TextureBindings[b].Key,
                                material.ShaderName ?? string.Empty));
                        }
                    }
                }

                // If no material bindings were found, fall back to BoundProperties and UsedByShaders
                if (bindings.Count == 0)
                {
                    for (var b = 0; b < texture.BoundProperties.Count; b++)
                    {
                        var prop = texture.BoundProperties[b];
                        var shader = texture.UsedByShaders.Count > 0 ? texture.UsedByShaders[0] : string.Empty;
                        bindings.Add(new KeyValuePair<string, string>(prop, shader));
                    }
                }

                for (var b = 0; b < bindings.Count; b++)
                {
                    var propertyName = bindings[b].Key;
                    var shaderName = bindings[b].Value;
                    var evidence = new ClassificationEvidence();

                    var kind = TextureUsageClassifier.Classify(
                        propertyName,
                        shaderName,
                        texture.Name,
                        texture.IsNormalMap,
                        texture.HasAlphaChannel,
                        texture.AlphaIsTransparency,
                        evidence);

                    texture.Usage = TextureUsageClassifier.MergeUsage(texture.Usage, kind, texture.HasAlphaChannel);
                    for (var i = 0; i < evidence.Reasons.Count; i++)
                    {
                        if (!texture.UsageReasons.Contains(evidence.Reasons[i]))
                        {
                            texture.UsageReasons.Add(evidence.Reasons[i]);
                        }
                    }
                }
            }
        }

        private static void ClassifySemantics(AvatarAnalysisReport report)
        {
            foreach (var texture in report.Textures)
            {
                var evidence = new List<SemanticEvidence>();
                foreach (var guid in texture.UsedByMaterialGuids)
                {
                    var material = report.FindMaterial(guid);
                    if (material == null) continue;
                    foreach (var binding in material.TextureBindings)
                    {
                        if (!string.Equals(binding.Value, texture.Guid, StringComparison.OrdinalIgnoreCase)) continue;
                        if (material.UsedByRendererPaths.Count == 0)
                            evidence.Add(new SemanticEvidence { MaterialName = material.Name, ShaderName = material.ShaderName, PropertyName = binding.Key, IsAnimated = material.UsedInMaterialSwapAnimation });
                        foreach (var path in material.UsedByRendererPaths)
                        {
                            if (!texture.RendererPaths.Contains(path)) texture.RendererPaths.Add(path);
                            string meshName = null;
                            var evidencePath = "animated/" + path;
                            foreach (var renderer in report.Renderers)
                                if (string.Equals(renderer.Path, path, StringComparison.OrdinalIgnoreCase) || renderer.Path.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase)) { meshName = renderer.MeshName; evidencePath = renderer.Path; break; }
                            evidence.Add(new SemanticEvidence { RendererPath = evidencePath, MaterialName = material.Name, ShaderName = material.ShaderName, PropertyName = binding.Key, MeshName = meshName, IsAnimated = material.UsedInMaterialSwapAnimation });
                        }
                    }
                }
                var result = SemanticClassifier.Classify(texture.Name, evidence);
                texture.Category = result.Category;
                texture.CategoryConfidence = result.Confidence;
                texture.CategoryReasons.AddRange(result.Reasons);
            }
        }

        // ===================================================================== animators

        private static void ScanAnimators(GameObject root, AvatarAnalysisReport report, ScanOptions options)
        {
            var animators = root.GetComponentsInChildren<Animator>(options.IncludeInactive);
            var seenControllers = new HashSet<RuntimeAnimatorController>();

            var cloneToOriginalCtrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (options.RecordedState != null && options.RecordedState.AppliedToAvatar)
            {
                for (var i = 0; i < options.RecordedState.AnimationBindings.Count; i++)
                {
                    var animBind = options.RecordedState.AnimationBindings[i];
                    if (!string.IsNullOrEmpty(animBind.GeneratedControllerPath) && !string.IsNullOrEmpty(animBind.OriginalControllerPath))
                    {
                        cloneToOriginalCtrl[animBind.GeneratedControllerPath] = animBind.OriginalControllerPath;
                    }
                }
            }

            for (var a = 0; a < animators.Length; a++)
            {
                var animator = animators[a];
                if (animator == null || animator.runtimeAnimatorController == null)
                {
                    continue;
                }

                var controller = animator.runtimeAnimatorController;
                var controllerPath = AssetDatabase.GetAssetPath(controller);
                string origCtrlPath;
                if (!string.IsNullOrEmpty(controllerPath) && cloneToOriginalCtrl.TryGetValue(controllerPath, out origCtrlPath))
                {
                    var origCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(origCtrlPath);
                    if (origCtrl != null)
                    {
                        controller = origCtrl;
                        controllerPath = origCtrlPath;
                    }
                }

                seenControllers.Add(animator.runtimeAnimatorController);
                if (controller != null) seenControllers.Add(controller);

                var entry = new AnimatorEntry();
                entry.Guid = string.IsNullOrEmpty(controllerPath)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(controllerPath);
                entry.Path = controllerPath;
                entry.Name = controller.name;
                entry.IsAnimatorOnAvatarRoot = animator.transform == root.transform;

                CollectControllerData(controller, entry, report);

                report.Animators.Add(entry);
            }

            // Also scan controllers on avatar descriptors (e.g. VRChat FX layers)
            var descriptorControllers = CollectDescriptorControllers(root);
            for (var d = 0; d < descriptorControllers.Count; d++)
            {
                var controller = descriptorControllers[d];
                if (controller == null || seenControllers.Contains(controller))
                {
                    continue;
                }

                var controllerPath = AssetDatabase.GetAssetPath(controller);
                string origCtrlPath;
                if (!string.IsNullOrEmpty(controllerPath) && cloneToOriginalCtrl.TryGetValue(controllerPath, out origCtrlPath))
                {
                    var origCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(origCtrlPath);
                    if (origCtrl != null)
                    {
                        controller = origCtrl;
                        controllerPath = origCtrlPath;
                    }
                }

                seenControllers.Add(descriptorControllers[d]);
                if (controller != null) seenControllers.Add(controller);

                var entry = new AnimatorEntry();
                entry.Guid = string.IsNullOrEmpty(controllerPath)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(controllerPath);
                entry.Path = controllerPath;
                entry.Name = controller.name;
                entry.IsAnimatorOnAvatarRoot = true;

                CollectControllerData(controller, entry, report);

                report.Animators.Add(entry);
            }
        }

        private static void CollectControllerData(
            RuntimeAnimatorController controller,
            AnimatorEntry entry,
            AvatarAnalysisReport report)
        {
            var overrideController = controller as AnimatorOverrideController;
            if (overrideController != null)
            {
                // AnimatorOverrideController.clips is obsolete in 2022.3; GetOverrides
                // reports the same pairs (original clip in Key, override in Value).
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideController.overridesCount);
                overrideController.GetOverrides(pairs);
                for (var i = 0; i < pairs.Count; i++)
                {
                    if (pairs[i].Key != null)
                    {
                        CollectClipData(pairs[i].Key, entry, report);
                    }

                    if (pairs[i].Value != null)
                    {
                        CollectClipData(pairs[i].Value, entry, report);
                    }
                }

                entry.ClipCount += pairs.Count;
                return;
            }

            var animatorController = controller as AnimatorController;
            if (animatorController == null)
            {
                return;
            }

            entry.LayerCount = animatorController.layers.Length;
            for (var l = 0; l < animatorController.layers.Length; l++)
            {
                var layer = animatorController.layers[l];
                if (layer != null && layer.name != null &&
                    layer.name.IndexOf("fx", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    entry.HasFxLayer = true;
                }

                if (layer == null || layer.stateMachine == null)
                {
                    continue;
                }

                CollectStateMachine(layer.stateMachine, entry, report, 0);
            }
        }

        private static void CollectStateMachine(
            AnimatorStateMachine stateMachine,
            AnimatorEntry entry,
            AvatarAnalysisReport report,
            int depth)
        {
            if (stateMachine == null || depth > 16)
            {
                // Depth guard: broken or cyclic state machines must not hang the editor.
                return;
            }

            var states = stateMachine.states;
            for (var s = 0; s < states.Length; s++)
            {
                var state = states[s].state;
                if (state == null)
                {
                    continue;
                }

                entry.StateCount++;

                if (state.motion is AnimationClip clip)
                {
                    entry.ClipCount++;
                    CollectClipData(clip, entry, report);
                }
                else if (state.motion is BlendTree tree)
                {
                    CollectBlendTree(tree, entry, report, 0);
                }
            }

            var children = stateMachine.stateMachines;
            for (var c = 0; c < children.Length; c++)
            {
                CollectStateMachine(children[c].stateMachine, entry, report, depth + 1);
            }
        }

        private static void CollectBlendTree(
            BlendTree tree,
            AnimatorEntry entry,
            AvatarAnalysisReport report,
            int depth)
        {
            if (tree == null || depth > 8)
            {
                return;
            }

            var children = tree.children;
            for (var i = 0; i < children.Length; i++)
            {
                if (children[i].motion is AnimationClip clip)
                {
                    entry.ClipCount++;
                    CollectClipData(clip, entry, report);
                }
                else if (children[i].motion is BlendTree nested)
                {
                    CollectBlendTree(nested, entry, report, depth + 1);
                }
            }
        }

        private static void CollectClipData(AnimationClip clip, AnimatorEntry entry, AvatarAnalysisReport report)
        {
            if (clip == null)
            {
                return;
            }

            // Material swap animations: object reference curves that write Material references.
            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            for (var b = 0; b < objectBindings.Length; b++)
            {
                var binding = objectBindings[b];
                if (!IsMaterialBinding(binding))
                {
                    continue;
                }

                var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                for (var k = 0; k < keyframes.Length; k++)
                {
                    var target = keyframes[k].value as Material;
                    if (target == null)
                    {
                        continue;
                    }

                    var targetPath = AssetDatabase.GetAssetPath(target);
                    var targetGuid = string.IsNullOrEmpty(targetPath)
                        ? string.Empty
                        : AssetDatabase.AssetPathToGUID(targetPath);

                    var description = binding.path + "/" + binding.propertyName;
                    if (!entry.MaterialSwapTargets.Contains(description))
                    {
                        entry.MaterialSwapTargets.Add(description);
                    }

                    if (!string.IsNullOrEmpty(targetGuid))
                    {
                        var material = report.FindMaterial(targetGuid);
                        if (material == null) report.AddFinding("finding.offHierarchyMaterialSwap", target.name);
                        RegisterMaterial(target, binding.path, report);
                        material = report.FindMaterial(targetGuid);
                        if (material != null)
                        {
                            material.UsedInMaterialSwapAnimation = true;
                            CollectMaterialTextures(target, targetGuid, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), report);
                        }
                    }
                }
            }

            // Ordinary transform/property curves: which GameObjects the animation drives.
            var curveBindings = AnimationUtility.GetCurveBindings(clip);
            for (var b = 0; b < curveBindings.Length; b++)
            {
                var path = curveBindings[b].path;
                if (!string.IsNullOrEmpty(path) && !entry.AnimatedObjectPaths.Contains(path))
                {
                    entry.AnimatedObjectPaths.Add(path);
                }
            }
        }

        // ===================================================================== dynamics (reflection)

        private static void ScanDynamicComponents(GameObject root, AvatarAnalysisReport report, ScanOptions options)
        {
            var components = root.GetComponentsInChildren<Component>(options.IncludeInactive);
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue; // missing script placeholder
                }

                string kindKey;
                if (!TryClassifyDynamicComponent(component, out kindKey))
                {
                    continue;
                }

                var entry = new DynamicComponentEntry();
                entry.TypeFullName = component.GetType().FullName;
                entry.Path = HierarchyPath(component.transform);
                entry.KindKey = kindKey;

                // The affected count is the component's transform subtree: PhysBones and
                // Contacts act on their children, and counting transforms needs no SDK types.
                entry.AffectedTransformCount =
                    component.GetComponentsInChildren<Transform>(options.IncludeInactive).Length - 1;

                report.DynamicComponents.Add(entry);
            }

            if (report.DynamicComponents.Count > 0)
            {
                report.AddFinding("finding.dynamicComponents", report.DynamicComponents.Count.ToString());
            }
        }

        private static bool TryClassifyDynamicComponent(Component component, out string kindKey)
        {
            kindKey = null;
            var typeName = component.GetType().Name;

            if (typeName.IndexOf("PhysBoneCollider", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kindKey = "finding.kind.physBoneCollider";
                return true;
            }

            if (typeName.IndexOf("PhysBone", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kindKey = "finding.kind.physBone";
                return true;
            }

            if (typeName.IndexOf("ContactSender", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kindKey = "finding.kind.contactSender";
                return true;
            }

            if (typeName.IndexOf("ContactReceiver", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kindKey = "finding.kind.contactReceiver";
                return true;
            }

            // SPS / PCS and similar animation-driven systems. Recognized only to be protected:
            // AvatarTuner never modifies them and must warn before touching their materials.
            if (typeName.IndexOf("SPS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("PCS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Penetrator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Orifice", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kindKey = "finding.kind.animationDrivenSystem";
                return true;
            }

            return false;
        }

        // ===================================================================== inactive objects

        private static void ScanInactiveObjects(GameObject root, AvatarAnalysisReport report, ScanOptions options)
        {
            var transforms = root.GetComponentsInChildren<Transform>(options.IncludeInactive);
            for (var i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t != null && !t.gameObject.activeSelf)
                {
                    report.InactiveObjects.Add(HierarchyPath(t));
                }
            }

            if (report.InactiveObjects.Count > 0)
            {
                report.AddFinding("finding.inactiveObjects", report.InactiveObjects.Count.ToString());
            }
        }

        // ===================================================================== sharing & duplicates

        private static void AnalyzeSharing(GameObject root, AvatarAnalysisReport report, ScanOptions options)
        {
            var avatarTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var avatarMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < report.Textures.Count; i++)
            {
                avatarTextures.Add(report.Textures[i].Guid);
            }

            for (var i = 0; i < report.Materials.Count; i++)
            {
                avatarMaterials.Add(report.Materials[i].Guid);
            }

            if (options.DetectExternalSharing)
            {
                var scanned = 0;
                var truncated = false;
                var guids = AssetDatabase.FindAssets("t:Material");
                for (var g = 0; g < guids.Length; g++)
                {
                    if (scanned >= options.ExternalSharingScanCap)
                    {
                        truncated = true;
                        break;
                    }

                    if (avatarMaterials.Contains(guids[g]))
                    {
                        continue;
                    }

                    var material = AssetDatabase.LoadAssetAtPath<Material>(
                        AssetDatabase.GUIDToAssetPath(guids[g]));
                    if (material == null)
                    {
                        continue;
                    }

                    scanned++;

                    var propertyNames = material.GetTexturePropertyNames();
                    for (var p = 0; p < propertyNames.Length; p++)
                    {
                        var texture = material.GetTexture(propertyNames[p]);
                        if (texture == null)
                        {
                            continue;
                        }

                        var texturePath = AssetDatabase.GetAssetPath(texture);
                        var textureGuid = string.IsNullOrEmpty(texturePath)
                            ? string.Empty
                            : AssetDatabase.AssetPathToGUID(texturePath);
                        if (string.IsNullOrEmpty(textureGuid) || !avatarTextures.Contains(textureGuid))
                        {
                            continue;
                        }

                        var entry = report.FindTexture(textureGuid);
                        if (entry != null)
                        {
                            entry.Sharing = SharingKind.SharedOutsideAvatar;
                        }
                    }
                }

                if (truncated)
                {
                    report.AddFinding("finding.sharingScanTruncated", scanned.ToString());
                }
            }

            var sharedInside = 0;
            var sharedOutside = 0;
            for (var i = 0; i < report.Textures.Count; i++)
            {
                if (report.Textures[i].Sharing == SharingKind.SharedInAvatar)
                {
                    sharedInside++;
                }
                else if (report.Textures[i].Sharing == SharingKind.SharedOutsideAvatar)
                {
                    sharedOutside++;
                }
            }

            if (sharedInside > 0)
            {
                report.AddFinding("finding.sharedTextures", sharedInside.ToString());
            }

            if (sharedOutside > 0)
            {
                // Not a problem in itself: generated copies are used instead of touching originals.
                report.AddFinding("finding.sharedOutsideAvatar", sharedOutside.ToString());
            }
        }

        private static void ComputeContentHashes(AvatarAnalysisReport report, ScanOptions options)
        {
            for (var i = 0; i < report.Textures.Count; i++)
            {
                var entry = report.Textures[i];
                if (string.IsNullOrEmpty(entry.Path) || !File.Exists(entry.Path))
                {
                    continue;
                }

                try
                {
                    var fileInfo = new FileInfo(entry.Path);
                    if (fileInfo.Length > options.MaxHashedFileBytes)
                    {
                        continue;
                    }

                    entry.ContentHash = StableHash.Sha256Hex(File.ReadAllBytes(entry.Path));
                }
                catch (IOException)
                {
                    // Hashing is best effort; a locked file must not abort the scan.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static void DetectDuplicateTextures(AvatarAnalysisReport report)
        {
            var byHash = new Dictionary<string, List<TextureEntry>>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < report.Textures.Count; i++)
            {
                var entry = report.Textures[i];
                if (string.IsNullOrEmpty(entry.ContentHash))
                {
                    continue;
                }

                List<TextureEntry> group;
                if (!byHash.TryGetValue(entry.ContentHash, out group))
                {
                    group = new List<TextureEntry>();
                    byHash[entry.ContentHash] = group;
                }

                group.Add(entry);
            }

            var duplicateGroups = 0;
            foreach (var pair in byHash)
            {
                if (pair.Value.Count < 2)
                {
                    continue;
                }

                duplicateGroups++;
                for (var i = 0; i < pair.Value.Count; i++)
                {
                    pair.Value[i].UsageReasons.Add("usage.reason.duplicateContent");
                }
            }

            if (duplicateGroups > 0)
            {
                report.AddFinding("finding.duplicateTextures", duplicateGroups.ToString());
            }
        }

        // ===================================================================== indirect dependencies

        private static void DetectIndirectDependencies(GameObject root, AvatarAnalysisReport report)
        {
            // Everything the scanner saw directly.
            var direct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < report.Textures.Count; i++)
            {
                direct.Add(report.Textures[i].Guid);
            }

            for (var i = 0; i < report.Materials.Count; i++)
            {
                direct.Add(report.Materials[i].Guid);
            }

            for (var i = 0; i < report.Renderers.Count; i++)
            {
                if (!string.IsNullOrEmpty(report.Renderers[i].MeshGuid))
                {
                    direct.Add(report.Renderers[i].MeshGuid);
                }
            }

            for (var i = 0; i < report.Animators.Count; i++)
            {
                if (!string.IsNullOrEmpty(report.Animators[i].Guid))
                {
                    direct.Add(report.Animators[i].Guid);
                }
            }

            // Everything the asset graph sees. Build-time tools can add references that only
            // show up here (or remove things later), so the leftovers are reported separately
            // as "possible build-time" instead of being silently mixed into the report.
            var rootPath = report.AvatarRootPath;
            if (string.IsNullOrEmpty(rootPath))
            {
                return; // scene object: the asset graph cannot be walked
            }

            var dependencies = AssetDatabase.GetDependencies(rootPath, true);
            for (var i = 0; i < dependencies.Length; i++)
            {
                var dependencyPath = dependencies[i];
                if (string.Equals(dependencyPath, rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var guid = AssetDatabase.AssetPathToGUID(dependencyPath);
                if (string.IsNullOrEmpty(guid) || direct.Contains(guid))
                {
                    continue;
                }

                report.PossibleBuildTimeDependencies.Add(guid);
            }

            if (report.PossibleBuildTimeDependencies.Count > 0)
            {
                report.AddFinding("finding.buildTimeDependencies",
                    report.PossibleBuildTimeDependencies.Count.ToString());
            }
        }

        // ===================================================================== helpers

        internal static string HierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var path = transform.name;
            var current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        internal static bool IsMaterialBinding(EditorCurveBinding binding)
        {
            if (binding.type == typeof(Material))
            {
                return true;
            }

            if (typeof(Renderer).IsAssignableFrom(binding.type))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(binding.propertyName) &&
                (binding.propertyName.StartsWith("m_Materials", StringComparison.Ordinal) ||
                 binding.propertyName.IndexOf("material", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            return false;
        }

        internal static List<RuntimeAnimatorController> CollectDescriptorControllers(GameObject root)
        {
            var list = new List<RuntimeAnimatorController>();
            if (root == null)
            {
                return list;
            }

            var components = root.GetComponentsInChildren<Component>(true);
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();
                if (type.Name.IndexOf("AvatarDescriptor", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                for (var f = 0; f < fields.Length; f++)
                {
                    var field = fields[f];
                    if (field.Name == "baseAnimationLayers" || field.Name == "specialAnimationLayers")
                    {
                        var array = field.GetValue(component) as Array;
                        if (array == null)
                        {
                            continue;
                        }

                        for (var a = 0; a < array.Length; a++)
                        {
                            var layer = array.GetValue(a);
                            if (layer == null)
                            {
                                continue;
                            }

                            var ctrlField = layer.GetType().GetField("animatorController", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (ctrlField != null)
                            {
                                var ctrl = ctrlField.GetValue(layer) as RuntimeAnimatorController;
                                if (ctrl != null && !list.Contains(ctrl))
                                {
                                    list.Add(ctrl);
                                }
                            }
                        }
                    }
                }
            }

            return list;
        }
    }
}
