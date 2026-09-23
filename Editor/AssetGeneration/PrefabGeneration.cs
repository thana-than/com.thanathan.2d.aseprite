using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace UnityEditor.U2D.Aseprite
{
    internal static class PrefabGeneration
    {
        public static void Generate(
            AssetImportContext ctx,
            TextureGenerationOutput output,
            List<Layer> layers,
            Dictionary<int, GameObject> layerIdToGameObject,
            Vector2Int canvasSize,
            AsepriteImporterSettings importSettings,
            IReadOnlyList<Tag> tags,
            ref UnityEngine.Object mainAsset,
            out GameObject rootGameObject)
        {
            rootGameObject = importSettings.generateAnimationImageTarget
                ? new GameObject("Root", typeof(RectTransform))
                : new GameObject("Root");
            if (!importSettings.generateAnimationImageTarget)
            {
#if ENABLE_URP
                if (importSettings.addShadowCasters && layers.Count > 1)
                    rootGameObject.AddComponent<UnityEngine.Rendering.Universal.CompositeShadowCaster2D>();
#endif
                if (importSettings.addSortingGroup && layers.Count > 1)
                    rootGameObject.AddComponent<SortingGroup>();
            }

            // Group layers from #EXPAND are preserved in the hierarchy only when explicitly requested
            // (or when not in ShallowMerge mode, where group layers are always part of the output).
            var preserveGroups = importSettings.layerImportMode != LayerImportModes.ShallowMerge
                || importSettings.preserveGroupHierarchy;

            if (layers.Count == 1)
            {
                layerIdToGameObject.Add(layers[0].index, rootGameObject);
            }
            else
                CreateLayerHierarchy(layers, layerIdToGameObject, rootGameObject, importSettings.generateAnimationImageTarget, preserveGroups);

            // UI Image renders in sibling order (higher index = in front), opposite of SpriteRenderer sorting.
            // Iterate forward for UI so layer[0] (Aseprite bottom) gets sibling index 0 (renders behind).
            int start = importSettings.generateAnimationImageTarget ? 0 : layers.Count - 1;
            int end = importSettings.generateAnimationImageTarget ? layers.Count : -1;
            int step = importSettings.generateAnimationImageTarget ? 1 : -1;
            var firstTag = tags != null && tags.Count > 0 ? tags[0] : null;
            var pixelsPerUnit = output.sprites is { Length: > 0 } ? output.sprites[0].pixelsPerUnit : 100f;
            var groupAnchors = CalculateGroupAnchors(layers, layerIdToGameObject, importSettings);

            for (var i = start; i != end; i += step)
            {
                var layer = layers[i];

                if (!preserveGroups && layer.layerType == LayerTypes.Group)
                    continue;

                SetupLayerGameObject(layer, layerIdToGameObject, output.sprites, importSettings, canvasSize, groupAnchors, pixelsPerUnit, firstTag);
            }

            for (var i = start; i != end; i += step)
            {
                var layer = layers[i];

                if (!preserveGroups && layer.layerType == LayerTypes.Group)
                    continue;

                if (layer.parentIndex == -1)
                    continue;

                // Use TryGetValue so that group layers skipped above don't cause a KeyNotFoundException;
                // the child simply stays under Root when its group parent has no GameObject.
                if (!layerIdToGameObject.TryGetValue(layer.parentIndex, out var parentGo))
                    continue;

                layerIdToGameObject[layer.index].transform.parent = parentGo.transform;
            }

            // We need the GameObjects in order to generate Animation Clips.
            // But we will only save down the GameObjects if it is requested.
            if (importSettings.generateModelPrefab)
            {
                ctx.AddObjectToAsset(rootGameObject.name, rootGameObject);
                mainAsset = rootGameObject;
            }
            else
                rootGameObject.hideFlags = HideFlags.HideAndDontSave;
        }

        static void CreateLayerHierarchy(List<Layer> layers, Dictionary<int, GameObject> layerIdToGameObject, GameObject root, bool uiOrder = false, bool preserveGroups = true)
        {
            int start = uiOrder ? 0 : layers.Count - 1;
            int end = uiOrder ? layers.Count : -1;
            int step = uiOrder ? 1 : -1;
            for (var i = start; i != end; i += step)
            {
                var layer = layers[i];
                if (!preserveGroups && layer.layerType == LayerTypes.Group)
                    continue;
                var go = uiOrder
                    ? new GameObject(layer.name, typeof(RectTransform))
                    : new GameObject(layer.name);
                go.transform.parent = root.transform;
                go.transform.localRotation = Quaternion.identity;
                layerIdToGameObject.Add(layer.index, go);
            }
        }

        static Dictionary<int, float2> CalculateGroupAnchors(
            List<Layer> layers,
            Dictionary<int, GameObject> layerIdToGameObject,
            AsepriteImporterSettings importSettings)
        {
            var anchors = new Dictionary<int, float2>();
            if (!importSettings.balanceGroupPivots
                || importSettings.defaultPivotSpace == PivotSpaces.Canvas
                || importSettings.generateAnimationImageTarget)
                return anchors;

            foreach (var layer in layers)
            {
                if (layer.layerType != LayerTypes.Group || layer.cells.Count > 0)
                    continue;
                if (!layerIdToGameObject.ContainsKey(layer.index))
                    continue;
                if (!TryGetGroupContentBounds(layer, layers, out var bounds))
                    continue;

                anchors.Add(layer.index, CalculateAnchorInBounds(bounds, importSettings));
            }

            return anchors;
        }

        static bool TryGetGroupContentBounds(Layer group, List<Layer> layers, out RectInt bounds)
        {
            bounds = default;
            var hasBounds = false;

            foreach (var layer in layers)
            {
                if (layer.cells.Count == 0 || !IsDescendantOf(layer, group, layers))
                    continue;

                var rect = layer.cells[0].cellRect;
                if (rect.width == 0 || rect.height == 0)
                    continue;

                if (!hasBounds)
                {
                    bounds = rect;
                    hasBounds = true;
                    continue;
                }

                var xMin = Mathf.Min(bounds.xMin, rect.xMin);
                var yMin = Mathf.Min(bounds.yMin, rect.yMin);
                var xMax = Mathf.Max(bounds.xMax, rect.xMax);
                var yMax = Mathf.Max(bounds.yMax, rect.yMax);
                bounds = new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
            }

            return hasBounds;
        }

        static bool IsDescendantOf(Layer layer, Layer group, List<Layer> layers)
        {
            var parentIndex = layer.parentIndex;
            while (parentIndex != -1)
            {
                if (parentIndex == group.index)
                    return true;

                var parent = layers.Find(x => x.index == parentIndex);
                if (parent == null)
                    return false;
                parentIndex = parent.parentIndex;
            }

            return false;
        }

        static float2 CalculateAnchorInBounds(RectInt bounds, AsepriteImporterSettings importSettings)
        {
            var alignment = importSettings.defaultPivotAlignment == SpriteAlignment.Custom
                ? new float2(importSettings.customPivotPosition.x, importSettings.customPivotPosition.y)
                : ImportUtilities.PivotAlignmentToVector(importSettings.defaultPivotAlignment);

            if (importSettings.pixelPerfectPivot)
                alignment = ImportUtilities.SnapPivotToPixel(alignment, bounds);

            return new float2(
                bounds.x + alignment.x * bounds.width,
                bounds.y + alignment.y * bounds.height);
        }

        static bool LayerHasContentInTag(Layer layer, Tag tag)
        {
            foreach (var cell in layer.cells)
                if (cell.frameIndex >= tag.fromFrame && cell.frameIndex < tag.toFrame)
                    return true;
            foreach (var lc in layer.linkedCells)
                if (lc.frameIndex >= tag.fromFrame && lc.frameIndex < tag.toFrame)
                    return true;
            return false;
        }

        static void SetupLayerGameObject(
            Layer layer,
            Dictionary<int, GameObject> layerIdToGameObject,
            Sprite[] sprites,
            AsepriteImporterSettings importSettings,
            Vector2Int canvasSize,
            IReadOnlyDictionary<int, float2> groupAnchors,
            float pixelsPerUnit,
            Tag firstTag = null)
        {
            if (groupAnchors.TryGetValue(layer.index, out var anchor))
            {
                layerIdToGameObject[layer.index].transform.localPosition = CanvasPixelToLocalPosition(anchor, canvasSize, importSettings, pixelsPerUnit);
                return;
            }

            if (layer.cells.Count == 0)
                return;

            var firstCell = layer.cells[0];
            var gameObject = layerIdToGameObject[layer.index];
            var sprite = Array.Find(sprites, x => x.GetSpriteID() == firstCell.spriteId);
            var startEnabled = firstTag == null || LayerHasContentInTag(layer, firstTag);

            if (importSettings.generateAnimationImageTarget)
            {
                var image = gameObject.AddComponent<Image>();
                image.sprite = sprite;
                image.enabled = startEnabled;

                var rt = gameObject.GetComponent<RectTransform>();
                if (sprite != null)
                {
                    rt.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height);
                    rt.pivot = sprite.pivot / sprite.rect.size;
                }

                if (importSettings.addUIComponents)
                {
                    var csf = gameObject.AddComponent<ContentSizeFitter>();
                    csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                    csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                    gameObject.AddComponent<ImageUseSpritePivot>();
                }
            }
            else
            {
                var sr = gameObject.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.enabled = startEnabled;
                sr.sortingOrder = layer.index + firstCell.additiveSortOrder;
                sr.spriteSortPoint = importSettings.usePivotSortPoint ? SpriteSortPoint.Pivot : SpriteSortPoint.Center;
#if ENABLE_URP
                if (importSettings.addShadowCasters)
                    gameObject.AddComponent<UnityEngine.Rendering.Universal.ShadowCaster2D>();
#endif
            }

            if (importSettings.defaultPivotSpace == PivotSpaces.Canvas)
                gameObject.transform.localPosition = Vector3.zero;
            else
            {
                var cellRect = firstCell.cellRect;
                var pivot = sprite.pivot;
                var canvasPixel = new float2(cellRect.x + pivot.x, cellRect.y + pivot.y);
                gameObject.transform.localPosition = CanvasPixelToLocalPosition(canvasPixel, canvasSize, importSettings, sprite.pixelsPerUnit);
            }
        }

        static Vector3 CanvasPixelToLocalPosition(float2 canvasPixel, Vector2Int canvasSize, AsepriteImporterSettings importSettings, float pixelsPerUnit)
        {
            var globalPivot = ImportUtilities.PivotAlignmentToVector(importSettings.defaultPivotAlignment);
            return new Vector3(
                (canvasPixel.x - canvasSize.x * globalPivot.x) / pixelsPerUnit,
                (canvasPixel.y - canvasSize.y * globalPivot.y) / pixelsPerUnit,
                0f);
        }
    }
}
