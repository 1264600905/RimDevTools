using System;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    // The same footprint must be used for validation, side exchange and spawn.
    // Combat blocks fill a vertical file before moving to the next file.
    public static class PawnGroupFormation
    {
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
