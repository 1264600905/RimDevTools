using System;

namespace LogSyncMod.AutoTest.Scenarios
{
    // Treat the camera as a stationary observer until the action has actually moved.
    internal sealed class ArenaCameraPan
    {
        private bool initialized;
        private float candidateX, candidateZ, candidateSeconds;
        internal float X { get; private set; }
        internal float Z { get; private set; }
        internal int Retargets { get; private set; }

        internal void Reset(float x, float z)
        {
            X = x; Z = z; initialized = true; candidateSeconds = 0;
        }

        internal void Observe(float targetX, float targetZ, float currentX, float currentZ, float elapsed)
        {
            if (!initialized) Reset(currentX, currentZ);
            var dx = targetX - X; var dz = targetZ - Z;
            var distanceSquared = dx * dx + dz * dz;
            if (distanceSquared <= 3 * 3)
            {
                candidateSeconds = 0; return;
            }
            var pendingX = candidateX - X; var pendingZ = candidateZ - Z;
            var dot = dx * pendingX + dz * pendingZ;
            var directionChanged = dot <= 0 || dot * dot < .5f * distanceSquared * (pendingX * pendingX + pendingZ * pendingZ);
            dx = targetX - candidateX; dz = targetZ - candidateZ;
            // A running participant may leave the candidate's radius before the
            // dwell ends. Keep confirming travel in the same direction; reject reversals.
            if (candidateSeconds == 0 || dx * dx + dz * dz > 2 * 2 && directionChanged)
            {
                candidateX = targetX; candidateZ = targetZ; candidateSeconds = 0;
            }
            candidateSeconds += Math.Clamp(elapsed, 0, .1f);
            if (candidateSeconds < .75f) return;
            X = targetX; Z = targetZ; Retargets++;
            candidateSeconds = 0;
        }
    }
}
