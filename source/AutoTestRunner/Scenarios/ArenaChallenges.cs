#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LogSyncMod.AutoTest.Scenarios
{
    public interface IArenaSchedule
    {
        string Champion { get; }
        ArenaMatch Next();
        void Resolve(string winner, ArenaScore score);
    }

    public sealed class ArenaGrowth
    {
        public string Target { get; set; } = "count";
        public string Mode { get; set; } = "multiply";
        public decimal Multiplier { get; set; }
        public decimal Increment { get; set; }
        public decimal MaxValue { get; set; }
    }

    public sealed class ArenaChallengePair
    {
        public string Id { get; set; }
        public string Boss { get; set; }
        public string Challenger { get; set; }
        public decimal InitialValue { get; set; }
        public string Status { get; set; } = "pending";
        public string LimitReason { get; set; }
        public ResolvedCombatRoster LimitRoster { get; set; }
        public decimal? WinningValue { get; set; }
        public List<string> Matches { get; set; } = new();
    }

    public sealed class ArenaBossStanding
    {
        public string EntrantId { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Unresolved { get; set; }
        public decimal ScoreSum { get; set; }
        public decimal? AverageVictoryScore { get; set; }
    }

    public sealed class ArenaSummaryPage
    {
        public List<string> EntryIds { get; set; } = new();
        public double VisibleSeconds { get; set; }
        public int SimulationTicks { get; set; }
        public string Screenshot { get; set; }
    }

    public sealed class ArenaChallengeSummary
    {
        public List<ArenaBossStanding> Bosses { get; set; } = new();
        public List<ArenaSummaryPage> Pages { get; set; } = new();
        public bool Completed { get; set; }
    }

    public static class ArenaChallengeRules
    {
        public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
        public static decimal InitialValue(ArenaTournamentInput input, ArenaEntrant entrant)
            => input.RosterMode == "combatPower" ? (entrant.Budget ?? input.Budget).Value : entrant.Count ?? input.Count;

        public static decimal? NextValue(ArenaGrowth growth, decimal current)
        {
            decimal next;
            try { next = growth.Mode == "add" ? checked(current + growth.Increment) : checked(current * growth.Multiplier); }
            catch (OverflowException) { return null; }
            if (growth.Target == "count") next = decimal.Ceiling(next);
            if (next <= current) throw new ScenarioConfigurationException("Growth does not increase the value at decimal precision");
            return next <= growth.MaxValue ? next : null;
        }

        public static void Validate(ArenaTournamentInput input)
        {
            if (!new[] { "knockout", "bossRoundRobin", "limitChallenge" }.Contains(input.Format))
                throw new ScenarioConfigurationException("Tournament format must be knockout/bossRoundRobin/limitChallenge");
            if (input.Format == "knockout")
            {
                if (input.Growth != null || input.Entrants.Any(e => e.Role != null)) throw new ScenarioConfigurationException("Roles and Growth require a challenge format");
                return;
            }
            if (input.Entrants.Any(e => e.Role != "boss" && e.Role != "challenger")
                || !input.Entrants.Any(e => e.Role == "boss") || !input.Entrants.Any(e => e.Role == "challenger"))
                throw new ScenarioConfigurationException("Challenge formats require at least one boss and one challenger; every entrant needs a role");
            if (input.Format == "bossRoundRobin")
            {
                if (input.Growth != null) throw new ScenarioConfigurationException("Growth is only supported by limitChallenge");
                return;
            }
            var g = input.Growth;
            if (g == null || g.MaxValue <= 0 || g.Target != "count" && g.Target != "budget"
                || g.Mode != "add" && g.Mode != "multiply"
                || g.Mode == "multiply" && (g.Multiplier <= 1 || g.Increment != 0)
                || g.Mode == "add" && (g.Increment <= 0 || g.Multiplier != 0 || g.Target == "count" && g.Increment != decimal.Truncate(g.Increment)))
                throw new ScenarioConfigurationException("Growth requires mode=multiply with multiplier > 1, or mode=add with positive increment (integer for count), and positive maxValue");
            if ((g.Target == "count") != (input.RosterMode == "count")) throw new ScenarioConfigurationException("Growth target must match Roster count/combatPower mode");
            if (g.Target == "count" && (g.MaxValue != decimal.Truncate(g.MaxValue) || g.MaxValue > CombatRoster.MaxTeamMembers)
                || g.Target == "budget" && g.MaxValue > 10000000)
                throw new ScenarioConfigurationException("Growth maxValue must be an integer in 1..200 for count, or at most 10000000 CP for budget");
            foreach (var e in input.Entrants.Where(e => e.Role == "challenger"))
            {
                if (InitialValue(input, e) > g.MaxValue) throw new ScenarioConfigurationException("Growth maxValue is below initial value: " + e.Id);
                if (g.Target == "budget" && e.Squad != null && input.SquadTemplates[e.Squad].Composition == "fixed")
                    throw new ScenarioConfigurationException("Budget growth cannot expand a fixed squad; use count growth or a weighted squad");
            }
        }

        public static int DeadlineSeconds(ArenaTournamentInput input)
        {
            try
            {
                checked
                {
                    var seconds = 180 + (int)ArenaPresentation.RulesSeconds + (int)Math.Ceiling(input.ShowcasePages.Sum(p => p.Seconds));
                    var perMatch = (input.MaxReplays + 1) * (input.TimeoutRealSeconds + (int)Math.Ceiling(input.BracketSeconds + input.SettlementSeconds + ArenaPresentation.PreBattleSeconds + ArenaPresentation.PostBattleSeconds) + 45);
                    if (input.Format == "knockout") return seconds + (int)ArenaStandings.DisplaySeconds + (input.Entrants.Count - 1) * perMatch;
                    var bosses = input.Entrants.Count(e => e.Role == "boss");
                    var challengers = input.Entrants.Where(e => e.Role == "challenger").ToList();
                    var entries = input.Format == "bossRoundRobin" ? bosses : bosses * challengers.Count;
                    seconds += (int)Math.Ceiling(entries / (double)SummaryPageSize) * (int)ArenaStandings.DisplaySeconds;
                    foreach (var e in challengers)
                    {
                        var value = InitialValue(input, e);
                        var levels = 1;
                        if (input.Format == "limitChallenge")
                        {
                            decimal? next;
                            while ((next = NextValue(input.Growth, value)).HasValue)
                            {
                                value = next.Value;
                                levels++;
                                // Reject impractical configurations before launch; this is not a runtime loss limit.
                                if ((long)levels * bosses * perMatch + seconds > int.MaxValue)
                                    throw new OverflowException();
                            }
                        }
                        seconds += levels * bosses * perMatch;
                    }
                    return seconds;
                }
            }
            catch (OverflowException) { throw new ScenarioConfigurationException("Challenge deadline exceeds the supported wait range; increase the growth step or lower maxValue"); }
        }

        public const int SummaryPageSize = 8;

        public static List<ArenaBossStanding> Standings(ArenaTournamentResult t)
            => t.Input.Entrants.Where(e => e.Role == "boss").Select(e => {
                var matches = t.Matches.Where(m => m.Left == e.Id).ToList();
                var wins = matches.Where(m => m.Winner == e.Id).ToList();
                var sum = wins.Sum(m => m.Score.Value);
                return new ArenaBossStanding { EntrantId = e.Id, Wins = wins.Count, Losses = matches.Count(m => m.Winner == m.Right),
                    Unresolved = t.Challenges.Count(p => p.Boss == e.Id && p.Status == "unresolvedDraw"), ScoreSum = sum,
                    AverageVictoryScore = wins.Count == 0 ? null : sum / wins.Count };
            }).ToList();

        public static List<string> SummaryEntries(ArenaTournamentResult t) => t.Input.Format == "bossRoundRobin"
            ? t.Input.Entrants.Where(e => e.Role == "boss").Select(e => e.Id).ToList()
            : t.Challenges.Select(p => p.Id).ToList();

        // Count growth repeats the frozen baseline members, preserving native loadouts.
        // Every baseline member gets floor(target/baseCount) copies, with the remainder
        // assigned in baseline order. Earlier snapshots are never mutated.
        public static ResolvedCombatRoster CountRoster(ResolvedCombatRoster baseline, int count, int seed)
        {
            var roster = Copy(baseline);
            roster.ResolvedCount = count;
            if (baseline.Members.Count > 0)
            {
                roster.Members.Clear();
                for (var i = 0; i < count; i++)
                {
                    var member = Copy(baseline.Members[i % baseline.Members.Count]);
                    if (i >= baseline.Members.Count) member.Seed = SquadTemplates.MemberSeed(seed, baseline.Id, i);
                    roster.Members.Add(member);
                }
                roster.UnitCombatPower = roster.ActualCombatPower / count;
            }
            return roster;
        }
    }

    public sealed class ArenaChallengeScheduler : IArenaSchedule
    {
        private readonly ArenaTournamentResult tournament;
        private int pairIndex;
        private int level;
        private decimal value;
        public string Champion => null;
        public ArenaMatch Active { get; private set; }
        public ArenaChallengeScheduler(ArenaTournamentResult tournament)
        {
            this.tournament = tournament;
            ArenaChallengeRules.Validate(tournament.Input);
            if (tournament.Input.Format == "knockout" || tournament.Challenges.Count != 0) throw new InvalidOperationException("Challenge scheduler requires a fresh challenge tournament");
            foreach (var challenger in tournament.Input.Entrants.Where(e => e.Role == "challenger"))
                foreach (var boss in tournament.Input.Entrants.Where(e => e.Role == "boss"))
                    tournament.Challenges.Add(new ArenaChallengePair { Id = tournament.Input.TestDefName + "/pair-" + (tournament.Challenges.Count + 1),
                        Boss = boss.Id, Challenger = challenger.Id, InitialValue = ArenaChallengeRules.InitialValue(tournament.Input, challenger) });
        }
        public ArenaMatch Next()
        {
            if (Active != null) throw new InvalidOperationException("Active challenge must resolve before advancing");
            if (pairIndex >= tournament.Challenges.Count) return null;
            var pair = tournament.Challenges[pairIndex];
            if (pair.Status == "pending") { value = pair.InitialValue; level = 0; pair.Status = "running"; }
            Active = new ArenaMatch { Id = tournament.Input.TestDefName + "/match-" + (tournament.Matches.Count + 1),
                PairId = pair.Id, Left = pair.Boss, Right = pair.Challenger, Round = pairIndex + 1, Level = level,
                Kind = tournament.Input.Format, ChallengeValue = value,
                Predecessors = pair.Matches.Count == 0 ? new() : new() { pair.Matches.Last() } };
            tournament.Matches.Add(Active); pair.Matches.Add(Active.Id);
            return Active;
        }
        public void Resolve(string winner, ArenaScore score)
        {
            if (Active == null || winner != Active.Left && winner != Active.Right || score == null || !score.IsRankable || score.Value < 0 || score.Value > 100)
                throw new InvalidOperationException("Invalid challenge result");
            Active.Winner = winner; Active.Score = score;
            var pair = tournament.Challenges[pairIndex];
            if (winner == pair.Challenger) { pair.WinningValue = value; Complete("won"); }
            else if (tournament.Input.Format == "bossRoundRobin") Complete("lost");
            else
            {
                var next = ArenaChallengeRules.NextValue(tournament.Input.Growth, value);
                if (next == null) { pair.LimitReason = "maxValue"; Complete("limitReached"); }
                else { value = next.Value; level++; Active = null; }
            }
        }
        public void ResolveDraw()
        {
            if (Active == null || Active.Attempts.Count != tournament.Input.MaxReplays + 1) throw new InvalidOperationException("Draw replay limit not reached");
            Complete("unresolvedDraw");
        }
        public void ReachRosterLimit(string reason, ResolvedCombatRoster rejected = null)
        {
            if (Active == null || Active.Level == 0 || Active.Attempts.Count != 0) throw new InvalidOperationException("Initial or already played roster cannot be skipped");
            var pair = tournament.Challenges[pairIndex];
            pair.Matches.RemoveAt(pair.Matches.Count - 1); tournament.Matches.RemoveAt(tournament.Matches.Count - 1);
            pair.LimitReason = reason;
            pair.LimitRoster = rejected;
            Complete("limitReached");
        }
        private void Complete(string status) { tournament.Challenges[pairIndex].Status = status; pairIndex++; Active = null; }
    }
}
