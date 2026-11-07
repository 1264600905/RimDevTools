using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class GroupCombatScreenshot
    {
        // Generate the requested original artifact directly from the completed frame;
        // screenshot dimensions must never change the live fullscreen window.
        internal static IEnumerator Capture(string path, int width, int height)
        {
            yield return new WaitForEndOfFrame();
            Texture2D source = null, output = null;
            RenderTexture target = null;
            var previous = RenderTexture.active;
            try
            {
                source = ScreenCapture.CaptureScreenshotAsTexture();
                target = new RenderTexture(width, height, 0); target.Create();
                Graphics.Blit(source, target); RenderTexture.active = target;
                output = new Texture2D(width, height, TextureFormat.RGBA32, false);
                output.ReadPixels(new Rect(0, 0, width, height), 0, 0); output.Apply();
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(output));
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
                if (source != null) UnityEngine.Object.Destroy(source);
                if (output != null) UnityEngine.Object.Destroy(output);
            }
        }
    }
}
