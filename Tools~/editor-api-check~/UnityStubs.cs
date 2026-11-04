// Minimal Unity API surface stubs, used ONLY by the offline editor-code compile check
// (Tools~/editor-api-check~). These are never shipped and never touch Unity; they exist so
// the Editor sources can be type-checked without a Unity installation.
//
// Signature discipline: every member must match the real Unity 2022.3 API, quirks included
// (GetSourceTextureWidthAndHeight returns void, Texture has no `format`, Transform derives
// from Component, UI Toolkit's EventBase has NO Use()). That fidelity is exactly what this
// harness exists to enforce: a stub member that the real API lacks turns a real Unity
// compile error into a silent offline pass. When in doubt, model the real hierarchy and
// leave invented conveniences out.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class Application
    {
        public static string unityVersion { get { return "2022.3.22f1"; } }
        public static void OpenURL(string url) { }
    }

    public class Object
    {
        public string name { get; set; }

        // Real Unity: Instantiate is a static on UnityEngine.Object.
        public static T Instantiate<T>(T original) where T : Object { return original; }
        public static void DestroyImmediate(Object obj, bool allowDestroyingAssets = false) { }
    }

    // Real Unity: ScriptableObject lives in UnityEngine and EditorWindow derives from it.
    public class ScriptableObject : Object
    {
    }

    public struct Color
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static Color white { get { return new Color(1, 1, 1, 1); } }
        public static Color clear { get { return new Color(0, 0, 0, 0); } }
    }

    public enum FontStyle
    {
        Normal = 0,
        Bold = 1
    }

    public enum TextAnchor
    {
        MiddleLeft = 3,
        MiddleCenter = 4
    }

    public enum TextureWrapMode
    {
        Repeat = 0,
        Clamp = 1,
        Mirror = 2,
        MirrorOnce = 3
    }

    public enum FilterMode
    {
        Point = 0,
        Bilinear = 1,
        Trilinear = 2
    }

    public enum ScaleMode
    {
        StretchToFill = 0,
        ScaleAndCrop = 1,
        ScaleToFit = 2
    }

    public class TextAsset : Object
    {
        public string text { get; set; }
    }

    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public struct Rect
    {
        public float width;
        public float height;

        public bool Contains(Vector2 point) { return false; }
    }

    public struct Color32
    {
        public byte r;
        public byte g;
        public byte b;
        public byte a;

        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }
    }

    public enum EventType
    {
        Repaint = 0,
        DragUpdated = 14,
        DragPerform = 15
    }

    // IMGUI event. Use() is IMGUI-only: it exists here and ONLY here. The UI Toolkit
    // event stubs below deliberately do not offer it (see the EventBase comment).
    public class Event
    {
        public static Event current { get { return null; } }
        public EventType type { get; set; }
        public Vector2 mousePosition { get; set; }
        public void Use() { }
    }

    public class GUIStyle
    {
    }

    public class GUIContent
    {
        public GUIContent(string text) { }
        public GUIContent(string text, string tooltip) { }
    }

    public class GUILayoutOption
    {
    }

    public static class GUI
    {
        public static void Box(Rect position, string text, GUIStyle style) { }
    }

    public static class GUILayout
    {
        public static bool Button(string text, params GUILayoutOption[] options) { return false; }
        public static bool Button(GUIContent content, params GUILayoutOption[] options) { return false; }
        public static int Toolbar(int selected, string[] texts) { return selected; }
        public static GUILayoutOption Width(float width) { return new GUILayoutOption(); }
        public static GUILayoutOption ExpandWidth(bool expand) { return new GUILayoutOption(); }
        public static void Space(float pixels) { }
        public static void FlexibleSpace() { }
    }

    public static class GUILayoutUtility
    {
        public static Rect GetRect(float width, float height, params GUILayoutOption[] options)
        {
            return new Rect();
        }
    }

    public static class GUIUtility
    {
        public static void ExitGUI() { }
    }

    public enum TextureFormat
    {
        DXT1 = 10,
        DXT5 = 12,
        BC7 = 25,
        RGBA32 = 4
    }

    namespace Experimental.Rendering
    {
        public enum GraphicsFormat
        {
            None = 0,
            BC7_UNORM = 0
        }
    }

    public class Texture : Object
    {
        public int width { get; set; }
        public int height { get; set; }
        public int mipmapCount { get; }
        public bool isReadable { get; }
        public Experimental.Rendering.GraphicsFormat graphicsFormat { get; set; }
    }

    public class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain, bool linear) { }
        public TextureFormat format { get; set; }
        public Color32[] GetPixels32(int miplevel) { return Array.Empty<Color32>(); }
        public Color32[] GetPixels32() { return GetPixels32(0); }
        public void SetPixels32(Color32[] colors, int miplevel) { }
        public void SetPixels32(Color32[] colors) { SetPixels32(colors, 0); }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
        public void Apply(bool updateMipmaps) { Apply(updateMipmaps, false); }
        public void Apply() { Apply(true, false); }
    }

    public static class ImageConversion
    {
        public static bool LoadImage(Texture2D tex, byte[] data, bool markNonReadable = false) { return false; }
        public static byte[] EncodeToPNG(Texture2D tex) { return Array.Empty<byte>(); }
    }

    public static class Mathf
    {
        public static float Clamp01(float value) { return value; }
    }

    // Real Unity: Transform derives from Component.
    public class Transform : Component
    {
        public Transform parent { get; set; }
        public int childCount { get; set; }
        public Transform GetChild(int index) { return null; }
    }

    public class Component : Object
    {
        public Transform transform { get; set; }
        public GameObject gameObject { get; set; }

        public T GetComponent<T>() where T : Component { return null; }

        public T[] GetComponents<T>() where T : Component
        {
            return Array.Empty<T>();
        }

        public Component[] GetComponents(Type type)
        {
            return Array.Empty<Component>();
        }

        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            return Array.Empty<T>();
        }
    }

    public class GameObject : Object
    {
        // Real Unity: GameObject exposes its transform directly.
        public Transform transform { get; set; }
        public bool activeSelf { get; set; }
        public bool activeInHierarchy { get; set; }
        public UnityEngine.SceneManagement.Scene scene { get; set; }

        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : Component
        {
            return Array.Empty<T>();
        }

        public Component[] GetComponents(Type type)
        {
            return Array.Empty<Component>();
        }

        public T[] GetComponents<T>() where T : Component
        {
            return Array.Empty<T>();
        }
    }

    public class Shader : Object
    {
    }

    public class Material : Object
    {
        public Shader shader { get; set; }
        public string[] shaderKeywords { get; set; } = Array.Empty<string>();
        public int renderQueue { get; set; }
        public void CopyPropertiesFromMaterial(Material mat) { }
        public string[] GetTexturePropertyNames() { return Array.Empty<string>(); }
        public Texture GetTexture(string name) { return null; }
        public void SetTexture(string name, Texture value) { }
    }

    public class Mesh : Object
    {
        public int vertexCount { get; set; }
        public int subMeshCount { get; set; }
        public int blendShapeCount { get; set; }
        public uint GetIndexCount(int submesh) { return 0; }
    }

    public class MeshFilter : Component
    {
        public Mesh sharedMesh { get; set; }
    }

    public class Renderer : Component
    {
        public Material[] sharedMaterials { get; set; }
    }

    public class SkinnedMeshRenderer : Renderer
    {
        public Mesh sharedMesh { get; set; }
        public Transform[] bones { get; set; }
        public Transform rootBone { get; set; }
    }

    public class Motion : Object
    {
    }

    public class AnimationClip : Motion
    {
    }

    public class RuntimeAnimatorController : Object
    {
    }

    public class AnimatorOverrideController : RuntimeAnimatorController
    {
        public int overridesCount { get; set; }
        public RuntimeAnimatorController runtimeAnimatorController { get; set; }

        // Real signature: fills the list, returns void.
        public void GetOverrides(List<KeyValuePair<AnimationClip, AnimationClip>> overrides)
        {
        }

        public void ApplyOverrides(IList<KeyValuePair<AnimationClip, AnimationClip>> overrides)
        {
        }
    }

    public class Behaviour : Component
    {
    }

    public class Animator : Behaviour
    {
        public RuntimeAnimatorController runtimeAnimatorController { get; set; }
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }
}

