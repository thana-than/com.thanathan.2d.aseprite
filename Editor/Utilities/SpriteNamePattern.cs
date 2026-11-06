using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace UnityEditor.U2D.Aseprite
{
    internal static class SpriteNamePattern
    {
        const string k_FileToken = "{file}";
        const string k_GroupsToken = "{groups}";
        const string k_LayerToken = "{layer}";
        const string k_GroupSeparator = "_";

        public static void Apply(string pattern, string fileName, bool isMerged, List<Layer> layers)
        {
            foreach (var layer in layers)
            {
                // A merged import has one flattened layer, which has no groups or name of its own.
                var groups = isMerged ? string.Empty : GroupPath(layer, layers);
                var layerName = isMerged ? string.Empty : layer.name;
                var baseName = Format(pattern, fileName, groups, layerName);

                var cells = layer.cells;
                for (var i = 0; i < cells.Count; ++i)
                {
                    var cell = cells[i];
                    cell.name = ImportUtilities.GetCellName(baseName, cell.frameIndex, cells.Count, false);
                    cells[i] = cell;
                }
            }
        }

        static string Format(string pattern, string fileName, string groups, string layerName)
        {
            var name = pattern.Replace(k_FileToken, fileName);
            name = ReplaceOptionalToken(name, k_GroupsToken, groups);
            name = ReplaceOptionalToken(name, k_LayerToken, layerName);
            return string.IsNullOrEmpty(name) ? fileName : name;
        }

        // An empty token takes one neighbouring separator with it, so that "{file}_{groups}_{layer}" has no "__" without groups.
        static string ReplaceOptionalToken(string pattern, string token, string value)
        {
            if (!string.IsNullOrEmpty(value))
                return pattern.Replace(token, value);

            var escapedToken = Regex.Escape(token);
            return Regex.Replace(pattern, $@"{escapedToken}[^\w{{]?|[^\w}}]{escapedToken}$", string.Empty);
        }

        static string GroupPath(Layer layer, List<Layer> layers)
        {
            var groups = new List<string>();
            var parent = layers.Find(x => x.index == layer.parentIndex);
            while (parent != null)
            {
                groups.Insert(0, parent.name);
                var parentIndex = parent.parentIndex;
                parent = layers.Find(x => x.index == parentIndex);
            }

            return string.Join(k_GroupSeparator, groups);
        }
    }
}
