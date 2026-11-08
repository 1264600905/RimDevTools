using System.Runtime.CompilerServices;
using Verse;

namespace LogSyncMod
{
    // Only fresh test pawns are registered. Weak keys let their normal cleanup own their lifetime.
    public static class TestPawnWorkRestrictions
    {
        private static readonly ConditionalWeakTable<Pawn, object> Unrestricted = new ConditionalWeakTable<Pawn, object>();

        public static void Remove(Pawn pawn)
        {
            if (pawn?.RaceProps?.Humanlike != true || Unrestricted.TryGetValue(pawn, out _)) return;
            Unrestricted.Add(pawn, new object());
            pawn.Notify_DisabledWorkTypesChanged();
        }

        internal static bool IsUnrestricted(Pawn pawn)
        {
            return pawn?.RaceProps?.Humanlike == true && Unrestricted.TryGetValue(pawn, out _);
        }
    }
}