namespace UnityEngine.UIElements
{
    using System;
    using System.Collections.Generic;

    public delegate void EventCallback<TEventType>(TEventType evt);

    // Real Unity: UI Toolkit events derive from EventBase, which exposes StopPropagation(),
    // StopImmediatePropagation() and PreventDefault() - and NO Use(). Use() belongs to
    // IMGUI's UnityEngine.Event (stubbed above); calling it on a UI Toolkit event is
    // CS1061 in Unity 2022.3 (a real regression this harness failed to catch once, because
    // an earlier stub version invented Use() here). Keep the real hierarchy modeled and do
    // not add convenience members the real API lacks: an invented member silently converts
    // a Unity compile error into an offline pass.
    public class EventBase
    {
        public void StopPropagation() { }
        public void StopImmediatePropagation() { }
        public void PreventDefault() { }
    }

    // Real Unity: MouseEventBase<T> : EventBase, IMouseEvent (mousePosition, button, ...).
    public class MouseEventBase<T> : EventBase
    {
        public Vector2 mousePosition { get; set; }
        public int button { get; set; }
    }

    // Real Unity: drag events inherit DragAndDropEventBase<T> -> MouseEventBase<T> -> EventBase.
    public class DragAndDropEventBase<T> : MouseEventBase<T>
    {
    }

