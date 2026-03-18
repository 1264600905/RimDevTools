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
                bool captured = TryCapture(new AutoTestScreenshotRequest
                {
                    Result = result,
                    AllowCapture = allowCapture,
                    Force = force,
                    ResultIndex = resultIndex,
                    StageSuffix = null,
                    Profile = AutoTestScreenshotProfiles.InfoCard,
                    SuccessNoteKey = "ScreenshotCaptured=True",
                    FailureNoteKey = "FailureScreenshotCaptured=True",
                    Guard = isInfoCardPresent
                });
                screenshotCaptured = screenshotCaptured || captured;
                return captured;
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
            return TryCapture(new AutoTestScreenshotRequest
            {
                Result = result,
                AllowCapture = allowCapture,
                Force = force,
                ResultIndex = resultIndex,
                StageSuffix = stageSuffix,
                RelativeSubdirectory = relativeSubdirectory,
                Prepare = prepare,
                Profile = AutoTestScreenshotProfiles.PocketMapOverview,
                SuccessNoteKey = "TurretScreenshotCaptured=True",
                FailureNoteKey = "TurretFailureScreenshotCaptured=True"
            });
        }

        public bool TryCapture(AutoTestScreenshotRequest request)
        {
            AutoTestResult result = request?.Result;
            if (result == null)
            {
                return false;
            }

            if (!request.Force && !request.AllowCapture)
            {
                return false;
            }

            if (runArtifacts == null)
            {
                return false;
            }

            if (request.Guard != null && !request.Guard())
            {
                return false;
            }

            try
            {
                request.Prepare?.Invoke();

                if (request.CaptureProbe != null)
                {
                    AutoTestCaptureProbeResult probeResult = request.CaptureProbe();
                    if (probeResult != null)
                    {
                        Log.Message(
                            "[LogSyncAutoTest] CaptureProbe " +
                            result.DefName +
                            " stage=" +
                            (request.StageSuffix ?? "default") +
                            " ready=" +
                            probeResult.Ready +
                            " details=" +
                            (probeResult.Details ?? string.Empty));

                        if (request.RequireCaptureProbePass && !probeResult.Ready)
                        {
                            result.Notes = appendNote(result.Notes, "CaptureProbeFailed=" + trimForNote(probeResult.Details));
                            return false;
                        }
                    }
                }

                string screenshotDirectory = ResolveScreenshotDirectory(request);
                Directory.CreateDirectory(screenshotDirectory);

                string suffix = string.IsNullOrWhiteSpace(request.StageSuffix)
                    ? null
                    : SanitizeFileName(request.StageSuffix);
                string fileName = string.IsNullOrWhiteSpace(suffix)
                    ? string.Format("{0:D4}_{1}.png", request.ResultIndex, SanitizeFileName(result.DefName))
                    : string.Format("{0:D4}_{1}_{2}.png", request.ResultIndex, SanitizeFileName(result.DefName), suffix);
                string screenshotPath = Path.Combine(screenshotDirectory, fileName);

                AutoTestScreenshotOverlay.Request(result);
                ScreenCapture.CaptureScreenshot(screenshotPath);
                result.ScreenshotPath = screenshotPath;
                if (result.ScreenshotPaths == null)
                {
                    result.ScreenshotPaths = new List<string>();
                }

                result.ScreenshotPaths.Add(screenshotPath);
                string noteKey = request.IsFailureArtifact
                    ? (request.FailureNoteKey ?? "FailureScreenshotCaptured=True")
                    : (request.SuccessNoteKey ?? "ScreenshotCaptured=True");
                result.Notes = appendNote(result.Notes, noteKey);
                if (request.Profile != null && !string.IsNullOrWhiteSpace(request.Profile.ProfileId))
                {
                    result.Notes = appendNote(result.Notes, "ScreenshotProfile=" + request.Profile.ProfileId);
                }

                Log.Message(
                    "[LogSyncAutoTest] Screenshot requested for " +
                    result.DefName +
                    " force=" +
                    request.Force +
                    " profile=" +
                    (request.Profile?.ProfileId ?? "None") +
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

        private string ResolveScreenshotDirectory(AutoTestScreenshotRequest request)
        {
            string baseDirectory;
            if (request != null && request.IsFailureArtifact)
            {
                baseDirectory = Path.Combine(runArtifacts.ScreenshotDirectory, "fail");
            }
            else
            {
                string caseTypeDirectory = SanitizeFileName(request?.Result?.CaseType);
                baseDirectory = string.IsNullOrWhiteSpace(caseTypeDirectory)
                    ? runArtifacts.ScreenshotDirectory
                    : Path.Combine(runArtifacts.ScreenshotDirectory, caseTypeDirectory);
            }

            if (string.IsNullOrWhiteSpace(request?.RelativeSubdirectory))
            {
                return baseDirectory;
            }

            return Path.Combine(baseDirectory, request.RelativeSubdirectory);
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
