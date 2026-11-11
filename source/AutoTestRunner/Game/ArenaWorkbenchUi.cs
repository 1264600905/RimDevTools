using UnityEngine;
using Verse;
using RimWorld;

namespace LogSyncMod.AutoTest
{
    internal static class ArenaWorkbenchUi
    {
        internal static readonly Color Background = new(.075f, .093f, .106f);
        internal static readonly Color PanelColor = new(.105f, .133f, .153f);
        internal static readonly Color Field = new(.075f, .103f, .12f);
        internal static readonly Color Border = new(.22f, .28f, .31f);
        internal static readonly Color Ink = new(.89f, .94f, .95f);
        internal static readonly Color Muted = new(.58f, .69f, .74f);
        internal static readonly Color Gold = new(.88f, .73f, .44f);
        internal static readonly Color Cyan = new(.48f, .76f, .8f);
        internal static void Box(Rect rect, Color fill, Color? border = null)
        {
            Widgets.DrawBoxSolid(rect, fill);
            if (!border.HasValue) return;
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width, 1), border.Value);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1, rect.width, 1), border.Value);
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 1, rect.height), border.Value);
            Widgets.DrawBoxSolid(new Rect(rect.xMax - 1, rect.y, 1, rect.height), border.Value);
        }
        internal static void Panel(Rect rect) => Box(rect, PanelColor);
        internal static void Label(Rect rect, string text, Color? color = null, GameFont font = GameFont.Small, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var c = GUI.color; var f = Text.Font; var a = Text.Anchor;
            GUI.color = color ?? Ink; Text.Font = font; Text.Anchor = anchor;
            Widgets.LabelEllipses(rect, text ?? "");
            GUI.color = c; Text.Font = f; Text.Anchor = a;
        }
        internal static bool Button(Rect rect, string text, bool selected = false, bool active = true)
        {
            var enabled = active && GUI.enabled;
            var hover = enabled && Mouse.IsOver(rect);
            var fill = selected ? new Color(.22f, .205f, .16f) : hover ? new Color(.18f, .235f, .27f) : new Color(.135f, .175f, .202f);
            if (!enabled) fill = new Color(.09f, .12f, .135f);
            Box(rect, fill, selected ? Gold : Border);
            Label(rect.ContractedBy(5), text, !enabled ? Muted * .65f : selected ? Gold : Ink, anchor: TextAnchor.MiddleCenter);
            return enabled && Widgets.ButtonInvisible(rect);
        }
        internal static void DefIcon(Rect rect, Def def)
        {
            if (def == null) return;
            var old = GUI.color; GUI.color = Color.white;
            try
            {
                if (def is FactionDef faction) GUI.DrawTexture(rect, faction.FactionIcon, ScaleMode.ScaleToFit);
                else if (def is AbilityDef ability) GUI.DrawTexture(rect, ability.uiIcon, ScaleMode.ScaleToFit);
                else if (def is PawnKindDef kind) Widgets.ThingIcon(rect, kind.race);
                else Widgets.DefIcon(rect, def);
            }
            catch { Label(rect, "·", Muted, anchor: TextAnchor.MiddleCenter); }
            finally { GUI.color = old; }
        }
    }
}