    public class DragUpdatedEvent : DragAndDropEventBase<DragUpdatedEvent>
    {
    }

    public class DragPerformEvent : DragAndDropEventBase<DragPerformEvent>
    {
    }

    public class DragExitedEvent : DragAndDropEventBase<DragExitedEvent>
    {
    }

    // Real Unity: mouse enter/leave events inherit MouseEventBase<T> -> EventBase.
    public class MouseEnterEvent : MouseEventBase<MouseEnterEvent>
    {
    }

    public class MouseLeaveEvent : MouseEventBase<MouseLeaveEvent>
    {
    }

    // Real Unity: GeometryChangedEvent : EventBase (oldRect/newRect are properties).
    public class GeometryChangedEvent : EventBase
    {
        public Rect oldRect { get; set; }
        public Rect newRect { get; set; }
    }

    public struct StyleLength
    {
        public static implicit operator StyleLength(float value) { return new StyleLength(); }
        public static implicit operator StyleLength(StyleKeyword keyword) { return new StyleLength(); }
    }

    public struct StyleFloat
    {
        public static implicit operator StyleFloat(float value) { return new StyleFloat(); }
    }

    public struct StyleColor
    {
        public static implicit operator StyleColor(Color value) { return new StyleColor(); }
    }

    public struct StyleEnum<T> where T : struct
    {
        public static implicit operator StyleEnum<T>(T value) { return new StyleEnum<T>(); }
    }

    public enum StyleKeyword
    {
        Auto = 0
    }

    public enum WhiteSpace
    {
        Normal = 0,
        NoWrap = 1
    }

    public enum FlexDirection
    {
        Column = 0,
        ColumnReverse = 1,
        Row = 2,
        RowReverse = 3
    }

    public enum Wrap
    {
        NoWrap = 0,
        Wrap = 1,
        WrapReverse = 2
    }

    // Real Unity enums used by IStyle.
    public enum Position
    {
        Relative = 0,
        Absolute = 1
    }

    public enum Justify
    {
        FlexStart = 0,
        Center = 1,
        FlexEnd = 2,
        SpaceBetween = 3,
        SpaceAround = 4
    }

    public enum Align
    {
        Auto = 0,
        FlexStart = 1,
        Center = 2,
        FlexEnd = 3,
        Stretch = 4
    }

    public enum DisplayStyle
    {
        None = 0,
        Flex = 1
    }

    public class StyleSheet : ScriptableObject
    {
    }

    public class StyleSheetCollection
    {
        public void Add(StyleSheet sheet) { }
    }

