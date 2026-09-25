using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor.U2D.Common;
using UnityEngine;

namespace UnityEditor.U2D.Aseprite
{
    internal sealed class SecondaryMapLayers : IDisposable
    {
        readonly Dictionary<string, SecondaryMap> m_Maps = new();
        readonly Dictionary<int, Layer> m_LayersByIndex;
        readonly Dictionary<int, Dictionary<string, List<Layer>>> m_MapLayersByTarget = new();
        readonly Dictionary<UUID, Dictionary<string, List<Layer>>> m_MapLayersBySource = new();
        readonly List<Layer> m_ExtractedLayers = new();
        Dictionary<string, List<Layer>> m_MapLayersForAll = new();

        public IEnumerable<SecondaryMap> maps => m_Maps.Values;
        public bool isEmpty => m_Maps.Count == 0;

        SecondaryMapLayers(List<Layer> layers) => m_LayersByIndex = layers.ToDictionary(l => l.index);

        public static SecondaryMapLayers Extract(List<Layer> layers)
        {
            var result = new SecondaryMapLayers(layers);

            var roots = new Dictionary<Layer, List<SecondaryMap>>();
            foreach (var layer in layers)
            {
                var layerMaps = SecondaryMap.Parse(layer.name).ToList();
                if (layerMaps.Count > 0)
                    roots.Add(layer, layerMaps);
            }

            var extracted = new HashSet<int>();
            foreach (var (root, rootMaps) in roots)
            {
                if (result.Ancestors(root).Skip(1).Any(roots.ContainsKey))
                    continue;

                var subtree = result.SelfAndDescendants(root, layers).ToList();
                foreach (var layer in subtree)
                    extracted.Add(layer.index);

                var baseName = BaseName(root.name);
                var target = layers.Find(l => l.parentIndex == root.parentIndex && !roots.ContainsKey(l) && BaseName(l.name) == baseName);
                if (target == null)
                {
                    Debug.LogWarning($"Secondary map layer \"{root.name}\" has no sibling layer named \"{baseName}\" to pair with.");
                    continue;
                }

                var cellLayers = subtree.Where(l => l.layerType == LayerTypes.Normal).ToList();
                if (!result.m_MapLayersByTarget.TryGetValue(target.index, out var byMap))
                    result.m_MapLayersByTarget[target.index] = byMap = new Dictionary<string, List<Layer>>();

                foreach (var map in rootMaps)
                {
                    result.m_Maps.TryAdd(map.propertyName, map);
                    if (!byMap.TryGetValue(map.propertyName, out var list))
                        byMap[map.propertyName] = list = new List<Layer>();
                    list.AddRange(cellLayers);
                }
            }

            result.m_ExtractedLayers.AddRange(layers.Where(l => extracted.Contains(l.index)));
            layers.RemoveAll(l => extracted.Contains(l.index));
            return result;
        }

        public void Resolve(IReadOnlyList<Layer> colorLayers)
        {
            foreach (var layer in colorLayers)
            {
                var sources = layer.layerType == LayerTypes.Group
                    ? DescendantNormalLayers(layer, colorLayers)
                    : layer.layerType == LayerTypes.Normal ? new[] { layer } : Array.Empty<Layer>();
                m_MapLayersBySource[layer.uuid] = ResolveSources(sources);
            }

            m_MapLayersForAll = ResolveSources(colorLayers.Where(l => l.layerType == LayerTypes.Normal));
        }

