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
}
