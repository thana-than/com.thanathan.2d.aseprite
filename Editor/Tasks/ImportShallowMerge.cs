using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace UnityEditor.U2D.Aseprite
{
    internal static class ImportShallowMerge
    {
        /// <summary>
        /// Flattens the layer list in place.
        /// - Top-level normal layers pass through unchanged.
        /// - Top-level group layers without #EXPAND/#EXP are merged into a single sprite per frame.
        /// - Top-level group layers tagged #EXPAND/#EXP are expanded: their direct children are
        ///   processed recursively with the same rules, and the group itself becomes an empty
        ///   container layer (LayerTypes.Group, no cells) in the output.
        /// </summary>
        public static void Import(List<Layer> layers, out List<NativeArray<Color32>> cellBuffers, out List<int2> cellSizes)
        {
            cellBuffers = new List<NativeArray<Color32>>();
            cellSizes = new List<int2>();
            var outputLayers = new List<Layer>();

            foreach (var layer in layers.Where(l => l.parentIndex == -1))
            {
                if (layer.layerType == LayerTypes.Group)
                {
                    if (LayerTagParser.HasTag(layer.name, LayerTag.Expand))
                        ExpandGroupLayer(layer, layers, -1, outputLayers, cellBuffers, cellSizes);
                    else
                        MergeGroupLayer(layer, layers, -1, outputLayers, cellBuffers, cellSizes);
                }
                else if (layer.layerType == LayerTypes.Normal)
                    CollectNormalLayer(layer, -1, outputLayers, cellBuffers, cellSizes);
            }

            layers.Clear();
            layers.AddRange(outputLayers);
        }

        /// <summary>
        /// Expands an #EXPAND-tagged group: emits an empty Group layer as a hierarchy container,
        /// then processes each direct child with the same merge/expand/collect rules.
        /// </summary>
        static void ExpandGroupLayer(Layer group, List<Layer> allLayers, int parentOutputIndex,
            List<Layer> outputLayers, List<NativeArray<Color32>> cellBuffers, List<int2> cellSizes)
        {
            var groupLayer = new Layer
            {
                layerType = LayerTypes.Group,
                index = outputLayers.Count,
                name = LayerTagParser.StripTag(group.name, LayerTag.Expand),
                parentIndex = parentOutputIndex,
                uuid = group.uuid
            };
            outputLayers.Add(groupLayer);
            // No cell buffers added — group layer carries no sprite.

            var groupOutputIndex = groupLayer.index;
            foreach (var child in GetDirectChildren(group.index, allLayers))
            {
                if (child.layerType == LayerTypes.Normal)
                    CollectNormalLayer(child, groupOutputIndex, outputLayers, cellBuffers, cellSizes);
                else if (child.layerType == LayerTypes.Group)
                {
                    if (LayerTagParser.HasTag(child.name, LayerTag.Expand))
                        ExpandGroupLayer(child, allLayers, groupOutputIndex, outputLayers, cellBuffers, cellSizes);
                    else
                        MergeGroupLayer(child, allLayers, groupOutputIndex, outputLayers, cellBuffers, cellSizes);
                }
            }
        }

        /// <summary>
        /// Merges all descendant normal layers into a single output layer, preserving shared linked-cell relationships.
        /// </summary>
        static void MergeGroupLayer(Layer group, List<Layer> allLayers, int parentOutputIndex,
            List<Layer> outputLayers, List<NativeArray<Color32>> cellBuffers, List<int2> cellSizes)
        {
            var descendants = GetDescendantNormalLayers(group.index, allLayers);
            if (descendants.Count == 0) return;

            var allCellsPerFrame = CellTasks.GetAllCellsPerFrame(descendants);
            if (allCellsPerFrame.Count == 0) return;

            var linkMap = ComputeGroupLinkMap(descendants);
            var framesToMerge = linkMap != null ? FilterLinkedFrames(allCellsPerFrame, linkMap) : allCellsPerFrame;

            var mergedCells = CellTasks.MergeCells(framesToMerge, group.name);
            CellTasks.CollectDataFromCells(mergedCells, out var buffers, out var sizes);
            cellBuffers.AddRange(buffers);
            cellSizes.AddRange(sizes);

            var mergedLayer = new Layer
            {
                layerType = LayerTypes.Normal,
                cells = mergedCells,
                index = outputLayers.Count,
                name = group.name,
                parentIndex = parentOutputIndex,
                uuid = group.uuid
            };

            if (linkMap != null)
            {
                foreach (var (linkedFrame, sourceFrame) in linkMap)
                    mergedLayer.linkedCells.Add(new LinkedCell { frameIndex = linkedFrame, linkedToFrame = sourceFrame });
            }

            outputLayers.Add(mergedLayer);
        }

        /// <summary>
        /// Passes a normal layer through unchanged, collecting its cell buffers.
        /// </summary>
        static void CollectNormalLayer(Layer layer, int parentOutputIndex,
            List<Layer> outputLayers, List<NativeArray<Color32>> cellBuffers, List<int2> cellSizes)
        {
            CellTasks.GetCellsFromLayers(new List<Layer> { layer }, out var cells);
            CellTasks.CollectDataFromCells(cells, out var buffers, out var sizes);
            CellTasks.FlipCellBuffers(buffers, sizes);
            cellBuffers.AddRange(buffers);
            cellSizes.AddRange(sizes);

            layer.parentIndex = parentOutputIndex;
            layer.index = outputLayers.Count;
            outputLayers.Add(layer);
        }

        /// <summary>
        /// Builds a map of linkedFrames for frames that produce identical merged output.
        /// Includes linking group frames when descendants share identical links.
        /// </summary>
        static Dictionary<int, int> ComputeGroupLinkMap(List<Layer> descendants)
        {
            var cellFrames = descendants.Select(l => l.cells.Select(c => c.frameIndex).ToHashSet()).ToList();
            var linkTargets = descendants.Select(l =>
                l.linkedCells.ToDictionary(lc => lc.frameIndex, lc => lc.linkedToFrame)).ToList();

            var allFrames = cellFrames.SelectMany(x => x).Concat(linkTargets.SelectMany(d => d.Keys)).ToHashSet();
            var sourceFrames = allFrames.Where(f => cellFrames.Any(cf => cf.Contains(f))).ToHashSet();

            string CellFingerprint(int frame) => string.Join(",",
                Enumerable.Range(0, descendants.Count).Select(i => cellFrames[i].Contains(frame) ? frame : -1));

            string LinkFingerprint(int frame) => string.Join(",",
                Enumerable.Range(0, descendants.Count).Select(i =>
                    linkTargets[i].TryGetValue(frame, out var t) ? t : -1));

            var sourceByFingerprint = sourceFrames.ToDictionary(CellFingerprint);
            var canonicals = new Dictionary<string, int>();
            var linkMap = new Dictionary<int, int>();

            foreach (var frame in allFrames.Except(sourceFrames).OrderBy(f => f))
            {
                var fp = LinkFingerprint(frame);
                if (sourceByFingerprint.TryGetValue(fp, out var src))
                    linkMap[frame] = src;
                else if (canonicals.TryGetValue(fp, out var canon))
                    linkMap[frame] = canon;
                else
                    canonicals[fp] = frame;
            }

            return linkMap.Count > 0 ? linkMap : null;
        }

        /// <summary>
        /// Returns only the frames that aren't already covered by a link in linkMap.
        /// </summary>
        static Dictionary<int, List<Cell>> FilterLinkedFrames(Dictionary<int, List<Cell>> cellsPerFrame, Dictionary<int, int> linkMap)
        {
            var result = new Dictionary<int, List<Cell>>();
            foreach (var kv in cellsPerFrame)
            {
                if (!linkMap.ContainsKey(kv.Key))
                    result[kv.Key] = kv.Value;
            }
            return result;
        }

        /// <summary>
        /// Returns the immediate children of <paramref name="parentIndex"/> in <paramref name="allLayers"/>.
        /// </summary>
        static List<Layer> GetDirectChildren(int parentIndex, List<Layer> allLayers)
        {
            var result = new List<Layer>();
            foreach (var layer in allLayers)
            {
                if (layer.parentIndex == parentIndex)
                    result.Add(layer);
            }
            return result;
        }

        /// <summary>
        /// Recursively collects all normal layers under the given parent, skipping nested group layers.
        /// </summary>
        static List<Layer> GetDescendantNormalLayers(int parentIndex, List<Layer> allLayers)
        {
            var result = new List<Layer>();
            foreach (var layer in allLayers)
            {
                if (layer.parentIndex != parentIndex)
                    continue;
                if (layer.layerType == LayerTypes.Normal)
                    result.Add(layer);
                else if (layer.layerType == LayerTypes.Group)
                    result.AddRange(GetDescendantNormalLayers(layer.index, allLayers));
            }
            return result;
        }
    }
}
