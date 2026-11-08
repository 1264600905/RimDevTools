#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    public sealed class GroupCombatRoundInput
    {
        public string CaseId { get; set; }
        public string TestDefName { get; set; }
        public string Name { get; set; }
        public int Seed { get; set; }
        public bool Swapped { get; set; }
        public string Xml { get; set; }
        public List<ResolvedCombatRoster> ResolvedRosters { get; set; } = new();
        public string Source { get; set; }
        public string InputHash { get; set; }
    }

    public sealed class GroupCombatUnit
    {
        public string Id { get; set; }
        public string NativeId { get; set; }
        public string Team { get; set; }
        public string Group { get; set; }
        public string GroupName { get; set; }
        public string Kind { get; set; }
        public string KindLabel { get; set; }
        public string Name { get; set; }
        public string State { get; set; } = "standing";
        public bool JoinedDuringBattle { get; set; }
        public int JoinedAtTick { get; set; }
        public bool PendingRevival { get; set; }
        public bool WasDowned { get; set; }
        public bool WasDead { get; set; }
        public bool CameraEligible => State == "standing" && !WasDowned && !WasDead;
        public int X { get; set; }
        public int Z { get; set; }
        public string InitialSnapshot { get; set; }
        public double EnemyDamage { get; set; }
        public double FriendlyDamage { get; set; }
        public double SelfDamage { get; set; }
        public double DamageTaken { get; set; }
        public int AliveTicks { get; set; }
        public double Dps { get; set; }
        public bool Alive => State == "standing";
    }

    public sealed class GroupCombatDamageEvent
    {
        public long Id { get; set; }
        public long? ParentId { get; set; }
        public int Tick { get; set; }
        public string SourceId { get; set; }
        public string VictimId { get; set; }
        public string SourceTeam { get; set; }
        public string VictimTeam { get; set; }
        public string DamageDef { get; set; }
        public string WeaponDef { get; set; }
        public string Channel { get; set; } = "pawn";
        public string Attribution { get; set; }
        public double Amount { get; set; }
        public double RawAmount { get; set; }
        public bool Absorbed { get; set; }
        public string Note { get; set; }
    }

    public sealed class GroupCombatDistribution
    {
        public int Count { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Mean { get; set; }
        public double Median { get; set; }
        public List<string> MinIds { get; set; } = new List<string>();
        public List<string> MaxIds { get; set; } = new List<string>();
        public static GroupCombatDistribution Calculate(IEnumerable<KeyValuePair<string, double>> values)
        {
            var ordered = values.OrderBy(v => v.Value).ThenBy(v => v.Key, StringComparer.Ordinal).ToList();
            if (ordered.Count == 0) return new GroupCombatDistribution();
            var count = ordered.Count;
            return new GroupCombatDistribution {
                Count = count, Min = ordered[0].Value, Max = ordered[count - 1].Value,
                Mean = ordered.Sum(v => v.Value) / count,
                Median = count % 2 == 0 ? (ordered[count / 2 - 1].Value + ordered[count / 2].Value) / 2 : ordered[count / 2].Value,
                MinIds = ordered.Where(v => v.Value == ordered[0].Value).Select(v => v.Key).ToList(),
                MaxIds = ordered.Where(v => v.Value == ordered[count - 1].Value).Select(v => v.Key).ToList()
            };
        }
    }

    public sealed class GroupCombatTeamResult
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Outcome { get; set; } = "undetermined";
        public int? EliminatedAtTick { get; set; }
        public int Initial { get; set; }
        public int Joined { get; set; }
        public int Total => Initial + Joined;
        public int Standing { get; set; }
        public int Downed { get; set; }
        public int Dead { get; set; }
        public int Escaped { get; set; }
        public int Removed { get; set; }
        public int Alive { get; set; }
        public int PendingRevival { get; set; }
        public double EnemyDamage { get; set; }
        public double UnrosteredEnemyDamage { get; set; }
        public double FriendlyDamage { get; set; }
        public double SelfDamage { get; set; }
        public double DamageTaken { get; set; }
        public double Dps { get; set; }
        public double MeanDps { get; set; }
        public double MeanOutput => Total > 0 ? EnemyDamage / Total : 0;
        public double MeanDamageTaken { get; set; }
        public GroupCombatDistribution Damage { get; set; }
        public GroupCombatDistribution DpsDistribution { get; set; }
        public GroupCombatDistribution Taken { get; set; }
    }

    public sealed class GroupCombatMatrixCell
    {
        public string Source { get; set; }
        public string Victim { get; set; }
        public string Channel { get; set; }
        public double Amount { get; set; }
        public long Events { get; set; }
    }

    public sealed class GroupCombatRoundResult
    {
        public string CaseId { get; set; }
        public string TestDefName { get; set; }
        public string Name { get; set; }
        public int Seed { get; set; }
        public bool Swapped { get; set; }
        public string Source { get; set; }
        public string InputHash { get; set; }
        public List<ResolvedCombatRoster> ResolvedRosters { get; set; } = new();
        public string ExecutionStatus { get; set; } = "preparing";
        public string EndReason { get; set; }
        public string Error { get; set; }
        public CombatStartupTiming StartupTiming { get; set; }
        public int ElapsedTicks { get; set; }
        public double SimulatedSeconds { get; set; }
        public string MeasurementQuality { get; set; } = "partial";
        public bool MeasurementRequirementsMet { get; set; }
        public bool RequireCompleteAttribution { get; set; }
        public List<string> MeasurementLimits { get; set; } = new List<string>();
        public List<string> Diagnostics { get; set; } = new List<string>();
        public List<GroupCombatUnit> Units { get; set; } = new List<GroupCombatUnit>();
        public List<GroupCombatTeamResult> Teams { get; set; } = new List<GroupCombatTeamResult>();
        public List<GroupCombatMatrixCell> Matrix { get; set; } = new List<GroupCombatMatrixCell>();
        public List<GroupCombatDamageEvent> Events { get; set; } = new List<GroupCombatDamageEvent>();
        public long TotalEvents { get; set; }
        public long DroppedDetailEvents { get; set; }
        public bool CleanupComplete { get; set; }
        public int MapId { get; set; }
        public bool IsPocketMap { get; set; }
        public string GeneratorDef { get; set; }
        public string Environment { get; set; }
        public int ScreenshotWidth { get; set; }
        public int ScreenshotHeight { get; set; }
        public List<string> Screenshots { get; set; } = new List<string>();
    }

    public sealed class CombatStartupTiming
    {
        // Arena times are relative to the countdown deadline; preparation can be negative.
        public double CountdownSeconds { get; set; }
        public double AdvanceStartSeconds { get; set; }
        public double? StartLabelSeconds { get; set; }
        public List<CombatCountdownObservation> CountdownLabels { get; set; } = new();
        public string ConfiguredSpeed { get; set; }
        public float TickRateMultiplier { get; set; }
        public int UpdateThreadId { get; set; }
        public int UiThreadId { get; set; }
        public int TickThreadId { get; set; }
        public double? InitialCaptureRequestedSeconds { get; set; }
        public double InitialCaptureCallSeconds { get; set; }
        public double? InitialCaptureReadySeconds { get; set; }
        public double? SpeedSwitchSeconds { get; set; }
        public double StartMeasurementSeconds { get; set; }
        public double? FirstTickSeconds { get; set; }
        public double? FirstMovementSeconds { get; set; }
        public double? FirstRepaintAfterSpeedSeconds { get; set; }
        public double ObservedRunningSeconds { get; set; }
        public CombatStartupWindow Waiting { get; set; } = new();
        public CombatStartupWindow Running { get; set; } = new();
    }

    public sealed class CombatCountdownObservation
    {
        public string Label { get; set; }
        public double Seconds { get; set; }
        public int ElapsedTicks { get; set; }
    }

    public sealed class CombatStartupWindow
    {
        public int Updates { get; set; }
        public int Repaints { get; set; }
        public int Ticks { get; set; }
        public double MaxUpdateGapSeconds { get; set; }
        public double MaxRepaintGapSeconds { get; set; }
        public double MaxUpdateSeconds { get; set; }
        public double MaxUiSeconds { get; set; }
        public double MaxTickSeconds { get; set; }
    }

    public sealed class GroupCombatExperimentSummary
    {
        public string TestDefName { get; set; }
        public string Team { get; set; }
        public int CompletedRounds { get; set; }
        public int Wins { get; set; }
        public int Draws { get; set; }
        public int Losses { get; set; }
        public double WinRate { get; set; }
        public GroupCombatDistribution RoundTeamDps { get; set; }
        public GroupCombatDistribution RoundAliveCount { get; set; }
        public GroupCombatDistribution PooledUnitDps { get; set; }
        public GroupCombatDistribution PooledUnitDamageTaken { get; set; }
        public static List<GroupCombatExperimentSummary> Calculate(IEnumerable<GroupCombatRoundResult> rounds)
        {
            return rounds.Where(r => r.ExecutionStatus == "completed").SelectMany(r => r.Teams.Select(t => new { round = r, team = t }))
                .GroupBy(p => new { p.round.TestDefName, p.team.Id }).Select(g => new GroupCombatExperimentSummary {
                    TestDefName = g.Key.TestDefName, Team = g.Key.Id, CompletedRounds = g.Count(), Wins = g.Count(p => p.team.Outcome == "win"), Draws = g.Count(p => p.team.Outcome == "draw"), Losses = g.Count(p => p.team.Outcome == "loss"), WinRate = g.Count(p => p.team.Outcome == "win") / (double)g.Count(),
                    RoundTeamDps = GroupCombatDistribution.Calculate(g.Select(p => new KeyValuePair<string, double>(p.round.CaseId, p.team.Dps))),
                    RoundAliveCount = GroupCombatDistribution.Calculate(g.Select(p => new KeyValuePair<string, double>(p.round.CaseId, p.team.Alive))),
                    PooledUnitDps = GroupCombatDistribution.Calculate(g.SelectMany(p => p.round.Units.Where(u => u.Team == p.team.Id).Select(u => new KeyValuePair<string, double>(p.round.CaseId + "/" + u.Id, u.Dps)))),
                    PooledUnitDamageTaken = GroupCombatDistribution.Calculate(g.SelectMany(p => p.round.Units.Where(u => u.Team == p.team.Id).Select(u => new KeyValuePair<string, double>(p.round.CaseId + "/" + u.Id, u.DamageTaken))))
                }).ToList();
        }
    }

    public sealed class GroupCombatRunReport
    {
        public int SchemaVersion { get; set; } = 1;
        public string Purpose { get; set; } = "groupcombat";
        public string RunId { get; set; }
        public string GameVersion { get; set; }
        public string Status { get; set; } = "running";
        public string StartedAtUtc { get; set; }
        public string CompletedAtUtc { get; set; }
        public string Error { get; set; }
        public List<ScenarioInput> Inputs { get; set; } = new List<ScenarioInput>();
        public List<GroupCombatRoundInput> PlannedRounds { get; set; } = new List<GroupCombatRoundInput>();
        public List<GroupCombatRoundResult> Rounds { get; set; } = new List<GroupCombatRoundResult>();
        public GroupCombatRoundResult ActiveRound { get; set; }
        public List<GroupCombatExperimentSummary> Experiments { get; set; } = new List<GroupCombatExperimentSummary>();
        public List<string> LoadedMods { get; set; } = new List<string>();
    }
}
