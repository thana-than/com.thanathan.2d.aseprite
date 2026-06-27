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

            animationClips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

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

        // Creates one AnimatorController per layer. All tags are covered — clip generation
        // produces disable clips for tags the layer has no content in, so every state is filled.
        public static void GeneratePerLayer(
            AssetImportContext ctx,
            string assetName,
            Dictionary<int, List<AnimationClip>> layerClips,
            Dictionary<int, GameObject> layerIdToGameObject,
            bool generateModelPrefab)
        {
            if (layerClips.Count == 0) return;

            foreach (var (layerIndex, clips) in layerClips)
            {
                if (clips.Count == 0) continue;
                if (!layerIdToGameObject.TryGetValue(layerIndex, out var layerGo)) continue;

                var layerName = layerGo.name;
                var prefix = layerName + "_";
                var controllerName = assetName + "_" + layerName;

                var controller = new AnimatorController { name = controllerName };
                controller.AddLayer("Base Layer");

                foreach (var clip in clips)
                {
                    var state = controller.AddMotion(clip);
                    state.name = clip.name.StartsWith(prefix) ? clip.name.Substring(prefix.Length) : clip.name;
                }

                ctx.AddObjectToAsset(controllerName + "_Controller", controller);
                foreach (var animLayer in controller.layers)
                {
                    var sm = animLayer.stateMachine;
                    ctx.AddObjectToAsset(controllerName + "_StateMachine", sm);
                    foreach (var state in sm.states)
                        ctx.AddObjectToAsset(controllerName + "_" + state.state.name + "_State", state.state);
                }

                if (generateModelPrefab)
                {
                    var animator = layerGo.AddComponent<Animator>();
                    AnimatorController.SetAnimatorController(animator, controller);
                }
            }
        }
    }
}