    public class IStyle
    {
        public StyleColor backgroundColor { get; set; }
        public StyleColor color { get; set; }
        public StyleFloat fontSize { get; set; }
        public StyleEnum<FontStyle> unityFontStyleAndWeight { get; set; }
        public StyleLength marginTop { get; set; }
        public StyleLength marginBottom { get; set; }
        public StyleLength marginLeft { get; set; }
        public StyleLength marginRight { get; set; }
        public StyleFloat borderLeftWidth { get; set; }
        public StyleFloat borderRightWidth { get; set; }
        public StyleFloat borderTopWidth { get; set; }
        public StyleFloat borderBottomWidth { get; set; }
        public StyleColor borderLeftColor { get; set; }
        public StyleColor borderRightColor { get; set; }
        public StyleColor borderTopColor { get; set; }
        public StyleColor borderBottomColor { get; set; }
        public StyleLength width { get; set; }
        public StyleLength minWidth { get; set; }
        public StyleLength maxWidth { get; set; }
        public StyleLength height { get; set; }
        public StyleLength maxHeight { get; set; }
        public StyleFloat flexGrow { get; set; }
        public StyleFloat flexShrink { get; set; }
        public StyleEnum<TextAnchor> unityTextAlign { get; set; }

        // Real Unity layout members (IStyle): positioning, padding, flex alignment, display.
        public StyleEnum<Position> position { get; set; }
        public StyleEnum<DisplayStyle> display { get; set; }
        public StyleLength left { get; set; }
        public StyleLength right { get; set; }
        public StyleLength top { get; set; }
        public StyleLength bottom { get; set; }
        public StyleLength paddingTop { get; set; }
        public StyleLength paddingBottom { get; set; }
        public StyleLength paddingLeft { get; set; }
        public StyleLength paddingRight { get; set; }
        public StyleEnum<Justify> justifyContent { get; set; }
        public StyleEnum<Align> alignItems { get; set; }
        public StyleEnum<WhiteSpace> whiteSpace { get; set; }
        public StyleEnum<FlexDirection> flexDirection { get; set; }
        public StyleEnum<Wrap> flexWrap { get; set; }
    }

    public class VisualElement
    {
        private readonly List<VisualElement> _children = new List<VisualElement>();
        private readonly IStyle _style = new IStyle();
        private readonly StyleSheetCollection _sheets = new StyleSheetCollection();

        public virtual VisualElement contentContainer { get { return this; } }
        public IStyle style { get { return _style; } }
        public StyleSheetCollection styleSheets { get { return _sheets; } }
        public string tooltip { get; set; }
        public string name { get; set; }

        public void Add(VisualElement child) { contentContainer._children.Add(child); }
        public void Clear() { contentContainer._children.Clear(); }
        public void AddToClassList(string className) { }
        public void RemoveFromClassList(string className) { }
        public void EnableInClassList(string className, bool enable) { }
        public bool ClassListContains(string className) { return false; }
        public void SetEnabled(bool enabled) { }

        // Real Unity: canonical full-window stretch (position:absolute + zero offsets).
        public void StretchToParentSize() { }

        // Real Unity: only EventBase-derived events can be registered for, so an IMGUI
        // Event (or anything else invented) cannot sneak into a UI Toolkit callback.
        public void RegisterCallback<TEventType>(EventCallback<TEventType> callback)
            where TEventType : EventBase
        {
        }
    }

    public class TextElement : VisualElement
    {
        public string text { get; set; }
    }

    public class Label : TextElement
    {
        public Label() { }
        public Label(string text) { this.text = text; }
    }

    public class Button : TextElement
    {
        public Button() { }

        public Button(Action click)
        {
        }
    }

    public class Foldout : VisualElement
    {
        private readonly VisualElement _body = new VisualElement();
        public override VisualElement contentContainer { get { return _body; } }
        public string text { get; set; }
        public bool value { get; set; }

