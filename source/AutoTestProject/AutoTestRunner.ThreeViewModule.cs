using System;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class AutoTestRunner
    {
        private sealed class ThreeViewTestSession
        {
            private const int SettleTicks = 45;
            private const int MotionPrimeTimeoutTicks = 30;
            private const int StudioWidth = 21;
            private const int StudioHeight = 21;

            private readonly AutoTestRunner owner;

            public AutoTestThreeViewTargetSpec Spec;
            public AutoTestResult Result;
            public Map TestMap;
            public IntVec3 RootCell;
            public AutoTestThreeViewTargetContext Context;
            public bool AllowScreenshots;
            public int WaitTicks;
            public ThreeViewTestPhase Phase;

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
                        case ThreeViewTestPhase.WaitingForSettle:
                            UpdateWaitingForSettle();
                            break;
                        case ThreeViewTestPhase.WaitingForMotionPrime:
                            UpdateWaitingForMotionPrime();
                            break;
                        case ThreeViewTestPhase.CapturingScreenshot:
                            CaptureScreenshot();
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
                Result.OverlayLine3 = string.IsNullOrWhiteSpace(Spec.PawnRaceLabel) ? string.Empty : "PawnRace: " + Spec.PawnRaceLabel;
                Result.OverlayLine4 = string.IsNullOrWhiteSpace(Spec.PawnBodyTypeLabel) ? string.Empty : "BodyType: " + Spec.PawnBodyTypeLabel;
                Result.Notes = AppendNote(Result.Notes, "ThreeViewTriptych=True");
                Result.Notes = AppendNote(Result.Notes, "SpawnedPawns=" + Context.Pawns.Count);
                if (!string.IsNullOrWhiteSpace(Spec.PawnRaceDefName))
                {
                    Result.Notes = AppendNote(Result.Notes, "ThreeViewPawnRace=" + Spec.PawnRaceDefName);
                }

                if (!string.IsNullOrWhiteSpace(Spec.PawnBodyTypeDefName))
                {
                    Result.Notes = AppendNote(Result.Notes, "ThreeViewBodyType=" + Spec.PawnBodyTypeDefName);
                }
                WaitTicks = SettleTicks;
                Phase = ThreeViewTestPhase.WaitingForSettle;
            }

            private void UpdateWaitingForSettle()
            {
                if (WaitTicks > 0)
                {
                    owner.threeViewService.PrepareForSettle(Context);
                    owner.threeViewService.Focus(TestMap, Context);
                    WaitTicks--;
                    return;
                }

                if (!owner.threeViewService.TryPrimeDirectionalWalk(Context, out string errorMessage))
                {
                    Fail("motion_prime_failed", errorMessage ?? "ThreeView directional walk could not be primed.");
                    return;
                }

                WaitTicks = MotionPrimeTimeoutTicks;
                Phase = ThreeViewTestPhase.WaitingForMotionPrime;
            }

            private void UpdateWaitingForMotionPrime()
            {
                owner.threeViewService.PrepareForCapture(Context);
                owner.threeViewService.Focus(TestMap, Context);

                if (owner.threeViewService.IsReadyForCapture(Context, out string readinessDetails))
                {
                    Result.Notes = AppendNote(Result.Notes, "ThreeViewMotionReady=" + readinessDetails);
                    Phase = ThreeViewTestPhase.CapturingScreenshot;
                    return;
                }

                if (WaitTicks > 0)
                {
                    WaitTicks--;
                    return;
                }

                Fail("motion_timeout", "ThreeView targets did not reach capture walk state in time.");
            }

            private void CaptureScreenshot()
            {
                Result.Stage = "capture";
                bool captureRequested = AllowScreenshots;
                bool captured = owner.TryCaptureThreeViewScreenshot(this, false);
                if (captureRequested && !captured)
                {
                    Fail("capture_failed", "ThreeView screenshot was requested but not captured.");
                    return;
                }

                Result.Status = "passed";
                Phase = ThreeViewTestPhase.CleaningUp;
            }

            private void Cleanup()
            {
                owner.threeViewService.Cleanup(Context);
                Context = null;
                owner.ClearArea(RootCell, StudioWidth, StudioHeight);
                owner.threeViewService.PrepareStudioArea(TestMap, RootCell, StudioWidth, StudioHeight);
                Phase = ThreeViewTestPhase.Done;
            }

            private void Fail(string stage, string message)
            {
                Result.Status = "failed";
                Result.Stage = stage;
                Result.ErrorMessage = message ?? string.Empty;
                owner.TryCaptureThreeViewScreenshot(this, true);
                Phase = ThreeViewTestPhase.CleaningUp;
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

                owner.ClearArea(rootCell, StudioWidth, StudioHeight);
                owner.threeViewService.PrepareStudioArea(owner.map, rootCell, StudioWidth, StudioHeight);
                session.RootCell = rootCell;
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

                if (!MatchesSpecFilters(spec.Kind, spec.TargetDef ?? (Def)spec.PawnKindDef))
                {
                    continue;
                }

                pendingThreeViewTargets.Enqueue(spec);
            }
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

            string defType = spec.TargetDef?.GetType().Name ?? nameof(PawnKindDef);
            AutoTestResult result = CreateResult(spec.ManifestEntry, defType, spec.DefName, spec.Label);
            LogSyncAutoTestSpecDef matchedSpec = ResolveBestMatchingSpec(spec.Kind, spec.TargetDef ?? (Def)spec.PawnKindDef);
            bool allowScreenshots = ShouldCaptureScreenshots(matchedSpec);
            result.Notes = AppendNote(result.Notes, "AutoTestMode=" + (settings.UseXmlDrivenAutoTest() ? "XmlDriven" : "Automatic"));
            result.Notes = AppendNote(result.Notes, "ThreeViewProfile=" + (spec.Profile?.ProfileId ?? "Unknown"));

            ThreeViewTestSession session = ThreeViewTestSession.Create(this, spec, result, allowScreenshots);
            if (session.Phase == ThreeViewTestPhase.Done)
            {
                results.Add(result);
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
                StageSuffix = force ? (session.Result?.Stage ?? "failed") : "triptych",
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
