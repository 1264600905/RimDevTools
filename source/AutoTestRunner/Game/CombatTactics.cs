using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace LogSyncMod.AutoTest
{
    // Squad strategy only. No ThinkTree/ThinkNode replacements or global job patches.
    // A native Defend duty remains the fallback for species-specific combat/abilities.
    internal sealed class LordJob_CombatTactics : LordJob
    {
        public override bool AddFleeToil => false;
        // Native Lord extension points: keep an issued squad order without
        // replacing or patching any pawn ThinkTree/ThinkNode.
        public override ThinkResult Notify_DutyResult(ThinkResult result, Pawn pawn, JobIssueParams issueParams)
            => CombatFightService.KeepTacticalOrder(result, pawn);
        public override ThinkResult Notify_DutyConstantResult(ThinkResult result, Pawn pawn, JobIssueParams issueParams)
            => CombatFightService.KeepTacticalOrder(result, pawn);
        public override StateGraph CreateGraph()
        {
            var graph = new StateGraph(); graph.AddToil(new LordToil_CombatTactics()); return graph;
        }
    }

    internal sealed class LordToil_CombatTactics : LordToil
    {
        public override bool AllowSatisfyLongNeeds => false;
        public override bool AllowRestingInBed => false;
        public override bool AllowSelfTend => false;
        public override void UpdateAllDuties()
        {
            foreach (var pawn in lord.ownedPawns)
                if (pawn.mindState != null)
                    pawn.mindState.duty = new PawnDuty(DutyDefOf.Defend, pawn.Position, 8) { wanderRadius = 0 };
        }
    }

    internal sealed class CombatTactics
    {
        private sealed class Unit
        {
            internal Pawn Pawn;
            internal Team Team;
            internal Verb Ranged;
            internal bool Melee, Bait, Hidden;
            internal int RoleUntil, NextPlan, LastMoveTick, LastProgressTick, StuckUntil, NextFlank, JobId = -1;
            internal bool AwaitingArrival, Evading;
            internal IntVec3 Destination = IntVec3.Invalid, LastPosition = IntVec3.Invalid, FailedCell = IntVec3.Invalid;
            internal Pawn Target;
            internal Job Job;
            internal float Range => Ranged?.EffectiveRange ?? 0;
        }
        private sealed class Team
        {
            internal string Id, Policy;
            internal Faction Faction;
            internal IntVec3 Anchor;
            internal float Radius;
            internal readonly List<Unit> Members = new(), Enemies = new();
            internal Vector3 Rear;
            internal int Shooters, MeleeEnemies, RangedEnemies, LastCombatTick;
        }
        private struct Candidate { internal IntVec3 Cell; internal float Score; }
        private readonly Map map;
        private readonly Dictionary<Faction, Team> teams = new();
        private readonly Dictionary<Pawn, Unit> units = new();
        private readonly Dictionary<IntVec3, Unit> destinations = new();
        private readonly List<Unit> controlled = new(), threats = new(4), supporters = new(4), nearbyMelee = new(4);
        private readonly List<IntVec3> staleDestinations = new(32);
        private readonly List<Candidate> candidates = new(CombatTacticsPolicy.CandidateLimit);
        private readonly CombatTacticsBudget budget = new();
        private int cursor, nextRefresh, tick, traces;
        private long batches, decisions, moves, reachChecks, losChecks, holds, baitAssignments, baitMoves, retreatMoves, supportedEvades, orderedRangedMelee, kites, coverPositions, blockedCells, shots, coveredShots, arrivals, keptOrders;
        private double totalMilliseconds, maxMilliseconds;
        private int peakReach, peakMoves;

        internal CombatTactics(Map map) { this.map = map; }
        internal void Add(string id, string policy, Faction faction, IntVec3 anchor, float radius)
            => teams.Add(faction, new Team { Id = id, Policy = policy, Faction = faction, Anchor = anchor, Radius = radius });
        internal object Status => new { batches, decisions, moves, reachChecks, losChecks, holds, baitAssignments, baitMoves, retreatMoves, supportedEvades, orderedRangedMelee, kites, coverPositions, blockedCells, shots, coveredShots, arrivals, keptOrders,
            peakReach, peakMoves, averageBatchMs = batches == 0 ? 0 : totalMilliseconds / batches, maxBatchMs = maxMilliseconds,
            controlled = controlled.Count, reserved = destinations.Count,
            teams = teams.Values.Select(t => new { t.Id, t.Policy, t.Shooters, t.MeleeEnemies, t.RangedEnemies,
                baits = t.Members.Count(u => u.Bait), melee = t.Members.Count(u => u.Ranged == null && u.Melee) }) };

        private bool Active(Pawn pawn) => pawn != null && pawn.Spawned && pawn.Map == map && !pawn.Dead && !pawn.Downed && !pawn.Destroyed;
        // Jobs are pooled: reference equality alone can mistake a recycled native job for our order.
        private static bool Owns(Unit u) => u.Job != null && u.Pawn.CurJob == u.Job && u.Job.loadID == u.JobId;
        internal ThinkResult KeepOrder(ThinkResult result, Pawn pawn)
        {
            if (!result.IsValid || result.FromQueue || !CombatFightService.IsCombatParticipant(pawn)
                || !units.TryGetValue(pawn, out var u) || !Owns(u) || pawn.InMentalState || pawn.IsBurning()
                || result.Job == pawn.CurJob || result.Job.ability != null) return result;
            var def = result.Job.def;
            if (def != JobDefOf.Goto && def != JobDefOf.Wait_Combat && def != JobDefOf.Wait_Wander
                && def != JobDefOf.AttackStatic && def != JobDefOf.AttackMelee) return result;
            if (!u.Destination.IsValid || !Free(u, u.Destination)
                || u.Target != null && !Active(u.Target)) return result;
            JobMaker.ReturnToPool(result.Job);
            keptOrders++;
            return new ThinkResult(pawn.CurJob, result.SourceNode, result.Tag);
        }
        private static bool Ranged(Verb verb) => verb?.verbProps != null && !verb.verbProps.IsMeleeAttack && verb.EffectiveRange > 1.5f;
        private static Verb RangedVerb(Pawn pawn)
        {
            var primary = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (Ranged(primary)) return primary; // Cooldown is not a change of combat role.
            var current = pawn.CurrentEffectiveVerb;
            if (Ranged(current)) return current;
            if (pawn.VerbTracker != null)
                foreach (var verb in pawn.VerbTracker.AllVerbs)
                    if (Ranged(verb) && !verb.verbProps.onlyManualCast) return verb;
            return null;
        }

        private void Refresh()
        {
            controlled.Clear();
            foreach (var team in teams.Values) { team.Members.Clear(); team.Enemies.Clear(); team.Shooters = 0; team.MeleeEnemies = 0; team.RangedEnemies = 0; }
            foreach (var pawn in CombatFightService.Pawns.Values)
            {
                if (!Active(pawn)) continue;
                if (!units.TryGetValue(pawn, out var unit)) units.Add(pawn, unit = new Unit { Pawn = pawn });
                unit.Ranged = RangedVerb(pawn);
                unit.Melee = pawn.kindDef.canMeleeAttack && !pawn.WorkTagIsDisabled(WorkTags.Violent);
                unit.Hidden = pawn.IsPsychologicallyInvisible();
                unit.Team = pawn.Faction != null && teams.TryGetValue(pawn.Faction, out var own) ? own : null;
                foreach (var team in teams.Values)
                {
                    if (team == unit.Team) { team.Members.Add(unit); if (unit.Ranged != null) team.Shooters++; }
                    // The fight validates two mutually hostile roster teams before
                    // starting. Use that ownership, rather than guest/host faction
                    // links captured by a native generated pawn in an earlier round.
                    else if (unit.Team != null)
                    { team.Enemies.Add(unit); if (unit.Ranged != null) team.RangedEnemies++; else if (unit.Melee) team.MeleeEnemies++; }
                }
                if (unit.Team != null && CombatTacticsPolicy.Custom(unit.Team.Policy)) controlled.Add(unit);
            }
            // Release invalid/job-replaced leases. No test map/pawn references survive a round.
            staleDestinations.Clear();
            foreach (var pair in destinations)
                if (!Active(pair.Value.Pawn) || !Owns(pair.Value) || pair.Value.Destination != pair.Key)
                    staleDestinations.Add(pair.Key);
            foreach (var cell in staleDestinations) destinations.Remove(cell);
            foreach (var team in teams.Values)
            {
                if (!CombatTacticsPolicy.Custom(team.Policy)) continue;
                var rear = Vector3.zero; var rearCount = 0; var melee = 0;
                foreach (var u in team.Members)
                {
                    if (u.Ranged == null && u.Melee) melee++;
                    if (u.Ranged != null) { rear += u.Pawn.Position.ToVector3(); rearCount++; }
                    if (u.Ranged == null) u.Bait = false;
                }
                team.Rear = rearCount > 0 ? rear / rearCount : team.Anchor.ToVector3();
                var approaching = 0;
                foreach (var enemy in team.Enemies)
                    if (enemy.Melee && enemy.Ranged == null && (enemy.Pawn.Position.ToVector3() - team.Rear).sqrMagnitude < 24 * 24) approaching++;
                var demand = team.Policy == "lure" ? CombatTacticsPolicy.BaitDemand(team.Shooters, melee, approaching) : 0;
                // Stable leases prevent different shooters volunteering every refresh.
                var selected = 0;
                foreach (var u in team.Members)
                {
                    if (!u.Bait) continue;
                    // Release excess volunteers after casualties or recovery, even
                    // during a lease. The last shooter must retain its ranged role.
                    if (u.RoleUntil > tick && selected < demand) selected++;
                    else { u.Bait = false; u.NextPlan = tick; }
                }
                while (selected < demand)
                {
                    Unit best = null; float score = float.MinValue;
                    foreach (var u in team.Members)
                    {
                        if (u.Ranged == null || u.Bait) continue;
                        float value = u.Pawn.health.summaryHealth.SummaryHealthPercent * 8 - u.Range * .1f
                            + u.Pawn.GetStatValue(StatDefOf.MoveSpeed) * 1.5f - u.Ranged.WarmupTime * 2;
                        if (value > score) { score = value; best = u; }
                    }
                    if (best == null) break;
                    best.Bait = true; best.RoleUntil = tick + CombatTacticsPolicy.RoleLockTicks; baitAssignments++; selected++;
                    Trace(best, "bait-role", best.Pawn.Position);
                }
            }
            nextRefresh = tick + 30;
        }

        internal void Tick(int elapsedTicks)
        {
            tick = elapsedTicks;
            if (tick % CombatTacticsPolicy.BatchTicks != 0) return;
            var started = Stopwatch.GetTimestamp();
            budget.Reset();
            if (tick >= nextRefresh) Refresh();
            var count = Math.Min(CombatTacticsPolicy.DecisionsPerBatch, controlled.Count);
            for (var i = 0; i < count; i++)
            {
                if ((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency >= CombatTacticsPolicy.BatchBudgetMilliseconds) break;
                if (cursor >= controlled.Count) cursor = 0;
                var u = controlled[cursor++];
                if (!Active(u.Pawn) || u.Pawn.InMentalState || u.Pawn.IsBurning()) { Release(u); continue; }
                if (tick < u.NextPlan) continue;
                Plan(u); decisions++;
            }
            reachChecks += budget.ReachChecks; peakReach = Math.Max(peakReach, budget.ReachChecks); peakMoves = Math.Max(peakMoves, budget.Moves);
            var ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            totalMilliseconds += ms; maxMilliseconds = Math.Max(maxMilliseconds, ms); batches++;
            if (tick % 600 == 0) Report();
        }

        internal void Report() => CombatFightService.TraceAi("group.ai_tactical_summary", System.Text.Json.JsonSerializer.Serialize(Status));
        internal void NotifyShot(Verb verb)
        {
            var pawn = verb.CasterPawn;
            if (pawn == null || !units.TryGetValue(pawn, out var u) || u.Team == null || !CombatTacticsPolicy.Custom(u.Team.Policy)) return;
            shots++; u.Team.LastCombatTick = tick;
            if (verb.CurrentTarget.IsValid && CoverUtility.CalculateOverallBlockChance(pawn, verb.CurrentTarget.Cell, map) > .05f) coveredShots++;
        }
        private void Trace(Unit u, string action, IntVec3 cell)
        {
            if (traces++ < 120)
                CombatFightService.TraceAi("group.ai_tactical", "pawn=" + CombatFightService.RosterId(u.Pawn) + "; team=" + u.Team.Id
                    + "; policy=" + u.Team.Policy + "; action=" + action + "; tick=" + tick + "; cell=" + cell + "; target=" + CombatFightService.RosterId(u.Target));
        }

        private void Plan(Unit u)
        {
            var pawn = u.Pawn;
            u.NextPlan = tick + 18;
            if (u.AwaitingArrival && u.Destination == pawn.Position) { arrivals++; u.AwaitingArrival = false; }
            // Leave native abilities and special mod jobs alone, including their warmup.
            var job = pawn.CurJob;
            if (job?.ability != null || job?.def.forceCompleteBeforeNextJob == true) return;
            if (job != null && !Owns(u) && job.def != JobDefOf.Goto && job.def != JobDefOf.Wait_Combat && job.def != JobDefOf.Wait_Wander
                && job.def != JobDefOf.AttackStatic && job.def != JobDefOf.AttackMelee && !job.def.isIdle) return;
            FindThreats(u);
            if (threats.Count == 0) { Release(u); return; }
            var nearest = threats[0];
            var danger = ClosestMelee(u, pawn.Position);
            var supportingFire = danger != null && threats.Count <= 2 ? FireSupport(danger.Pawn.Position, true) : 0;
            var urgent = danger != null && CombatTacticsPolicy.ShouldEvade(Distance(pawn.Position, danger.Pawn.Position),
                danger.Pawn.GetStatValue(StatDefOf.MoveSpeed), IsPursuing(danger, pawn), u.Bait, supportingFire, u.Team.Enemies.Count);
            var exposedToRanged = false;
            foreach (var enemy in threats)
                if (enemy.Ranged != null && enemy.Ranged.CanHitTargetFrom(enemy.Pawn.Position, pawn)) { exposedToRanged = true; break; }
            var rangedPressure = CombatTacticsPolicy.ShouldBreakRangedExposure(u.Team.Shooters,
                u.Team.Members.Count(m => m.Ranged == null && m.Melee), u.Team.RangedEnemies, exposedToRanged);
            // Do not restart aim/burst for cosmetic formation improvements.
            if (pawn.stances.stunner.Stunned || u.Ranged?.state == VerbState.Bursting) return;
            if (u.Destination.IsValid && u.Destination != pawn.Position && Owns(u)
                && (pawn.pather.Moving || pawn.CurJobDef == JobDefOf.Goto))
            {
                if (pawn.Position != u.LastPosition) { u.LastPosition = pawn.Position; u.LastProgressTick = tick; }
                if (urgent && u.Evading && Free(u, u.Destination)
                    && (pawn.stances.FullBodyBusy || tick - u.LastProgressTick <= 120)
                    && Distance(u.Destination, danger.Pawn.Position) > Distance(pawn.Position, danger.Pawn.Position) + 1) return;
                // Goto may be waiting for the current shot's cooldown before its
                // first step. It must not be replaced with another firing job.
                if (pawn.CurJobDef == JobDefOf.Goto && pawn.stances.FullBodyBusy && !urgent)
                { u.LastProgressTick = tick; return; }
                if (!Free(u, u.Destination) || tick - u.LastProgressTick > 120)
                {
                    u.FailedCell = u.Destination; u.StuckUntil = tick + 120;
                    Release(u); pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
                }
                else if (!urgent && !(pawn.CurJobDef == JobDefOf.AttackMelee && u.Ranged != null && !u.Bait)
                    && !(pawn.CurJobDef == JobDefOf.AttackMelee && Active(u.Target)
                    && !u.Destination.InHorDistOf(u.Target.Position, 3) && tick - u.LastMoveTick >= 45)) return;
            }
            var front = u.Ranged == null;
            if (front)
            {
                u.Target = InterceptTarget(u);
                if (u.Target == null) return;
                if (!u.Melee) { Hold(u); return; }
                if (u.Team.Policy == "lure" && u.Team.Shooters > 0 && danger != null && IsPursuing(danger, pawn)
                    && Distance(pawn.Position, danger.Pawn.Position) < 7 && CanEscape(u, danger))
                {
                    if (FindPosition(u, u.Target, danger, true, false, out var escape) && Move(u, escape, "lure")) { kites++; return; }
                }
                var gap = Distance(pawn.Position, u.Target.Position);
                if (gap <= 1.5f) { AttackMelee(u, pawn.Position); return; }
                // Real melee fighters guard the rear. Ranged fire alone is not
                // a reason to send a supported melee fighter on a frontal charge.
                var toRear = (u.Target.Position.ToVector3() - u.Team.Rear).magnitude;
                var engage = u.Team.Shooters == 0 || toRear < 14 || u.Team.MeleeEnemies > 0 && tick - u.Team.LastCombatTick > 300;
                if (engage && FindMeleeCell(u, out var attackCell)) { AttackMelee(u, attackCell); return; }
                if (FindPosition(u, u.Target, danger, false, true, out var guard)) Move(u, guard, "guard");
                else Hold(u);
                return;
            }
            u.Target = ShootTarget(u);
            if (u.Target == null) u.Target = nearest.Pawn;
            var retreat = urgent;
            // A slower shooter still tries a short escape; it is never volunteered
            // for AttackMelee. Only threatened shooters move, leaving allies firing.
            if (retreat && FindPosition(u, u.Target, danger, true, false, out var back))
            { if (Move(u, back, u.Bait ? "bait" : "retreat")) return; }
            if (CanShoot(u, pawn.Position, u.Target))
            {
                // Under a numerically superior ranged line, break exposure between
                // shots instead of trading damage from a static position.
                if (!urgent && rangedPressure && tick >= u.LastMoveTick + 90
                    && FindFlank(u, out var evasive) && Move(u, evasive, "retreat")) return;
                // Spread the firing line gradually while preserving every clear shot.
                if (!urgent && CombatTacticsPolicy.ShouldSpread(u.Team.Shooters, u.Team.RangedEnemies,
                    true, tick - u.LastMoveTick) && FindFlank(u, out var flankPosition)
                    && Move(u, flankPosition, "spread")) return;
                // Recheck cover infrequently and retain firing positions unless materially better.
                if (!retreat && pawn.stances.curStance is not Stance_Warmup && tick >= u.LastMoveTick + 180 && u.Team.RangedEnemies > 0
                    && FindPosition(u, u.Target, danger, false, false, out var covered) && covered != pawn.Position
                    && Move(u, covered, "cover")) return;
                AttackRanged(u); return;
            }
            if (FindPosition(u, u.Target, danger, retreat, false, out var firing)) Move(u, firing, retreat ? "retreat" : "fire-position");
            else if (tick >= u.NextFlank && FindFlank(u, out var flank)) Move(u, flank, "flank");
            else Hold(u);
        }

        private void FindThreats(Unit u)
        {
            threats.Clear();
            supporters.Clear();
            nearbyMelee.Clear();
            foreach (var ally in u.Team.Members)
            {
                if (ally != u && ally.Ranged == null && ally.Melee && Active(ally.Pawn))
                {
                    var meleeIndex = 0;
                    var meleeDistance = (ally.Pawn.Position - u.Pawn.Position).LengthHorizontalSquared;
                    while (meleeIndex < nearbyMelee.Count && (nearbyMelee[meleeIndex].Pawn.Position - u.Pawn.Position).LengthHorizontalSquared <= meleeDistance) meleeIndex++;
                    if (meleeIndex < 4) { nearbyMelee.Insert(meleeIndex, ally); if (nearbyMelee.Count > 4) nearbyMelee.RemoveAt(4); }
                }
                if (ally == u || ally.Ranged == null || !Active(ally.Pawn) || ally.Pawn.pather.Moving
                    || ally.Pawn.CurJobDef == JobDefOf.AttackMelee) continue;
                var index = 0;
                var d = (ally.Pawn.Position - u.Pawn.Position).LengthHorizontalSquared;
                while (index < supporters.Count && (supporters[index].Pawn.Position - u.Pawn.Position).LengthHorizontalSquared <= d) index++;
                if (index < 4) { supporters.Insert(index, ally); if (supporters.Count > 4) supporters.RemoveAt(4); }
            }
            // Keep the unit's actual pursuer even when a dense melee front pushes it
            // outside the nearest-threat shortlist.
            Unit pursuer = null;
            foreach (var enemy in u.Team.Enemies)
                if (Active(enemy.Pawn) && !enemy.Hidden && IsPursuing(enemy, u.Pawn)) { pursuer = enemy; break; }
            if (pursuer != null && !threats.Contains(pursuer))
            { if (threats.Count == 4) threats.RemoveAt(3); threats.Add(pursuer); }
            // Four nearest threats, with a slot reserved for a ranged flank threat.
            Unit ranged = null; float rangedDistance = float.MaxValue;
            foreach (var enemy in u.Team.Enemies)
            {
                if (!Active(enemy.Pawn) || enemy.Hidden) continue;
                float d = (enemy.Pawn.Position - u.Pawn.Position).LengthHorizontalSquared;
                if (enemy.Ranged != null && d < rangedDistance) { ranged = enemy; rangedDistance = d; }
                var index = 0;
                while (index < threats.Count && (threats[index].Pawn.Position - u.Pawn.Position).LengthHorizontalSquared <= d) index++;
                if (index < 4) { threats.Insert(index, enemy); if (threats.Count > 4) threats.RemoveAt(4); }
            }
            if (ranged != null && !threats.Contains(ranged))
            { if (threats.Count == 4) threats.RemoveAt(3); threats.Add(ranged); }
        }
        private Unit ClosestMelee(Unit u, IntVec3 cell)
        {
            Unit best = null; float distance = float.MaxValue;
            foreach (var enemy in threats)
            {
                if (!enemy.Melee || enemy.Ranged != null && !IsPursuing(enemy, u.Pawn) && !enemy.Pawn.Position.InHorDistOf(cell, 4)) continue;
                var d = (enemy.Pawn.Position - cell).LengthHorizontalSquared;
                if (d < distance) { best = enemy; distance = d; }
            }
            return best;
        }
        private Pawn InterceptTarget(Unit u)
        {
            Pawn best = null; float score = float.MaxValue;
            foreach (var enemy in threats)
            {
                var distance = Distance(u.Pawn.Position, enemy.Pawn.Position);
                var rearDistance = (enemy.Pawn.Position.ToVector3() - u.Team.Rear).magnitude;
                float value = distance + rearDistance * .6f;
                foreach (var ally in u.Team.Members) if (ally != u && ally.Target == enemy.Pawn && (ally.Bait || ally.Ranged == null)) value += 4;
                if (enemy.Ranged == null) value -= 5;
                if (value < score) { score = value; best = enemy.Pawn; }
            }
            return best;
        }
        private Pawn ShootTarget(Unit u)
        {
            if (Active(u.Target) && u.Team.Enemies.Exists(e => e.Pawn == u.Target) && CanShoot(u, u.Pawn.Position, u.Target)) return u.Target;
            Pawn best = null;
            float bestScore = float.MinValue;
            foreach (var enemy in threats)
            {
                if (!CanShoot(u, u.Pawn.Position, enemy.Pawn)) continue;
                var score = TargetPriority(u, enemy);
                if (score > bestScore) { best = enemy.Pawn; bestScore = score; }
            }
            return best;
        }
        private float TargetPriority(Unit u, Unit enemy)
        {
            var distance = Distance(u.Pawn.Position, enemy.Pawn.Position);
            var score = enemy.Melee ? 20 : 0;
            if (enemy.Pawn.CurJobDef == JobDefOf.AttackMelee && enemy.Pawn.CurJob.targetA.Thing is Pawn victim
                && units.TryGetValue(victim, out var victimUnit) && victimUnit.Team == u.Team)
            {
                score += u.Team.Policy == "lure" && victimUnit.Bait ? 35 : 24;
                if (FireSupport(enemy.Pawn.Position, false) > 0) score += 18;
            }
            if (enemy.Ranged != null) score += 8;
            if (enemy.Pawn.Downed) score += 12;
            return score - distance * .12f;
        }
        private bool CanShoot(Unit u, IntVec3 cell, Pawn target)
        {
            if (u.Ranged == null || !Active(target)) return false;
            var d = Distance(cell, target.Position);
            if (d > u.Range || d < u.Ranged.verbProps.EffectiveMinRange(target, u.Pawn)) return false;
            losChecks++; return u.Ranged.CanHitTargetFrom(cell, target);
        }
        private static bool IsPursuing(Unit enemy, Pawn pawn)
            => enemy.Pawn.CurJobDef == JobDefOf.AttackMelee && enemy.Pawn.CurJob.targetA.Thing == pawn
                || enemy.Ranged == null && enemy.Pawn.pather.Moving && enemy.Pawn.mindState?.enemyTarget == pawn;
        private static bool CanEscape(Unit u, Unit enemy)
            => u.Pawn.GetStatValue(StatDefOf.MoveSpeed) >= enemy.Pawn.GetStatValue(StatDefOf.MoveSpeed) * .92f;
        private static float Distance(IntVec3 a, IntVec3 b) => a.DistanceTo(b);

        private static IntVec3 FollowingCell(IntVec3 destination, Unit pursuer)
        {
            var behind = pursuer.Pawn.Position.ToVector3() - destination.ToVector3();
            return (destination.ToVector3() + Vector3.ClampMagnitude(behind, 6)).ToIntVec3();
        }
        private int FireSupport(IntVec3 cell, bool lineOfSight)
        {
            if (!cell.InBounds(map)) return 0;
            var count = 0;
            foreach (var ally in supporters)
            {
                if (!Active(ally.Pawn) || Distance(ally.Pawn.Position, cell) > ally.Range - 2) continue;
                if (lineOfSight) { losChecks++; if (!ally.Ranged.CanHitTargetFrom(ally.Pawn.Position, cell)) continue; }
                count++;
            }
            return count;
        }

        private bool Free(Unit u, IntVec3 cell)
        {
            if (!cell.InBounds(map) || !cell.WalkableBy(map, u.Pawn)) return false;
            if (u.StuckUntil > tick && u.FailedCell == cell) return false;
            if (destinations.TryGetValue(cell, out var owner) && owner != u && Active(owner.Pawn) && Owns(owner)) return false;
            var things = map.thingGrid.ThingsListAtFast(cell);
            foreach (var thing in things)
                if (thing is Pawn other && other != u.Pawn && other.Spawned) { blockedCells++; return false; }
            if (!map.pawnDestinationReservationManager.CanReserve(cell, u.Pawn)) return false;
            if (u.Team.Radius > 0 && !cell.InHorDistOf(u.Team.Anchor, u.Team.Radius)) return false;
            return !PawnUtility.KnownDangerAt(cell, map, u.Pawn);
        }
        private bool Reach(Unit u, IntVec3 cell)
        {
            if (cell == u.Pawn.Position) return true;
            return budget.TakeReach() && u.Pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some);
        }
        private bool FindMeleeCell(Unit u, out IntVec3 result)
        {
            candidates.Clear();
            foreach (var offset in GenAdj.AdjacentCells)
            {
                var cell = u.Target.Position + offset;
                if (!Free(u, cell)) continue;
                candidates.Add(new Candidate { Cell = cell, Score = -Distance(cell, u.Pawn.Position) });
            }
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            for (var i = 0; i < Math.Min(CombatTacticsPolicy.ShortlistLimit, candidates.Count); i++)
                if (Reach(u, candidates[i].Cell)) { result = candidates[i].Cell; return true; }
            result = IntVec3.Invalid; return false;
        }

        private bool FindFlank(Unit u, out IntVec3 result)
        {
            u.NextFlank = tick + 120;
            candidates.Clear();
            // A wall may require temporarily moving away from the ideal range.
            // Sample two small rings; the native path follower handles the route.
            // Never flood-fill the map or search every cell for every shooter.
            var radius = Math.Max(4, Math.Min(12, (int)(u.Range * .65f)));
            for (var i = 0; i < 8; i++)
            {
                var offset = GenAdj.AdjacentCells[i];
                var cell = u.Pawn.Position + offset * 9;
                if (!Free(u, cell) || !CanShoot(u, cell, u.Target)) continue;
                var score = Score(u, cell, u.Target, ClosestMelee(u, cell), false, false, u.Pawn.Position.ToVector3());
                candidates.Add(new Candidate { Cell = cell, Score = score });
            }
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            for (var i = 0; i < Math.Min(CombatTacticsPolicy.ShortlistLimit, candidates.Count); i++)
                if (Reach(u, candidates[i].Cell)) { result = candidates[i].Cell; return true; }
            result = IntVec3.Invalid; return false;
        }

        private bool FindPosition(Unit u, Pawn target, Unit danger, bool retreat, bool guard, out IntVec3 result)
        {
            candidates.Clear();
            var current = u.Pawn.Position;
            var direction = target.Position.ToVector3() - u.Team.Rear;
            direction.y = 0; if (direction.sqrMagnitude < 1) direction = Vector3.right; direction.Normalize();
            var ideal = guard ? u.Team.Anchor.ToVector3() + direction * 5 : target.Position.ToVector3() - direction * CombatTacticsPolicy.FireDistance(u.Range, u.Ranged?.verbProps.EffectiveMinRange(target, u.Pawn) ?? 0, danger != null);
            if (retreat && danger != null)
            {
                var away = (current - danger.Pawn.Position).ToVector3(); away.Normalize();
                // Tangential movement keeps a pursuer inside the supporting firing arc.
                if (!guard && (u.Bait || u.Team.Policy == "lure" && u.Ranged == null))
                {
                    var side = new Vector3(-away.z, 0, away.x);
                    var supportCenter = Vector3.zero;
                    foreach (var ally in supporters) supportCenter += ally.Pawn.Position.ToVector3();
                    if (supporters.Count > 0)
                    { if (Vector3.Dot(side, supportCenter / supporters.Count - current.ToVector3()) < 0) side = -side; }
                    else if (u.Pawn.thingIDNumber % 2 != 0) side = -side;
                    away = (away + side * 1.5f).normalized;
                }
                ideal = current.ToVector3() + away * 5;
            }
            AddCandidate(u, current, target, danger, retreat, guard, ideal);
            var toward = ideal - current.ToVector3(); toward.y = 0; toward = Vector3.ClampMagnitude(toward, 6);
            var center = (current.ToVector3() + toward).ToIntVec3();
            AddCandidate(u, center, target, danger, retreat, guard, ideal);
            // Fixed local sample count: never enumerate the map or invoke unrestricted cast-position searches.
            for (var i = 0; i < 8; i++)
            {
                var offset = GenAdj.AdjacentCells[i];
                AddCandidate(u, current + offset * 3, target, danger, retreat, guard, ideal);
                AddCandidate(u, center + offset * 2, target, danger, retreat, guard, ideal);
                if (i < 6) AddCandidate(u, current + offset, target, danger, retreat, guard, ideal);
            }
            var currentScore = Score(u, current, target, danger, retreat, guard, ideal);
            var preserveSupport = retreat && danger != null && FireSupport(danger.Pawn.Position, true) > 0;
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            for (var i = 0; i < Math.Min(CombatTacticsPolicy.ShortlistLimit, candidates.Count); i++)
            {
                var candidate = candidates[i];
                candidate.Score += CoverScore(u, candidate.Cell);
                candidates[i] = candidate;
            }
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            for (var i = 0; i < Math.Min(CombatTacticsPolicy.ShortlistLimit, candidates.Count); i++)
            {
                var c = candidates[i];
                if (c.Cell == current) { result = current; return false; }
                if (!CombatTacticsPolicy.WorthMoving(currentScore, c.Score, retreat)) continue;
                // Intermediate approach steps need not yet have a shot, but occupied firing lines never qualify as cover.
                if (!guard && !retreat && Distance(current, target.Position) <= u.Range && !CanShoot(u, c.Cell, target)) continue;
                if (preserveSupport && Distance(current, danger.Pawn.Position) > 2.5f
                    && FireSupport(FollowingCell(c.Cell, danger), true) == 0) continue;
                if (!Reach(u, c.Cell)) continue;
                result = c.Cell; return true;
            }
            result = IntVec3.Invalid; return false;
        }
        private void AddCandidate(Unit u, IntVec3 cell, Pawn target, Unit danger, bool retreat, bool guard, Vector3 ideal)
        {
            if (candidates.Count >= CombatTacticsPolicy.CandidateLimit || !Free(u, cell)) return;
            foreach (var candidate in candidates) if (candidate.Cell == cell) return;
            candidates.Add(new Candidate { Cell = cell, Score = Score(u, cell, target, danger, retreat, guard, ideal) });
        }
        private float Score(Unit u, IntVec3 cell, Pawn target, Unit danger, bool retreat, bool guard, Vector3 ideal)
        {
            float score = -(cell.ToVector3() - ideal).magnitude * (guard ? 2 : 1.1f) - Distance(cell, u.Pawn.Position) * .65f;
            if (!guard && u.Ranged != null)
            {
                var distance = Distance(cell, target.Position);
                if (distance <= u.Range && distance >= u.Ranged.verbProps.EffectiveMinRange(target, u.Pawn)) score += 12;
                else score -= Math.Max(0, distance - u.Range) * 2;
            }
            foreach (var enemy in threats)
            {
                var d = Distance(cell, enemy.Pawn.Position);
                if (enemy.Melee && (enemy.Ranged == null || IsPursuing(enemy, u.Pawn)))
                { if (d < 8 && !guard) score -= (8 - d) * (retreat ? 9 : 5); }
            }
            if (retreat && danger != null && supporters.Count > 0)
            {
                var coverage = FireSupport(FollowingCell(cell, danger), false);
                score += coverage == 0 ? -30 : coverage * 7;
            }
            if (!guard && u.Team.Shooters > 0)
            {
                var rearDistance = (cell.ToVector3() - u.Team.Rear).magnitude;
                if (retreat && (u.Bait || u.Ranged == null) && rearDistance > 18) score -= (rearDistance - 18) * 5;
                // Only real melee fighters define the front line. An evading
                // shooter must not drag the entire ranged formation after it.
                if (u.Ranged != null)
                    foreach (var ally in nearbyMelee)
                    {
                        if (!Active(ally.Pawn)) continue;
                        var projection = Vector3.Dot(cell.ToVector3() - ally.Pawn.Position.ToVector3(), directionTo(target, u.Team.Rear));
                        if (projection > -3 && Distance(cell, ally.Pawn.Position) < 10) score -= (projection + 3) * 2;
                    }
            }
            if (u.Ranged != null && u.Team.RangedEnemies > 0)
                foreach (var ally in u.Team.Members)
                    if (ally != u && ally.Ranged != null && Active(ally.Pawn))
                    {
                        var spacing = Distance(cell, ally.Pawn.Position);
                        if (spacing < 6) score -= (6 - spacing) * 1.8f;
                    }
            return score;
        }
        private float CoverScore(Unit u, IntVec3 cell)
        {
            if (!u.Pawn.def.CanBenefitFromCover) return 0;
            var score = 0f;
            foreach (var enemy in threats)
                if (enemy.Ranged != null && Distance(cell, enemy.Pawn.Position) <= enemy.Range)
                {
                    losChecks++;
                    score += CoverUtility.CalculateOverallBlockChance(cell, enemy.Pawn.Position, map) * 22 / threats.Count;
                }
            return score;
        }
        private static Vector3 directionTo(Pawn target, Vector3 rear) => (target.Position.ToVector3() - rear).normalized;

        private void Release(Unit u)
        {
            if (u.Destination.IsValid && destinations.TryGetValue(u.Destination, out var owner) && owner == u) destinations.Remove(u.Destination);
            u.Destination = IntVec3.Invalid;
            u.AwaitingArrival = false;
            u.Evading = false;
        }
        private void Start(Unit u, Job job, IntVec3 destination)
        {
            Release(u);
            job.expiryInterval = 600; job.checkOverrideOnExpire = true; job.collideWithPawns = true;
            u.Pawn.jobs.StartJob(job, JobCondition.InterruptForced, cancelBusyStances: false);
            u.Job = null; u.JobId = -1;
            if (u.Pawn.CurJob != job) return;
            u.Job = job; u.JobId = job.loadID;
            u.Destination = destination; destinations[destination] = u;
            map.pawnDestinationReservationManager.Reserve(u.Pawn, job, destination);
            if (u.Pawn.mindState?.duty != null) u.Pawn.mindState.duty.focus = destination;
        }
        private bool Move(Unit u, IntVec3 destination, string action)
        {
            if (destination == u.Pawn.Position || !Free(u, destination)) return false;
            if (Owns(u) && u.Pawn.CurJobDef == JobDefOf.Goto && u.Destination == destination) return true;
            if (!budget.TakeMove()) { u.NextPlan = tick + 3; return false; }
            Start(u, JobMaker.MakeJob(JobDefOf.Goto, destination), destination);
            u.Evading = action == "bait" || action == "retreat" || action == "lure";
            if (action == "bait") baitMoves++;
            if (action == "retreat") retreatMoves++;
            if (u.Evading)
            {
                var pursuer = ClosestMelee(u, u.Pawn.Position);
                if (pursuer != null && FireSupport(FollowingCell(destination, pursuer), true) > 0) supportedEvades++;
            }
            if (action == "bait" || action == "retreat") u.Pawn.stances.CancelBusyStanceSoft();
            u.AwaitingArrival = true;
            u.LastPosition = u.Pawn.Position; u.LastMoveTick = u.LastProgressTick = tick; moves++; Trace(u, action, destination);
            return true;
        }
        private void Hold(Unit u)
        {
            if (Owns(u) && u.Pawn.CurJobDef == JobDefOf.Wait_Combat) return;
            var job = JobMaker.MakeJob(JobDefOf.Wait_Combat); job.canUseRangedWeapon = true;
            Start(u, job, u.Pawn.Position); holds++;
        }
        private void AttackRanged(Unit u)
        {
            u.Team.LastCombatTick = tick;
            if (Owns(u) && u.Pawn.CurJobDef == JobDefOf.AttackStatic && u.Job.targetA.Thing == u.Target) return;
            var job = JobMaker.MakeJob(JobDefOf.AttackStatic, u.Target);
            job.endIfCantShootTargetFromCurPos = true; job.endIfCantShootInMelee = true;
            Start(u, job, u.Pawn.Position);
            if (threats.Exists(e => e.Ranged != null && CoverUtility.CalculateOverallBlockChance(u.Pawn, e.Pawn.Position, map) > .05f)) coverPositions++;
            Trace(u, "shoot", u.Pawn.Position);
        }
        private void AttackMelee(Unit u, IntVec3 cell)
        {
            if (!Free(u, cell)) return;
            if (Owns(u) && u.Pawn.CurJobDef == JobDefOf.AttackMelee && u.Job.targetA.Thing == u.Target && u.Destination == cell) return;
            if (cell != u.Pawn.Position && !budget.TakeMove()) { u.NextPlan = tick + 3; return; }
            var job = JobMaker.MakeJob(JobDefOf.AttackMelee, u.Target); job.targetB = cell;
            if (u.Ranged != null) { orderedRangedMelee++; JobMaker.ReturnToPool(job); return; }
            Start(u, job, cell); u.LastMoveTick = u.LastProgressTick = tick; u.LastPosition = u.Pawn.Position; u.Team.LastCombatTick = tick;
            if (cell != u.Pawn.Position) moves++;
            Trace(u, "intercept", cell);
        }
    }
}
