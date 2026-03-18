using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestScreenshotService
    {
        private readonly AutoTestRunArtifacts runArtifacts;
        private readonly Func<string, string, string> appendNote;
        private readonly Func<string, string> trimForNote;

        public AutoTestScreenshotService(
            AutoTestRunArtifacts runArtifacts,
            Func<string, string, string> appendNote,
            Func<string, string> trimForNote)
        {
            this.runArtifacts = runArtifacts;
            this.appendNote = appendNote;
            this.trimForNote = trimForNote;
        }

        public bool TryCaptureInfoCard(
            AutoTestResult result,
            ref bool screenshotCaptured,
            bool allowCapture,
            bool force,
            int resultIndex,
            Func<bool> isInfoCardPresent)
        {
            if (result == null || screenshotCaptured)
            {
                return false;
            }

            if (!force && !allowCapture)
            {
                return false;
            }

            if (runArtifacts == null || isInfoCardPresent == null || !isInfoCardPresent())
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(runArtifacts.ScreenshotDirectory);
                string fileName = string.Format(
                    "{0:D4}_{1}.png",
                    resultIndex,
                    SanitizeFileName(result.DefName));
                string screenshotPath = Path.Combine(runArtifacts.ScreenshotDirectory, fileName);

                AutoTestScreenshotOverlay.Request(result);
                ScreenCapture.CaptureScreenshot(screenshotPath);
                result.ScreenshotPath = screenshotPath;
                if (result.ScreenshotPaths == null)
                {
                    result.ScreenshotPaths = new List<string>();
                }
                result.ScreenshotPaths.Add(screenshotPath);
                result.Notes = appendNote(
                    result.Notes,
                    force ? "FailureScreenshotCaptured=True" : "ScreenshotCaptured=True");
                screenshotCaptured = true;

                Log.Message(
                    "[LogSyncAutoTest] Screenshot requested for " +
                    result.DefName +
                    " force=" +
                    force +
                    " -> " +
                    screenshotPath);

                return true;
            }
            catch (Exception ex)
            {
                result.Notes = appendNote(result.Notes, "ScreenshotError=" + trimForNote(ex.Message));
                Log.Warning("[LogSyncAutoTest] Screenshot capture failed for " + result.DefName + ": " + ex.Message);
                return false;
            }
        }

        public bool TryCaptureMap(
            AutoTestResult result,
            bool allowCapture,
            bool force,
            int resultIndex,
            string stageSuffix,
            Action prepare,
            string relativeSubdirectory = null)
        {
            if (result == null)
            {
                return false;
            }

            if (!force && !allowCapture)
            {
                return false;
            }

            if (runArtifacts == null)
            {
                return false;
            }

            try
            {
                prepare?.Invoke();

                string screenshotDirectory = string.IsNullOrWhiteSpace(relativeSubdirectory)
                    ? runArtifacts.ScreenshotDirectory
                    : Path.Combine(runArtifacts.ScreenshotDirectory, relativeSubdirectory);
                Directory.CreateDirectory(screenshotDirectory);

                string fileName = string.Format(
                    "{0:D4}_{1}_{2}.png",
                    resultIndex,
                    SanitizeFileName(result.DefName),
                    SanitizeFileName(stageSuffix));
                string screenshotPath = Path.Combine(screenshotDirectory, fileName);

                AutoTestScreenshotOverlay.Request(result);
                ScreenCapture.CaptureScreenshot(screenshotPath);
                result.ScreenshotPath = screenshotPath;
                if (result.ScreenshotPaths == null)
                {
                    result.ScreenshotPaths = new List<string>();
                }
                result.ScreenshotPaths.Add(screenshotPath);
                result.Notes = appendNote(
                    result.Notes,
                    force ? "TurretFailureScreenshotCaptured=True" : "TurretScreenshotCaptured=True");

                Log.Message(
                    "[LogSyncAutoTest] Turret screenshot requested for " +
                    result.DefName +
                    " force=" +
                    force +
                    " -> " +
                    screenshotPath);

                return true;
            }
            catch (Exception ex)
            {
                result.Notes = appendNote(result.Notes, "TurretScreenshotError=" + trimForNote(ex.Message));
                Log.Warning("[LogSyncAutoTest] Turret screenshot capture failed for " + result.DefName + ": " + ex.Message);
                return false;
            }
        }

        public int ResolveTurretResultIndex(IReadOnlyList<AutoTestResult> results, int activeSessionIndex)
        {
            int completedCount = results?.Count ?? 0;
            int offset = activeSessionIndex < 0 ? 0 : activeSessionIndex;
            return completedCount + offset + 1;
        }

        private static string SanitizeFileName(string text)
        {
            string value = string.IsNullOrWhiteSpace(text) ? "autotest" : text;
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidChar, '_');
            }

            return value;
        }
    }

    internal static class AutoTestScreenshotOverlay
    {
        private const int OverlayRepaintFrames = 3;
        private static string line1 = string.Empty;
        private static string line2 = string.Empty;
        private static int remainingRepaintFrames;

        public static void Request(AutoTestResult result)
        {
            line1 = string.IsNullOrWhiteSpace(result?.DefName) ? "UnknownDef" : result.DefName.Trim();
            line2 = string.IsNullOrWhiteSpace(result?.Label) ? line1 : result.Label.Trim();
            remainingRepaintFrames = Math.Max(remainingRepaintFrames, OverlayRepaintFrames);
        }

        public static void DrawOverlay()
        {
            if (remainingRepaintFrames <= 0 || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            float margin = 16f;
            float width = Mathf.Min(Screen.width - margin * 2f, Screen.width * 0.7f);
            Rect backgroundRect = new Rect(margin, margin, width, 150f);
            Widgets.DrawBoxSolid(backgroundRect, new Color(0f, 0f, 0f, 0.72f));

            Color originalColor = GUI.color;
            GameFont originalFont = Text.Font;
            TextAnchor originalAnchor = Text.Anchor;

            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;

                DrawShadowedLabel(new Rect(backgroundRect.x + 14f, backgroundRect.y + 10f, backgroundRect.width - 20f, 58f), line1);
                DrawShadowedLabel(new Rect(backgroundRect.x + 14f, backgroundRect.y + 76f, backgroundRect.width - 20f, 58f), line2);
            }
            finally
            {
                GUI.color = originalColor;
                Text.Font = originalFont;
                Text.Anchor = originalAnchor;
                remainingRepaintFrames--;
            }
        }

        private static void DrawShadowedLabel(Rect rect, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            GUIStyle style = new GUIStyle(Text.CurFontStyle)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 38,
                wordWrap = false,
                clipping = TextClipping.Overflow
            };

            GUI.color = Color.black;
            GUI.Label(new Rect(rect.x + 3f, rect.y + 3f, rect.width, rect.height), text, style);
            GUI.color = Color.white;
            GUI.Label(rect, text, style);
        }
    }
}
