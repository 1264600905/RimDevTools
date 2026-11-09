using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class ArenaBroadcastUI
    {
        internal static readonly Color Gold = new(1f, .77f, .35f);
        internal static readonly Color Muted = new(.58f, .66f, .76f);
        internal static readonly Color Boss = new(.99f, .49f, .32f);
        internal static readonly Color Challenger = new(.32f, .8f, .86f);
        internal static readonly Color Green = new(.49f, .9f, .65f);
        internal static float Scale => Mathf.Min(UI.screenWidth / 1600f, UI.screenHeight / 900f);
        internal static Rect R(float x, float y, float w, float h) => new(
            (UI.screenWidth - 1600 * Scale) / 2 + x * Scale,
            (UI.screenHeight - 900 * Scale) / 2 + y * Scale, w * Scale, h * Scale);
        internal static void Box(float x, float y, float w, float h, Color c) => Widgets.DrawBoxSolid(R(x, y, w, h), c);
        internal static void Label(float x, float y, float w, float h, string value, float size = 20, Color? color = null, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var previous = GUI.color;
            GUI.color = color ?? Color.white;
            // Fit long names without allowing them to collide with adjacent columns.
            var longest = 1;
            foreach (var line in (value ?? "").Split('\n')) longest = Mathf.Max(longest, line.Length);
            var fit = Mathf.Min(size, w / longest);
            GroupCombatOverlay.DrawText(R(x, y, w, h), value ?? "", Mathf.Max(10, fit) * Scale, anchor);
            GUI.color = previous;
        }
        internal static void Portrait(float x, float y, float size, string id, float elapsed = 0)
            => ArenaEntrantShowcase.DrawIdentity(R(x, y, size, size), id, elapsed);
        internal static void Background(string eyebrow, string title, string subtitle)
        {
            Widgets.DrawBoxSolid(new Rect(0, 0, UI.screenWidth, UI.screenHeight), new Color(.022f, .031f, .049f));
            Box(0, 0, 1600, 5, Gold);
            Box(64, 60, 32, 3, Gold);
            Label(112, 42, 1120, 36, eyebrow, 16, Gold);
            Label(64, 94, 1280, 64, title, 44);
            Label(66, 164, 1420, 36, subtitle, 19, Muted);
            if (Widgets.ButtonText(R(1390, 48, 146, 34), "取消赛事")) ArenaService.Cancel();
        }
        internal static void Panel(float x, float y, float w, float h, Color accent)
        {
            Box(x + 3, y + 6, w, h, new Color(0, 0, 0, .2f));
            Box(x, y, w, h, new Color(.055f, .073f, .103f));
            Box(x, y, w, 3, accent);
        }
    }
}
