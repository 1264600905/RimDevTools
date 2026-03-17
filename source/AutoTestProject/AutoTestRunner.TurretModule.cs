using System;
using System.Collections.Generic;
using System.Linq;
using LogSyncMod;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class AutoTestRunner
    {
        private sealed class TurretTestSession
        {
            private const int MapReadyWaitTicks = 180;
            private const int FightDurationTicks = 60 * 60;
            private const int ObservationTicks = 60 * 10;
            private const int VirtualProjectilePrimeTick = 5;
            private const int ScreenshotFocusWaitTicks = 6;
            private const int ScreenshotWaitTicks = 10;

            internal sealed class StageScreenshotRequest
            {
                public string StageSuffix;
                public bool Force;
                public bool AllowCapture;
                public Vector3? FocusRootPos;
                public float? FocusRootSize;
                public int FocusDelayTicks;
                public bool PrimeOnly;
            }

            private readonly AutoTestRunner owner;

            public Map TestMap;
            public ThingDef TurretDef;
            public Thing SpawnedTurret;
            public Thing PowerGenerator;
            public Pawn OperatorPawn;
            public List<Pawn> EnemyPawns = new List<Pawn>();
            public Dictionary<int, float> EnemyInitialHealthPercent = new Dictionary<int, float>();
            public TurretTestPhase Phase;
            public AutoTestResult Result;
            public int TimeoutTicks;
            public int DelayTicks;
            public int FightingStartTick;
            public int WaitTicks;
            public MapParent Parent;
            public string CleanupReason = "initialized";
            public int FirstKillTick = -1;
            public string FirstKillPawnLabel = string.Empty;
            public int FirstDamageTick = -1;
            public string FirstDamagePawnLabel = string.Empty;
            public bool TurretScreenshotRequested;
            public bool TurretScreenshotForce;
            public bool TurretScreenshotCaptured;
            public bool AllowScreenshots;
            public bool CaptureProjectileScreenshots;
            public List<int> ProjectileScreenshotTicks = new List<int>();
            public bool ProjectileShotDetected;
            public int ProjectileShotTick = -1;
            public int NextProjectileScreenshotIndex;
            public bool ProjectileScreenshotPrimed;
            public Dictionary<int, Vector3> ProjectileLastPositions = new Dictionary<int, Vector3>();
            public Dictionary<int, int> ProjectileLastTicks = new Dictionary<int, int>();
            private readonly Queue<StageScreenshotRequest> PendingStageScreenshots = new Queue<StageScreenshotRequest>();
            internal StageScreenshotRequest ActiveStageScreenshot;
            private int PendingStageScreenshotDelayTicks;

            public TurretTestSession(AutoTestRunner owner, ThingDef turretDef, AutoTestResult result)
            {
                this.owner = owner;
                TurretDef = turretDef;
                Result = result;
                Phase = TurretTestPhase.Initializing;
                TimeoutTicks = 60 * 120;
            }

            public void Update()
            {
                if (TimeoutTicks > 0)
                {
                    TimeoutTicks--;
                    if (TimeoutTicks == 0)
                    {
                        Fail("timeout", "Total session timeout reached.");
                        return;
                    }
                }

                try
                {
                    if (!EnsureMapAlive("tick_" + Phase))
                    {
                        return;
                    }

                    switch (Phase)
                    {
                        case TurretTestPhase.Initializing:
                            WaitTicks = MapReadyWaitTicks;
                            LogMapState("initializing");
                            Phase = TurretTestPhase.WaitingForMap;
                            break;

                        case TurretTestPhase.WaitingForMap:
                            if (TestMap != null && Find.Maps.Contains(TestMap))
                            {
                                LogMapState("map_ready");
                                Phase = TurretTestPhase.SpawningPower;
                            }
                            else if (WaitTicks > 0)
                            {
                                if (WaitTicks == MapReadyWaitTicks || WaitTicks % 60 == 0)
                                {
                                    LogMapState("waiting_for_map");
                                }

                                WaitTicks--;
                            }
                            else
                            {
                                Fail("map_not_ready", "Map failed to stabilize in Find.Maps after " + MapReadyWaitTicks + " ticks.");
                            }

                            break;

                        case TurretTestPhase.SpawningPower:
                            LogMapState("before_spawn_power");
                            IntVec3 powerPos = new IntVec3(3, 0, 3);
                            PowerGenerator = GenSpawn.Spawn(ThingDef.Named("LogSync_AutoTestPowerGenerator"), powerPos, TestMap);
                            if (PowerGenerator == null)
                            {
                                Fail("failed_power_spawn", "Power generator returned null when spawning.");
                                return;
                            }

                            PreparePowerGenerator();
                            Result.Notes = AppendNote(Result.Notes, "MapSize=" + TestMap.Size.x + "x" + TestMap.Size.z);
                            Result.Stage = "power";
                            Phase = TurretTestPhase.SpawningTurret;
                            break;

                        case TurretTestPhase.SpawningTurret:
                            LogMapState("before_spawn_turret");
                            ThingDef turretStuff = owner.ResolveStuff(TurretDef);
                            Thing turretThing = ThingMaker.MakeThing(TurretDef, turretStuff);
                            SpawnedTurret = turretThing != null ? GenSpawn.Spawn(turretThing, TestMap.Center, TestMap) : null;
                            if (SpawnedTurret == null)
                            {
                                Fail("failed_turret_spawn", "Turret returned null when spawning.");
                                return;
                            }

                            ConnectPower();
                            PrepareTurretForCombat();
                            Result.Label = SpawnedTurret.LabelCap;
                            Result.Stage = "spawn";
                            Phase = TurretTestPhase.Reloading;
                            break;

                        case TurretTestPhase.Reloading:
                            ReloadTurret();
                            Result.Stage = "reload";
                            Phase = TurretTestPhase.SpawningEnemies;
                            break;

                        case TurretTestPhase.SpawningEnemies:
                            LogMapState("before_spawn_enemies");
                            SpawnEnemies(5);
                            EnsureOperatorManning();
                            LogTurretState("after_spawn_enemies");
                            Result.Stage = "fighting_60s";
                            DelayTicks = FightDurationTicks;
                            Phase = TurretTestPhase.Fighting;
                            break;

                        case TurretTestPhase.Fighting:
                            EnsureOperatorManning();
                            UpdateProjectileScreenshotSchedule();
                            if (ProcessPendingStageScreenshotQueue())
                            {
                                break;
                            }

                            if (TryBeginPostKillObservation())
                            {
                                break;
                            }

                            if (TryBeginPostDamageObservation())
                            {
                                break;
                            }

                            if (DelayTicks > 0)
                            {
                                DelayTicks--;
                                if (DelayTicks % 600 == 0)
                                {
                                    int remainingSec = DelayTicks / 60;
                                    Result.Stage = $"fighting_{remainingSec}s";
                                    Log.Message($"[LogSyncAutoTest] Turret '{TurretDef.defName}' in Map {TestMap.uniqueID} fighting... {remainingSec}s left.");
                                }
                            }
                            else
                            {
                                Result.Status = "failed_no_damage";
                                Result.Notes = AppendNote(Result.Notes, "No target took damage within 60 seconds.");
                                Log.Message($"[LogSyncAutoTest] Combat timeout for '{TurretDef.defName}'. No target took damage within 60 seconds.");
                                LogTurretState("combat_timeout");
                                TransitionToCleanup("test_failed_no_damage", forceScreenshot: true);
                            }

                            break;

                        case TurretTestPhase.WaitingAfterKill:
                            EnsureOperatorManning();
                            UpdateProjectileScreenshotSchedule();
                            if (ProcessPendingStageScreenshotQueue())
                            {
                                break;
                            }
                            if (DelayTicks > 0)
                            {
                                DelayTicks--;
                                if (DelayTicks % 120 == 0)
                                {
                                    int remainingSec = DelayTicks / 60;
                                    Result.Stage = $"post_kill_{remainingSec}s";
                                }
                            }
                            else
                            {
                                int deadCount = EnemyPawns.Count(p => p == null || p.Destroyed || p.Dead || p.Downed);
                                Result.Notes = AppendNote(Result.Notes, $"Final results: {deadCount}/5 targets eliminated.");
                                Result.Status = "passed";
                                Log.Message($"[LogSyncAutoTest] Observation finished for '{TurretDef.defName}'. Result: {Result.Status}. Closing map.");
                                LogTurretState("post_kill_observation_finished");
                                TransitionToCleanup("test_passed_post_kill_delay", forceScreenshot: false);
                            }

                            break;

                        case TurretTestPhase.CapturingScreenshot:
                            if (!TurretScreenshotRequested)
                            {
                                TurretScreenshotRequested = true;
                                Result.Stage = "capturing_screenshot";
                                if (!owner.FocusTurretScreenshotMap(this))
                                {
                                    Phase = TurretTestPhase.CleaningUp;
                                    break;
                                }

                                DelayTicks = ScreenshotFocusWaitTicks;
                                break;
                            }

                            if (!TurretScreenshotCaptured)
                            {
                                if (DelayTicks > 0)
                                {
                                    DelayTicks--;
                                    break;
                                }

                                if (!owner.TryCaptureTurretScreenshot(this, TurretScreenshotForce))
                                {
                                    Phase = TurretTestPhase.CleaningUp;
                                    break;
                                }

                                TurretScreenshotCaptured = true;
                                DelayTicks = ScreenshotWaitTicks;
                                break;
                            }

                            if (DelayTicks > 0)
                            {
                                DelayTicks--;
                            }
                            else
                            {
                                Phase = TurretTestPhase.CleaningUp;
                            }

                            break;

                        case TurretTestPhase.CleaningUp:
                            if (OperatorPawn != null && !OperatorPawn.Destroyed)
                            {
                                OperatorPawn.Destroy();
                                OperatorPawn = null;
                            }

                            Log.Message($"[LogSyncAutoTest] [MapCleanup] Starting cleanup for '{TurretDef.defName}' in Map {TestMap?.uniqueID ?? -1}. Reason: {CleanupReason}");
                            if (TestMap != null && Find.Maps.Contains(TestMap))
                            {
                                int mapId = TestMap.uniqueID;
                                PocketMapUtility.DestroyPocketMap(TestMap);
                                Log.Message($"[LogSyncAutoTest] [MapCleanup] Successfully destroyed official PocketMap {mapId}.");
                            }
                            else if (Parent != null && !Parent.Destroyed)
                            {
                                Parent.Destroy();
                                Log.Message($"[LogSyncAutoTest] [MapCleanup] Successfully destroyed MapParent.");
                            }

                            if (Current.Game != null && owner.map != null && Find.Maps.Contains(owner.map))
                            {
                                Current.Game.CurrentMap = owner.map;
                            }

                            Phase = TurretTestPhase.Done;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Fail("exception", ex.ToString());
                }
            }

            private void ConnectPower()
            {
                if (PowerGenerator == null || SpawnedTurret == null) return;
                EnsureConnectorRing(PowerGenerator);
                EnsureConnectorRing(SpawnedTurret);

                IntVec3 start = FindBestConnectorCell(PowerGenerator, SpawnedTurret.Position);
                IntVec3 end = FindBestConnectorCell(SpawnedTurret, PowerGenerator.Position);
                if (!start.IsValid || !end.IsValid)
                {
                    Result.Notes = AppendNote(Result.Notes, "PowerConnectorMissing=True");
                    Log.Warning($"[LogSyncAutoTest] Failed to find valid power connector cells for '{TurretDef.defName}'.");
                    return;
                }

                IntVec3 mid = new IntVec3(start.x, 0, end.z);
                SpawnConduit(start);
                SpawnLine(start, mid);
                SpawnLine(mid, end);
                SpawnConduit(end);
                Log.Message($"[LogSyncAutoTest] [PowerLink] turret='{TurretDef.defName}' start={start} end={end} mid={mid}");
            }

            private void SpawnLine(IntVec3 a, IntVec3 b)
            {
                foreach (IntVec3 cell in GenSight.PointsOnLineOfSight(a, b))
                {
                    SpawnConduit(cell);
                }
            }

            private void EnsureConnectorRing(Thing thing)
            {
                if (thing == null)
                {
                    return;
                }

                foreach (IntVec3 cell in EnumerateAdjacentCardinalCells(thing.OccupiedRect()))
                {
                    SpawnConduit(cell);
                }
            }

            private IntVec3 FindBestConnectorCell(Thing thing, IntVec3 toward)
            {
                if (thing == null)
                {
                    return IntVec3.Invalid;
                }

                IntVec3 bestCell = IntVec3.Invalid;
                float bestDistance = float.MaxValue;
                foreach (IntVec3 cell in EnumerateAdjacentCardinalCells(thing.OccupiedRect()))
                {
                    if (!cell.InBounds(TestMap))
                    {
                        continue;
                    }

                    float distance = cell.DistanceToSquared(toward);
                    if (!bestCell.IsValid || distance < bestDistance)
                    {
                        bestCell = cell;
                        bestDistance = distance;
                    }
                }

                return bestCell;
            }

            private IEnumerable<IntVec3> EnumerateAdjacentCardinalCells(CellRect rect)
            {
                HashSet<IntVec3> yielded = new HashSet<IntVec3>();

                for (int x = rect.minX; x <= rect.maxX; x++)
                {
                    IntVec3 north = new IntVec3(x, 0, rect.maxZ + 1);
                    IntVec3 south = new IntVec3(x, 0, rect.minZ - 1);
                    if (yielded.Add(north)) yield return north;
                    if (yielded.Add(south)) yield return south;
                }

                for (int z = rect.minZ; z <= rect.maxZ; z++)
                {
                    IntVec3 east = new IntVec3(rect.maxX + 1, 0, z);
                    IntVec3 west = new IntVec3(rect.minX - 1, 0, z);
                    if (yielded.Add(east)) yield return east;
                    if (yielded.Add(west)) yield return west;
                }
            }

            private void SpawnConduit(IntVec3 cell)
            {
                if (!cell.InBounds(TestMap) || cell.GetFirstThing(TestMap, ThingDefOf.PowerConduit) != null)
                {
                    return;
                }

                Thing conduit = GenSpawn.Spawn(ThingDefOf.PowerConduit, cell, TestMap);
                conduit.SetFaction(Faction.OfPlayer);
            }

            private void PrepareTurretForCombat()
            {
                Faction playerFaction = Faction.OfPlayer;
                if (playerFaction == null)
                {
                    Result.Notes = AppendNote(Result.Notes, "PlayerFactionMissing=True");
                    return;
                }

                if (PowerGenerator != null && PowerGenerator.def.CanHaveFaction && PowerGenerator.Faction != playerFaction)
                {
                    PowerGenerator.SetFaction(playerFaction);
                }

                if (SpawnedTurret != null && SpawnedTurret.def.CanHaveFaction && SpawnedTurret.Faction != playerFaction)
                {
                    SpawnedTurret.SetFaction(playerFaction);
                }

                var dormantComp = SpawnedTurret?.TryGetComp<CompCanBeDormant>();
                dormantComp?.WakeUp();

                if (SpawnedTurret is Building_TurretGun turretGun && turretGun.gun == null)
                {
                    turretGun.MakeGun();
                }

                EnsureOperatorManning();
                LogTurretState("prepared_for_combat");
            }

            private void PreparePowerGenerator()
            {
                if (PowerGenerator == null)
                {
                    return;
                }

                Faction playerFaction = Faction.OfPlayer;
                if (playerFaction != null && PowerGenerator.def.CanHaveFaction && PowerGenerator.Faction != playerFaction)
                {
                    PowerGenerator.SetFaction(playerFaction);
                }

                var powerPlantComp = PowerGenerator.TryGetComp<CompPowerPlant>();
                if (powerPlantComp != null)
                {
                    powerPlantComp.PowerOn = true;
                    powerPlantComp.UpdateDesiredPowerOutput();
                }

                var powerTraderComp = PowerGenerator.TryGetComp<CompPowerTrader>();
                string message =
                    "[LogSyncAutoTest] [PowerState] generator='" + PowerGenerator.def.defName +
                    "' faction=" + (PowerGenerator.Faction?.Name ?? "null") +
                    " powerOn=" + (powerTraderComp?.PowerOn.ToString() ?? "n/a") +
                    " powerOutput=" + (powerTraderComp?.PowerOutput.ToString() ?? "n/a");
                Log.Message(message);
                Result.Notes = AppendNote(Result.Notes, "GeneratorPowerOn=" + (powerTraderComp?.PowerOn.ToString() ?? "n/a"));
            }

            private void ReloadTurret()
            {
                if (SpawnedTurret == null) return;
                var refuelable = SpawnedTurret.TryGetComp<CompRefuelable>();
                if (refuelable != null) refuelable.Refuel(refuelable.Props.fuelCapacity);

                try
                {
                    var comps = (SpawnedTurret as ThingWithComps)?.AllComps;
                    if (comps != null)
                    {
                        var ammoComp = comps.FirstOrDefault(c => c != null && c.GetType().Name.Contains("CompAmmoUser"));
                        if (ammoComp != null)
                        {
                            var t = ammoComp.GetType();
                            var selectedAmmo = t.GetProperty("SelectedAmmo")?.GetValue(ammoComp, null);
                            if (selectedAmmo != null) t.GetProperty("CurrentAmmo")?.SetValue(ammoComp, selectedAmmo, null);
                            var magSizeProp = t.GetProperty("MagSize");
                            if (magSizeProp != null)
                            {
                                object magSizeVal = magSizeProp.GetValue(ammoComp, null);
                                if (magSizeVal != null) t.GetProperty("CurMagCount")?.SetValue(ammoComp, (int)magSizeVal, null);
                            }
                        }
                    }

                    if (SpawnedTurret.GetType().Name.Contains("Building_TurretGunCE"))
                    {
                        var compAmmoProp = SpawnedTurret.GetType().GetProperty("CompAmmo");
                        var compAmmo = compAmmoProp?.GetValue(SpawnedTurret, null);
                        if (compAmmo != null)
                        {
                            var t = compAmmo.GetType();
                            var selectedAmmo = t.GetProperty("SelectedAmmo")?.GetValue(compAmmo, null);
                            if (selectedAmmo != null) t.GetProperty("CurrentAmmo")?.SetValue(compAmmo, selectedAmmo, null);
                            var magSizeProp = t.GetProperty("MagSize");
                            if (magSizeProp != null)
                            {
                                object magSizeVal = magSizeProp.GetValue(compAmmo, null);
                                if (magSizeVal != null) t.GetProperty("CurMagCount")?.SetValue(compAmmo, (int)magSizeVal, null);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[LogSyncAutoTest] CE reload reflection failed for {SpawnedTurret.def.defName}: {ex.Message}");
                }
            }

            private void SpawnEnemies(int count)
            {
                if (!EnsureMapAlive("spawn_enemies"))
                {
                    return;
                }

                float range = 15f;
                if (SpawnedTurret is Building_TurretGun turretGun)
                {
                    if (turretGun.GunCompEq?.PrimaryVerb != null) range = turretGun.GunCompEq.PrimaryVerb.verbProps.range;
                    else if (turretGun.AttackVerb != null) range = turretGun.AttackVerb.verbProps.range;
                }
                else
                {
                    try
                    {
                        var gunProp = SpawnedTurret.GetType().GetProperty("Gun");
                        var gun = gunProp?.GetValue(SpawnedTurret, null);
                        if (gun is ThingWithComps twc)
                        {
                            var verbs = (twc as IVerbOwner)?.VerbTracker?.AllVerbs;
                            var mainVerb = verbs?.FirstOrDefault(v => v != null && v.verbProps != null && v.verbProps.range > 0);
                            if (mainVerb != null) range = mainVerb.verbProps.range;
                        }
                    }
                    catch
                    {
                    }
                }

                List<IntVec3> spawnCells = FindEnemySpawnCells(count, range);
                if (spawnCells.Count == 0)
                {
                    Fail("failed_enemy_spawn_pos", "No valid enemy spawn cells were found within turret range and edge-safe area.");
                    return;
                }

                Log.Message($"[LogSyncAutoTest] Spawning {count} enemies for turret '{TurretDef.defName}' in Map {TestMap.uniqueID}. SpawnCells={string.Join(", ", spawnCells)}.");

                for (int i = 0; i < count && i < spawnCells.Count; i++)
                {
                    IntVec3 spawnPos = spawnCells[i];

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
                        if (pawn != null && TestMap != null)
                        {
                            GenSpawn.Spawn(pawn, spawnPos, TestMap);
                            EnemyInitialHealthPercent[pawn.thingIDNumber] = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
                            var targetHediff = DefDatabase<HediffDef>.GetNamedSilentFail("LogSync_TurretTestTarget");
                            if (targetHediff != null)
                            {
                                HealthUtility.AdjustSeverity(pawn, targetHediff, 1.0f);
                            }

                            EnemyPawns.Add(pawn);
                            Log.Message($"[LogSyncAutoTest] Enemy #{i + 1} ({kind.defName}) spawned at {spawnPos} for turret '{TurretDef.defName}'.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[LogSyncAutoTest] Enemy spawn CRITICAL failed for turret '{TurretDef.defName}': {ex}");
                    }
                }

                Log.Message($"[LogSyncAutoTest] Spawn process for '{TurretDef.defName}' finished. Total enemy count: {EnemyPawns.Count}.");
            }

            private void UpdateProjectileScreenshotSchedule()
            {
                if (!CaptureProjectileScreenshots || ProjectileScreenshotTicks == null || ProjectileScreenshotTicks.Count == 0)
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
                    Log.Message($"[LogSyncAutoTest] Projectile detected for turret '{TurretDef.defName}' at tick {ProjectileShotTick}.");
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
                    Result.Notes = AppendNote(Result.Notes, "ProjectileScreenshotPrimed=True");
                }

                if (NextProjectileScreenshotIndex >= ProjectileScreenshotTicks.Count)
                {
                    UpdateProjectileHistory(activeProjectiles);
                    return;
                }

                int offset = ProjectileScreenshotTicks[NextProjectileScreenshotIndex];
                if (currentTick - ProjectileShotTick < offset)
                {
                    UpdateProjectileHistory(activeProjectiles);
                    return;
                }

                if (TryCaptureCenteredProjectileScreenshot(activeProjectiles, currentTick, offset))
                {
                    NextProjectileScreenshotIndex++;
                }

                UpdateProjectileHistory(activeProjectiles);
            }

            private bool HasActiveProjectile()
            {
                if (TestMap == null || !Find.Maps.Contains(TestMap))
                {
                    return false;
                }

                IEnumerable<Thing> projectiles = TestMap.listerThings?.ThingsInGroup(ThingRequestGroup.Projectile);
                if (projectiles == null)
                {
                    return false;
                }

                return projectiles.Any(projectile => projectile != null && !projectile.Destroyed);
            }

            private List<Thing> GetActiveProjectiles()
            {
                if (TestMap == null || !Find.Maps.Contains(TestMap))
                {
                    return new List<Thing>();
                }

                IEnumerable<Thing> projectiles = TestMap.listerThings?.ThingsInGroup(ThingRequestGroup.Projectile);
                if (projectiles == null)
                {
                    return new List<Thing>();
                }

                return projectiles
                    .Where(projectile => projectile != null && !projectile.Destroyed && projectile.Spawned && projectile.Map == TestMap)
                    .ToList();
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
                if (!owner.TryResolveTurretScreenshotView(this, aspect, out Vector3 screenshotCenter, out float rootSize))
                {
                    return false;
                }

                Thing bestProjectile = null;
                float bestDistance = float.MaxValue;
                float bestSpeed = 0f;
                foreach (Thing projectile in activeProjectiles)
                {
                    if (!ProjectileLastPositions.TryGetValue(projectile.thingIDNumber, out Vector3 previousPos) ||
                        !ProjectileLastTicks.TryGetValue(projectile.thingIDNumber, out int previousTick))
                    {
                        continue;
                    }

                    int deltaTicks = Math.Max(1, currentTick - previousTick);
                    Vector3 currentPos = projectile.DrawPos;
                    Vector3 segment = currentPos - previousPos;
                    Vector3 velocityPerTick = segment / deltaTicks;
                    float speedPerTick = HorizontalMagnitude(velocityPerTick);
                    if (speedPerTick <= 0.001f)
                    {
                        continue;
                    }

                    Vector3 closestPoint = ClosestPointOnHorizontalSegment(previousPos, currentPos, screenshotCenter, out float segmentProgress);
                    float closestDistance = HorizontalDistance(closestPoint, screenshotCenter);
                    float captureRadius = Mathf.Max(0.35f, rootSize * 0.035f);
                    if (closestDistance > captureRadius)
                    {
                        continue;
                    }

                    if (segmentProgress <= 0f || segmentProgress > 1f)
                    {
                        continue;
                    }

                    if (closestDistance < bestDistance)
                    {
                        bestProjectile = projectile;
                        bestDistance = closestDistance;
                        bestSpeed = speedPerTick;
                    }
                }

                if (bestProjectile == null)
                {
                    return false;
                }

                Vector3? focusRootPos = null;
                float? focusRootSize = null;
                if (owner.TryResolveProjectileFocusView(this, bestProjectile, aspect, out Vector3 resolvedRootPos, out float resolvedRootSize))
                {
                    focusRootPos = resolvedRootPos;
                    focusRootSize = resolvedRootSize;
                }

                Result.Notes = AppendNote(
                    Result.Notes,
                    $"ProjectileCenteredCapture=tick:{currentTick},offset:{offset},dist:{bestDistance:0.00},speed:{bestSpeed:0.00}");
                QueueStageScreenshot("projectile_center_t" + offset, force: true, allowCapture: true, focusRootPos, focusRootSize);
                return true;
            }

            private bool ProcessPendingStageScreenshotQueue()
            {
                if (ActiveStageScreenshot == null && PendingStageScreenshots.Count == 0)
                {
                    return false;
                }

                if (ActiveStageScreenshot == null)
                {
                    ActiveStageScreenshot = PendingStageScreenshots.Dequeue();
                    PendingStageScreenshotDelayTicks = Math.Max(0, ActiveStageScreenshot.FocusDelayTicks);
                }

                if (!owner.FocusTurretScreenshotMap(this))
                {
                    ActiveStageScreenshot = null;
                    PendingStageScreenshotDelayTicks = 0;
                    return false;
                }

                if (PendingStageScreenshotDelayTicks > 0)
                {
                    PendingStageScreenshotDelayTicks--;
                    return true;
                }

                if (ActiveStageScreenshot.PrimeOnly)
                {
                    ActiveStageScreenshot = null;
                    return true;
                }

                owner.TryCaptureTurretStageScreenshot(
                    this,
                    ActiveStageScreenshot.StageSuffix,
                    ActiveStageScreenshot.Force,
                    ActiveStageScreenshot.AllowCapture);
                ActiveStageScreenshot = null;
                return true;
            }

            private void QueueStageScreenshot(
                string stageSuffix,
                bool force,
                bool allowCapture,
                Vector3? focusRootPos = null,
                float? focusRootSize = null,
                int focusDelayTicks = 0,
                bool primeOnly = false)
            {
                PendingStageScreenshots.Enqueue(new StageScreenshotRequest
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

            private List<IntVec3> FindEnemySpawnCells(int count, float range)
            {
                List<IntVec3> result = new List<IntVec3>();
                if (TestMap == null || SpawnedTurret == null || count <= 0)
                {
                    return result;
                }

                CellRect safeRect = CellRect.FromLimits(5, 5, TestMap.Size.x - 6, TestMap.Size.z - 6);
                if (safeRect.Width <= 0 || safeRect.Height <= 0)
                {
                    return result;
                }

                IntVec3 turretCell = SpawnedTurret.Position;
                IntVec3[] directions =
                {
                    new IntVec3(0, 0, 1),
                    new IntVec3(1, 0, 0),
                    new IntVec3(0, 0, -1),
                    new IntVec3(-1, 0, 0)
                };

                IntVec3 bestDirection = new IntVec3(0, 0, 1);
                int bestDistance = -1;
                foreach (IntVec3 direction in directions)
                {
                    int maxDistanceInSafeRect = MaxDistanceInSafeRect(turretCell, direction, safeRect);
                    int candidateDistance = Mathf.Min(Mathf.RoundToInt(range * 0.7f), maxDistanceInSafeRect);
                    if (candidateDistance > bestDistance)
                    {
                        bestDistance = candidateDistance;
                        bestDirection = direction;
                    }
                }

                if (bestDistance < 1)
                {
                    return result;
                }

                int targetDistance = Mathf.Max(1, bestDistance);
                IntVec3 anchor = turretCell + bestDirection * targetDistance;
                if (!anchor.InBounds(TestMap) || !safeRect.Contains(anchor))
                {
                    return result;
                }

                IntVec3 lateral = new IntVec3(bestDirection.z, 0, -bestDirection.x);
                int lateralRadius = Mathf.Max(count, 3);
                List<IntVec3> candidates = new List<IntVec3>();
                for (int offset = -lateralRadius; offset <= lateralRadius; offset++)
                {
                    IntVec3 candidate = anchor + lateral * offset;
                    if (!candidate.InBounds(TestMap) || !safeRect.Contains(candidate))
                    {
                        continue;
                    }

                    float distanceToTurret = candidate.DistanceTo(SpawnedTurret.Position);
                    if (distanceToTurret > range)
                    {
                        continue;
                    }

                    if (SpawnedTurret.Position.DistanceTo(candidate) < 5f)
                    {
                        continue;
                    }

                    if (!GenSight.LineOfSight(SpawnedTurret.Position, candidate, TestMap))
                    {
                        continue;
                    }

                    candidates.Add(candidate);
                }

                foreach (IntVec3 candidate in candidates.OrderBy(cell => Mathf.Abs((cell - anchor).LengthHorizontalSquared)))
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

            private void EnsureOperatorManning()
            {
                if (SpawnedTurret == null || TestMap == null)
                {
                    return;
                }

                var mannableComp = SpawnedTurret.TryGetComp<CompMannable>();
                if (mannableComp == null)
                {
                    return;
                }

                if (OperatorPawn == null || OperatorPawn.Destroyed || OperatorPawn.Map != TestMap)
                {
                    SpawnOperatorPawn();
                }

                if (OperatorPawn == null || OperatorPawn.Destroyed || !OperatorPawn.Spawned)
                {
                    Result.Notes = AppendNote(Result.Notes, "OperatorMissing=True");
                    return;
                }

                IntVec3 interactionCell = SpawnedTurret.InteractionCell;
                if (interactionCell.IsValid && interactionCell.InBounds(TestMap) && OperatorPawn.Position != interactionCell)
                {
                    OperatorPawn.Position = interactionCell;
                }

                mannableComp.ManForATick(OperatorPawn);
            }

            private void SpawnOperatorPawn()
            {
                Faction playerFaction = Faction.OfPlayer;
                if (playerFaction == null || TestMap == null || SpawnedTurret == null)
                {
                    return;
                }

                IntVec3 spawnPos = SpawnedTurret.InteractionCell;
                if (!spawnPos.IsValid || !spawnPos.InBounds(TestMap))
                {
                    spawnPos = CellFinder.RandomClosewalkCellNear(SpawnedTurret.Position, TestMap, 2);
                }

                OperatorPawn = null;
                for (int attempt = 0; attempt < 5 && OperatorPawn == null; attempt++)
                {
                    var request = new PawnGenerationRequest(
                        PawnKindDefOf.Colonist,
                        playerFaction,
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
                    if (violentDisabled || !canManipulate)
                    {
                        candidate.Destroy();
                        continue;
                    }

                    OperatorPawn = candidate;
                }

                if (OperatorPawn == null)
                {
                    Result.Notes = AppendNote(Result.Notes, "OperatorSpawnFailed=True");
                    return;
                }

                GenSpawn.Spawn(OperatorPawn, spawnPos, TestMap);
                OperatorPawn.Name = new NameSingle("自动测试炮手");
                Result.Notes = AppendNote(Result.Notes, "OperatorPawnSpawned=True");
            }

            private void LogTurretState(string stageTag)
            {
                if (SpawnedTurret == null)
                {
                    Log.Message($"[LogSyncAutoTest] [TurretState] turret='{TurretDef.defName}' stage={stageTag} spawned=False");
                    return;
                }

                var powerComp = SpawnedTurret.TryGetComp<CompPowerTrader>();
                var dormantComp = SpawnedTurret.TryGetComp<CompCanBeDormant>();
                var mannableComp = SpawnedTurret.TryGetComp<CompMannable>();
                var turretGun = SpawnedTurret as Building_TurretGun;

                string message =
                    "[LogSyncAutoTest] [TurretState] turret='" + TurretDef.defName +
                    "' stage=" + stageTag +
                    " faction=" + (SpawnedTurret.Faction?.Name ?? "null") +
                    " position=" + SpawnedTurret.Position +
                    " active=" + (turretGun?.Active.ToString() ?? "n/a") +
                    " hasGun=" + ((turretGun?.gun) != null) +
                    " powerOn=" + (powerComp?.PowerOn.ToString() ?? "n/a") +
                    " awake=" + (dormantComp?.Awake.ToString() ?? "n/a") +
                    " mannable=" + (mannableComp != null) +
                    " mannedNow=" + (mannableComp?.MannedNow.ToString() ?? "n/a") +
                    " operator=" + (mannableComp?.ManningPawn?.LabelShortCap ?? OperatorPawn?.LabelShortCap ?? "null") +
                    " attackVerb=" + (turretGun?.AttackVerb != null) +
                    " attackVerbAvailable=" + (turretGun?.AttackVerb?.Available().ToString() ?? "n/a");

                Log.Message(message);
                Result.Notes = AppendNote(Result.Notes, stageTag + "_Active=" + (turretGun?.Active.ToString() ?? "n/a"));
            }

            private void TransitionToCleanup(string cleanupReason, bool forceScreenshot)
            {
                CleanupReason = cleanupReason;
                TurretScreenshotForce = forceScreenshot;
                TurretScreenshotRequested = false;
                TurretScreenshotCaptured = false;
                bool shouldCaptureFinalScreenshot = forceScreenshot ||
                    (AllowScreenshots && !(CaptureProjectileScreenshots && Result?.ScreenshotPaths != null && Result.ScreenshotPaths.Count > 0));

                if (shouldCaptureFinalScreenshot && TestMap != null && Find.Maps.Contains(TestMap))
                {
                    Phase = TurretTestPhase.CapturingScreenshot;
                }
                else
                {
                    Phase = TurretTestPhase.CleaningUp;
                }
            }

            private bool TryBeginPostKillObservation()
            {
                Pawn firstKilledPawn = null;
                bool foundKilledPawn = false;
                foreach (Pawn pawn in EnemyPawns)
                {
                    if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.Downed)
                    {
                        firstKilledPawn = pawn;
                        foundKilledPawn = true;
                        break;
                    }
                }

                if (!foundKilledPawn)
                {
                    return false;
                }

                FirstKillTick = Find.TickManager.TicksGame;
                FirstKillPawnLabel = firstKilledPawn == null ? "destroyed_target" : firstKilledPawn.LabelShortCap;
                DelayTicks = ObservationTicks;
                Result.Stage = "post_kill_10s";
                Result.Notes = AppendNote(Result.Notes, "FirstKillTarget=" + FirstKillPawnLabel);
                Result.Notes = AppendNote(Result.Notes, "FirstKillTick=" + FirstKillTick);
                Log.Message($"[LogSyncAutoTest] First target downed or killed for '{TurretDef.defName}'. Target={FirstKillPawnLabel}. Starting 10s pass countdown.");
                Phase = TurretTestPhase.WaitingAfterKill;
                CleanupReason = "waiting_post_kill_delay";
                return true;
            }

            private bool TryBeginPostDamageObservation()
            {
                Pawn firstDamagedPawn = null;
                foreach (Pawn pawn in EnemyPawns)
                {
                    if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.health?.summaryHealth == null)
                    {
                        continue;
                    }

                    if (!EnemyInitialHealthPercent.TryGetValue(pawn.thingIDNumber, out float initialHealthPercent))
                    {
                        continue;
                    }

                    float currentHealthPercent = pawn.health.summaryHealth.SummaryHealthPercent;
                    if (currentHealthPercent < initialHealthPercent - 0.0001f)
                    {
                        firstDamagedPawn = pawn;
                        break;
                    }
                }

                if (firstDamagedPawn == null)
                {
                    return false;
                }

                FirstDamageTick = Find.TickManager.TicksGame;
                FirstDamagePawnLabel = firstDamagedPawn.LabelShortCap;
                DelayTicks = ObservationTicks;
                Result.Stage = "post_damage_10s";
                Result.Notes = AppendNote(Result.Notes, "FirstDamageTarget=" + FirstDamagePawnLabel);
                Result.Notes = AppendNote(Result.Notes, "FirstDamageTick=" + FirstDamageTick);
                Log.Message($"[LogSyncAutoTest] First target damaged for '{TurretDef.defName}'. Target={FirstDamagePawnLabel}. Starting 10s pass countdown.");
                Phase = TurretTestPhase.WaitingAfterKill;
                CleanupReason = "waiting_post_damage_delay";
                return true;
            }

            private void Fail(string status, string message = "")
            {
                Result.Status = status;
                Result.ErrorMessage = message;

                if (Phase == TurretTestPhase.CleaningUp)
                {
                    Log.Error($"[LogSyncAutoTest] Fatal error during cleanup of '{TurretDef.defName}': {message}");
                    Phase = TurretTestPhase.Done;
                    return;
                }

                CleanupReason = $"failed_{status}";
                Log.Warning($"[LogSyncAutoTest] Turret test FAILED for '{TurretDef.defName}': {status} - {message}. Scheduled for cleanup.");
                LogTurretState("failure_" + status);
                TransitionToCleanup($"failed_{status}", forceScreenshot: true);
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
                    string parentState = Parent == null
                        ? "parent=null"
                        : "parentDestroyed=" + Parent.Destroyed;
                    string sourceMapState = (TestMap.Parent as PocketMapParent)?.sourceMap != null
                        ? "sourceMap=" + (TestMap.Parent as PocketMapParent).sourceMap.uniqueID
                        : "sourceMap=null";
                    Fail("map_removed", "TestMap left Find.Maps before stage " + stageTag + ". " + parentState + " | " + sourceMapState);
                    return false;
                }

                return true;
            }

            private void LogMapState(string stageTag)
            {
                bool mapExists = TestMap != null && Find.Maps.Contains(TestMap);
                bool parentRegistered = Parent != null && Find.World.pocketMaps.Contains(Parent as PocketMapParent);
                int sourceMapId = ((TestMap?.Parent as PocketMapParent)?.sourceMap != null)
                    ? (TestMap.Parent as PocketMapParent).sourceMap.uniqueID
                    : -1;
                string message =
                    "[LogSyncAutoTest] [PocketMapState] turret='" + TurretDef.defName +
                    "' stage=" + stageTag +
                    " mapId=" + (TestMap?.uniqueID ?? -1) +
                    " mapExists=" + mapExists +
                    " parentRegistered=" + parentRegistered +
                    " parentDestroyed=" + (Parent?.Destroyed ?? false) +
                    " sourceMap=" + sourceMapId;
                Log.Message(message);
                Result.Notes = AppendNote(Result.Notes, stageTag + "_mapExists=" + mapExists);
            }
        }

        private void BeginTurretTestSession()
        {
            var turretDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(IsTargetTurretDef)
                .OrderBy(d => d.defName)
                .ToList();

            foreach (ThingDef turretDef in turretDefs)
            {
                pendingTurretDefs.Enqueue(turretDef);
            }

            Log.Message("[LogSyncAutoTest] Turret queue prepared. ConcurrentLimit=" + MaxConcurrentTurretTests + " Total=" + pendingTurretDefs.Count);
        }

        private void StartNewTurretSession(ThingDef turretDef)
        {
            var result = CreateResult("Turret", "ThingDef", turretDef.defName, turretDef.label);
            var session = new TurretTestSession(this, turretDef, result);
            LogSyncAutoTestSpecDef spec = ResolveBestMatchingSpec(AutoTestKind.TurretPocketMap, turretDef);
            session.AllowScreenshots = ShouldCaptureScreenshots(spec);
            session.CaptureProjectileScreenshots = ShouldCaptureTurretProjectileScreenshots(spec);
            session.ProjectileScreenshotTicks = ResolveTurretProjectileScreenshotTicks(spec);
            result.Notes = AppendNote(result.Notes, "AutoTestMode=" + (settings.UseXmlDrivenAutoTest() ? "XmlDriven" : "Automatic"));
            if (spec != null)
            {
                result.Notes = AppendNote(result.Notes, "Spec=" + spec.defName);
            }

            try
            {
                var mapGenerator = DefDatabase<MapGeneratorDef>.GetNamed("LogSync_AutoTestPocketMap");
                var mapSize = new IntVec3(50, 1, 50);
                var pocketMapParent = WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.PocketMap) as PocketMapParent;
                if (pocketMapParent == null)
                {
                    result.Status = "failed_map_generation";
                    result.ErrorMessage = "Failed to create PocketMapParent.";
                    results.Add(result);
                    return;
                }

                pocketMapParent.sourceMap = this.map;
                pocketMapParent.mapGenerator = mapGenerator;
                session.Parent = pocketMapParent;

                session.TestMap = MapGenerator.GenerateMap(
                    mapSize,
                    pocketMapParent,
                    mapGenerator,
                    null,
                    generatedMap => MapGenerator.PlayerStartSpot = generatedMap.Center,
                    isPocketMap: true);

                if (session.TestMap != null)
                {
                    Find.World.pocketMaps.Add(pocketMapParent);
                    session.Parent = session.TestMap.Parent as PocketMapParent ?? pocketMapParent;
                    activeTurretSessions.Add(session);
                    Log.Message("[LogSyncAutoTest] Starting managed POCKET MAP for '" + turretDef.defName + "' in Map " + session.TestMap.uniqueID + ". SourceMap=" + this.map.uniqueID + ".");
                }
                else
                {
                    result.Status = "failed_map_generation";
                    result.ErrorMessage = "Managed pocket map generation returned null Map.";
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                result.Status = "failed_map_creation";
                result.ErrorMessage = ex.ToString();
                results.Add(result);
                if (session.TestMap != null)
                {
                    PocketMapUtility.DestroyPocketMap(session.TestMap);
                }
                else if (session.Parent != null && !session.Parent.Destroyed)
                {
                    session.Parent.Destroy();
                }
            }
        }

        private bool IsTargetTurretDef(ThingDef def)
        {
            if (def == null || def.modContentPack != targetMod || def.category != ThingCategory.Building || def.thingClass == null)
                return false;

            if (def.building?.turretGunDef != null)
                return MatchesSpecFilters(AutoTestKind.TurretPocketMap, def);

            Type type = def.thingClass;
            while (type != null && type != typeof(object))
            {
                if (type.Name == "Building_Turret" || type.Name == "Building_TurretGunCE")
                    return MatchesSpecFilters(AutoTestKind.TurretPocketMap, def);
                type = type.BaseType;
            }

            return false;
        }
    }
}
