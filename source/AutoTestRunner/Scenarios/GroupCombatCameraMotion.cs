using System;

namespace LogSyncMod.AutoTest.Scenarios
{
    public sealed class GroupCombatCameraMotion
    {
        private bool initialized;
        public float X { get; private set; }
        public float Z { get; private set; }
        public float Size { get; private set; }

        public void Set(float x, float z, float size)
        {
            X = x; Z = z; Size = size; initialized = true;
        }

        // A small spatial quiet area filters recoil without waiting for a dwell timer.
        // Larger jumps (including teleports) move to the new framing immediately.
        public void Follow(float x, float z, float size)
        {
            var dx = x - X; var dz = z - Z;
            var distance = (float)Math.Sqrt(dx * dx + dz * dz);
            if (!initialized || distance >= 3) { Set(x, z, size); return; }
            const float quietRadius = .35f;
            if (distance > quietRadius)
            {
                X = x - dx / distance * quietRadius;
                Z = z - dz / distance * quietRadius;
            }
            Size = size;
        }

        public void Step(float targetX, float targetZ, float targetSize, float realDeltaSeconds,
            float positionSeconds = .5f, float maximumMoveSpeed = float.MaxValue, bool smoothZoom = true)
        {
            if (!initialized)
            {
                Set(targetX, targetZ, targetSize); return;
            }
            // Real time keeps the camera smooth while the simulation is paused. Cap long
            // focus/loading gaps so returning to the game cannot jump straight to a point.
            var dt = Math.Clamp(realDeltaSeconds, 0, .1f);
            var positionBlend = (float)(1 - Math.Exp(-dt / positionSeconds));
            var zoomBlend = (float)(1 - Math.Exp(-dt / .85));
            var dx = (targetX - X) * positionBlend; var dz = (targetZ - Z) * positionBlend;
            var distance = (float)Math.Sqrt(dx * dx + dz * dz);
            var limit = maximumMoveSpeed * dt;
            if (distance > limit && distance > 0) { dx *= limit / distance; dz *= limit / distance; }
            X += dx; Z += dz;
            Size = smoothZoom ? Size + (targetSize - Size) * zoomBlend : targetSize;
        }
    }
}
