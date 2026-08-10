using System;
using System.Collections.Generic;
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
            WaitingForInfoCardClose,
            WaitingForRecipeBillConfigOpen,
            WaitingForRecipeBillConfigCapture,
            WaitingForRecipeBillConfigClose
        }

        private enum ActiveSessionMode
        {
            InfoCard,
            RecipeBill
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
            public ActiveSessionMode Mode;
            public Thing RecipeWorktableThing;
            public Bill_Production RecipeBill;
            public bool RecipeBillAdded;
            public IAutoTestUiHandle UiHandle;
            public bool CloseIssued;
            public bool ScreenshotCaptured;
            public bool AllowScreenshots;
            public int OpenStableRepaintFrames;
            public int RemainingUiFrames;
            public SessionPhase Phase;
            public InfoCardOperationAdapter EvidenceAdapter;
            public IDisposable EvidenceCaseScope;
            public AutoTestWaitHandle PreOpenWait;
            public AutoTestWaitHandle OpenWait;
            public AutoTestWaitHandle CloseWait;
            public AutoTestOperationEnvelope OpenOperation;
            public AutoTestOperationEnvelope CaptureOperation;
            public AutoTestOperationEnvelope FailureCaptureOperation;
            public AutoTestOperationEnvelope CloseOperation;
            public AutoTestOperationEnvelope ForceCloseOperation;
            public long CloseRequestedAtRealtimeMs;
            public bool OpenAssertionRecorded;
            public bool CloseAssertionRecorded;
            public readonly List<Dialog_InfoCard.InfoCardTab> RequiredInfoCardTabs = new List<Dialog_InfoCard.InfoCardTab>();
            public readonly List<Dialog_InfoCard.InfoCardTab> VerifiedInfoCardTabs = new List<Dialog_InfoCard.InfoCardTab>();
            public int InfoCardTabProbeIndex;
            public bool InfoCardTabAwaitingRepaint;
            public bool InfoCardTabsRestored;
            public bool RestoreTerrainAfterCleanup;
            public IntVec3 RestoreTerrainCell;
            public TerrainDef OriginalTerrain;
            public bool RestorePollutionAfterCleanup;
            public bool OriginalPollution;
        }

        public void ProcessUi()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            if (activeThreeViewUiSession != null)
            {
                AutoTestScreenshotOverlay.Request(activeThreeViewUiSession.Result);
            }

            if (activeStaticViewUiSession != null)
            {
                AutoTestScreenshotOverlay.Request(activeStaticViewUiSession.Result);
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

            if (activeStaticViewUiSession != null)
            {
                try
                {
                    ProcessStaticViewUiSession();
                }
                catch (Exception ex)
                {
                    activeStaticViewUiSession.FailFromUi("ui_capture_failed", ex.ToString());
                    activeStaticViewUiSession = null;
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
                    case SessionPhase.WaitingForRecipeBillConfigOpen:
                        ProcessWaitingForRecipeBillConfigOpen();
                        break;
                    case SessionPhase.WaitingForRecipeBillConfigCapture:
                        ProcessWaitingForRecipeBillConfigCapture();
                        break;
                    case SessionPhase.WaitingForRecipeBillConfigClose:
                        ProcessWaitingForRecipeBillConfigClose();
                        break;
                }
            }
            catch (Exception ex)
            {
                bool recipeMode = activeSession.Mode == ActiveSessionMode.RecipeBill;
                if (!recipeMode)
                {
                    string status = activeSession.Phase == SessionPhase.WaitingForInfoCardClose ? "failed_info_card_close_request" : "failed_info_card_open_request";
                    string stage = activeSession.Phase == SessionPhase.WaitingForInfoCardClose ? "info_close" : "info_open";
                    FailActiveSession(status, stage, ex.ToString());
                    return;
                }
                activeSession.Result.Status = recipeMode
                    ? (activeSession.Phase == SessionPhase.WaitingForRecipeBillConfigClose ? "failed_recipe_bill_config_close_request" : "failed_recipe_bill_config_open_request")
                    : (activeSession.Phase == SessionPhase.WaitingForInfoCardClose ? "failed_info_card_close_request" : "failed_info_card_open_request");
                activeSession.Result.Stage = recipeMode
                    ? (activeSession.Phase == SessionPhase.WaitingForRecipeBillConfigClose ? "recipe_close_bill_config" : "recipe_open_bill_config")
                    : (activeSession.Phase == SessionPhase.WaitingForInfoCardClose ? "info_close" : "info_open");
                activeSession.Result.ErrorMessage = ex.ToString();
                if (recipeMode)
                {
                    TryCaptureRecipeBillScreenshot(true);
                }
                else
                {
                    TryCaptureInfoCardScreenshot(true);
                    activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "Trace=" + InfoCardCloseTrace.Describe());
                }
                CleanupSelectionAndWindows();
                if (!recipeMode)
                {
                    InfoCardCloseTrace.Stop();
                }
                CompleteSemanticCase(activeSession.Result);
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

        private void ProcessStaticViewUiSession()
        {
            StaticViewTestSession session = activeStaticViewUiSession;
            if (session == null)
            {
                return;
            }

            if (session.ProcessUiFrame())
            {
                activeStaticViewUiSession = null;
            }
        }

        private void ProcessWaitingForPreOpenSettle()
        {
            if (activeSession == null) return;

            if (activeSession.EvidenceAdapter != null && activeSession.Mode == ActiveSessionMode.InfoCard)
            {
                if (activeSession.PreOpenWait == null)
                {
                    activeSession.PreOpenWait = activeSession.EvidenceAdapter.CreateWait(
                        "wait_pre_open_settle",
                        new AutoTestWaitOptions
                        {
                            ConditionId = "ui.pre_open_settled",
                            TimeoutMs = 10000,
                            MinimumPollIntervalTicks = 0,
                            RequiredStablePolls = PreOpenSettleRepaintFrames
                        },
                        delegate
                        {
                            return new AutoTestProbeResult { Satisfied = true, Message = "Repaint observed.", Snapshot = BuildWindowSummary() };
                        });
                }

                AutoTestWaitOutcome settle = activeSession.PreOpenWait.Poll(Find.TickManager?.TicksGame ?? Time.frameCount);
                if (!settle.Terminal) return;
                if (!settle.Satisfied)
                {
                    FailActiveSession("failed_info_card_pre_open_timeout", "wait_pre_open_settle", AutoTestWaitHandle.Describe(settle));
                    return;
                }

                activeSession.Phase = SessionPhase.WaitingForInfoCardOpen;
                Log.Message("[LogSyncAutoTest] UI pre-open settle completed for " + activeSession.Result.DefName + " mode=" + activeSession.Mode);
                return;
            }

            activeSession.RemainingUiFrames--;
            if (activeSession.RemainingUiFrames > 0) return;

            activeSession.OpenStableRepaintFrames = 0;
            activeSession.RemainingUiFrames = OpenTimeoutRepaintFrames;
            activeSession.Phase = activeSession.Mode == ActiveSessionMode.RecipeBill
                ? SessionPhase.WaitingForRecipeBillConfigOpen
                : SessionPhase.WaitingForInfoCardOpen;
            Log.Message("[LogSyncAutoTest] UI pre-open settle completed for " + activeSession.Result.DefName + " mode=" + activeSession.Mode);
        }

        private void ProcessWaitingForRecipeBillConfigOpen()
        {
            if (activeSession == null) return;

            if (activeSession.UiHandle == null)
            {
                OpenRecipeBillConfigOnUiThread();
                return;
            }

            if (IsRecipeBillConfigPresent())
            {
                activeSession.OpenStableRepaintFrames++;
                if (activeSession.OpenStableRepaintFrames >= OpenStableFrameRequirement)
                {
                    activeSession.Result.Stage = "recipe_capture_bill_config";
                    RecordInfoCardSemantic(true);
                    activeSession.Phase = SessionPhase.WaitingForRecipeBillConfigCapture;
                    activeSession.RemainingUiFrames = 2;
                }

                return;
            }

            activeSession.OpenStableRepaintFrames = 0;
            activeSession.RemainingUiFrames--;
            if (activeSession.RemainingUiFrames > 0) return;

            FailActiveSession(
                "failed_recipe_bill_config_open",
                "recipe_open_bill_config",
                "Recipe bill config did not stay open long enough to confirm opening. " + BuildWindowSummary());
        }

        private void ProcessWaitingForRecipeBillConfigCapture()
        {
            if (activeSession == null) return;

            if (!IsRecipeBillConfigPresent())
            {
                FailActiveSession(
                    "failed_recipe_bill_config_interrupted",
                    "recipe_open_bill_config",
                    "Recipe bill config disappeared before screenshot capture.");
                return;
            }

            activeSession.RemainingUiFrames--;
            if (activeSession.RemainingUiFrames > 0) return;

            TryCaptureRecipeBillScreenshot(false);
            activeSession.Phase = SessionPhase.WaitingForRecipeBillConfigClose;
            activeSession.CloseIssued = false;
            activeSession.RemainingUiFrames = CloseTimeoutRepaintFrames;
            activeSession.Result.Stage = "recipe_close_bill_config";
        }

        private void ProcessWaitingForRecipeBillConfigClose()
        {
            if (activeSession == null) return;

            if (!IsRecipeBillConfigPresent())
            {
                CompleteActiveSession();
                return;
            }

            if (!activeSession.CloseIssued && activeSession.UiHandle != null && activeSession.UiHandle.IsOpen)
            {
                activeSession.UiHandle.RequestClose();
                activeSession.CloseIssued = true;
            }

            if (activeSession.CloseIssued &&
                activeSession.RemainingUiFrames == CloseTimeoutRepaintFrames / 2 &&
                activeSession.UiHandle != null &&
                activeSession.UiHandle.IsOpen)
            {
                activeSession.UiHandle.ForceClose();
            }

            activeSession.RemainingUiFrames--;
            if (activeSession.RemainingUiFrames > 0) return;

            FailActiveSession(
                "failed_recipe_bill_config_close_timeout",
                "recipe_close_bill_config",
                "Recipe bill config remained open after auto-test requested close.");
        }

        private void ProcessWaitingForInfoCardOpen()
        {
            if (activeSession == null) return;

            if (activeSession.EvidenceAdapter != null)
            {
                if (activeSession.UiHandle == null)
                {
                    OpenInfoCardOnUiThread();
                    if (activeSession == null) return;
                }

                if (activeSession.OpenWait == null)
                {
                    activeSession.OpenWait = activeSession.EvidenceAdapter.CreateWait(
                        "wait_info_card_open",
                        new AutoTestWaitOptions { ConditionId = "window.info_card_open", TimeoutMs = 15000, MinimumPollIntervalTicks = 0, RequiredStablePolls = OpenStableFrameRequirement },
                        delegate
                        {
                            bool present = IsInfoCardPresent();
                            return new AutoTestProbeResult { Satisfied = present, Message = present ? "InfoCard visible." : "InfoCard not visible.", Snapshot = BuildWindowSummary() };
                        });
                }

                AutoTestWaitOutcome open = activeSession.OpenWait.Poll(Find.TickManager?.TicksGame ?? Time.frameCount);
                if (!open.Terminal) return;
                if (!open.Satisfied)
                {
                    activeSession.EvidenceAdapter.AssertTrue("info_card.stably_open", false, AutoTestWaitHandle.Describe(open), activeSession.OpenWait.OperationId);
                    activeSession.OpenAssertionRecorded = true;
                    FailActiveSession("failed_info_card_open", "wait_info_card_open", AutoTestWaitHandle.Describe(open));
                    return;
                }

                if (!activeSession.OpenAssertionRecorded)
                {
                    activeSession.EvidenceAdapter.AssertTrue("info_card.stably_open", true, AutoTestWaitHandle.Describe(open), activeSession.OpenWait.OperationId);
                    activeSession.OpenAssertionRecorded = true;
                }
                RecordInfoCardSemantic(true);
                InfoCardCloseTrace.AddReason("OpenConfirmed.StablePolls=" + open.StablePolls);
                activeSession.Phase = SessionPhase.WaitingForCaptureSettle;
                activeSession.RemainingUiFrames = 2;
                return;
            }

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
                    RecordInfoCardSemantic(true);
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

            if (ProcessInfoCardTabProbe())
            {
                return;
            }

            activeSession.RemainingUiFrames--;
            if (activeSession.RemainingUiFrames > 0) return;

            TryCaptureInfoCardScreenshot(false);
            activeSession.Phase = SessionPhase.WaitingBeforeClose;
            activeSession.RemainingUiFrames = WaitBeforeCloseRepaintFrames;
        }

        private bool ProcessInfoCardTabProbe()
        {
            if (activeSession == null || activeSession.RequiredInfoCardTabs.Count == 0)
            {
                return false;
            }

            Dialog_InfoCard infoCard = Find.WindowStack?.WindowOfType<Dialog_InfoCard>();
            if (infoCard == null)
            {
                return false;
            }

            if (activeSession.InfoCardTabAwaitingRepaint)
            {
                Dialog_InfoCard.InfoCardTab verified = activeSession.RequiredInfoCardTabs[activeSession.InfoCardTabProbeIndex - 1];
                if (!activeSession.VerifiedInfoCardTabs.Contains(verified))
                {
                    activeSession.VerifiedInfoCardTabs.Add(verified);
                }
                activeSession.InfoCardTabAwaitingRepaint = false;
            }

            if (activeSession.InfoCardTabProbeIndex < activeSession.RequiredInfoCardTabs.Count)
            {
                Dialog_InfoCard.InfoCardTab nextTab = activeSession.RequiredInfoCardTabs[activeSession.InfoCardTabProbeIndex++];
                infoCard.SetTab(nextTab);
                activeSession.InfoCardTabAwaitingRepaint = true;
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "TabProbe=" + nextTab);
                return true;
            }

            if (!activeSession.InfoCardTabsRestored)
            {
                infoCard.SetTab(Dialog_InfoCard.InfoCardTab.Stats);
                activeSession.InfoCardTabsRestored = true;
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "TabsVerified=" + string.Join(",", activeSession.VerifiedInfoCardTabs));
                return true;
            }

            return false;
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

            if (activeSession.EvidenceAdapter != null)
            {
                if (activeSession.CloseOperation == null && IsInfoCardPresent())
                {
                    activeSession.CloseOperation = activeSession.EvidenceAdapter.Run("request_close", delegate
                    {
                        if (activeSession.UiHandle == null || !activeSession.UiHandle.IsOpen)
                            throw new InvalidOperationException("InfoCard handle was not open when close was requested.");
                        InfoCardCloseTrace.AddReason("CloseActionIssued.CloseXClick");
                        activeSession.UiHandle.RequestClose();
                        activeSession.CloseIssued = true;
                        activeSession.CloseRequestedAtRealtimeMs = evidenceClock.RealtimeMilliseconds;
                    });
                    activeSession.EvidenceAdapter.AssertOperation("info_card.close_request", activeSession.CloseOperation);
                    if (!activeSession.CloseOperation.Success)
                    {
                        FailActiveSession("failed_info_card_close_request", "request_close", activeSession.CloseOperation.Error?.Message ?? "Close request failed.");
                        return;
                    }
                }

                if (activeSession.CloseOperation != null && activeSession.ForceCloseOperation == null && IsInfoCardPresent() &&
                    evidenceClock.RealtimeMilliseconds - activeSession.CloseRequestedAtRealtimeMs >= 500)
                {
                    activeSession.ForceCloseOperation = activeSession.EvidenceAdapter.Run("force_close_fallback", delegate
                    {
                        InfoCardCloseTrace.AddReason("CloseFallback.ForceClose");
                        activeSession.UiHandle?.ForceClose();
                    });
                    activeSession.EvidenceAdapter.AssertOperation("info_card.force_close_fallback", activeSession.ForceCloseOperation);
                    if (!activeSession.ForceCloseOperation.Success)
                    {
                        FailActiveSession("failed_info_card_close_request", "force_close_fallback", activeSession.ForceCloseOperation.Error?.Message ?? "Force close failed.");
                        return;
                    }
                }

                if (activeSession.CloseWait == null)
                {
                    activeSession.CloseWait = activeSession.EvidenceAdapter.CreateWait(
                        "wait_info_card_closed",
                        new AutoTestWaitOptions { ConditionId = "window.info_card_closed", TimeoutMs = 15000, MinimumPollIntervalTicks = 0, RequiredStablePolls = 1 },
                        delegate
                        {
                            bool present = IsInfoCardPresent();
                            return new AutoTestProbeResult { Satisfied = !present, Message = present ? "InfoCard still visible." : "InfoCard closed.", Snapshot = BuildWindowSummary() };
                        });
                }

                AutoTestWaitOutcome closed = activeSession.CloseWait.Poll(Find.TickManager?.TicksGame ?? Time.frameCount);
                if (!closed.Terminal) return;
                if (!closed.Satisfied)
                {
                    activeSession.EvidenceAdapter.AssertTrue("info_card.closed", false, AutoTestWaitHandle.Describe(closed), activeSession.CloseWait.OperationId);
                    activeSession.CloseAssertionRecorded = true;
                    FailActiveSession("failed_info_card_close_timeout", "wait_info_card_closed", AutoTestWaitHandle.Describe(closed));
                    return;
                }

                activeSession.EvidenceAdapter.AssertTrue("info_card.closed", true, AutoTestWaitHandle.Describe(closed), activeSession.CloseWait.OperationId);
                activeSession.CloseAssertionRecorded = true;
                CompleteActiveSession();
                return;
            }

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

        private bool IsRecipeBillConfigPresent()
        {
            if (activeSession?.UiHandle != null && activeSession.UiHandle.IsOpen)
            {
                return true;
            }

            Dialog_BillConfig currentDialog = Find.WindowStack?.WindowOfType<Dialog_BillConfig>();
            if (currentDialog == null) return false;

            activeSession.UiHandle = new AutoTestBillConfigHandle(currentDialog, activeSession.RecipeBill);
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
                    CloseExistingRecipeWindows();
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
            if (activeSession?.RecipeBillAdded == true &&
                activeSession.RecipeWorktableThing is IBillGiver billGiver &&
                activeSession.RecipeBill != null)
            {
                try
                {
                    billGiver.BillStack?.Delete(activeSession.RecipeBill);
                }
                catch
                {
                }
            }

            if (activeSession?.SpawnedThing != null && !activeSession.SpawnedThing.Destroyed)
            {
                try
                {
                    activeSession.SpawnedThing.Destroy(DestroyMode.Vanish);
                }
                catch
                {
                }
            }

            if (activeSession?.SpawnedThing != null)
            {
                bool targetRemoved = activeSession.SpawnedThing.Destroyed || !activeSession.SpawnedThing.Spawned;
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "TargetRemoved=" + targetRemoved);
                activeSession.EvidenceAdapter?.AssertTrue("cleanup.target_removed", targetRemoved, "Expected destroyed or despawned; actual=" + (activeSession.SpawnedThing.Destroyed ? "destroyed" : (activeSession.SpawnedThing.Spawned ? "spawned" : "despawned")));
            }

            if (activeSession?.RestorePollutionAfterCleanup == true &&
                activeSession.RestoreTerrainCell.IsValid &&
                activeSession.RestoreTerrainCell.InBounds(map) &&
                map.pollutionGrid != null)
            {
                try
                {
                    map.pollutionGrid.SetPolluted(activeSession.RestoreTerrainCell, activeSession.OriginalPollution, silent: true);
                    bool pollutionRestored = map.pollutionGrid.IsPolluted(activeSession.RestoreTerrainCell) == activeSession.OriginalPollution;
                    activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "PollutionRestored=" + pollutionRestored);
                    activeSession.EvidenceAdapter?.AssertTrue("cleanup.pollution_restored", pollutionRestored, "Expected=" + activeSession.OriginalPollution + " Actual=" + map.pollutionGrid.IsPolluted(activeSession.RestoreTerrainCell));
                }
                catch
                {
                }
            }

            if (activeSession?.RestoreTerrainAfterCleanup == true &&
                activeSession.OriginalTerrain != null &&
                activeSession.RestoreTerrainCell.IsValid &&
                activeSession.RestoreTerrainCell.InBounds(map))
            {
                try
                {
                    map.terrainGrid.SetTerrain(activeSession.RestoreTerrainCell, activeSession.OriginalTerrain);
                    bool terrainRestored = map.terrainGrid.TerrainAt(activeSession.RestoreTerrainCell) == activeSession.OriginalTerrain;
                    activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "TerrainRestored=" + terrainRestored);
                    activeSession.EvidenceAdapter?.AssertTrue("cleanup.terrain_restored", terrainRestored, "Expected=" + activeSession.OriginalTerrain.defName + " Actual=" + (map.terrainGrid.TerrainAt(activeSession.RestoreTerrainCell)?.defName ?? "null"));
                }
                catch
                {
                }
            }

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
            RecordInfoCardSemantic(false);
            CleanupSelectionAndWindows();
            RecordInfoCardClosedSemantic();
            InfoCardCloseTrace.Stop();
            FinalizeInfoCardEvidence(false, errorMessage);
            // Close the semantic cursor at the actual InfoCard terminal state. The result
            // is already present in the legacy results list, so do not add it a second time.
            CompleteSemanticCase(activeSession.Result);
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
                    RecordInfoCardSemantic(false);
                    CleanupSelectionAndWindows();
                    RecordInfoCardClosedSemantic();
                    InfoCardCloseTrace.Stop();
                    FinalizeInfoCardEvidence(false, errorMessage);
                    CompleteSemanticCase(activeSession.Result);
                    activeSession = null;
                    return;
                }
            }
            else if (activeSession.SpawnedThing != null && !activeSession.SpawnedThing.Destroyed)
            {
                if (!IsPawnInfoCardKind(activeSession.Item.Kind))
                {
                    activeSession.Result.Label = activeSession.SpawnedThing.LabelCap;
                }
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
            else if (activeSession.InfoCardDef != null)
            {
                activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "InfoCardOpened=True");
            }

            activeSession.Result.Status = "passed";
            activeSession.Result.Stage = activeSession.Item.Kind == AutoTestKind.Recipe ? "produce" : "info_close";
            activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "Trace=" + InfoCardCloseTrace.Describe());
            Log.Message("[LogSyncAutoTest] InfoCard close confirmed for " + activeSession.Result.DefName);
            CleanupSelectionAndWindows();
            RecordInfoCardClosedSemantic();
            InfoCardCloseTrace.Stop();
            FinalizeInfoCardEvidence(true, "InfoCard case completed.");
            CompleteSemanticCase(activeSession.Result);
            activeSession = null;
        }

        private void CloseExistingInfoCards()
        {
            Find.WindowStack.TryRemoveAssignableFromType(typeof(Dialog_InfoCard), false);
        }

        private void CloseExistingRecipeWindows()
        {
            Find.WindowStack.TryRemoveAssignableFromType(typeof(Dialog_BillConfig), false);
        }

        private void OpenInfoCardOnUiThread()
        {
            if (activeSession == null || activeSession.InfoCardDef == null || activeSession.UiHandle != null) return;

            if (activeSession.EvidenceAdapter != null)
            {
                ActiveSession session = activeSession;
                session.OpenOperation = session.EvidenceAdapter.Run("open_info_card", delegate
                {
                    if (session.AttachedHediff != null)
                        session.UiHandle = AutoTestInfoCardUiAdapter.Open(session.AttachedHediff);
                    else
                        session.UiHandle = session.SpawnedThing != null
                            ? AutoTestInfoCardUiAdapter.Open(session.SpawnedThing)
                            : AutoTestInfoCardUiAdapter.Open(session.InfoCardDef);

                    InfoCardCloseTrace.Begin(session.Result.DefName);
                    InfoCardCloseTrace.Attach(Find.WindowStack.WindowOfType<Dialog_InfoCard>());
                    InfoCardCloseTrace.AddReason("OpenRequested");
                    Log.Message("[LogSyncAutoTest] InfoCard requested for " + session.Result.DefName + " handle=" + session.UiHandle.DebugName + " windows=" + Find.WindowStack.Windows.Count + " summary=" + BuildWindowSummary());
                });
                session.EvidenceAdapter.AssertOperation("info_card.open_request", session.OpenOperation);
                if (!session.OpenOperation.Success)
                    FailActiveSession("failed_info_card_open_request", "open_info_card", session.OpenOperation.Error?.Message ?? "InfoCard open request failed.");
                return;
            }

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

        private void OpenRecipeBillConfigOnUiThread()
        {
            if (activeSession == null || activeSession.RecipeWorktableThing == null || activeSession.UiHandle != null)
            {
                return;
            }

            if (!(activeSession.RecipeWorktableThing is IBillGiver billGiver))
            {
                throw new InvalidOperationException("Recipe worktable does not implement IBillGiver.");
            }

            Find.Selector.ClearSelection();
            Find.Selector.Select(activeSession.RecipeWorktableThing, false, false);

            if (activeSession.RecipeBill == null)
            {
                Bill_Production bill = BillUtility.MakeNewBill(activeSession.Item.RecipeDef, null) as Bill_Production ?? new Bill_Production(activeSession.Item.RecipeDef, null);
                billGiver.BillStack.AddBill(bill);
                activeSession.RecipeBill = bill;
                activeSession.RecipeBillAdded = true;
            }

            activeSession.Result.Stage = "recipe_open_bill_config";
            activeSession.UiHandle = AutoTestBillConfigUiAdapter.Open(activeSession.RecipeBill, activeSession.RecipeWorktableThing.Position);
            activeSession.Result.Notes = AppendNote(activeSession.Result.Notes, "BillConfigOpened=True");
        }

        private void TryCaptureInfoCardScreenshot(bool force)
        {
            if (activeSession == null || screenshotService == null)
            {
                return;
            }

            if (activeSession.EvidenceAdapter != null)
            {
                ActiveSession session = activeSession;
                AutoTestOperationEnvelope captureOperation = null;
                if (session.CaptureOperation == null)
                {
                    captureOperation = session.EvidenceAdapter.Run("capture_info_card", delegate
                    {
                        bool capturedFlag = session.ScreenshotCaptured;
                        bool captured = screenshotService.TryCaptureInfoCard(session.Result, ref capturedFlag, session.AllowScreenshots, force, results.Count, IsInfoCardPresent);
                        session.ScreenshotCaptured = capturedFlag;
                        return captured;
                    }, captured => captured ? "screenshot_requested" : "screenshot_not_requested");
                    session.CaptureOperation = captureOperation;
                }
                else if (force && !session.ScreenshotCaptured && session.FailureCaptureOperation == null)
                {
                    captureOperation = session.EvidenceAdapter.Run("capture_info_card_failure", delegate
                    {
                        bool capturedFlag = session.ScreenshotCaptured;
                        bool captured = screenshotService.TryCaptureInfoCard(session.Result, ref capturedFlag, session.AllowScreenshots, true, results.Count, IsInfoCardPresent);
                        session.ScreenshotCaptured = capturedFlag;
                        return captured;
                    }, captured => captured ? "failure_screenshot_requested" : "failure_screenshot_not_requested");
                    session.FailureCaptureOperation = captureOperation;
                }

                if (captureOperation != null)
                {
                    if (captureOperation.Success && session.ScreenshotCaptured && !string.IsNullOrEmpty(session.Result.ScreenshotPath))
                        session.EvidenceAdapter.AddCapture("InfoCard", session.Result.ScreenshotPath, captureOperation.OperationId, session.Result.DefName);
                    return;
                }
            }

            screenshotService.TryCaptureInfoCard(activeSession.Result, ref activeSession.ScreenshotCaptured, activeSession.AllowScreenshots, force, results.Count, IsInfoCardPresent);
        }

        private void FinalizeInfoCardEvidence(bool success, string message)
        {
            if (activeSession?.EvidenceAdapter == null) return;
            if (!activeSession.OpenAssertionRecorded)
            {
                activeSession.EvidenceAdapter.AssertTrue("info_card.stably_open", success, message);
                activeSession.OpenAssertionRecorded = true;
            }
            if (!activeSession.CloseAssertionRecorded)
            {
                activeSession.EvidenceAdapter.AssertTrue("info_card.closed", success, message);
                activeSession.CloseAssertionRecorded = true;
            }
            activeSession.EvidenceAdapter.CompleteCase(success, message);
            activeSession.EvidenceCaseScope?.Dispose();
            activeSession.EvidenceCaseScope = null;
        }

        private void TryCaptureRecipeBillScreenshot(bool force)
        {
            if (activeSession == null || screenshotService == null)
            {
                return;
            }

            screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = activeSession.Result,
                AllowCapture = activeSession.AllowScreenshots,
                Force = force,
                ResultIndex = results.Count,
                Profile = AutoTestScreenshotProfiles.InfoCard,
                SuccessNoteKey = "RecipeBillScreenshotCaptured=True",
                FailureNoteKey = "RecipeBillFailureScreenshotCaptured=True",
                Guard = IsRecipeBillConfigPresent
            });
            activeSession.ScreenshotCaptured = true;
        }
    }
}
