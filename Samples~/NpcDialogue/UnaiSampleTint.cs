using UnityEngine;
using UnityEngine.Rendering;

namespace UnAI.Samples.NpcDialogue
{
    /// <summary>
    /// Colors a primitive without shipping materials, so the scene renders in Built-in, URP and HDRP:
    /// uses the active render pipeline's default material and sets the color through a property block.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class UnaiSampleTint : MonoBehaviour
    {
        public Color Color = Color.white;

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();

        private void Apply()
        {
            var renderer = GetComponent<Renderer>();
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && pipeline.defaultMaterial != null && renderer.sharedMaterial != pipeline.defaultMaterial)
                renderer.sharedMaterial = pipeline.defaultMaterial;

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_Color", Color);     // Built-in
            block.SetColor("_BaseColor", Color); // URP / HDRP
            renderer.SetPropertyBlock(block);
        }
    }
}
