using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LogSyncMod.AutoTest
{
    internal static class AutoTestViewComposite
    {
        internal static bool IsReady(string path)
        {
            try
            {
                // CaptureScreenshot returns before its encoder closes the PNG. File existence alone
                // is insufficient, including for the last frame of a single-direction view.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length < 32) return false;
                var header = new byte[8]; stream.ReadExactly(header);
                if (!header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException("View original is not PNG: " + path);
                stream.Seek(-12, SeekOrigin.End);
                var trailer = new byte[12]; stream.ReadExactly(trailer);
                return trailer[0] == 0 && trailer[1] == 0 && trailer[2] == 0 && trailer[3] == 0 && trailer[4] == 73 && trailer[5] == 69 && trailer[6] == 78 && trailer[7] == 68;
            }
            catch (IOException) { return false; }
        }
        // Inputs and output belong to the current tool-allocated screenshot directory.
        internal static string Compose(IReadOnlyList<string> paths)
        {
            var textures = new List<Texture2D>();
            Texture2D composite = null;
            try
            {
                foreach (var path in paths)
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    textures.Add(texture);
                    if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path))) throw new InvalidOperationException("Cannot decode view original: " + path);
                }
                if (textures.Count == 0 || textures.Any(t => t.width != textures[0].width || t.height != textures[0].height)) throw new InvalidOperationException("View originals have inconsistent dimensions");
                var width = textures[0].width; var height = textures[0].height;
                composite = new Texture2D(checked(width * textures.Count), height, TextureFormat.RGBA32, false);
                for (var i = 0; i < textures.Count; i++) composite.SetPixels(i * width, 0, width, height, textures[i].GetPixels());
                composite.Apply();
                var output = Path.Combine(Path.GetDirectoryName(paths[0]), Path.GetFileNameWithoutExtension(paths[0]) + "_composite.png");
                File.WriteAllBytes(output, ImageConversion.EncodeToPNG(composite));
                return output;
            }
            finally
            {
                foreach (var texture in textures) UnityEngine.Object.Destroy(texture);
                if (composite != null) UnityEngine.Object.Destroy(composite);
            }
        }
    }
}
