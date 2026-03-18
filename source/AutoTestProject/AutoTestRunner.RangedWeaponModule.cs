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
        private sealed class RangedWeaponTestSession
        {
            private const int MapReadyWaitTicks = 180;
            private const int FightDurationTicks = 60 * 60;
            private const int ObservationTicks = 60 * 10;
            private const int PostDamageObservationTicks = 60 * 30;
            private const int ProjectileCenteredScreenshotTargetCount = 2;
            private const int ProjectileScreenshotGraceTicks = 20 * 60;
            private const int VirtualProjectilePrimeTick = 5;
            private const int ScreenshotFocusWaitTicks = 6;
            private const int ScreenshotWaitTicks = 10;

            private readonly AutoTestRunner owner;
            private readonly Queue<StageScreenshotRequest> pendingStageScreenshots = new Queue<StageScreenshotRequest>();
            private int pendingStageScreenshotDelayTicks;
            private int lastAttackOrderTick = -9999;

            public Map TestMap;
            public ThingDef WeaponDef;
            public Pawn ShooterPawn;
            public ThingWithComps EquippedWeapon;
            public readonly List<Pawn> EnemyPawns = new List<Pawn>();
            public readonly Dictionary<int, float> EnemyInitialHealthPercent = new Dictionary<int, float>();
            public RangedWeaponTestPhase Phase;
            public AutoTestResult Result;
            public int TimeoutTicks;
            public int DelayTicks;
            public int WaitTicks;
            public MapParent Parent;
            public string CleanupReason = "initialized";
            public int FirstKillTick = -1;
            public string FirstKillPawnLabel = string.Empty;
            public int FirstDamageTick = -1;
            public string FirstDamagePawnLabel = string.Empty;
            public bool FinalScreenshotRequested;
            public bool FinalScreenshotForce;
            public bool FinalScreenshotCaptured;
            public bool AllowScreenshots;
            public bool CaptureProjectileScreenshots;
            public List<int> ProjectileScreenshotTicks = new List<int>();
            public bool ProjectileShotDetected;
            public int ProjectileShotTick = -1;
            public int NextProjectileScreenshotIndex;
            public bool ProjectileScreenshotPrimed;
            public int ProjectileScreenshotGraceTicksRemaining = -1;
            public bool PostHitResultRecorded;
            public readonly Dictionary<int, Vector3> ProjectileLastPositions = new Dictionary<int, Vector3>();
            public readonly Dictionary<int, int> ProjectileLastTicks = new Dictionary<int, int>();
            public StageScreenshotRequest ActiveStageScreenshot;

            public RangedWeaponTestSession(AutoTestRunner owner, ThingDef weaponDef, AutoTestResult result)
            {
                this.owner = owner;
                WeaponDef = weaponDef;
                Result = result;
                Phase = RangedWeaponTestPhase.Initializing;
                TimeoutTicks = 60 * 120;
            }

            public void Update()
            {
                if (TimeoutTicks > 0 && --TimeoutTicks == 0)
                {
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
                        case RangedWeaponTestPhase.Initializing:
                            WaitTicks = MapReadyWaitTicks;
                            LogMapState("initializing");
                            Phase = RangedWeaponTestPhase.WaitingForMap;
                            break;
                        case RangedWeaponTestPhase.WaitingForMap:
                            UpdateWaitingForMap();
                            break;
                        case RangedWeaponTestPhase.SpawningShooter:
                            SpawnShooterPawn();
                            if (ShooterPawn == null)
                            {
                                Fail("failed_shooter_spawn", "Shooter pawn could not be created.");
                                return;
                            }

                            PlaceShooterWaterRing();
                            Result.Stage = "spawn_shooter";
                            Phase = RangedWeaponTestPhase.EquippingWeapon;
                            break;
                        case RangedWeaponTestPhase.EquippingWeapon:
                            EquipWeapon();
                            if (EquippedWeapon == null)
                            {
                                Fail("failed_weapon_equip", "Weapon could not be equipped.");
                                return;
                            }

                            Result.Stage = "equip_weapon";
                            Phase = RangedWeaponTestPhase.SpawningEnemies;
                            break;
                        case RangedWeaponTestPhase.SpawningEnemies:
                            SpawnEnemies(5);
                            if (EnemyPawns.Count == 0)
                            {
                                Fail("failed_enemy_spawn", "No enemies were spawned for the ranged weapon test.");
                                return;
                            }

                            EnsureShooterAttacking();
                            Result.Stage = "fighting_60s";
                            DelayTicks = FightDurationTicks;
                            Phase = RangedWeaponTestPhase.Fighting;
                            break;
                        case RangedWeaponTestPhase.Fighting:
                            UpdateFightingPhase();
                            break;
                        case RangedWeaponTestPhase.WaitingAfterHit:
                            UpdateObservationPhase();
                            break;
                        case RangedWeaponTestPhase.CapturingScreenshot:
                            UpdateFinalScreenshotPhase();
                            break;
                        case RangedWeaponTestPhase.CleaningUp:
                            CleanupAndClose();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Fail("exception", ex.ToString());
                }
            }

            private void UpdateWaitingForMap()
            {
                if (TestMap != null && Find.Maps.Contains(TestMap))
                {
                    LogMapState("map_ready");
                    Phase = RangedWeaponTestPhase.SpawningShooter;
                    return;
                }

                if (WaitTicks > 0)
                {
                    if (WaitTicks == MapReadyWaitTicks || WaitTicks % 60 == 0)
                    {
                        LogMapState("waiting_for_map");
                    }

                    WaitTicks--;
                    return;
                }

                Fail("map_not_ready", "Map failed to stabilize in Find.Maps after " + MapReadyWaitTicks + " ticks.");
            }

            private void SpawnShooterPawn()
            {
                if (Faction.OfPlayer == null || TestMap == null)
                {
                    return;
                }

                for (int attempt = 0; attempt < 5 && ShooterPawn == null; attempt++)
                {
                    var request = new PawnGenerationRequest(
                        PawnKindDefOf.Colonist,
                        Faction.OfPlayer,
                        PawnGenerationContext.NonPlayer,
                        TestMap.Tile,
                        forceGenerateNewPawn: true,
                        canGeneratePawnRelations: false,
                        mustBeCapableOfViolence: true);
                    Pawn candidate = PawnGenerator.GeneratePawn(request);
                    if (candidate == null)
                    {
                        continue;
                    }

                    bool violentDisabled = candidate.WorkTagIsDisabled(WorkTags.Violent);
                    bool canManipulate = candidate.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) ?? false;
                    bool canSee = candidate.health?.capacities?.CapableOf(PawnCapacityDefOf.Sight) ?? false;
                    if (violentDisabled || !canManipulate || !canSee)
                    {
                        candidate.Destroy();
                        continue;
                    }

                    ShooterPawn = candidate;
                }

                if (ShooterPawn == null)
                {
                    return;
                }

                IntVec3 spawnPos = new IntVec3(Mathf.RoundToInt(TestMap.Size.x * 0.375f), 0, TestMap.Size.z / 2);
                GenSpawn.Spawn(ShooterPawn, spawnPos, TestMap);
                ConfigureTestPawnCombatStats(ShooterPawn);
                ShooterPawn.Name = new NameSingle("自动测试射手");
                if (ShooterPawn.drafter != null)
                {
                    ShooterPawn.drafter.Drafted = true;
                    ShooterPawn.drafter.FireAtWill = true;
                }

                Result.Label = ShooterPawn.LabelCap;
                Result.Notes = AppendNote(Result.Notes, "ShooterPawnSpawned=True");
            }

            private void PlaceShooterWaterRing()
            {
                if (ShooterPawn == null || TestMap == null)
                {
                    return;
                }

                TerrainDef deepWater = TerrainDefOf.WaterDeep ?? TerrainDefOf.WaterOceanDeep;
                if (deepWater == null)
                {
                    deepWater = DefDatabase<TerrainDef>.GetNamedSilentFail("WaterDeep") ??
                        DefDatabase<TerrainDef>.GetNamedSilentFail("WaterOceanDeep");
                }

                if (deepWater == null)
                {
                    Result.Notes = AppendNote(Result.Notes, "DeepWaterMissing=True");
                    return;
                }

                foreach (IntVec3 cell in GenAdj.CellsAdjacent8Way(ShooterPawn))
                {
                    if (!cell.InBounds(TestMap))
                    {
                        continue;
                    }

                    foreach (Thing thing in cell.GetThingList(TestMap).ToList())
                    {
                        if (thing == null || thing.Destroyed || thing == ShooterPawn)
                        {
                            continue;
                        }

                        thing.Destroy(DestroyMode.Vanish);
                    }

                    TestMap.terrainGrid.SetTerrain(cell, deepWater);
                }

                TestMap.terrainGrid.SetTerrain(ShooterPawn.Position, TerrainDefOf.Soil);
                Result.Notes = AppendNote(Result.Notes, "DeepWaterRing=True");
            }

            private void EquipWeapon()
            {
                if (ShooterPawn == null || WeaponDef == null)
                {
                    return;
                }

                Thing createdThing = ThingMaker.MakeThing(WeaponDef, owner.ResolveStuff(WeaponDef));
                EquippedWeapon = createdThing as ThingWithComps;
                if (EquippedWeapon == null)
                {
                    return;
                }

                ShooterPawn.equipment?.MakeRoomFor(EquippedWeapon);
                ShooterPawn.equipment?.AddEquipment(EquippedWeapon);
                ReloadEquippedWeapon();
                Result.Label = EquippedWeapon.LabelCap;
                Result.Notes = AppendNote(Result.Notes, "EquippedWeapon=" + EquippedWeapon.def.defName);
            }

            private void ReloadEquippedWeapon()
            {
                if (EquippedWeapon == null)
                {
                    return;
                }

                try
                {
                    var refuelable = EquippedWeapon.TryGetComp<CompRefuelable>();
                    if (refuelable != null)
                    {
                        refuelable.Refuel(refuelable.Props.fuelCapacity);
                    }

                    var ammoComp = EquippedWeapon.AllComps?.FirstOrDefault(comp => comp != null && comp.GetType().Name.Contains("CompAmmoUser"));
                    if (ammoComp == null)
                    {
                        return;
                    }

                    var type = ammoComp.GetType();
                    var selectedAmmo = type.GetProperty("SelectedAmmo")?.GetValue(ammoComp, null);
                    if (selectedAmmo != null)
                    {
                        type.GetProperty("CurrentAmmo")?.SetValue(ammoComp, selectedAmmo, null);
                    }

                    var magSizeProperty = type.GetProperty("MagSize");
                    if (magSizeProperty == null)
                    {
                        return;
                    }

                    object magSizeValue = magSizeProperty.GetValue(ammoComp, null);
                    if (magSizeValue != null)
                    {
                        type.GetProperty("CurMagCount")?.SetValue(ammoComp, (int)magSizeValue, null);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[LogSyncAutoTest] Weapon ammo reload reflection failed for {WeaponDef.defName}: {ex.Message}");
                }
            }

            private void SpawnEnemies(int count)
            {
                float range = ResolveWeaponRange();
                List<IntVec3> spawnCells = FindEnemySpawnCells(count, range);
                if (spawnCells.Count == 0)
                {
                    Fail("failed_enemy_spawn_pos", "No valid enemy spawn cells were found within weapon range and edge-safe area.");
                    return;
                }

                for (int i = 0; i < count && i < spawnCells.Count; i++)
                {
                    try
                    {
                        var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("SpaceRefugee") ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Colonist") ?? PawnKindDefOf.WildMan;
                        var faction = Faction.OfAncientsHostile ?? Faction.OfMechanoids;
                        var request = new PawnGenerationRequest(
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

                        GenSpawn.Spawn(pawn, spawnCells[i], TestMap);
                        EnemyInitialHealthPercent[pawn.thingIDNumber] = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
                        HediffDef targetHediff = DefDatabase<HediffDef>.GetNamedSilentFail("LogSync_TurretTestTarget");
                        if (targetHediff != null)
                        {
                            HealthUtility.AdjustSeverity(pawn, targetHediff, 1f);
                        }

                        EnemyPawns.Add(pawn);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[LogSyncAutoTest] Enemy spawn CRITICAL failed for ranged weapon '{WeaponDef.defName}': {ex}");
                    }
                }

                Result.Notes = AppendNote(Result.Notes, "EnemyCount=" + EnemyPawns.Count);
            }

            private float ResolveWeaponRange()
            {
                Verb verb = ResolveAttackVerb(ResolvePrimaryTarget());
                if (verb?.verbProps != null && verb.verbProps.range > 0f)
                {
                    return verb.verbProps.range;
                }

                VerbProperties verbProps = WeaponDef?.Verbs?
                    .Where(value => value != null && value.range > 1f)
                    .OrderByDescending(value => value.range)
                    .FirstOrDefault();
                return verbProps?.range ?? 15f;
            }

            private List<IntVec3> FindEnemySpawnCells(int count, float range)
            {
                List<IntVec3> result = new List<IntVec3>();
                if (TestMap == null || ShooterPawn == null || count <= 0)
                {
                    return result;
                }

                CellRect safeRect = CellRect.FromLimits(5, 5, TestMap.Size.x - 6, TestMap.Size.z - 6);
                IntVec3 shooterCell = ShooterPawn.Position;
                IntVec3 forward = new IntVec3(1, 0, 0);
                IntVec3 lateral = new IntVec3(0, 0, 1);

                int maxDistance = MaxDistanceInSafeRect(shooterCell, forward, safeRect);
                int preferredDistance = Mathf.RoundToInt(Mathf.Min(range * 0.8f, TestMap.Size.x * 0.5f));
                int targetDistance = Mathf.Clamp(preferredDistance, 5, maxDistance);

                if (targetDistance < 2)
                {
                    return result;
                }

                IntVec3 anchor = new IntVec3(Mathf.RoundToInt(TestMap.Size.x * 0.625f), 0, TestMap.Size.z / 2);
                int lateralRadius = Mathf.Max(count, 3);
                List<IntVec3> candidates = new List<IntVec3>();
                for (int offset = -lateralRadius; offset <= lateralRadius; offset++)
                {
                    IntVec3 candidate = anchor + lateral * offset;
                    if (!candidate.InBounds(TestMap) || !safeRect.Contains(candidate))
                    {
                        continue;
                    }

                    if (candidate.z < 15 || candidate.z > 35)
                    {
                        continue;
                    }

                    if (candidate.DistanceTo(shooterCell) > range || candidate.DistanceTo(shooterCell) < 2f)
                    {
                        continue;
                    }

                    if (!GenSight.LineOfSight(shooterCell, candidate, TestMap))
                    {
                        continue;
                    }

                    candidates.Add(candidate);
                }

                foreach (IntVec3 candidate in candidates.OrderBy(cell => Mathf.Abs(cell.z - anchor.z)))
                {
                    if (result.Count >= count)
                    {
                        break;
                    }

                    result.Add(candidate);
                }

                return result;
            }

            private int MaxDistanceInSafeRect(IntVec3 start, IntVec3 direction, CellRect safeRect)
            {
                int distance = 0;
                IntVec3 current = start;
                while (true)
                {
                    IntVec3 next = current + direction;
                    if (!next.InBounds(TestMap) || !safeRect.Contains(next))
                    {
                        break;
                    }

                    distance++;
                    current = next;
                }

                return distance;
            }

            private void UpdateFightingPhase()
            {
                EnsureShooterAttacking();
                UpdateProjectileScreenshotSchedule();
                if (ProcessPendingStageScreenshotQueue())
                {
                    return;
                }

                if (TryBeginPostKillObservation() || TryBeginPostDamageObservation())
                {
                    return;
                }

                if (DelayTicks > 0)
                {
                    DelayTicks--;
                    if (DelayTicks % 600 == 0)
                    {
                        Result.Stage = $"fighting_{DelayTicks / 60}s";
                    }
                    return;
                }

                Result.Status = "failed_no_damage";
                Result.Notes = AppendNote(Result.Notes, "No target took damage within 60 seconds.");
                TransitionToCleanup("test_failed_no_damage", forceScreenshot: true);
            }

            private void UpdateObservationPhase()
            {
                EnsureShooterAttacking();
                UpdateProjectileScreenshotSchedule();
                if (ProcessPendingStageScreenshotQueue())
                {
                    return;
                }

                if (DelayTicks > 0)
                {
                    DelayTicks--;
                    if (DelayTicks % 120 == 0)
                    {
                        Result.Stage = $"post_hit_{DelayTicks / 60}s";
                    }
                    return;
                }

                RecordPostHitSuccessIfNeeded();
                if (!TryWaitForRemainingProjectileScreenshots())
                {
                    TransitionToCleanup("test_passed_post_hit_delay", forceScreenshot: false);
                }
            }

            private void EnsureShooterAttacking()
            {
                if (ShooterPawn == null || ShooterPawn.Destroyed || ShooterPawn.Map != TestMap)
                {
                    return;
                }

                Pawn target = ResolvePrimaryTarget();
                if (target == null)
                {
                    return;
                }

                if (ShooterPawn.drafter != null)
                {
                    ShooterPawn.drafter.Drafted = true;
                    ShooterPawn.drafter.FireAtWill = true;
                }

                Verb attackVerb = ResolveAttackVerb(target);
                if (attackVerb == null || !attackVerb.Available() || !attackVerb.CanHitTarget(target))
                {
                    Result.Notes = AppendNote(Result.Notes, "AttackVerbUnavailable=True");
                    return;
                }

                int currentTick = Find.TickManager.TicksGame;
                bool needsNewJob = ShooterPawn.CurJob == null ||
                    ShooterPawn.CurJob.def != JobDefOf.AttackStatic ||
                    ShooterPawn.CurJob.targetA.Thing != target;
                if (!needsNewJob && currentTick - lastAttackOrderTick < 60)
                {
                    return;
                }

                Job attackJob = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                attackJob.expiryInterval = 1200;
                attackJob.maxNumStaticAttacks = int.MaxValue;
                attackJob.canUseRangedWeapon = true;
                attackJob.endIfCantShootTargetFromCurPos = true;
                attackJob.endIfCantShootInMelee = false;
                attackJob.collideWithPawns = false;
                attackJob.verbToUse = attackVerb;
                ShooterPawn.jobs?.TryTakeOrderedJob(attackJob, null, false);
                lastAttackOrderTick = currentTick;
            }

            public Pawn ResolvePrimaryTarget()
            {
                return EnemyPawns
                    .Where(pawn => pawn != null && !pawn.Destroyed && pawn.Spawned && pawn.Map == TestMap && !pawn.Dead)
                    .OrderBy(pawn => pawn.Downed ? 1 : 0)
                    .ThenBy(pawn => pawn.Position.DistanceToSquared(ShooterPawn?.Position ?? IntVec3.Zero))
                    .FirstOrDefault();
            }

            private Verb ResolveAttackVerb(Thing target)
            {
                Verb verb = target == null || ShooterPawn == null
                    ? null
                    : ShooterPawn.TryGetAttackVerb(target, true, false);
                if (verb != null)
                {
                    return verb;
                }

                return ShooterPawn?.equipment?.AllEquipmentVerbs?
                    .Where(value => value != null && value.verbProps != null && value.verbProps.range > 1f)
                    .OrderByDescending(value => value.verbProps.range)
                    .FirstOrDefault();
            }

            private void UpdateProjectileScreenshotSchedule()
            {
                if (!CaptureProjectileScreenshots)
                {
                    return;
                }

                List<Thing> activeProjectiles = GetActiveProjectiles();
                if (!ProjectileShotDetected)
                {
                    if (activeProjectiles.Count == 0)
                    {
                        return;
                    }

                    ProjectileShotDetected = true;
                    ProjectileShotTick = Find.TickManager.TicksGame;
                    Result.Notes = AppendNote(Result.Notes, "ProjectileShotTick=" + ProjectileShotTick);
                }

                if (activeProjectiles.Count == 0)
                {
                    return;
                }

                int currentTick = Find.TickManager.TicksGame;
                PruneProjectileHistory(activeProjectiles);
                if (!ProjectileScreenshotPrimed && currentTick - ProjectileShotTick >= VirtualProjectilePrimeTick)
                {
                    QueueStageScreenshot("projectile_prime_t" + VirtualProjectilePrimeTick, force: false, allowCapture: false, focusDelayTicks: 0, primeOnly: true);
                    ProjectileScreenshotPrimed = true;
                }

                if (NextProjectileScreenshotIndex < ResolveProjectileCenteredScreenshotTargetCount())
                {
                    int offset = ResolveProjectileCenteredScreenshotOffset(NextProjectileScreenshotIndex);
                    if (currentTick - ProjectileShotTick >= offset && TryCaptureCenteredProjectileScreenshot(activeProjectiles, currentTick, offset))
                    {
                        NextProjectileScreenshotIndex++;
                    }
                }

                UpdateProjectileHistory(activeProjectiles);
            }

            private List<Thing> GetActiveProjectiles()
            {
                IEnumerable<Thing> projectiles = TestMap?.listerThings?.ThingsInGroup(ThingRequestGroup.Projectile);
                return projectiles == null
                    ? new List<Thing>()
                    : projectiles.Where(projectile => projectile != null && !projectile.Destroyed && projectile.Spawned && projectile.Map == TestMap).ToList();
            }

            private void PruneProjectileHistory(List<Thing> activeProjectiles)
            {
                HashSet<int> activeIds = new HashSet<int>(activeProjectiles.Select(projectile => projectile.thingIDNumber));
                foreach (int thingId in ProjectileLastPositions.Keys.Where(id => !activeIds.Contains(id)).ToList())
                {
                    ProjectileLastPositions.Remove(thingId);
                    ProjectileLastTicks.Remove(thingId);
                }
            }

            private void UpdateProjectileHistory(List<Thing> activeProjectiles)
            {
                int currentTick = Find.TickManager.TicksGame;
                foreach (Thing projectile in activeProjectiles)
                {
                    ProjectileLastPositions[projectile.thingIDNumber] = projectile.DrawPos;
                    ProjectileLastTicks[projectile.thingIDNumber] = currentTick;
                }
            }

            private bool TryCaptureCenteredProjectileScreenshot(List<Thing> activeProjectiles, int currentTick, int offset)
            {
                float aspect = ResolveTurretScreenshotAspect(Find.CameraDriver);
                if (!owner.TryResolveRangedWeaponScreenshotView(this, aspect, out Vector3 screenshotCenter, out float rootSize))
                {
                    return false;
                }

                Thing bestProjectile = null;
                float bestDistance = float.MaxValue;
                float bestScore = float.MaxValue;
                Vector3? bestFocusRootPos = null;
                float? bestFocusRootSize = null;
                foreach (Thing projectile in activeProjectiles)
                {
                    if (!ProjectileLastPositions.TryGetValue(projectile.thingIDNumber, out Vector3 previousPos) ||
                        !ProjectileLastTicks.TryGetValue(projectile.thingIDNumber, out int previousTick))
                    {
                        continue;
                    }

                    int deltaTicks = Math.Max(1, currentTick - previousTick);
                    Vector3 currentPos = projectile.DrawPos;
                    Vector3 candidateCenter = screenshotCenter;
                    float candidateRootSize = rootSize;
                    if (owner.TryResolveRangedWeaponProjectileFocusView(this, projectile, aspect, out Vector3 resolvedRootPos, out float resolvedRootSize))
                    {
                        candidateCenter = resolvedRootPos;
                        candidateRootSize = resolvedRootSize;
                    }

                    Vector3 closestPoint = ClosestPointOnHorizontalSegment(previousPos, currentPos, candidateCenter, out float segmentProgress);
                    float closestDistance = HorizontalDistance(closestPoint, candidateCenter);
                    float captureRadius = Mathf.Max(0.35f, candidateRootSize * 0.035f);
                    if (closestDistance > captureRadius || segmentProgress <= 0f || segmentProgress > 1f)
                    {
                        continue;
                    }

                    float speed = HorizontalMagnitude((currentPos - previousPos) / deltaTicks);
                    float score = closestDistance / Mathf.Max(0.001f, captureRadius);
                    if (speed <= 0.001f || score > bestScore || (Mathf.Approximately(score, bestScore) && closestDistance >= bestDistance))
                    {
                        continue;
                    }

                    bestProjectile = projectile;
                    bestDistance = closestDistance;
                    bestScore = score;
                    bestFocusRootPos = candidateCenter;
                    bestFocusRootSize = candidateRootSize;
                }

                if (bestProjectile == null)
                {
                    return false;
                }

                QueueStageScreenshot("projectile_center_t" + offset, force: true, allowCapture: true, bestFocusRootPos, bestFocusRootSize);
                return true;
            }

            private bool ProcessPendingStageScreenshotQueue()
            {
                if (ActiveStageScreenshot == null && pendingStageScreenshots.Count == 0)
                {
                    return false;
                }

                if (ActiveStageScreenshot == null)
                {
                    ActiveStageScreenshot = pendingStageScreenshots.Dequeue();
                    pendingStageScreenshotDelayTicks = Math.Max(0, ActiveStageScreenshot.FocusDelayTicks);
                }

                if (!owner.FocusRangedWeaponScreenshotMap(this))
                {
                    ActiveStageScreenshot = null;
                    pendingStageScreenshotDelayTicks = 0;
                    return false;
                }

                if (pendingStageScreenshotDelayTicks > 0)
                {
                    pendingStageScreenshotDelayTicks--;
                    return true;
                }

                if (ActiveStageScreenshot.PrimeOnly)
                {
                    ActiveStageScreenshot = null;
                    return true;
                }

                owner.TryCaptureRangedWeaponStageScreenshot(this, ActiveStageScreenshot.StageSuffix, ActiveStageScreenshot.Force, ActiveStageScreenshot.AllowCapture);
                ActiveStageScreenshot = null;
                return true;
            }

            private void QueueStageScreenshot(string stageSuffix, bool force, bool allowCapture, Vector3? focusRootPos = null, float? focusRootSize = null, int focusDelayTicks = 0, bool primeOnly = false)
            {
                pendingStageScreenshots.Enqueue(new StageScreenshotRequest
                {
                    StageSuffix = stageSuffix,
                    Force = force,
                    AllowCapture = allowCapture,
                    FocusRootPos = focusRootPos,
                    FocusRootSize = focusRootSize,
                    FocusDelayTicks = focusDelayTicks,
                    PrimeOnly = primeOnly
                });
            }

            private void UpdateFinalScreenshotPhase()
            {
                if (!FinalScreenshotRequested)
                {
                    FinalScreenshotRequested = true;
                    Result.Stage = "capturing_screenshot";
                    if (!owner.FocusRangedWeaponScreenshotMap(this))
                    {
                        Phase = RangedWeaponTestPhase.CleaningUp;
                        return;
                    }

                    DelayTicks = ScreenshotFocusWaitTicks;
                    return;
                }

                if (!FinalScreenshotCaptured)
                {
                    if (DelayTicks > 0)
                    {
                        DelayTicks--;
                        return;
                    }

                    if (!owner.TryCaptureRangedWeaponScreenshot(this, FinalScreenshotForce))
                    {
                        Phase = RangedWeaponTestPhase.CleaningUp;
                        return;
                    }

                    FinalScreenshotCaptured = true;
                    DelayTicks = ScreenshotWaitTicks;
                    return;
                }

                if (DelayTicks > 0)
                {
                    DelayTicks--;
                }
                else
                {
                    Phase = RangedWeaponTestPhase.CleaningUp;
                }
            }

            private void CleanupAndClose()
            {
                CleanupShooter();
                if (TestMap != null && Find.Maps.Contains(TestMap))
                {
                    owner.DestroyPocketMap(TestMap, Parent);
                }
                else if (Parent != null && !Parent.Destroyed)
                {
                    owner.DestroyPocketMap(null, Parent);
                }

                if (Current.Game != null && owner.map != null && Find.Maps.Contains(owner.map))
                {
                    Current.Game.CurrentMap = owner.map;
                }

                Phase = RangedWeaponTestPhase.Done;
            }

            private void CleanupShooter()
            {
                try
                {
                    if (ShooterPawn != null && !ShooterPawn.Destroyed)
                    {
                        ShooterPawn.Destroy(DestroyMode.Vanish);
                    }
                }
                catch
                {
                }

                ShooterPawn = null;
                EquippedWeapon = null;
            }

            private void RecordPostHitSuccessIfNeeded()
            {
                if (PostHitResultRecorded)
                {
                    return;
                }

                Result.Status = "passed";
                Result.Notes = AppendNote(Result.Notes, "TargetsDownedOrKilled=" + EnemyPawns.Count(pawn => pawn == null || pawn.Destroyed || pawn.Dead || pawn.Downed));
                PostHitResultRecorded = true;
            }

            private bool TryWaitForRemainingProjectileScreenshots()
            {
                if (!CaptureProjectileScreenshots || CountRemainingProjectileVisualCaptures() <= 0)
                {
                    return false;
                }

                if (ProjectileScreenshotGraceTicksRemaining < 0)
                {
                    ProjectileScreenshotGraceTicksRemaining = ProjectileScreenshotGraceTicks;
                    Result.Notes = AppendNote(Result.Notes, "ProjectileScreenshotGraceStarted=True");
                }

                if (ProjectileScreenshotGraceTicksRemaining > 0)
                {
                    ProjectileScreenshotGraceTicksRemaining--;
                    if (ProjectileScreenshotGraceTicksRemaining % 120 == 0)
                    {
                        Result.Stage = $"projectile_grace_{ProjectileScreenshotGraceTicksRemaining / 60}s";
                    }
                    return true;
                }

                return false;
            }

            private int CountRemainingProjectileVisualCaptures()
            {
                return Math.Max(0, ResolveProjectileCenteredScreenshotTargetCount() - NextProjectileScreenshotIndex);
            }

            private int ResolveProjectileCenteredScreenshotTargetCount()
            {
                return ProjectileScreenshotTicks == null || ProjectileScreenshotTicks.Count == 0
                    ? 0
                    : Math.Min(ProjectileCenteredScreenshotTargetCount, ProjectileScreenshotTicks.Count);
            }

            private int ResolveProjectileCenteredScreenshotOffset(int captureIndex)
            {
                if (ProjectileScreenshotTicks == null || ProjectileScreenshotTicks.Count == 0)
                {
                    return 0;
                }

                return captureIndex <= 0 || ProjectileScreenshotTicks.Count == 1
                    ? ProjectileScreenshotTicks[0]
                    : ProjectileScreenshotTicks[ProjectileScreenshotTicks.Count - 1];
            }

            private bool TryBeginPostKillObservation()
            {
                Pawn firstKilledPawn = EnemyPawns.FirstOrDefault(pawn => pawn == null || pawn.Destroyed || pawn.Dead || pawn.Downed);
                if (firstKilledPawn == null && EnemyPawns.All(pawn => pawn != null && !pawn.Destroyed && !pawn.Dead && !pawn.Downed))
                {
                    return false;
                }

                FirstKillTick = Find.TickManager.TicksGame;
                FirstKillPawnLabel = firstKilledPawn == null ? "destroyed_target" : firstKilledPawn.LabelShortCap;
                DelayTicks = ObservationTicks;
                Result.Stage = "post_kill_10s";
                Result.Notes = AppendNote(Result.Notes, "FirstKillTarget=" + FirstKillPawnLabel);
                Phase = RangedWeaponTestPhase.WaitingAfterHit;
                CleanupReason = "waiting_post_kill_delay";
                return true;
            }

            private bool TryBeginPostDamageObservation()
            {
                Pawn firstDamagedPawn = EnemyPawns.FirstOrDefault(pawn =>
                    pawn != null &&
                    !pawn.Destroyed &&
                    !pawn.Dead &&
                    pawn.health?.summaryHealth != null &&
                    EnemyInitialHealthPercent.TryGetValue(pawn.thingIDNumber, out float initialHealthPercent) &&
                    pawn.health.summaryHealth.SummaryHealthPercent < initialHealthPercent - 0.0001f);
                if (firstDamagedPawn == null)
                {
                    return false;
                }

                FirstDamageTick = Find.TickManager.TicksGame;
                FirstDamagePawnLabel = firstDamagedPawn.LabelShortCap;
                DelayTicks = PostDamageObservationTicks;
                Result.Stage = "post_damage_30s";
                Result.Notes = AppendNote(Result.Notes, "FirstDamageTarget=" + FirstDamagePawnLabel);
                Phase = RangedWeaponTestPhase.WaitingAfterHit;
                CleanupReason = "waiting_post_damage_delay";
                return true;
            }

            private void TransitionToCleanup(string cleanupReason, bool forceScreenshot)
            {
                CleanupReason = cleanupReason;
                FinalScreenshotForce = forceScreenshot;
                FinalScreenshotRequested = false;
                FinalScreenshotCaptured = false;
                bool shouldCaptureFinalScreenshot = forceScreenshot ||
                    (AllowScreenshots && !(CaptureProjectileScreenshots && Result?.ScreenshotPaths != null && Result.ScreenshotPaths.Count > 0));
                Phase = shouldCaptureFinalScreenshot && TestMap != null && Find.Maps.Contains(TestMap)
                    ? RangedWeaponTestPhase.CapturingScreenshot
                    : RangedWeaponTestPhase.CleaningUp;
            }

            private void Fail(string status, string message)
            {
                Result.Status = status;
                Result.ErrorMessage = message ?? string.Empty;
                if (Phase == RangedWeaponTestPhase.CleaningUp)
                {
                    Phase = RangedWeaponTestPhase.Done;
                    return;
                }

                TransitionToCleanup("failed_" + status, forceScreenshot: true);
            }

            private bool EnsureMapAlive(string stageTag)
            {
                if (TestMap == null)
                {
                    Fail("map_missing", "TestMap was null at stage " + stageTag + ".");
                    return false;
                }

                if (!Find.Maps.Contains(TestMap))
                {
                    Fail("map_removed", "TestMap left Find.Maps before stage " + stageTag + ".");
                    return false;
                }

                return true;
            }

            private void LogMapState(string stageTag)
            {
                Log.Message("[LogSyncAutoTest] [PocketMapState] ranged='" + WeaponDef.defName + "' stage=" + stageTag + " mapId=" + (TestMap?.uniqueID ?? -1) + " mapExists=" + (TestMap != null && Find.Maps.Contains(TestMap)));
            }

            private static float HorizontalDistance(Vector3 a, Vector3 b)
            {
                float dx = a.x - b.x;
                float dz = a.z - b.z;
                return Mathf.Sqrt(dx * dx + dz * dz);
            }

            private static Vector3 ClosestPointOnHorizontalSegment(Vector3 start, Vector3 end, Vector3 point, out float progress)
            {
                Vector2 start2 = new Vector2(start.x, start.z);
                Vector2 end2 = new Vector2(end.x, end.z);
                Vector2 point2 = new Vector2(point.x, point.z);
                Vector2 segment = end2 - start2;
                float segmentLengthSquared = segment.sqrMagnitude;
                if (segmentLengthSquared <= 0.0001f)
                {
                    progress = 0f;
                    return start;
                }

                progress = Mathf.Clamp01(Vector2.Dot(point2 - start2, segment) / segmentLengthSquared);
                Vector2 closest2 = start2 + segment * progress;
                return new Vector3(closest2.x, point.y, closest2.y);
            }

            private static float HorizontalMagnitude(Vector3 value)
            {
                return Mathf.Sqrt(value.x * value.x + value.z * value.z);
            }
        }

        private void BeginRangedWeaponTestSession()
        {
            List<ThingDef> weaponDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(IsTargetRangedWeaponDef)
                .OrderBy(def => def.defName)
                .ToList();

            foreach (ThingDef weaponDef in weaponDefs)
            {
                RegisterDiscoveredCase(AutoTestCaseType.RangedWeaponPocketMap, "ThingDef", weaponDef.defName, weaponDef.label);
                pendingRangedWeaponDefs.Enqueue(weaponDef);
            }

            Log.Message("[LogSyncAutoTest] Ranged weapon queue prepared. ConcurrentLimit=" + MaxConcurrentTurretTests + " Total=" + pendingRangedWeaponDefs.Count);
        }

        private void StartNewRangedWeaponSession(ThingDef weaponDef)
        {
            AutoTestResult result = CreateResult(AutoTestCaseType.RangedWeaponPocketMap, "ThingDef", weaponDef.defName, weaponDef.label);
            RangedWeaponTestSession session = new RangedWeaponTestSession(this, weaponDef, result);
            LogSyncAutoTestSpecDef spec = ResolveBestMatchingSpec(AutoTestKind.RangedWeaponPocketMap, weaponDef);
            session.AllowScreenshots = ShouldCaptureScreenshots(spec);
            session.CaptureProjectileScreenshots = session.AllowScreenshots ||
                ShouldCaptureProjectileScreenshots(spec, AutoTestKind.RangedWeaponPocketMap);
            session.ProjectileScreenshotTicks = ResolveProjectileScreenshotTicks(spec);
            result.Notes = AppendNote(result.Notes, "AutoTestMode=" + (settings.UseXmlDrivenAutoTest() ? "XmlDriven" : "Automatic"));
            if (spec != null)
            {
                result.Notes = AppendNote(result.Notes, "Spec=" + spec.defName);
            }

            try
            {
                AutoTestPocketMapHandle handle = CreatePocketMapHandle(
                    "LogSync_AutoTestPocketMap",
                    new IntVec3(50, 1, 50),
                    generatedMap => MapGenerator.PlayerStartSpot = generatedMap.Center,
                    out string errorMessage);
                if (handle == null)
                {
                    result.Status = "failed_map_generation";
                    result.ErrorMessage = errorMessage ?? "Failed to create PocketMapParent.";
                    results.Add(result);
                    return;
                }

                session.Parent = handle.Parent;
                session.TestMap = handle.Map;
                if (session.TestMap == null)
                {
                    result.Status = "failed_map_generation";
                    result.ErrorMessage = "Managed pocket map generation returned null Map.";
                    results.Add(result);
                    return;
                }

                session.Parent = session.TestMap.Parent as PocketMapParent ?? handle.Parent;
                activeRangedWeaponSessions.Add(session);
                Log.Message("[LogSyncAutoTest] Starting ranged pocket map for '" + weaponDef.defName + "' in Map " + session.TestMap.uniqueID + ". SourceMap=" + map.uniqueID + ".");
            }
            catch (Exception ex)
            {
                result.Status = "failed_map_creation";
                result.ErrorMessage = ex.ToString();
                results.Add(result);
                DestroyPocketMap(session.TestMap, session.Parent);
            }
        }

        private bool IsTargetRangedWeaponDef(ThingDef def)
        {
            return def != null &&
                def.modContentPack == targetMod &&
                def.category == ThingCategory.Item &&
                def.IsWeapon &&
                def.IsRangedWeapon &&
                !IsTurretMountedGunDef(def) &&
                MatchesSpecFilters(AutoTestKind.RangedWeaponPocketMap, def);
        }
    }
}
