using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    // Shared by the draw-only HUD and camera: the battle fits between the side panels.
    public static class GroupCombatPresentation
    {
        public static float PanelWidth(float width) => Math.Clamp(width * .22f, 172f, 330f);

        public static (float x, float z, float size) Frame(IEnumerable<(float x, float z)> positions,
            float width, float height, bool overlay, float minimumSize = 12)
        {
            var points = positions.ToList();
            if (points.Count == 0) throw new ArgumentException("Camera requires at least one position");
            var left = overlay ? PanelWidth(width) + 22 : 16;
            var top = overlay ? 94 : 16;
            var bottom = 90f; // Leave native time controls visible and usable.
            var usableWidth = Math.Max(1, width - left * 2);
            var usableHeight = Math.Max(1, height - top - bottom);
            var minX = points.Min(p => p.x); var maxX = points.Max(p => p.x);
            var minZ = points.Min(p => p.z); var maxZ = points.Max(p => p.z);
            var size = Math.Max(minimumSize, Math.Max((maxX - minX + 6) * height / (2 * usableWidth), (maxZ - minZ + 6) * height / (2 * usableHeight)));
            // The safe region is vertically offset when the header and bottom controls differ.
            return ((minX + maxX) / 2, (minZ + maxZ) / 2 + (top - bottom) * size / height, size);
        }

        public static float ZoomTarget(float currentSize, float requiredSize)
        {
            // Retain a one-unit reserve and a two-unit dead band. Both expansion and
            // contraction are allowed; small movements cannot repeatedly reverse zoom.
            return currentSize <= 0 || requiredSize > currentSize || requiredSize < currentSize - 2
                ? requiredSize + 1 : currentSize;
        }
    }
}
