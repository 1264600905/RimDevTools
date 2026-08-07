using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MyCustomMod
{
    public static class ShaderBundleBuilder
    {
        private const string DataSearchRoot = "Assets/Data";
        private const string BundleName = "mycustommod_shaders";

        [MenuItem("MyCustomMod/Build Shader Bundle")]
        public static void Build()
        {
            string[] shaderAssets = FindShaderAssets();
            if (shaderAssets.Length == 0)
            {
                throw new IOException("No shader files found under Assets/Data.");
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            string outputDir = Path.Combine(projectRoot, "AssetBundles");
            Directory.CreateDirectory(outputDir);

            BuildPipeline.BuildAssetBundles(
                outputDir,
                new[]
                {
                    new AssetBundleBuild
                    {
                        assetBundleName = BundleName,
                        assetNames = shaderAssets
                    }
                },
                BuildAssetBundleOptions.ForceRebuildAssetBundle,
                BuildTarget.StandaloneWindows64);

            DeleteIfExists(Path.Combine(outputDir, "AssetBundles"));
            DeleteIfExists(Path.Combine(outputDir, "AssetBundles.manifest"));

            Debug.Log("[MyCustomMod] Shader bundle build finished: " + Path.Combine(outputDir, BundleName));
        }

        private static string[] FindShaderAssets()
        {
            var shaderFiles = new List<string>();
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string searchDir = Path.Combine(unityProjectRoot, DataSearchRoot);

            if (!Directory.Exists(searchDir))
            {
                return shaderFiles.ToArray();
            }

            foreach (string file in Directory.GetFiles(searchDir, "*.shader", SearchOption.AllDirectories))
            {
                string relativePath = file.Replace(unityProjectRoot + Path.DirectorySeparatorChar, string.Empty)
                    .Replace(Path.DirectorySeparatorChar, '/');
                shaderFiles.Add(relativePath);
            }

            return shaderFiles.ToArray();
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
