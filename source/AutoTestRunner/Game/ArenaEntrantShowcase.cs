using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace LogSyncMod.AutoTest
{
    internal static class ArenaEntrantShowcase
    {
        private static ArenaTournamentResult tournament;
        private static double[] maxima;
        private static Dictionary<string, float> hues;
        private static readonly Dictionary<string, Pawn> previewPawns = new();
        private static readonly Dictionary<string, RenderTexture> portraits = new();
        private static readonly Dictionary<string, List<string>> generatedHediffs = new();
        private static AutoTestMainMap previewMap;
        private static int initializationStartTick;
        internal static bool Ready { get; private set; }
        private const int InitializationTicks = 300;
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
            Clear();
            tournament = value;
            value.Showcase = new ArenaShowcaseResult { PageSeconds = value.Input.ShowcasePageSeconds };
            // Spawn real, equipped pawns so PostSpawnSetup and native ticks can add
            // runtime comps/hediffs before sampling, as they do for normal characters.
            previewMap = new AutoTestMainMap(Find.CurrentMap, value.Input.Seed, new IntVec3(100, 1, 100));
            Rand.PushState(value.Input.Seed);
            try
            {
                foreach (var roster in value.Rosters)
                {
                    var kind = DefDatabase<PawnKindDef>.GetNamed(roster.PawnKind);
                    var age = Math.Max(kind.RaceProps.lifeStageAges.Last().minAge, kind.RaceProps.Humanlike ? 25 : 0);
                    var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, forceGenerateNewPawn: true,
                            canGeneratePawnRelations: false, fixedBiologicalAge: age));
                    previewPawns.Add(roster.Id, pawn);
                    generatedHediffs.Add(roster.Id, pawn.health.hediffSet.hediffs.Select(h => h.def.defName).ToList());
                    var index = previewPawns.Count - 1;
                    GenSpawn.Spawn(pawn, new IntVec3(10 + index % 8 * 11, 0, 10 + index / 8 * 11), previewMap.Map, Rot4.South);
                    var wait = JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture);
                    wait.expiryInterval = 60000;
                    pawn.jobs.StartJob(wait, JobCondition.InterruptForced);
                }
            }
            finally { Rand.PopState(); }
            initializationStartTick = Find.TickManager.TicksGame;
            Ready = false;
        }

        internal static bool InitializeFrame()
        {
            if (Ready) return true;
            if (Find.TickManager.TicksGame - initializationStartTick < InitializationTicks)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                return false;
            }
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            var showcase = tournament.Showcase;
            showcase.InitializationTicks = Find.TickManager.TicksGame - initializationStartTick;
            Rand.PushState(tournament.Input.Seed);
            try
            {
                foreach (var roster in tournament.Rosters)
                {
                    var pawn = previewPawns[roster.Id];
                    if (!pawn.Spawned || pawn.Destroyed || pawn.Dead) throw new InvalidOperationException("Preview pawn did not survive initialization: " + roster.Id);
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
                        SharpArmor = OverallArmor(pawn, StatDefOf.ArmorRating_Sharp),
                        BluntArmor = OverallArmor(pawn, StatDefOf.ArmorRating_Blunt),
                        NaturalSharpArmor = pawn.GetStatValue(StatDefOf.ArmorRating_Sharp),
                        NaturalBluntArmor = pawn.GetStatValue(StatDefOf.ArmorRating_Blunt),
                        WornApparel = pawn.apparel?.WornApparel.Select(a => a.def.defName).ToList() ?? new(),
                        PrimaryWeapon = pawn.equipment?.Primary?.def.defName,
                        HediffsAfterGeneration = generatedHediffs[roster.Id],
                        HediffsAtSnapshot = pawn.health.hediffSet.hediffs.Select(h => h.def.defName).ToList(),
                        SpawnedAtSnapshot = pawn.Spawned, InitializationTicks = showcase.InitializationTicks
                    });
                }
            }
            finally { Rand.PopState(); }
            maxima = Enumerable.Range(0, Labels.Length).Select(i => showcase.Entrants.Max(e => e.Values()[i])).ToArray();
            hues = ArenaPresentation.IdentityHues(tournament.Rosters.Select(r => r.PawnKind));
            Ready = true;
            return true;
        }

        private static double OverallArmor(Pawn pawn, StatDef stat)
        {
            var apparel = pawn.apparel?.WornApparel;
            return ArenaShowcase.OverallArmor(pawn.GetStatValue(stat), pawn.RaceProps.body.AllParts.Select(part =>
                ((double)part.coverageAbs, apparel?.Where(a => a.def.apparel.CoversBodyPart(part))
                    .Select(a => (double)a.GetStatValue(stat)).ToArray() ?? Array.Empty<double>())));
        }

        internal static void Clear()
        {
            foreach (var texture in portraits.Values) { texture.Release(); UnityEngine.Object.Destroy(texture); }
            portraits.Clear();
            foreach (var pawn in previewPawns.Values)
            {
                if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
                if (!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
            }
            previewPawns.Clear();
            previewMap?.Dispose(); previewMap = null;
            if (tournament?.Showcase != null) tournament.Showcase.PreviewCleanupComplete = true;
            generatedHediffs.Clear(); Ready = false;
            tournament = null; maxima = null; hues = null;
        }

        private static void DrawPortrait(Rect rect, ArenaEntrantProfile entry)
        {
            var pawn = previewPawns[entry.Id];
            if (!pawn.RaceProps.Humanlike)
            {
                Widgets.DefIcon(rect, DefDatabase<PawnKindDef>.GetNamed(entry.PawnKind).race);
                return;
            }
            if (!portraits.TryGetValue(entry.Id, out var texture) && Event.current.type == EventType.Repaint)
            {
                PortraitsCache.SetDirty(pawn);
                pawn.Drawer.renderer.SetAllGraphicsDirty();
                pawn.Drawer.renderer.EnsureGraphicsInitialized();
                texture = new RenderTexture(256, 256, 0); texture.Create();
                PawnCacheCameraManager.PawnCacheRenderer.RenderPawn(pawn, texture, Vector3.zero, 1f, 0, Rot4.South,
                    renderHead: true, renderHeadgear: true, renderClothes: true, portrait: true);
                portraits.Add(entry.Id, texture);
                entry.PortraitRendered = true;
            }
            if (texture != null) GUI.DrawTexture(rect, texture);
        }

        private static void TextAt(Rect rect, string value, float size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var previous = GUI.color; GUI.color = color;
            GroupCombatOverlay.DrawText(rect, value, size, anchor); GUI.color = previous;
        }

        internal static void Draw(int page, float elapsed)
        {
            if (tournament?.Showcase == null) return;
            if (!Ready)
            {
                Widgets.DrawBoxSolid(new Rect(0, 0, UI.screenWidth, UI.screenHeight), new Color(.025f, .035f, .055f));
                TextAt(new Rect(0, UI.screenHeight / 2 - 44, UI.screenWidth, 48), "正在准备参赛者", 30, Color.white, TextAnchor.MiddleCenter);
                TextAt(new Rect(0, UI.screenHeight / 2 + 12, UI.screenWidth, 28), "生成角色与装备 · 初始化身体状态", 17, Muted, TextAnchor.MiddleCenter);
                if (Widgets.ButtonText(new Rect(UI.screenWidth - 164, 28, 136, 34), "取消擂台")) ArenaService.Cancel();
                return;
            }
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
                    DrawPortrait(R(x + (cardWidth - 144) / 2, y + 30, 144, 144), entry);
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
                TextAt(R(72, 805, 1150, 26), "成年角色与原生装备  ·  护甲含衣甲覆盖  ·  条长以本届同项最高值为满格", 14 * scale, Muted);
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
