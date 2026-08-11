using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class AutoTestRunner
    {
        private sealed class ThreeViewTestSession
        {
            private const float PausedCaptureSettleSeconds = 1f;
            private const int CaptureWindowTailTicks = 3;
            private const int StudioWidth = 21;
            private const int StudioHeight = 21;

            private readonly AutoTestRunner owner;

            public AutoTestThreeViewTargetSpec Spec;
            public AutoTestResult Result;
            public Map TestMap;
            public IntVec3 RootCell;
            public AutoTestThreeViewTargetContext Context;
            public StaticViewContext StudioSnapshot;
            public bool AllowScreenshots;
            public int WaitTicks;
            public ThreeViewTestPhase Phase;
            public TimeSpeed ResumeSpeed;
            public bool PauseBypassArmed;
            public float PausedCaptureStartRealtime;
            public bool PauseUiCaptureStarted;

            public ThreeViewTestSession(AutoTestRunner owner)
            {
                this.owner = owner;
            }

            public void Update()
            {
                try
                {
                    switch (Phase)
                    {
                        case ThreeViewTestPhase.SpawningTargets:
                            SpawnTargets();
                            break;
                        case ThreeViewTestPhase.PausingForCapture:
                            PauseForCapture();
                            break;
                        case ThreeViewTestPhase.CleaningUp:
                            Cleanup();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Fail("unexpected_error", ex.ToString());
                }
            }

            private void SpawnTargets()
            {
                if (TestMap == null || !Find.Maps.Contains(TestMap))
                {
                    Fail("map_missing", "ThreeView studio map was missing before spawn.");
                    return;
                }

                IntVec3 anchorCell = new IntVec3(RootCell.x + 10, 0, RootCell.z + 10);
                if (!owner.threeViewService.TryPopulateTriptych(TestMap, anchorCell, Spec, out AutoTestThreeViewTargetContext context, out string errorMessage))
                {
                    Fail("spawn_failed", errorMessage ?? "ThreeView targets failed to spawn.");
                    return;
                }

                Context = context;
                Result.Stage = "spawn";
                Result.Label = string.IsNullOrWhiteSpace(Spec.Label) ? Spec.DefName : Spec.Label;
                Result.OverlayLine1 = string.IsNullOrWhiteSpace(Spec.OverlayLine1)
                    ? (string.IsNullOrWhiteSpace(Spec.DefName) ? Result.DefName : Spec.DefName)
                    : Spec.OverlayLine1;
                Result.OverlayLine2 = string.IsNullOrWhiteSpace(Spec.OverlayLine2)
                    ? (string.IsNullOrWhiteSpace(Spec.Label) ? Result.Label : Spec.Label)
                    : Spec.OverlayLine2;
                Result.OverlayLine3 = string.IsNullOrWhiteSpace(Spec.OverlayLine3)
                    ? (string.IsNullOrWhiteSpace(Spec.PawnRaceLabel) ? string.Empty : "Race: " + Spec.PawnRaceLabel)
                    : Spec.OverlayLine3;
                Result.OverlayLine4 = string.IsNullOrWhiteSpace(Spec.OverlayLine4)
                    ? (string.IsNullOrWhiteSpace(Spec.PawnBodyTypeLabel) ? string.Empty : "BodyType: " + Spec.PawnBodyTypeLabel)
                    : Spec.OverlayLine4;
                Result.Notes = AppendNote(Result.Notes, "ThreeViewTriptych=True");
                Result.Notes = AppendNote(Result.Notes, "SpawnedPawns=" + Context.Pawns.Count);
                Result.Notes = AppendNote(Result.Notes, "SuppressedMissingEyeOverlay=True");
                Result.Notes = AppendNote(Result.Notes, "RegisteredThreeViewPawnCount=" + AutoTestThreeViewRenderSuppression.RegisteredCount);
                if (!string.IsNullOrWhiteSpace(Spec.PawnRaceDefName))
                {
                    Result.Notes = AppendNote(Result.Notes, "ThreeViewPawnRace=" + Spec.PawnRaceDefName);
                }

                if (!string.IsNullOrWhiteSpace(Spec.PawnBodyTypeDefName))
                {
                    Result.Notes = AppendNote(Result.Notes, "ThreeViewBodyType=" + Spec.PawnBodyTypeDefName);
                }
                if (Spec.TargetLifeStage != null)
                {
                    Result.OverlayLine4 = "Age: " + Context.ActualBiologicalAgeYears.ToString("0.###") + " / StageIndex=" + Context.ActualLifeStageIndex;
                    Result.Notes = AppendNote(Result.Notes, "TargetLifeStageDef=" + Spec.TargetLifeStage.defName);
                    Result.Notes = AppendNote(Result.Notes, "TargetLifeStageIndex=" + Spec.TargetLifeStageIndex);
                    Result.Notes = AppendNote(Result.Notes, "AgeGenerationMode=PawnGenerationRequest.FixedBiologicalAge");
                    Result.Notes = AppendNote(Result.Notes, "ActualLifeStageDef=" + (Context.ActualLifeStage?.defName ?? "null"));
                    Result.Notes = AppendNote(Result.Notes, "ActualLifeStageIndex=" + Context.ActualLifeStageIndex);
                    Result.Notes = AppendNote(Result.Notes, "ActualBiologicalAgeYears=" + Context.ActualBiologicalAgeYears.ToString("0.###"));
                    Result.Notes = AppendNote(Result.Notes, "LifeStageLockFallbackUsed=" + Context.LifeStageLockFallbackUsed);
                }
                if (Spec.ForceStripLoadout)
                {
                    bool naked = Context.Pawns.All(pawn =>
                        (pawn?.apparel?.WornApparel == null || pawn.apparel.WornApparel.Count == 0) &&
                        pawn?.equipment?.Primary == null);
                    Result.Notes = AppendNote(Result.Notes, "Naked=" + naked);
                    if (!naked)
                    {
                        Fail("loadout_cleanup_failed", "Life-stage presenter retained apparel or equipment.");
                        return;
                    }
                }
                if (Spec.TargetHairDef != null || Spec.TargetHeadTypeDef != null || Spec.TargetBodyTypeDef != null || Spec.TargetGeneDef != null || Spec.TargetXenotypeDef != null)
                {
                    Result.Notes = AppendNote(Result.Notes, "PresenterPawnKind=" + (Spec.PawnKindDef?.defName ?? "null"));
                    Result.Notes = AppendNote(Result.Notes, "PresenterRace=" + (Spec.RaceDef?.defName ?? "null"));
                    Result.Notes = AppendNote(Result.Notes, "PresenterGender=" + (Context.Pawns.FirstOrDefault()?.gender.ToString() ?? "null"));
                    Result.Notes = AppendNote(Result.Notes, "PresenterBodyType=" + (Context.Pawns.FirstOrDefault()?.story?.bodyType?.defName ?? "null"));
                    Result.Notes = AppendNote(Result.Notes, "VisualEffectDetected=" + Context.VisualEffectDetected);
                    if (Spec.TargetGeneDef != null)
                    {
                        Result.Notes = AppendNote(Result.Notes, "TargetGenePresent=" + Context.TargetGenePresent);
                        Result.Notes = AppendNote(Result.Notes, "TargetGeneLocation=" + (Context.TargetGeneLocation ?? "Missing"));
                        Result.Notes = AppendNote(Result.Notes, "TargetGeneCount=" + Context.TargetGeneCount);
                        Result.Notes = AppendNote(Result.Notes, "ActualTotalGeneCount=" + Context.ActualTotalGeneCount);
                        Result.Notes = AppendNote(Result.Notes, "TargetEndogeneCount=" + Context.TargetEndogeneCount);
                        Result.Notes = AppendNote(Result.Notes, "TargetXenogeneCount=" + Context.TargetXenogeneCount);
                        Result.Notes = AppendNote(Result.Notes, "DependencyGenesPresent=" + Context.DependencyGenesPresent);
                        Result.Notes = AppendNote(Result.Notes, "OnlyTargetAndDependencies=" + Context.OnlyTargetAndDependencies);
                        Result.Notes = AppendNote(Result.Notes, "GeneDependenciesActive=" + Context.GeneDependenciesActive);
                        Result.Notes = AppendNote(Result.Notes, "TargetGeneActive=" + Context.TargetGeneActive);
                        Result.Notes = AppendNote(Result.Notes, "TriptychGeneSetsMatch=" + Context.TriptychGeneSetsMatch);
                        if (!Context.TargetGenePresent || !Context.DependencyGenesPresent || !Context.OnlyTargetAndDependencies ||
                            !Context.GeneDependenciesActive || !Context.TargetGeneActive || !Context.TriptychGeneSetsMatch ||
                            Context.TargetGeneCount != 1 || Context.ActualTotalGeneCount != Context.ExpectedTotalGeneCount)
                        {
                            Fail("gene_expected_set_mismatch", "Triptych presenters did not retain exactly the target Gene and its prerequisite closure.");
                            return;
                        }
                    }

                    if (Spec.TargetXenotypeDef != null)
                    {
                        Result.Notes = AppendNote(Result.Notes, "TargetXenotypeMatches=" + Context.TargetXenotypeMatches);
                        Result.Notes = AppendNote(Result.Notes, "TargetXenotypeGeneSetMatches=" + Context.TargetXenotypeGeneSetMatches);
                        if (!Context.TargetXenotypeMatches || !Context.TargetXenotypeGeneSetMatches)
                        {
                            Fail("xenotype_apply_failed", "Target Xenotype or its GeneSet was not retained on the generated presenter.");
                            return;
                        }
                    }
                }
                if (!owner.threeViewService.TryPrimeDirectionalFacingForPausedCapture(Context, out string pausedCaptureError))
                {
                    Fail("motion_prime_failed", pausedCaptureError ?? "ThreeView paused capture directional facing could not be primed.");
                    return;
                }

                Result.Notes = AppendNote(Result.Notes, "ThreeViewPausedCapture=True");
                Log.Message("[LogSyncAutoTest] ThreeView paused capture primed: def=" + Spec.DefName + " tick=" + (Find.TickManager?.TicksGame ?? -1));
                Phase = ThreeViewTestPhase.PausingForCapture;
            }

            private void PauseForCapture()
            {
                owner.threeViewService.PrepareForCapture(Context);
                owner.threeViewService.Focus(TestMap, Context);

                TickManager tickManager = Find.TickManager;
                if (tickManager == null)
                {
                    Fail("pause_failed", "TickManager missing before paused capture.");
                    return;
                }

                if (!PauseBypassArmed)
                {
                    ResumeSpeed = tickManager.CurTimeSpeed;
                    AutoTestBootstrap.SetTemporaryPauseBypass(true);
                    PauseBypassArmed = true;
                }

                tickManager.CurTimeSpeed = TimeSpeed.Paused;
                Result.Notes = AppendNote(Result.Notes, "ThreeViewPauseTick=" + (Find.TickManager?.TicksGame ?? -1));
                Log.Message("[LogSyncAutoTest] ThreeView paused for capture: def=" + Spec.DefName + " tick=" + (Find.TickManager?.TicksGame ?? -1) + " resumeSpeed=" + ResumeSpeed);
                PausedCaptureStartRealtime = Time.realtimeSinceStartup;
                PauseUiCaptureStarted = true;
                Result.Notes = AppendNote(Result.Notes, "ThreeViewPausedCaptureSettleSeconds=" + PausedCaptureSettleSeconds.ToString("0.0"));
                owner.activeThreeViewUiSession = this;
                Phase = ThreeViewTestPhase.CleaningUp;
            }

            public bool ProcessUiFrame()
            {
                if (!PauseUiCaptureStarted)
                {
                    return true;
                }

                owner.threeViewService.PrepareForCapture(Context);
                owner.threeViewService.Focus(TestMap, Context);

                TickManager tickManager = Find.TickManager;
                if (tickManager == null)
                {
                    FailFromUi("pause_failed", "TickManager missing during paused UI capture.");
                    return true;
                }

                tickManager.CurTimeSpeed = TimeSpeed.Paused;
                float elapsed = Time.realtimeSinceStartup - PausedCaptureStartRealtime;
                if (elapsed < PausedCaptureSettleSeconds)
                {
                    return false;
                }

                if (!AutoTestScreenshotService.PrepareCaptureFrame())
                {
                    return false;
                }

                Result.Stage = "capture";
                Log.Message("[LogSyncAutoTest] ThreeView capture begin: def=" + Spec.DefName + " tick=" + (Find.TickManager?.TicksGame ?? -1) + " paused=" + (Find.TickManager?.Paused ?? false) + " elapsedRealtime=" + elapsed.ToString("0.000") + " allowScreenshots=" + AllowScreenshots + " rootCell=" + RootCell);
                bool captureRequested = AllowScreenshots;
                bool captured = owner.TryCaptureThreeViewScreenshot(this, false);
                RestoreTimeAfterCapture();
                if (captureRequested && !captured)
                {
                    FailFromUi("capture_failed", "ThreeView screenshot was requested but not captured.");
                    return true;
                }

                Result.Status = "passed";
                WaitTicks = CaptureWindowTailTicks;
                PauseUiCaptureStarted = false;
                return true;
            }

            private void Cleanup()
            {
                if (WaitTicks > 0)
                {
                    owner.threeViewService.PrepareForSettle(Context);
                    owner.threeViewService.Focus(TestMap, Context);
                    WaitTicks--;
                    return;
                }

                owner.RecordThreeViewSemanticBeforeCleanup(this);
                List<Pawn> trackedPawns = Context?.Pawns?.Where(pawn => pawn != null).ToList() ?? new List<Pawn>();
                owner.threeViewService.Cleanup(Context);
                bool targetsRemoved = trackedPawns.All(pawn => pawn.Destroyed || !pawn.Spawned);
                bool renderSuppressionCleared = trackedPawns.All(pawn => !AutoTestThreeViewRenderSuppression.IsRegistered(pawn));
                Result.Notes = AppendNote(Result.Notes, "ThreeViewRenderSuppressionCleared=" + renderSuppressionCleared);
                Context = null;
                bool studioRestored = owner.RestoreStaticViewContext(StudioSnapshot, Result);
                Result.Notes = AppendNote(Result.Notes, "ThreeViewStudioRestored=" + studioRestored);
                owner.RefreshThreeViewCooldown("cleanup");
                owner.RecordThreeViewCleanupSemantic(this, targetsRemoved, renderSuppressionCleared);
                if (!studioRestored && Result.Status == "passed")
                {
                    Result.Status = "failed";
                    Result.Stage = "cleanup";
                    Result.ErrorMessage = "ThreeView studio map state was not restored exactly.";
                }
                Phase = ThreeViewTestPhase.Done;
            }

            private void Fail(string stage, string message)
            {
                RestoreTimeAfterCapture();
                Result.Status = "failed";
                Result.Stage = stage;
                Result.ErrorMessage = message ?? string.Empty;
                owner.TryCaptureThreeViewScreenshot(this, true);
                Phase = ThreeViewTestPhase.CleaningUp;
            }

            public void FailFromUi(string stage, string message)
            {
                Fail(stage, message);
                PauseUiCaptureStarted = false;
            }

            private void RestoreTimeAfterCapture()
            {
                if (!PauseBypassArmed)
                {
                    return;
                }

                try
                {
                    TickManager tickManager = Find.TickManager;
                    if (tickManager != null)
                    {
                        tickManager.CurTimeSpeed = ResumeSpeed;
                    }
                }
                catch
                {
                }
                finally
                {
                    AutoTestBootstrap.SetTemporaryPauseBypass(false);
                    PauseBypassArmed = false;
                    Log.Message("[LogSyncAutoTest] ThreeView capture speed restored: def=" + Spec.DefName + " speed=" + ResumeSpeed + " tick=" + (Find.TickManager?.TicksGame ?? -1));
                    TickManager tickManager = Find.TickManager;
                    if (tickManager != null)
                    {
                        Log.Message("[LogSyncAutoTest] ThreeView restore observed state: def=" + Spec.DefName + " paused=" + tickManager.Paused + " forcePaused=" + tickManager.ForcePaused + " speed=" + tickManager.CurTimeSpeed);
                        if (tickManager.Paused || tickManager.ForcePaused)
                        {
                            Log.Warning("[LogSyncAutoTest] ThreeView restore left game paused: def=" + Spec.DefName + " paused=" + tickManager.Paused + " forcePaused=" + tickManager.ForcePaused + " speed=" + tickManager.CurTimeSpeed);
                        }
                    }
                }
            }

            public static ThreeViewTestSession Create(
                AutoTestRunner owner,
                AutoTestThreeViewTargetSpec spec,
                AutoTestResult result,
                bool allowScreenshots)
            {
                ThreeViewTestSession session = new ThreeViewTestSession(owner)
                {
                    Spec = spec,
                    Result = result,
                    AllowScreenshots = allowScreenshots,
                    TestMap = owner.map,
                    Phase = ThreeViewTestPhase.Initializing
                };

                if (!owner.TryAllocateThreeViewStudioRoot(StudioWidth, StudioHeight, out IntVec3 rootCell))
                {
                    result.Status = "failed";
                    result.Stage = "layout";
                    result.ErrorMessage = "No free studio layout space remained on the current map.";
                    session.Phase = ThreeViewTestPhase.Done;
                    return session;
                }

                StaticViewContext studioSnapshot = new StaticViewContext
                {
                    Map = owner.map,
                    RootCell = rootCell,
                    AnchorCell = new IntVec3(rootCell.x + 10, 0, rootCell.z + 10),
                    Profile = spec.Profile
                };
                if (!owner.TrySnapshotAndClearStaticViewArea(studioSnapshot, StudioWidth, StudioHeight, out string snapshotError))
                {
                    result.Status = "failed";
                    result.Stage = "snapshot";
                    result.ErrorMessage = snapshotError ?? "ThreeView studio snapshot failed.";
                    owner.RestoreStaticViewContext(studioSnapshot, result);
                    session.Phase = ThreeViewTestPhase.Done;
                    return session;
                }

                owner.threeViewService.PrepareStudioArea(owner.map, rootCell, StudioWidth, StudioHeight);
                session.RootCell = rootCell;
                session.StudioSnapshot = studioSnapshot;
                session.Result.Stage = "prepare_studio";
                session.Phase = ThreeViewTestPhase.SpawningTargets;
                return session;
            }
        }

        private void BeginThreeViewTestSession()
        {
            foreach (AutoTestThreeViewTargetSpec spec in threeViewService.ResolveDefaultTargets(targetMod, turretMountedGunDefs, RegisterDiscoveredCase))
            {
                if (spec == null)
                {
                    continue;
                }

                if (!MatchesThreeViewFilters(spec))
                {
                    continue;
                }

                if (!threeViewService.TryResolveAppearancePresenter(spec, out string appearanceSkipReason))
                {
                    spec.SkipReason = string.IsNullOrWhiteSpace(appearanceSkipReason)
                        ? "no_compatible_presenter"
                        : appearanceSkipReason;
                }

                pendingThreeViewTargets.Enqueue(spec);
            }
        }

        private bool MatchesThreeViewFilters(AutoTestThreeViewTargetSpec spec)
        {
            if (spec == null)
            {
                return false;
            }

            Def primaryDef = spec.TargetDef ?? (Def)spec.PawnKindDef;
            if (settings.UseXmlDrivenAutoTest())
            {
                return MatchesSpecFilters(spec.Kind, primaryDef);
            }

            if (settings.autoTestTargetDefNames == null || settings.autoTestTargetDefNames.Count == 0)
            {
                return true;
            }

            string[] aliases =
            {
                spec.DefName,
                spec.TargetDef?.defName,
                spec.PawnKindDef?.defName,
                spec.RaceDef?.defName,
                spec.ItemDef?.defName,
                spec.TargetLifeStage?.defName
            };
            return aliases.Any(alias => !string.IsNullOrWhiteSpace(alias) && settings.MatchesAutoTestTargetDefName(alias));
        }

        private bool TryAllocateThreeViewStudioRoot(int width, int height, out IntVec3 rootCell)
        {
            int safeWidth = Math.Max(1, width);
            int safeHeight = Math.Max(1, height);
            int maxX = Math.Max(10, map.Size.x - safeWidth - 10);
            int maxZ = Math.Max(10, map.Size.z - safeHeight - 10);

            for (int z = 10; z <= maxZ; z += 2)
            {
                for (int x = 10; x <= maxX; x += 2)
                {
                    IntVec3 candidate = new IntVec3(x, 0, z);
                    if (IsThreeViewStudioAreaUsable(candidate, safeWidth, safeHeight))
                    {
                        rootCell = candidate;
                        return true;
                    }
                }
            }

            rootCell = IntVec3.Invalid;
            return false;
        }

        private bool IsThreeViewStudioAreaUsable(IntVec3 rootCell, int width, int height)
        {
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    IntVec3 cell = new IntVec3(rootCell.x + x, 0, rootCell.z + z);
                    if (!cell.InBounds(map))
                    {
                        return false;
                    }

                    if (map.roofGrid.RoofAt(cell) != null)
                    {
                        return false;
                    }

                    foreach (Thing thing in cell.GetThingList(map))
                    {
                        if (thing == null || thing.Destroyed)
                        {
                            continue;
                        }

                        if (!(thing.def?.destroyable ?? false))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private void StartNewThreeViewSession(AutoTestThreeViewTargetSpec spec)
        {
            if (spec == null)
            {
                return;
            }

            if (activeThreeViewSessions.Count > 0)
            {
                Log.Warning("[LogSyncAutoTest] ThreeView start rejected because another session is still active. pending=" + pendingThreeViewTargets.Count + " active=" + activeThreeViewSessions.Count + " def=" + spec.DefName);
                pendingThreeViewTargets.Enqueue(spec);
                return;
            }

            string defType = spec.TargetDef?.GetType().Name ?? nameof(PawnKindDef);
            AutoTestResult result = CreateResult(spec.ManifestEntry, defType, spec.DefName, spec.Label);
            LogSyncAutoTestSpecDef matchedSpec = ResolveBestMatchingSpec(spec.Kind, spec.TargetDef ?? (Def)spec.PawnKindDef);
            bool allowScreenshots = ShouldCaptureScreenshots(matchedSpec);
            result.Notes = AppendNote(result.Notes, "AutoTestMode=" + (settings.UseXmlDrivenAutoTest() ? "XmlDriven" : "Automatic"));
            result.Notes = AppendNote(result.Notes, "ThreeViewProfile=" + (spec.Profile?.ProfileId ?? "Unknown"));
            if (!string.IsNullOrWhiteSpace(spec.CompatibilitySource))
            {
                result.Notes = AppendNote(result.Notes, "CompatibilitySource=" + spec.CompatibilitySource);
            }
            if (spec.TargetGeneDef != null)
            {
                result.Notes = AppendNote(result.Notes, "TargetGeneDef=" + spec.TargetGeneDef.defName);
                result.Notes = AppendNote(result.Notes, "GeneDependencyChain=" + (spec.GeneDependencyChain ?? "None"));
                result.Notes = AppendNote(result.Notes, "DependencyGeneCount=" + spec.TargetGeneDependencies.Count);
                result.Notes = AppendNote(result.Notes, "DependencyGenes=" + (spec.TargetGeneDependencies.Count == 0
                    ? "None"
                    : string.Join(",", spec.TargetGeneDependencies.Select(gene => gene.defName).ToArray())));
                result.Notes = AppendNote(result.Notes, "ExpectedTotalGeneCount=" + spec.ExpectedGeneSet.Count);
            }

            if (!string.IsNullOrWhiteSpace(spec.SkipReason))
            {
                result.Status = "skipped";
                result.Stage = "presenter_resolution";
                result.ErrorMessage = spec.SkipReason;
                result.Notes = AppendNote(result.Notes, "SkipReason=" + spec.SkipReason);
                RecordResult(result);
                return;
            }

            ThreeViewTestSession session = ThreeViewTestSession.Create(this, spec, result, allowScreenshots);
            if (session.Phase == ThreeViewTestPhase.Done)
            {
                RecordResult(result);
                return;
            }

            activeThreeViewSessions.Add(session);
        }

        private bool TryCaptureThreeViewScreenshot(ThreeViewTestSession session, bool force)
        {
            if (session == null || screenshotService == null || session.TestMap == null || !Find.Maps.Contains(session.TestMap))
            {
                return false;
            }

            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = session.AllowScreenshots,
                Force = force,
                IsFailureArtifact = force,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeThreeViewSessions.IndexOf(session)),
                StageSuffix = force
                    ? (session.Result?.Stage ?? "failed")
                    : (string.IsNullOrWhiteSpace(session.Spec?.ScreenshotStageSuffix) ? "triptych" : session.Spec.ScreenshotStageSuffix),
                RelativeSubdirectory = session.Spec?.RelativeScreenshotSubdirectory,
                Prepare = () =>
                {
                    threeViewService.PrepareForCapture(session.Context);
                    threeViewService.Focus(session.TestMap, session.Context);
                },
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap) && session.Context != null,
                Profile = session.Spec?.Profile,
                SuccessNoteKey = "ThreeViewScreenshotCaptured=True",
                FailureNoteKey = "ThreeViewFailureScreenshotCaptured=True"
            });
        }
    }
}
