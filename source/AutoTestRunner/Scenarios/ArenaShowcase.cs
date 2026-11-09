#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    public sealed class ArenaRulesPageResult
    {
        public double MinimumSeconds { get; set; } = ArenaPresentation.RulesSeconds;
        public double LoadingSeconds { get; set; }
        public double VisibleSeconds { get; set; }
        public bool Completed { get; set; }
        public string Screenshot { get; set; }
    }

    public sealed class ArenaEntrantProfile
    {
        public string Id { get; set; }
        public string PawnKind { get; set; }
        public string DisplayName { get; set; }
        public int Count { get; set; }
        public decimal TotalCombatPower { get; set; }
        public string Xenotype { get; set; }
        public List<ArenaEntrantProfile> Composition { get; set; } = new();
        public double CombatPower { get; set; }
        public double Health { get; set; }
        public double AttackDamage { get; set; }
        public double AttackSpeed { get; set; }
        public double SharpArmor { get; set; }
        public double BluntArmor { get; set; }
        public double NaturalSharpArmor { get; set; }
        public double NaturalBluntArmor { get; set; }
        public string ArmorSource { get; set; } = "native gear tab overall armor: body coverage and worn apparel";
        public List<string> WornApparel { get; set; } = new();
        public List<string> HediffsAfterGeneration { get; set; } = new();
        public List<string> HediffsAtSnapshot { get; set; } = new();
        public string PrimaryWeapon { get; set; }
        public bool PortraitRendered { get; set; }
        public bool SpawnedAtSnapshot { get; set; }
        public int InitializationTicks { get; set; }
        public double[] Values() => new[] { CombatPower, Health, AttackDamage, AttackSpeed, SharpArmor, BluntArmor };
    }

    public sealed class ArenaShowcasePageInput
    {
        public double Seconds { get; set; } = ArenaShowcase.PageSeconds;
        public string Caption { get; set; } = "";
        public List<string> EntrantIds { get; set; } = new();
    }

    public sealed class ArenaShowcasePage
    {
        public List<string> EntrantIds { get; set; } = new();
        public double DurationSeconds { get; set; }
        public string Caption { get; set; } = "";
        public string Screenshot { get; set; }
        public double VisibleSeconds { get; set; }
        public int SimulationTicks { get; set; }
    }

    public sealed class ArenaShowcaseResult
    {
        public double PageSeconds { get; set; } = ArenaShowcase.PageSeconds;
        public bool Completed { get; set; }
        public int InitializationTicks { get; set; }
        public bool PreviewCleanupComplete { get; set; }
        public List<ArenaEntrantProfile> Entrants { get; set; } = new();
        public List<ArenaShowcasePage> Pages { get; set; } = new();
        public List<ArenaCompositionSwitch> CompositionSwitches { get; set; } = new();
    }

    public sealed class ArenaCompositionSwitch
    {
        public string EntrantId { get; set; }
        public int Index { get; set; }
        public float Seconds { get; set; }
    }

    public sealed class ArenaPlacement
    {
        public int Rank { get; set; }
        public string EntrantId { get; set; }
        public int EliminationStage { get; set; }
        public decimal LastVictoryScore { get; set; }
        public int Wins { get; set; }
    }

    public sealed class ArenaTopFiveResult
    {
        public List<ArenaPlacement> Entries { get; set; } = new();
        public double DurationSeconds { get; set; } = ArenaStandings.DisplaySeconds;
        public double VisibleSeconds { get; set; }
        public int SimulationTicks { get; set; }
        public bool Completed { get; set; }
        public string Screenshot { get; set; }
    }

    public static class ArenaStandings
    {
        public const double DisplaySeconds = 10;
        public static List<ArenaPlacement> Build(ArenaTournamentResult tournament)
        {
            if (tournament.Champion == null || tournament.Matches.Count != tournament.Rosters.Count - 1 || tournament.Matches.Any(m => m.Winner == null))
                throw new InvalidOperationException("Standings require a completed knockout tournament");
            return tournament.Rosters.Select(roster => {
                var loss = tournament.Matches.SingleOrDefault(m => (m.Left == roster.Id || m.Right == roster.Id) && m.Winner != roster.Id);
                var wins = tournament.Matches.Where(m => m.Winner == roster.Id).ToList();
                return new ArenaPlacement {
                    EntrantId = roster.Id, EliminationStage = loss == null ? int.MaxValue : loss.Round * 2 + (loss.Kind == "extra" ? 1 : 0),
                    LastVictoryScore = wins.LastOrDefault()?.Score?.Value ?? 0, Wins = wins.Count
                };
            }).OrderByDescending(p => p.EliminationStage).ThenByDescending(p => p.LastVictoryScore)
                .ThenBy(p => ArenaScheduler.Stable(tournament.Input.Seed, p.EntrantId), StringComparer.Ordinal)
                .ThenBy(p => p.EntrantId, StringComparer.Ordinal).Take(5)
                .Select((p, index) => { p.Rank = index + 1; return p; }).ToList();
        }
    }

    public static class ArenaShowcase
    {
        public const int PageSize = 5;
        public const double PageSeconds = 5;
        public static int CompositionIndex(double elapsed, int count, int selected = 0, double selectedAt = 0)
            => count <= 1 ? 0 : (selected + (int)Math.Floor(Math.Max(0, elapsed - selectedAt))) % count;
        public static int PageCount(int count) => (count + PageSize - 1) / PageSize;
        public static double Duration(int count) => PageCount(count) * PageSeconds;
        public static double Fill(double value, double maximum) => double.IsFinite(value) && double.IsFinite(maximum) && maximum > 0
            ? Math.Clamp(value / maximum, 0, 1) : 0;
        public static int ProgressTier(double progress)
        {
            if (!double.IsFinite(progress) || progress < .2) return 0;
            if (progress < .4) return 1;
            if (progress < .6) return 2;
            if (progress < .8) return 3;
            return progress < .95 ? 4 : 5;
        }
        // Same layered, body-coverage-weighted summary used by the native Gear tab.
        public static double OverallArmor(double natural, IEnumerable<(double coverage, double[] layers)> parts)
        {
            var bodyUnprotected = 1 - Math.Clamp(natural / 2, 0, 1);
            return Math.Clamp(parts.Sum(part => part.coverage * (1 - part.layers.Aggregate(bodyUnprotected,
                (unprotected, armor) => unprotected * (1 - Math.Clamp(armor / 2, 0, 1))))) * 2, 0, 2);
        }
    }
}
