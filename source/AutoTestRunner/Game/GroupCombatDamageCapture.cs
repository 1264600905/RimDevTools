using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class GroupCombatDamageCapture
    {
        internal sealed class ProbeContext : IDisposable
        {
            internal Map Map;
            internal GroupCombatLedger Ledger;
            internal Dictionary<Thing, string> Ids;
            internal Dictionary<Faction, string> Teams;
            internal ProbeContext(Map map, GroupCombatLedger ledger, Dictionary<Thing, string> ids, Dictionary<Faction, string> teams)
            {
                if (probe != null || GroupCombatService.Busy) throw new InvalidOperationException("Another group combat measurement is active");
                Map = map; Ledger = ledger; Ids = ids; Teams = teams; Clear(); probe = this;
            }
            public void Dispose() { if (probe == this) probe = null; Clear(); }
        }
        private static ProbeContext probe;
        private static GroupCombatLedger ActiveLedger => probe?.Ledger ?? GroupCombatService.Ledger;
        private static string Id(Thing thing) => thing != null && probe?.Ids.TryGetValue(thing, out var value) == true ? value : GroupCombatService.RosterId(thing);
        private static string Team(Faction faction) => faction != null && probe?.Teams.TryGetValue(faction, out var value) == true ? value : GroupCombatService.Team(faction);
        internal sealed class Invocation
        {
            internal GroupCombatLedger Ledger;
            internal GroupCombatDamageEvent Event;
            internal readonly List<DamageWorker.DamageResult> ChildResults = new List<DamageWorker.DamageResult>();
            internal bool Completed;
        }
        [ThreadStatic] private static Stack<Invocation> stack;
        internal static void Clear() { stack?.Clear(); }
        internal static Invocation Begin(Thing victim, DamageInfo info)
        {
            var ledger = ActiveLedger;
            if (ledger == null || victim.MapHeld != (probe?.Map ?? GroupCombatService.Map) && Id(victim) == null) return null;
            stack ??= new Stack<Invocation>();
            var call = new Invocation { Ledger = ledger, Event = new GroupCombatDamageEvent {
                Id = ledger.NextEventId(), ParentId = stack.Count == 0 ? null : stack.Peek().Event.Id, Tick = Find.TickManager.TicksGame,
                SourceId = Id(info.Instigator) ?? info.Instigator?.GetUniqueLoadID(), VictimId = Id(victim) ?? victim.GetUniqueLoadID(),
                SourceTeam = Team(info.Instigator?.Faction), VictimTeam = Team(victim.Faction),
                DamageDef = info.Def?.defName, WeaponDef = info.Weapon?.defName, RawAmount = Math.Max(0, info.Amount),
                Channel = victim is Pawn ? "pawn" : victim is Apparel ? "armor" : victim is Building ? "building" : "other"
            } };
            stack.Push(call); return call;
        }
        internal static void Complete(Invocation call, DamageWorker.DamageResult result)
        {
            if (call == null || result == null) return;
            call.Completed = true;
            if (call.ChildResults.Any(child => ReferenceEquals(child, result)))
            {
                GroupCombatService.DamageDiagnostic("damage.forwarded_result: a nested invocation returned the same DamageResult; recorded only once");
                return;
            }
            // A mod can wrap a child result in an aggregate result. Exact shared injury objects
            // establish overlap; equal tick/amount alone never does. Ambiguous wrappers remain
            // explicitly incomplete instead of pretending a heuristic is exact attribution.
            var overlaps = call.ChildResults.Where(child => child.hediffs != null && result.hediffs != null && child.hediffs.Any(h => result.hediffs.Contains(h))).ToList();
            if (overlaps.Count > 0)
            {
                GroupCombatService.DamageDiagnostic("damage.aggregate_overlap: nested aggregate shares injury objects; parent omitted, measurement incomplete for this handler");
                return;
            }
            call.Event.Amount = result.totalDamageDealt;
            call.Ledger.Record(call.Event);
            if (stack != null) foreach (var parent in stack.Where(p => p != call)) parent.ChildResults.Add(result);
        }
        internal static void End(Invocation call, Exception error)
        {
            if (call == null) return;
            if (stack?.Count > 0 && stack.Peek() == call) stack.Pop();
            else { stack?.Clear(); GroupCombatService.DamageDiagnostic("damage.stack_mismatch: nested native damage invocations were not balanced"); }
            if (error != null) GroupCombatService.DamageDiagnostic("damage.exception: native TakeDamage did not return a settled result: " + error.Message);
        }
        internal static void NativeShieldAbsorbed(DamageInfo info, bool absorbed)
        {
            if (!absorbed || stack == null || stack.Count == 0 || ActiveLedger == null) return;
            var invocation = stack.Peek(); var hit = invocation.Event; hit.Absorbed = true;
            invocation.Ledger.Record(new GroupCombatDamageEvent {
                Id = invocation.Ledger.NextEventId(), ParentId = hit.Id, Tick = hit.Tick, SourceId = hit.SourceId, VictimId = hit.VictimId,
                SourceTeam = hit.SourceTeam, VictimTeam = hit.VictimTeam, DamageDef = hit.DamageDef, WeaponDef = hit.WeaponDef,
                Channel = "shield", Amount = Math.Max(0, info.Amount), RawAmount = Math.Max(0, info.Amount), Absorbed = true,
                Note = "Input fully absorbed by the native CompShield.PostPreApplyDamage; separate from settled Pawn damage and main DPS"
            });
        }
    }

    [HarmonyPatch(typeof(CompShield), nameof(CompShield.PostPreApplyDamage))]
    internal static class GroupCombat_ShieldAbsorption_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(DamageInfo dinfo, bool absorbed) => GroupCombatDamageCapture.NativeShieldAbsorbed(dinfo, absorbed);
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class GroupCombat_TakeDamage_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Thing __instance, DamageInfo dinfo, out GroupCombatDamageCapture.Invocation __state) => __state = GroupCombatDamageCapture.Begin(__instance, dinfo);
        [HarmonyPostfix]
        private static void Postfix(DamageWorker.DamageResult __result, GroupCombatDamageCapture.Invocation __state) => GroupCombatDamageCapture.Complete(__state, __result);
        [HarmonyFinalizer]
        private static void Finalizer(Exception __exception, GroupCombatDamageCapture.Invocation __state) => GroupCombatDamageCapture.End(__state, __exception);
    }

    [HarmonyPatch(typeof(Pawn), "ExitMap")]
    internal static class GroupCombat_ExitMap_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(Pawn __instance) => GroupCombatService.NotifyExit(__instance);
    }
}
