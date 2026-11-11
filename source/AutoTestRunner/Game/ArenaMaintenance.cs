using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    // Owns only this fight's calendar offset and floor cleanup. Simulation ticks never change.
    internal sealed class ArenaMaintenance : IDisposable
    {
        private const int CleanupIntervalTicks = 5 * 60;
        private readonly Map map;
        private readonly int originalCalendarOffset;
        private readonly Dictionary<Thing, int> groundItemsSince = new();
        private readonly HashSet<Thing> activeJobTargets = new();
        private readonly HashSet<Pawn> observedDeaths = new();
        private int nextNoonTick = 5000;
        private int nextCleanupTick = CleanupIntervalTicks;
        private bool disposed;
        internal ArenaMaintenanceResult Result { get; }

        internal ArenaMaintenance(Map map, ArenaTournamentInput input)
        {
            this.map = map;
            originalCalendarOffset = Find.TickManager.gameStartAbsTick;
            Result = new ArenaMaintenanceResult { ClearCorpses = input.ClearCorpses, ClearDroppedItems = input.ClearDroppedItems };
            try { ResetNoon(0); }
            catch { Find.TickManager.gameStartAbsTick = originalCalendarOffset; throw; }
        }

        private void ResetNoon(int elapsedTicks)
        {
            var delta = Mathf.RoundToInt((.5f - GenLocalDate.DayPercent(map)) * 60000f);
            Find.TickManager.gameStartAbsTick += delta;
            map.skyManager.SkyManagerUpdate();
            Result.NoonResetTicks.Add(elapsedTicks);
            Result.NoonResetHours.Add(GenLocalDate.HourFloat(map));
        }

        internal static bool HasPendingRevival(Pawn pawn) => pawn != null && !pawn.Destroyed &&
            (pawn.health.hediffSet.hediffs.OfType<Hediff_DeathRefusal>().Any(h => h.InProgress || h.UsesLeft > 0)
                || GroupCombatLifecycleAdapters.PendingRevival(pawn));

        internal void NotifyDead(Pawn pawn) => observedDeaths.Add(pawn);
        internal void NotifySpawn(Pawn pawn)
        {
            if (!pawn.Dead && observedDeaths.Remove(pawn)) Result.RevivalsObserved++;
        }

        internal void Tick(int elapsedTicks)
        {
            if (elapsedTicks >= nextNoonTick)
            {
                ResetNoon(elapsedTicks);
                nextNoonTick = elapsedTicks + 5000;
            }
            if (elapsedTicks < nextCleanupTick) return;
            nextCleanupTick = elapsedTicks + CleanupIntervalTicks;
            if (!Result.ClearCorpses && !Result.ClearDroppedItems) return;
            Result.CleanupPasses++;
            Result.CleanupTicks.Add(elapsedTicks);
            // Take snapshots: destruction changes the map's thing lists.
            if (Result.ClearCorpses)
                foreach (var corpse in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>().ToList())
                {
                    if (corpse.Age < CleanupIntervalTicks) continue;
                    if (HasPendingRevival(corpse.InnerPawn)) { Result.ProtectedRevivalCorpses++; continue; }
                    var age = corpse.Age;
                    corpse.Destroy(DestroyMode.Vanish);
                    Result.MinimumClearedCorpseAgeTicks = Math.Min(Result.MinimumClearedCorpseAgeTicks ?? age, age);
                    Result.ClearedCorpses++;
                }
            if (Result.ClearDroppedItems)
            {
                activeJobTargets.Clear();
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    var job = pawn.CurJob;
                    if (job == null) continue;
                    foreach (var target in new[] { job.targetA, job.targetB, job.targetC })
                        if (target.HasThing) activeJobTargets.Add(target.Thing);
                }
                foreach (var stale in groundItemsSince.Keys.Where(t => t.Destroyed || !t.Spawned || t.Map != map).ToList()) groundItemsSince.Remove(stale);
                foreach (var item in map.listerThings.AllThings.Where(t => t.Spawned && t.Map == map
                    && t.def.category == ThingCategory.Item && t is not Corpse).ToList())
                {
                    // Let native drop/unequip callbacks and in-flight pickup jobs finish.
                    if (!groundItemsSince.TryGetValue(item, out var firstSeen)) { groundItemsSince[item] = elapsedTicks; continue; }
                    var groundTicks = elapsedTicks - firstSeen;
                    if (groundTicks < CleanupIntervalTicks || activeJobTargets.Contains(item)) continue;
                    item.Destroy(DestroyMode.Vanish);
                    Result.MinimumClearedItemGroundTicks = Math.Min(Result.MinimumClearedItemGroundTicks ?? groundTicks, groundTicks);
                    groundItemsSince.Remove(item);
                    Result.ClearedItems++;
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Find.TickManager.gameStartAbsTick = originalCalendarOffset;
            Result.CalendarRestored = Find.TickManager.gameStartAbsTick == originalCalendarOffset;
            Result.RemainingCorpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).Count;
            Result.RemainingItems = map.listerThings.AllThings.Count(t => t.Spawned && t.Map == map
                && t.def.category == ThingCategory.Item && t is not Corpse);
        }
    }

    // Keep the original map reference even when the current map becomes a preview or arena.
    internal sealed class ArenaBackgroundCleanup
    {
        private readonly Map original;
        private readonly List<Pawn> found = new();
        private int nextTick;
        internal ArenaBackgroundCleanupResult Result { get; }
        internal ArenaBackgroundCleanup(Map original)
        {
            this.original = original;
            Result = new ArenaBackgroundCleanupResult { MapId = original.uniqueID };
            Poll();
        }

        internal void Poll()
        {
            if (Find.TickManager.TicksGame < nextTick || !Find.Maps.Contains(original)) return;
            nextTick = Find.TickManager.TicksGame + 250;
            Result.Passes++;
            ThingOwnerUtility.GetAllThingsRecursively(original, ThingRequest.ForGroup(ThingRequestGroup.Pawn), found);
            foreach (var pawn in found.Where(p => !p.Destroyed && p.MapHeld == original).ToList())
            {
                if (pawn.Destroyed) continue;
                var colonist = pawn.IsColonist;
                var corpse = pawn.Corpse;
                var flyer = pawn.holdingOwner?.Owner as Skyfaller_FlyingPawn;
                if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
                if (corpse is { Spawned: true } && corpse.Map == original) corpse.Destroy(DestroyMode.Vanish);
                // A flying arrival indexes its single passenger during tick and draw.
                // Remove the arrival with its passenger so no empty flyer remains.
                if (flyer is { Destroyed: false }) flyer.Destroy(DestroyMode.Vanish);
                if (!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
                if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
                Result.RemovedPawns++;
                if (colonist) Result.RemovedColonists++;
            }
            ThingOwnerUtility.GetAllThingsRecursively(original, ThingRequest.ForGroup(ThingRequestGroup.Pawn), found);
            Result.RemainingPawns = found.Count(p => !p.Destroyed && p.MapHeld == original);
        }
    }

    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.CheckOrUpdateGameOver))]
    internal static class ArenaGameOverCheckPatch
    {
        internal static bool TestSession => ArenaService.ManualActive || global::LogSyncMod.LogSyncMod.settings is { testSessionActive: true, testSessionKind: "arena" };
        private static bool Prefix() => !TestSession;
    }

    [HarmonyPatch(typeof(GameEnder), nameof(GameEnder.GameEndTick))]
    internal static class ArenaGameOverTickPatch
    {
        private static bool Prefix() => !ArenaGameOverCheckPatch.TestSession;
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.TryDropEquipment))]
    internal static class ArenaConsumedWeaponDropPatch
    {
        private static readonly AccessTools.FieldRef<Verb, int> ShotsLeft = AccessTools.FieldRefAccess<Verb, int>("burstShotsLeft");

        private static bool Prefix(Pawn_EquipmentTracker __instance, ThingWithComps eq, ref ThingWithComps resultingEq, ref bool __result)
        {
            if (!CombatFightService.IsArena || !CombatFightService.IsCombatParticipant(__instance.pawn)
                || eq?.TryGetComp<CompEquippable>()?.PrimaryVerb is not Verb_ShootOneUse verb
                || verb.state != VerbState.Bursting || ShotsLeft(verb) >= verb.BurstShotCount) return true;

            // Native unequip consumes a partly fired one-use weapon. Do it before
            // GenSpawn removes its holder, so the drop cannot respawn the destroyed item.
            __instance.Remove(eq);
            if (!eq.Destroyed) eq.Destroy();
            resultingEq = null;
            __result = true;
            return false;
        }
    }
}