        // Real Unity: Foldout implements INotifyValueChanged<bool>.
        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<bool>> callback)
        {
        }
    }

    // Real Unity: ChangeEvent<T> : EventBase with previousValue/newValue properties.
    public class ChangeEvent<T> : EventBase
    {
        public T previousValue { get; set; }
        public T newValue { get; set; }
    }

    public class TextField : TextElement
    {
        public TextField(string label = null) { this.label = label; }
        public string value { get; set; }
        public string label { get; set; }
        public bool isDelayed { get; set; }

        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<string>> callback)
        {
        }
    }

    public class Toggle : VisualElement
    {
        public Toggle() { }
        public Toggle(string label) { }
        public bool value { get; set; }
        public string label { get; set; }

        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<bool>> callback)
        {
        }
    }

    public class DropdownField : TextElement
    {
        public List<string> choices { get; set; }
        public string value { get; set; }
        public string label { get; set; }

        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<string>> callback)
        {
        }
    }

    public class PopupField<T> : VisualElement
    {
        public PopupField(string label, List<T> choices, int defaultIndex) { }
        public T value { get; set; }

        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<T>> callback) { }
    }

    public class FloatField : VisualElement
    {
        public FloatField(string label = null) { }
        public float value { get; set; }
        public bool isDelayed { get; set; }

        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<float>> callback) { }
    }

    public class IntegerField : VisualElement
    {
        public IntegerField(string label = null) { }
        public int value { get; set; }
        public bool isDelayed { get; set; }

        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<int>> callback) { }
    }

    public class Slider : VisualElement
    {
        public Slider(string label, float lowValue, float highValue) { }
        public float value { get; set; }

        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<float>> callback) { }
    }

    public enum ScrollViewMode
    {
        Vertical = 0,
        Horizontal = 1,
        VerticalAndHorizontal = 2,
        HorizontalAndVertical = 3
    }

    public class ScrollView : VisualElement
    {
        public ScrollView() { }
        public ScrollView(ScrollViewMode mode) { }
    }

    public class Image : VisualElement
    {
        public Texture image { get; set; }
        public ScaleMode scaleMode { get; set; }
    }
}

namespace UnityEditor
{
    using UnityEngine;

    public sealed class MenuItem : Attribute
    {
        public MenuItem(string itemName) { }
    }

    public enum MessageType
    {
        None = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    public enum DragAndDropVisualMode
    {
        None = 0,
        Copy = 1,
        Rejected = 16
    }

    public static class DragAndDrop
    {
        public static Object[] objectReferences { get { return Array.Empty<Object>(); } }
        public static DragAndDropVisualMode visualMode { get; set; }
        public static void AcceptDrag() { }
    }

    public static class SessionState
    {
        public static int GetInt(string key, int defaultValue) { return defaultValue; }
        public static void SetInt(string key, int value) { }
        public static string GetString(string key, string defaultValue) { return defaultValue; }
        public static void SetString(string key, string value) { }
    }

    public struct GlobalObjectId
    {
        public static GlobalObjectId GetGlobalObjectIdSlow(Object targetObject) { return new GlobalObjectId(); }
    }

    public class EditorWindow : UnityEngine.ScriptableObject
    {
        public static T GetWindow<T>() where T : EditorWindow { return null; }
        public GUIContent titleContent { get; set; }
        public Vector2 minSize { get; set; }
        public UnityEngine.UIElements.VisualElement rootVisualElement { get; } =
            new UnityEngine.UIElements.VisualElement();
        public void Show() { }
    }

    public static class EditorStyles
    {
        public static GUIStyle boldLabel { get { return new GUIStyle(); } }
        public static GUIStyle miniLabel { get { return new GUIStyle(); } }
        public static GUIStyle helpBox { get { return new GUIStyle(); } }
    }

    public static class EditorGUI
    {
        public static void BeginDisabledGroup(bool disabled) { }
        public static void EndDisabledGroup() { }
        public static void BeginChangeCheck() { }
        public static bool EndChangeCheck() { return false; }
    }

    public static class EditorGUILayout
    {
        public static void LabelField(string label) { }
        public static void LabelField(string label, params GUILayoutOption[] options) { }
        public static void LabelField(string label, GUIStyle style) { }
        public static void LabelField(string label, GUIStyle style, params GUILayoutOption[] options) { }
        public static void LabelField(string label, string label2) { }
        public static void LabelField(string label, string label2, params GUILayoutOption[] options) { }
        public static void LabelField(GUIContent label) { }
        public static void LabelField(GUIContent label, params GUILayoutOption[] options) { }
        public static void HelpBox(string message, MessageType type) { }
        public static bool Foldout(bool foldout, string content) { return foldout; }
        public static bool Toggle(GUIContent label, bool value) { return value; }
        public static bool Toggle(bool value, params GUILayoutOption[] options) { return value; }
        public static bool ToggleLeft(string label, bool value, params GUILayoutOption[] options) { return value; }
        public static string TextField(GUIContent label, string text) { return text; }
        public static int Popup(int selectedIndex, string[] displayedOptions) { return selectedIndex; }
        public static Enum EnumPopup(Enum selected) { return selected; }
        public static UnityEngine.Object ObjectField(GUIContent label, UnityEngine.Object obj, Type objType, bool allowSceneObjects)
        {
            return obj;
        }

