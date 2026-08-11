using System;
using System.Linq;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestRecipeWorkerResolution
    {
        public Pawn Worker;
        public bool PassedAfterRetry;
        public string FirstFailure;
        public string ErrorMessage;
    }

    internal sealed class AutoTestRecipeWorkerService
    {
        private readonly Map map;
        private readonly Func<IntVec3> allocateSpawnCell;

        public AutoTestRecipeWorkerService(Map map, Func<IntVec3> allocateSpawnCell)
        {
            this.map = map;
            this.allocateSpawnCell = allocateSpawnCell;
        }

        public AutoTestRecipeWorkerResolution Create(RecipeDef recipe)
        {
            AutoTestRecipeWorkerResolution result = new AutoTestRecipeWorkerResolution();
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                Pawn worker = null;
                try
                {
                    Faction faction = Faction.OfPlayer ?? Find.FactionManager?.OfPlayer;
                    PawnGenerationRequest request = new PawnGenerationRequest(
                        PawnKindDefOf.Colonist,
                        faction,
                        PawnGenerationContext.NonPlayer,
                        map.Tile,
                        forceGenerateNewPawn: true,
                        allowDead: false,
                        allowDowned: false,
                        canGeneratePawnRelations: false,
                        mustBeCapableOfViolence: false,
                        allowPregnant: false,
                        allowAddictions: false,
                        fixedBiologicalAge: 25f,
                        fixedChronologicalAge: 25f,
                        forceBaselinerChance: 1f,
                        developmentalStages: DevelopmentalStage.Adult,
                        forceNoBackstory: true,
                        forceNoGear: true);
                    worker = PawnGenerator.GeneratePawn(request);
                    if (worker == null) throw new InvalidOperationException("PawnGenerator.GeneratePawn returned null for recipe worker.");
                    if (worker.health == null) throw new InvalidOperationException("Generated recipe worker has no health tracker.");

                    worker.health.RemoveAllHediffs();
                    PrepareSkills(worker, recipe);
                    worker.workSettings?.EnableAndInitialize();

                    IntVec3 cell = allocateSpawnCell == null ? IntVec3.Invalid : allocateSpawnCell();
                    if (!cell.IsValid || !cell.InBounds(map))
                        throw new InvalidOperationException("No safe spawn cell remained for recipe worker.");
                    GenSpawn.Spawn(worker, cell, map, WipeMode.Vanish);
                    if (worker.Destroyed || worker.Dead || worker.Downed || worker.Map != map)
                        throw new InvalidOperationException("Recipe worker was not healthy and spawned after generation.");

                    result.Worker = worker;
                    result.PassedAfterRetry = attempt == 2;
                    return result;
                }
                catch (Exception ex)
                {
                    if (result.FirstFailure == null) result.FirstFailure = ex.Message;
                    Destroy(worker);
                    if (!AutoTestRetryPolicy.CanRetry(AutoTestFailureKind.TransientPawnGeneration, attempt))
                        break;
                }
            }

            result.ErrorMessage = result.FirstFailure ?? "Recipe worker generation failed.";
            return result;
        }

        public static void Destroy(Pawn worker)
        {
            if (worker == null || worker.Destroyed) return;
            try { worker.Destroy(DestroyMode.Vanish); } catch { }
        }

        private static void PrepareSkills(Pawn worker, RecipeDef recipe)
        {
            if (worker.skills == null || recipe == null) return;
            if (recipe.workSkill != null)
            {
                SkillRecord record = worker.skills.GetSkill(recipe.workSkill);
                if (record != null && record.Level < 12) record.Level = 12;
            }

            foreach (SkillRequirement requirement in recipe.skillRequirements ?? Enumerable.Empty<SkillRequirement>())
            {
                if (requirement?.skill == null) continue;
                SkillRecord record = worker.skills.GetSkill(requirement.skill);
                int targetLevel = Math.Max(12, requirement.minLevel);
                if (record != null && record.Level < targetLevel) record.Level = targetLevel;
            }
        }
    }
}
