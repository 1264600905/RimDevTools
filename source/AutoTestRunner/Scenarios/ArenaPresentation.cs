using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    public static class ArenaPresentation
    {
        public const float RevealBSeconds = .65f;
        public const float AdvanceSeconds = 1.3f;
        public const float AnimationSeconds = 2.4f;
        public const float PostBattleSeconds = 5.5f;

        public static float Progress(float elapsed, float start, float duration)
        {
            var t = Math.Clamp((elapsed - start) / duration, 0, 1);
            return t * t * (3 - 2 * t);
        }

        public static Dictionary<string, float> IdentityHues(IEnumerable<string> pawnKinds)
        {
            var kinds = pawnKinds.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var hues = new Dictionary<string, float>(StringComparer.Ordinal);
            var gap = .65f / Math.Max(24, kinds.Count);
            foreach (var kind in kinds)
            {
                uint hash = 2166136261;
                foreach (var c in kind) hash = unchecked((hash ^ c) * 16777619);
                var hue = (hash % 3600) / 3600f;
                // Resolve nearby hues once per tournament, independent of A/B
                // positions and roster enumeration order.
                for (var i = 0; i < 3600 && hues.Values.Any(h => Math.Min(Math.Abs(h - hue), 1 - Math.Abs(h - hue)) < gap); i++)
                    hue = (hue + 1f / 3600) % 1;
                hues.Add(kind, hue);
            }
            return hues;
        }
    }
}
