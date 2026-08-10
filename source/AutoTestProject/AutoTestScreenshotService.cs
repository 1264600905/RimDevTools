using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestScreenshotService
    {
        private const int MaxScreenshotLabelLength = 72;
        private readonly AutoTestRunArtifacts runArtifacts;
        private readonly Func<string, string, string> appendNote;
        private readonly Func<string, string> trimForNote;
        private static readonly System.Type EditWindowLogType = AccessTools.TypeByName("LudeonTK.EditWindow_Log");
        private static int debugLogDismissedFrame = -1;

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

                string fileName = BuildScreenshotFileName(request);
                string screenshotPath = Path.Combine(screenshotDirectory, fileName);

                AutoTestScreenshotOverlay.Request(result);
                DismissDebugLogWindow();
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
            string caseTypeDirectory = SanitizeFileName(request?.Result?.CaseType);
            string topLevelGroup = ResolveTopLevelGroup(request);
            string baseDirectory = string.IsNullOrWhiteSpace(topLevelGroup)
                ? runArtifacts.ScreenshotDirectory
                : Path.Combine(runArtifacts.ScreenshotDirectory, topLevelGroup);

            if (!string.IsNullOrWhiteSpace(caseTypeDirectory))
            {
                baseDirectory = Path.Combine(baseDirectory, caseTypeDirectory);
            }

            if (string.IsNullOrWhiteSpace(request?.RelativeSubdirectory))
            {
                return baseDirectory;
            }

            return Path.Combine(baseDirectory, request.RelativeSubdirectory);
        }

        private static string ResolveTopLevelGroup(AutoTestScreenshotRequest request)
        {
            string formalScope = (request?.Result?.Scope ?? string.Empty).Trim();
            if (string.Equals(formalScope, "UI", StringComparison.OrdinalIgnoreCase))
            {
                return "UI";
            }

            if (string.Equals(formalScope, "Combat", StringComparison.OrdinalIgnoreCase))
            {
                return "Combat";
            }

            if (string.Equals(formalScope, "View", StringComparison.OrdinalIgnoreCase))
            {
                return "View";
            }

            if (string.Equals(formalScope, "Misc", StringComparison.OrdinalIgnoreCase))
            {
                return "Misc";
            }

            switch ((request?.Result?.CaseType ?? string.Empty).Trim())
            {
                case "BuildingInfoCard":
                case "ItemInfoCard":
                case "PlantInfoCard":
                case "TerrainInfoCard":
                case "RecipeInfoCard":
                case "RecipeBillUi":
                case "HediffInfoCard":
                case "AnimalInfoCard":
                case "MechanoidInfoCard":
                case "PawnInfoCard":
                case "GeneInfoCard":
                case "XenotypeInfoCard":
                    return "UI";

                case "TurretPocketMap":
                case "RangedWeaponPocketMap":
                case "MeleeWeaponPocketMap":
                case "ApparelCombat":
                case "AnimalCombat":
                case "MechanoidCombat":
                case "AlienCombat":
                    return "Combat";

                case "HumanoidThreeView":
                case "AnimalThreeView":
                case "MechanoidThreeView":
                case "AlienThreeView":
                case "ApparelThreeView":
                case "WeaponThreeView":
                case "PlantGrowthView":
                case "BuildingView":
                case "TerrainPatchView":
                case "PawnLifeStageThreeView":
                case "HairThreeView":
                case "HeadTypeThreeView":
                case "BodyTypeThreeView":
                case "GeneThreeView":
                case "XenotypeThreeView":
                    return "View";

                case "SkillPocketMap":
                    return "Misc";

                default:
                    return "Misc";
            }
        }

        private static string BuildScreenshotFileName(AutoTestScreenshotRequest request)
        {
            AutoTestResult result = request?.Result;
            string defName = SanitizeFileName(result?.DefName);
            string rawLabel = string.IsNullOrWhiteSpace(result?.Label) ? result?.DefName : result.Label;
            string label = TruncateLabel(SanitizeFileName(rawLabel));
            string suffix = string.IsNullOrWhiteSpace(request?.StageSuffix)
                ? null
                : SanitizeFileName(request.StageSuffix);
            string identity = defName + "(" + label + ")";
            return string.IsNullOrWhiteSpace(suffix)
                ? string.Format("{0:D4}_{1}.png", request?.ResultIndex ?? 0, identity)
                : string.Format("{0:D4}_{1}_{2}.png", request?.ResultIndex ?? 0, identity, suffix);
        }

        private static string TruncateLabel(string label)
        {
            string value = string.IsNullOrWhiteSpace(label) ? "autotest" : label;
            return value.Length <= MaxScreenshotLabelLength
                ? value
                : value.Substring(0, MaxScreenshotLabelLength);
        }

        public int ResolveTurretResultIndex(IReadOnlyList<AutoTestResult> results, int activeSessionIndex)
        {
            int completedCount = results?.Count ?? 0;
            int offset = activeSessionIndex < 0 ? 0 : activeSessionIndex;
            return completedCount + offset + 1;
        }

        public static bool PrepareCaptureFrame()
        {
            bool removed = DismissDebugLogWindow();
            if (removed)
            {
                debugLogDismissedFrame = Time.frameCount;
                return false;
            }

            return debugLogDismissedFrame < 0 || Time.frameCount > debugLogDismissedFrame;
        }

        private static bool DismissDebugLogWindow()
        {
            try
            {
                if (Current.ProgramState != ProgramState.Playing)
                {
                    return false;
                }

                WindowStack windowStack = Find.WindowStack;
                if (windowStack == null)
                {
                    return false;
                }

                bool removed = false;
                foreach (Window window in windowStack.Windows
                    .Where(window => window != null && string.Equals(window.GetType().Name, "EditWindow_Log", StringComparison.Ordinal))
                    .ToList())
                {
                    removed = windowStack.TryRemove(window, doCloseSound: false) || removed;
                }

                if (EditWindowLogType != null)
                {
                    removed = windowStack.TryRemove(EditWindowLogType, doCloseSound: false) || removed;
                }

                if (removed)
                {
                    Log.Message("[LogSyncAutoTest] Dismissed debug log window before screenshot capture.");
                }

                return removed;
            }
            catch (Exception ex)
            {
                Log.Warning("[LogSyncAutoTest] Failed to dismiss debug log window: " + ex.Message);
                return false;
            }
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
        private static string line3 = string.Empty;
        private static string line4 = string.Empty;
        private static int remainingRepaintFrames;

        public static void Request(AutoTestResult result)
        {
            line1 = string.IsNullOrWhiteSpace(result?.OverlayLine1)
                ? (string.IsNullOrWhiteSpace(result?.DefName) ? "UnknownDef" : result.DefName.Trim())
                : result.OverlayLine1.Trim();
            line2 = string.IsNullOrWhiteSpace(result?.OverlayLine2)
                ? (string.IsNullOrWhiteSpace(result?.Label) ? line1 : result.Label.Trim())
                : result.OverlayLine2.Trim();
            line3 = string.IsNullOrWhiteSpace(result?.OverlayLine3) ? string.Empty : result.OverlayLine3.Trim();
            line4 = string.IsNullOrWhiteSpace(result?.OverlayLine4) ? string.Empty : result.OverlayLine4.Trim();
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
            int visibleLineCount = 0;
            if (!string.IsNullOrWhiteSpace(line1)) visibleLineCount++;
            if (!string.IsNullOrWhiteSpace(line2)) visibleLineCount++;
            if (!string.IsNullOrWhiteSpace(line3)) visibleLineCount++;
            if (!string.IsNullOrWhiteSpace(line4)) visibleLineCount++;

            float backgroundHeight = visibleLineCount >= 4 ? 206f : 150f;
            Rect backgroundRect = new Rect(margin, margin, width, backgroundHeight);
            Widgets.DrawBoxSolid(backgroundRect, new Color(0f, 0f, 0f, 0.72f));

            Color originalColor = GUI.color;
            GameFont originalFont = Text.Font;
            TextAnchor originalAnchor = Text.Anchor;

            try
            {
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.UpperLeft;

                DrawShadowedLabel(new Rect(backgroundRect.x + 14f, backgroundRect.y + 10f, backgroundRect.width - 20f, 52f), line1, 38);
                DrawShadowedLabel(new Rect(backgroundRect.x + 14f, backgroundRect.y + 66f, backgroundRect.width - 20f, 52f), line2, 38);
                DrawShadowedLabel(new Rect(backgroundRect.x + 14f, backgroundRect.y + 122f, backgroundRect.width - 20f, 36f), line3, 26);
                DrawShadowedLabel(new Rect(backgroundRect.x + 14f, backgroundRect.y + 158f, backgroundRect.width - 20f, 36f), line4, 26);
            }
            finally
            {
                GUI.color = originalColor;
                Text.Font = originalFont;
                Text.Anchor = originalAnchor;
                remainingRepaintFrames--;
            }
        }

        private static void DrawShadowedLabel(Rect rect, string text, int fontSize)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            GUIStyle style = new GUIStyle(Text.CurFontStyle)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = fontSize,
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
