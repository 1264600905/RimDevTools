#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    // Counters are authoritative. Retaining fewer event details never changes totals.
    // This ledger deliberately has no text, amount, tick, or time based deduplication.
    public sealed class GroupCombatLedger
    {
        private readonly Dictionary<string, GroupCombatUnit> units;
        private readonly Dictionary<string, GroupCombatTeamResult> teams;
        private readonly Dictionary<string, GroupCombatMatrixCell> matrix = new Dictionary<string, GroupCombatMatrixCell>();
        private readonly List<GroupCombatDamageEvent> details = new List<GroupCombatDamageEvent>();
        private readonly int detailLimit;
        private long sequence;
        private long events;
        private long invalidEvents;
        private readonly Dictionary<string, int> observedTicks = new Dictionary<string, int>();
        private int snapshotTick;
        public GroupCombatLedger(IEnumerable<GroupCombatUnit> roster, IEnumerable<KeyValuePair<string, string>> teamNames, int detailLimit = 10000)
        {
            if (detailLimit < 0) throw new ArgumentOutOfRangeException(nameof(detailLimit));
            this.detailLimit = detailLimit;
            units = roster.ToDictionary(u => u.Id, StringComparer.Ordinal);
            teams = teamNames.ToDictionary(t => t.Key, t => new GroupCombatTeamResult { Id = t.Key, Name = t.Value }, StringComparer.Ordinal);
            if (teams.Count != 2 || units.Count == 0 || units.Values.Any(u => !teams.ContainsKey(u.Team)) || teams.Keys.Any(t => !units.Values.Any(u => u.Team == t))) throw new ArgumentException("Group combat requires two nonempty teams and unique roster IDs");
        }

        public long NextEventId() => ++sequence;
        public bool AddUnit(GroupCombatUnit unit, int elapsedTicks)
        {
            if (elapsedTicks < snapshotTick || !teams.ContainsKey(unit.Team)) throw new ArgumentException("Invalid reinforcement team or arrival tick");
            if (units.ContainsKey(unit.Id)) return false;
            unit.JoinedDuringBattle = true; unit.JoinedAtTick = elapsedTicks; unit.AliveTicks = 0;
            units.Add(unit.Id, unit); observedTicks[unit.Id] = elapsedTicks;
            return true;
        }
        public void Record(GroupCombatDamageEvent hit)
        {
            if (!double.IsFinite(hit.Amount) || hit.Amount < 0 || !double.IsFinite(hit.RawAmount) || hit.RawAmount < 0) { invalidEvents++; return; }
            // A result is submitted once by its native invocation, even if another invocation
            // in the same tick has precisely the same amount and participants.
            if (hit.Id <= 0) hit.Id = NextEventId();
            units.TryGetValue(hit.SourceId ?? "", out var source);
            units.TryGetValue(hit.VictimId ?? "", out var victim);
            hit.SourceTeam = source?.Team ?? hit.SourceTeam;
            hit.VictimTeam = victim?.Team ?? hit.VictimTeam;
            var sourceTeam = hit.SourceTeam != null && teams.TryGetValue(hit.SourceTeam, out var st) ? st : null;
            var victimTeam = hit.VictimTeam != null && teams.TryGetValue(hit.VictimTeam, out var vt) ? vt : null;
            hit.Attribution = hit.SourceId != null && hit.SourceId == hit.VictimId ? "self" : sourceTeam != null && victimTeam != null ? sourceTeam == victimTeam ? "friendly" : "enemy" : hit.SourceId == null ? "environment" : "unknown";
            var from = sourceTeam?.Id ?? (hit.SourceId == null ? "environment" : "unknown");
            var to = victimTeam?.Id ?? "outside";
            var key = from + "|" + to + "|" + hit.Channel;
            if (!matrix.TryGetValue(key, out var cell)) matrix.Add(key, cell = new GroupCombatMatrixCell { Source = from, Victim = to, Channel = hit.Channel });
            cell.Amount += hit.Amount; cell.Events++;
            if (hit.Channel == "pawn")
            {
                if (victim != null) victim.DamageTaken += hit.Amount;
                if (victimTeam != null) victimTeam.DamageTaken += hit.Amount;
                if (hit.Attribution == "enemy")
                {
                    sourceTeam.EnemyDamage += hit.Amount;
                    if (source != null) source.EnemyDamage += hit.Amount; else sourceTeam.UnrosteredEnemyDamage += hit.Amount;
                }
                else if (hit.Attribution == "friendly") { sourceTeam.FriendlyDamage += hit.Amount; if (source != null) source.FriendlyDamage += hit.Amount; }
                else if (hit.Attribution == "self") { if (sourceTeam != null) sourceTeam.SelfDamage += hit.Amount; if (source != null) source.SelfDamage += hit.Amount; }
            }
            events++;
            if (details.Count < detailLimit) details.Add(hit);
        }

        public void SetState(string id, string state, int? elapsedTicks = null)
        {
            if (!new[] { "standing", "downed", "dead", "escaped", "removed" }.Contains(state)) throw new ArgumentException("Unknown roster state " + state);
            Advance(units[id], elapsedTicks ?? snapshotTick);
            units[id].State = state;
            if (state == "downed") MarkDowned(id);
            if (state == "dead") MarkDead(id);
        }

        // Preserve history even if a native recovery/resurrection happens in the same tick.
        public void MarkDowned(string id) { if (units.TryGetValue(id, out var unit)) unit.WasDowned = true; }
        public void MarkDead(string id) { if (units.TryGetValue(id, out var unit)) unit.WasDead = true; }
        public bool IsCameraTarget(string id) => units.TryGetValue(id, out var unit) && unit.CameraEligible;
        public void SetPendingRevival(string id, bool pending) => units[id].PendingRevival = pending;

        private void Advance(GroupCombatUnit unit, int tick)
        {
            observedTicks.TryGetValue(unit.Id, out var previous);
            if (tick < previous) throw new ArgumentOutOfRangeException(nameof(tick), "Unit lifetime clock cannot move backwards");
            if (unit.Alive) unit.AliveTicks += tick - previous;
            observedTicks[unit.Id] = tick;
        }

        public GroupCombatRoundResult Snapshot(int elapsedTicks, bool includeEventDetails = true)
        {
            if (elapsedTicks < 0) throw new ArgumentOutOfRangeException(nameof(elapsedTicks));
            snapshotTick = elapsedTicks;
            var seconds = elapsedTicks / 60d;
            foreach (var unit in units.Values)
            {
                Advance(unit, elapsedTicks);
                unit.Dps = unit.AliveTicks > 0 ? unit.EnemyDamage * 60d / unit.AliveTicks : 0;
            }
            foreach (var team in teams.Values)
            {
                var members = units.Values.Where(u => u.Team == team.Id).ToList();
                team.Initial = members.Count(u => !u.JoinedDuringBattle); team.Joined = members.Count(u => u.JoinedDuringBattle); team.Standing = members.Count(u => u.State == "standing"); team.Downed = members.Count(u => u.State == "downed"); team.Dead = members.Count(u => u.State == "dead"); team.Escaped = members.Count(u => u.State == "escaped"); team.Removed = members.Count(u => u.State == "removed"); team.Alive = team.Standing + team.Downed;
                var living = members.Where(u => u.Alive).ToList();
                team.PendingRevival = members.Count(u => u.PendingRevival);
                team.Dps = seconds > 0 ? team.EnemyDamage / seconds : 0;
                team.MeanDps = living.Count > 0 ? living.Average(u => u.Dps) : 0;
                team.MeanDamageTaken = living.Count > 0 ? living.Average(u => u.DamageTaken) : 0;
                team.Damage = GroupCombatDistribution.Calculate(members.Select(u => new KeyValuePair<string, double>(u.Id, u.EnemyDamage)));
                team.DpsDistribution = GroupCombatDistribution.Calculate(members.Select(u => new KeyValuePair<string, double>(u.Id, u.Dps)));
                team.Taken = GroupCombatDistribution.Calculate(members.Select(u => new KeyValuePair<string, double>(u.Id, u.DamageTaken)));
            }
            // Detach every snapshot from the running counters: paused/final reports and HUD
            // must not mutate when the next native damage callback arrives.
            var result = new GroupCombatRoundResult {
                ElapsedTicks = elapsedTicks, SimulatedSeconds = seconds, TotalEvents = events, DroppedDetailEvents = events - details.Count,
                Units = units.Values.OrderBy(u => u.Id, StringComparer.Ordinal).Select(CopyUnit).ToList(),
                Teams = teams.Values.Select(CopyTeam).ToList(), Matrix = matrix.Values.Select(c => new GroupCombatMatrixCell { Source = c.Source, Victim = c.Victim, Channel = c.Channel, Amount = c.Amount, Events = c.Events }).ToList(),
                Events = includeEventDetails ? details.Select(CopyEvent).ToList() : new List<GroupCombatDamageEvent>()
            };
            if (invalidEvents > 0) result.Diagnostics.Add("Rejected invalid settled-damage values: " + invalidEvents);
            result.MeasurementRequirementsMet = invalidEvents == 0 && !matrix.Values.Any(c => c.Source == "unknown" && c.Amount > 0);
            return result;
        }

        private static GroupCombatUnit CopyUnit(GroupCombatUnit u) => new GroupCombatUnit { Id = u.Id, NativeId = u.NativeId, Team = u.Team, Group = u.Group, GroupName = u.GroupName, Kind = u.Kind, KindLabel = u.KindLabel, Name = u.Name, State = u.State, JoinedDuringBattle = u.JoinedDuringBattle, JoinedAtTick = u.JoinedAtTick, PendingRevival = u.PendingRevival, WasDowned = u.WasDowned, WasDead = u.WasDead, X = u.X, Z = u.Z, InitialSnapshot = u.InitialSnapshot, EnemyDamage = u.EnemyDamage, FriendlyDamage = u.FriendlyDamage, SelfDamage = u.SelfDamage, DamageTaken = u.DamageTaken, AliveTicks = u.AliveTicks, Dps = u.Dps };
        private static GroupCombatDistribution CopyDistribution(GroupCombatDistribution d) => new GroupCombatDistribution { Count = d.Count, Min = d.Min, Max = d.Max, Mean = d.Mean, Median = d.Median, MinIds = d.MinIds.ToList(), MaxIds = d.MaxIds.ToList() };
        private static GroupCombatTeamResult CopyTeam(GroupCombatTeamResult t) => new GroupCombatTeamResult { Id = t.Id, Name = t.Name, Outcome = t.Outcome, EliminatedAtTick = t.EliminatedAtTick, Initial = t.Initial, Joined = t.Joined, Standing = t.Standing, Downed = t.Downed, Dead = t.Dead, Escaped = t.Escaped, Removed = t.Removed, Alive = t.Alive, PendingRevival = t.PendingRevival, EnemyDamage = t.EnemyDamage, UnrosteredEnemyDamage = t.UnrosteredEnemyDamage, FriendlyDamage = t.FriendlyDamage, SelfDamage = t.SelfDamage, DamageTaken = t.DamageTaken, Dps = t.Dps, MeanDps = t.MeanDps, MeanDamageTaken = t.MeanDamageTaken, Damage = CopyDistribution(t.Damage), DpsDistribution = CopyDistribution(t.DpsDistribution), Taken = CopyDistribution(t.Taken) };
        private static GroupCombatDamageEvent CopyEvent(GroupCombatDamageEvent e) => new GroupCombatDamageEvent { Id = e.Id, ParentId = e.ParentId, Tick = e.Tick, SourceId = e.SourceId, VictimId = e.VictimId, SourceTeam = e.SourceTeam, VictimTeam = e.VictimTeam, DamageDef = e.DamageDef, WeaponDef = e.WeaponDef, Channel = e.Channel, Attribution = e.Attribution, Amount = e.Amount, RawAmount = e.RawAmount, Absorbed = e.Absorbed, Note = e.Note };
    }

    public sealed class GroupCombatEndRule
    {
        private readonly Dictionary<string, int> eliminated = new Dictionary<string, int>();
        private readonly Dictionary<string, int> totals = new Dictionary<string, int>();
        private readonly int confirmationTicks;
        public GroupCombatEndRule(int confirmationTicks = 300)
        {
            if (confirmationTicks < 0) throw new ArgumentOutOfRangeException(nameof(confirmationTicks));
            this.confirmationTicks = confirmationTicks;
        }
        public bool Evaluate(GroupCombatRoundResult current, int durationTicks)
        {
            foreach (var team in current.Teams)
            {
                var defeated = team.Total > 0 && team.Downed + team.Dead == team.Total && team.PendingRevival == 0;
                if (totals.TryGetValue(team.Id, out var previous) && previous != team.Total) eliminated.Remove(team.Id);
                totals[team.Id] = team.Total;
                if (defeated && !eliminated.ContainsKey(team.Id)) eliminated.Add(team.Id, current.ElapsedTicks);
                if (!defeated) { eliminated.Remove(team.Id); team.EliminatedAtTick = null; }
                if (eliminated.TryGetValue(team.Id, out var tick)) team.EliminatedAtTick = tick;
            }
            var active = current.Teams.Where(t => !eliminated.TryGetValue(t.Id, out var since) || current.ElapsedTicks - since < confirmationTicks).ToList();
            if (eliminated.Count == current.Teams.Count && active.Count > 0 && current.ElapsedTicks < durationTicks) return false;
            if (active.Count > 1 && current.ElapsedTicks < durationTicks) return false;
            var timeLimit = current.ElapsedTicks >= durationTicks;
            if (timeLimit) active = current.Teams.ToList();
            current.EndReason = timeLimit ? "timeLimit" : "allDowned";
            var winners = active.Count == 0 ? current.Teams.ToList() : active.Where(t => t.Alive == active.Max(a => a.Alive)).ToList();
            foreach (var team in current.Teams) team.Outcome = winners.Contains(team) ? winners.Count > 1 ? "draw" : "win" : "loss";
            return true;
        }
    }
}
