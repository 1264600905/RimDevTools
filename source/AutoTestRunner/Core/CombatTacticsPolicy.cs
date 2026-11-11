using System;

namespace LogSyncMod.AutoTest
{
    // Simulation-tick and operation limits, shared by both tournament tactics.
    // Cheap candidate scoring precedes the small LOS/reachability shortlist.
    internal static class CombatTacticsPolicy
    {
        internal const int BatchTicks = 3;
        internal const int DecisionsPerBatch = 12;
        internal const int ReachChecksPerBatch = 8;
        internal const int MovesPerBatch = 4;
        internal const int CandidateLimit = 25;
        internal const int ShortlistLimit = 3;
        internal const double BatchBudgetMilliseconds = 8.0;
        internal const int RoleLockTicks = 180;

        internal static bool Custom(string policy) => policy == "defendPoint" || policy == "lure";

        internal static int BaitDemand(int shooters, int melee, int approaching)
        {
            if (shooters < 2 || approaching == 0) return 0;
            return Math.Max(0, Math.Min((shooters + 3) / 4, approaching) - melee);
        }

        internal static bool ShouldEvade(float distance, float enemySpeed, bool pursued, bool bait, int supporters = 0, int enemies = int.MaxValue)
        {
            var threshold = enemies <= 2 && supporters >= 3
                ? Math.Max(5, enemySpeed * 1.25f + 1)
                : Math.Max(9, enemySpeed * (bait ? 2.4f : 2f) + 2);
            return distance < threshold && (pursued || distance < 2.5f);
        }

        internal static float FireDistance(float range, float minimum, bool meleeThreat)
            => Math.Max(minimum + 2, Math.Min(range - 1, range * (meleeThreat ? .82f : .9f)));

        internal static bool WorthMoving(float current, float candidate, bool urgent)
            => candidate > current + (urgent ? 1 : 7);

        internal static bool ShouldSpread(int shooters, int rangedEnemies, bool hasShot, int ticksSinceMove)
            => shooters > 1 && rangedEnemies > 0 && hasShot && ticksSinceMove >= 180;

        internal static bool ShouldBreakRangedExposure(int shooters, int melee, int rangedEnemies, bool exposed)
            => exposed && rangedEnemies > 0 && (shooters == 0 || melee > shooters || rangedEnemies > shooters * 2);
    }

    internal sealed class CombatTacticsBudget
    {
        internal int ReachChecks { get; private set; }
        internal int Moves { get; private set; }
        internal void Reset() { ReachChecks = 0; Moves = 0; }
        internal bool TakeReach()
        {
            if (ReachChecks >= CombatTacticsPolicy.ReachChecksPerBatch) return false;
            ReachChecks++; return true;
        }
        internal bool TakeMove()
        {
            if (Moves >= CombatTacticsPolicy.MovesPerBatch) return false;
            Moves++; return true;
        }
    }
}