        public static void BeginHorizontal() { }
        public static void EndHorizontal() { }
        public static Vector2 BeginScrollView(Vector2 scrollPosition, params GUILayoutOption[] options)
        {
            return scrollPosition;
        }

        public static void EndScrollView() { }
        public static void Space() { }
    }

    public static class EditorUtility
    {
        public static void DisplayProgressBar(string title, string info, float progress) { }
        public static bool DisplayCancelableProgressBar(string title, string info, float progress) { return false; }
        public static void ClearProgressBar() { }
        public static bool DisplayDialog(string title, string message, string ok, string cancel) { return true; }
        public static bool DisplayDialog(string title, string message, string ok) { return true; }
        public static void RevealInFinder(string path) { }
        public static void SetDirty(Object target) { }
        public static void CopySerialized(Object source, Object dest) { }
        public static string SaveFilePanel(string title, string directory, string defaultName, string extension)
        {
            return string.Empty;
        }
    }

    public static class EditorGUIUtility
    {
        public static string systemCopyBuffer { get; set; }
        public static bool isProSkin { get { return true; } }
        public static int PingObject(Object targetObject) { return 0; }
    }

    public static class Selection
    {
        public static Object activeObject { get; set; }
        public static GameObject activeGameObject { get; set; }
    }

    public static class AssetPreview
    {
        public static Texture2D GetMiniThumbnail(Object asset) { return null; }
    }

    public struct Hash128
    {
        public override string ToString() { return "stubhash"; }
    }

    public static class AssetDatabase
    {
        public static string GetAssetPath(Object asset) { return string.Empty; }
        public static bool CopyAsset(string from, string to) { return false; }
        public static bool DeleteAsset(string path) { return false; }
        public static string AssetPathToGUID(string path) { return string.Empty; }
        public static string GUIDToAssetPath(string guid) { return string.Empty; }
        public static string CreateFolder(string parentFolder, string newFolderName) { return string.Empty; }
        public static bool IsValidFolder(string path) { return false; }
        public static void ImportAsset(string path) { }
        public static bool OpenAsset(Object target) { return false; }
        public static void SaveAssets() { }
        public static void Refresh(ImportAssetOptions options = ImportAssetOptions.Default) { }
        public static Hash128 GetAssetDependencyHash(string path) { return new Hash128(); }
        public static string[] GetDependencies(string pathName, bool recursive) { return Array.Empty<string>(); }
        public static string[] FindAssets(string filter) { return Array.Empty<string>(); }
        public static T LoadAssetAtPath<T>(string assetPath) where T : UnityEngine.Object { return null; }
    }

    public class AssetImporter
    {
        public static AssetImporter GetAtPath(string path) { return null; }
    }

    public enum TextureImporterType
    {
        Default = 0,
        NormalMap = 1
    }

    public enum ImportAssetOptions
    {
        Default = 0
    }

    public enum TextureImporterAlphaSource
    {
        None = 0,
        FromInput = 1,
        FromGrayScale = 2
    }

    public enum TextureImporterFormat
    {
        Automatic = -1,
        DXT1 = 10,
        DXT5 = 12,
        BC4 = 26,
        BC5 = 27,
        BC6H = 24,
        BC7 = 25,
        R8 = 63,
        RG16 = 62,
        R16 = 9,
        RGB24 = 3,
        RGB16 = 7,
        RGBA32 = 4,
        ARGB32 = 5,
        RHalf = 15,
        RGHalf = 16,
        RGBAHalf = 17,
        RFloat = 18,
        RGFloat = 19,
        RGBAFloat = 20,
        ETC2_RGB4 = 45,
        ETC2_RGBA8 = 47
    }

    public enum TextureImporterShape
    {
        Texture2D = 1,
        TextureCube = 2,
        Texture2DArray = 3,
        Texture3D = 4
    }

    public enum TextureResizeAlgorithm
    {
        Mitchell = 0,
        Bilinear = 1
    }

    public enum BuildTarget
    {
        NoTarget = 0,
        StandaloneWindows = 5,
        Android = 13
    }

