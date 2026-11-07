using System;

namespace LogSyncMod.AutoTest.Scenarios
{
    public sealed class GroupCombatCameraMotion
    {
        private bool initialized;
        public float X { get; private set; }
        public float Z { get; private set; }
        public float Size { get; private set; }

        public void Step(float targetX, float targetZ, float targetSize, float realDeltaSeconds)
        {
            if (!initialized)
            {
                X = targetX; Z = targetZ; Size = targetSize; initialized = true; return;
            }
            // Real time keeps the camera smooth while the simulation is paused. Cap long
            // focus/loading gaps so returning to the game cannot jump straight to a point.
            var dt = Math.Clamp(realDeltaSeconds, 0, .1f);
            var positionBlend = (float)(1 - Math.Exp(-dt / .5));
            var zoomBlend = (float)(1 - Math.Exp(-dt / .85));
            X += (targetX - X) * positionBlend;
            Z += (targetZ - Z) * positionBlend;
            Size += (targetSize - Size) * zoomBlend;
        }
    }
}
