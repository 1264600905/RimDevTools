using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
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

    internal enum SkillCasterTestPath
    {
        NativeHolder,
        FallbackTester,
        MechanitorControlledMech
    }

    internal sealed partial class AutoTestRunner
    {
        private sealed class SkillPocketMapTestSession
        {
            private const int MapReadyWaitTicks = 180;
            private const int FightDurationTicks = 3600;
            private const int CooldownResetIntervalTicks = 600;
            private const int DirectCastInitialDelayTicks = 60;
            private const int DirectCastRetryIntervalTicks = 120;
            private const int PostCastInstantScreenshotDelayTicks = 2;
            private readonly AutoTestRunner owner;

            public Map TestMap;
            public AbilityDef AbilityDef;
            public Pawn CasterPawn;
            public Pawn MechanitorPawn;
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
            public int LastCombatRefreshTick;
            public int CooldownResetCount;
            public bool CastSucceeded;
            public bool ScreenshotCaptured;
            public SkillCasterTestPath CasterTestPath;
            public int FirstCastTick = -1;
            public int WarmupStartTick = -1;
            public int WarmupEndTick = -1;
            public int WarmupScreenshotTick = -1;
            public bool AutoCastConfigured;
            public bool AutoCastAvailable;
            public bool SearchAndDestroyEnabled;
            public string SearchAndDestroyStatus = string.Empty;
            public string SkillTargetMode = "Unknown";
            public bool UniversalPrerequisitesApplied;
            public int FightingStartTick = -1;
            public int NextDirectCastAttemptTick = -1;
            public int DirectCastAttemptCount;
            public string DirectCastTargetSummary = string.Empty;
            public AutoTestAbilitySnapshot AbilityBefore;
            public readonly Dictionary<string, string> TargetStateBefore = new Dictionary<string, string>(StringComparer.Ordinal);

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
                        Phase = SkillPocketMapTestPhase.CleaningUp;
                        break;
                    case SkillPocketMapTestPhase.SpawningCaster:
                        if (!TrySpawnCaster())
                        {
                            Phase = SkillPocketMapTestPhase.CleaningUp;
                            break;
                        }

                        Phase = SkillPocketMapTestPhase.SpawningTargets;
                        break;
                    case SkillPocketMapTestPhase.SpawningTargets:
                        if (!TrySpawnTargets())
                        {
                            Phase = SkillPocketMapTestPhase.CleaningUp;
                            break;
                        }
                        Phase = SkillPocketMapTestPhase.ConfiguringAbilities;
                        break;
                    case SkillPocketMapTestPhase.ConfiguringAbilities:
                        CaptureSemanticBaseline();
                        ConfigureAutoCastForTest();
                        FightTicksRemaining = FightDurationTicks;
                        LastCooldownResetTick = Find.TickManager.TicksGame;
                        FightingStartTick = LastCooldownResetTick;
                        NextDirectCastAttemptTick = FightingStartTick + DirectCastInitialDelayTicks;
                        Result.Stage = "fighting_60s";
                        Phase = SkillPocketMapTestPhase.Fighting;
                        break;
                    case SkillPocketMapTestPhase.Fighting:
                        UpdateFighting();
                        break;
                    case SkillPocketMapTestPhase.CapturingScreenshot:
                        CapturePendingSuccessScreenshot();
                        break;
                    case SkillPocketMapTestPhase.CleaningUp:
                        CleanupAndClose();
                        break;
                    default:
                        Result.Status = "skipped";
                        Result.Stage = "skill_not_implemented";
                        Result.ErrorMessage = "Skill pocket map session has not been implemented yet.";
                        TryCaptureSkillScreenshot(true, "skill_not_implemented");
                        Phase = SkillPocketMapTestPhase.CleaningUp;
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

                PawnKindDef nativeHolder = ResolveNativeHolderPawnKind();
                if (nativeHolder != null)
                {
                    if (nativeHolder.RaceProps != null && nativeHolder.RaceProps.IsMechanoid)
                    {
                        CasterTestPath = SkillCasterTestPath.MechanitorControlledMech;
                        Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "SkillPath=MechanitorControlledMech");
                        if (TrySpawnMechanitorControlledMech(nativeHolder))
                        {
                            return true;
                        }
                    }

                    CasterTestPath = SkillCasterTestPath.NativeHolder;
                    Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "SkillPath=NativeHolder");
                    if (TrySpawnNativeCaster(nativeHolder))
                    {
                        return true;
                    }
                }

                CasterTestPath = SkillCasterTestPath.FallbackTester;
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "SkillPath=FallbackTester");
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "NativeHolderFound=" + (nativeHolder != null));
                if (TrySpawnFallbackCaster())
                {
                    return true;
                }

                Result.Status = "failed_caster_kind_missing";
                Result.Stage = "spawn_caster";
                Result.ErrorMessage = "No PawnKindDef with the target ability was found, and fallback tester setup also failed.";
                TryCaptureSkillScreenshot(true, "failed_caster_kind_missing");
                return false;
            }

            private PawnKindDef ResolveNativeHolderPawnKind()
            {
                return DefDatabase<PawnKindDef>.AllDefsListForReading
                    .Where(def => def != null && def.modContentPack == owner.targetMod && def.abilities != null && def.abilities.Contains(AbilityDef))
                    .OrderBy(def => def.defName)
                    .FirstOrDefault();
            }

            private bool TrySpawnNativeCaster(PawnKindDef pawnKind)
            {
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
                    Result.ErrorMessage = "Failed to generate native holder pawn.";
                    TryCaptureSkillScreenshot(true, "failed_caster_spawn");
                    return false;
                }

                if (!FinalizeCasterPawn(pawn, pawnKind.defName, "NativeHolder"))
                {
                    return false;
                }

                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "NativeHolderPawnKind=" + pawnKind.defName);
                return true;
            }

            private bool TrySpawnMechanitorControlledMech(PawnKindDef pawnKind)
            {
                Pawn mechanitor = PawnGenerator.GeneratePawn(
                    new PawnGenerationRequest(
                        PawnKindDefOf.Colonist,
                        Faction.OfPlayer,
                        PawnGenerationContext.NonPlayer,
                        TestMap.Tile,
                        forceGenerateNewPawn: true,
                        canGeneratePawnRelations: false,
                        mustBeCapableOfViolence: true));
                if (mechanitor == null)
                {
                    Result.Status = "failed_mechanitor_spawn";
                    Result.Stage = "spawn_caster";
                    Result.ErrorMessage = "Failed to generate mechanitor controller pawn.";
                    TryCaptureSkillScreenshot(true, "failed_mechanitor_spawn");
                    return false;
                }

                HealthUtility.AdjustSeverity(mechanitor, HediffDefOf.MechlinkImplant, 1f);
                HediffDef extraBandwidthDef = DefDatabase<HediffDef>.GetNamedSilentFail("LogSync_TestMechanitorBandwidth");
                if (extraBandwidthDef != null)
                {
                    HealthUtility.AdjustSeverity(mechanitor, extraBandwidthDef, 1f);
                }

                IntVec3 mechanitorPos = new IntVec3(Mathf.RoundToInt(TestMap.Size.x * 0.28f), 0, TestMap.Size.z / 2);
                GenSpawn.Spawn(mechanitor, mechanitorPos, TestMap);
                MechanitorPawn = mechanitor;

                Pawn mech = PawnGenerator.GeneratePawn(
                    new PawnGenerationRequest(
                        pawnKind,
                        Faction.OfPlayer,
                        PawnGenerationContext.NonPlayer,
                        TestMap.Tile,
                        forceGenerateNewPawn: true,
                        canGeneratePawnRelations: false,
                        mustBeCapableOfViolence: false));
                if (mech == null)
                {
                    Result.Status = "failed_mech_spawn";
                    Result.Stage = "spawn_caster";
                    Result.ErrorMessage = "Failed to generate mechanitor-controlled mech pawn.";
                    TryCaptureSkillScreenshot(true, "failed_mech_spawn");
                    return false;
                }

                if (!FinalizeCasterPawn(mech, pawnKind.defName, "MechanitorControlledMech"))
                {
                    return false;
                }

                mechanitor.relations?.RemoveDirectRelation(PawnRelationDefOf.Overseer, mech);
                mechanitor.relations?.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
                mechanitor.mechanitor?.Notify_BandwidthChanged();

                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "MechanitorPawn=" + mechanitor.LabelShortCap);
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "MechanitorBound=True");
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "NativeHolderPawnKind=" + pawnKind.defName);
                return true;
            }

            private bool TrySpawnFallbackCaster()
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    PawnKindDefOf.Colonist,
                    Faction.OfPlayer,
                    PawnGenerationContext.NonPlayer,
                    TestMap.Tile,
                    forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: true);
                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (pawn == null)
                {
                    Result.Status = "failed_fallback_caster_spawn";
                    Result.Stage = "spawn_caster";
                    Result.ErrorMessage = "Failed to generate fallback tester pawn.";
                    TryCaptureSkillScreenshot(true, "failed_fallback_caster_spawn");
                    return false;
                }

                try
                {
                    EquipFallbackTesterLoadout(pawn);
                    EnsureFallbackAbility(pawn);
                }
                catch (System.Exception ex)
                {
                    Result.Status = "failed_fallback_caster_prepare";
                    Result.Stage = "spawn_caster";
                    Result.ErrorMessage = ex.ToString();
                    TryCaptureSkillScreenshot(true, "failed_fallback_caster_prepare");
                    return false;
                }

                if (!FinalizeCasterPawn(pawn, pawn.kindDef?.defName ?? "Colonist", "FallbackTester"))
                {
                    return false;
                }

                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "FallbackTesterPawnKind=" + (pawn.kindDef?.defName ?? "Colonist"));
                return true;
            }

            private bool FinalizeCasterPawn(Pawn pawn, string pawnKindDefName, string sourceTag)
            {
                IntVec3 spawnPos = new IntVec3(Mathf.RoundToInt(TestMap.Size.x * 0.375f), 0, TestMap.Size.z / 2);
                GenSpawn.Spawn(pawn, spawnPos, TestMap);
                CasterPawn = pawn;

                ApplyUniversalSkillTestPrerequisites(CasterPawn);

                if (CasterPawn.drafter != null)
                {
                    CasterPawn.drafter.Drafted = true;
                    CasterPawn.drafter.FireAtWill = false;
                }

                SearchAndDestroyEnabled = AutoTestRunner.TryEnableSearchAndDestroy(CasterPawn, out string searchAndDestroyStatus);
                SearchAndDestroyStatus = searchAndDestroyStatus ?? string.Empty;
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "SearchAndDestroy=" + (SearchAndDestroyEnabled ? "Enabled" : SearchAndDestroyStatus));
                if (SearchAndDestroyEnabled)
                {
                    Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "SearchAndDestroyRefresh=InterruptCurrentJob");
                    CasterPawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, true);
                }

                Ability = pawn.abilities?.GetAbility(AbilityDef, true);
                if (Ability == null)
                {
                    Result.Status = "failed_ability_missing";
                    Result.Stage = "spawn_caster";
                    Result.ErrorMessage = "Generated caster pawn does not own the target ability.";
                    TryCaptureSkillScreenshot(true, "failed_ability_missing");
                    return false;
                }

                Result.Stage = "spawn_caster";
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "CasterSource=" + sourceTag);
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "CasterPawnKind=" + pawnKindDefName);
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "CasterPawn=" + pawn.LabelShortCap);
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "UniversalPrerequisitesApplied=" + UniversalPrerequisitesApplied);
                return true;
            }

            private void ApplyUniversalSkillTestPrerequisites(Pawn pawn)
            {
                if (pawn == null || pawn.Destroyed)
                {
                    UniversalPrerequisitesApplied = false;
                    return;
                }

                bool mechlinkApplied = EnsureMechanitorCore(pawn);
                bool psylinkApplied = EnsurePsylinkLevelSix(pawn);
                bool hemogenicApplied = EnsureHemogenicGene(pawn);

                UniversalPrerequisitesApplied = mechlinkApplied && psylinkApplied && hemogenicApplied;
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "Prereq_Mechlink=" + mechlinkApplied);
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "Prereq_PsylinkLevel6=" + psylinkApplied);
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "Prereq_Hemogenic=" + hemogenicApplied);
            }

            private static bool EnsureMechanitorCore(Pawn pawn)
            {
                if (pawn?.health?.hediffSet == null)
                {
                    return false;
                }

                if (pawn.health.hediffSet.HasHediff(HediffDefOf.MechlinkImplant))
                {
                    return true;
                }

                HealthUtility.AdjustSeverity(pawn, HediffDefOf.MechlinkImplant, 1f);
                return pawn.health.hediffSet.HasHediff(HediffDefOf.MechlinkImplant);
            }

            private static bool EnsurePsylinkLevelSix(Pawn pawn)
            {
                if (pawn?.health?.hediffSet == null || !ModsConfig.RoyaltyActive)
                {
                    return false;
                }

                Hediff_Level psylink = pawn.GetMainPsylinkSource();
                if (psylink == null)
                {
                    BodyPartRecord brain = pawn.health.hediffSet.GetBrain();
                    if (brain == null)
                    {
                        return false;
                    }

                    psylink = HediffMaker.MakeHediff(HediffDefOf.PsychicAmplifier, pawn, brain) as Hediff_Level;
                    if (psylink == null)
                    {
                        return false;
                    }

                    pawn.health.AddHediff(psylink);
                }

                int targetLevel = Math.Max(1, Math.Min(6, (int)HediffDefOf.PsychicAmplifier.maxSeverity));
                psylink.ChangeLevel(targetLevel - psylink.level);
                return psylink.level >= targetLevel;
            }

            private static bool EnsureHemogenicGene(Pawn pawn)
            {
                if (pawn == null || !ModsConfig.BiotechActive)
                {
                    return false;
                }

                if (pawn.genes == null)
                {
                    pawn.genes = new Pawn_GeneTracker(pawn);
                }

                GeneDef hemogenic = GeneDefOf.Hemogenic ?? DefDatabase<GeneDef>.GetNamedSilentFail("Hemogenic");
                if (hemogenic == null)
                {
                    return false;
                }

                if (pawn.genes.GetGene(hemogenic) == null)
                {
                    pawn.genes.AddGene(hemogenic, xenogene: false);
                }

                return pawn.genes.GetGene(hemogenic) != null;
            }

            private void EnsureFallbackAbility(Pawn pawn)
            {
                if (pawn.abilities == null)
                {
                    pawn.abilities = new Pawn_AbilityTracker(pawn);
                }

                if (pawn.abilities.GetAbility(AbilityDef, true) == null)
                {
                    pawn.abilities.GainAbility(AbilityDef);
                }
            }

            private void EquipFallbackTesterLoadout(Pawn pawn)
            {
                pawn.apparel?.DestroyAll();
                pawn.equipment?.DestroyAllEquipment();

                ThingDef armorDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_PowerArmor");
                ThingDef helmetDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_PowerArmorHelmet");
                ThingDef knifeDef = DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_Knife");
                ThingDef goldDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gold");

                if (armorDef != null)
                {
                    Apparel armor = ThingMaker.MakeThing(armorDef) as Apparel;
                    armor?.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Legendary, ArtGenerationContext.Colony);
                    if (armor != null)
                    {
                        pawn.apparel?.Wear(armor);
                    }
                }

                if (helmetDef != null)
                {
                    Apparel helmet = ThingMaker.MakeThing(helmetDef) as Apparel;
                    helmet?.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Legendary, ArtGenerationContext.Colony);
                    if (helmet != null)
                    {
                        pawn.apparel?.Wear(helmet);
                    }
                }

                if (knifeDef != null)
                {
                    ThingWithComps knife = ThingMaker.MakeThing(knifeDef, goldDef) as ThingWithComps;
                    knife?.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Awful, ArtGenerationContext.Colony);
                    if (knife != null)
                    {
                        pawn.equipment?.MakeRoomFor(knife);
                        pawn.equipment?.AddEquipment(knife);
                    }
                }
            }

            private bool TrySpawnTargets()
            {
                bool needsAlly = ShouldSpawnAllyTarget();
                bool needsEnemy = ShouldSpawnEnemyTargets();

                if (needsAlly && !TrySpawnAllyTarget())
                {
                    return false;
                }

                if (needsEnemy && !TrySpawnEnemyTargets(5))
                {
                    return false;
                }

                if (!needsAlly && !needsEnemy)
                {
                    Result.Stage = "spawn_targets";
                    SkillTargetMode = "SelfOnly";
                    Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "SkillTargetMode=SelfOnly");
                }

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
                SkillTargetMode = "Enemy";
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "SkillTargetMode=Enemy");
                return true;
            }

            private bool TrySpawnAllyTarget()
            {
                if (TestMap == null || CasterPawn == null)
                {
                    Result.Status = "failed_ally_spawn";
                    Result.Stage = "spawn_targets";
                    Result.ErrorMessage = "Skill pocket map ally target prerequisites were missing.";
                    TryCaptureSkillScreenshot(true, "failed_ally_spawn");
                    return false;
                }

                Pawn pawn = PawnGenerator.GeneratePawn(
                    new PawnGenerationRequest(
                        PawnKindDefOf.Colonist,
                        Faction.OfPlayer,
                        PawnGenerationContext.NonPlayer,
                        TestMap.Tile,
                        forceGenerateNewPawn: true,
                        canGeneratePawnRelations: false,
                        mustBeCapableOfViolence: false));
                if (pawn == null)
                {
                    Result.Status = "failed_ally_spawn";
                    Result.Stage = "spawn_targets";
                    Result.ErrorMessage = "Failed to generate ally target pawn.";
                    TryCaptureSkillScreenshot(true, "failed_ally_spawn");
                    return false;
                }

                IntVec3 allyPos = new IntVec3(Mathf.RoundToInt(TestMap.Size.x * 0.47f), 0, TestMap.Size.z / 2);
                GenSpawn.Spawn(pawn, allyPos, TestMap);
                AllyPawn = pawn;
                if (AllyPawn.drafter != null)
                {
                    AllyPawn.drafter.Drafted = true;
                    AllyPawn.drafter.FireAtWill = false;
                }

                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "AllyTarget=" + AllyPawn.LabelShortCap);
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "AllyTargetSpawned=True");
                SkillTargetMode = EnemyPawns.Count > 0 ? "EnemyAndAlly" : "AllyOrSelf";
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "SkillTargetMode=AllyOrSelf");
                return true;
            }

            private bool ShouldSpawnEnemyTargets()
            {
                if (Ability == null)
                {
                    return true;
                }

                LocalTargetInfo enemyProbe = new LocalTargetInfo(new IntVec3(Mathf.RoundToInt(TestMap.Size.x * 0.625f), 0, TestMap.Size.z / 2));
                if (Ability.CanApplyOn(enemyProbe))
                {
                    return true;
                }

                return Ability.def.hostile;
            }

            private bool ShouldSpawnAllyTarget()
            {
                if (Ability?.verb?.targetParams == null)
                {
                    return false;
                }

                return Ability.verb.targetParams.canTargetPawns && !Ability.def.hostile;
            }

            private void UpdateFighting()
            {
                if (CasterPawn == null || CasterPawn.Destroyed || !CasterPawn.Spawned || CasterPawn.Map != TestMap)
                {
                    Result.Status = "failed_caster_lost";
                    Result.Stage = "fighting_60s";
                    Result.ErrorMessage = "Caster pawn disappeared during skill test.";
                    TryCaptureSkillScreenshot(true, "failed_caster_lost");
                    Phase = SkillPocketMapTestPhase.CleaningUp;
                    return;
                }

                if (Ability == null)
                {
                    Result.Status = "failed_ability_lost";
                    Result.Stage = "fighting_60s";
                    Result.ErrorMessage = "Ability instance disappeared during skill test.";
                    TryCaptureSkillScreenshot(true, "failed_ability_lost");
                    Phase = SkillPocketMapTestPhase.CleaningUp;
                    return;
                }

                int currentTick = Find.TickManager.TicksGame;
                EnsureCasterAutoCombatState(currentTick);
                TryQueueDirectAbilityCast(currentTick);

                if (currentTick - LastCooldownResetTick >= CooldownResetIntervalTicks)
                {
                    Ability.ResetCooldown();
                    LastCooldownResetTick = currentTick;
                    CooldownResetCount++;
                    Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "CooldownResetTick=" + currentTick);
                    Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "CooldownResetCount=" + CooldownResetCount);
                }

                ObserveAbilityExecution(currentTick);

                if (CastSucceeded && WarmupScreenshotTick >= 0)
                {
                    Result.Stage = "waiting_warmup_screenshot";
                    if (currentTick >= WarmupScreenshotTick)
                    {
                        Phase = SkillPocketMapTestPhase.CapturingScreenshot;
                    }

                    return;
                }

                if (CastSucceeded)
                {
                    Phase = SkillPocketMapTestPhase.CapturingScreenshot;
                    return;
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
                Phase = SkillPocketMapTestPhase.CleaningUp;
            }

            private void ConfigureAutoCastForTest()
            {
                AutoCastConfigured = TryConfigureAutoCastForCaster(out string autoCastSummary);
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "AutoCastConfigured=" + AutoCastConfigured);
                if (!string.IsNullOrWhiteSpace(autoCastSummary))
                {
                    Result.Notes = AutoTestRunner.AppendNote(Result.Notes, autoCastSummary);
                }

                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "DirectCastFallback=True");
            }

            private void TryQueueDirectAbilityCast(int currentTick)
            {
                if (CastSucceeded || currentTick < NextDirectCastAttemptTick || Ability == null || CasterPawn?.jobs == null)
                {
                    return;
                }

                NextDirectCastAttemptTick = currentTick + DirectCastRetryIntervalTicks;
                DirectCastAttemptCount++;
                LocalTargetInfo target = ResolveCastTarget();
                DirectCastTargetSummary = DescribeTarget(target);
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "DirectCastAttempt=" + DirectCastAttemptCount + "@" + currentTick + ",Target=" + DirectCastTargetSummary);
                if (!target.IsValid)
                {
                    Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "DirectCastRejected=NoValidTarget");
                    return;
                }

                try
                {
                    Ability.ResetCooldown();
                    if (Ability.UsesCharges && Ability.RemainingCharges <= 0)
                    {
                        Ability.RemainingCharges = Math.Max(1, Ability.maxCharges);
                    }

                    if (!Ability.CanApplyOn(target) || (Ability.verb != null && !Ability.verb.ValidateTarget(target, false)))
                    {
                        Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "DirectCastRejected=TargetValidationFailed");
                        return;
                    }

                    Ability.QueueCastingJob(target, LocalTargetInfo.Invalid);
                    Job queuedJob = CasterPawn.CurJob;
                    bool accepted = queuedJob?.ability == Ability || Ability.Casting;
                    Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "DirectCastQueued=" + accepted + ",Job=" + (queuedJob?.def?.defName ?? "none"));
                    if (accepted)
                    {
                        ObserveAbilityExecution(currentTick);
                    }
                }
                catch (Exception ex)
                {
                    Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "DirectCastError=" + AutoTestRunner.TrimForNote(ex.Message));
                }
            }

            private bool TryConfigureAutoCastForCaster(out string summary)
            {
                summary = string.Empty;
                AutoCastAvailable = false;

                if (CasterPawn == null || AbilityDef == null || Current.Game == null)
                {
                    summary = "AutoCastState=PrerequisiteMissing";
                    return false;
                }

                try
                {
                    Assembly autoAbilityAssembly = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "AutoAbility", StringComparison.OrdinalIgnoreCase));
                    if (autoAbilityAssembly == null)
                    {
                        summary = "AutoCastState=AssemblyMissing";
                        return false;
                    }

                    Type managerType = autoAbilityAssembly.GetType("AutoAbility.AutoCastManager");
                    Type settingType = autoAbilityAssembly.GetType("AutoAbility.AutoCastSetting");
                    if (managerType == null || settingType == null)
                    {
                        summary = "AutoCastState=TypeMissing";
                        return false;
                    }

                    FieldInfo componentsField = AccessTools.Field(typeof(Game), "components");
                    IEnumerable<GameComponent> components = componentsField?.GetValue(Current.Game) as IEnumerable<GameComponent>;
                    object manager = components?.FirstOrDefault(component => component != null && managerType.IsInstanceOfType(component));
                    if (manager == null)
                    {
                        summary = "AutoCastState=ManagerMissing";
                        return false;
                    }

                    MethodInfo getOrCreateSettingMethod = managerType.GetMethod("GetOrCreateSetting", BindingFlags.Public | BindingFlags.Instance);
                    if (getOrCreateSettingMethod == null)
                    {
                        summary = "AutoCastState=MethodMissing";
                        return false;
                    }

                    object setting = getOrCreateSettingMethod.Invoke(manager, new object[] { CasterPawn.GetUniqueLoadID() + "_ability_" + AbilityDef.defName });
                    if (setting == null)
                    {
                        summary = "AutoCastState=SettingMissing";
                        return false;
                    }

                    bool canTargetEnemy = CanTargetAnyEnemy();
                    bool canTargetAlly = CanTargetAlly();
                    bool canTargetSelf = CanTargetSelf();

                    SetFieldOrProperty(settingType, setting, "stopIfHasWindow", true);
                    SetFieldOrProperty(settingType, setting, "useWhenDrafted", true);
                    SetFieldOrProperty(settingType, setting, "useWhenDaily", true);
                    SetFieldOrProperty(settingType, setting, "useWhenReact", true);
                    SetFieldOrProperty(settingType, setting, "timelyUseInterval", 600L);
                    SetFieldOrProperty(settingType, setting, "priority", 999);
                    SetFieldOrProperty(settingType, setting, "minUseDis", 0);
                    SetFieldOrProperty(settingType, setting, "maxUseDis", 9999);
                    SetFieldOrProperty(settingType, setting, "applyToEnemy", true);
                    SetFieldOrProperty(settingType, setting, "applyToDownedEnemy", true);
                    SetFieldOrProperty(settingType, setting, "applyToWildAnimal", true);
                    SetFieldOrProperty(settingType, setting, "applyToAlly", true);
                    SetFieldOrProperty(settingType, setting, "applyToFriendMech", true);
                    SetFieldOrProperty(settingType, setting, "applyToPrisoner", true);
                    SetFieldOrProperty(settingType, setting, "applyToSelf", true);
                    SetFieldOrProperty(settingType, setting, "autoPreventAlly", false);
                    SetFieldOrProperty(settingType, setting, "onlyApplyToNoHediff", false);
                    SetFieldOrProperty(settingType, setting, "notUseToIneffective", false);
                    SetFieldOrProperty(settingType, setting, "onlyApplyToAllowedArea", false);
                    SetFieldOrProperty(settingType, setting, "fixStuck", true);
                    SetEnumFieldOrPropertyByName(settingType, setting, "autoType", "UseAfterCooldown", 1);

                    AutoCastAvailable = true;
                    if (canTargetEnemy)
                    {
                        SkillTargetMode = canTargetAlly ? "EnemyPreferred" : "Enemy";
                    }
                    else if (canTargetAlly)
                    {
                        SkillTargetMode = canTargetSelf ? "AllyPreferred" : "Ally";
                    }
                    else if (canTargetSelf)
                    {
                        SkillTargetMode = "SelfOnly";
                    }

                    summary = "AutoCastMode=UseAfterCooldown,AllFlagsChecked=True,CanTargetEnemy:" + canTargetEnemy + ",CanTargetAlly:" + canTargetAlly + ",CanTargetSelf:" + canTargetSelf;
                    return true;
                }
                catch (Exception ex)
                {
                    summary = "AutoCastState=Error:" + AutoTestRunner.TrimForNote(ex.Message);
                    return false;
                }
            }

            private static void SetFieldOrProperty(Type targetType, object instance, string memberName, object value)
            {
                FieldInfo field = AccessTools.Field(targetType, memberName);
                if (field != null)
                {
                    field.SetValue(instance, value);
                    return;
                }

                PropertyInfo property = AccessTools.Property(targetType, memberName);
                property?.SetValue(instance, value, null);
            }

            private static void SetEnumFieldOrProperty(Type targetType, object instance, string memberName, int enumValue)
            {
                FieldInfo field = AccessTools.Field(targetType, memberName);
                if (field != null)
                {
                    field.SetValue(instance, Enum.ToObject(field.FieldType, enumValue));
                    return;
                }

                PropertyInfo property = AccessTools.Property(targetType, memberName);
                if (property != null)
                {
                    property.SetValue(instance, Enum.ToObject(property.PropertyType, enumValue), null);
                }
            }

            private static void SetEnumFieldOrPropertyByName(Type targetType, object instance, string memberName, string enumName, int fallbackEnumValue)
            {
                FieldInfo field = AccessTools.Field(targetType, memberName);
                if (field != null)
                {
                    object parsed = ParseEnumValue(field.FieldType, enumName, fallbackEnumValue);
                    field.SetValue(instance, parsed);
                    return;
                }

                PropertyInfo property = AccessTools.Property(targetType, memberName);
                if (property != null)
                {
                    object parsed = ParseEnumValue(property.PropertyType, enumName, fallbackEnumValue);
                    property.SetValue(instance, parsed, null);
                }
            }

            private static object ParseEnumValue(Type enumType, string enumName, int fallbackEnumValue)
            {
                if (enumType != null && enumType.IsEnum)
                {
                    try
                    {
                        return Enum.Parse(enumType, enumName, true);
                    }
                    catch
                    {
                        return Enum.ToObject(enumType, fallbackEnumValue);
                    }
                }

                return fallbackEnumValue;
            }

            private void EnsureCasterAutoCombatState(int currentTick)
            {
                if (CasterPawn == null || CasterPawn.Destroyed || !CasterPawn.Spawned || CasterPawn.Map != TestMap)
                {
                    return;
                }

                if (CasterPawn.drafter != null)
                {
                    CasterPawn.drafter.Drafted = true;
                    CasterPawn.drafter.FireAtWill = false;
                }

                if (!SearchAndDestroyEnabled)
                {
                    return;
                }

                bool alreadyCasting = CasterPawn.CurJob != null && CasterPawn.CurJob.ability == Ability;
                if (!alreadyCasting && currentTick - LastCombatRefreshTick >= 90)
                {
                    LastCombatRefreshTick = currentTick;
                    Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "SearchAndDestroyRefreshTick=" + currentTick);
                    CasterPawn.jobs?.EndCurrentJob(JobCondition.InterruptForced, true);
                }
            }

            private void ObserveAbilityExecution(int currentTick)
            {
                Job currentJob = CasterPawn.CurJob;
                if (currentJob != null && currentJob.ability == Ability)
                {
                    if (FirstCastTick < 0)
                    {
                        FirstCastTick = currentTick;
                        Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "FirstCastTick=" + FirstCastTick);
                    }

                    MarkCastSucceeded(currentTick, "job_started");
                }

                Stance_Warmup warmup = CasterPawn.stances?.curStance as Stance_Warmup;
                if (warmup != null)
                {
                    if (WarmupStartTick < 0)
                    {
                        WarmupStartTick = currentTick;
                        WarmupEndTick = currentTick + Math.Max(0, warmup.ticksLeft);
                        WarmupScreenshotTick = WarmupEndTick + 2;
                        Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "WarmupStartTick=" + WarmupStartTick);
                        Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "WarmupEndTick=" + WarmupEndTick);
                        Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "WarmupScreenshotTick=" + WarmupScreenshotTick);
                    }

                    MarkCastSucceeded(currentTick, "warmup_started");
                }
            }

            private void MarkCastSucceeded(int currentTick, string reason)
            {
                if (!CastSucceeded)
                {
                    CastSucceeded = true;
                    Result.Status = "passed";
                    Result.ErrorMessage = string.Empty;
                }

                Result.Stage = WarmupScreenshotTick >= 0 ? "waiting_warmup_screenshot" : "skill_casted";
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "CastObserved=" + reason + "@" + currentTick);
                if (WarmupScreenshotTick < 0 && FirstCastTick >= 0)
                {
                    WarmupScreenshotTick = currentTick + PostCastInstantScreenshotDelayTicks;
                    Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "WarmupScreenshotTick=" + WarmupScreenshotTick);
                }
            }

            private void CapturePendingSuccessScreenshot()
            {
                Result.Stage = WarmupStartTick >= 0 ? "warmup_end_plus_2" : "skill_casted";
                TryCaptureSkillScreenshot(false, WarmupStartTick >= 0 ? "warmup_end_p2" : "skill_casted");
                Phase = SkillPocketMapTestPhase.CleaningUp;
            }

            private void CaptureSemanticBaseline()
            {
                AbilityBefore = AutoTestAbilityProbe.Capture(Ability);
                TargetStateBefore.Clear();
                foreach (Pawn pawn in EnemyPawns.Concat(new[] { AllyPawn, CasterPawn }).Where(item => item != null))
                {
                    string id = AutoTestThingProbe.Target(pawn)?.Id;
                    if (!string.IsNullOrEmpty(id)) TargetStateBefore[id] = BuildPawnEffectFingerprint(pawn);
                }
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "forceBypass=true");
                Result.Notes = AutoTestRunner.AppendNote(Result.Notes, "AbilityBaseline=" + AbilityBefore.DefName + "/cooldown=" + AbilityBefore.CooldownTicks + "/charges=" + AbilityBefore.Charges);
            }

            private void CleanupAndClose()
            {
                owner.RecordSkillSemanticBeforeCleanup(this);
                if (TestMap != null && Find.Maps.Contains(TestMap)) owner.DestroyPocketMap(TestMap, Parent);
                else if (Parent != null && !Parent.Destroyed) owner.DestroyPocketMap(null, Parent);
                if (Current.Game != null && owner.map != null && Find.Maps.Contains(owner.map)) Current.Game.CurrentMap = owner.map;
                owner.RecordSkillCleanupSemantic(this);
                Phase = SkillPocketMapTestPhase.Done;
            }

            private bool CanTargetAnyEnemy()
            {
                foreach (Pawn enemy in EnemyPawns)
                {
                    if (enemy == null || enemy.Destroyed || !enemy.Spawned || enemy.Map != TestMap || enemy.Dead)
                    {
                        continue;
                    }

                    LocalTargetInfo target = new LocalTargetInfo(enemy);
                    if (Ability.CanApplyOn(target) && (Ability.verb == null || Ability.verb.ValidateTarget(target, false)))
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool CanTargetAlly()
            {
                if (AllyPawn == null || AllyPawn.Destroyed || !AllyPawn.Spawned || AllyPawn.Map != TestMap || AllyPawn.Dead)
                {
                    return false;
                }

                LocalTargetInfo target = new LocalTargetInfo(AllyPawn);
                return Ability.CanApplyOn(target) && (Ability.verb == null || Ability.verb.ValidateTarget(target, false));
            }

            private bool CanTargetSelf()
            {
                if (CasterPawn == null || CasterPawn.Destroyed || !CasterPawn.Spawned || CasterPawn.Map != TestMap || CasterPawn.Dead)
                {
                    return false;
                }

                LocalTargetInfo target = new LocalTargetInfo(CasterPawn);
                return Ability.CanApplyOn(target) && (Ability.verb == null || Ability.verb.ValidateTarget(target, false));
            }

            private LocalTargetInfo ResolveCastTarget()
            {
                foreach (Pawn enemy in EnemyPawns.Where(pawn => pawn != null && !pawn.Destroyed && pawn.Spawned && pawn.Map == TestMap && !pawn.Dead))
                {
                    LocalTargetInfo enemyTarget = new LocalTargetInfo(enemy);
                    if (Ability.CanApplyOn(enemyTarget) && (Ability.verb == null || Ability.verb.ValidateTarget(enemyTarget, false)))
                    {
                        SkillTargetMode = "EnemyPawn";
                        return enemyTarget;
                    }

                    LocalTargetInfo enemyCellTarget = new LocalTargetInfo(enemy.Position);
                    if (Ability.CanApplyOn(enemyCellTarget) && (Ability.verb == null || Ability.verb.ValidateTarget(enemyCellTarget, false)))
                    {
                        SkillTargetMode = "EnemyLocation";
                        return enemyCellTarget;
                    }
                }

                if (AllyPawn != null && !AllyPawn.Destroyed && AllyPawn.Spawned && AllyPawn.Map == TestMap && !AllyPawn.Dead)
                {
                    LocalTargetInfo allyTarget = new LocalTargetInfo(AllyPawn);
                    if (Ability.CanApplyOn(allyTarget) && (Ability.verb == null || Ability.verb.ValidateTarget(allyTarget, false)))
                    {
                        SkillTargetMode = "AllyPawn";
                        return allyTarget;
                    }
                }

                LocalTargetInfo selfTarget = new LocalTargetInfo(CasterPawn);
                if (Ability.CanApplyOn(selfTarget) && (Ability.verb == null || Ability.verb.ValidateTarget(selfTarget, false)))
                {
                    SkillTargetMode = "Self";
                    return selfTarget;
                }

                LocalTargetInfo nearbyCellTarget = new LocalTargetInfo(CasterPawn.Position);
                if (Ability.CanApplyOn(nearbyCellTarget) && (Ability.verb == null || Ability.verb.ValidateTarget(nearbyCellTarget, false)))
                {
                    SkillTargetMode = "SelfLocation";
                    return nearbyCellTarget;
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

                Result.OverlayLine1 = Result.DefName;
                Result.OverlayLine2 = string.IsNullOrWhiteSpace(Result.Label) ? Result.DefName : Result.Label;
                Result.OverlayLine3 = "TargetMode: " + SkillTargetMode;
                Result.OverlayLine4 = WarmupScreenshotTick >= 0
                    ? "WarmupEnd+2: " + WarmupScreenshotTick
                    : "CastTick: " + (FirstCastTick >= 0 ? FirstCastTick.ToString() : "NA");

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
                    RecordResult(result);
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
                RecordResult(result);
            }
        }

        private bool IsTargetSkillAbilityDef(AbilityDef def)
        {
            return def != null &&
                def.modContentPack == targetMod &&
                MatchesSpecFilters(AutoTestKind.SkillPocketMap, def);
        }

        internal bool ShouldForceAbilityForSkillTest(Ability ability)
        {
            if (ability?.def == null || ability.pawn == null)
            {
                return false;
            }

            for (int i = 0; i < activeSkillPocketMapSessions.Count; i++)
            {
                SkillPocketMapTestSession session = activeSkillPocketMapSessions[i];
                if (session == null || session.AbilityDef == null || session.CasterPawn == null)
                {
                    continue;
                }

                if (session.CasterPawn == ability.pawn && session.AbilityDef == ability.def)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
