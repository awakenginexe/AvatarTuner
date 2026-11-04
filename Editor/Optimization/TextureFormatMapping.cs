// AvatarTuner v1.0.0 — tuner format to Unity importer format mapping.
//
// The mapping is a plain name table so it can be unit tested without Unity. The editor
// glue resolves the names to UnityEngine.TextureImporterFormat values; a format with no
// importer equivalent maps to null and is never silently downgraded to something else.

using System;
using System.Collections.Generic;
using AvatarTuner.Core;

namespace AvatarTuner.Editor.Optimization
{
    public static class TextureFormatMapping
    {
        private static readonly Dictionary<TunerTextureFormat, string> ImporterFormatNames =
            new Dictionary<TunerTextureFormat, string>
            {
                { TunerTextureFormat.BC1, "DXT1" },
                { TunerTextureFormat.BC3, "DXT5" },
                { TunerTextureFormat.BC4, "BC4" },
                { TunerTextureFormat.BC5, "BC5" },
                { TunerTextureFormat.BC6H, "BC6H" },
                { TunerTextureFormat.BC7, "BC7" },
                { TunerTextureFormat.R8, "R8" },
                { TunerTextureFormat.RG16, "RG16" },
                { TunerTextureFormat.R16, "R16" },
                { TunerTextureFormat.RGB24, "RGB24" },
                { TunerTextureFormat.RGB16, "RGB16" },
                { TunerTextureFormat.RGBA32, "RGBA32" },
                { TunerTextureFormat.ARGB32, "ARGB32" },
                { TunerTextureFormat.RHalf, "RHalf" },
                { TunerTextureFormat.RGHalf, "RGHalf" },
                { TunerTextureFormat.RGBAHalf, "RGBAHalf" },
                { TunerTextureFormat.RFloat, "RFloat" },
                { TunerTextureFormat.RGFloat, "RGFloat" },
                { TunerTextureFormat.RGBAFloat, "RGBAFloat" },
                { TunerTextureFormat.ETC2_RGB4, "ETC2_RGB4" },
                { TunerTextureFormat.ETC2_RGBA8, "ETC2_RGBA8" },
                { TunerTextureFormat.ASTC_4x4, "ASTC_4x4" },
                { TunerTextureFormat.ASTC_6x6, "ASTC_6x6" },
                { TunerTextureFormat.ASTC_8x8, "ASTC_8x8" }
            };

        /// <summary>
        /// The <c>TextureImporterFormat</c> member name a tuner format materializes as, or
        /// null when Unity cannot express it. Unmapped formats are skipped with an
        /// explanation, never approximated.
        /// </summary>
        public static string ImporterFormatName(TunerTextureFormat format)
        {
            string name;
            return ImporterFormatNames.TryGetValue(format, out name) ? name : null;
        }

        /// <summary>True when the format can be written to a generated texture asset.</summary>
        public static bool IsMaterializable(TunerTextureFormat format)
        {
            return ImporterFormatName(format) != null;
        }

        /// <summary>Every format the mapping can materialize, in enum order.</summary>
        public static List<TunerTextureFormat> MaterializableFormats()
        {
            var formats = new List<TunerTextureFormat>();
            foreach (TunerTextureFormat format in Enum.GetValues(typeof(TunerTextureFormat)))
            {
                if (IsMaterializable(format))
                {
                    formats.Add(format);
                }
            }

            return formats;
        }
    }
}
