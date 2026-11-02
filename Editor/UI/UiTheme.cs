// AvatarTuner v1.0.0 — semantic color system.
//
// Colors are chosen per Editor theme (dark / light) and always carry a semantic role:
//   Accent  (blue/cyan)  primary actions, selected navigation, information
//   Success (green)      verified, optimized, up to date
//   Warning (amber)      review recommended, optimizable, stale
//   Error   (red)        failures, destructive actions, blockers
//   Neutral (gray)       secondary information, inactive controls
//
// Color never carries meaning alone: the window pairs every tone with a text label (and a
// shape chip), so the UI reads correctly for color-blind users and in both themes.

using UnityEditor;
using UnityEngine;

namespace AvatarTuner.Editor.UI
{
    public static class UiTheme
    {
        public static bool IsDark
        {
            get { return EditorGUIUtility.isProSkin; }
        }

        // ---- semantic roles -------------------------------------------------------

        public static Color Accent
        {
            get { return Rgb(IsDark ? 0x3E9BFF : 0x1F6FEB); }
        }

        public static Color Success
        {
            get { return Rgb(IsDark ? 0x4CC38A : 0x1A7F4B); }
        }

        public static Color Warning
        {
            get { return Rgb(IsDark ? 0xE5B567 : 0x9A6A00); }
        }

        public static Color Error
        {
            get { return Rgb(IsDark ? 0xE5646B : 0xB3261E); }
        }

        public static Color Info
        {
            get { return Rgb(IsDark ? 0x56B6C2 : 0x0E7490); }
        }

        public static Color Neutral
        {
            get { return Rgb(IsDark ? 0x9D9D9D : 0x6B6B6B); }
        }

        // ---- surfaces & text ------------------------------------------------------

        public static Color Text
        {
            get { return Rgb(IsDark ? 0xDCDCDC : 0x1F1F1F); }
        }

        public static Color TextSecondary
        {
            get { return Rgb(IsDark ? 0x9D9D9D : 0x5A5A5A); }
        }

        public static Color TextOnAccent
        {
            get { return Color.white; }
        }

        public static Color CardBackground
        {
            get { return Rgb(IsDark ? 0x353535 : 0xFFFFFF); }
        }

        public static Color CardBorder
        {
            get { return Rgb(IsDark ? 0x232323 : 0xD6D6D6); }
        }

        public static Color CardHeaderBackground
        {
            get { return Rgb(IsDark ? 0x2E2E2E : 0xF0F0F0); }
        }

        public static Color SelectedBackground
        {
            get { return Rgb(IsDark ? 0x2F4E73 : 0xDCE9FB); }
        }

        public static Color ChipBackground
        {
            get { return Rgb(IsDark ? 0x2A2A2A : 0xEDEDED); }
        }

        public static Color RowHoverBackground
        {
            get { return Rgb(IsDark ? 0x3D3D3D : 0xF5F5F5); }
        }

        // ---- composites -----------------------------------------------------------

        /// <summary>Semantic tone to its display color.</summary>
        public static Color ForTone(ViewModels.SemanticTone tone)
        {
            switch (tone)
            {
                case ViewModels.SemanticTone.Info: return Info;
                case ViewModels.SemanticTone.Success: return Success;
                case ViewModels.SemanticTone.Warning: return Warning;
                case ViewModels.SemanticTone.Error: return Error;
                default: return Neutral;
            }
        }

        /// <summary>Compatibility / quality risk level to its display color.</summary>
        public static Color ForRisk(Core.CompatRisk risk)
        {
            switch (risk)
            {
                case Core.CompatRisk.High: return Error;
                case Core.CompatRisk.Medium: return Warning;
                default: return Success;
            }
        }

        private static Color Rgb(int hex)
        {
            return new Color(
                ((hex >> 16) & 0xFF) / 255.0f,
                ((hex >> 8) & 0xFF) / 255.0f,
                (hex & 0xFF) / 255.0f,
                1.0f);
        }
    }
}