    public static class EditorUserBuildSettings
    {
        public static BuildTarget activeBuildTarget { get; set; }
    }

    public class TextureImporterSettings
    {
    }

    public enum TextureImporterCompression
    {
        Uncompressed = 0,
        Compressed = 1,
        CompressedHQ = 2,
        CompressedLQ = 3
    }

    public class TextureImporterPlatformSettings
    {
        public string name { get; set; }
        public bool overridden { get; set; }
        public TextureImporterFormat format { get; set; }
        public int maxTextureSize { get; set; }
        public TextureImporterCompression textureCompression { get; set; }
        public int compressionQuality { get; set; }
        public bool crunchedCompression { get; set; }
        public TextureResizeAlgorithm resizeAlgorithm { get; set; }
    }

    public class TextureImporter : AssetImporter
    {
        public int maxTextureSize { get; set; }
        public bool mipmapEnabled { get; set; }
        public bool isReadable { get; set; }
        public TextureImporterType textureType { get; set; }
        public TextureImporterShape textureShape { get; set; }
        public bool sRGBTexture { get; set; }
        public bool alphaIsTransparency { get; set; }
        public TextureImporterAlphaSource alphaSource { get; set; }
        public bool convertToNormalmap { get; set; }
        public float heightmapScale { get; set; }
        public FilterMode filterMode { get; set; }
        public int anisoLevel { get; set; }
        public TextureWrapMode wrapMode { get; set; }
        public TextureWrapMode wrapModeU { get; set; }
        public TextureWrapMode wrapModeV { get; set; }
        public TextureWrapMode wrapModeW { get; set; }
        public bool streamingMipmaps { get; set; }
        public bool borderMipmap { get; set; }

        public bool DoesSourceTextureHaveAlpha() { return false; }

        public void ReadTextureSettings(TextureImporterSettings dest) { }
        public void SetTextureSettings(TextureImporterSettings src) { }

        // Real signature: returns void, can throw while an import is in flight.
        public void GetSourceTextureWidthAndHeight(out int width, out int height)
        {
            width = 0;
            height = 0;
        }

        public TextureImporterPlatformSettings GetPlatformTextureSettings(string platform)
        {
            return new TextureImporterPlatformSettings();
        }

        public void SetPlatformTextureSettings(TextureImporterPlatformSettings platformSettings)
        {
        }

        public void SaveAndReimport()
        {
        }
    }

    public struct EditorCurveBinding
    {
        public string path;
        public string propertyName;
        public Type type;
    }

    public struct ObjectReferenceKeyframe
    {
        public UnityEngine.Object value;
    }

    public static class AnimationUtility
    {
        public static EditorCurveBinding[] GetObjectReferenceCurveBindings(AnimationClip clip)
        {
            return Array.Empty<EditorCurveBinding>();
        }

        public static ObjectReferenceKeyframe[] GetObjectReferenceCurve(AnimationClip clip, EditorCurveBinding binding)
        {
            return Array.Empty<ObjectReferenceKeyframe>();
        }

        // Real Unity: rewrites the curve in place; used on generated clones only.
        public static void SetObjectReferenceCurve(AnimationClip clip, EditorCurveBinding binding, ObjectReferenceKeyframe[] keyframes)
        {
        }

        public static EditorCurveBinding[] GetCurveBindings(AnimationClip clip)
        {
            return Array.Empty<EditorCurveBinding>();
        }
    }

    public static class Undo
    {
        public static void RecordObject(Object objectToUndo, string name) { }
        public static void IncrementCurrentGroup() { }
        public static int GetCurrentGroup() { return 0; }
        public static void CollapseUndoOperations(int groupIndex) { }
        public static void FlushUndoRecordObjects() { }
        public static void RevertAllDownToGroup(int group) { }
    }

    public static class PrefabUtility
    {
        public static void RecordPrefabInstancePropertyModifications(Object targetObject) { }
    }
}

namespace UnityEditor.UIElements
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;

    public class ObjectField : VisualElement
    {
        public string label { get; set; }
        public Type objectType { get; set; }
        public bool allowSceneObjects { get; set; }
        public UnityEngine.Object value { get; set; }

        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<UnityEngine.Object>> callback)
        {
        }
    }

    public class HelpBox : VisualElement
    {
        public HelpBox(string text, MessageType messageType) { }
    }
}

