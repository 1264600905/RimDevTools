using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MyCustomMod.Rendering
{
    public static class ShaderMaterialCache
    {
        private static readonly Dictionary<string, Material> CachedMaterials = new Dictionary<string, Material>();
        private static readonly HashSet<string> MissingShaders = new HashSet<string>();
        private static readonly HashSet<string> LoadedShaders = new HashSet<string>();

        public static Material GetMaterial(string shaderPath, int renderQueue)
        {
            if (string.IsNullOrWhiteSpace(shaderPath))
            {
                return null;
            }

            if (CachedMaterials.TryGetValue(shaderPath, out Material cached))
            {
                return cached;
            }

            Shader shader = ContentFinder<Shader>.Get(shaderPath, false);
            if (shader == null)
            {
                if (MissingShaders.Add(shaderPath))
                {
                    LogUtils.Warning(nameof(ShaderMaterialCache), $"未找到 Shader: {shaderPath}");
                }

                return null;
            }

            var material = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = renderQueue
            };

            if (LoadedShaders.Add(shaderPath))
            {
                LogUtils.Message(nameof(ShaderMaterialCache), $"已加载 Shader: {shaderPath}");
            }

            CachedMaterials[shaderPath] = material;
            return material;
        }
    }
}
