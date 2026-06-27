using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UnityEditor.U2D.Aseprite
{
    internal static class AnimatorControllerGeneration
    {
        public static void Generate(AssetImportContext ctx, string assetName, GameObject rootGameObject, bool generateModelPrefab)
        {
            var assetObjects = new List<Object>();
            ctx.GetObjects(assetObjects);

            var animationClips = new List<AnimationClip>();
            foreach (var obj in assetObjects)
            {
                if (obj is AnimationClip clip)
                    animationClips.Add(clip);
            }

            if (animationClips.Count == 0)
                return;

            var controller = new AnimatorController();
            controller.name = assetName;
            controller.AddLayer("Base Layer");

            foreach (var clip in animationClips)
                controller.AddMotion(clip);

            ctx.AddObjectToAsset(controller.name + "_Controller", controller);
            foreach (var layer in controller.layers)
            {
                var stateMachine = layer.stateMachine;
                ctx.AddObjectToAsset(stateMachine.name + "_StateMachine", stateMachine);

                foreach (var state in stateMachine.states)
                    ctx.AddObjectToAsset(state.state.name + "_State", state.state);
            }

            if (generateModelPrefab)
            {
                var animator = rootGameObject.AddComponent<Animator>();
                AnimatorController.SetAnimatorController(animator, controller);
            }
        }

        // Creates one shared base AnimatorController (one state per tag, empty placeholder clips),
        // then one AnimatorOverrideController per layer that swaps in the layer's actual clips.
        // All layers share the same state machine, so a single parameter/trigger drives them all in sync.
        public static void GeneratePerLayer(
            AssetImportContext ctx,
            string assetName,
            Dictionary<int, List<AnimationClip>> layerClips,
            Dictionary<int, GameObject> layerIdToGameObject,
            bool generateModelPrefab)
        {
            if (layerClips.Count == 0) return;

            // Collect all unique tag names and create an empty base clip for each.
            var tagBaseClips = new Dictionary<string, AnimationClip>();
            foreach (var (layerIndex, clips) in layerClips)
            {
                if (!layerIdToGameObject.TryGetValue(layerIndex, out var layerGo)) continue;
                var prefix = layerGo.name + "_";
                foreach (var clip in clips)
                {
                    var tagName = clip.name.StartsWith(prefix) ? clip.name.Substring(prefix.Length) : clip.name;
                    if (!tagBaseClips.ContainsKey(tagName))
                        tagBaseClips[tagName] = new AnimationClip { name = tagName };
                }
            }

            // Build base controller — one state per tag using the empty placeholder clips.
            var baseController = new AnimatorController { name = assetName + "_Base" };
            baseController.AddLayer("Base Layer");
            foreach (var baseClip in tagBaseClips.Values)
                baseController.AddMotion(baseClip);

            ctx.AddObjectToAsset(baseController.name + "_Controller", baseController);
            foreach (var baseClip in tagBaseClips.Values)
                ctx.AddObjectToAsset(baseClip.name + "_BaseClip", baseClip);
            foreach (var animLayer in baseController.layers)
            {
                var sm = animLayer.stateMachine;
                ctx.AddObjectToAsset(sm.name + "_StateMachine", sm);
                foreach (var state in sm.states)
                    ctx.AddObjectToAsset(state.state.name + "_State", state.state);
            }

            // Build one AnimatorOverrideController per layer, swapping in that layer's clips.
            foreach (var (layerIndex, clips) in layerClips)
            {
                if (clips.Count == 0) continue;
                if (!layerIdToGameObject.TryGetValue(layerIndex, out var layerGo)) continue;

                var layerName = layerGo.name;
                var prefix = layerName + "_";

                var overrideController = new AnimatorOverrideController(baseController) { name = assetName + "_" + layerName };

                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                overrideController.GetOverrides(overrides);
                for (var i = 0; i < overrides.Count; i++)
                {
                    var tagName = overrides[i].Key.name;
                    var layerClip = clips.Find(c => c.name == prefix + tagName || c.name == tagName);
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, layerClip);
                }
                overrideController.ApplyOverrides(overrides);

                ctx.AddObjectToAsset(overrideController.name + "_Override", overrideController);

                if (generateModelPrefab)
                {
                    var animator = layerGo.AddComponent<Animator>();
                    animator.runtimeAnimatorController = overrideController;
                }
            }
        }
    }
}
