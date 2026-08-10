using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class AutoTestRunner
    {
        private enum CombatPawnTestPhase
        {
            Initializing,
            WaitingForMap,
            SpawningTarget,
            SpawningEnemies,
            Fighting,
            CleaningUp,
            Done
        }

        private sealed class CombatPawnTestSession
        {
            private const int MapReadyWaitTicks = 180;
            private const int FightDurationTicks = 60 * 60;
            private const int ScreenshotFocusWaitTicks = 6;
            private const string TargetMarkerHediffDefName = "LogSync_CombatPawnTargetMarker";

            private readonly AutoTestRunner owner;
            private int lastEnemyAttackOrderTick = -9999;
            private float initialHealthPercent = 1f;
            private int screenshotRetryCount = 2;

            public AutoTestCombatPawnTargetSpec Spec;
            public AutoTestResult Result;
            public Map TestMap;
            public MapParent Parent;
            public Pawn TargetPawn;
            public readonly List<Pawn> EnemyPawns = new List<Pawn>();
            public CombatPawnTestPhase Phase;
            public int TimeoutTicks = 60 * 120;
            public int WaitTicks;
            public int FightTicksRemaining;
            public bool AllowScreenshots;
            public bool FirstHitObserved;
            public bool FirstHitScreenshotPending;
            public bool FirstHitScreenshotCaptured;
            public int FirstHitTick = -1;
            public int HitScreenshotDelayTicks;
            public AutoTestPawnSnapshot InitialSnapshot;

            public CombatPawnTestSession(AutoTestRunner owner)
            {
                this.owner = owner;
            }

            public void Update()
            {
                if (TimeoutTicks > 0 && --TimeoutTicks == 0)
                {
                    LogCombatSnapshot("timeout");
                    Fail("timeout", "Total session timeout reached.");
                    return;
                }

                try
                {
                    if (!EnsureMapAlive("tick_" + Phase))
                    {
                        return;
                    }

                    switch (Phase)
                    {
                        case CombatPawnTestPhase.Initializing:
                            WaitTicks = MapReadyWaitTicks;
                            Phase = CombatPawnTestPhase.WaitingForMap;
                            break;
                        case CombatPawnTestPhase.WaitingForMap:
                            UpdateWaitingForMap();
                            break;
                        case CombatPawnTestPhase.SpawningTarget:
                            SpawnTargetPawn();
                            break;
                        case CombatPawnTestPhase.SpawningEnemies:
                            SpawnEnemies(5);
                            break;
                        case CombatPawnTestPhase.Fighting:
                            UpdateFighting();
                            break;
                        case CombatPawnTestPhase.CleaningUp:
                            CleanupAndClose();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Fail("exception", ex.ToString());
                }
            }

            public Pawn ResolvePrimaryEnemy()
            {
                Pawn trackedTarget = ResolveTrackedTargetPawn();
                return EnemyPawns
                    .Where(pawn => pawn != null && !pawn.Destroyed && pawn.Spawned && pawn.Map == TestMap && !pawn.Dead)
                    .OrderBy(pawn => pawn.Downed ? 1 : 0)
                    .ThenBy(pawn => pawn.Position.DistanceToSquared(trackedTarget?.Position ?? IntVec3.Zero))
                    .FirstOrDefault();
            }

            private bool EnsureMapAlive(string stageTag)
            {
                if (owner.pocketMapService.IsAlive(TestMap, Parent))
                {
                    return true;
                }

                Result.Status = "failed";
                Result.Stage = stageTag;
                Result.ErrorMessage = "Combat test pocket map disappeared unexpectedly.";
                Phase = CombatPawnTestPhase.Done;
                return false;
            }

            private void UpdateWaitingForMap()
            {
                if (TestMap != null && Find.Maps.Contains(TestMap))
                {
                    Phase = CombatPawnTestPhase.SpawningTarget;
                    return;
                }

                if (WaitTicks > 0)
                {
                    WaitTicks--;
                    return;
                }

                Fail("map_not_ready", "Map failed to stabilize in Find.Maps after " + MapReadyWaitTicks + " ticks.");
            }

            private void SpawnTargetPawn()
            {
                if (!owner.combatPawnService.TrySpawnConfiguredPawn(TestMap, Spec, out Pawn pawn, out string errorMessage))
                {
                    Fail("failed_target_spawn", errorMessage ?? "Combat target pawn could not be created.");
                    return;
                }

                TargetPawn = pawn;
                IntVec3 spawnPos = new IntVec3(Mathf.RoundToInt(TestMap.Size.x * 0.375f), 0, TestMap.Size.z / 2);
                GenSpawn.Spawn(TargetPawn, spawnPos, TestMap);
                ApplyTargetMarkerHediff(TargetPawn);
                ConfigureTestPawnCombatStats(TargetPawn);
                TargetPawn.Name = new NameSingle("自动测试目标");
                if (TargetPawn.drafter != null)
                {
                    TargetPawn.drafter.Drafted = false;
                    TargetPawn.drafter.FireAtWill = true;
                }

                initialHealthPercent = TargetPawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
                InitialSnapshot = AutoTestPawnProbe.Capture(TargetPawn);
                Result.Stage = "spawn_target";
                Result.Notes = AppendNote(Result.Notes, "TargetPawn=" + (TargetPawn.kindDef?.defName ?? "Unknown"));
                Log.Message("[LogSyncAutoTest] CombatPawn target spawned: " + BuildTargetSummary(TargetPawn));
                Phase = CombatPawnTestPhase.SpawningEnemies;
            }

            private void SpawnEnemies(int count)
            {
                List<IntVec3> spawnCells = FindEnemySpawnCells(count);
                if (spawnCells.Count == 0)
                {
                    Fail("failed_enemy_spawn_pos", "No valid enemy spawn cells were found for combat pawn test.");
                    return;
                }

                foreach (IntVec3 spawnCell in spawnCells)
                {
                    try
                    {
                        PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("SpaceRefugee")
                            ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Colonist")
                            ?? PawnKindDefOf.WildMan;
                        Faction faction = Faction.OfAncientsHostile ?? Faction.OfPirates ?? Faction.OfMechanoids;
                        PawnGenerationRequest request = new PawnGenerationRequest(
                            kind,
                            faction,
                            PawnGenerationContext.NonPlayer,
                            TestMap.Tile,
                            forceGenerateNewPawn: true,
                            canGeneratePawnRelations: false,
                            mustBeCapableOfViolence: true);
                        Pawn pawn = PawnGenerator.GeneratePawn(request);
                        if (pawn == null)
                        {
                            continue;
                        }

                        pawn.apparel?.DestroyAll();
                        pawn.equipment?.DestroyAllEquipment();
                        GenSpawn.Spawn(pawn, spawnCell, TestMap);
                        HediffDef assaulterHediff = DefDatabase<HediffDef>.GetNamedSilentFail("LogSync_CombatAssaulter");
                        if (assaulterHediff != null)
                        {
                            HealthUtility.AdjustSeverity(pawn, assaulterHediff, 1f);
                        }

                        EnemyPawns.Add(pawn);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[LogSyncAutoTest] Combat enemy spawn failed for '" + (Spec?.DefName ?? "unknown") + "': " + ex);
                    }
                }

                if (EnemyPawns.Count == 0)
                {
                    Fail("failed_enemy_spawn", "No enemies were spawned for the combat pawn test.");
                    return;
                }

                Result.Stage = "fighting_60s";
                Result.Notes = AppendNote(Result.Notes, "EnemyCount=" + EnemyPawns.Count);
                Log.Message("[LogSyncAutoTest] CombatPawn enemies spawned: targetCount=" + GetTargetCount() +
                    " targets=[" + BuildCurrentTargetList() + "] enemyCount=" + EnemyPawns.Count +
                    " enemies=[" + BuildEnemyList() + "]");
                FightTicksRemaining = FightDurationTicks;
                Phase = CombatPawnTestPhase.Fighting;
            }

            private List<IntVec3> FindEnemySpawnCells(int count)
            {
                List<IntVec3> result = new List<IntVec3>();
                if (TestMap == null || TargetPawn == null || count <= 0)
                {
                    return result;
                }

                CellRect safeRect = CellRect.FromLimits(5, 5, TestMap.Size.x - 6, TestMap.Size.z - 6);
                IntVec3 anchor = new IntVec3(Mathf.RoundToInt(TestMap.Size.x * 0.625f), 0, TestMap.Size.z / 2);
                int[] lateralOffsets = { 0, -1, 1, -2, 2, -3, 3, -4, 4 };

                foreach (int offset in lateralOffsets)
                {
                    if (result.Count >= count)
                    {
                        break;
                    }

                    IntVec3 candidate = anchor + new IntVec3(0, 0, offset);
                    if (!candidate.InBounds(TestMap) || !safeRect.Contains(candidate) || !candidate.Walkable(TestMap))
                    {
                        continue;
                    }

                    result.Add(candidate);
                }

                return result;
            }

            private void UpdateFighting()
            {
                Pawn trackedTarget = ResolveTrackedTargetPawn();
                MaintainEnemyAttackOrders();
                UpdateFirstHitScreenshot();
                ObserveFirstDamage();

                if (trackedTarget == null || trackedTarget.Destroyed || trackedTarget.Dead || trackedTarget.Downed)
                {
                    Log.Message("[LogSyncAutoTest] CombatPawn finish condition met: " +
                        "def=" + (Spec?.DefName ?? "unknown") +
                        " judgeTarget=" + BuildTargetSummary(trackedTarget) +
                        " targetCount=" + GetTargetCount() +
                        " targets=[" + BuildCurrentTargetList() + "]" +
                        " enemyCount=" + EnemyPawns.Count +
                        " enemies=[" + BuildEnemyList() + "]");
                    Result.Status = "passed";
                    if (trackedTarget != null && trackedTarget.Downed && !trackedTarget.Dead)
                    {
                        Result.Stage = "target_downed";
                        Result.Notes = AppendNote(Result.Notes, "CombatOutcome=TargetDowned");
                    }
                    else
                    {
                        Result.Stage = "target_dead";
                        Result.Notes = AppendNote(Result.Notes, "CombatOutcome=TargetDead");
                    }
                    if (FirstHitObserved && !FirstHitScreenshotCaptured)
                    {
                        owner.TryCaptureCombatPawnHitScreenshot(this, force: true);
                    }
                    Phase = CombatPawnTestPhase.CleaningUp;
                    return;
                }

                if (FightTicksRemaining > 0)
                {
                    FightTicksRemaining--;
                    if (FightTicksRemaining % 600 == 0)
                    {
                        Result.Stage = "fighting_" + (FightTicksRemaining / 60) + "s";
                    }
                    return;
                }

                Result.Status = "passed";
                Result.Stage = "survived_60s";
                Result.Notes = AppendNote(Result.Notes, "CombatOutcome=Survived60Seconds");
                Log.Message("[LogSyncAutoTest] CombatPawn survived full duration: " +
                    "def=" + (Spec?.DefName ?? "unknown") +
                    " judgeTarget=" + BuildTargetSummary(trackedTarget) +
                    " targetCount=" + GetTargetCount() +
                    " targets=[" + BuildCurrentTargetList() + "]" +
                    " enemyCount=" + EnemyPawns.Count +
                    " enemies=[" + BuildEnemyList() + "]");
                Phase = CombatPawnTestPhase.CleaningUp;
            }

            private void ObserveFirstDamage()
            {
                Pawn trackedTarget = ResolveTrackedTargetPawn();
                if (FirstHitObserved || trackedTarget == null || trackedTarget.Destroyed || trackedTarget.health == null)
                {
                    return;
                }

                float currentHealthPercent = trackedTarget.health.summaryHealth?.SummaryHealthPercent ?? 1f;
                if (currentHealthPercent >= initialHealthPercent - 0.0001f)
                {
                    return;
                }

                FirstHitObserved = true;
                FirstHitTick = Find.TickManager.TicksGame;
                Result.Notes = AppendNote(Result.Notes, "FirstDamageReceived=True");
                Result.Notes = AppendNote(Result.Notes, "FirstDamageTick=" + FirstHitTick);
                if (AllowScreenshots)
                {
                    FirstHitScreenshotPending = true;
                    HitScreenshotDelayTicks = ScreenshotFocusWaitTicks;
                }
            }

            private void UpdateFirstHitScreenshot()
            {
                if (!FirstHitScreenshotPending || FirstHitScreenshotCaptured)
                {
                    return;
                }

                if (HitScreenshotDelayTicks > 0)
                {
                    owner.FocusCombatPawnHit(this);
                    HitScreenshotDelayTicks--;
                    return;
                }

                FirstHitScreenshotCaptured = owner.TryCaptureCombatPawnHitScreenshot(this, force: false);
                if (FirstHitScreenshotCaptured)
                {
                    FirstHitScreenshotPending = false;
                    return;
                }

                if (screenshotRetryCount > 0)
                {
                    screenshotRetryCount--;
                    HitScreenshotDelayTicks = ScreenshotFocusWaitTicks;
                    return;
                }

                FirstHitScreenshotPending = false;
            }

            private void MaintainEnemyAttackOrders()
            {
                Pawn trackedTarget = ResolveTrackedTargetPawn();
                if (trackedTarget == null || trackedTarget.Destroyed || !trackedTarget.Spawned || trackedTarget.Map != TestMap)
                {
                    return;
                }

                int currentTick = Find.TickManager.TicksGame;
                if (currentTick - lastEnemyAttackOrderTick < 30)
                {
                    return;
                }

                foreach (Pawn enemy in EnemyPawns.Where(pawn => pawn != null && !pawn.Destroyed && pawn.Spawned && pawn.Map == TestMap && !pawn.Dead))
                {
                    if (enemy.CurJob != null &&
                        enemy.CurJob.def == JobDefOf.AttackMelee &&
                        enemy.CurJob.targetA.Thing == trackedTarget)
                    {
                        continue;
                    }

                    Job attackJob = JobMaker.MakeJob(JobDefOf.AttackMelee, trackedTarget);
                    attackJob.expiryInterval = 1200;
                    enemy.jobs?.TryTakeOrderedJob(attackJob, null, false);
                }

                lastEnemyAttackOrderTick = currentTick;
            }

            private Pawn ResolveTrackedTargetPawn()
            {
                if (HasTargetMarkerHediff(TargetPawn))
                {
                    return TargetPawn;
                }

                Pawn resolvedPawn = TestMap?.mapPawns?.AllPawnsSpawned?
                    .FirstOrDefault(HasTargetMarkerHediff);
                if (resolvedPawn != null)
                {
                    TargetPawn = resolvedPawn;
                }

                return TargetPawn;
            }

            private static bool HasTargetMarkerHediff(Pawn pawn)
            {
                return pawn != null &&
                       !pawn.Destroyed &&
                       pawn.health?.hediffSet?.HasHediff(DefDatabase<HediffDef>.GetNamedSilentFail(TargetMarkerHediffDefName)) == true;
            }

            private static void ApplyTargetMarkerHediff(Pawn pawn)
            {
                if (pawn?.health == null)
                {
                    return;
                }

                HediffDef markerHediff = DefDatabase<HediffDef>.GetNamedSilentFail(TargetMarkerHediffDefName);
                if (markerHediff == null || pawn.health.hediffSet?.HasHediff(markerHediff) == true)
                {
                    return;
                }

                HealthUtility.AdjustSeverity(pawn, markerHediff, 1f);
            }

            private void CleanupAndClose()
            {
                owner.RecordCombatPawnSemanticBeforeCleanup(this);
                try
                {
                    if (TargetPawn != null && !TargetPawn.Destroyed)
                    {
                        TargetPawn.Destroy(DestroyMode.Vanish);
                    }
                }
                catch
                {
                }

                foreach (Pawn enemy in EnemyPawns)
                {
                    try
                    {
                        if (enemy != null && !enemy.Destroyed)
                        {
                            enemy.Destroy(DestroyMode.Vanish);
                        }
                    }
                    catch
                    {
                    }
                }

                owner.DestroyPocketMap(TestMap, Parent);
                owner.RecordCombatPawnCleanupSemantic(this);
                Phase = CombatPawnTestPhase.Done;
            }

            private void Fail(string stage, string message)
            {
                LogCombatSnapshot("fail_" + stage);
                Result.Status = "failed";
                Result.Stage = stage;
                Result.ErrorMessage = message ?? string.Empty;
                owner.TryCaptureCombatPawnFailureScreenshot(this);
                Phase = CombatPawnTestPhase.CleaningUp;
            }

            public string BuildDebugStatus()
            {
                Pawn trackedTarget = ResolveTrackedTargetPawn();
                return "def=" + (Spec?.DefName ?? "unknown") +
                    " targetCount=" + GetTargetCount() +
                    " targets=[" + BuildCurrentTargetList() + "]" +
                    " enemyCount=" + EnemyPawns.Count +
                    " enemies=[" + BuildEnemyList() + "]" +
                    " judgeTarget=" + BuildTargetSummary(trackedTarget);
            }

            private void LogCombatSnapshot(string reason)
            {
                Log.Message("[LogSyncAutoTest] CombatPawn snapshot(" + reason + "): " + BuildDebugStatus());
            }

            private int GetTargetCount()
            {
                return TestMap?.mapPawns?.AllPawnsSpawned?.Count(HasTargetMarkerHediff) ?? (HasTargetMarkerHediff(TargetPawn) ? 1 : 0);
            }

            private string BuildCurrentTargetList()
            {
                IEnumerable<Pawn> targets = TestMap?.mapPawns?.AllPawnsSpawned?.Where(HasTargetMarkerHediff) ?? Enumerable.Empty<Pawn>();
                string[] entries = targets.Select(BuildTargetSummary).ToArray();
                if (entries.Length == 0 && TargetPawn != null)
                {
                    return BuildTargetSummary(TargetPawn);
                }

                return entries.Length == 0 ? "none" : string.Join("; ", entries);
            }

            private string BuildEnemyList()
            {
                List<string> entries = EnemyPawns
                    .Where(pawn => pawn != null)
                    .Select(BuildTargetSummary)
                    .ToList();
                return entries.Count == 0 ? "none" : string.Join("; ", entries);
            }

            private static string BuildTargetSummary(Pawn pawn)
            {
                if (pawn == null)
                {
                    return "null";
                }

                string label = pawn.LabelShortCap;
                string kind = pawn.kindDef?.defName ?? "UnknownKind";
                string name = pawn.Name?.ToStringShort ?? label;
                bool marked = HasTargetMarkerHediff(pawn);
                bool downed = pawn.Downed;
                bool dead = pawn.Dead;
                bool spawned = pawn.Spawned;
                bool destroyed = pawn.Destroyed;
                return string.Format(
                    "{0}/{1}(kind={2},marked={3},downed={4},dead={5},spawned={6},destroyed={7})",
                    label,
                    name,
                    kind,
                    marked,
                    downed,
                    dead,
                    spawned,
                    destroyed);
            }

            public static CombatPawnTestSession Create(
                AutoTestRunner owner,
                AutoTestCombatPawnTargetSpec spec,
                AutoTestResult result,
                bool allowScreenshots)
            {
                CombatPawnTestSession session = new CombatPawnTestSession(owner)
                {
                    Spec = spec,
                    Result = result,
                    AllowScreenshots = allowScreenshots,
                    Phase = CombatPawnTestPhase.Initializing
                };

                AutoTestPocketMapHandle handle = owner.CreatePocketMapHandle(
                    "LogSync_AutoTestPocketMap",
                    new IntVec3(50, 1, 50),
                    generatedMap => MapGenerator.PlayerStartSpot = generatedMap.Center,
                    out string errorMessage);
                if (handle == null || handle.Map == null)
                {
                    result.Status = "failed_map_generation";
                    result.Stage = "prepare_map";
                    result.ErrorMessage = errorMessage ?? "Failed to create combat pocket map.";
                    session.Phase = CombatPawnTestPhase.Done;
                    return session;
                }

                session.Parent = handle.Parent;
                session.TestMap = handle.Map;
                return session;
            }
        }

        private void BeginCombatPawnTestSession()
        {
            try
            {
                foreach (AutoTestCombatPawnTargetSpec spec in combatPawnService.ResolveDefaultTargets(targetMod, RegisterDiscoveredCase))
                {
                    if (spec == null)
                    {
                        continue;
                    }

                    Def filterDef = spec.TargetDef ?? (Def)spec.PawnKindDef;
                    if (!MatchesSpecFilters(spec.Kind, filterDef))
                    {
                        continue;
                    }

                    if (spec.ManifestEntry == null)
                    {
                        spec.ManifestEntry = RegisterDiscoveredCase(spec.CaseType, filterDef?.GetType().Name ?? nameof(Def), filterDef?.defName, spec.Label);
                    }

                    pendingCombatPawnTargets.Enqueue(spec);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[LogSyncAutoTest] Combat pawn queue preparation failed: " + ex);
            }
        }

        private void StartNewCombatPawnSession(AutoTestCombatPawnTargetSpec spec)
        {
            if (spec == null)
            {
                return;
            }

            try
            {
                AutoTestResult result = CreateResult(spec.ManifestEntry, spec.TargetDef?.GetType().Name ?? nameof(Def), spec.DefName, spec.Label);
                result.OverlayLine3 = string.IsNullOrWhiteSpace(spec.PawnRaceLabel) ? string.Empty : "PawnRace: " + spec.PawnRaceLabel;
                result.OverlayLine4 = string.IsNullOrWhiteSpace(spec.PawnBodyTypeLabel) ? string.Empty : "BodyType: " + spec.PawnBodyTypeLabel;
                result.Notes = AppendNote(result.Notes, "AutoTestMode=" + (settings.UseXmlDrivenAutoTest() ? "XmlDriven" : "Automatic"));
                result.Notes = AppendNote(result.Notes, "CombatKind=" + spec.Kind);
                if (!string.IsNullOrWhiteSpace(spec.PawnRaceDefName))
                {
                    result.Notes = AppendNote(result.Notes, "CombatPawnRace=" + spec.PawnRaceDefName);
                }
                if (!string.IsNullOrWhiteSpace(spec.PawnBodyTypeDefName))
                {
                    result.Notes = AppendNote(result.Notes, "CombatBodyType=" + spec.PawnBodyTypeDefName);
                }

                LogSyncAutoTestSpecDef specDef = ResolveBestMatchingSpec(spec.Kind, spec.TargetDef ?? (Def)spec.PawnKindDef);
                if (specDef != null)
                {
                    result.Notes = AppendNote(result.Notes, "Spec=" + specDef.defName);
                }

                CombatPawnTestSession session = CombatPawnTestSession.Create(this, spec, result, ShouldCaptureScreenshots(specDef));
                if (session.Phase == CombatPawnTestPhase.Done)
                {
                    results.Add(result);
                    return;
                }

                activeCombatPawnSessions.Add(session);
            }
            catch (Exception ex)
            {
                AutoTestResult result = CreateResult(spec.ManifestEntry, spec.TargetDef?.GetType().Name ?? nameof(Def), spec.DefName, spec.Label);
                result.Status = "failed_session_start";
                result.Stage = "prepare_session";
                result.ErrorMessage = ex.ToString();
                results.Add(result);
                Log.Error("[LogSyncAutoTest] Combat pawn session start failed for '" + spec.DefName + "': " + ex);
            }
        }

        private bool TryCaptureCombatPawnHitScreenshot(CombatPawnTestSession session, bool force)
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
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeCombatPawnSessions.IndexOf(session)),
                StageSuffix = "first_hit",
                Prepare = () => FocusCombatPawnHit(session),
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                Profile = AutoTestScreenshotProfiles.CombatPawnHit,
                SuccessNoteKey = "CombatPawnHitScreenshotCaptured=True",
                FailureNoteKey = "CombatPawnFailureScreenshotCaptured=True"
            });
        }

        private bool TryCaptureCombatPawnFailureScreenshot(CombatPawnTestSession session)
        {
            if (session == null || screenshotService == null || session.TestMap == null || !Find.Maps.Contains(session.TestMap))
            {
                return false;
            }

            return screenshotService.TryCapture(new AutoTestScreenshotRequest
            {
                Result = session.Result,
                AllowCapture = true,
                Force = true,
                IsFailureArtifact = true,
                ResultIndex = screenshotService.ResolveTurretResultIndex(results, activeCombatPawnSessions.IndexOf(session)),
                StageSuffix = session.Result?.Status ?? "failed",
                Prepare = () =>
                {
                    if (!FocusCombatPawnHit(session))
                    {
                        FocusPocketMapOverview(session.TestMap);
                    }
                },
                Guard = () => session.TestMap != null && Find.Maps.Contains(session.TestMap),
                Profile = AutoTestScreenshotProfiles.CombatPawnHit,
                SuccessNoteKey = "CombatPawnHitScreenshotCaptured=True",
                FailureNoteKey = "CombatPawnFailureScreenshotCaptured=True"
            });
        }

        private bool FocusCombatPawnHit(CombatPawnTestSession session)
        {
            if (session?.TestMap == null || !Find.Maps.Contains(session.TestMap))
            {
                return false;
            }

            try
            {
                if (Current.Game != null)
                {
                    Current.Game.CurrentMap = session.TestMap;
                }

                CameraDriver cameraDriver = Find.CameraDriver;
                if (cameraDriver == null)
                {
                    return false;
                }

                Vector3 center;
                Pawn targetPawn = session.TargetPawn;
                Pawn primaryEnemy = session.ResolvePrimaryEnemy();
                if (targetPawn != null && !targetPawn.Destroyed && primaryEnemy != null && !primaryEnemy.Destroyed)
                {
                    center = (targetPawn.DrawPos + primaryEnemy.DrawPos) * 0.5f;
                }
                else if (targetPawn != null && !targetPawn.Destroyed)
                {
                    center = targetPawn.DrawPos;
                }
                else
                {
                    center = session.TestMap.Center.ToVector3Shifted();
                }

                cameraDriver.SetRootPosAndSize(
                    new Vector3(center.x, AutoTestScreenshotProfiles.CombatPawnHit.CameraHeight, center.z),
                    AutoTestScreenshotProfiles.CombatPawnHit.FixedRootSize ?? 11f);
                return true;
            }
            catch (Exception ex)
            {
                session.Result.Notes = AppendNote(session.Result.Notes, "CombatScreenshotFocusError=" + TrimForNote(ex.Message));
                return false;
            }
        }
    }
}
