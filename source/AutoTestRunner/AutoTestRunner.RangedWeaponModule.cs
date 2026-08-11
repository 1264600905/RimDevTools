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
            public string TargetingMode = "unsupported";
            public Verb CombatVerb;
            public VerbProperties CombatVerbProps;
            public AutoTestCombatPlacementResult CombatPlacement;
            public Thing AttackTargetBuilding;
            public IntVec3 AttackTargetCell = IntVec3.Invalid;
            public int AttackTargetBuildingInitialHitPoints = -1;
            public int FirstAttackInitiatedTick = -1;
            public string CombatPassEvidence = string.Empty;
            private bool attackVerbUnavailableRecorded;
            private bool attackInitiationRecorded;
            private bool combatVerbEvidenceRecorded;

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

                            ResolveCombatVerbProperties();
                            TargetingMode = ClassifyTargetingMode();
                            Result.Notes = AppendNote(Result.Notes, "TargetingMode=" + TargetingMode);
                            if (TargetingMode == "unsupported")
                            {
                                Result.Status = "unsupported";
                                Result.Stage = "targeting_classification";
                                Result.ErrorMessage = "The equipped combat Verb does not expose a supported Pawn, Location, or Building target type.";
                                Phase = RangedWeaponTestPhase.CleaningUp;
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
                ResolveCombatVerbProperties();
                if (CombatVerbProps == null)
                {
                    Fail("unsupported_harness", "No ranged VerbProperties were available after equipping the weapon.");
                    return;
                }

                bool buildingTarget = TargetingMode == "building_target";
                int requestedCells = count + (buildingTarget ? 1 : 0);
                CellRect safeRect = CellRect.FromLimits(5, 5, TestMap.Size.x - 6, TestMap.Size.z - 6);
                IntVec3 preferredAnchor = new IntVec3(Mathf.RoundToInt(TestMap.Size.x * 0.625f), 0, TestMap.Size.z / 2);
                CombatPlacement = new AutoTestCombatPlacementService().FindTargets(new AutoTestCombatPlacementRequest
                {
                    Map = TestMap,
                    Attacker = ShooterPawn,
                    AttackerCell = ShooterPawn.Position,
                    VerbProps = CombatVerbProps,
                    EffectiveRange = CombatVerb?.EffectiveRange ?? CombatVerbProps.range,
                    TargetCount = requestedCells,
                    PreferredAnchor = preferredAnchor,
                    SafeRect = safeRect,
                    RequireLineOfSight = CombatVerbProps.requireLineOfSight,
                    RequireWalkable = true,
                    MinimumSeparationMargin = 0.5f,
                    MaximumRangeMargin = 0.5f,
                    MinimumDistanceFloor = 2f
                });
                RecordCombatPlacementEvidence(CombatPlacement);
                if (CombatPlacement.SelectedCells.Count == 0)
                {
                    string failure = CombatPlacement.FailureReason == "no_legal_distance_interval"
                        ? "unsupported_harness"
                        : "failed_enemy_spawn_pos";
                    Fail(failure, "No valid ranged target cells were found. reason=" + CombatPlacement.FailureReason +
                        " diagnostics=" + CombatPlacement.DiagnosticsSummary);
                    return;
                }

                List<IntVec3> spawnCells = CombatPlacement.SelectedCells.ToList();
                if (buildingTarget)
                {
                    AttackTargetCell = spawnCells[0];
                    if (!TrySpawnBuildingAttackTarget(AttackTargetCell))
                    {
                        Fail("unsupported_harness", "The building-target harness could not create a valid target building.");
                        return;
                    }
                    spawnCells.RemoveAt(0);
                }
                else
                {
                    AttackTargetCell = spawnCells[0];
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
                Result.Notes = AppendNote(Result.Notes, "ActualTargetDistances=" + BuildActualTargetDistances());
                if (!TryValidateAttackTarget(out string validationError))
                {
                    Fail("failed_target_validation", validationError);
                }
            }

            private void ResolveCombatVerbProperties()
            {
                CombatVerb = ShooterPawn?.equipment?.AllEquipmentVerbs?
                    .Where(value => value?.verbProps != null && value.verbProps.range > 1f)
                    .OrderByDescending(value => value.verbProps.isPrimary)
                    .ThenByDescending(value => value.verbProps.range)
                    .FirstOrDefault();
                CombatVerbProps = CombatVerb?.verbProps ?? WeaponDef?.Verbs?
                    .Where(value => value != null && value.range > 1f)
                    .OrderByDescending(value => value.isPrimary)
                    .ThenByDescending(value => value.range)
                    .FirstOrDefault();
                if (CombatVerbProps == null)
                {
                    return;
                }

                if (!combatVerbEvidenceRecorded)
                {
                    Result.Notes = AppendNote(Result.Notes, "VerbDef=" + (WeaponDef?.defName ?? "unknown"));
                    Result.Notes = AppendNote(Result.Notes, "VerbClass=" + (CombatVerb?.GetType().FullName ?? CombatVerbProps.verbClass?.FullName ?? "unknown"));
                    Result.Notes = AppendNote(Result.Notes, "MinRange=" + CombatVerbProps.minRange.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                    Result.Notes = AppendNote(Result.Notes, "Range=" + (CombatVerb?.EffectiveRange ?? CombatVerbProps.range).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                    Result.Notes = AppendNote(Result.Notes, "RequireLineOfSight=" + CombatVerbProps.requireLineOfSight);
                    Result.Notes = AppendNote(Result.Notes, "OnlyManualCast=" + CombatVerbProps.onlyManualCast);
                    combatVerbEvidenceRecorded = true;
                }
            }

            private void RecordCombatPlacementEvidence(AutoTestCombatPlacementResult placement)
            {
                if (placement == null)
                {
                    return;
                }

                Result.Notes = AppendNote(Result.Notes, "CombatMinRange=" + placement.MinValidDistance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                Result.Notes = AppendNote(Result.Notes, "CombatMaxRange=" + placement.MaxValidDistance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                Result.Notes = AppendNote(Result.Notes, "PreferredTargetDistance=" + placement.PreferredDistance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                Result.Notes = AppendNote(Result.Notes, "PreferredCellWasValid=" + placement.PreferredCellWasValid);
                Result.Notes = AppendNote(Result.Notes, "EnemyRepositioned=" + placement.EnemyRepositioned);
                Result.Notes = AppendNote(Result.Notes, "RepositionReason=" + placement.RepositionReason);
                Result.Notes = AppendNote(Result.Notes, "CandidateDiagnostics=" + placement.DiagnosticsSummary);
            }

            private bool TrySpawnBuildingAttackTarget(IntVec3 cell)
            {
                try
                {
                    ThingDef wallDef = ThingDefOf.Wall;
                    ThingDef stuffDef = ThingDefOf.Steel;
                    Thing wall = ThingMaker.MakeThing(wallDef, stuffDef);
                    if (wall == null)
                    {
                        return false;
                    }

                    Faction targetFaction = Faction.OfAncientsHostile ?? Faction.OfPirates ?? Faction.OfMechanoids;
                    owner.PrepareBuildingFactionBeforeSpawn(wall, AutoTestBuildingContext.InteractiveBuilding, Result, targetFaction);
                    AttackTargetBuilding = GenSpawn.Spawn(wall, cell, TestMap);
                    AttackTargetBuildingInitialHitPoints = AttackTargetBuilding?.HitPoints ?? -1;
                    Result.Notes = AppendNote(Result.Notes, "BuildingAttackTarget=" + (AttackTargetBuilding?.def?.defName ?? "missing"));
                    return AttackTargetBuilding != null;
                }
                catch (Exception ex)
                {
                    Result.Notes = AppendNote(Result.Notes, "BuildingTargetSpawnError=" + TrimForNote(ex.Message));
                    return false;
                }
            }

            private void UpdateFightingPhase()
            {
                EnsureShooterAttacking();
                UpdateProjectileScreenshotSchedule();
                if (ProcessPendingStageScreenshotQueue())
                {
                    return;
                }

                if (TryBeginPostKillObservation() || TryBeginPostDamageObservation() || TryBeginPostProjectileObservation())
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

                LocalTargetInfo target = ResolveAttackTarget();
                if (!target.IsValid)
                {
                    return;
                }

                if (ShooterPawn.drafter != null)
                {
                    ShooterPawn.drafter.Drafted = true;
                    ShooterPawn.drafter.FireAtWill = true;
                }

                Verb attackVerb = ResolveAttackVerb(target);
                if (attackVerb == null || !attackVerb.Available() || !IsTargetAllowed(attackVerb, target) || !attackVerb.CanHitTarget(target))
                {
                    if (!attackVerbUnavailableRecorded)
                    {
                        Result.Notes = AppendNote(Result.Notes, "AttackVerbUnavailable=True");
                        attackVerbUnavailableRecorded = true;
                    }
                    return;
                }

                int currentTick = Find.TickManager.TicksGame;
                if (attackVerb.verbProps.onlyManualCast)
                {
                    if (attackVerb.state == VerbState.Bursting || ShooterPawn.stances?.curStance is Stance_Warmup)
                    {
                        return;
                    }

                    if (currentTick - lastAttackOrderTick < 60)
                    {
                        return;
                    }

                    if (attackVerb.TryStartCastOn(target, surpriseAttack: false, canHitNonTargetPawns: true, preventFriendlyFire: false))
                    {
                        lastAttackOrderTick = currentTick;
                        FirstAttackInitiatedTick = FirstAttackInitiatedTick < 0 ? currentTick : FirstAttackInitiatedTick;
                        RecordAttackInitiation("ManualVerbCast");
                    }
                    return;
                }

                bool needsNewJob = ShooterPawn.CurJob == null ||
                    ShooterPawn.CurJob.def != JobDefOf.AttackStatic ||
                    ShooterPawn.CurJob.targetA != target;
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
                if (ShooterPawn.jobs?.TryTakeOrderedJob(attackJob, null, false) ?? false)
                {
                    lastAttackOrderTick = currentTick;
                    FirstAttackInitiatedTick = FirstAttackInitiatedTick < 0 ? currentTick : FirstAttackInitiatedTick;
                    RecordAttackInitiation("AttackStaticJob");
                }
            }

            public Pawn ResolvePrimaryTarget()
            {
                return EnemyPawns
                    .Where(pawn => pawn != null && !pawn.Destroyed && pawn.Spawned && pawn.Map == TestMap && !pawn.Dead)
                    .OrderBy(pawn => pawn.Downed ? 1 : 0)
                    .ThenBy(pawn => pawn.Position.DistanceToSquared(ShooterPawn?.Position ?? IntVec3.Zero))
                    .FirstOrDefault();
            }

            private Verb ResolveAttackVerb(LocalTargetInfo target)
            {
                if (CombatVerb != null)
                {
                    return CombatVerb;
                }

                Verb verb = !target.IsValid || ShooterPawn == null
                    ? null
                    : ShooterPawn.TryGetAttackVerb(target.Thing, true, false);
                if (verb != null)
                {
                    return verb;
                }

                return ShooterPawn?.equipment?.AllEquipmentVerbs?
                    .Where(value => value != null && value.verbProps != null && value.verbProps.range > 1f)
                    .OrderByDescending(value => value.verbProps.range)
                    .FirstOrDefault();
            }

            private LocalTargetInfo ResolveAttackTarget()
            {
                if (TargetingMode == "building_target")
                {
                    return AttackTargetBuilding == null || AttackTargetBuilding.Destroyed
                        ? LocalTargetInfo.Invalid
                        : new LocalTargetInfo(AttackTargetBuilding);
                }

                if (TargetingMode == "location_target")
                {
                    return AttackTargetCell.IsValid ? new LocalTargetInfo(AttackTargetCell) : LocalTargetInfo.Invalid;
                }

                Pawn pawn = ResolvePrimaryTarget();
                return pawn == null ? LocalTargetInfo.Invalid : new LocalTargetInfo(pawn);
            }

            private bool IsTargetAllowed(Verb verb, LocalTargetInfo target)
            {
                if (verb?.targetParams == null || !target.IsValid)
                {
                    return false;
                }

                TargetInfo targetInfo = target.HasThing
                    ? new TargetInfo(target.Thing)
                    : new TargetInfo(target.Cell, TestMap);
                return verb.targetParams.CanTarget(targetInfo, verb) && verb.ValidateTarget(target, showMessages: false);
            }

            private void RecordAttackInitiation(string initiation)
            {
                if (attackInitiationRecorded)
                {
                    return;
                }

                Result.Notes = AppendNote(Result.Notes, "AttackInitiation=" + initiation);
                Result.Notes = AppendNote(Result.Notes, "FirstAttackInitiatedTick=" + FirstAttackInitiatedTick);
                attackInitiationRecorded = true;
            }

            private bool TryValidateAttackTarget(out string errorMessage)
            {
                errorMessage = string.Empty;
                LocalTargetInfo target = ResolveAttackTarget();
                if (!target.IsValid)
                {
                    errorMessage = "The selected target type did not produce a valid LocalTargetInfo.";
                    return false;
                }

                if (CombatPlacement == null)
                {
                    errorMessage = "Combat placement evidence was missing after target spawn.";
                    return false;
                }

                foreach (IntVec3 cell in EnemyPawns
                    .Where(pawn => pawn != null && pawn.Spawned && pawn.Map == TestMap)
                    .Select(pawn => pawn.Position)
                    .Concat(AttackTargetBuilding != null && AttackTargetBuilding.Spawned
                        ? new[] { AttackTargetBuilding.Position }
                        : Enumerable.Empty<IntVec3>()))
                {
                    float distance = cell.DistanceTo(ShooterPawn.Position);
                    if (distance < CombatPlacement.MinValidDistance - 0.01f || distance > CombatPlacement.MaxValidDistance + 0.01f)
                    {
                        errorMessage = "Spawned target at " + cell + " has illegal distance " +
                            distance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ".";
                        return false;
                    }
                }

                Verb verb = ResolveAttackVerb(target);
                if (verb == null)
                {
                    errorMessage = "No equipped attack Verb was available after target spawn.";
                    return false;
                }

                if (!IsTargetAllowed(verb, target))
                {
                    errorMessage = "TargetingParameters rejected the prepared " + TargetingMode + " target.";
                    return false;
                }

                if (!verb.CanHitTarget(target))
                {
                    errorMessage = "The equipped Verb could not hit the prepared target after spawn.";
                    return false;
                }

                Result.Notes = AppendNote(Result.Notes, "VerbCanHitTarget=True");
                return true;
            }

            private string BuildActualTargetDistances()
            {
                if (ShooterPawn == null)
                {
                    return "none";
                }

                List<IntVec3> cells = EnemyPawns
                    .Where(pawn => pawn != null && pawn.Spawned && pawn.Map == TestMap)
                    .Select(pawn => pawn.Position)
                    .ToList();
                if (AttackTargetBuilding != null && AttackTargetBuilding.Spawned)
                {
                    cells.Add(AttackTargetBuilding.Position);
                }

                return cells.Count == 0
                    ? "none"
                    : string.Join(",", cells
                        .Select(cell => cell.DistanceTo(ShooterPawn.Position).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                        .ToArray());
            }

            private void UpdateProjectileScreenshotSchedule()
            {
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

                if (!CaptureProjectileScreenshots)
                {
                    return;
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

                QueueStageScreenshot("projectile_center_t" + offset, force: true, allowCapture: true, bestFocusRootPos, bestFocusRootSize, retryAttempts: 10);
                return true;
            }

            private string ClassifyTargetingMode()
            {
                TargetingParameters parameters = CombatVerbProps?.targetParams;
                if (parameters == null) return "unsupported";
                if (parameters.canTargetPawns) return "pawn_target";
                if (parameters.canTargetLocations) return "location_target";
                if (parameters.canTargetBuildings) return "building_target";
                return "unsupported";
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

                bool captured = owner.TryCaptureRangedWeaponStageScreenshot(this, ActiveStageScreenshot.StageSuffix, ActiveStageScreenshot.Force, ActiveStageScreenshot.AllowCapture);
                if (!captured && ActiveStageScreenshot.RetryAttemptsRemaining > 0)
                {
                    ActiveStageScreenshot.RetryAttemptsRemaining--;
                    pendingStageScreenshotDelayTicks = 1;
                    return true;
                }

                ActiveStageScreenshot = null;
                return true;
            }

            private void QueueStageScreenshot(string stageSuffix, bool force, bool allowCapture, Vector3? focusRootPos = null, float? focusRootSize = null, int focusDelayTicks = 0, bool primeOnly = false, int retryAttempts = 0)
            {
                pendingStageScreenshots.Enqueue(new StageScreenshotRequest
                {
                    StageSuffix = stageSuffix,
                    Force = force,
                    AllowCapture = allowCapture,
                    FocusRootPos = focusRootPos,
                    FocusRootSize = focusRootSize,
                    FocusDelayTicks = focusDelayTicks,
                    PrimeOnly = primeOnly,
                    RetryAttemptsRemaining = retryAttempts
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
                owner.RecordRangedSemanticBeforeCleanup(this);
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

                owner.RecordRangedCleanupSemantic(this);
                Phase = RangedWeaponTestPhase.Done;
            }

            private void CleanupShooter()
            {
                try
                {
                    if (AttackTargetBuilding != null && !AttackTargetBuilding.Destroyed)
                    {
                        AttackTargetBuilding.Destroy(DestroyMode.Vanish);
                    }

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
                AttackTargetBuilding = null;
            }

            private void RecordPostHitSuccessIfNeeded()
            {
                if (PostHitResultRecorded)
                {
                    return;
                }

                Result.Status = "passed";
                Result.Notes = AppendNote(Result.Notes, "CombatPassEvidence=" + (string.IsNullOrEmpty(CombatPassEvidence) ? "DamageOrKill" : CombatPassEvidence));
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
                if (TargetingMode == "building_target" && AttackTargetBuilding != null && AttackTargetBuilding.Destroyed)
                {
                    FirstKillTick = Find.TickManager.TicksGame;
                    FirstKillPawnLabel = "building_target";
                    CombatPassEvidence = "BuildingTargetDestroyed";
                    DelayTicks = ObservationTicks;
                    Result.Stage = "post_kill_10s";
                    Result.Notes = AppendNote(Result.Notes, "FirstKillTarget=building_target");
                    Phase = RangedWeaponTestPhase.WaitingAfterHit;
                    CleanupReason = "waiting_post_kill_delay";
                    return true;
                }

                Pawn firstKilledPawn = EnemyPawns.FirstOrDefault(pawn => pawn == null || pawn.Destroyed || pawn.Dead || pawn.Downed);
                if (firstKilledPawn == null && EnemyPawns.All(pawn => pawn != null && !pawn.Destroyed && !pawn.Dead && !pawn.Downed))
                {
                    return false;
                }

                FirstKillTick = Find.TickManager.TicksGame;
                FirstKillPawnLabel = firstKilledPawn == null ? "destroyed_target" : firstKilledPawn.LabelShortCap;
                CombatPassEvidence = "TargetDownedOrKilled";
                DelayTicks = ObservationTicks;
                Result.Stage = "post_kill_10s";
                Result.Notes = AppendNote(Result.Notes, "FirstKillTarget=" + FirstKillPawnLabel);
                Phase = RangedWeaponTestPhase.WaitingAfterHit;
                CleanupReason = "waiting_post_kill_delay";
                return true;
            }

            private bool TryBeginPostDamageObservation()
            {
                if (AttackTargetBuilding != null && !AttackTargetBuilding.Destroyed &&
                    AttackTargetBuildingInitialHitPoints >= 0 && AttackTargetBuilding.HitPoints < AttackTargetBuildingInitialHitPoints)
                {
                    FirstDamageTick = Find.TickManager.TicksGame;
                    FirstDamagePawnLabel = "building_target";
                    CombatPassEvidence = "BuildingTargetDamaged";
                    DelayTicks = PostDamageObservationTicks;
                    Result.Stage = "post_damage_30s";
                    Result.Notes = AppendNote(Result.Notes, "FirstDamageTarget=building_target");
                    Phase = RangedWeaponTestPhase.WaitingAfterHit;
                    CleanupReason = "waiting_post_damage_delay";
                    return true;
                }

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
                CombatPassEvidence = "TargetDamaged";
                DelayTicks = PostDamageObservationTicks;
                Result.Stage = "post_damage_30s";
                Result.Notes = AppendNote(Result.Notes, "FirstDamageTarget=" + FirstDamagePawnLabel);
                Phase = RangedWeaponTestPhase.WaitingAfterHit;
                CleanupReason = "waiting_post_damage_delay";
                return true;
            }

            private bool TryBeginPostProjectileObservation()
            {
                if (!ProjectileShotDetected)
                {
                    return false;
                }

                CombatPassEvidence = "ProjectileObserved";
                DelayTicks = ObservationTicks;
                Result.Stage = "post_projectile_10s";
                Result.Notes = AppendNote(Result.Notes, "ProjectileCombatEvidence=True");
                Phase = RangedWeaponTestPhase.WaitingAfterHit;
                CleanupReason = "waiting_post_projectile_delay";
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

            Log.Message("[LogSyncAutoTest] Ranged weapon queue prepared. PocketMapConcurrentLimit=" + settings.GetPocketMapMaxConcurrent() + " Total=" + pendingRangedWeaponDefs.Count);
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
                    RecordResult(result);
                    return;
                }

                session.Parent = handle.Parent;
                session.TestMap = handle.Map;
                if (session.TestMap == null)
                {
                    result.Status = "failed_map_generation";
                    result.ErrorMessage = "Managed pocket map generation returned null Map.";
                    RecordResult(result);
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
                RecordResult(result);
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
