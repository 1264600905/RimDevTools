#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    public sealed class ArenaPlan
    {
        public int SchemaVersion { get; set; } = 1;
        public string Purpose { get; set; } = "arena";
        public int SkippedFiles { get; set; }
        public List<ScenarioInput> Inputs { get; set; } = new();
        public List<string> SelectedTestDefNames { get; set; } = new();
        public List<ArenaTournamentInput> Tournaments { get; set; } = new();
        public int DefaultDeadlineSeconds => checked(Tournaments.Sum(t => 180 + (int)ArenaStandings.DisplaySeconds + (int)Math.Ceiling(t.ShowcasePages.Sum(p => p.Seconds)) + (t.Entrants.Count - 1) * (t.MaxReplays + 1) * (t.TimeoutRealSeconds + (int)Math.Ceiling(t.BracketSeconds + t.SettlementSeconds + ArenaPresentation.PostBattleSeconds) + 45)));
    }
    public sealed class ArenaTournamentInput
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string TestDefName { get; set; }
        public string Xml { get; set; }
        public string Source { get; set; }
        public string InputHash { get; set; }
        public int Seed { get; set; } = 12345;
        public string RosterMode { get; set; }
        public decimal? Budget { get; set; }
        public int Count { get; set; } = 20;
        public int MaxReplays { get; set; } = 2;
        public int ConfirmationTicks { get; set; } = 300;
        public int TimeoutRealSeconds { get; set; } = 300;
        public double DurationSeconds { get; set; } = 180;
        public double BracketSeconds { get; set; } = 3;
        public double AdvanceStartSeconds { get; set; } = 3;
        public double SettlementSeconds { get; set; } = 3;
        public double ShowcasePageSeconds { get; set; } = ArenaShowcase.PageSeconds;
        public List<ArenaShowcasePageInput> ShowcasePages { get; set; } = new();
        public decimal SurvivalWeight { get; set; } = 45;
        public decimal SuppressionWeight { get; set; } = 25;
        public decimal ExchangeWeight { get; set; } = 20;
        public decimal SpeedWeight { get; set; } = 10;
        public decimal TimeReferenceSeconds { get; set; } = 180;
        public List<ArenaEntrant> Entrants { get; set; } = new();
    }
    public sealed class ArenaEntrant
    {
        public string Id { get; set; }
        public string PawnKind { get; set; }
        public string Name { get; set; }
        public int? Count { get; set; }
        public decimal? Budget { get; set; }
    }
    public sealed class ArenaScore
    {
        public string Algorithm { get; set; }
        public int Initial { get; set; }
        public int Standing { get; set; }
        public int Downed { get; set; }
        public int EnemyInitial { get; set; }
        public int EnemyStanding { get; set; }
        public int ElapsedTicks { get; set; }
        public int EffectiveElapsedTicks { get; set; }
        public double EnemyDamage { get; set; }
        public double DamageTaken { get; set; }
        public decimal PreservationRatio { get; set; }
        public decimal SuppressionRatio { get; set; }
        public decimal ExchangeRatio { get; set; }
        public decimal SpeedFactor { get; set; }
        public decimal PreservationPoints { get; set; }
        public decimal SuppressionPoints { get; set; }
        public decimal ExchangePoints { get; set; }
        public decimal SpeedPoints { get; set; }
        public string DamageQuality { get; set; }
        public bool NeutralExchange { get; set; }
        public bool IsRankable { get; set; }
        public decimal Value { get; set; }
        public static ArenaScore Calculate(ArenaTournamentInput input, GroupCombatRoundResult fight)
        {
            if (input.TimeReferenceSeconds <= 0 || new[] { input.SurvivalWeight, input.SuppressionWeight, input.ExchangeWeight, input.SpeedWeight }.Any(w => w < 0 || w > 100)
                || input.SurvivalWeight + input.SuppressionWeight + input.ExchangeWeight + input.SpeedWeight != 100)
                throw new ScenarioConfigurationException("Arena score weights must total 100 with a positive time reference");
            var winner = fight.Teams.Single(t => t.Outcome == "win");
            var enemy = fight.Teams.Single(t => t.Outcome == "loss");
            var originals = fight.Units.Where(u => u.Team == winner.Id && !u.JoinedDuringBattle).ToList();
            var opponents = fight.Units.Where(u => u.Team == enemy.Id && !u.JoinedDuringBattle).ToList();
            if (fight.Teams.Count != 2 || winner.Initial < 1 || enemy.Initial < 1 || originals.Count != winner.Initial || opponents.Count != enemy.Initial || fight.ElapsedTicks < 0
                || !double.IsFinite(winner.EnemyDamage) || !double.IsFinite(winner.DamageTaken) || winner.EnemyDamage < 0 || winner.DamageTaken < 0)
                throw new ScenarioConfigurationException("Invalid arena score components");
            var ticks = fight.EndReason == "allUnavailable" && enemy.EliminatedAtTick.HasValue ? enemy.EliminatedAtTick.Value : fight.ElapsedTicks;
            if (ticks < 0 || ticks > fight.ElapsedTicks) throw new ScenarioConfigurationException("Invalid arena elimination clock");
            var score = new ArenaScore {
                Algorithm = "victory-quality-v2", Initial = winner.Initial,
                Standing = originals.Count(u => u.State == "standing"), Downed = originals.Count(u => u.State == "downed"),
                EnemyInitial = enemy.Initial, EnemyStanding = opponents.Count(u => u.State == "standing"),
                ElapsedTicks = fight.ElapsedTicks, EffectiveElapsedTicks = ticks,
                EnemyDamage = winner.EnemyDamage, DamageTaken = winner.DamageTaken,
                DamageQuality = fight.MeasurementQuality,
                IsRankable = fight.MeasurementRequirementsMet && (fight.MeasurementQuality == "partial" || fight.MeasurementQuality == "complete"),
                NeutralExchange = winner.EnemyDamage == 0 && winner.DamageTaken == 0
            };
            score.PreservationRatio = (score.Standing + .25m * score.Downed) / score.Initial;
            score.SuppressionRatio = 1 - (decimal)score.EnemyStanding / score.EnemyInitial;
            // Canonicalize to the game's JSON precision, then divide in decimal so
            // round-tripped evidence and runtime scheduling use identical arithmetic.
            var dealt = double.Parse(score.EnemyDamage.ToString("G15", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            var taken = double.Parse(score.DamageTaken.ToString("G15", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (!double.IsFinite(dealt)) dealt = score.EnemyDamage;
            if (!double.IsFinite(taken)) taken = score.DamageTaken;
            var largest = Math.Max(dealt, taken);
            var scale = largest > 1e20 || largest > 0 && largest < 1e-10 ? largest : 1;
            var dealtDecimal = decimal.Parse((dealt / scale).ToString("G15", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
            var takenDecimal = decimal.Parse((taken / scale).ToString("G15", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
            score.ExchangeRatio = score.NeutralExchange ? .5m : dealtDecimal / (dealtDecimal + takenDecimal);
            score.SpeedFactor = Math.Max(0, 1 - (decimal)ticks / 60 / input.TimeReferenceSeconds);
            score.PreservationPoints = input.SurvivalWeight * score.PreservationRatio;
            score.SuppressionPoints = input.SuppressionWeight * score.SuppressionRatio;
            score.ExchangePoints = input.ExchangeWeight * score.ExchangeRatio;
            score.SpeedPoints = input.SpeedWeight * score.SpeedFactor;
            score.Value = score.PreservationPoints + score.SuppressionPoints + score.ExchangePoints + score.SpeedPoints;
            return score;
        }
    }
    public sealed class ArenaMatch
    {
        public string Id { get; set; }
        public int Round { get; set; }
        public string Kind { get; set; }
        public string Left { get; set; }
        public string Right { get; set; }
        public string[] PairKey => ArenaScheduler.Pair(Left, Right);
        public string Winner { get; set; }
        public ArenaScore Score { get; set; }
        public List<string> Predecessors { get; set; } = new();
        public List<GroupCombatRoundResult> Attempts { get; set; } = new();
        public List<string> BracketScreenshots { get; set; } = new();
        public List<string> PostBattleScreenshots { get; set; } = new();
    }
    public sealed class ArenaTournamentResult
    {
        public ArenaTournamentInput Input { get; set; }
        public List<ResolvedCombatRoster> Rosters { get; set; } = new();
        public ArenaShowcaseResult Showcase { get; set; }
        public ArenaTopFiveResult TopFive { get; set; }
        public List<ArenaMatch> Matches { get; set; } = new();
        public string Status { get; set; } = "running";
        public string Champion { get; set; }
        public string ChampionScreenshot { get; set; }
        public string Error { get; set; }
    }
    public sealed class ArenaRunReport
    {
        public int SchemaVersion { get; set; } = 1;
        public string Purpose { get; set; } = "arena";
        public string RunId { get; set; }
        public string GameVersion { get; set; }
        public string Status { get; set; } = "running";
        public string StartedAtUtc { get; set; }
        public string CompletedAtUtc { get; set; }
        public string Error { get; set; }
        public string ErrorCode { get; set; }
        public ArenaPlan Plan { get; set; }
        public List<string> LoadedMods { get; set; } = new();
        public List<ArenaTournamentResult> Tournaments { get; set; } = new();
    }

    // Scheduling knows no Pawn objects, maps, clocks or presentation state.
    public sealed class ArenaScheduler
    {
        private readonly ArenaTournamentResult tournament;
        private List<string> pool;
        private readonly Queue<ArenaMatch> queued = new();
        private readonly Dictionary<string, decimal> scores = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> lastMatch = new(StringComparer.Ordinal);
        private int round;
        private bool qualification;
        public string Champion { get; private set; }
        public ArenaMatch Active { get; private set; }
        public ArenaScheduler(ArenaTournamentResult tournament)
        {
            this.tournament = tournament;
            pool = tournament.Rosters.Select(r => r.Id).ToList();
            if (pool.Count < 2 || pool.Distinct(StringComparer.Ordinal).Count() != pool.Count) throw new ScenarioConfigurationException("Arena needs distinct entrants");
            qualification = pool.Count % 2 == 1;
        }
        public static string[] Pair(string a, string b) => StringComparer.Ordinal.Compare(a, b) < 0 ? new[] { a, b } : new[] { b, a };
        public static string Stable(params object[] parts)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts)))).Replace("-", "");
        }
        public static int AttemptSeed(ArenaTournamentInput input, ArenaMatch match, int attempt) => unchecked((int)uint.Parse(Stable(input.Seed, match.PairKey, attempt).Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        private string Tie(string a, string b) => Stable(tournament.Input.Seed, Pair(a, b));
        private string[] Closest(List<string> ids) => ids.SelectMany((a, i) => ids.Skip(i + 1).Select(b => Pair(a, b)))
            .OrderBy(p => Math.Abs(tournament.Rosters.Single(r => r.Id == p[0]).ResolvedCount - tournament.Rosters.Single(r => r.Id == p[1]).ResolvedCount))
            .ThenBy(p => Tie(p[0], p[1]), StringComparer.Ordinal).ThenBy(p => p[0], StringComparer.Ordinal).ThenBy(p => p[1], StringComparer.Ordinal).First();
        private ArenaMatch Make(string[] pair, string kind)
        {
            var leftFirst = (uint.Parse(Tie(pair[0], pair[1]).Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture) & 1) == 0;
            return new ArenaMatch { Id = tournament.Input.TestDefName + "/match-" + (tournament.Matches.Count + queued.Count + 1), Round = round, Kind = kind, Left = pair[leftFirst ? 0 : 1], Right = pair[leftFirst ? 1 : 0], Predecessors = pair.Where(lastMatch.ContainsKey).Select(id => lastMatch[id]).Distinct().ToList() };
        }
        public ArenaMatch Next()
        {
            if (Active != null) throw new InvalidOperationException("Active match must resolve before advancing");
            if (Champion != null) return null;
            if (queued.Count == 0)
            {
                if (pool.Count == 1) { Champion = pool[0]; return null; }
                if (qualification) { queued.Enqueue(Make(Closest(pool), "qualification")); qualification = false; }
                else if (pool.Count % 2 == 1)
                {
                    var lowest = pool.OrderBy(id => scores[id]).ThenBy(id => Stable(tournament.Input.Seed, id), StringComparer.Ordinal).ThenBy(id => id, StringComparer.Ordinal).Take(2).ToArray();
                    queued.Enqueue(Make(Pair(lowest[0], lowest[1]), "extra"));
                }
                else
                {
                    round++;
                    var remaining = pool.ToList();
                    while (remaining.Count > 0) { var pair = Closest(remaining); queued.Enqueue(Make(pair, "round")); remaining.Remove(pair[0]); remaining.Remove(pair[1]); }
                }
            }
            Active = queued.Dequeue(); tournament.Matches.Add(Active); return Active;
        }
        public void Resolve(string winner, ArenaScore score)
        {
            if (Active == null || winner != Active.Left && winner != Active.Right || score == null || !score.IsRankable || score.Value < 0 || score.Value > 100) throw new InvalidOperationException("Invalid advancement or unrankable arena score");
            Active.Winner = winner; Active.Score = score;
            pool.Remove(winner == Active.Left ? Active.Right : Active.Left);
            scores[winner] = score.Value; lastMatch[winner] = Active.Id;
            Active = null;
        }
    }

    public sealed class ArenaEndRule
    {
        private readonly Dictionary<string, int> since = new();
        private readonly Dictionary<string, int> totals = new();
        private readonly int confirmation;
        public ArenaEndRule(int confirmation) { this.confirmation = confirmation; }
        public bool Evaluate(GroupCombatRoundResult current, int durationTicks)
        {
            foreach (var t in current.Teams)
            {
                if (totals.TryGetValue(t.Id, out var previous) && previous != t.Total) since.Remove(t.Id);
                totals[t.Id] = t.Total;
                var defeated = t.Total > 0 && t.Standing == 0 && t.PendingRevival == 0;
                if (!defeated) since.Remove(t.Id);
                else if (!since.ContainsKey(t.Id)) since[t.Id] = current.ElapsedTicks;
                t.EliminatedAtTick = since.TryGetValue(t.Id, out var tick) ? tick : null;
            }
            var eliminated = current.Teams.Where(t => since.ContainsKey(t.Id) && current.ElapsedTicks - since[t.Id] >= confirmation).ToList();
            List<GroupCombatTeamResult> winners;
            if (eliminated.Count > 0 && !current.Teams.Any(t => since.ContainsKey(t.Id) && current.ElapsedTicks - since[t.Id] < confirmation))
            {
                winners = current.Teams.Except(eliminated).ToList(); current.EndReason = "allUnavailable";
            }
            else if (current.ElapsedTicks >= durationTicks)
            {
                var a = current.Teams[0]; var b = current.Teams[1];
                var nA = current.Units.Count(u => u.Team == a.Id && !u.JoinedDuringBattle && u.State == "standing");
                var nB = current.Units.Count(u => u.Team == b.Id && !u.JoinedDuringBattle && u.State == "standing");
                var comparison = ((long)nA * b.Initial).CompareTo((long)nB * a.Initial);
                winners = comparison > 0 ? new() { a } : comparison < 0 ? new() { b } : new() { a, b }; current.EndReason = "timeLimitRatio";
            }
            else return false;
            foreach (var t in current.Teams) t.Outcome = winners.Count != 1 ? "draw" : winners.Contains(t) ? "win" : "loss";
            return true;
        }
    }
}
