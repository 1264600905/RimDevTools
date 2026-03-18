using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class AutoTestRunner
    {
        private enum SessionPhase
        {
            WaitingForPreOpenSettle,
            WaitingForInfoCardOpen,
            WaitingForCaptureSettle,
            WaitingBeforeClose,
            WaitingForInfoCardClose
        }

        private sealed class ActiveSession
        {
            public AutoTestWorkItem Item;
            public AutoTestResult Result;
            public Thing SpawnedThing;
            public TerrainDef TerrainDef;
            public Def InfoCardDef;
            public Pawn TestPawn;
            public Hediff AttachedHediff;
            public IAutoTestUiHandle UiHandle;
            public bool CloseIssued;
            public bool ScreenshotCaptured;
            public bool AllowScreenshots;
            public int OpenStableRepaintFrames;
            public int RemainingUiFrames;
            public SessionPhase Phase;
        }

        public void ProcessUi()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            if (activeThreeViewUiSession != null)
            {
                AutoTestScreenshotOverlay.Request(activeThreeViewUiSession.Result);
            }

            if (activeSession != null)
            {
                AutoTestScreenshotOverlay.Request(activeSession.Result);
            }
            AutoTestScreenshotOverlay.DrawOverlay();

            if (completed) return;

            if (activeThreeViewUiSession != null)
            {
                try
                {
                    ProcessThreeViewUiSession();
                }
                catch (Exception ex)
                {
                    activeThreeViewUiSession.FailFromUi("ui_capture_failed", ex.ToString());
                    activeThreeViewUiSession = null;
                }
            }

            if (activeSession == null) return;

            try
            {
                switch (activeSession.Phase)
                {
                    case SessionPhase.WaitingForPreOpenSettle:
                        ProcessWaitingForPreOpenSettle();
                        break;
                    case SessionPhase.WaitingForInfoCardOpen:
                        ProcessWaitingForInfoCardOpen();
                        break;
                    case SessionPhase.WaitingForCaptureSettle:
                        ProcessWaitingForCaptureSettle();
                        break;
                    case SessionPhase.WaitingBeforeClose:
                        ProcessWaitingBeforeClose();
                        break;
                    case SessionPhase.WaitingForInfoCardClose:
                        ProcessWaitingForInfoCardClose();
                        break;
                }
            }
            catch (Exception ex)
            {
                activeSession.Result.Status = activeSession.Phase == SessionPhase.WaitingForInfoCardClose
                    ? "failed_info_card_close_request"
                    : "failed_info_card_open_request";
                activeSession.Result.Stage = activeSession.Phase == SessionPhase.WaitingForInfoCardClose ? "info_close" : "info_open";
                activeSession.Result.ErrorMessage = ex.ToString();
                TryCaptureInfoCardScreenshot(true);
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "Trace=" + InfoCardCloseTrace.Describe());
                CleanupSelectionAndWindows();
                InfoCardCloseTrace.Stop();
                activeSession = null;
            }
        }

        private void ProcessThreeViewUiSession()
        {
            ThreeViewTestSession session = activeThreeViewUiSession;
            if (session == null)
            {
                return;
            }

            if (session.ProcessUiFrame())
            {
                activeThreeViewUiSession = null;
            }
        }

        private void ProcessWaitingForPreOpenSettle()
        {
            if (activeSession == null) return;

            activeSession.RemainingUiFrames--;
            if (activeSession.RemainingUiFrames > 0) return;

            activeSession.OpenStableRepaintFrames = 0;
            activeSession.RemainingUiFrames = OpenTimeoutRepaintFrames;
            activeSession.Phase = SessionPhase.WaitingForInfoCardOpen;
            Log.Message("[LogSyncAutoTest] InfoCard pre-open settle completed for " + activeSession.Result.DefName);
        }

        private void ProcessWaitingForInfoCardOpen()
        {
            if (activeSession == null) return;

            if (activeSession.UiHandle == null)
            {
                OpenInfoCardOnUiThread();
                return;
            }

            if (IsInfoCardPresent())
            {
                if (activeSession.OpenStableRepaintFrames == 0)
                {
                    InfoCardCloseTrace.AddReason("OpenDetected");
                    Log.Message("[LogSyncAutoTest] InfoCard visible for " + activeSession.Result.DefName);
                }

                activeSession.OpenStableRepaintFrames++;
                if (activeSession.OpenStableRepaintFrames >= OpenStableFrameRequirement)
                {
                    InfoCardCloseTrace.AddReason("OpenConfirmed.RepaintFrames=" + activeSession.OpenStableRepaintFrames);
                    Log.Message("[LogSyncAutoTest] InfoCard open confirmed for " + activeSession.Result.DefName);
                    activeSession.Phase = SessionPhase.WaitingForCaptureSettle;
                    activeSession.RemainingUiFrames = 2;
                }

                return;
            }

            activeSession.OpenStableRepaintFrames = 0;
            activeSession.RemainingUiFrames--;
            if (activeSession.RemainingUiFrames > 0) return;

            FailActiveSession(
                "failed_info_card_open",
                "info_open",
                "InfoCard did not stay open long enough to confirm opening. " + BuildWindowSummary());
        }

        private void ProcessWaitingForCaptureSettle()
        {
            if (activeSession == null) return;

            if (!IsInfoCardPresent())
            {
                FailActiveSession(
                    "failed_info_card_interrupted",
                    "info_open",
                    "InfoCard disappeared before screenshot capture.");
                return;
            }

            activeSession.RemainingUiFrames--;
            if (activeSession.RemainingUiFrames > 0) return;

            TryCaptureInfoCardScreenshot(false);
            activeSession.Phase = SessionPhase.WaitingBeforeClose;
            activeSession.RemainingUiFrames = WaitBeforeCloseRepaintFrames;
        }

        private void ProcessWaitingBeforeClose()
        {
            if (activeSession == null) return;

            if (!IsInfoCardPresent())
            {
                FailActiveSession(
                    "failed_info_card_interrupted",
                    "info_open",
                    "InfoCard disappeared before auto-test requested close.");
                return;
            }

            activeSession.RemainingUiFrames--;
            if (activeSession.RemainingUiFrames > 0) return;

            if (activeSession.UiHandle == null || !activeSession.UiHandle.IsOpen)
            {
                FailActiveSession(
                    "failed_info_card_interrupted",
                    "info_open",
                    "InfoCard disappeared before auto-test requested close.");
                return;
            }

            activeSession.Phase = SessionPhase.WaitingForInfoCardClose;
            activeSession.CloseIssued = false;
            activeSession.RemainingUiFrames = CloseTimeoutRepaintFrames;
            Log.Message("[LogSyncAutoTest] InfoCard close phase entered for " + activeSession.Result.DefName);
        }

        private void ProcessWaitingForInfoCardClose()
        {
            if (activeSession == null) return;

            if (!IsInfoCardPresent())
            {
                CompleteActiveSession();
                return;
            }

            if (!activeSession.CloseIssued &&
                activeSession.UiHandle != null &&
                activeSession.UiHandle.IsOpen)
            {
                InfoCardCloseTrace.AddReason("CloseActionIssued.CloseXClick");
                activeSession.UiHandle.RequestClose();
                activeSession.CloseIssued = true;
                Log.Message("[LogSyncAutoTest] InfoCard close requested for " + activeSession.Result.DefName);
            }

            if (activeSession.CloseIssued &&
                activeSession.RemainingUiFrames == CloseTimeoutRepaintFrames / 2 &&
                activeSession.UiHandle != null &&
                activeSession.UiHandle.IsOpen)
            {
                InfoCardCloseTrace.AddReason("CloseFallback.ForceClose");
                activeSession.UiHandle.ForceClose();
            }

            activeSession.RemainingUiFrames--;
            if (activeSession.RemainingUiFrames > 0) return;

            FailActiveSession(
                "failed_info_card_close_timeout",
                "info_close",
                "InfoCard remained open after auto-test requested close.");
        }

        private bool IsInfoCardPresent()
        {
            if (activeSession?.UiHandle != null && activeSession.UiHandle.IsOpen)
            {
                return true;
            }

            Dialog_InfoCard currentInfoCard = Find.WindowStack?.WindowOfType<Dialog_InfoCard>();
            if (currentInfoCard == null) return false;

            activeSession.UiHandle = new AutoTestInfoCardHandle(currentInfoCard, activeSession.InfoCardDef);
            InfoCardCloseTrace.Attach(currentInfoCard);
            return true;
        }

        private void CleanupSelectionAndWindows()
        {
            try
            {
                if (activeSession?.UiHandle != null)
                {
                    activeSession.UiHandle.ForceClose();
                }
                else
                {
                    CloseExistingInfoCards();
                }
            }
            catch
            {
            }

            CleanupActiveSessionArtifacts();
            Find.Selector.ClearSelection();
        }

        private void CleanupActiveSessionArtifacts()
        {
            if (activeSession?.AttachedHediff == null || activeSession.TestPawn?.health == null)
            {
                return;
            }

            try
            {
                activeSession.TestPawn.health.RemoveHediff(activeSession.AttachedHediff);
            }
            catch
            {
            }
        }

        private void FailActiveSession(string status, string stage, string errorMessage)
        {
            if (activeSession == null) return;

            activeSession.Result.Status = status;
            activeSession.Result.Stage = stage;
            activeSession.Result.ErrorMessage = errorMessage;
            TryCaptureInfoCardScreenshot(true);
            activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "Trace=" + InfoCardCloseTrace.Describe());
            CleanupSelectionAndWindows();
            InfoCardCloseTrace.Stop();
            activeSession = null;
        }

        private void CompleteActiveSession()
        {
            if (activeSession == null) return;

            if (activeSession.Item.Kind == AutoTestKind.Recipe)
            {
                activeSession.Result.Stage = "produce";
                if (!TryProduceRecipe(activeSession, out string errorMessage))
                {
                    activeSession.Result.Status = "failed_recipe_produce";
                    activeSession.Result.ErrorMessage = errorMessage;
                    activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "Trace=" + InfoCardCloseTrace.Describe());
                    CleanupSelectionAndWindows();
                    InfoCardCloseTrace.Stop();
                    activeSession = null;
                    return;
                }
            }
            else if (activeSession.SpawnedThing != null && !activeSession.SpawnedThing.Destroyed)
            {
                activeSession.Result.Label = activeSession.SpawnedThing.LabelCap;
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "InfoCardOpened=True");
            }
            else if (activeSession.TerrainDef != null)
            {
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "InfoCardOpened=True");
            }
            else if (activeSession.AttachedHediff != null)
            {
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "InfoCardOpened=True");
            }

            activeSession.Result.Status = "passed";
            activeSession.Result.Stage = activeSession.Item.Kind == AutoTestKind.Recipe ? "produce" : "info_close";
            activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "Trace=" + InfoCardCloseTrace.Describe());
            Log.Message("[LogSyncAutoTest] InfoCard close confirmed for " + activeSession.Result.DefName);
            CleanupSelectionAndWindows();
            InfoCardCloseTrace.Stop();
            activeSession = null;
        }

        private void CloseExistingInfoCards()
        {
            Find.WindowStack.TryRemoveAssignableFromType(typeof(Dialog_InfoCard), false);
        }

        private void OpenInfoCardOnUiThread()
        {
            if (activeSession == null || activeSession.InfoCardDef == null || activeSession.UiHandle != null) return;

            if (activeSession.AttachedHediff != null)
            {
                activeSession.UiHandle = AutoTestInfoCardUiAdapter.Open(activeSession.AttachedHediff);
            }
            else
            {
                activeSession.UiHandle = activeSession.SpawnedThing != null
                    ? AutoTestInfoCardUiAdapter.Open(activeSession.SpawnedThing)
                    : AutoTestInfoCardUiAdapter.Open(activeSession.InfoCardDef);
            }

            InfoCardCloseTrace.Begin(activeSession.Result.DefName);
            InfoCardCloseTrace.Attach(Find.WindowStack.WindowOfType<Dialog_InfoCard>());
            InfoCardCloseTrace.AddReason("OpenRequested");

            Log.Message(string.Format(
                "[LogSyncAutoTest] InfoCard requested for {0} handle={1} windows={2} summary={3}",
                activeSession.Result.DefName,
                activeSession.UiHandle.DebugName,
                Find.WindowStack.Windows.Count,
                BuildWindowSummary()));
        }

        private void TryCaptureInfoCardScreenshot(bool force)
        {
            if (activeSession == null || screenshotService == null)
            {
                return;
            }

            screenshotService.TryCaptureInfoCard(
                activeSession.Result,
                ref activeSession.ScreenshotCaptured,
                activeSession.AllowScreenshots,
                force,
                results.Count,
                IsInfoCardPresent);
        }
    }
}
