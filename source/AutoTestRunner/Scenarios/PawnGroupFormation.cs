using System;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    // The same footprint must be used for validation, side exchange and spawn.
    // Combat blocks fill a vertical file before moving to the next file.
    public static class PawnGroupFormation
    {
        public static int ArenaRows(int count, int mapWidth)
        {
            // Prefer a broad, shallow block (about four columns per row), keeping
            // each army in its own side of the map with a central approach lane.
            var maxColumns = Math.Max(1, ((mapWidth * 2 / 5 - 3) / 2) + 1);
            return Math.Max((int)Math.Ceiling(Math.Sqrt(count) / 2), (count + maxColumns - 1) / maxColumns);
        }
        private static (int count, int length, int spacing) Dimensions(XElement actor)
        {
            var count = ScenarioLoader.I(actor, "count", 1);
            var length = ScenarioLoader.A(actor, "formation", "line") == "grid" ? ScenarioLoader.I(actor, "columns", 5) : count;
            var spacing = ScenarioLoader.I(actor, "spacing", 2);
            if (count < 1 || length < 1 || spacing < 1) throw new ScenarioConfigurationException("Formation dimensions must be positive");
            return (count, length, spacing);
        }
        public static (int x, int z) Offset(XElement actor, int index, bool verticalFirst)
        {
            var d = Dimensions(actor);
            if (index < 0 || index >= d.count) throw new ArgumentOutOfRangeException(nameof(index));
            var along = index % d.length * d.spacing;
            var across = index / d.length * d.spacing;
            return verticalFirst ? (across, along) : (along, across);
        }
        public static (int x, int z) Extents(XElement actor, bool verticalFirst)
        {
            var d = Dimensions(actor);
            var along = (Math.Min(d.count, d.length) - 1) * d.spacing;
            var across = (d.count - 1) / d.length * d.spacing;
            return verticalFirst ? (across, along) : (along, across);
        }
    }
}
