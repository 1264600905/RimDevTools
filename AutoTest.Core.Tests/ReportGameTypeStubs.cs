// The report renderer only reads the production data models. These empty engine
// types let that exact renderer compile in game-independent, in-memory tests.
namespace Verse
{
    internal class ThingDef { }
    internal class TerrainDef { }
    internal class HediffDef { }
    internal class PawnKindDef { }
    internal static class Log
    {
        internal static void Error(string message) => throw new InvalidOperationException(message);
        internal static void Warning(string message) => throw new InvalidOperationException(message);
    }
}

namespace RimWorld
{
    internal class RecipeDef { }
    internal class GeneDef { }
    internal class XenotypeDef { }
}
