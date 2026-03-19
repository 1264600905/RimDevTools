using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace LogSyncMod.AutoTest
{
    internal enum SkillPocketMapTestPhase
    {
        Initializing,
        WaitingForMap,
        SpawningCaster,
        ConfiguringAbilities,
        SpawningTargets,
        Fighting,
        CapturingScreenshot,
        CleaningUp,
        Done
    }

    internal sealed partial class AutoTestRunner
    {
        private sealed class SkillPocketMapTestSession
        {
            private const int MapReadyWaitTicks = 180;
            private const int FightDurationTicks = 3600;
            private const int CooldownResetIntervalTicks = 600;
            private readonly AutoTestRunner owner;

            public Map TestMap;
            public AbilityDef AbilityDef;
            public Pawn CasterPawn;
            public Pawn AllyPawn;
            public readonly List<Pawn> EnemyPawns = new List<Pawn>();
            public SkillPocketMapTestPhase Phase;
            public AutoTestResult Result;
            public MapParent Parent;
            public bool AllowScreenshots;
            public int WaitTicks;
            public Ability Ability;
            public int FightTicksRemaining;
            public int LastCooldownResetTick;
            public int LastCastOrderTick;
            public bool CastSucceeded;
            public bool ScreenshotCaptured;

            public SkillPocketMapTestSession(AutoTestRunner owner)
            {
                this.owner = owner;
            }

            public void Update()
            {
                if (Result == null)
                {
                    Phase = SkillPocketMapTestPhase.Done;
                    return;
                }

                switch (Phase)
                {
                    case SkillPocketMapTestPhase.Initializing:
                        WaitTicks = MapReadyWaitTicks;
                        Phase = SkillPocketMapTestPhase.WaitingForMap;
                        break;
                    case SkillPocketMapTestPhase.WaitingForMap:
                        if (TestMap != null && Find.Maps.Contains(TestMap))
                        {
                            Phase = SkillPocketMapTestPhase.SpawningCaster;
                            break;
                        }

                        if (WaitTicks > 0)
                        {
                            WaitTicks--;
                            break;
                        }

                        Result.Status = "failed_map_not_ready";
                        Result.Stage = "setup";
                        Result.ErrorMessage = "Skill pocket map did not stabilize in time.";
                        TryCaptureSkillScreenshot(true, "failed_map_not_ready");
                        Phase = SkillPocketMapTestPhase.Done;
                        break;
                    case SkillPocketMapTestPhase.SpawningCaster:
                        if (!TrySpawnCaster())
                        {
                            Phase = SkillPocketMapTestPhase.Done;
                            break;
                        }

                        Phase = SkillPocketMapTestPhase.SpawningTargets;
                        break;
                    case SkillPocketMapTestPhase.SpawningTargets:
                        if (!TrySpawnEnemyTargets(5))
                        {
                            Phase = SkillPocketMapTestPhase.Done;
                            break;
                        }
                        FightTicksRemaining = FightDurationTicks;
                        LastCooldownResetTick = Find.TickManager.TicksGame;
                        Result.Stage = "fighting_60s";
                        Phase = SkillPocketMapTestPhase.Fighting;
                        break;
                    case SkillPocketMapTestPhase.Fighting:
                        UpdateFighting();
                        break;
                    default:
                        Result.Status = "skipped";
                        Result.Stage = "skill_not_implemented";
                        Result.ErrorMessage = "Skill pocket map session has not been implemented yet.";
                        TryCaptureSkillScreenshot(true, "skill_not_implemented");
                        Phase = SkillPocketMapTestPhase.Done;
                        break;
                }
            }

            private bool TrySpawnCaster()
            {
                if (TestMap == null)
                {
                    Result.Status = "failed_map_missing";
                    Result.Stage = "spawn_caster";
                    Result.ErrorMessage = "Skill pocket map test map was missing.";
                    TryCaptureSkillScreenshot(true, "failed_map_missing");
                    return false;
                }

                PawnKindDef pawnKind = DefDatabase<PawnKindDef>.AllDefsListForReading
                    .Where(def => def != null && def.modContentPack == owner.targetMod && def.abilities != null && def.abilities.Contains(AbilityDef))
                    .OrderBy(def => def.defName)
                    .FirstOrDefault();
                if (pawnKind == null)
                {
                    Result.Status = "failed_caster_kind_missing";
                    Result.Stage = "spawn_caster";
                    Result.ErrorMessage = "No PawnKindDef with the target ability was found.";
                    TryCaptureSkillScreenshot(true, "failed_caster_kind_missing");
                    return false;
                }

                PawnGenerationRequest request = new PawnGenerationRequest(
                    pawnKind,
                    Faction.OfPlayer,
                    PawnGenerationContext.NonPlayer,
                    TestMap.Tile,
                    forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: false);
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (pawn == null)
                {
                    Result.Status = "failed_caster_spawn";
                    Result.Stage = "spawn_caster";
                    Result.ErrorMessage = "Failed to generate caster pawn.";
                    TryCaptureSkillScreenshot(true, "failed_caster_spawn");
                    return false;
                }

                IntVec3 spawnPos = new IntVec3(Mathf.RoundToInt(TestMap.Size.x * 0.375f), 0, TestMap.Size.z / 2);
                GenSpawn.Spawn(pawn, spawnPos, TestMap);
                CasterPawn = pawn;

                Ability = pawn.abilities?.GetAbility(AbilityDef);
                if (Ability == null)
                {
                    Result.Status = "failed_ability_missing";
                    Result.Stage = "spawn_caster";
                    Result.ErrorMessage = "Generated caster pawn does not own the target ability.";
                    TryCaptureSkillScreenshot(true, "failed_ability_missing");
                    return false;
                }

                Result.Stage = "spawn_caster";
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "CasterPawnKind=" + pawnKind.defName);
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "CasterPawn=" + pawn.LabelShortCap);
                return true;
            }

            private bool TrySpawnEnemyTargets(int count)
            {
                if (TestMap == null || CasterPawn == null || count <= 0)
                {
                    Result.Status = "failed_enemy_spawn";
                    Result.Stage = "spawn_targets";
                    Result.ErrorMessage = "Skill pocket map target setup prerequisites were missing.";
                    TryCaptureSkillScreenshot(true, "failed_enemy_spawn");
                    return false;
                }

                List<IntVec3> spawnCells = FindEnemySpawnCells(count);
                if (spawnCells.Count == 0)
                {
                    Result.Status = "failed_enemy_spawn_pos";
                    Result.Stage = "spawn_targets";
                    Result.ErrorMessage = "No valid enemy spawn cells were found for skill pocket map test.";
                    TryCaptureSkillScreenshot(true, "failed_enemy_spawn_pos");
                    return false;
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
                        EnemyPawns.Add(pawn);
                    }
                    catch
                    {
                    }
                }

                if (EnemyPawns.Count == 0)
                {
                    Result.Status = "failed_enemy_spawn";
                    Result.Stage = "spawn_targets";
                    Result.ErrorMessage = "No enemy targets were spawned for skill pocket map test.";
                    TryCaptureSkillScreenshot(true, "failed_enemy_spawn");
                    return false;
                }

                Result.Stage = "spawn_targets";
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "EnemyCount=" + EnemyPawns.Count);
                return true;
            }

            private void UpdateFighting()
            {
                if (CasterPawn == null || CasterPawn.Destroyed || !CasterPawn.Spawned || CasterPawn.Map != TestMap)
                {
                    Result.Status = "failed_caster_lost";
                    Result.Stage = "fighting_60s";
                    Result.ErrorMessage = "Caster pawn disappeared during skill test.";
                    TryCaptureSkillScreenshot(true, "failed_caster_lost");
                    Phase = SkillPocketMapTestPhase.Done;
                    return;
                }

                if (Ability == null)
                {
                    Result.Status = "failed_ability_lost";
                    Result.Stage = "fighting_60s";
                    Result.ErrorMessage = "Ability instance disappeared during skill test.";
                    TryCaptureSkillScreenshot(true, "failed_ability_lost");
                    Phase = SkillPocketMapTestPhase.Done;
                    return;
                }

                int currentTick = Find.TickManager.TicksGame;
                if (currentTick - LastCooldownResetTick >= CooldownResetIntervalTicks)
                {
                    Ability.ResetCooldown();
                    LastCooldownResetTick = currentTick;
                    Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "CooldownResetTick=" + currentTick);
                }

                if (!CastSucceeded)
                {
                    if (Ability.OnCooldown || (CasterPawn.CurJob != null && CasterPawn.CurJob.ability == Ability))
                    {
                        CastSucceeded = true;
                        Result.Status = "passed";
                        Result.Stage = "skill_casted";
                        Result.ErrorMessage = string.Empty;
                        Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "CastTick=" + currentTick);
                        TryCaptureSkillScreenshot(false, "skill_casted");
                        Phase = SkillPocketMapTestPhase.Done;
                        return;
                    }

                    TryIssueCastOrder(currentTick);
                }

                FightTicksRemaining--;
                if (FightTicksRemaining > 0)
                {
                    return;
                }

                Result.Status = "failed_no_skill_cast";
                Result.Stage = "fighting_60s";
                Result.ErrorMessage = "Caster did not successfully start any ability cast within 60 seconds.";
                TryCaptureSkillScreenshot(true, "failed_no_skill_cast");
                Phase = SkillPocketMapTestPhase.Done;
            }

            private void TryIssueCastOrder(int currentTick)
            {
                if (currentTick - LastCastOrderTick < 90)
                {
                    return;
                }

                LocalTargetInfo target = ResolveCastTarget();
                if (!target.IsValid)
                {
                    return;
                }

                if (!Ability.CanCast || !Ability.CanApplyOn(target))
                {
                    return;
                }

                if (Ability.verb != null && !Ability.verb.ValidateTarget(target, false))
                {
                    return;
                }

                string disabledReason;
                if (Ability.GizmoDisabled(out disabledReason) && disabledReason.Translate() != "AbilityDisabledUndrafted".Translate())
                {
                    return;
                }

                if (CasterPawn.drafter != null)
                {
                    CasterPawn.drafter.Drafted = true;
                    CasterPawn.drafter.FireAtWill = true;
                }

                Job job = Ability.GetJob(target, target);
                CasterPawn.jobs?.TryTakeOrderedJob(job, JobTag.Misc, false);
                LastCastOrderTick = currentTick;
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "LastCastTarget=" + DescribeTarget(target));
            }

            private LocalTargetInfo ResolveCastTarget()
            {
                Pawn enemy = EnemyPawns.FirstOrDefault(pawn => pawn != null && !pawn.Destroyed && pawn.Spawned && pawn.Map == TestMap && !pawn.Dead);
                if (enemy != null)
                {
                    LocalTargetInfo enemyTarget = new LocalTargetInfo(enemy);
                    if (Ability.CanApplyOn(enemyTarget) && (Ability.verb == null || Ability.verb.ValidateTarget(enemyTarget, false)))
                    {
                        return enemyTarget;
                    }
                }

                LocalTargetInfo selfTarget = new LocalTargetInfo(CasterPawn);
                if (Ability.CanApplyOn(selfTarget) && (Ability.verb == null || Ability.verb.ValidateTarget(selfTarget, false)))
                {
                    return selfTarget;
                }

                return LocalTargetInfo.Invalid;
            }

            private static string DescribeTarget(LocalTargetInfo target)
            {
                if (!target.IsValid)
                {
                    return "Invalid";
                }

                if (target.Pawn != null)
                {
                    return target.Pawn.LabelShortCap + "/" + (target.Pawn.kindDef?.defName ?? "UnknownPawn");
                }

                if (target.Thing != null)
                {
                    return target.Thing.LabelCap + "/" + target.Thing.def.defName;
                }

                return target.Cell.ToString();
            }

            private void TryCaptureSkillScreenshot(bool force, string stageSuffix)
            {
                if (ScreenshotCaptured || owner.screenshotService == null || TestMap == null || !Find.Maps.Contains(TestMap))
                {
                    return;
                }

                Result.OverlayLine1 = string.IsNullOrWhiteSpace(Result.Label) ? Result.DefName : Result.Label;
                Result.OverlayLine2 = Result.DefName;
                Result.OverlayLine3 = "Caster: " + (CasterPawn?.LabelShortCap ?? "Unknown");
                Result.OverlayLine4 = "Enemies: " + EnemyPawns.Count(pawn => pawn != null && !pawn.Destroyed && pawn.Spawned && !pawn.Dead);

                bool captured = owner.screenshotService.TryCapture(new AutoTestScreenshotRequest
                {
                    Result = Result,
                    AllowCapture = AllowScreenshots,
                    Force = force,
                    IsFailureArtifact = force,
                    ResultIndex = owner.screenshotService.ResolveTurretResultIndex(owner.results, owner.activeSkillPocketMapSessions.IndexOf(this)),
                    StageSuffix = stageSuffix,
                    Prepare = () => owner.FocusPocketMapOverview(TestMap),
                    Guard = () => TestMap != null && Find.Maps.Contains(TestMap),
                    Profile = AutoTestScreenshotProfiles.PocketMapOverview,
                    SuccessNoteKey = "SkillScreenshotCaptured=True",
                    FailureNoteKey = "SkillFailureScreenshotCaptured=True"
                });

                ScreenshotCaptured = ScreenshotCaptured || captured;
            }

            private List<IntVec3> FindEnemySpawnCells(int count)
            {
                List<IntVec3> result = new List<IntVec3>();
                CellRect safeRect = CellRect.FromLimits(5, 5, TestMap.Size.x - 6, TestMap.Size.z - 6);
                IntVec3 anchor = new IntVec3(Mathf.RoundToInt(TestMap.Size.x * 0.625f), 0, TestMap.Size.z / 2);
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
        }

        private void BeginSkillPocketMapTestSession()
        {
            List<AbilityDef> abilityDefs = DefDatabase<AbilityDef>.AllDefsListForReading
                .Where(def => IsTargetSkillAbilityDef(def))
                .OrderBy(def => def.defName)
                .ToList();

            foreach (AbilityDef abilityDef in abilityDefs)
            {
                RegisterDiscoveredCase(AutoTestCaseType.SkillPocketMap, nameof(AbilityDef), abilityDef.defName, abilityDef.label);
                pendingSkillAbilityDefs.Enqueue(abilityDef);
            }

            Log.Message("[LogSyncAutoTest] Skill pocket map queue prepared. Total=" + pendingSkillAbilityDefs.Count);
        }

        private void StartNewSkillPocketMapSession(AbilityDef abilityDef)
        {
            AutoTestResult result = CreateResult(AutoTestCaseType.SkillPocketMap, nameof(AbilityDef), abilityDef.defName, abilityDef.label);
            result.OverlayLine1 = string.IsNullOrWhiteSpace(abilityDef.label) ? abilityDef.defName : abilityDef.label;
            result.OverlayLine2 = abilityDef.defName;
            SkillPocketMapTestSession session = new SkillPocketMapTestSession(this)
            {
                AbilityDef = abilityDef,
                Result = result,
                AllowScreenshots = ShouldCaptureScreenshots(ResolveBestMatchingSpec(AutoTestKind.SkillPocketMap, abilityDef)),
                Phase = SkillPocketMapTestPhase.Initializing
            };

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
                    result.Stage = "setup";
                    result.ErrorMessage = errorMessage ?? "Failed to create SkillPocketMap.";
                    results.Add(result);
                    return;
                }

                session.Parent = handle.Parent;
                session.TestMap = handle.Map;
                activeSkillPocketMapSessions.Add(session);
            }
            catch (System.Exception ex)
            {
                result.Status = "failed_map_creation";
                result.Stage = "setup";
                result.ErrorMessage = ex.ToString();
                results.Add(result);
            }
        }

        private bool IsTargetSkillAbilityDef(AbilityDef def)
        {
            return def != null &&
                def.modContentPack == targetMod &&
                def.aiCanUse &&
                MatchesSpecFilters(AutoTestKind.SkillPocketMap, def);
        }
    }
}
