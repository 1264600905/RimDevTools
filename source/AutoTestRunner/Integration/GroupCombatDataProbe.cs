using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    // Isolated native transport verification. The synthetic worker tests invocation accounting;
    // it is never registered as a playable Def and never used by a benchmark XML rule.
    public sealed class GroupCombatProbeDamageWorker : DamageWorker
    {
        internal static Func<DamageInfo, Thing, DamageResult> Handler;
        public override DamageResult Apply(DamageInfo info, Thing victim) => Handler?.Invoke(info, victim) ?? new DamageResult { totalDamageDealt = info.Amount };
    }

    internal static class GroupCombatDataProbe
    {
        internal static object LastResult;
        internal static object RunUniqueMap()
        {
            if (global::LogSyncMod.LogSyncMod.settings is not { testSessionActive: true, testSessionKind: "manual" } || ManualTestService.Busy || GroupCombatService.Busy)
                throw new InvalidOperationException("Unique-map verification requires an idle CLI manual test process");
            var source = Find.CurrentMap;
            if (source == null || Find.Maps.Count != 1) throw new InvalidOperationException("Unique-map verification requires exactly one command-owned quicktest source map");
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            var arena = new AutoTestMainMap(source, 170412, new IntVec3(50, 1, 50));
            var arenaId = arena.Map.uniqueID;
            // This endpoint is restricted to a disposable command session. Removing its
            // generated quicktest map exercises the actual no-safe-source disposal branch.
            Current.Game.DeinitAndRemoveMap(source, false);
            arena.Dispose();
            var checks = new { noMaps = Find.Maps.Count == 0, noCurrentMap = Find.CurrentMap == null, noArenaParent = !Find.WorldObjects.AllWorldObjects.Contains(arena.Parent), arenaDestroyed = !Find.Maps.Any(m => m.uniqueID == arenaId) };
            return new { passed = checks.noMaps && checks.noCurrentMap && checks.noArenaParent && checks.arenaDestroyed, checks, nativeSessionExitRequested = true, artifacts = (object)null };
        }
        internal static object Run()
        {
            if (global::LogSyncMod.LogSyncMod.settings is not { testSessionActive: true, testSessionKind: "manual" } || ManualTestService.Busy || GroupCombatService.Busy) throw new InvalidOperationException("Native group data verification requires an idle CLI manual test process");
            var source = Find.CurrentMap; var originalSpeed = Find.TickManager.CurTimeSpeed;
            var originalMaps = Find.Maps.Select(m => m.uniqueID).OrderBy(id => id).ToList();
            var owned = new List<Thing>(); var checks = new Dictionary<string, bool>(); var facts = new Dictionary<string, object>();
            AutoTestMainMap arena = null;
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                arena = new AutoTestMainMap(source, 170411, new IntVec3(50, 1, 50));
                var template = TemporaryPawnData.Create(XElement.Parse("<PawnTemplate><basePawnKind>Colonist</basePawnKind><minGenerationAge>25</minGenerationAge><maxGenerationAge>25</maxGenerationAge><Apparel mode='replace'/><Equipment mode='replace'/><Hediffs mode='replace'/></PawnTemplate>"), "controlled-native-group-probe", null, null);
                var enemyFaction = Faction.OfAncientsHostile;
                Pawn Make(Faction faction, int x)
                {
                    var pawn = ScenarioGameBackend.GenerateTemporaryPawn(template, faction, (_, __) => { }, out var things); owned.AddRange(things);
                    GenSpawn.Spawn(pawn, new IntVec3(x, 0, 25), arena.Map); return pawn;
                }
                var a0 = Make(Faction.OfPlayer, 20); var a1 = Make(Faction.OfPlayer, 21); var b0 = Make(enemyFaction, 28);
                var ids = new Dictionary<Thing, string> { [a0] = "a0", [a1] = "a1", [b0] = "b0" };
                var ledger = new GroupCombatLedger(ids.Select(p => new GroupCombatUnit { Id = p.Value, Team = p.Value.StartsWith("a") ? "A" : "B" }), new[] { new KeyValuePair<string, string>("A", "A"), new KeyValuePair<string, string>("B", "B") }, detailLimit: 4);
                using (var scope = new GroupCombatDamageCapture.ProbeContext(arena.Map, ledger, ids, new Dictionary<Faction, string> { [Faction.OfPlayer] = "A", [enemyFaction] = "B" }))
                {
                    var basic = new DamageDef { defName = "RimDevTools_Probe_Settled", workerClass = typeof(GroupCombatProbeDamageWorker), harmsHealth = false };
                    var forward = new DamageDef { defName = "RimDevTools_Probe_Forward", workerClass = typeof(GroupCombatProbeDamageWorker), harmsHealth = false };
                    var reflected = new DamageDef { defName = "RimDevTools_Probe_Reflect", workerClass = typeof(GroupCombatProbeDamageWorker), harmsHealth = false };
                    DamageInfo Info(DamageDef def, float amount, Thing instigator) => new DamageInfo(def, amount, instigator: instigator);
                    var tick = Find.TickManager.TicksGame;
                    b0.TakeDamage(Info(basic, 10, a0)); b0.TakeDamage(Info(basic, 10, a0));
                    var state = ledger.Snapshot(60);
                    checks["same_tick_equal_hits"] = Find.TickManager.TicksGame == tick && state.TotalEvents == 2 && state.Teams[0].EnemyDamage == 20 && state.Events.Select(e => e.Id).Distinct().Count() == 2;
                    GroupCombatProbeDamageWorker.Handler = (info, victim) => info.Def == forward ? victim.TakeDamage(Info(basic, 7, a0)) : info.Def == reflected ? Reflect(victim) : new DamageWorker.DamageResult { totalDamageDealt = info.Amount };
                    DamageWorker.DamageResult Reflect(Thing victim) { a0.TakeDamage(Info(basic, 2, b0)); return new DamageWorker.DamageResult { totalDamageDealt = 5 }; }
                    b0.TakeDamage(Info(forward, 7, a0)); state = ledger.Snapshot(60);
                    checks["forwarded_result_once"] = state.TotalEvents == 3 && state.Teams[0].EnemyDamage == 27;
                    b0.TakeDamage(Info(reflected, 5, a0)); state = ledger.Snapshot(60);
                    checks["nested_reflection_attribution"] = state.TotalEvents == 5 && state.Teams[0].EnemyDamage == 32 && state.Teams[1].EnemyDamage == 2;
                    a1.TakeDamage(Info(basic, 3, a0)); a0.TakeDamage(Info(basic, 4, a0)); b0.TakeDamage(Info(basic, 6, null));
                    var outside = ThingMaker.MakeThing(ThingDefOf.Steel); owned.Add(outside); b0.TakeDamage(Info(basic, 8, outside)); state = ledger.Snapshot(60);
                    checks["friend_self_environment_unknown"] = state.Teams[0].FriendlyDamage == 3 && state.Teams[0].SelfDamage == 4 && state.Matrix.Single(m => m.Source == "environment").Amount == 6 && state.Matrix.Single(m => m.Source == "unknown").Amount == 8 && !state.MeasurementRequirementsMet;
                    var belt = (Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_ShieldBelt")); owned.Add(belt); b0.apparel.Wear(belt, false);
                    var shield = belt.TryGetComp<CompShield>();
                    for (int i = 0; i < 20000 && shield.Energy < .6f; i++) shield.CompTick();
                    var energy = shield.Energy; var injuries = b0.health.hediffSet.hediffs.Count;
                    var result = b0.TakeDamage(Info(DamageDefOf.Bullet, 5, a0)); state = ledger.Snapshot(60);
                    checks["native_shield_separate_from_dps"] = result.totalDamageDealt == 0 && shield.Energy < energy && b0.health.hediffSet.hediffs.Count == injuries && state.Teams[0].EnemyDamage == 32 && state.Matrix.Single(m => m.Channel == "shield").Amount == 5;
                    var reusedResult = new DamageWorker.DamageResult { totalDamageDealt = 4 };
                    GroupCombatProbeDamageWorker.Handler = (_, __) => reusedResult;
                    b0.TakeDamage(Info(basic, 4, a0)); b0.TakeDamage(Info(basic, 4, a0));
                    var reused = ledger.Snapshot(60);
                    checks["separate_calls_reusing_result_object"] = reused.TotalEvents == state.TotalEvents + 2 && reused.Teams[0].EnemyDamage == 40;
                    GroupCombatProbeDamageWorker.Handler = null;
                    state = reused;
                    var before = state.TotalEvents;
                    for (int i = 0; i < 20000; i++) b0.TakeDamage(Info(basic, 1, a0));
                    state = ledger.Snapshot(120);
                    checks["native_detail_overflow_exact"] = state.TotalEvents == before + 20000 && state.Teams[0].EnemyDamage == 20040 && state.Events.Count == 4 && state.DroppedDetailEvents == state.TotalEvents - 4 && state.Teams[0].Damage.Count == 2;
                    checks["common_window_pause_speed"] = Find.TickManager.TicksGame == tick && state.Teams[0].Dps == 10020 && ledger.Snapshot(120).Teams[0].Dps == state.Teams[0].Dps;
                    facts["totalEvents"] = state.TotalEvents; facts["retainedEvents"] = state.Events.Count; facts["enemyDamageA"] = state.Teams[0].EnemyDamage; facts["enemyDamageB"] = state.Teams[1].EnemyDamage; facts["syntheticWorker"] = "Transport/settled-result accounting only; no claim of real injury loss";
                }
            }
            finally
            {
                GroupCombatProbeDamageWorker.Handler = null;
                foreach (var thing in owned) { if (thing is Pawn p && Find.WorldPawns.Contains(p)) Find.WorldPawns.RemovePawn(p); if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish); }
                arena?.Dispose();
                foreach (var pawn in owned.OfType<Pawn>()) if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
                Find.TickManager.CurTimeSpeed = originalSpeed;
            }
            checks["owned_cleanup_source_restored"] = Find.CurrentMap == source && originalMaps.SequenceEqual(Find.Maps.Select(m => m.uniqueID).OrderBy(id => id)) && owned.All(t => t.Destroyed) && owned.OfType<Pawn>().All(p => !Find.WorldPawns.Contains(p));
            LastResult = new { passed = checks.Values.All(v => v), checks, facts, artifacts = (object)null }; return LastResult;
        }
    }
}
