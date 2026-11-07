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
        internal static string Compose(IReadOnlyList<string> paths, IReadOnlyList<Rect> pawnViewports = null)
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
                var crop = new RectInt(0, 0, width, height);
                if (pawnViewports != null)
                {
                    if (pawnViewports.Count != paths.Count || pawnViewports.Any(r => r.width <= 0 || r.height <= 0)) throw new InvalidOperationException("Pawn view crop metadata is incomplete");
                    // PNG pixels and camera viewports both start at the bottom left. Use one union
                    // for every direction so the original scale and the pawn's alignment are retained.
                    const int padding = 8;
                    var left = Math.Max(0, (int)Math.Floor(pawnViewports.Min(r => r.xMin) * width) - padding);
                    var bottom = Math.Max(0, (int)Math.Floor(pawnViewports.Min(r => r.yMin) * height) - padding);
                    var right = Math.Min(width, (int)Math.Ceiling(pawnViewports.Max(r => r.xMax) * width) + padding);
                    var top = Math.Min(height, (int)Math.Ceiling(pawnViewports.Max(r => r.yMax) * height) + padding);
                    if (right <= left || top <= bottom) throw new InvalidOperationException("Pawn view crop is outside the original image");
                    crop = new RectInt(left, bottom, right - left, top - bottom);
                }
                composite = new Texture2D(checked(crop.width * textures.Count), crop.height, TextureFormat.RGBA32, false);
                for (var i = 0; i < textures.Count; i++) composite.SetPixels(i * crop.width, 0, crop.width, crop.height, textures[i].GetPixels(crop.x, crop.y, crop.width, crop.height));
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
