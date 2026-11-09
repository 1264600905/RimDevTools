using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    // Shared by the draw-only HUD and camera: the battle fits between the side panels.
    public static class GroupCombatPresentation
    {
        public const float PanelTop = 116;
        public const float IntroDuration = 3.8f;
        public const float SettlementDuration = 3f;
        public const float MaximumProjectionSize = 28;
        public static float CameraSizeLimit(float uiHeight, float markerThreshold = 0)
            => Math.Min(MaximumProjectionSize, uiHeight / (2 * Math.Max(18, markerThreshold + 2)));
        public static string UnitTypeName(string label, string defName) => string.IsNullOrWhiteSpace(label) ? defName : label.Trim();
        public static (double output, double taken) SurvivorTotals(IEnumerable<GroupCombatUnit> units)
        {
            var living = units.Where(u => u.Alive).ToList();
            return (living.Sum(u => u.EnemyDamage), living.Sum(u => u.DamageTaken));
        }
        public static float PanelWidth(float width) => Math.Min(width * .3f, Math.Clamp(width * .22f, 172f, 330f) * 1.5f);
        public static float CompactPanelWidth(float width) => Math.Min(width * .2f, Math.Clamp(width * .14f, 156, 228));
        public static float PanelHeight(float height) => Math.Min(height - PanelTop - 160, 975);
        public static float ContentScale(float width, float height) => Math.Clamp(Math.Min(width / 330, height / 500), .75f, 1.5f);
        public static string CountdownLabel(float elapsedSeconds) => elapsedSeconds < 1 ? "3" : elapsedSeconds < 2 ? "2" : elapsedSeconds < 3 ? "1" : elapsedSeconds < IntroDuration ? "开始" : "";

        public static (float x, float z, float size) Frame(IEnumerable<(float x, float z)> positions,
            float width, float height, bool overlay, float minimumSize = 12, float? panelWidth = null, float bottomInset = 90, float maximumSize = float.MaxValue)
        {
            var points = positions.ToList();
            if (points.Count == 0) throw new ArgumentException("Camera requires at least one position");
            var left = overlay ? (panelWidth ?? PanelWidth(width)) + 22 : 16;
            var top = overlay ? PanelTop + 6 : 16;
            var bottom = bottomInset; // Leave native time controls and any camera toolbar visible.
            var usableWidth = Math.Max(1, width - left * 2);
            var usableHeight = Math.Max(1, height - top - bottom);
            var minX = points.Min(p => p.x); var maxX = points.Max(p => p.x);
            var minZ = points.Min(p => p.z); var maxZ = points.Max(p => p.z);
            var size = Math.Max(minimumSize, Math.Max((maxX - minX + 6) * height / (2 * usableWidth), (maxZ - minZ + 6) * height / (2 * usableHeight)));
            size = Math.Min(maximumSize, size);
            // The safe region is vertically offset when the header and bottom controls differ.
            return ((minX + maxX) / 2, (minZ + maxZ) / 2 + (top - bottom) * size / height, size);
        }

        public static float ZoomTarget(float currentSize, float requiredSize, float maximumSize = MaximumProjectionSize)
        {
            // Retain a one-unit reserve and a two-unit dead band. Both expansion and
            // contraction are allowed; small movements cannot repeatedly reverse zoom.
            return Math.Min(maximumSize, currentSize <= 0 || requiredSize > currentSize || requiredSize < currentSize - 2
                ? requiredSize + 1 : currentSize);
        }
    }
}
