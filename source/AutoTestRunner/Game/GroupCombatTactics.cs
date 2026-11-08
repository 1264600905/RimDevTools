using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace LogSyncMod.AutoTest
{
    // Test decisions use real verbs/jobs, so accuracy, warmup, cooldown, armor and
    // modded weapon effects still run through the game's normal combat pipeline.
    internal static class GroupCombatTactics
    {
        private static readonly HashSet<string> decisions = new HashSet<string>();
        private static readonly HashSet<Pawn> shooters = new HashSet<Pawn>();
        internal static void Reset() { decisions.Clear(); shooters.Clear(); }

        private static bool ValidTarget(Pawn pawn, Pawn target) => target != null && target != pawn
            && target.Spawned && target.Map == pawn.Map && !target.Dead && !target.Downed
            && target.HostileTo(pawn) && !target.IsPsychologicallyInvisible();

        // Inspect the weapon before asking TryGetAttackVerb: that API can fall back
        // to fists when a ranged weapon is temporarily unavailable.
        private static Verb RangedVerb(Pawn pawn)
        {
            var primary = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (primary != null && !primary.verbProps.IsMeleeAttack) return primary;
            var native = pawn.TryGetAttackVerb(null, true);
            return native != null && !native.verbProps.IsMeleeAttack ? native : null;
        }

        internal static bool KeepCurrentJob(Pawn pawn)
        {
            if (pawn.InMentalState || pawn.stances.FullBodyBusy) return true;
            var active = pawn.CurJob;
            if (active == null) return false;
            if (active.def == JobDefOf.CastAbilityOnThing || active.def == JobDefOf.CastAbilityOnWorldTile) return true;
            var target = active.targetA.Thing as Pawn;
            var ranged = RangedVerb(pawn);
            if (ranged?.state == VerbState.Bursting) return true;
            if (active.def == JobDefOf.AttackStatic)
                return ValidTarget(pawn, target) && ranged != null && ranged.Available() && ranged.CanHitTarget(target);
            if (active.def == JobDefOf.AttackMelee)
                return ValidTarget(pawn, target) && (ranged == null || pawn.Position.AdjacentTo8WayOrInside(target.Position));
            if (active.def == JobDefOf.Goto && pawn.mindState.enemyTarget is Pawn enemy && ValidTarget(pawn, enemy))
                return Find.TickManager.TicksGame - active.startTick < 120 && (ranged == null || !ranged.Available() || !ranged.CanHitTarget(enemy));
            return false;
        }

        internal static Job CreateJob(Pawn pawn)
        {
            if (!CombatFightService.UsesTestCombatAi(pawn) || pawn.Dead || pawn.Downed || pawn.InMentalState) return null;
            var ranged = RangedVerb(pawn);
            var available = ranged?.Available() == true;
            var targets = CombatFightService.Pawns.Values.Where(p => ValidTarget(pawn, p))
                .OrderBy(p => pawn.Position.DistanceToSquared(p.Position)).ThenBy(p => CombatFightService.RosterId(p), StringComparer.Ordinal).ToList();
            // Keep a shootable target; otherwise prefer another target that can be
            // fired at now, rather than crossing the arena to chase someone else.
            var old = pawn.mindState.enemyTarget as Pawn;
            var target = ValidTarget(pawn, old) && available && ranged.CanHitTarget(old) ? old
                : available ? targets.FirstOrDefault(p => ranged.CanHitTarget(p)) : null;
            target ??= targets.FirstOrDefault(p => pawn.CanReach(p, PathEndMode.Touch, Danger.Deadly));
            if (target == null) return Wait(pawn, null, ranged, "no-reachable-target");
            pawn.mindState.enemyTarget = target;

            var ability = JobGiver_AIFightEnemy.GetAbilityJob(pawn, target);
            if (ability != null) return Finish(pawn, target, ranged, ability, "ability");
            var adjacent = pawn.Position.AdjacentTo8WayOrInside(target.Position);
            if (ranged == null || adjacent && (!available || !ranged.CanHitTarget(target)))
            {
                if (!pawn.kindDef.canMeleeAttack || pawn.meleeVerbs.TryGetMeleeVerb(target) == null)
                    return Wait(pawn, target, ranged, "no-melee-verb");
                var melee = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                melee.expiryInterval = 600;
                return Finish(pawn, target, ranged, melee, ranged == null ? "melee" : "melee-contact");
            }
            if (!available) return Wait(pawn, target, ranged, "ranged-unavailable");
            if (ranged.CanHitTarget(target))
            {
                var shoot = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                shoot.verbToUse = ranged;
                shoot.maxNumStaticAttacks = int.MaxValue;
                shoot.expiryInterval = int.MaxValue;
                shoot.endIfCantShootTargetFromCurPos = true;
                shoot.endIfCantShootInMelee = true;
                return Finish(pawn, target, ranged, shoot, "shoot");
            }
            // Do not substitute a melee charge if no firing position exists.
            // The buffer also keeps a search for cover from choosing contact range.
            var buffer = Math.Min(6f, ranged.EffectiveRange * .5f);
            if (CastPositionFinder.TryFindCastPosition(new CastPositionRequest {
                caster = pawn, target = target, verb = ranged, maxRangeFromTarget = ranged.EffectiveRange,
                wantCoverFromTarget = true,
                validator = cell => cell.DistanceToSquared(target.Position) >= buffer * buffer
            }, out var position) && position != pawn.Position)
            {
                var move = JobMaker.MakeJob(JobDefOf.Goto, position);
                move.expiryInterval = 120;
                return Finish(pawn, target, ranged, move, "move-to-shoot");
            }
            return Wait(pawn, target, ranged, "no-firing-position");
        }

        private static Job Wait(Pawn pawn, Pawn target, Verb verb, string reason)
        {
            // Wait rather than Wait_Combat: the latter runs autonomous target/verb
            // selection, which would introduce a second combat controller.
            var job = JobMaker.MakeJob(JobDefOf.Wait, 30);
            return Finish(pawn, target, verb, job, reason);
        }

        private static Job Finish(Pawn pawn, Pawn target, Verb verb, Job job, string decision)
        {
            job.checkOverrideOnExpire = false;
            var id = CombatFightService.RosterId(pawn);
            var targetId = CombatFightService.RosterId(target);
            // Bound diagnostics while retaining every pawn's first decision of each kind.
            if (decisions.Count < 2000 && decisions.Add(id + "/" + decision + "/" + targetId))
                CombatFightService.TraceAi("group.ai_decision", Details(pawn, target, verb)
                    + "; decision=" + decision + "; job=" + job.def.defName + "; destination=" + job.targetA.Cell);
            return job;
        }

        private static string Details(Pawn pawn, Pawn target, Verb verb) => "pawn=" + CombatFightService.RosterId(pawn)
            + "; target=" + CombatFightService.RosterId(target) + "; tick=" + CombatFightService.Display.ElapsedTicks
            + "; distance=" + (target == null ? "none" : pawn.Position.DistanceTo(target.Position).ToString("F2", CultureInfo.InvariantCulture))
            + "; weapon=" + pawn.equipment?.Primary?.def.defName + "; verb=" + verb?.GetType().Name
            + "; range=" + verb?.EffectiveRange.ToString("F2", CultureInfo.InvariantCulture);

        internal static void NotifyShot(Verb verb)
        {
            var pawn = verb.CasterPawn;
            if (!CombatFightService.UsesTestCombatAi(pawn) || !shooters.Add(pawn)) return;
            CombatFightService.TraceAi("group.ai_shot", Details(pawn, verb.CurrentTarget.Thing as Pawn, verb) + "; launched=true");
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    internal static class GroupCombat_Shot_Patch
    {
        private static void Postfix(Verb_LaunchProjectile __instance, bool __result)
        {
            if (__result) GroupCombatTactics.NotifyShot(__instance);
        }
    }
}
