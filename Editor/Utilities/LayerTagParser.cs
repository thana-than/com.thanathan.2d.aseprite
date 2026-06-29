using System;
using System.Text.RegularExpressions;

namespace UnityEditor.U2D.Aseprite
{
    /// <summary>
    /// A layer-name tag that controls import behaviour.
    /// Each tag carries its own recognised tokens — define a new tag here and it works everywhere.
    /// Tokens are matched case-insensitively and must end on a word boundary.
    /// </summary>
    internal sealed class LayerTag
    {
        /// <summary>#EXPAND or #EXP — expand a group into separate child layers instead of merging them.</summary>
        public static readonly LayerTag Expand = new("#expand", "#exp");

        internal readonly string[] Tokens;

        LayerTag(params string[] tokens) => Tokens = tokens;
    }

    /// <summary>
    /// Checks for and strips <see cref="LayerTag"/> instances embedded in Aseprite layer names.
    /// </summary>
    internal static class LayerTagParser
    {
        /// <summary>Returns true if <paramref name="layerName"/> contains the given tag.</summary>
        public static bool HasTag(string layerName, LayerTag tag)
        {
            if (string.IsNullOrEmpty(layerName))
                return false;

            var lower = layerName.ToLowerInvariant();
            foreach (var token in tag.Tokens)
            {
                var idx = lower.IndexOf(token, StringComparison.Ordinal);
                if (idx < 0)
                    continue;

                // Token must end at a word boundary (end of string or non-alphanumeric character).
                var after = idx + token.Length;
                if (after >= lower.Length || !char.IsLetterOrDigit(lower[after]))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns a copy of <paramref name="layerName"/> with all occurrences of the given tag removed
        /// and surrounding whitespace trimmed.
        /// </summary>
        public static string StripTag(string layerName, LayerTag tag)
        {
            if (string.IsNullOrEmpty(layerName))
                return layerName;

            var result = layerName;
            foreach (var token in tag.Tokens)
                result = Regex.Replace(result, Regex.Escape(token) + @"(?![a-zA-Z0-9])", string.Empty, RegexOptions.IgnoreCase);

            return result.Trim();
        }
    }
}
