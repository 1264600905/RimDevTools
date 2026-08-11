using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class AutoTestRunner
    {
        private void StartNextSession()
        {
            AutoTestWorkItem item = pendingItems.Dequeue();
            switch (item.Kind)
            {
                case AutoTestKind.Building:
                case AutoTestKind.Item:
                case AutoTestKind.Plant:
                    StartThingSession(item);
                    break;
                case AutoTestKind.Terrain:
                    StartTerrainSession(item);
                    break;
                case AutoTestKind.Recipe:
                    StartRecipeSession(item);
                    break;
                case AutoTestKind.Hediff:
                    StartHediffSession(item);
                    break;
                case AutoTestKind.AnimalInfoCard:
                case AutoTestKind.MechanoidInfoCard:
                case AutoTestKind.PawnInfoCard:
                    StartPawnInfoCardSession(item);
                    break;
                case AutoTestKind.GeneInfoCard:
                case AutoTestKind.XenotypeInfoCard:
                    StartDefInfoCardSession(item);
                    break;
            }
        }

        private void StartThingSession(AutoTestWorkItem item)
        {
            AutoTestResult result = CreateResult(item.ManifestEntry, "ThingDef", item.ThingDef.defName, item.ThingDef.label);
            results.Add(result);

            string caseId = item.CaseId ?? item.ManifestEntry?.CaseId ?? result.CaseId;
            InfoCardOperationAdapter adapter = new InfoCardOperationAdapter(runId, caseId, operationRunner, operationJournal, logJournal, evidenceBuilder, evidenceClock);
            IDisposable caseScope = adapter.BeginCase();
            bool noSpace = false;
            Thing preparedThing = null;
            bool restoreTerrain = false;
            IntVec3 restoreTerrainCell = IntVec3.Invalid;
            TerrainDef originalTerrain = null;
            bool restorePollution = false;
            bool originalPollution = false;
            AutoTestOperationEnvelope prepare = adapter.Run("spawn_or_prepare_target", delegate
            {
                ThingDef def = item.ThingDef;
                ThingDef stuff = ResolveStuff(def);
                Thing thing = ThingMaker.MakeThing(def, stuff);
                if (def.useHitPoints && thing.HitPoints <= 0)
                {
                    thing.HitPoints = thing.MaxHitPoints;
                }
                if (thing is Plant preparedPlant)
                {
                    preparedPlant.HitPoints = preparedPlant.MaxHitPoints;
                    preparedPlant.Growth = 1f;
                    preparedPlant.sown = true;
                }
                result.Stage = "create";

                IntVec2 size = def.Size;
                int width = Math.Max(1, size.x);
                int height = Math.Max(1, size.z);
                IntVec3 rootCell;
                if (!TryAllocateThingRoot(width, height, out rootCell))
                {
                    noSpace = true;
                    return (Thing)null;
                }

                ClearArea(rootCell, width, height);
                if (item.Kind == AutoTestKind.Plant)
                {
                    restoreTerrain = true;
                    restoreTerrainCell = rootCell;
                    originalTerrain = map.terrainGrid.TerrainAt(rootCell);
                    TerrainDef plantTerrain = TerrainDefOf.SoilRich ?? TerrainDefOf.Soil ?? DefDatabase<TerrainDef>.GetNamedSilentFail("Soil");
                    if (plantTerrain == null)
                    {
                        throw new InvalidOperationException("No fertile terrain was available for Plant InfoCard setup.");
                    }
                    map.terrainGrid.SetTerrain(rootCell, plantTerrain);

                    if (map.pollutionGrid != null && (def.plant.RequiresPollution || def.plant.RequiresNoPollution))
                    {
                        restorePollution = true;
                        originalPollution = map.pollutionGrid.IsPolluted(rootCell);
                        map.pollutionGrid.SetPolluted(rootCell, def.plant.RequiresPollution, silent: true);
                    }
                }

                if (item.Kind == AutoTestKind.Building)
                {
                    PrepareBuildingFactionBeforeSpawn(thing, AutoTestBuildingContext.BuildingInfoCard, result,
                        ResolveFactionFromSpec(item.SpecDef), item.SpecDef?.buildingFactionless == true);
                    thing = GenSpawn.Spawn(thing, rootCell, map, Rot4.North, WipeMode.Vanish, false, false);
                }
                else
                    thing = GenSpawn.Spawn(thing, rootCell, map, WipeMode.Vanish);

                preparedThing = thing;
                if (thing == null || thing.Destroyed || !thing.Spawned)
                {
                    throw new InvalidOperationException(
                        "Spawned target was immediately destroyed or never entered the map." +
                        " null=" + (thing == null) +
                        " destroyed=" + (thing?.Destroyed.ToString() ?? "unknown") +
                        " spawned=" + (thing?.Spawned.ToString() ?? "unknown") +
                        " hitPoints=" + (thing?.HitPoints.ToString() ?? "unknown") +
                        " maxHitPoints=" + (thing?.MaxHitPoints.ToString() ?? "unknown") +
                        " terrain=" + (map.terrainGrid.TerrainAt(rootCell)?.defName ?? "unknown"));
                }

                if (thing is Plant plant)
                {
                    // A mature plant avoids environment-dependent growth-rate stats while
                    // still exercising the actual Plant instance InfoCard path.
                    plant.Growth = 1f;
                    if (plant.HitPoints <= 0)
                    {
                        plant.HitPoints = plant.MaxHitPoints;
                    }
                    map.mapDrawer.MapMeshDirty(rootCell, MapMeshFlagDefOf.Things);
                    result.Notes = AppendNote(result.Notes, "PlantLifeStage=" + plant.LifeStage);
                    result.Notes = AppendNote(result.Notes, "Growth=" + plant.Growth.ToString("0.####"));
                }

                result.Stage = "spawn";
                result.Label = thing.LabelCap;
                try
                {
                    result.Notes = AppendNote(result.Notes, TrimForNote(thing.GetInspectString()));
                }
                catch (Exception ex)
                {
                    result.Notes = AppendNote(result.Notes, "InspectProbeError=" + TrimForNote(ex.Message));
                }
                int gizmoCount = 0;
                foreach (Gizmo gizmo in thing.GetGizmos()) if (gizmo != null) gizmoCount++;
                result.Notes = AppendNote(result.Notes, "Gizmos=" + gizmoCount);
                Find.Selector.ClearSelection();
                if (!thing.Destroyed && thing.Spawned)
                {
                    Find.Selector.Select(thing, false, false);
                }
                return thing;
            }, thing => thing == null ? "no_layout_space" : thing.def.defName);
            adapter.AssertOperation("target.prepared", prepare);

            if (!prepare.Success)
            {
                result.Status = "failed_spawn";
                result.Stage = "spawn_or_prepare_target";
                result.ErrorMessage = prepare.Error == null ? "Target preparation failed." : prepare.Error.Message;
                adapter.AssertTrue("target.preparation_assertion", false, result.ErrorMessage, prepare.OperationId);
                adapter.CompleteCase(false, result.ErrorMessage);
                caseScope.Dispose();
                RestorePreparedInfoCardEnvironment(restoreTerrain, restoreTerrainCell, originalTerrain, restorePollution, originalPollution);
                CompleteSemanticCase(result);
                return;
            }

            if (noSpace)
            {
                result.Status = "skipped";
                result.Stage = "layout";
                result.ErrorMessage = "No free layout space remained on the current map.";
                adapter.AssertTrue("target.preparation_assertion", false, result.ErrorMessage, prepare.OperationId);
                adapter.CompleteCase(false, result.ErrorMessage);
                caseScope.Dispose();
                RestorePreparedInfoCardEnvironment(restoreTerrain, restoreTerrainCell, originalTerrain, restorePollution, originalPollution);
                CompleteSemanticCase(result);
                return;
            }

            adapter.AssertTrue("target.preparation_assertion", true, item.ThingDef.defName, prepare.OperationId);
            activeSession = new ActiveSession
            {
                Item = item,
                Result = result,
                SpawnedThing = preparedThing,
                InfoCardDef = item.ThingDef,
                AllowScreenshots = ShouldCaptureScreenshots(item.SpecDef),
                RemainingUiFrames = PreOpenSettleRepaintFrames,
                Phase = SessionPhase.WaitingForPreOpenSettle,
                EvidenceAdapter = adapter,
                EvidenceCaseScope = caseScope,
                RestoreTerrainAfterCleanup = restoreTerrain,
                RestoreTerrainCell = restoreTerrainCell,
                OriginalTerrain = originalTerrain,
                RestorePollutionAfterCleanup = restorePollution,
                OriginalPollution = originalPollution
            };
            result.Stage = "info_open";
        }

        private void RestorePreparedInfoCardEnvironment(bool restoreTerrain, IntVec3 cell, TerrainDef terrain, bool restorePollution, bool originalPollution)
        {
            if (!cell.IsValid || !cell.InBounds(map))
            {
                return;
            }

            if (restorePollution && map.pollutionGrid != null)
            {
                try
                {
                    map.pollutionGrid.SetPolluted(cell, originalPollution, silent: true);
                }
                catch
                {
                }
            }

            if (!restoreTerrain || terrain == null)
            {
                return;
            }

            try
            {
                map.terrainGrid.SetTerrain(cell, terrain);
            }
            catch
            {
            }
        }

        private void StartTerrainSession(AutoTestWorkItem item)
        {
            AutoTestResult result = CreateResult(item.ManifestEntry, "TerrainDef", item.TerrainDef.defName, item.TerrainDef.label);
            results.Add(result);

            try
            {
                if (!TryAllocateTerrainCell(out IntVec3 cell))
                {
                    result.Status = "skipped";
                    result.Stage = "layout";
                    result.ErrorMessage = "No free terrain layout space remained on the current map.";
                    CompleteSemanticCase(result);
                    return;
                }

                ClearArea(cell, 1, 1);
                map.terrainGrid.SetTerrain(cell, item.TerrainDef);
                TerrainDef applied = map.terrainGrid.TerrainAt(cell);
                result.Notes = applied == null ? "Terrain applied but could not be read back." : "Applied terrain: " + applied.defName;

                activeSession = new ActiveSession
                {
                    Item = item,
                    Result = result,
                    TerrainDef = item.TerrainDef,
                    InfoCardDef = item.TerrainDef,
                    AllowScreenshots = ShouldCaptureScreenshots(item.SpecDef),
                    RemainingUiFrames = PreOpenSettleRepaintFrames,
                    Phase = SessionPhase.WaitingForPreOpenSettle
                };
                result.Stage = "info_open";
            }
            catch (Exception ex)
            {
                result.Status = "failed_terrain_apply";
                result.Stage = "terrain";
                result.ErrorMessage = ex.ToString();
                CleanupSelectionAndWindows();
                CompleteSemanticCase(result);
            }
        }

        private void StartRecipeSession(AutoTestWorkItem item)
        {
            AutoTestResult result = CreateResult(item.ManifestEntry, "RecipeDef", item.RecipeDef.defName, item.RecipeDef.label);
            results.Add(result);

            Thing worktableThing = GetPlacedRecipeWorktable(item.WorktableDef);
            if (worktableThing == null)
            {
                result.Status = "skipped";
                result.Stage = "setup";
                result.ErrorMessage = "Required worktable could not be placed or was lost.";
                result.Notes = AppendNote(result.Notes, "Worktable=" + SafeDefName(item.WorktableDef));
                CompleteSemanticCase(result);
                return;
            }

            if (!(worktableThing is IBillGiver))
            {
                result.Status = "skipped";
                result.Stage = "setup";
                result.ErrorMessage = "Placed worktable does not implement IBillGiver.";
                result.Notes = AppendNote(result.Notes, "Worktable=" + SafeDefName(item.WorktableDef));
                CompleteSemanticCase(result);
                return;
            }

            result.Label = item.ThingDef?.label ?? item.RecipeDef.label ?? string.Empty;
            result.Notes = AppendNote(result.Notes, "Product=" + SafeDefName(item.ThingDef));
            result.Notes = AppendNote(result.Notes, "Worktable=" + SafeDefName(item.WorktableDef));
            result.Notes = AppendNote(result.Notes, "BuildingFactionPrepared=" + (worktableThing.Faction != null) +
                ";BuildingFactionReason=RecipeWorktable;BuildingFactionFinal=" + (worktableThing.Faction?.def?.defName ?? "None"));
            result.Notes = AppendNote(result.Notes, "BillConfigOpened=False");

            activeSession = new ActiveSession
            {
                Item = item,
                Result = result,
                Mode = ActiveSessionMode.RecipeBill,
                RecipeWorktableThing = worktableThing,
                InfoCardDef = item.ThingDef ?? (Def)item.RecipeDef,
                AllowScreenshots = ShouldCaptureScreenshots(item.SpecDef),
                RemainingUiFrames = PreOpenSettleRepaintFrames,
                Phase = SessionPhase.WaitingForPreOpenSettle
            };
            result.Stage = "recipe_select_worktable";
        }

        private void StartHediffSession(AutoTestWorkItem item)
        {
            AutoTestResult result = CreateResult(item.ManifestEntry, "HediffDef", item.HediffDef.defName, item.HediffDef.label);
            results.Add(result);
            Pawn pawn = null;

            try
            {
                AutoTestHediffPawnResolver pawnResolver = new AutoTestHediffPawnResolver(map, targetMod, delegate
                {
                    if (!TryAllocateThingRoot(1, 1, out IntVec3 rootCell)) return IntVec3.Invalid;
                    ClearArea(rootCell, 1, 1);
                    return rootCell;
                });
                AutoTestHediffPawnResolution context = pawnResolver.Resolve(item.HediffDef, item.SpecDef);
                pawn = context.Pawn;
                if (pawn == null)
                {
                    result.Status = "failed_hediff_pawn";
                    result.Stage = "pawn";
                    result.ErrorMessage = context.ErrorMessage ?? "No legal test pawn could be generated for Hediff validation.";
                    result.Notes = AppendNote(result.Notes, "FailureKind=" + AutoTestFailureKind.TransientPawnGeneration);
                    CompleteSemanticCase(result);
                    return;
                }

                AutoTestHediffPartResolution partResolution = context.PartResolution;
                if (partResolution == null || !partResolution.Supported)
                {
                    result.Status = AutoTestResultStatus.Unsupported;
                    result.Stage = "hediff_part";
                    result.ErrorMessage = partResolution?.ErrorMessage ?? "Hediff part resolution failed.";
                    result.Notes = AppendNote(result.Notes, "FailureKind=" + AutoTestFailureKind.UnsupportedPath);
                    pawn.Destroy(DestroyMode.Vanish);
                    CompleteSemanticCase(result);
                    return;
                }

                BodyPartRecord targetPart = partResolution.Part;
                Hediff hediff = HediffMaker.MakeHediff(item.HediffDef, pawn, targetPart);
                if (hediff == null)
                {
                    result.Status = "failed_hediff_apply";
                    result.Stage = "hediff";
                    result.ErrorMessage = "HediffMaker.MakeHediff returned null.";
                    pawn.Destroy(DestroyMode.Vanish);
                    CompleteSemanticCase(result);
                    return;
                }

                if (item.HediffDef.initialSeverity > 0f)
                {
                    hediff.Severity = item.HediffDef.initialSeverity;
                }

                pawn.health.AddHediff(hediff, targetPart, null, null);
                Find.Selector.ClearSelection();
                Find.Selector.Select(pawn, false, false);

                result.Label = hediff.LabelCap;
                result.Notes = AppendNote(result.Notes, "Pawn=" + pawn.LabelShortCap);
                result.Notes = AppendNote(result.Notes, "HediffPawnKind=" + context.PawnKind?.defName);
                result.Notes = AppendNote(result.Notes, "HediffPawnSource=" + context.Source);
                result.Notes = AppendNote(result.Notes, "HediffPart=" + (targetPart?.def?.defName ?? "WholeBody"));
                result.Notes = AppendNote(result.Notes, "HediffPartSource=" + partResolution.Source);
                result.Notes = AppendNote(result.Notes, "HediffInstallRecipe=" + (partResolution.InstallRecipe?.defName ?? partResolution.InstallContext ?? "None"));
                IEnumerable<string> prerequisites = pawn.health.hediffSet.hediffs
                    .Where(existing => existing != null && existing != hediff && existing.def?.modContentPack == targetMod)
                    .Select(existing => existing.def.defName)
                    .Distinct()
                    .OrderBy(defName => defName, StringComparer.Ordinal);
                result.Notes = AppendNote(result.Notes, "PrerequisiteHediffs=" + (prerequisites.Any() ? string.Join(",", prerequisites.ToArray()) : "None"));
                if (context.PassedAfterRetry)
                {
                    result.Notes = AppendNote(result.Notes, "RetryKind=" + AutoTestFailureKind.TransientPawnGeneration);
                    result.Notes = AppendNote(result.Notes, "FirstFailure=" + TrimForNote(context.FirstFailure));
                }

                activeSession = new ActiveSession
                {
                    Item = item,
                    Result = result,
                    InfoCardDef = item.HediffDef,
                    TestPawn = pawn,
                    AttachedHediff = hediff,
                    DestroyTestPawnOnCleanup = true,
                    PassedAfterRetry = context.PassedAfterRetry,
                    AllowScreenshots = ShouldCaptureScreenshots(item.SpecDef),
                    RemainingUiFrames = PreOpenSettleRepaintFrames,
                    Phase = SessionPhase.WaitingForPreOpenSettle
                };
                activeSession.PrerequisiteHediffs.AddRange(context.AddedPrerequisites);
                result.Stage = "info_open";
            }
            catch (Exception ex)
            {
                result.Status = "failed_hediff_apply";
                result.Stage = "hediff";
                result.ErrorMessage = ex.ToString();
                CleanupSelectionAndWindows();
                try
                {
                    if (pawn != null && !pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
                }
                catch { }
                CompleteSemanticCase(result);
            }
        }

        private void EnqueueThingDefs()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading
                .Where(IsTargetBuildingDef)
                .OrderBy(def => def.defName))
            {
                pendingItems.Enqueue(CreateWorkItem(
                    AutoTestKind.Building,
                    thingDef: def,
                    specDef: ResolveBestMatchingSpec(AutoTestKind.Building, def)));
            }

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading
                .Where(IsTargetItemDef)
                .OrderBy(def => def.defName))
            {
                pendingItems.Enqueue(CreateWorkItem(
                    AutoTestKind.Item,
                    thingDef: def,
                    specDef: ResolveBestMatchingSpec(AutoTestKind.Item, def)));
            }
        }

        private void EnqueueTerrainDefs()
        {
            foreach (TerrainDef def in DefDatabase<TerrainDef>.AllDefsListForReading
                .Where(IsTargetTerrainDef)
                .OrderBy(def => def.defName))
            {
                pendingItems.Enqueue(CreateWorkItem(
                    AutoTestKind.Terrain,
                    terrainDef: def,
                    specDef: ResolveBestMatchingSpec(AutoTestKind.Terrain, def)));
            }
        }

        private void EnqueueHediffDefs()
        {
            foreach (HediffDef def in DefDatabase<HediffDef>.AllDefsListForReading
                .Where(IsTargetHediffDef)
                .OrderBy(def => def.defName))
            {
                pendingItems.Enqueue(CreateWorkItem(
                    AutoTestKind.Hediff,
                    hediffDef: def,
                    specDef: ResolveBestMatchingSpec(AutoTestKind.Hediff, def)));
            }
        }

        private void EnqueueRecipeDefs()
        {
            List<AutoTestWorkItem> recipeItems = new List<AutoTestWorkItem>();
            HashSet<ThingDef> requiredWorktables = new HashSet<ThingDef>();

            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading
                .Where(IsTargetRecipeDef)
                .OrderBy(recipe => recipe.defName))
            {
                ThingDef productDef = ResolvePrimaryRecipeProduct(recipe);
                AutoTestRecipeClassification classification = ClassifyRecipe(recipe, ResolveRecipeWorktable(recipe), ResolveBestMatchingSpec(AutoTestKind.Recipe, recipe));
                if (productDef == null && classification.Kind != AutoTestRecipeExecutionKind.MechResurrection)
                {
                    AddImmediateResult(
                        CreateResult(
                            RegisterDiscoveredCase(AutoTestCaseType.RecipeBillUi, "RecipeDef", recipe.defName, recipe.label),
                            "RecipeDef",
                            recipe.defName,
                            recipe.label),
                        "skipped",
                        "collect",
                        "No supported product ThingDef was found for this recipe.");
                    continue;
                }

                ThingDef worktableDef = ResolveRecipeWorktable(recipe);
                if (worktableDef == null)
                {
                    AutoTestResult result = CreateResult(
                        RegisterDiscoveredCase(AutoTestCaseType.RecipeBillUi, "RecipeDef", recipe.defName, recipe.label),
                        "RecipeDef",
                        recipe.defName,
                        recipe.label);
                    result.Label = productDef?.label ?? recipe.label ?? string.Empty;
                    result.Notes = AppendNote(result.Notes, "Product=" + SafeDefName(productDef));
                    AddImmediateResult(
                        result,
                        "skipped",
                        "collect",
                        "No usable worktable recipe user was found.");
                    continue;
                }

                requiredWorktables.Add(worktableDef);
                recipeItems.Add(CreateWorkItem(
                    AutoTestKind.Recipe,
                    thingDef: productDef,
                    recipeDef: recipe,
                    worktableDef: worktableDef,
                    specDef: ResolveBestMatchingSpec(AutoTestKind.Recipe, recipe)));
            }

            PlaceRecipeWorktables(requiredWorktables);

            foreach (AutoTestWorkItem item in recipeItems)
            {
                if (GetPlacedRecipeWorktable(item.WorktableDef) != null)
                {
                    pendingItems.Enqueue(item);
                    continue;
                }

                AutoTestResult result = CreateResult(item.ManifestEntry, "RecipeDef", item.RecipeDef.defName, item.RecipeDef.label);
                result.Label = item.ThingDef?.label ?? item.RecipeDef.label ?? string.Empty;
                result.Notes = AppendNote(result.Notes, "Product=" + SafeDefName(item.ThingDef));
                result.Notes = AppendNote(result.Notes, "Worktable=" + SafeDefName(item.WorktableDef));
                AddImmediateResult(
                    result,
                    "skipped",
                    "setup",
                    "Required worktable could not be placed.");
            }
        }

        private bool IsTargetBuildingDef(ThingDef def)
        {
            return def != null &&
                   def.modContentPack == targetMod &&
                   def.category == ThingCategory.Building &&
                   def.thingClass != null &&
                   !def.IsBlueprint &&
                   !def.IsFrame &&
                   MatchesSpecFilters(AutoTestKind.Building, def);
        }

        private bool IsTargetItemDef(ThingDef def)
        {
            return def != null &&
                   def.modContentPack == targetMod &&
                   def.category == ThingCategory.Item &&
                   def.plant == null &&
                   def.thingClass != null &&
                   !typeof(Corpse).IsAssignableFrom(def.thingClass) &&
                   !def.IsBlueprint &&
                   !def.IsFrame &&
                   MatchesSpecFilters(AutoTestKind.Item, def);
        }

        private bool IsTargetTerrainDef(TerrainDef def)
        {
            return def != null &&
                   def.modContentPack == targetMod &&
                   MatchesSpecFilters(AutoTestKind.Terrain, def);
        }

        private bool IsTargetHediffDef(HediffDef def)
        {
            return def != null &&
                   def.modContentPack == targetMod &&
                   def.hediffClass != null &&
                   MatchesSpecFilters(AutoTestKind.Hediff, def);
        }

        private bool IsTargetRecipeDef(RecipeDef recipe)
        {
            if (recipe == null) return false;

            bool recipeOwnedByTargetMod = recipe.modContentPack == targetMod;
            bool producesTargetModThing = recipe.products != null && recipe.products.Any(product =>
                product != null &&
                product.thingDef != null &&
                product.thingDef.modContentPack == targetMod &&
                IsSupportedRecipeProductDef(product.thingDef));

            if (!recipeOwnedByTargetMod && !producesTargetModThing) return false;
            bool hasDiscoverableProduct = ResolvePrimaryRecipeProduct(recipe) != null;
            bool discoverableResurrection = recipeOwnedByTargetMod && recipe.mechResurrection;
            return (hasDiscoverableProduct || discoverableResurrection) && MatchesSpecFilters(AutoTestKind.Recipe, recipe);
        }

        private ThingDef ResolvePrimaryRecipeProduct(RecipeDef recipe)
        {
            if (recipe?.products == null) return null;

            ThingDef targetProduct = recipe.products
                .Where(product => product != null && IsSupportedRecipeProductDef(product.thingDef) && product.thingDef.modContentPack == targetMod)
                .Select(product => product.thingDef)
                .FirstOrDefault();

            if (targetProduct != null) return targetProduct;

            return recipe.products
                .Where(product => product != null && IsSupportedRecipeProductDef(product.thingDef))
                .Select(product => product.thingDef)
                .FirstOrDefault();
        }

        private bool IsSupportedRecipeProductDef(ThingDef def)
        {
            return def != null &&
                   def.thingClass != null &&
                   !typeof(Corpse).IsAssignableFrom(def.thingClass) &&
                   !def.IsBlueprint &&
                   !def.IsFrame;
        }

        private ThingDef ResolveRecipeWorktable(RecipeDef recipe)
        {
            IEnumerable<ThingDef> candidates = recipe.AllRecipeUsers ?? Enumerable.Empty<ThingDef>();
            return candidates
                .Where(def => def != null &&
                              def.category == ThingCategory.Building &&
                              def.thingClass != null &&
                              !def.IsBlueprint &&
                              !def.IsFrame)
                .OrderByDescending(def => def.modContentPack == targetMod ? 1 : 0)
                .ThenBy(def => def.defName)
                .FirstOrDefault();
        }

        private void UnlockAllResearch()
        {
            try
            {
                if (Find.ResearchManager != null)
                {
                    Find.ResearchManager.DebugSetAllProjectsFinished();
                    Log.Message("[LogSyncAutoTest] FinishAllResearch applied.");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[LogSyncAutoTest] Failed to finish all research: " + ex);
            }
        }

        private void PlaceRecipeWorktables(IEnumerable<ThingDef> worktableDefs)
        {
            foreach (ThingDef worktableDef in worktableDefs
                .Where(def => def != null && !placedRecipeWorktables.ContainsKey(def))
                .OrderByDescending(def => def.modContentPack == targetMod ? 1 : 0)
                .ThenBy(def => def.defName))
            {
                try
                {
                    ThingDef stuff = ResolveStuff(worktableDef);
                    Thing worktableThing = ThingMaker.MakeThing(worktableDef, stuff);
                    if (!(worktableThing is IBillGiver))
                    {
                        Log.Warning("[LogSyncAutoTest] Skipping non-billgiver worktable: " + worktableDef.defName);
                        continue;
                    }

                    IntVec2 size = worktableDef.Size;
                    int width = Math.Max(1, size.x);
                    int height = Math.Max(1, size.z);
                    if (!TryAllocateThingRoot(width, height, out IntVec3 rootCell))
                    {
                        Log.Warning("[LogSyncAutoTest] No layout space remained for worktable: " + worktableDef.defName);
                        continue;
                    }

                    ClearArea(rootCell, width, height);
                    PrepareBuildingFactionBeforeSpawn(worktableThing, AutoTestBuildingContext.RecipeWorktable, null);
                    GenSpawn.Spawn(worktableThing, rootCell, map, Rot4.North, WipeMode.Vanish, false, false);
                    placedRecipeWorktables[worktableDef] = worktableThing;

                    Log.Message(string.Format(
                        "[LogSyncAutoTest] Placed worktable {0} at {1}",
                        worktableDef.defName,
                        rootCell));
                }
                catch (Exception ex)
                {
                    Log.Warning("[LogSyncAutoTest] Failed to place worktable " + worktableDef.defName + ": " + ex);
                }
            }
        }

        private Thing GetPlacedRecipeWorktable(ThingDef worktableDef)
        {
            if (worktableDef == null) return null;
            if (!placedRecipeWorktables.TryGetValue(worktableDef, out Thing worktableThing)) return null;

            if (worktableThing == null || worktableThing.Destroyed || worktableThing.Map != map)
            {
                placedRecipeWorktables.Remove(worktableDef);
                return null;
            }

            return worktableThing;
        }

        private bool TryCreateRecipeIngredients(
            RecipeDef recipe,
            Bill productionBill,
            Thing worktableThing,
            out List<Thing> ingredients,
            out string ingredientSummary,
            out string errorMessage,
            bool skipCorpseIngredients = false)
        {
            ingredients = new List<Thing>();
            List<string> summaryParts = new List<string>();
            errorMessage = null;

            if (recipe.ingredients == null || recipe.ingredients.Count == 0)
            {
                ingredientSummary = "None";
                return true;
            }

            foreach (IngredientCount ingredientCount in recipe.ingredients)
            {
                if (skipCorpseIngredients && ingredientCount?.filter?.AllowedThingDefs.Any(def => def?.thingClass != null && typeof(Corpse).IsAssignableFrom(def.thingClass)) == true)
                    continue;

                if (!TryResolveIngredientDef(ingredientCount, recipe, productionBill, out ThingDef ingredientDef))
                {
                    ingredientSummary = string.Join(",", summaryParts.ToArray());
                    errorMessage = "No valid ingredient ThingDef could be resolved for recipe ingredient.";
                    return false;
                }

                int requiredCount = ResolveRequiredIngredientCount(ingredientCount, ingredientDef, recipe, productionBill);
                int remainingCount = requiredCount;
                int placedCount = 0;

                while (remainingCount > 0)
                {
                    Thing ingredientThing = ThingMaker.MakeThing(ingredientDef, ResolveStuff(ingredientDef));
                    if (ingredientThing == null)
                    {
                        ingredientSummary = string.Join(",", summaryParts.ToArray());
                        errorMessage = "ThingMaker.MakeThing returned null for ingredient " + ingredientDef.defName;
                        return false;
                    }

                    int stackLimit = Math.Max(1, ingredientThing.def.stackLimit);
                    int stackCount = Math.Max(1, Math.Min(remainingCount, stackLimit));
                    ingredientThing.stackCount = stackCount;

                    ingredients.Add(ingredientThing);

                    placedCount += stackCount;
                    remainingCount -= stackCount;
                }

                summaryParts.Add(ingredientDef.defName + "x" + placedCount);
            }

            ingredientSummary = string.Join(", ", summaryParts.ToArray());
            return true;
        }

        private bool TryResolveIngredientDef(IngredientCount ingredientCount, RecipeDef recipe, Bill productionBill, out ThingDef ingredientDef)
        {
            ingredientDef = null;
            if (ingredientCount == null) return false;

            if (ingredientCount.filter == null) return false;

            ingredientDef = ingredientCount.filter.AllowedThingDefs
                .Where(def => IsUsableIngredientDef(def) &&
                    (ingredientCount.IsFixedIngredient || recipe.fixedIngredientFilter == null || recipe.fixedIngredientFilter.Allows(def)) &&
                    (ingredientCount.IsFixedIngredient || !(productionBill is Bill_Production bill) || bill.ingredientFilter == null || bill.ingredientFilter.Allows(def)))
                .OrderByDescending(def => def.modContentPack == targetMod ? 1 : 0)
                .ThenBy(def => def.defName)
                .FirstOrDefault();

            return ingredientDef != null;
        }

        private bool IsUsableIngredientDef(ThingDef def)
        {
            return def != null &&
                   def.thingClass != null &&
                   !typeof(Corpse).IsAssignableFrom(def.thingClass) &&
                   !def.IsBlueprint &&
                   !def.IsFrame;
        }

        private int ResolveRequiredIngredientCount(IngredientCount ingredientCount, ThingDef ingredientDef, RecipeDef recipe, Bill bill)
        {
            int requiredCount = 0;

            try
            {
                requiredCount = Mathf.CeilToInt(ingredientCount.CountRequiredOfFor(ingredientDef, recipe, bill));
            }
            catch
            {
            }

            if (requiredCount > 0) return requiredCount;

            try
            {
                requiredCount = Mathf.CeilToInt(ingredientCount.GetBaseCount());
            }
            catch
            {
            }

            return Math.Max(1, requiredCount);
        }

        private ThingDef ResolveStuff(ThingDef def)
        {
            if (def == null || !def.MadeFromStuff) return null;
            if (def.defaultStuff != null) return def.defaultStuff;
            if (def.stuffCategories == null || def.stuffCategories.Count == 0) return null;

            foreach (ThingDef candidate in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (candidate == null || !candidate.IsStuff || candidate.stuffProps == null || candidate.stuffProps.categories == null)
                    continue;

                if (candidate.stuffProps.categories.Any(category => def.stuffCategories.Contains(category)))
                {
                    return candidate;
                }
            }

            return null;
        }

        private bool TryAllocateThingRoot(int width, int height, out IntVec3 rootCell)
        {
            int safeWidth = Math.Max(1, width);
            int safeHeight = Math.Max(1, height);

            if (nextThingX + safeWidth + 5 >= map.Size.x)
            {
                nextThingX = 10;
                nextThingZ += currentThingRowDepth + 4;
                currentThingRowDepth = 0;
            }

            if (nextThingZ + safeHeight + 5 >= map.Size.z)
            {
                rootCell = IntVec3.Invalid;
                return false;
            }

            rootCell = new IntVec3(nextThingX, 0, nextThingZ);
            nextThingX += safeWidth + 2;
            currentThingRowDepth = Math.Max(currentThingRowDepth, safeHeight);
            return true;
        }

        private bool TryAllocateTerrainCell(out IntVec3 cell)
        {
            if (nextTerrainX + 2 >= map.Size.x)
            {
                nextTerrainX = Math.Max(map.Size.x / 2, 10);
                nextTerrainZ += 2;
            }

            if (nextTerrainZ + 2 >= map.Size.z)
            {
                cell = IntVec3.Invalid;
                return false;
            }

            cell = new IntVec3(nextTerrainX, 0, nextTerrainZ);
            nextTerrainX += 2;
            return true;
        }

        private void ClearArea(IntVec3 rootCell, int width, int height)
        {
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    IntVec3 cell = new IntVec3(rootCell.x + x, 0, rootCell.z + z);
                    if (!cell.InBounds(map)) continue;

                    foreach (Thing thing in cell.GetThingList(map).ToList())
                    {
                        if (thing == null || thing.Destroyed) continue;
                        if (!(thing.def?.destroyable ?? false)) continue;
                        try
                        {
                            thing.Destroy(DestroyMode.Vanish);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private IntVec3 ResolveDropCellNear(Thing anchorThing)
        {
            if (anchorThing != null && !anchorThing.Destroyed)
            {
                CellRect occupiedRect = anchorThing.OccupiedRect();
                for (int x = occupiedRect.minX - 1; x <= occupiedRect.maxX + 1; x++)
                {
                    for (int z = occupiedRect.minZ - 1; z <= occupiedRect.maxZ + 1; z++)
                    {
                        IntVec3 candidate = new IntVec3(x, 0, z);
                        if (!candidate.InBounds(map)) continue;
                        if (!candidate.Walkable(map)) continue;
                        return candidate;
                    }
                }
            }

            return new IntVec3(10, 0, 10);
        }
    }
}
