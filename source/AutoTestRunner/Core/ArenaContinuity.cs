using System;

namespace LogSyncMod.AutoTest
{
    // Optional restoration and observation must never decide whether an arena fight can run.
    public static class ArenaContinuity
    {
        public static bool TryOptional(string stage, Action action, Action<string> diagnostic)
        {
            try { action(); return true; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                diagnostic(stage + ": " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        public static bool PresentationReady(bool captured, double elapsedSeconds, Action<string> diagnostic)
        {
            if (captured) return true;
            if (elapsedSeconds < 20) return false;
            diagnostic("Screenshot unavailable after 20 seconds; continuing the tournament.");
            return true;
        }
    }
}
