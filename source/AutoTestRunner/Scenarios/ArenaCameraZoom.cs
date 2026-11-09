using System;

namespace LogSyncMod.AutoTest.Scenarios
{
    // Expand when needed; only contract after the smaller framing has stayed stable.
    internal sealed class ArenaCameraZoom
    {
        private float candidate, stableSeconds;

        internal float Target(float current, float required, float maximum, float elapsed)
        {
            var desired = Math.Min(maximum, required + 1);
            if (current <= 0 || current > maximum || desired > current)
            {
                stableSeconds = 0; candidate = desired;
                return desired;
            }
            if (current - desired < 3)
            {
                stableSeconds = 0; return current;
            }
            if (Math.Abs(candidate - desired) > 1)
            {
                candidate = desired; stableSeconds = 0;
            }
            stableSeconds += Math.Clamp(elapsed, 0, .1f);
            if (stableSeconds < 5) return current;
            stableSeconds = 0;
            return Math.Max(desired, current - 2);
        }
    }
}