        public NativeArray<Color32> Pack(SecondaryMap map, IReadOnlyList<Layer> outputLayers, IReadOnlyList<int2> imageSizes,
            RectInt[] spriteRects, Vector2Int[] uvTransforms, int spritePadding, int mosaicPadding, int packedWidth, int packedHeight)
        {
            var count = imageSizes.Count;
            var buffers = new NativeArray<Color32>[count];
            var widths = new int[count];
            var tightRects = new RectInt[count];

            var bufferIndex = 0;
            foreach (var layer in outputLayers)
            {
                if (!m_MapLayersBySource.TryGetValue(layer.uuid, out var byMap))
                    byMap = m_MapLayersForAll;

                var cellsPerFrame = byMap.TryGetValue(map.propertyName, out var mapLayers)
                    ? CellTasks.GetAllCellsPerFrame(mapLayers)
                    : null;

                foreach (var cell in layer.cells)
                {
                    if (bufferIndex >= count)
                        break;
                    List<Cell> mapCells = null;
                    cellsPerFrame?.TryGetValue(cell.frameIndex, out mapCells);
                    buffers[bufferIndex++] = Composite(map.fill, cell.cellRect, mapCells);
                }
            }

            for (; bufferIndex < count; ++bufferIndex)
                buffers[bufferIndex] = Composite(map.fill, new RectInt(0, 0, imageSizes[bufferIndex].x, imageSizes[bufferIndex].y), null);

            for (var i = 0; i < count; ++i)
            {
                widths[i] = imageSizes[i].x;
                tightRects[i] = new RectInt(
                    spriteRects[i].position - uvTransforms[i],
                    spriteRects[i].size - new Vector2Int(spritePadding, spritePadding));
            }

            var packed = new NativeArray<Color32>(packedWidth * packedHeight, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            Fill(packed, map.fill);
            ImagePacker.Blit(packed, spriteRects, packedWidth, buffers, tightRects, widths, mosaicPadding);

            foreach (var buffer in buffers)
                buffer.DisposeIfCreated();

            return packed;
        }

        public void Dispose()
        {
            foreach (var layer in m_ExtractedLayers)
            {
                foreach (var cell in layer.cells)
                    cell.image.DisposeIfCreated();
            }
        }

        Dictionary<string, List<Layer>> ResolveSources(IEnumerable<Layer> sources)
        {
            var resolved = new Dictionary<string, HashSet<Layer>>();
            foreach (var source in sources)
            {
                foreach (var ancestor in Ancestors(source))
                {
                    if (!m_MapLayersByTarget.TryGetValue(ancestor.index, out var byMap))
                        continue;

                    foreach (var (propertyName, mapLayers) in byMap)
                    {
                        if (!resolved.TryGetValue(propertyName, out var set))
                            resolved[propertyName] = set = new HashSet<Layer>();
                        set.UnionWith(mapLayers);
                    }
                }
            }

            return resolved.ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(l => l.index).ToList());
        }

        IEnumerable<Layer> Ancestors(Layer layer)
        {
            for (var current = layer; current != null; current = m_LayersByIndex.GetValueOrDefault(current.parentIndex))
                yield return current;
        }

        IEnumerable<Layer> SelfAndDescendants(Layer layer, IReadOnlyList<Layer> layers)
        {
            yield return layer;
            foreach (var child in layers.Where(l => l.parentIndex == layer.index))
            {
                foreach (var descendant in SelfAndDescendants(child, layers))
                    yield return descendant;
            }
        }

        IEnumerable<Layer> DescendantNormalLayers(Layer layer, IReadOnlyList<Layer> layers) =>
            SelfAndDescendants(layer, layers).Skip(1).Where(l => l.layerType == LayerTypes.Normal);

        static string BaseName(string layerName) =>
            LayerTagParser.StripTag(SecondaryMap.StripTags(layerName), LayerTag.Expand);

        static unsafe NativeArray<Color32> Composite(Color32 fill, RectInt rect, List<Cell> cells)
        {
            var output = new NativeArray<Color32>(rect.width * rect.height, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            Fill(output, fill);
            if (cells == null || cells.Count == 0)
                return output;

            var textures = new NativeArray<IntPtr>(cells.Count, Allocator.Temp);
            var cellRects = new NativeArray<RectInt>(cells.Count, Allocator.Temp);
            var blendModes = new NativeArray<BlendModes>(cells.Count, Allocator.Temp);
            for (var i = 0; i < cells.Count; ++i)
            {
                textures[i] = (IntPtr)cells[i].image.GetUnsafeReadOnlyPtr();
                cellRects[i] = cells[i].cellRect;
                blendModes[i] = cells[i].blendMode;
            }

            TextureTasks.BlendTextures(in textures, in cellRects, in blendModes, in rect, ref output);

            textures.Dispose();
            cellRects.Dispose();
            blendModes.Dispose();
            return output;
        }

        static unsafe void Fill(NativeArray<Color32> buffer, Color32 color)
        {
            if (buffer.Length == 0)
                return;
            UnsafeUtility.MemCpyReplicate(buffer.GetUnsafePtr(), &color, sizeof(Color32), buffer.Length);
        }
    }
}
