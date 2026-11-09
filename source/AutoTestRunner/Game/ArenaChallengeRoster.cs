using System.Linq;
using LogSyncMod.AutoTest.Scenarios;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class ArenaChallengeRoster
    {
        internal static void Prepare(ArenaTournamentResult tournament, ArenaMatch match)
        {
            if (match.ResolvedRosters.Count != 0) return;
            var input = tournament.Input;
            var boss = tournament.Rosters.Single(r => r.Id == match.Left);
            var baseline = tournament.Rosters.Single(r => r.Id == match.Right);
            var challenger = ArenaChallengeRules.Copy(baseline);
            if (input.Format == "limitChallenge" && match.Level > 0)
            {
                var seed = SquadTemplates.MemberSeed(input.Seed, match.PairId, match.Level);
                if (input.Growth.Target == "count")
                    challenger = ArenaChallengeRules.CountRoster(baseline, (int)match.ChallengeValue.Value, seed);
                else if (baseline.SquadId != null)
                {
                    challenger = SquadGeneration.Resolve(baseline.Id, input.SquadTemplates[baseline.SquadId], match.ChallengeValue,
                        input.Count, input.CostMode, seed, Find.CurrentMap.Tile);
                    challenger.DisplayName = baseline.DisplayName; challenger.Source = baseline.Source;
                }
                else
                {
                    challenger = CombatRoster.Resolve(baseline.Id, baseline.PawnKind, baseline.UnitCombatPower, match.ChallengeValue);
                    challenger.CostMode = baseline.CostMode; challenger.DisplayName = baseline.DisplayName; challenger.Source = baseline.Source;
                }
            }
            match.ResolvedRosters = new() { ArenaChallengeRules.Copy(boss), challenger };
            ArenaLoader.CompileBattle(input, match.ResolvedRosters, match, 0);
        }
    }
}
