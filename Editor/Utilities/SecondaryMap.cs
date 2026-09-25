using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UnityEditor.U2D.Aseprite
{
    internal sealed class SecondaryMap
    {
        public static readonly SecondaryMap Normal = new("_NormalMap", new Color32(128, 128, 255, 255), TextureImporterType.NormalMap, false, new LayerTag("#normal", "#nrm"));
        public static readonly SecondaryMap Mask = new("_MaskTex", new Color32(0, 0, 0, 0), TextureImporterType.Default, false, new LayerTag("#mask"));

        static readonly Regex CustomToken = new(@"#map:(\w+)", RegexOptions.IgnoreCase);

        static readonly SecondaryMap[] BuiltIn = typeof(SecondaryMap)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(SecondaryMap))
            .Select(f => (SecondaryMap)f.GetValue(null))
            .ToArray();

        public readonly string propertyName;
        public readonly Color32 fill;
        public readonly TextureImporterType textureType;
        public readonly bool sRGB;
        readonly LayerTag m_Tag;

        SecondaryMap(string propertyName, Color32 fill, TextureImporterType textureType, bool sRGB, LayerTag tag = null)
        {
            this.propertyName = propertyName;
            this.fill = fill;
            this.textureType = textureType;
            this.sRGB = sRGB;
            m_Tag = tag;
        }

        public static IEnumerable<SecondaryMap> Parse(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
                yield break;

            foreach (var map in BuiltIn)
            {
                if (LayerTagParser.HasTag(layerName, map.m_Tag))
                    yield return map;
            }

            foreach (Match match in CustomToken.Matches(layerName))
                yield return new SecondaryMap(match.Groups[1].Value, new Color32(0, 0, 0, 0), TextureImporterType.Default, true);
        }

        public static string StripTags(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
                return layerName;

            var result = CustomToken.Replace(layerName, string.Empty);
            foreach (var map in BuiltIn)
                result = LayerTagParser.StripTag(result, map.m_Tag);
            return result.Trim();
        }
    }
}
