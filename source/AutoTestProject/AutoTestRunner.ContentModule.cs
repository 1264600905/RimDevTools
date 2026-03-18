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
            }
        }

        private void StartThingSession(AutoTestWorkItem item)
        {
            AutoTestResult result = CreateResult(item.ManifestEntry, "ThingDef", item.ThingDef.defName, item.ThingDef.label);
            results.Add(result);

            try
            {
                ThingDef def = item.ThingDef;
                ThingDef stuff = ResolveStuff(def);
                Thing thing = ThingMaker.MakeThing(def, stuff);
                result.Stage = "create";

                IntVec2 size = def.Size;
                int width = Math.Max(1, size.x);
                int height = Math.Max(1, size.z);
                if (!TryAllocateThingRoot(width, height, out IntVec3 rootCell))
                {
                    result.Status = "skipped";
                    result.Stage = "layout";
                    result.ErrorMessage = "No free layout space remained on the current map.";
                    return;
                }

                ClearArea(rootCell, width, height);

                if (item.Kind == AutoTestKind.Building)
                {
                    GenSpawn.Spawn(thing, rootCell, map, Rot4.North, WipeMode.Vanish, false, false);
                }
                else
                {
                    GenSpawn.Spawn(thing, rootCell, map, WipeMode.Vanish);
                }

                result.Stage = "spawn";
                result.Label = thing.LabelCap;
                result.Notes = TrimForNote(thing.GetInspectString());

                int gizmoCount = 0;
                foreach (Gizmo gizmo in thing.GetGizmos())
                {
                    if (gizmo != null) gizmoCount++;
                }

                result.Notes = AppendNote(result.Notes, "Gizmos=" + gizmoCount);

                Find.Selector.ClearSelection();
                Find.Selector.Select(thing, false, false);

                activeSession = new ActiveSession
                {
                    Item = item,
                    Result = result,
                    SpawnedThing = thing,
                    InfoCardDef = def,
                    AllowScreenshots = ShouldCaptureScreenshots(item.SpecDef),
                    RemainingUiFrames = PreOpenSettleRepaintFrames,
                    Phase = SessionPhase.WaitingForPreOpenSettle
                };
                result.Stage = "info_open";
            }
            catch (Exception ex)
            {
                result.Status = result.Stage == "create" ? "failed_spawn" : "failed_create";
                result.ErrorMessage = ex.ToString();
                CleanupSelectionAndWindows();
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
                return;
            }

            if (!(worktableThing is IBillGiver))
            {
                result.Status = "skipped";
                result.Stage = "setup";
                result.ErrorMessage = "Placed worktable does not implement IBillGiver.";
                result.Notes = AppendNote(result.Notes, "Worktable=" + SafeDefName(item.WorktableDef));
                return;
            }

            result.Label = item.ThingDef?.label ?? item.RecipeDef.label ?? string.Empty;
            result.Notes = AppendNote(result.Notes, "Product=" + SafeDefName(item.ThingDef));
            result.Notes = AppendNote(result.Notes, "Worktable=" + SafeDefName(item.WorktableDef));

            activeSession = new ActiveSession
            {
                Item = item,
                Result = result,
                InfoCardDef = item.ThingDef ?? (Def)item.RecipeDef,
                AllowScreenshots = ShouldCaptureScreenshots(item.SpecDef),
                RemainingUiFrames = PreOpenSettleRepaintFrames,
                Phase = SessionPhase.WaitingForPreOpenSettle
            };
            result.Stage = "info_open";
        }

        private void StartHediffSession(AutoTestWorkItem item)
        {
            AutoTestResult result = CreateResult(item.ManifestEntry, "HediffDef", item.HediffDef.defName, item.HediffDef.label);
            results.Add(result);

            try
            {
                Pawn pawn = EnsureSharedHediffPawn();
                if (pawn == null)
                {
                    result.Status = "skipped";
                    result.Stage = "pawn";
                    result.ErrorMessage = "No test pawn could be generated for Hediff validation.";
                    return;
                }

                CleanupTargetModHediffs(pawn);

                BodyPartRecord targetPart = ResolveHediffPart(item.HediffDef, pawn);
                Hediff hediff = HediffMaker.MakeHediff(item.HediffDef, pawn, targetPart);
                if (hediff == null)
                {
                    result.Status = "skipped";
                    result.Stage = "hediff";
                    result.ErrorMessage = "HediffMaker.MakeHediff returned null.";
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
                result.Notes = AppendNote(result.Notes, "Part=" + (targetPart?.def?.defName ?? "WholeBody"));

                activeSession = new ActiveSession
                {
                    Item = item,
                    Result = result,
                    InfoCardDef = item.HediffDef,
                    TestPawn = pawn,
                    AttachedHediff = hediff,
                    AllowScreenshots = ShouldCaptureScreenshots(item.SpecDef),
                    RemainingUiFrames = PreOpenSettleRepaintFrames,
                    Phase = SessionPhase.WaitingForPreOpenSettle
                };
                result.Stage = "info_open";
            }
            catch (Exception ex)
            {
                result.Status = "failed_hediff_apply";
                result.Stage = "hediff";
                result.ErrorMessage = ex.ToString();
                CleanupSelectionAndWindows();
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
                if (productDef == null)
                {
                    AddImmediateResult(
                        CreateResult(
                            RegisterDiscoveredCase(AutoTestCaseType.RecipeInfoCard, "RecipeDef", recipe.defName, recipe.label),
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
                        RegisterDiscoveredCase(AutoTestCaseType.RecipeInfoCard, "RecipeDef", recipe.defName, recipe.label),
                        "RecipeDef",
                        recipe.defName,
                        recipe.label);
                    result.Label = productDef.label ?? recipe.label ?? string.Empty;
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

        private Pawn EnsureSharedHediffPawn()
        {
            if (sharedHediffPawn != null &&
                !sharedHediffPawn.Destroyed &&
                !sharedHediffPawn.Dead &&
                sharedHediffPawn.Map == map)
            {
                return sharedHediffPawn;
            }

            Faction faction = Faction.OfPlayer ?? Find.FactionManager?.OfPlayer;
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, faction, null);
            if (pawn == null)
            {
                return null;
            }

            if (!TryAllocateThingRoot(1, 1, out IntVec3 rootCell))
            {
                return null;
            }

            ClearArea(rootCell, 1, 1);
            GenSpawn.Spawn(pawn, rootCell, map, WipeMode.Vanish);
            sharedHediffPawn = pawn;
            return sharedHediffPawn;
        }

        private void CleanupTargetModHediffs(Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs == null)
            {
                return;
            }

            foreach (Hediff hediff in pawn.health.hediffSet.hediffs
                .Where(hediff => hediff?.def?.modContentPack == targetMod)
                .ToList())
            {
                try
                {
                    pawn.health.RemoveHediff(hediff);
                }
                catch
                {
                }
            }
        }

        private BodyPartRecord ResolveHediffPart(HediffDef def, Pawn pawn)
        {
            if (def == null || pawn?.health?.hediffSet == null)
            {
                return null;
            }

            IEnumerable<BodyPartRecord> parts = pawn.health.hediffSet.GetNotMissingParts(default(BodyPartHeight), default(BodyPartDepth), null, null) ?? Enumerable.Empty<BodyPartRecord>();
            if (def.defaultInstallPart != null)
            {
                BodyPartRecord preferredPart = parts.FirstOrDefault(part => part.def == def.defaultInstallPart);
                if (preferredPart != null)
                {
                    return preferredPart;
                }
            }

            if (def.addedPartProps != null || def.injuryProps != null || def.defaultInstallPart != null)
            {
                return parts.FirstOrDefault();
            }

            return null;
        }

        private bool IsTargetRecipeDef(RecipeDef recipe)
        {
            if (recipe == null) return false;
            if (recipe.products == null || recipe.products.Count == 0) return false;

            bool recipeOwnedByTargetMod = recipe.modContentPack == targetMod;
            bool producesTargetModThing = recipe.products.Any(product =>
                product != null &&
                product.thingDef != null &&
                product.thingDef.modContentPack == targetMod &&
                IsSupportedRecipeProductDef(product.thingDef));

            if (!recipeOwnedByTargetMod && !producesTargetModThing) return false;
            return ResolvePrimaryRecipeProduct(recipe) != null &&
                   MatchesSpecFilters(AutoTestKind.Recipe, recipe);
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

        private bool TryProduceRecipe(ActiveSession session, out string errorMessage)
        {
            errorMessage = null;

            RecipeDef recipe = session.Item.RecipeDef;
            Thing worktableThing = GetPlacedRecipeWorktable(session.Item.WorktableDef);
            if (recipe == null)
            {
                errorMessage = "RecipeDef was missing from the active session.";
                return false;
            }

            if (!(worktableThing is IBillGiver billGiver))
            {
                errorMessage = "Placed worktable is unavailable or no longer implements IBillGiver.";
                return false;
            }

            Pawn worker = map.mapPawns?.FreeColonistsSpawned?.FirstOrDefault(pawn => pawn != null && !pawn.Dead);
            if (worker == null)
            {
                errorMessage = "No spawned colonist was available to evaluate the recipe.";
                return false;
            }

            Bill_Production productionBill = BillUtility.MakeNewBill(recipe, null) as Bill_Production ?? new Bill_Production(recipe, null);
            if (productionBill == null)
            {
                errorMessage = "Failed to create Bill_Production for recipe.";
                return false;
            }

            BillStack billStack = billGiver.BillStack;
            if (billStack == null)
            {
                errorMessage = "Worktable BillStack was null.";
                return false;
            }

            bool billAdded = false;
            List<Thing> ingredients = new List<Thing>();
            List<Thing> spawnedProducts = new List<Thing>();

            try
            {
                billStack.AddBill(productionBill);
                billAdded = true;

                if (!TryCreateRecipeIngredients(recipe, productionBill, worktableThing, out ingredients, out string ingredientSummary, out errorMessage))
                {
                    return false;
                }

                Thing dominantIngredient = ingredients.FirstOrDefault(thing => thing != null && !thing.Destroyed);
                IEnumerable<Thing> producedThings = GenRecipe.MakeRecipeProducts(
                    recipe,
                    worker,
                    ingredients,
                    dominantIngredient,
                    billGiver,
                    null,
                    null,
                    null);

                if (producedThings != null)
                {
                    foreach (Thing producedThing in producedThings)
                    {
                        if (producedThing == null) continue;

                        if (!TryAllocateThingRoot(1, 1, out IntVec3 spawnCell))
                        {
                            spawnCell = ResolveDropCellNear(worktableThing);
                        }

                        GenSpawn.Spawn(producedThing, spawnCell, map, WipeMode.Vanish);
                        spawnedProducts.Add(producedThing);
                    }
                }

                if (spawnedProducts.Count == 0)
                {
                    errorMessage = "Recipe execution returned no products.";
                    return false;
                }

                productionBill.Notify_IterationCompleted(worker, ingredients);
                session.Result.Notes = AppendNote(session.Result.Notes, "InfoCardOpened=True");
                session.Result.Notes = AppendNote(session.Result.Notes, "Ingredients=" + ingredientSummary);
                session.Result.Notes = AppendNote(session.Result.Notes, "Products=" + BuildThingSummary(spawnedProducts));
                session.Result.Notes = AppendNote(session.Result.Notes, "Worker=" + worker.LabelShortCap);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.ToString();
                return false;
            }
            finally
            {
                foreach (Thing ingredient in ingredients)
                {
                    try
                    {
                        if (ingredient != null && !ingredient.Destroyed)
                        {
                            ingredient.Destroy(DestroyMode.Vanish);
                        }
                    }
                    catch
                    {
                    }
                }

                if (billAdded)
                {
                    try
                    {
                        billStack.Delete(productionBill);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private bool TryCreateRecipeIngredients(
            RecipeDef recipe,
            Bill productionBill,
            Thing worktableThing,
            out List<Thing> ingredients,
            out string ingredientSummary,
            out string errorMessage)
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
                if (!TryResolveIngredientDef(ingredientCount, out ThingDef ingredientDef))
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

                    GenSpawn.Spawn(ingredientThing, ResolveDropCellNear(worktableThing), map, WipeMode.Vanish);
                    ingredients.Add(ingredientThing);

                    placedCount += stackCount;
                    remainingCount -= stackCount;
                }

                summaryParts.Add(ingredientDef.defName + "x" + placedCount);
            }

            ingredientSummary = string.Join(", ", summaryParts.ToArray());
            return true;
        }

        private bool TryResolveIngredientDef(IngredientCount ingredientCount, out ThingDef ingredientDef)
        {
            ingredientDef = null;
            if (ingredientCount == null) return false;

            if (ingredientCount.filter == null) return false;

            ingredientDef = ingredientCount.filter.AllowedThingDefs
                .Where(IsUsableIngredientDef)
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
