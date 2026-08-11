using System;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class AutoTestSemanticUiAdapter
    {
        public static bool Close(Window window)
        {
            if (window == null) return true;
            try
            {
                window.Close(false);
                return true;
            }
            catch
            {
                try { return Find.WindowStack.TryRemove(window, false); } catch { return false; }
            }
        }

        public static bool ForceClose(Type windowType)
        {
            try { Find.WindowStack.TryRemoveAssignableFromType(windowType, false); return true; } catch { return false; }
        }
    }
}
