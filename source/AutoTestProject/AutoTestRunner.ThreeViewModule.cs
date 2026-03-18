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
                Result.Notes = AppendNote(Result.Notes, "ThreeViewTriptych=True");
                Result.Notes = AppendNote(Result.Notes, "SpawnedPawns=" + Context.Pawns.Count);
                WaitTicks = SettleTicks;
                Phase = ThreeViewTestPhase.WaitingForSettle;
            }

            private void UpdateWaitingForSettle()
            {
                if (WaitTicks > 0)
                {
                    owner.threeViewService.PrepareForCapture(Context);
                    owner.threeViewService.Focus(TestMap, Context);
                    WaitTicks--;
                    return;
                }

                Phase = ThreeViewTestPhase.CapturingScreenshot;
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

                if (!owner.TryAllocateThingRoot(StudioWidth, StudioHeight, out IntVec3 rootCell))
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
            foreach (AutoTestThreeViewTargetSpec spec in threeViewService.ResolveDefaultTargets(RegisterDiscoveredCase))
            {
                if (spec == null)
                {
                    continue;
                }

                if (!MatchesSpecFilters(spec.Kind, spec.PawnKindDef))
                {
                    continue;
                }

                pendingThreeViewTargets.Enqueue(spec);
            }
        }

        private void StartNewThreeViewSession(AutoTestThreeViewTargetSpec spec)
        {
            if (spec == null)
            {
                return;
            }

            AutoTestResult result = CreateResult(spec.ManifestEntry, nameof(PawnKindDef), spec.DefName, spec.Label);
            LogSyncAutoTestSpecDef matchedSpec = ResolveBestMatchingSpec(spec.Kind, spec.PawnKindDef);
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
