using System;
using System.Text.RegularExpressions;

namespace UnityEditor.U2D.Aseprite
{
    /// <summary>
    /// Tags that can be embedded in Aseprite layer names to control import behavior.
    /// </summary>
    internal enum LayerTag
    {
        /// <summary>
        /// #EXPAND or #EXP — expand a group into separate child layers instead of merging them.
        /// </summary>
        Expand
    }

    /// <summary>
    /// Parses and strips layer-name tags (e.g. "#EXPAND", "#EXP") from Aseprite layer names.
    /// Tags are case-insensitive and may appear anywhere in the name.
    /// </summary>
    internal static class LayerTagParser
    {
        static readonly string[] k_ExpandTokens = { "#expand", "#exp" };

        /// <summary>Returns true if <paramref name="layerName"/> contains the given tag.</summary>
        public static bool HasTag(string layerName, LayerTag tag)
        {
            if (string.IsNullOrEmpty(layerName))
                return false;

            var tokens = TokensForTag(tag);
            var lower = layerName.ToLowerInvariant();
            foreach (var token in tokens)
            {
                var idx = lower.IndexOf(token, StringComparison.Ordinal);
                if (idx < 0)
                    continue;

                // Require the token to end at a word boundary (end of string or non-letter/digit)
                var afterIdx = idx + token.Length;
                if (afterIdx >= lower.Length || !char.IsLetterOrDigit(lower[afterIdx]))
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
            foreach (var token in TokensForTag(tag))
            {
                // Case-insensitive replace, token must end at a non-alphanumeric boundary
                result = Regex.Replace(result, Regex.Escape(token) + @"(?![a-zA-Z0-9])", string.Empty,
                    RegexOptions.IgnoreCase);
            }

            return result.Trim();
        }

        static string[] TokensForTag(LayerTag tag) => tag switch
        {
            LayerTag.Expand => k_ExpandTokens,
            _ => Array.Empty<string>()
        };
    }
}
