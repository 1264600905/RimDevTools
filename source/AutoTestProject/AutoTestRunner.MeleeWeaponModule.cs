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
        private sealed class MeleeWeaponTestSession
        {
            private const int MapReadyWaitTicks = 180;
            private const int FightDurationTicks = 60 * 60;
            private const int ObservationTicks = 60 * 10;
            private const int PostDamageObservationTicks = 60 * 30;
            private const int ScreenshotFocusWaitTicks = 6;
            private const int ScreenshotWaitTicks = 10;

            private readonly AutoTestRunner owner;
            private int lastAttackOrderTick = -9999;

            public Map TestMap;
            public ThingDef WeaponDef;
            public Pawn ShooterPawn;
            public ThingWithComps EquippedWeapon;
            public readonly List<Pawn> EnemyPawns = new List<Pawn>();
            public readonly Dictionary<int, float> EnemyInitialHealthPercent = new Dictionary<int, float>();
            public MeleeWeaponTestPhase Phase;
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
            public bool ActionScreenshotCaptured;
            public bool AllowScreenshots;
            public bool SearchAndDestroyEnabled;
            public string SearchAndDestroyStatus = string.Empty;
            public bool PostHitResultRecorded;

            public MeleeWeaponTestSession(AutoTestRunner owner, ThingDef weaponDef, AutoTestResult result)
            {
                this.owner = owner;
                WeaponDef = weaponDef;
                Result = result;
                Phase = MeleeWeaponTestPhase.Initializing;
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
                        case MeleeWeaponTestPhase.Initializing:
                            WaitTicks = MapReadyWaitTicks;
                            LogMapState("initializing");
                            Phase = MeleeWeaponTestPhase.WaitingForMap;
                            break;
                        case MeleeWeaponTestPhase.WaitingForMap:
                            UpdateWaitingForMap();
                            break;
                        case MeleeWeaponTestPhase.SpawningShooter:
                            SpawnShooterPawn();
                            if (ShooterPawn == null)
                            {
                                Fail("failed_shooter_spawn", "Shooter pawn could not be created.");
                                return;
                            }

                            Result.Stage = "spawn_shooter";
                            Phase = MeleeWeaponTestPhase.EquippingWeapon;
                            break;
                        case MeleeWeaponTestPhase.EquippingWeapon:
                            EquipWeapon();
                            if (EquippedWeapon == null)
                            {
                                Fail("failed_weapon_equip", "Melee weapon could not be equipped.");
                                return;
                            }

                            Result.Stage = "equip_weapon";
                            Phase = MeleeWeaponTestPhase.SpawningEnemies;
                            break;
                        case MeleeWeaponTestPhase.SpawningEnemies:
                            SpawnEnemies(5);
                            if (EnemyPawns.Count == 0)
                            {
                                Fail("failed_enemy_spawn", "No enemies were spawned for the melee weapon test.");
                                return;
                            }

                            EnsureShooterAttacking();
                            Result.Stage = "fighting_60s";
                            DelayTicks = FightDurationTicks;
                            Phase = MeleeWeaponTestPhase.Fighting;
                            break;
                        case MeleeWeaponTestPhase.Fighting:
                            UpdateFightingPhase();
                            break;
                        case MeleeWeaponTestPhase.WaitingAfterHit:
                            UpdateObservationPhase();
                            break;
                        case MeleeWeaponTestPhase.CapturingScreenshot:
                            UpdateFinalScreenshotPhase();
                            break;
                        case MeleeWeaponTestPhase.CleaningUp:
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
                    Phase = MeleeWeaponTestPhase.SpawningShooter;
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
                    PawnGenerationRequest request = new PawnGenerationRequest(
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
                    bool canMove = candidate.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) ?? false;
                    if (violentDisabled || !canManipulate || !canMove)
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

                // 穿上传奇品质的海军装备
                try
                {
                    ShooterPawn.apparel?.DestroyAll();

                    ThingDef armorDef = ThingDef.Named("Apparel_PowerArmor");
                    ThingDef helmetDef = ThingDef.Named("Apparel_PowerArmorHelmet");

                    Apparel armor = ThingMaker.MakeThing(armorDef) as Apparel;
                    Apparel helmet = ThingMaker.MakeThing(helmetDef) as Apparel;

                    armor.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Legendary, ArtGenerationContext.Colony);
                    helmet.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Legendary, ArtGenerationContext.Colony);

                    ShooterPawn.apparel?.Wear(armor);
                    ShooterPawn.apparel?.Wear(helmet);
                }
                catch (Exception ex)
                {
                    Log.Warning("[LogSyncAutoTest] Failed to equip legendary marine gear: " + ex.Message);
                }

                ShooterPawn.Name = new NameSingle("自动测试近战员");
                if (ShooterPawn.drafter != null)
                {
                    ShooterPawn.drafter.Drafted = true;
                    ShooterPawn.drafter.FireAtWill = false;
                }

                Result.Label = ShooterPawn.LabelCap;
                Result.Notes = AppendNote(Result.Notes, "ShooterPawnSpawned=True");
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
                Result.Label = EquippedWeapon.LabelCap;
                Result.Notes = AppendNote(Result.Notes, "EquippedWeapon=" + EquippedWeapon.def.defName);

                SearchAndDestroyEnabled = TryEnableSearchAndDestroy(ShooterPawn, out string status);
                SearchAndDestroyStatus = status ?? string.Empty;
                Result.Notes = AppendNote(Result.Notes, "SearchAndDestroy=" + (SearchAndDestroyEnabled ? "Enabled" : SearchAndDestroyStatus));
                if (SearchAndDestroyEnabled)
                {
                    ShooterPawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, true);
                }
            }

            private void SpawnEnemies(int count)
            {
                if (TestMap == null || ShooterPawn == null || count <= 0)
                {
                    return;
                }

                foreach (IntVec3 spawnCell in FindEnemySpawnCells(count))
                {
                    try
                    {
                        PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("SpaceRefugee") ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Colonist") ?? PawnKindDefOf.WildMan;
                        Faction faction = Faction.OfAncientsHostile ?? Faction.OfMechanoids;
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

                        GenSpawn.Spawn(pawn, spawnCell, TestMap);
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
                        Log.Error($"[LogSyncAutoTest] Enemy spawn CRITICAL failed for melee weapon '{WeaponDef.defName}': {ex}");
                    }
                }

                Result.Notes = AppendNote(Result.Notes, "EnemyCount=" + EnemyPawns.Count);
            }

            private List<IntVec3> FindEnemySpawnCells(int count)
            {
                List<IntVec3> result = new List<IntVec3>();
                if (TestMap == null || ShooterPawn == null)
                {
                    return result;
                }

                CellRect safeRect = CellRect.FromLimits(5, 5, TestMap.Size.x - 6, TestMap.Size.z - 6);
                // 敌人生成在地图 X 轴 5/8 处 (31.25)
                IntVec3 anchor = new IntVec3(Mathf.RoundToInt(TestMap.Size.x * 0.625f), 0, TestMap.Size.z / 2);
                // 纵向分布偏移
                int[] lateralOffsets = { 0, -1, 1, -2, 2, -3, 3 };

                foreach (int offset in lateralOffsets)
                {
                    if (result.Count >= count)
                    {
                        break;
                    }

                    IntVec3 candidate = anchor + new IntVec3(0, 0, offset);
                    if (!candidate.InBounds(TestMap) || !safeRect.Contains(candidate))
                    {
                        continue;
                    }

                    if (!candidate.Walkable(TestMap))
                    {
                        continue;
                    }

                    result.Add(candidate);
                }

                return result;
            }

            private void UpdateFightingPhase()
            {
                EnsureShooterAttacking();
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

                if (!ActionScreenshotCaptured)
                {
                    int startDelay = (FirstKillTick != -1) ? ObservationTicks : PostDamageObservationTicks;
                    int elapsed = startDelay - DelayTicks;

                    if (elapsed == 10)
                    {
                        owner.FocusMeleeAction(this);
                    }
                    else if (elapsed >= 15)
                    {
                        owner.TryCaptureMeleeActionScreenshot(this);
                        ActionScreenshotCaptured = true;
                    }
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
                TransitionToCleanup("test_passed_post_hit_delay", forceScreenshot: false);
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
                }

                int currentTick = Find.TickManager.TicksGame;
                bool alreadyEngaging = ShooterPawn.CurJob != null &&
                    (ShooterPawn.CurJob.def == JobDefOf.AttackMelee ||
                     ShooterPawn.CurJob.targetA.Thing == target);

                if (SearchAndDestroyEnabled)
                {
                    if (!alreadyEngaging && currentTick - lastAttackOrderTick >= 90)
                    {
                        ShooterPawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, true);
                        lastAttackOrderTick = currentTick;
                    }
                    return;
                }

                if (alreadyEngaging && currentTick - lastAttackOrderTick < 60)
                {
                    return;
                }

                Job attackJob = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                attackJob.expiryInterval = 1200;
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

            private void UpdateFinalScreenshotPhase()
            {
                if (!FinalScreenshotRequested)
                {
                    FinalScreenshotRequested = true;
                    Result.Stage = "capturing_screenshot";
                    if (!owner.FocusMeleeWeaponScreenshotMap(this))
                    {
                        Phase = MeleeWeaponTestPhase.CleaningUp;
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

                    if (!owner.TryCaptureMeleeWeaponScreenshot(this, FinalScreenshotForce))
                    {
                        Phase = MeleeWeaponTestPhase.CleaningUp;
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
                    Phase = MeleeWeaponTestPhase.CleaningUp;
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

                Phase = MeleeWeaponTestPhase.Done;
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
                Phase = MeleeWeaponTestPhase.WaitingAfterHit;
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
                Phase = MeleeWeaponTestPhase.WaitingAfterHit;
                CleanupReason = "waiting_post_damage_delay";
                return true;
            }

            private void TransitionToCleanup(string cleanupReason, bool forceScreenshot)
            {
                CleanupReason = cleanupReason;
                FinalScreenshotForce = forceScreenshot;
                FinalScreenshotRequested = false;
                FinalScreenshotCaptured = false;
                bool shouldCaptureFinalScreenshot = forceScreenshot || AllowScreenshots;
                Phase = shouldCaptureFinalScreenshot && TestMap != null && Find.Maps.Contains(TestMap)
                    ? MeleeWeaponTestPhase.CapturingScreenshot
                    : MeleeWeaponTestPhase.CleaningUp;
            }

            private void Fail(string status, string message)
            {
                Result.Status = status;
                Result.ErrorMessage = message ?? string.Empty;
                if (Phase == MeleeWeaponTestPhase.CleaningUp)
                {
                    Phase = MeleeWeaponTestPhase.Done;
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
                Log.Message("[LogSyncAutoTest] [PocketMapState] melee='" + WeaponDef.defName + "' stage=" + stageTag + " mapId=" + (TestMap?.uniqueID ?? -1) + " mapExists=" + (TestMap != null && Find.Maps.Contains(TestMap)));
            }
        }

        private void BeginMeleeWeaponTestSession()
        {
            List<ThingDef> weaponDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(IsTargetMeleeWeaponDef)
                .OrderBy(def => def.defName)
                .ToList();

            foreach (ThingDef weaponDef in weaponDefs)
            {
                RegisterDiscoveredCase(AutoTestCaseType.MeleeWeaponPocketMap, "ThingDef", weaponDef.defName, weaponDef.label);
                pendingMeleeWeaponDefs.Enqueue(weaponDef);
            }

            Log.Message("[LogSyncAutoTest] Melee weapon queue prepared. ConcurrentLimit=" + MaxConcurrentTurretTests + " Total=" + pendingMeleeWeaponDefs.Count);
        }

        private void StartNewMeleeWeaponSession(ThingDef weaponDef)
        {
            AutoTestResult result = CreateResult(AutoTestCaseType.MeleeWeaponPocketMap, "ThingDef", weaponDef.defName, weaponDef.label);
            MeleeWeaponTestSession session = new MeleeWeaponTestSession(this, weaponDef, result);
            LogSyncAutoTestSpecDef spec = ResolveBestMatchingSpec(AutoTestKind.MeleeWeaponPocketMap, weaponDef);
            session.AllowScreenshots = ShouldCaptureScreenshots(spec);
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
                activeMeleeWeaponSessions.Add(session);
                Log.Message("[LogSyncAutoTest] Starting melee pocket map for '" + weaponDef.defName + "' in Map " + session.TestMap.uniqueID + ". SourceMap=" + map.uniqueID + ".");
            }
            catch (Exception ex)
            {
                result.Status = "failed_map_creation";
                result.ErrorMessage = ex.ToString();
                results.Add(result);
                DestroyPocketMap(session.TestMap, session.Parent);
            }
        }

        private bool IsTargetMeleeWeaponDef(ThingDef def)
        {
            return def != null &&
                def.modContentPack == targetMod &&
                def.category == ThingCategory.Item &&
                def.IsWeapon &&
                def.IsMeleeWeapon &&
                MatchesSpecFilters(AutoTestKind.MeleeWeaponPocketMap, def);
        }
    }
}
