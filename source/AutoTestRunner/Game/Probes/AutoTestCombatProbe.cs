using System;
using System.Linq;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class AutoTestCombatProbe
    {
        public static AutoTestCombatSnapshot Capture(Thing attacker, Thing target, Map map)
        {
            AutoTestCombatSnapshot snapshot = new AutoTestCombatSnapshot
            {
                AttackerId = AutoTestThingProbe.Target(attacker)?.Id,
                TargetId = AutoTestThingProbe.Target(target)?.Id,
                Tick = Find.TickManager?.TicksGame ?? -1,
                Supported = attacker != null && target != null && map != null
            };
            if (!snapshot.Supported)
            {
                snapshot.UnsupportedReason = "attacker, target, or map missing";
                snapshot.Summary = "supported=false reason=" + snapshot.UnsupportedReason;
                return snapshot;
            }
            Pawn pawnTarget = target as Pawn;
            snapshot.TargetHealthPercent = pawnTarget?.health?.summaryHealth?.SummaryHealthPercent ?? (target.def?.useHitPoints == true && target.MaxHitPoints > 0 ? (float)target.HitPoints / target.MaxHitPoints : 1f);
            snapshot.TargetHitPoints = target.def?.useHitPoints == true ? target.HitPoints : 0;
            snapshot.TargetDowned = pawnTarget?.Downed ?? false;
            snapshot.TargetDead = pawnTarget?.Dead ?? target.Destroyed;
            Pawn pawnAttacker = attacker as Pawn;
            if (pawnAttacker != null)
            {
                snapshot.CurrentJobDefName = pawnAttacker.CurJobDef?.defName ?? string.Empty;
                snapshot.CurrentTargetId = AutoTestThingProbe.Target(pawnAttacker.CurJob?.targetA.Thing)?.Id;
            }
            else
            {
                object currentTarget = AutoTestPawnProbe.ReadRawMember(attacker, "currentTargetInt", "currentTarget", "CurrentTarget");
                LocalTargetInfo? localTarget = currentTarget is LocalTargetInfo value ? value : (LocalTargetInfo?)null;
                snapshot.CurrentTargetId = AutoTestThingProbe.Target(localTarget?.Thing)?.Id;
            }
            if (map.listerThings?.ThingsInGroup(ThingRequestGroup.Projectile) != null)
            {
                var projectiles = map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile)
                    .Where(item => item != null && !item.Destroyed && item.Spawned && item.Map == map).ToList();
                snapshot.ActiveProjectileCount = projectiles.Count;
                snapshot.ActiveProjectileDefNames.AddRange(projectiles.Select(item => item.def?.defName ?? item.GetType().Name));
            }
            snapshot.Summary = "tick=" + snapshot.Tick +
                " attacker=" + snapshot.AttackerId +
                " target=" + snapshot.TargetId +
                " currentTarget=" + snapshot.CurrentTargetId +
                " job=" + snapshot.CurrentJobDefName +
                " hp=" + snapshot.TargetHitPoints +
                " health=" + snapshot.TargetHealthPercent.ToString("0.000") +
                " downed=" + snapshot.TargetDowned +
                " dead=" + snapshot.TargetDead +
                " projectiles=" + snapshot.ActiveProjectileCount;
            return snapshot;
        }
    }
}
