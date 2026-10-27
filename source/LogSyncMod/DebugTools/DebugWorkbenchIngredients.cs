using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod
{
    [HarmonyPatch]
    internal static class DebugWorkbenchIngredientsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Building), nameof(Building.GetGizmos));
            // A mod worktable may replace GetGizmos without calling base.GetGizmos().
            foreach (Type type in GenTypes.AllTypes.Where(t => !t.ContainsGenericParameters && typeof(Building_WorkTable).IsAssignableFrom(t)))
            {
                MethodInfo method = type.GetMethod(nameof(Building.GetGizmos),
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (method != null && !method.IsAbstract) yield return method;
            }
        }

        private static void Postfix(Building __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!Prefs.DevMode || !__instance.Spawned || !(__instance is IBillGiver giver) ||
                __instance.GetInspectTabs()?.Any(tab => tab is ITab_Bills) != true) return;
            __result = AddCommand(__result, __instance, giver);
        }

        private static IEnumerable<Gizmo> AddCommand(IEnumerable<Gizmo> original, Building table, IBillGiver giver)
        {
            bool alreadyAdded = false;
            foreach (Gizmo gizmo in original)
            {
                if (gizmo is DebugIngredientsCommand) alreadyAdded = true;
                yield return gizmo;
            }
            if (alreadyAdded) yield break;
            var command = new DebugIngredientsCommand
            {
                defaultLabel = "DEV: " + "LogSyncMod.DebugGiveIngredients".Translate(),
                defaultDesc = "LogSyncMod.DebugGiveIngredientsDesc".Translate(),
                icon = ThingDefOf.Steel.uiIcon,
                action = () => DebugWorkbenchIngredients.Give(table, giver)
            };
            if (giver.BillStack == null || giver.BillStack.Count == 0)
                command.Disable("LogSyncMod.DebugIngredientsNoBill".Translate());
            yield return command;
        }

        private sealed class DebugIngredientsCommand : Command_Action { }
    }

    internal static class DebugWorkbenchIngredients
    {
        internal static void Give(Building table, IBillGiver giver)
        {
            if (!Prefs.DevMode || !table.Spawned || giver.BillStack == null || giver.BillStack.Count == 0) return;
            Bill bill = giver.BillStack[0]; // The visible first bill, including a suspended or already satisfied bill.
            RecipeDef recipe = bill.recipe;
            if (recipe.ingredients.NullOrEmpty())
            {
                Messages.Message("LogSyncMod.DebugIngredientsNotNeeded".Translate(recipe.LabelCap), table,
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            var requests = new List<DebugSupplyRequest>();
            var missing = new List<string>();
            foreach (IngredientCount ingredient in recipe.ingredients)
            {
                try
                {
                    if (recipe.Worker.GetIngredientCount(ingredient, bill) <= 0f) continue;
                    DebugSupplyRequest request = Resolve(ingredient, recipe, bill);
                    if (request == null) missing.Add(ingredient.filter.Summary);
                    else requests.Add(request);
                }
                catch (Exception ex)
                {
                    missing.Add(ingredient.filter.Summary);
                    Log.Warning("[LogSyncMod] Cannot resolve ingredients for " + recipe.defName + ": " + ex);
                }
            }
            if (missing.Count > 0)
            {
                Log.Warning("[LogSyncMod] No complete ingredient set for " + recipe.defName + ": " + string.Join("; ", missing));
                Messages.Message("LogSyncMod.DebugIngredientsUnavailable".Translate(recipe.LabelCap, string.Join("; ", missing)), table,
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            if (requests.Count == 0)
            {
                Messages.Message("LogSyncMod.DebugIngredientsNotNeeded".Translate(recipe.LabelCap), table,
                    MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Map map = table.Map;
            IntVec3 root = table.Position;
            float radius = bill.ingredientSearchRadius;
            string summary = string.Join(", ", requests.Select(r => r.Def.LabelCap + " ×" + r.Quantity));
            DebugSupplySpawner.Queue(map, table.InteractionCell, requests,
                "LogSyncMod.DebugIngredientsForBill".Translate(recipe.LabelCap, summary),
                c => c.InHorDistOf(root, radius));
        }

        private static DebugSupplyRequest Resolve(IngredientCount ingredient, RecipeDef recipe, Bill bill)
        {
            IEnumerable<ThingDef> candidates = ingredient.filter.AllowedThingDefs
                .Where(d => d.category == ThingCategory.Item && d.thingClass != null && !d.thingClass.IsAbstract &&
                    d.stackLimit > 0 && bill.IsFixedOrAllowedIngredient(d) &&
                    (typeof(Corpse).IsAssignableFrom(d.thingClass) || DebugThingPlaceHelper.IsDebugSpawnable(d)))
                .OrderByDescending(d => recipe.defaultIngredientFilter?.Allows(d) == true)
                .ThenBy(d => typeof(Corpse).IsAssignableFrom(d.thingClass))
                .ThenBy(d => d.defName, StringComparer.Ordinal);
            foreach (ThingDef def in candidates)
            {
                Thing probe = null;
                try
                {
                    float value = recipe.IngredientValueGetter.ValuePerUnitOf(def);
                    double count = Math.Ceiling(recipe.Worker.GetIngredientCount(ingredient, bill) / (double)value);
                    if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ||
                        double.IsNaN(count) || double.IsInfinity(count) || count <= 0 || count > int.MaxValue) continue;
                    int required = ingredient.CountRequiredOfFor(def, recipe, bill);
                    if (required <= 0) continue;
                    ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
                    if (def.MadeFromStuff && (stuff?.stuffProps == null || !stuff.stuffProps.CanMake(def))) continue;

                    PawnKindDef corpseKind = null;
                    if (typeof(Corpse).IsAssignableFrom(def.thingClass))
                    {
                        corpseKind = DefDatabase<PawnKindDef>.AllDefsListForReading
                            .Where(k => k.GetType() == typeof(PawnKindDef) && k.race?.race?.corpseDef == def)
                            .OrderBy(k => k.defName, StringComparer.Ordinal).FirstOrDefault();
                        if (corpseKind == null) continue;
                    }
                    ThingFilter[] filters = ingredient.IsFixedIngredient
                        ? new[] { ingredient.filter } : new[] { ingredient.filter, bill.ingredientFilter, recipe.fixedIngredientFilter };
                    QualityCategory minQuality = filters.Max(f => f.AllowedQualityLevels.min);
                    QualityCategory maxQuality = filters.Min(f => f.AllowedQualityLevels.max);
                    float minHp = filters.Max(f => f.AllowedHitPointsPercents.min);
                    float maxHp = filters.Min(f => f.AllowedHitPointsPercents.max);
                    if (minQuality > maxQuality || minHp > maxHp) continue;
                    QualityCategory quality = (QualityCategory)Mathf.Clamp((int)QualityCategory.Normal, (int)minQuality, (int)maxQuality);
                    Func<Thing> factory = () => MakeIngredient(def, stuff, corpseKind, quality, maxHp);
                    probe = factory();
                    if (!ingredient.filter.Allows(probe) || !bill.IsFixedOrAllowedIngredient(probe)) continue;
                    return new DebugSupplyRequest(def, stuff, required, factory);
                }
                catch (Exception ex)
                {
                    Log.Warning("[LogSyncMod] Ingredient candidate " + def.defName + " for " + recipe.defName + " was rejected: " + ex.Message);
                }
                finally
                {
                    if (probe != null && !probe.Destroyed && !probe.Spawned) probe.Destroy(DestroyMode.Vanish);
                }
            }
            return null;
        }

        private static Thing MakeIngredient(ThingDef def, ThingDef stuff, PawnKindDef corpseKind,
            QualityCategory quality, float hitPointPercent)
        {
            Thing thing;
            if (corpseKind != null)
            {
                // Never take or redress an existing world pawn to supply a corpse ingredient.
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(corpseKind,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false, allowAddictions: false, allowFood: false,
                    forceNoIdeo: true, forbidAnyTitle: true, forceNoGear: true));
                pawn.health.SetDead();
                thing = pawn.MakeCorpse(null, false, 0f);
            }
            else thing = ThingMaker.MakeThing(def, stuff);
            if (thing == null) throw new InvalidOperationException("Ingredient factory returned null for " + def.defName);
            thing.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Colony);
            if (def.useHitPoints) thing.HitPoints = Math.Max(1, Mathf.FloorToInt(thing.MaxHitPoints * hitPointPercent));
            return thing;
        }
    }
}
