using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class ScenarioGameBackend
    {
        private void ExecuteFunctionalAction(XElement step)
        {
            var t = One(A(step, "actor"));
            switch (step.Name.LocalName)
            {
                case "BindMech":
                    var mech = t as Pawn ?? throw new ScenarioConfigurationException("BindMech requires mech Pawn");
                    var controller = Pawn(A(step, "target"));
                    if (!mech.RaceProps.IsMechanoid || controller.mechanitor == null || mech.Faction != Faction.OfPlayer || controller.Faction != Faction.OfPlayer) throw new ScenarioConfigurationException("BindMech requires colony mech and a player mechanitor with MechlinkImplant configured");
                    var previous = mech.GetOverseer();
                    if (previous != null && !owned.Contains(previous)) throw new ScenarioConfigurationException("Cannot reassign an overseer outside the scene");
                    previous?.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, mech);
                    controller.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech); controller.mechanitor.Notify_BandwidthChanged();
                    if (mech.GetOverseer() != controller) throw new InvalidOperationException("Mech control postcondition failed");
                    trace("mech.controller", mech.GetUniqueLoadID() + " -> " + controller.GetUniqueLoadID());
                    break;
                case "ApplyDamage":
                    var amount = (float)ScenarioLoader.D(step, "amount");
                    if (amount <= 0) throw new ScenarioConfigurationException("Damage amount must be positive");
                    var info = new DamageInfo(Def<DamageDef>(A(step, "def")), amount, (float)ScenarioLoader.D(step, "armorPenetration", 0), instigator: A(step, "instigator") == null ? null : One(A(step, "instigator")), hitPart: t is Pawn damagePawn ? Part(damagePawn, step) : null);
                    var result = t.TakeDamage(info);
                    trace("damage.settled", "target=" + t.GetUniqueLoadID() + "; requested=" + amount + "; totalDamageDealt=" + result.totalDamageDealt);
                    break;
                case "ExecuteRecipe": ExecuteRecipeStep(step); break;
                case "TransferThing":
                    var receiver = Pawn(A(step, "target")); Need(receiver.inventory, "inventory");
                    if (t is Pawn || t is Corpse || t.Destroyed || !t.Spawned || t.Map != Map) throw new ScenarioConfigurationException("TransferThing requires a live scene item on the map");
                    t.DeSpawn();
                    if (!receiver.inventory.innerContainer.TryAdd(t, canMergeWithExistingStacks: false)) throw new InvalidOperationException("Native inventory transfer rejected");
                    break;
                case "DropInventory":
                    var pawn = t as Pawn ?? throw new ScenarioConfigurationException("DropInventory requires Pawn actor");
                    var item = One(A(step, "target"));
                    var dropCell = GenRadial.RadialCellsAround(pawn.Position, 3, true).FirstOrDefault(c => c.InBounds(Map) && c.Standable(Map) && !c.GetThingList(Map).Any(existing => existing.CanStackWith(item)));
                    if (!dropCell.IsValid || pawn.inventory?.innerContainer.Contains(item) != true || !pawn.inventory.innerContainer.TryDrop(item, dropCell, Map, ThingPlaceMode.Direct, out var dropped) || dropped != item) throw new InvalidOperationException("Native inventory drop rejected/changed identity");
                    break;
                case "SetRot":
                    var rot = t.TryGetComp<CompRottable>() ?? throw new ScenarioConfigurationException("Actor has no rottable component");
                    var stage = Enum.Parse<RotStage>(A(step, "state"));
                    if (stage == RotStage.Fresh) rot.RotProgress = 0; else rot.RotImmediately(stage);
                    if (rot.Stage != stage) throw new InvalidOperationException("Rot stage postcondition failed");
                    break;
                case "Resurrect":
                    var corpse = t as Corpse ?? throw new ScenarioConfigurationException("Resurrect requires Corpse actor");
                    var inner = corpse.InnerPawn;
                    if (!ResurrectionUtility.TryResurrect(inner) || inner.Dead || !inner.Spawned || inner.Map != Map) throw new InvalidOperationException("Native resurrection failed");
                    trace("corpse.resurrected", "corpse=" + corpse.GetUniqueLoadID() + "; sameInnerPawn=" + inner.GetUniqueLoadID());
                    MarkCoverage("CorpseLifecycle", inner.kindDef.defName);
                    break;
            }
            trace("action." + step.Name, step.ToString(SaveOptions.DisableFormatting));
            if (new[] { "TransferThing", "SetRot" }.Contains(step.Name.LocalName)) MarkCoverage("ThingBehavior", t.def.defName);
            if (step.Name == "DropInventory") MarkCoverage("ThingBehavior", One(A(step, "target")).def.defName);
        }

        private void ExecuteRecipeStep(XElement step)
        {
            // These are the same native recipe execution phases as the ordinary functional adapter.
            // Work/cycle acceleration is explicit and recorded; this does not test worker job AI or any UI.
            if (A(step, "accelerate") != "true") throw new ScenarioConfigurationException("ExecuteRecipe currently requires explicit accelerate=true for native functional execution");
            var recipe = Def<RecipeDef>(A(step, "recipe"));
            var worker = Pawn(A(step, "actor"));
            var table = One(A(step, "worktable"));
            if (!(table is IBillGiver giver) || !table.def.AllRecipes.Contains(recipe)) throw new ScenarioConfigurationException("Worktable does not support the requested RecipeDef");
            if (!worker.Spawned || worker.Map != Map || worker.Dead || worker.Downed || !table.Spawned || table.Map != Map) throw new ScenarioConfigurationException("Recipe worker/table unavailable in scene");
            if (recipe.mechanitorOnlyRecipe && worker.mechanitor == null) throw new ScenarioConfigurationException("Recipe requires a mechanitor worker configured with a native mechlink");
            var bill = recipe.MakeNewBill();
            var supplies = (step.Element("Ingredients")?.Elements("Ingredient") ?? Enumerable.Empty<XElement>()).Select(e => new { Thing = One(A(e, "actor")), Count = I(e, "count") }).ToList();
            if (supplies.Select(s => s.Thing).Distinct().Count() != supplies.Count || supplies.Any(s => s.Thing.Destroyed || s.Count < 1 || s.Count > s.Thing.stackCount || !s.Thing.Spawned || s.Thing.Map != Map)) throw new ScenarioConfigurationException("Recipe ingredient identity/count/location invalid");
            var remaining = supplies.ToDictionary(s => s.Thing, s => s.Count);
            foreach (var requirement in recipe.ingredients)
            {
                double need = 1;
                foreach (var supply in supplies.Where(s => requirement.filter.Allows(s.Thing) && (requirement.IsFixedIngredient || bill.IsFixedOrAllowedIngredient(s.Thing))))
                {
                    var units = requirement.CountRequiredOfFor(supply.Thing.def, recipe, bill);
                    if (units <= 0) continue;
                    int take = Math.Min(remaining[supply.Thing], (int)Math.Ceiling(need * units - .00001));
                    remaining[supply.Thing] -= take; need -= (double)take / units;
                    if (need <= .00001) break;
                }
                if (need > .00001) throw new ScenarioConfigurationException("Explicit ingredients do not satisfy recipe requirement: " + requirement.Summary);
            }
            if (remaining.Values.Any(n => n > 0)) throw new ScenarioConfigurationException("Explicit recipe ingredients contain unused units; provide the exact consumed quantities");
            var ingredients = new List<Thing>(); var products = new List<Thing>();
            giver.BillStack.AddBill(bill);
            try
            {
                foreach (var s in supplies) { var ingredient = s.Thing.SplitOff(s.Count); owned.Add(ingredient); ingredients.Add(ingredient); }
                trace("recipe.inputs", "recipe=" + recipe.defName + "; worker=" + worker.GetUniqueLoadID() + "; table=" + table.GetUniqueLoadID() + "; accelerate=true; ingredients=" + string.Join(",", ingredients.Select(t => t.GetUniqueLoadID() + ":" + t.def.defName + "x" + t.stackCount)));
                if (bill is Bill_Mech mech)
                {
                    var gestator = table as Building_MechGestator ?? throw new ScenarioConfigurationException("Mech recipe requires native mech gestator");
                    mech.Notify_DoBillStarted(worker);
                    foreach (var ingredient in ingredients)
                    { if (ingredient.Spawned) ingredient.DeSpawn(); var count = ingredient.stackCount; if (!gestator.innerContainer.TryAdd(ingredient)) throw new InvalidOperationException("Gestator rejected ingredient"); gestator.Notify_HauledTo(worker, ingredient, count); }
                    mech.Notify_BillWorkFinished(worker); mech.ForceCompleteAllCycles(); mech.BillTick();
                    var pawn = gestator.GestatingMech ?? throw new InvalidOperationException("No living gestated mech produced");
                    owned.Add(pawn);
                    var finalized = GenRecipe.FinalizeGestatedPawns(mech, worker, null, null).ToList();
                    if (finalized.Count != 1 || finalized[0] != pawn || pawn.Dead) throw new InvalidOperationException("Mech finalization changed expected pawn identity");
                    gestator.innerContainer.Remove(pawn); products.Add(pawn);
                }
                else
                {
                    if (recipe.products.Any(p => p.thingDef.race != null)) throw new ScenarioConfigurationException("Unregistered pawn-product recipe path");
                    products.AddRange(GenRecipe.MakeRecipeProducts(recipe, worker, ingredients, ingredients.FirstOrDefault(), giver, null, null, null));
                    foreach (var product in products) owned.Add(product);
                    foreach (var ingredient in ingredients) recipe.Worker.ConsumeIngredient(ingredient, recipe, Map);
                }
                if (products.Count == 0) throw new InvalidOperationException("Native recipe returned no products");
                var placed = new List<Thing>();
                foreach (var product in products)
                {
                    if (!GenPlace.TryPlaceThing(product, table.Position, Map, ThingPlaceMode.Near, (thing, count) => { owned.Add(thing); if (!placed.Contains(thing)) placed.Add(thing); })) throw new InvalidOperationException("Native recipe product placement failed");
                }
                if (bill is Bill_Production production) production.Notify_IterationCompleted(worker, ingredients);
                actors[A(step, "output")] = placed;
                trace("recipe.products", string.Join(",", placed.Select(p => p.GetUniqueLoadID() + ":" + p.def.defName + "x" + p.stackCount)));
                MarkCoverage("RecipeExecution", recipe.defName);
            }
            finally { giver.BillStack.Delete(bill); }
        }
    }
}