namespace UnityEditor.Animations
{
    using UnityEngine;

    public class AnimatorState : Object
    {
        public Motion motion { get; set; }
    }

    public struct ChildAnimatorState
    {
        public AnimatorState state;
    }

    public class BlendTree : Motion
    {
        public ChildMotion[] children { get; set; }
    }

    public struct ChildMotion
    {
        public Motion motion;
    }

    public class AnimatorStateMachine : Object
    {
        public ChildAnimatorState[] states { get; set; }
        public ChildAnimatorStateMachine[] stateMachines { get; set; }
    }

    public struct ChildAnimatorStateMachine
    {
        public AnimatorStateMachine stateMachine;
    }

    public class AnimatorControllerLayer
    {
        public string name { get; set; }
        public AnimatorStateMachine stateMachine { get; set; }
    }

    public class AnimatorController : RuntimeAnimatorController
    {
        public AnimatorControllerLayer[] layers { get; set; }
    }
}

namespace UnityEditor.PackageManager
{
    public sealed class PackageInfo
    {
        public string name { get; set; }

        public static PackageInfo[] GetAllRegisteredPackages()
        {
            return Array.Empty<PackageInfo>();
        }
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
    }
}

namespace UnityEditor.SceneManagement
{
    using UnityEngine.SceneManagement;

    public static class EditorSceneManager
    {
        public static void MarkSceneDirty(Scene scene) { }
    }
}

// NDMF (Non-Destructive Modular Framework) API stubs - signatures verified against
// https://github.com/bdunderscore/ndmf (package nadena.dev.ndmf, Unity 2022.3):
// ExportsPlugin, PluginBase/Plugin<T>, BuildPhase (class with static fields), and
// BuildContext.AvatarRootObject / AvatarRootTransform. The fluent Sequence lives in
// nadena.dev.ndmf.fluent. The real AvatarTuner.Editor.NDMF assembly compiles only when
// the NDMF package is installed (version define + define constraint); this harness compiles
// it unconditionally so the integration is type-checked offline.
namespace nadena.dev.ndmf
{
    using System;
    using UnityEngine;
    using nadena.dev.ndmf.fluent;

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class ExportsPlugin : Attribute
    {
        public Type PluginType { get; }
        public ExportsPlugin(Type pluginType) { PluginType = pluginType; }
    }

    // Real NDMF: BuildPhase is a sealed class with static readonly fields, not an enum.
    public sealed class BuildPhase
    {
        public string Name { get; }
        private BuildPhase(string name) { Name = name; }

        public static readonly BuildPhase FirstChance = new BuildPhase("FirstChance");
        public static readonly BuildPhase PlatformInit = new BuildPhase("PlatformInit");
        public static readonly BuildPhase Resolving = new BuildPhase("Resolving");
        public static readonly BuildPhase Generating = new BuildPhase("Generating");
        public static readonly BuildPhase Transforming = new BuildPhase("Transforming");
        public static readonly BuildPhase Optimizing = new BuildPhase("Optimizing");
        public static readonly BuildPhase PlatformFinish = new BuildPhase("PlatformFinish");
    }

    public abstract class PluginBase
    {
        public abstract string QualifiedName { get; }
        public abstract string DisplayName { get; }
    }

    public abstract class Plugin<T> : PluginBase where T : Plugin<T>, new()
    {
        protected abstract void Configure();
        protected Sequence InPhase(BuildPhase phase) { return new Sequence(); }
    }

    // Real NDMF: AvatarRootObject is the avatar CLONE being built, never the scene avatar.
    public class BuildContext
    {
        public GameObject AvatarRootObject { get; set; }
        public Transform AvatarRootTransform { get; set; }
    }
}

namespace nadena.dev.ndmf.fluent
{
    using System;

    // Real NDMF: weak plugin ordering (BeforePlugin/AfterPlugin by qualified name) and
    // Run(displayName, Action<BuildContext>) for anonymous passes.
    public sealed class Sequence
    {
        public Sequence BeforePlugin(string qualifiedName) { return this; }
        public Sequence AfterPlugin(string qualifiedName) { return this; }
        public Sequence Run(string displayName, Action<nadena.dev.ndmf.BuildContext> action) { return this; }
    }
}
