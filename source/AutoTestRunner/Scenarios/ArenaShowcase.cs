#nullable disable
using System;
using System.Collections.Generic;

namespace LogSyncMod.AutoTest.Scenarios
{
    public sealed class ArenaEntrantProfile
    {
        public string Id { get; set; }
        public string PawnKind { get; set; }
        public string DisplayName { get; set; }
        public double CombatPower { get; set; }
        public double Health { get; set; }
        public double AttackDamage { get; set; }
        public double AttackSpeed { get; set; }
        public double SharpArmor { get; set; }
        public double BluntArmor { get; set; }
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
        public List<ArenaEntrantProfile> Entrants { get; set; } = new();
        public List<ArenaShowcasePage> Pages { get; set; } = new();
    }

    public static class ArenaShowcase
    {
        public const int PageSize = 5;
        public const double PageSeconds = 5;
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
    }
}
