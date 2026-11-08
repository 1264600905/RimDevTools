using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class ArenaEntrantShowcase
    {
        private static ArenaTournamentResult tournament;
        private static double[] maxima;
        private static Dictionary<string, float> hues;
        private static readonly string[] Labels = { "战力 · CP", "血量", "攻击伤害", "攻击速度", "利器护甲", "钝器护甲" };
        private static readonly Color Muted = new(.57f, .64f, .73f);
        private static readonly Color Gold = new(1f, .79f, .4f);
        private static readonly Color[] GradeColors = {
            new(.56f, .60f, .65f), new(.9f, .93f, .97f), new(.34f, .84f, .49f),
            new(.31f, .65f, 1f), new(.73f, .48f, .98f), new(1f, .62f, .22f)
        };
        private static readonly string[] GradeLabels = { "粗糙 <20%", "普通 20–40%", "优秀 40–60%", "稀有 60–80%", "史诗 80–95%", "传说 ≥95%" };

        internal static void Begin(ArenaTournamentResult value)
        {
            tournament = value;
            var showcase = value.Showcase = new ArenaShowcaseResult { PageSeconds = value.Input.ShowcasePageSeconds };
            // Preview pawns never enter a map or the world-pawn pool. Isolate their
            // native generation and stat calculations from subsequent battle RNG.
            Rand.PushState(value.Input.Seed);
            try
            {
                foreach (var roster in value.Rosters)
                {
                    var kind = DefDatabase<PawnKindDef>.GetNamed(roster.PawnKind);
                    Pawn pawn = null;
                    try
                    {
                        var age = kind.RaceProps.lifeStageAges.Last().minAge;
                        pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, forceGenerateNewPawn: true,
                            canGeneratePawnRelations: false, fixedBiologicalAge: age));
                        var attacks = pawn.meleeVerbs.GetUpdatedAvailableVerbsList(terrainTools: false)
                            .Where(v => v.IsMeleeAttack).ToList();
                        var totalWeight = attacks.Sum(v => v.GetSelectionWeight(null));
                        var damage = totalWeight > 0 ? attacks.Sum(v => v.GetSelectionWeight(null) / totalWeight
                            * v.verb.verbProps.AdjustedMeleeDamageAmount(v.verb, pawn)) : 0;
                        var cooldown = totalWeight > 0 ? attacks.Sum(v => v.GetSelectionWeight(null) / totalWeight
                            * v.verb.verbProps.AdjustedCooldownTicks(v.verb, pawn)) / 60f : 0;
                        showcase.Entrants.Add(new ArenaEntrantProfile {
                            Id = roster.Id, PawnKind = roster.PawnKind, DisplayName = roster.DisplayName,
                            CombatPower = (double)roster.UnitCombatPower, AttackDamage = damage,
                            Health = pawn.RaceProps.body.AllParts.Sum(part => part.def.GetMaxHealth(pawn)),
                            AttackSpeed = cooldown > 0 ? 1 / cooldown : 0,
                            SharpArmor = pawn.GetStatValue(StatDefOf.ArmorRating_Sharp),
                            BluntArmor = pawn.GetStatValue(StatDefOf.ArmorRating_Blunt)
                        });
                    }
                    finally
                    {
                        if (pawn != null && !pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
                    }
                }
            }
            finally { Rand.PopState(); }
            maxima = Enumerable.Range(0, Labels.Length).Select(i => showcase.Entrants.Max(e => e.Values()[i])).ToArray();
            hues = ArenaPresentation.IdentityHues(value.Rosters.Select(r => r.PawnKind));
        }

        internal static void Clear() { tournament = null; maxima = null; hues = null; }

        private static void TextAt(Rect rect, string value, float size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var previous = GUI.color; GUI.color = color;
            GroupCombatOverlay.DrawText(rect, value, size, anchor); GUI.color = previous;
        }

        internal static void Draw(int page, float elapsed)
        {
            if (tournament?.Showcase == null) return;
            var previousColor = GUI.color; var previousFont = Text.Font; var previousAnchor = Text.Anchor;
            try
            {
                GUI.color = Color.white;
                var w = UI.screenWidth; var h = UI.screenHeight;
                var scale = Mathf.Min(w / 1600f, h / 900f);
                var ox = (w - 1600 * scale) / 2; var oy = (h - 900 * scale) / 2;
                Rect R(float x, float y, float width, float height) => new(ox + x * scale, oy + y * scale, width * scale, height * scale);
                Widgets.DrawBoxSolid(new Rect(0, 0, w, h), new Color(.025f, .035f, .055f));
                Widgets.DrawBoxSolid(R(0, 0, 1600, 6), Gold);
                Widgets.DrawBoxSolid(R(72, 66, 30, 3), Gold);
                TextAt(R(116, 48, 500, 36), "ARENA  /  参赛者巡礼", 15 * scale, Gold);
                TextAt(R(72, 94, 1200, 68), "本届参赛者", 46 * scale, Color.white);
                var pageInput = tournament.Input.ShowcasePages[page];
                TextAt(R(1030, 117, 498, 40), tournament.Rosters.Count + " 种参赛者  ·  本页 " + pageInput.Seconds.ToString("0.##", CultureInfo.InvariantCulture) + " 秒", 17 * scale, Muted, TextAnchor.MiddleRight);
                var caption = pageInput.Caption.Length > 0 ? "“" + pageInput.Caption + "”" : "全员登场  ·  淘汰对决即将开始";
                // User copy is a literal sentence, not rich-text markup.
                var captionStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Mathf.Min(20, 1456f / Mathf.Max(1, caption.Length)) * scale), alignment = TextAnchor.MiddleLeft, richText = false, wordWrap = false };
                captionStyle.normal.textColor = pageInput.Caption.Length > 0 ? new Color(.87f, .8f, .65f) : Muted;
                GUI.Label(R(74, 165, 1454, 40), caption, captionStyle);
                Text.Font = GameFont.Small;
                if (Widgets.ButtonText(R(1380, 60, 148, 36), "取消擂台")) ArenaService.Cancel();

                var profiles = tournament.Showcase.Entrants.ToDictionary(e => e.Id);
                var entries = pageInput.EntrantIds.Select(id => profiles[id]).ToList();
                var offset = tournament.Input.ShowcasePages.Take(page).Sum(p => p.EntrantIds.Count);
                const float cardWidth = 276.8f;
                var startX = (1600 - entries.Count * cardWidth - (entries.Count - 1) * 18) / 2;
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var color = Color.HSVToRGB(hues[entry.PawnKind], .48f, .95f);
                    var reveal = ArenaPresentation.Progress(elapsed, i * .045f, .36f);
                    var x = startX + i * (cardWidth + 18); var y = 234 + (1 - reveal) * 16;
                    Widgets.DrawBoxSolid(R(x + 3, y + 7, cardWidth, 554), new Color(0, 0, 0, .2f));
                    Widgets.DrawBoxSolid(R(x, y, cardWidth, 554), new Color(.065f, .082f, .115f));
                    Widgets.DrawBoxSolid(R(x, y, cardWidth, 3), color);
                    Widgets.DrawBoxSolid(R(x + 1, y + 4, cardWidth - 2, 169), Color.Lerp(new Color(.065f, .082f, .115f), color, .085f));
                    TextAt(R(x + 18, y + 15, 95, 26), "选手 " + (offset + i + 1).ToString("D2"), 13 * scale, color);
                    TextAt(R(x + cardWidth - 80, y + 12, 60, 28), "◆", 16 * scale, color, TextAnchor.MiddleRight);
                    var kind = DefDatabase<PawnKindDef>.GetNamed(entry.PawnKind);
                    Widgets.DefIcon(R(x + (cardWidth - 128) / 2, y + 42, 128, 128), kind.race);
                    var nameSize = Mathf.Min(25, (cardWidth - 38) / Mathf.Max(1, entry.DisplayName.Length));
                    TextAt(R(x + 18, y + 184, cardWidth - 36, 40), entry.DisplayName, nameSize * scale, Color.white, TextAnchor.MiddleCenter);
                    Widgets.DrawBoxSolid(R(x + 22, y + 239, cardWidth - 44, 1), new Color(.16f, .2f, .26f));
                    var values = entry.Values();
                    for (var row = 0; row < Labels.Length; row++)
                    {
                        var metricY = y + 252 + row * 46;
                        var fill = ArenaShowcase.Fill(values[row], maxima[row]);
                        var gradeColor = GradeColors[ArenaShowcase.ProgressTier(fill)];
                        TextAt(R(x + 23, metricY, 95, 23), Labels[row], 15 * scale, new Color(.76f, .81f, .88f));
                        var number = row switch {
                            0 => values[row].ToString("0.##", CultureInfo.InvariantCulture),
                            1 => values[row].ToString("0.#", CultureInfo.InvariantCulture),
                            2 => values[row].ToString("0.#", CultureInfo.InvariantCulture) + " /击",
                            3 => values[row].ToString("0.00", CultureInfo.InvariantCulture) + " 次/秒",
                            _ => (values[row] * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%"
                        };
                        TextAt(R(x + 119, metricY, cardWidth - 142, 23), number, 14 * scale, gradeColor, TextAnchor.MiddleRight);
                        var bar = R(x + 23, metricY + 29, cardWidth - 46, 8);
                        Widgets.DrawBoxSolid(bar, new Color(.025f, .037f, .06f));
                        if (fill > 0) Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, Mathf.Max(2 * scale, bar.width * (float)fill * reveal), bar.height), gradeColor);
                        for (var segment = 1; segment < 10; segment++)
                            Widgets.DrawBoxSolid(new Rect(bar.x + bar.width * segment / 10, bar.y, scale, bar.height), new Color(.055f, .075f, .1f, .65f));
                    }
                }

                var pages = tournament.Input.ShowcasePages.Count;
                TextAt(R(72, 805, 1150, 26), "单体成年基础能力  ·  数字为实际属性  ·  条长以本届同项最高值为满格", 14 * scale, Muted);
                for (var i = 0; i < GradeColors.Length; i++)
                {
                    Widgets.DrawBoxSolid(R(72 + i * 193, 841, 7, 7), GradeColors[i]);
                    TextAt(R(86 + i * 193, 832, 179, 24), GradeLabels[i], 12.5f * scale, GradeColors[i]);
                }
                TextAt(R(1300, 814, 228, 32), (page + 1).ToString("D2") + "  /  " + pages.ToString("D2"), 22 * scale, Color.white, TextAnchor.MiddleRight);
                var segmentWidth = (1456f - (pages - 1) * 8) / pages;
                for (var i = 0; i < pages; i++)
                {
                    var bar = R(72 + i * (segmentWidth + 8), 861, segmentWidth, 3);
                    Widgets.DrawBoxSolid(bar, new Color(.14f, .18f, .24f));
                    var progress = i < page ? 1 : i == page ? Mathf.Clamp01(elapsed / (float)pageInput.Seconds) : 0;
                    if (progress > 0) Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * progress, bar.height), Gold);
                }
            }
            finally { GUI.color = previousColor; Text.Font = previousFont; Text.Anchor = previousAnchor; }
        }
    }
}
