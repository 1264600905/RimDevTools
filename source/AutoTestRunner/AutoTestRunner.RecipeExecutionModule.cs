using System;
using System.Collections.Generic;
using System.Linq;
using LogSyncMod;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class AutoTestRunner
    {
        private sealed class AutoTestRecipeExecutionResult
        {
            public bool Success;
            public AutoTestFailureKind FailureKind;
            public string ErrorMessage;
            public bool PassedAfterRetry;
        }

        private interface IAutoTestRecipeExecutionAdapter
        {
            bool CanHandle(AutoTestRecipeExecutionKind kind);
            AutoTestRecipeExecutionResult Execute(ActiveSession session);
        }

        private abstract class RecipeExecutionAdapterBase : IAutoTestRecipeExecutionAdapter
        {
            protected readonly AutoTestRunner Runner;

            protected RecipeExecutionAdapterBase(AutoTestRunner runner)
            {
                Runner = runner;
            }

            public abstract bool CanHandle(AutoTestRecipeExecutionKind kind);
            public abstract AutoTestRecipeExecutionResult Execute(ActiveSession session);

            protected AutoTestRecipeWorkerResolution CreateWorker(RecipeDef recipe)
            {
                AutoTestRecipeWorkerService service = new AutoTestRecipeWorkerService(Runner.map, delegate
                {
                    if (!Runner.TryAllocateThingRoot(1, 1, out IntVec3 cell)) return IntVec3.Invalid;
                    Runner.ClearArea(cell, 1, 1);
                    return cell;
                });
                return service.Create(recipe);
            }

            protected static AutoTestRecipeExecutionResult Failed(AutoTestFailureKind kind, string error)
            {
                return new AutoTestRecipeExecutionResult { FailureKind = kind, ErrorMessage = error };
            }

            protected static AutoTestRecipeExecutionResult Passed(bool passedAfterRetry)
            {
                return new AutoTestRecipeExecutionResult { Success = true, PassedAfterRetry = passedAfterRetry };
            }
        }

        private sealed class StandardRecipeExecutionAdapter : RecipeExecutionAdapterBase
        {
            public StandardRecipeExecutionAdapter(AutoTestRunner runner) : base(runner) { }
            public override bool CanHandle(AutoTestRecipeExecutionKind kind) => kind == AutoTestRecipeExecutionKind.Standard;

            public override AutoTestRecipeExecutionResult Execute(ActiveSession session)
            {
                RecipeDef recipe = session.Item.RecipeDef;
                Thing worktableThing = session.RecipeWorktableThing ?? Runner.GetPlacedRecipeWorktable(session.Item.WorktableDef);
                if (recipe == null) return Failed(AutoTestFailureKind.DeterministicFailure, "RecipeDef was missing from the active session.");
                if (!(worktableThing is IBillGiver billGiver)) return Failed(AutoTestFailureKind.DeterministicFailure, "Placed worktable is unavailable or not an IBillGiver.");

                AutoTestRecipeWorkerResolution workerResolution = CreateWorker(recipe);
                Pawn worker = workerResolution.Worker;
                if (worker == null) return Failed(AutoTestFailureKind.TransientPawnGeneration, workerResolution.ErrorMessage);

                Bill_Production productionBill = session.RecipeBill ?? recipe.MakeNewBill() as Bill_Production;
                if (productionBill == null)
                {
                    AutoTestRecipeWorkerService.Destroy(worker);
                    return Failed(AutoTestFailureKind.DeterministicFailure, "Recipe did not create a Bill_Production.");
                }

                BillStack billStack = billGiver.BillStack;
                if (billStack == null)
                {
                    AutoTestRecipeWorkerService.Destroy(worker);
                    return Failed(AutoTestFailureKind.DeterministicFailure, "Worktable BillStack was null.");
                }

                bool billAdded = false;
                List<Thing> ingredients = new List<Thing>();
                try
                {
                    if (session.RecipeBillAdded) billAdded = true;
                    else
                    {
                        billStack.AddBill(productionBill);
                        session.RecipeBillAdded = true;
                        billAdded = true;
                    }
                    session.RecipeBill = productionBill;

                    if (!Runner.TryCreateRecipeIngredients(recipe, productionBill, worktableThing, out ingredients, out string ingredientSummary, out string ingredientError))
                        return Failed(AutoTestFailureKind.DeterministicFailure, ingredientError);

                    Thing dominantIngredient = ingredients.FirstOrDefault(thing => thing != null && !thing.Destroyed);
                    List<Thing> rawProducts = (GenRecipe.MakeRecipeProducts(recipe, worker, ingredients, dominantIngredient, billGiver, null, null, null)
                        ?? Enumerable.Empty<Thing>()).Where(product => product != null).ToList();
                    if (rawProducts.Count == 0)
                        return Failed(AutoTestFailureKind.DeterministicFailure, "Recipe execution returned no products.");

                    Dictionary<ThingDef, int> expectedTotals = rawProducts
                        .GroupBy(product => product.def)
                        .ToDictionary(group => group.Key, group => group.Sum(product => product.stackCount));
                    Dictionary<ThingDef, int> placedTotals = new Dictionary<ThingDef, int>();
                    Dictionary<ThingDef, HashSet<Thing>> placedStacks = new Dictionary<ThingDef, HashSet<Thing>>();

                    foreach (Thing product in rawProducts)
                    {
                        if (product is Building)
                            Runner.PrepareBuildingFactionBeforeSpawn(product, AutoTestBuildingContext.InteractiveBuilding, session.Result);
                        IntVec3 center;
                        if (!Runner.TryAllocateThingRoot(1, 1, out center)) center = Runner.ResolveDropCellNear(worktableThing);
                        else Runner.ClearArea(center, 1, 1);

                        bool placed = GenPlace.TryPlaceThing(product, center, Runner.map, ThingPlaceMode.Near, delegate(Thing placedThing, int count)
                        {
                            if (placedThing == null) return;
                            if (!placedTotals.ContainsKey(placedThing.def)) placedTotals[placedThing.def] = 0;
                            placedTotals[placedThing.def] += count;
                            if (!placedStacks.TryGetValue(placedThing.def, out HashSet<Thing> stacks))
                            {
                                stacks = new HashSet<Thing>();
                                placedStacks[placedThing.def] = stacks;
                            }
                            stacks.Add(placedThing);
                            if (!session.RecipeProducts.Contains(placedThing)) session.RecipeProducts.Add(placedThing);
                        });
                        if (!placed)
                            return Failed(AutoTestFailureKind.DeterministicFailure, "GenPlace.TryPlaceThing failed for product " + product.def?.defName + ".");
                    }

                    AutoTestProductPlacementVerification verification = AutoTestProductPlacementVerifier.Verify(
                        expectedTotals.ToDictionary(pair => pair.Key.defName, pair => pair.Value, StringComparer.Ordinal),
                        placedTotals.ToDictionary(pair => pair.Key.defName, pair => pair.Value, StringComparer.Ordinal),
                        placedStacks.SelectMany(pair => pair.Value.Select(stack => new AutoTestProductStackSnapshot
                        {
                            DefName = pair.Key.defName,
                            StackCount = stack.stackCount,
                            StackLimit = stack.def.stackLimit
                        })));
                    if (!verification.Success)
                        return Failed(AutoTestFailureKind.DeterministicFailure, verification.ErrorMessage);

                    productionBill.Notify_IterationCompleted(worker, ingredients);
                    session.Result.Notes = AppendNote(session.Result.Notes, "BillConfigOpened=True");
                    session.Result.Notes = AppendNote(session.Result.Notes, "Ingredients=" + ingredientSummary);
                    session.Result.Notes = AppendNote(session.Result.Notes, "Products=" + BuildPlacedProductSummary(placedTotals, placedStacks));
                    session.Result.Notes = AppendNote(session.Result.Notes, "BillIterationCompleted=True");
                    session.Result.Notes = AppendNote(session.Result.Notes, "RecipeWorkerGenerated=True;RecipeWorker=" + worker.LabelShortCap + ";RecipeWorkerKind=" + worker.kindDef?.defName);
                    if (workerResolution.PassedAfterRetry)
                    {
                        session.Result.Notes = AppendNote(session.Result.Notes, "RetryKind=" + AutoTestFailureKind.TransientPawnGeneration);
                        session.Result.Notes = AppendNote(session.Result.Notes, "FirstFailure=" + TrimForNote(workerResolution.FirstFailure));
                    }
                    return Passed(workerResolution.PassedAfterRetry);
                }
                catch (Exception ex)
                {
                    return Failed(AutoTestFailureKind.DeterministicFailure, ex.ToString());
                }
                finally
                {
                    DestroyThings(ingredients);
                    DeleteBill(session, billStack, productionBill, billAdded);
                    AutoTestRecipeWorkerService.Destroy(worker);
                    session.Result.Notes = AppendNote(session.Result.Notes, "RecipeWorkerDestroyed=" + worker.Destroyed);
                }
            }
        }

        private sealed class MechGestationRecipeExecutionAdapter : RecipeExecutionAdapterBase
        {
            public MechGestationRecipeExecutionAdapter(AutoTestRunner runner) : base(runner) { }
            public override bool CanHandle(AutoTestRecipeExecutionKind kind) => kind == AutoTestRecipeExecutionKind.MechGestation;
            public override AutoTestRecipeExecutionResult Execute(ActiveSession session) => ExecuteMechBill(session, resurrection: false);

            private AutoTestRecipeExecutionResult ExecuteMechBill(ActiveSession session, bool resurrection)
            {
                return Runner.ExecuteMechBill(session, resurrection, CreateWorker);
            }
        }

        private sealed class MechResurrectionRecipeExecutionAdapter : RecipeExecutionAdapterBase
        {
            public MechResurrectionRecipeExecutionAdapter(AutoTestRunner runner) : base(runner) { }
            public override bool CanHandle(AutoTestRecipeExecutionKind kind) => kind == AutoTestRecipeExecutionKind.MechResurrection;
            public override AutoTestRecipeExecutionResult Execute(ActiveSession session) => Runner.ExecuteMechBill(session, resurrection: true, CreateWorker);
        }

        private sealed class UnsupportedSpecialRecipeExecutionAdapter : RecipeExecutionAdapterBase
        {
            public UnsupportedSpecialRecipeExecutionAdapter(AutoTestRunner runner) : base(runner) { }
            public override bool CanHandle(AutoTestRecipeExecutionKind kind) => kind == AutoTestRecipeExecutionKind.PawnProduct || kind == AutoTestRecipeExecutionKind.UnsupportedCustom;
            public override AutoTestRecipeExecutionResult Execute(ActiveSession session)
            {
                return Failed(AutoTestFailureKind.UnsupportedPath, "No safe public execution path is registered for recipe " + session.Item.RecipeDef?.defName + ".");
            }
        }

        private AutoTestRecipeExecutionResult ExecuteRecipe(ActiveSession session)
        {
            AutoTestRecipeClassification classification = ClassifyRecipe(session.Item.RecipeDef, session.Item.WorktableDef, session.Item.SpecDef);
            session.Result.Notes = AppendNote(session.Result.Notes, "RecipeExecutionKind=" + classification.Kind);
            session.Result.Notes = AppendNote(session.Result.Notes, "RecipeExecutionAdapter=" + classification.Adapter);
            session.Result.Notes = AppendNote(session.Result.Notes, "RecipeClassificationReason=" + classification.Reason);

            IAutoTestRecipeExecutionAdapter[] adapters =
            {
                new StandardRecipeExecutionAdapter(this),
                new MechGestationRecipeExecutionAdapter(this),
                new MechResurrectionRecipeExecutionAdapter(this),
                new UnsupportedSpecialRecipeExecutionAdapter(this)
            };
            IAutoTestRecipeExecutionAdapter adapter = adapters.FirstOrDefault(candidate => candidate.CanHandle(classification.Kind));
            return adapter == null
                ? new AutoTestRecipeExecutionResult { FailureKind = AutoTestFailureKind.UnsupportedPath, ErrorMessage = "No recipe adapter matched " + classification.Kind + "." }
                : adapter.Execute(session);
        }

        private AutoTestRecipeClassification ClassifyRecipe(RecipeDef recipe, ThingDef worktableDef, LogSyncAutoTestSpecDef spec)
        {
            bool productIsPawn = recipe?.products != null && recipe.products.Any(product => product?.thingDef?.race != null ||
                (product?.thingDef?.thingClass != null && typeof(Pawn).IsAssignableFrom(product.thingDef.thingClass)));
            bool hasCorpseIngredient = RecipeAllowsCorpse(recipe);
            bool gestator = worktableDef?.thingClass != null && typeof(Building_MechGestator).IsAssignableFrom(worktableDef.thingClass);
            return AutoTestRecipeExecutionClassifier.Classify(new AutoTestRecipeClassificationInput
            {
                GestationCycles = recipe?.gestationCycles ?? 0,
                MechResurrection = recipe?.mechResurrection == true,
                ProductIsPawn = productIsPawn,
                HasCorpseIngredient = hasCorpseIngredient,
                WorktableIsMechGestator = gestator,
                RecipeLooksLikeBaseMechanoidRecipe = (recipe?.gestationCycles ?? 0) > 0 || recipe?.mechResurrection == true,
                RecipeLooksCustom = false,
                ExplicitAdapter = spec?.recipeExecutionAdapter
            });
        }

        private AutoTestRecipeExecutionResult ExecuteMechBill(ActiveSession session, bool resurrection, Func<RecipeDef, AutoTestRecipeWorkerResolution> createWorker)
        {
            RecipeDef recipe = session.Item.RecipeDef;
            if (recipe == null) return new AutoTestRecipeExecutionResult { FailureKind = AutoTestFailureKind.DeterministicFailure, ErrorMessage = "RecipeDef was missing." };
            if (!(session.RecipeWorktableThing is Building_MechGestator gestator))
                return new AutoTestRecipeExecutionResult { FailureKind = AutoTestFailureKind.UnsupportedPath, ErrorMessage = "Recipe worktable is not Building_MechGestator." };

            AutoTestRecipeWorkerResolution workerResolution = createWorker(recipe);
            Pawn worker = workerResolution.Worker;
            if (worker == null)
                return new AutoTestRecipeExecutionResult { FailureKind = AutoTestFailureKind.TransientPawnGeneration, ErrorMessage = workerResolution.ErrorMessage };

            Bill_Mech mechBill = session.RecipeBill as Bill_Mech ?? recipe.MakeNewBill() as Bill_Mech;
            if (mechBill == null)
            {
                AutoTestRecipeWorkerService.Destroy(worker);
                return new AutoTestRecipeExecutionResult { FailureKind = AutoTestFailureKind.UnsupportedPath, ErrorMessage = "Recipe did not create a Bill_Mech." };
            }
            if (resurrection && !(mechBill is Bill_ResurrectMech))
            {
                AutoTestRecipeWorkerService.Destroy(worker);
                return new AutoTestRecipeExecutionResult { FailureKind = AutoTestFailureKind.UnsupportedPath, ErrorMessage = "Resurrection recipe did not create Bill_ResurrectMech." };
            }

            BillStack billStack = gestator.BillStack;
            bool billAdded = false;
            List<Thing> ingredients = new List<Thing>();
            Pawn corpsePawn = null;
            try
            {
                if (session.RecipeBillAdded) billAdded = true;
                else
                {
                    billStack.AddBill(mechBill);
                    session.RecipeBillAdded = true;
                    billAdded = true;
                }
                session.RecipeBill = mechBill;

                if (!TryCreateRecipeIngredients(recipe, mechBill, gestator, out ingredients, out string ingredientSummary, out string ingredientError, skipCorpseIngredients: resurrection))
                    return new AutoTestRecipeExecutionResult { FailureKind = AutoTestFailureKind.DeterministicFailure, ErrorMessage = ingredientError };

                if (resurrection)
                {
                    if (!TryCreateMechCorpse(recipe, gestator, out Corpse corpse, out corpsePawn, out string corpseError))
                        return new AutoTestRecipeExecutionResult { FailureKind = AutoTestFailureKind.DeterministicFailure, ErrorMessage = corpseError };
                    ingredients.Insert(0, corpse);
                    ingredientSummary = corpse.def.defName + "x1" + (ingredientSummary == "None" ? string.Empty : ", " + ingredientSummary);
                }

                mechBill.Notify_DoBillStarted(worker);
                foreach (Thing ingredient in ingredients.ToList())
                {
                    if (ingredient.Spawned) ingredient.DeSpawn();
                    int count = ingredient.stackCount;
                    if (!gestator.innerContainer.TryAdd(ingredient))
                        return new AutoTestRecipeExecutionResult { FailureKind = AutoTestFailureKind.DeterministicFailure, ErrorMessage = "Gestator rejected ingredient " + ingredient.def?.defName + "." };
                    gestator.Notify_HauledTo(worker, ingredient, count);
                }

                mechBill.Notify_BillWorkFinished(worker);
                mechBill.ForceCompleteAllCycles();
                mechBill.BillTick();
                Pawn productPawn = gestator.GestatingMech;
                if (productPawn == null || productPawn.health == null || productPawn.kindDef == null || productPawn.Dead)
                    return new AutoTestRecipeExecutionResult { FailureKind = AutoTestFailureKind.DeterministicFailure, ErrorMessage = "Mech gestator did not produce a complete living Pawn." };

                Thing product = GenRecipe.FinalizeGestatedPawns(mechBill, worker, null, null).FirstOrDefault();
                if (product != productPawn)
                    return new AutoTestRecipeExecutionResult { FailureKind = AutoTestFailureKind.DeterministicFailure, ErrorMessage = "FinalizeGestatedPawns returned an unexpected product." };
                gestator.innerContainer.Remove(product);

                IntVec3 center;
                if (!TryAllocateThingRoot(1, 1, out center)) center = ResolveDropCellNear(gestator);
                else ClearArea(center, 1, 1);
                if (!GenPlace.TryPlaceThing(product, center, map, ThingPlaceMode.Near, delegate(Thing placedThing, int count)
                {
                    if (placedThing != null && !session.RecipeProducts.Contains(placedThing)) session.RecipeProducts.Add(placedThing);
                }))
                    return new AutoTestRecipeExecutionResult { FailureKind = AutoTestFailureKind.DeterministicFailure, ErrorMessage = "Failed to place gestated Pawn on the map." };

                Pawn placedPawn = product as Pawn;
                if (placedPawn == null || placedPawn.health == null || placedPawn.kindDef == null || placedPawn.Dead || placedPawn.Map != map)
                    return new AutoTestRecipeExecutionResult { FailureKind = AutoTestFailureKind.DeterministicFailure, ErrorMessage = "Placed mech Pawn failed health/kind/faction/death/map validation." };

                mechBill.Notify_IterationCompleted(worker, ingredients);
                session.Result.Notes = AppendNote(session.Result.Notes, "BillConfigOpened=True");
                session.Result.Notes = AppendNote(session.Result.Notes, "Ingredients=" + ingredientSummary);
                session.Result.Notes = AppendNote(session.Result.Notes, "Products=" + placedPawn.kindDef.defName + "x1(stacks=1)");
                session.Result.Notes = AppendNote(session.Result.Notes, "MechPawnHealthReadable=True;MechPawnFaction=" + (placedPawn.Faction?.def?.defName ?? "None") + ";MechPawnDead=False;MechPawnMap=True");
                session.Result.Notes = AppendNote(session.Result.Notes, "BillIterationCompleted=True;GestationAccelerated=True;GestatorPowerCompPresent=" + (gestator.Power != null));
                session.Result.Notes = AppendNote(session.Result.Notes, "RecipeWorkerGenerated=True;RecipeWorker=" + worker.LabelShortCap + ";RecipeWorkerKind=" + worker.kindDef?.defName);
                if (workerResolution.PassedAfterRetry)
                {
                    session.Result.Notes = AppendNote(session.Result.Notes, "RetryKind=" + AutoTestFailureKind.TransientPawnGeneration);
                    session.Result.Notes = AppendNote(session.Result.Notes, "FirstFailure=" + TrimForNote(workerResolution.FirstFailure));
                }
                return new AutoTestRecipeExecutionResult { Success = true, PassedAfterRetry = workerResolution.PassedAfterRetry };
            }
            catch (Exception ex)
            {
                return new AutoTestRecipeExecutionResult { FailureKind = AutoTestFailureKind.DeterministicFailure, ErrorMessage = ex.ToString() };
            }
            finally
            {
                DestroyThings(ingredients);
                if (corpsePawn != null && !corpsePawn.Destroyed && !session.RecipeProducts.Contains(corpsePawn)) corpsePawn.Destroy(DestroyMode.Vanish);
                DeleteBill(session, billStack, mechBill, billAdded);
                AutoTestRecipeWorkerService.Destroy(worker);
                session.Result.Notes = AppendNote(session.Result.Notes, "RecipeWorkerDestroyed=" + worker.Destroyed);
            }
        }

        private bool TryCreateMechCorpse(RecipeDef recipe, Thing gestator, out Corpse corpse, out Pawn corpsePawn, out string error)
        {
            corpse = null;
            corpsePawn = null;
            error = null;
            ThingDef corpseDef = recipe.fixedIngredientFilter?.AllowedThingDefs
                .Where(def => def?.thingClass != null && typeof(Corpse).IsAssignableFrom(def.thingClass))
                .OrderByDescending(def => def.modContentPack == targetMod ? 1 : 0)
                .ThenBy(def => def.defName, StringComparer.Ordinal)
                .FirstOrDefault();
            if (corpseDef == null)
            {
                error = "No allowed Corpse ThingDef was found in fixedIngredientFilter.";
                return false;
            }

            ThingDef race = corpseDef.ingestible?.sourceDef ?? DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def?.race?.corpseDef == corpseDef)
                .OrderByDescending(def => def.modContentPack == targetMod ? 1 : 0)
                .ThenBy(def => def.defName, StringComparer.Ordinal)
                .FirstOrDefault();
            PawnKindDef kind = DefDatabase<PawnKindDef>.AllDefsListForReading
                .Where(candidate => candidate?.race == race)
                .OrderByDescending(candidate => candidate.modContentPack == targetMod ? 1 : 0)
                .ThenBy(candidate => candidate.defName, StringComparer.Ordinal)
                .FirstOrDefault();
            if (kind == null)
            {
                error = "No PawnKindDef was found for corpse " + corpseDef.defName + ".";
                return false;
            }

            PawnGenerationRequest request = new PawnGenerationRequest(kind, Faction.OfPlayer ?? Find.FactionManager?.OfPlayer,
                PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                canGeneratePawnRelations: false, mustBeCapableOfViolence: false, developmentalStages: DevelopmentalStage.Adult);
            corpsePawn = PawnGenerator.GeneratePawn(request);
            if (corpsePawn == null)
            {
                error = "Pawn generation failed for resurrection corpse kind " + kind.defName + ".";
                return false;
            }

            IntVec3 cell;
            if (!TryAllocateThingRoot(1, 1, out cell)) cell = ResolveDropCellNear(gestator);
            else ClearArea(cell, 1, 1);
            GenSpawn.Spawn(corpsePawn, cell, map, WipeMode.Vanish);
            corpsePawn.Kill(null, null);
            corpse = corpsePawn.Corpse;
            if (corpse == null || corpse.def != corpseDef || corpse.InnerPawn != corpsePawn || !corpsePawn.Dead)
            {
                error = "Normal death flow did not produce the required corpse " + corpseDef.defName + ".";
                return false;
            }
            return true;
        }

        private static bool RecipeAllowsCorpse(RecipeDef recipe)
        {
            if (recipe == null) return false;
            bool fixedCorpse = recipe.fixedIngredientFilter?.AllowedThingDefs.Any(def => def?.thingClass != null && typeof(Corpse).IsAssignableFrom(def.thingClass)) == true;
            bool ingredientCorpse = recipe.ingredients?.Any(ingredient => ingredient?.filter?.AllowedThingDefs.Any(def => def?.thingClass != null && typeof(Corpse).IsAssignableFrom(def.thingClass)) == true) == true;
            return fixedCorpse || ingredientCorpse;
        }

        private static string BuildPlacedProductSummary(Dictionary<ThingDef, int> totals, Dictionary<ThingDef, HashSet<Thing>> stacks)
        {
            return string.Join(", ", totals.OrderBy(pair => pair.Key.defName, StringComparer.Ordinal)
                .Select(pair => pair.Key.defName + "x" + pair.Value + "(stacks=" + (stacks.TryGetValue(pair.Key, out HashSet<Thing> values) ? values.Count : 0) + ")")
                .ToArray());
        }

        private static void DestroyThings(IEnumerable<Thing> things)
        {
            foreach (Thing thing in (things ?? Enumerable.Empty<Thing>()).Distinct().ToList())
            {
                try { if (thing != null && !thing.Destroyed) thing.Destroy(DestroyMode.Vanish); } catch { }
            }
        }

        private static void DeleteBill(ActiveSession session, BillStack billStack, Bill bill, bool billAdded)
        {
            if (!billAdded || billStack == null || bill == null) return;
            try
            {
                billStack.Delete(bill);
                session.RecipeBillAdded = false;
                session.RecipeBill = bill as Bill_Production;
            }
            catch { }
        }
    }
}
