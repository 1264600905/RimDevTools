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
        private static readonly Dictionary<string, int> selectedTypes = new();
        private static readonly Dictionary<string, float> selectedAt = new();
        private static readonly Dictionary<string, int> lastDrawnTypes = new();
        private static readonly Dictionary<string, List<(string id, int count, decimal totalPower)>> sampleTypes = new();
        private static readonly Dictionary<string, Pawn> previewPawns = new();
        private static readonly Dictionary<string, RenderTexture> portraits = new();
        private static readonly Dictionary<string, List<string>> generatedHediffs = new();
        private static readonly List<SquadGeneration.FactionScope> previewFactions = new();
        private static AutoTestMainMap previewMap;
        private static int initializationStartTick;
        internal static bool Ready { get; private set; }
        private const int InitializationTicks = 300;
        private static readonly string[] Labels = { "单体 · CP", "血量", "攻击伤害", "攻击速度", "利器护甲", "钝器护甲" };
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
            previewMap = new AutoTestMainMap(Find.CurrentMap, value.Input.Seed, new IntVec3(200, 1, 200));
            Rand.PushState(value.Input.Seed);
            try
            {
                foreach (var roster in value.Rosters)
                {
                    var samples = new List<(string id, int count, decimal totalPower)>(); sampleTypes.Add(roster.Id, samples);
                    var groups = roster.Members.GroupBy(m => SquadGeneration.KindLabel(m.PawnKind), StringComparer.Ordinal).ToList();
                    var scope = roster.FactionDef == null ? null : new SquadGeneration.FactionScope(roster.FactionDef, roster.DisplayName);
                    if (scope != null) previewFactions.Add(scope);
                    var faction = scope?.Faction;
                    var sampleCount = Math.Max(1, groups.Count);
                    for (var type = 0; type < sampleCount; type++)
                    {
                        Pawn pawn;
                        var sampleId = type == 0 ? roster.Id : roster.Id + "/type-" + type;
                        if (roster.Members.Count > 0)
                            pawn = ScenarioGameBackend.GenerateSquadPawn(groups[type].First(), faction, previewMap.Map.Tile);
                        else
                        {
                            var kind = DefDatabase<PawnKindDef>.GetNamed(roster.PawnKind);
                            var age = Math.Max(kind.RaceProps.lifeStageAges.Last().minAge, kind.RaceProps.Humanlike ? 25 : 0);
                            var xenotype = value.Input.Entrants.Single(e => e.Id == roster.Id).Xenotype;
                            pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, forceGenerateNewPawn: true,
                                canGeneratePawnRelations: false, fixedBiologicalAge: age,
                                forcedXenotype: xenotype == null ? null : DefDatabase<XenotypeDef>.GetNamed(xenotype)));
                        }
                        TestPawnWorkRestrictions.Remove(pawn);
                        previewPawns.Add(sampleId, pawn);
                        samples.Add((sampleId, groups.Count > 0 ? groups[type].Count() : roster.ResolvedCount,
                            groups.Count > 0 ? groups[type].Sum(m => m.Cost) : roster.ActualCombatPower));
                        generatedHediffs.Add(sampleId, pawn.health.hediffSet.hediffs.Select(h => h.def.defName).ToList());
                        var index = previewPawns.Count - 1;
                        if (index >= 3600) throw new ScenarioConfigurationException("Too many distinct squad preview types");
                        GenSpawn.Spawn(pawn, new IntVec3(5 + index % 60 * 3, 0, 5 + index / 60 * 3), previewMap.Map, Rot4.South);
                        var wait = JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture); wait.expiryInterval = 60000;
                        pawn.jobs.StartJob(wait, JobCondition.InterruptForced);
                    }
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
                    var profiles = new List<ArenaEntrantProfile>();
                    foreach (var sample in sampleTypes[roster.Id])
                    {
                        var pawn = previewPawns[sample.id];
                        if (!pawn.Spawned || pawn.Destroyed || pawn.Dead) throw new InvalidOperationException("Preview pawn did not survive initialization: " + roster.Id);
                        var attacks = pawn.meleeVerbs.GetUpdatedAvailableVerbsList(terrainTools: false)
                            .Where(v => v.IsMeleeAttack).ToList();
                        var totalWeight = attacks.Sum(v => v.GetSelectionWeight(null));
                        var damage = totalWeight > 0 ? attacks.Sum(v => v.GetSelectionWeight(null) / totalWeight
                            * v.verb.verbProps.AdjustedMeleeDamageAmount(v.verb, pawn)) : 0;
                        var cooldown = totalWeight > 0 ? attacks.Sum(v => v.GetSelectionWeight(null) / totalWeight
                            * v.verb.verbProps.AdjustedCooldownTicks(v.verb, pawn)) / 60f : 0;
                        var profile = new ArenaEntrantProfile {
                            Id = sample.id, PawnKind = pawn.kindDef.defName, DisplayName = pawn.kindDef.LabelCap,
                            Count = sample.count, Xenotype = pawn.genes?.Xenotype?.defName,
                            TotalCombatPower = sample.totalPower,
                            CombatPower = (double)(roster.Members.FirstOrDefault(m => m.PawnKind == pawn.kindDef.defName && m.Xenotype == pawn.genes?.Xenotype?.defName)?.Cost ?? roster.UnitCombatPower), AttackDamage = damage,
                            Health = pawn.RaceProps.body.AllParts.Sum(part => part.def.GetMaxHealth(pawn)),
                            AttackSpeed = cooldown > 0 ? 1 / cooldown : 0,
                            SharpArmor = OverallArmor(pawn, StatDefOf.ArmorRating_Sharp),
                            BluntArmor = OverallArmor(pawn, StatDefOf.ArmorRating_Blunt),
                            NaturalSharpArmor = pawn.GetStatValue(StatDefOf.ArmorRating_Sharp),
                            NaturalBluntArmor = pawn.GetStatValue(StatDefOf.ArmorRating_Blunt),
                            WornApparel = pawn.apparel?.WornApparel.Select(a => a.def.defName).ToList() ?? new(),
                            PrimaryWeapon = pawn.equipment?.Primary?.def.defName,
                            HediffsAfterGeneration = generatedHediffs[sample.id],
                            HediffsAtSnapshot = pawn.health.hediffSet.hediffs.Select(h => h.def.defName).ToList(),
                            SpawnedAtSnapshot = pawn.Spawned, InitializationTicks = showcase.InitializationTicks
                        };
                        profiles.Add(profile);
                    }
                    var entry = System.Text.Json.JsonSerializer.Deserialize<ArenaEntrantProfile>(System.Text.Json.JsonSerializer.Serialize(profiles[0]));
                    if (roster.SquadId != null) entry.Composition = profiles;
                    entry.Count = roster.ResolvedCount; entry.TotalCombatPower = roster.ActualCombatPower;
                    entry.DisplayName = roster.DisplayName;
                    showcase.Entrants.Add(entry);
                }
            }
            finally { Rand.PopState(); }
            maxima = Enumerable.Range(0, Labels.Length).Select(i => showcase.Entrants.SelectMany(e => e.Composition.Count > 0 ? e.Composition : new List<ArenaEntrantProfile> { e }).Max(e => e.Values()[i])).ToArray();
            hues = ArenaPresentation.IdentityHues(tournament.Rosters.Select(r => tournament.Input.Format == "knockout" ? r.PawnKind : r.Id));
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

        // Keep the frozen portraits and scales for match previews and final standings; release all
        // live pawns and their map before any battle starts.
        internal static void ReleasePreviews()
        {
            foreach (var pair in previewPawns)
            {
                var pawn = pair.Value;
                if (Ready && pawn.RaceProps.Humanlike && !portraits.ContainsKey(pair.Key))
                {
                    var texture = new RenderTexture(256, 256, 0); texture.Create();
                    PawnPortraitRendering.Render(pawn, texture);
                    portraits.Add(pair.Key, texture);
                }
                if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemovePawn(pawn);
                if (!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
            }
            previewPawns.Clear(); sampleTypes.Clear();
            previewMap?.Dispose(); previewMap = null;
            foreach (var faction in previewFactions) faction.Dispose(); previewFactions.Clear();
            if (tournament?.Showcase != null) tournament.Showcase.PreviewCleanupComplete = true;
            generatedHediffs.Clear();
        }

        internal static void Clear()
        {
            ReleasePreviews();
            foreach (var texture in portraits.Values) { texture.Release(); UnityEngine.Object.Destroy(texture); }
            portraits.Clear(); selectedTypes.Clear(); selectedAt.Clear(); lastDrawnTypes.Clear(); Ready = false;
            tournament = null; maxima = null; hues = null;
        }

        internal static void DrawIdentity(Rect rect, string entrantId, float elapsed = 0)
        {
            var entry = tournament?.Showcase?.Entrants.FirstOrDefault(e => e.Id == entrantId);
            if (entry == null) return;
            var detail = entry.Composition.Count == 0 ? entry : entry.Composition[ArenaShowcase.CompositionIndex(elapsed, entry.Composition.Count)];
            DrawPortrait(rect, detail);
        }

        private static void DrawPortrait(Rect rect, ArenaEntrantProfile entry)
        {
            if (portraits.TryGetValue(entry.Id, out var cached)) { GUI.DrawTexture(rect, cached); return; }
            var kind = DefDatabase<PawnKindDef>.GetNamed(entry.PawnKind);
            if (!kind.RaceProps.Humanlike)
            {
                Widgets.DefIcon(rect, kind.race);
                return;
            }
            if (!previewPawns.TryGetValue(entry.Id, out var pawn)) return;
            if (!portraits.TryGetValue(entry.Id, out var texture) && Event.current.type == EventType.Repaint)
            {
                PortraitsCache.SetDirty(pawn);
                pawn.Drawer.renderer.SetAllGraphicsDirty();
                pawn.Drawer.renderer.EnsureGraphicsInitialized();
                texture = new RenderTexture(256, 256, 0); texture.Create();
                PawnPortraitRendering.Render(pawn, texture);
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

        internal static void ResetMatchupSelection()
        {
            selectedTypes.Clear(); selectedAt.Clear();
        }

        internal static void Draw(int page, float elapsed, bool standings = false, ArenaMatch matchup = null)
        {
            if (tournament?.Showcase == null) return;
            if (!Ready)
            {
                ArenaRulesView.Draw(tournament.Input, 0);
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
                TextAt(R(116, 48, 1180, 36), matchup != null ? "ARENA  /  赛前对阵  ·  " + ArenaPresentation.MatchTitle(matchup) : standings ? "ARENA  /  荣誉榜" : "ARENA  /  参赛者巡礼", 15 * scale, Gold);
                var title = matchup != null ? tournament.Input.Name : standings ? tournament.TopFive.Entries.Count == 5 ? "本届前五 · 荣耀时刻" : "本届排名 · 荣耀时刻" : "本届参赛者";
                if (tournament.Input.Format != "knockout" && matchup == null && !standings)
                    title = tournament.Input.Entrants.Single(e => e.Id == tournament.Input.ShowcasePages[page].EntrantIds[0]).Role == "boss" ? "本届魔王" : "本届挑战者";
                TextAt(R(72, 94, 930, 68), title, Mathf.Min(46, 930f / Mathf.Max(1, title.Length)) * scale, Color.white);
                var pageInput = standings ? new ArenaShowcasePageInput {
                    Seconds = tournament.TopFive.DurationSeconds,
                    EntrantIds = tournament.TopFive.Entries.Select(p => p.EntrantId).ToList(),
                    Caption = ""
                } : matchup != null ? new ArenaShowcasePageInput {
                    Seconds = ArenaPresentation.PreBattleSeconds, EntrantIds = new() { matchup.Left, matchup.Right }, Caption = ""
                } : tournament.Input.ShowcasePages[page];
                TextAt(R(1030, 117, 498, 40), matchup != null ? "第 " + (tournament.Matches.IndexOf(matchup) + 1) + " 场  ·  赛前展示 5 秒" : standings ? "赛事完结  ·  展示 10 秒" : tournament.Rosters.Count + " 种参赛者  ·  本页 " + pageInput.Seconds.ToString("0.##", CultureInfo.InvariantCulture) + " 秒", 17 * scale, Muted, TextAnchor.MiddleRight);
                var caption = matchup != null ? "即将对战：" + tournament.Rosters.Single(r => r.Id == matchup.Left).DisplayName + "  VS  " + tournament.Rosters.Single(r => r.Id == matchup.Right).DisplayName
                    : standings ? "晋级越远，名次越高  ·  同阶段按最近胜利评分排序" : pageInput.Caption.Length > 0 ? "“" + pageInput.Caption + "”"
                    : tournament.Input.Format == "knockout" ? "全员登场  ·  淘汰对决即将开始" : "魔王与挑战者登场 · 每组独立、满编对战";
                // User copy is a literal sentence, not rich-text markup.
                var captionStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Mathf.Min(20, 1456f / Mathf.Max(1, caption.Length)) * scale), alignment = TextAnchor.MiddleLeft, richText = false, wordWrap = false };
                captionStyle.normal.textColor = pageInput.Caption.Length > 0 ? new Color(.87f, .8f, .65f) : Muted;
                GUI.Label(R(74, 165, 1454, 40), caption, captionStyle);
                Text.Font = GameFont.Small;
                if (Widgets.ButtonText(R(1380, 60, 148, 36), "取消擂台")) ArenaService.Cancel();

                var profiles = tournament.Showcase.Entrants.ToDictionary(e => e.Id);
                var entries = pageInput.EntrantIds.Select(id => profiles[id]).ToList();
                var offset = tournament.Input.ShowcasePages.Take(page).Sum(p => p.EntrantIds.Count);
                var cardWidth = matchup != null ? 400f : 276.8f;
                var cardGap = matchup != null ? 160f : 18f;
                var startX = (1600 - entries.Count * cardWidth - (entries.Count - 1) * cardGap) / 2;
                if (matchup != null) TextAt(R(720, 420, 160, 90), "VS", 48 * scale, Gold, TextAnchor.MiddleCenter);
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var roster = (matchup?.ResolvedRosters.Count > 0 ? matchup.ResolvedRosters : tournament.Rosters).Single(r => r.Id == entry.Id);
                    var selected = selectedTypes.TryGetValue(entry.Id, out var selection) ? selection : 0;
                    if (!standings)
                    {
                        selected = ArenaShowcase.CompositionIndex(elapsed, entry.Composition.Count, selected,
                            selectedAt.TryGetValue(entry.Id, out var changedAt) ? changedAt : 0);
                        if (matchup == null && entry.Composition.Count > 1 && Event.current.type == EventType.Repaint
                            && (!lastDrawnTypes.TryGetValue(entry.Id, out var last) || last != selected))
                        {
                            lastDrawnTypes[entry.Id] = selected;
                            tournament.Showcase.CompositionSwitches.Add(new ArenaCompositionSwitch { EntrantId = entry.Id, Index = selected, Seconds = elapsed });
                        }
                    }
                    var detail = entry.Composition.Count > 0 ? entry.Composition[selected % entry.Composition.Count] : entry;
                    var color = standings && i == 0 ? Gold : standings && i == 1 ? new Color(.78f, .85f, .93f) : standings && i == 2 ? new Color(.88f, .59f, .37f) : Color.HSVToRGB(hues[tournament.Input.Format == "knockout" ? entry.PawnKind : entry.Id], .48f, .95f);
                    var reveal = ArenaPresentation.Progress(elapsed, i * .045f, .36f);
                    var x = startX + i * (cardWidth + cardGap); var y = 234 + (1 - reveal) * 16;
                    Widgets.DrawBoxSolid(R(x + 3, y + 7, cardWidth, 554), new Color(0, 0, 0, .2f));
                    Widgets.DrawBoxSolid(R(x, y, cardWidth, 554), new Color(.065f, .082f, .115f));
                    Widgets.DrawBoxSolid(R(x, y, cardWidth, 3), color);
                    Widgets.DrawBoxSolid(R(x + 1, y + 4, cardWidth - 2, 169), Color.Lerp(new Color(.065f, .082f, .115f), color, .085f));
                    var rankLabel = matchup != null ? i == 0 ? "A 队 · 本场出战" : "B 队 · 本场出战" : standings ? new[] { "第一名 · 冠军", "第二名 · 亚军", "第三名", "第四名", "第五名" }[i] : "选手 " + (offset + i + 1).ToString("D2");
                    if (tournament.Input.Format != "knockout") rankLabel = (tournament.Input.Entrants.Single(e => e.Id == entry.Id).Role == "boss" ? "魔王" : "挑战者") + (matchup == null ? " · 初始阵容" : " · 本场出战");
                    TextAt(R(x + 18, y + 15, cardWidth - 85, 26), rankLabel, standings ? 16 * scale : 13 * scale, color);
                    TextAt(R(x + cardWidth - 80, y + 12, 60, 28), "◆", 16 * scale, color, TextAnchor.MiddleRight);
                    DrawPortrait(R(x + (cardWidth - 144) / 2, y + 30, 144, 144), detail);
                    var nameSize = Mathf.Min(25, (cardWidth - 38) / Mathf.Max(1, entry.DisplayName.Length));
                    TextAt(R(x + 18, y + 184, cardWidth - 36, 40), entry.DisplayName, nameSize * scale, Color.white, TextAnchor.MiddleCenter);
                    if (standings)
                    {
                        var placement = tournament.TopFive.Entries[i];
                        TextAt(R(x + 18, y + 216, cardWidth - 36, 22), placement.Wins == 0 ? "本届未获胜" : placement.Wins + " 场胜利  ·  最近胜利 " + placement.LastVictoryScore.ToString("F2") + " 分", 12 * scale, color, TextAnchor.MiddleCenter);
                    }
                    if (!standings && entry.Composition.Count > 0)
                    {
                        if (entry.Composition.Count > 1 && Widgets.ButtonText(R(x + 8, y + 218, 25, 22), "‹"))
                        { selectedTypes[entry.Id] = (selected + entry.Composition.Count - 1) % entry.Composition.Count; selectedAt[entry.Id] = elapsed; }
                        if (entry.Composition.Count > 1 && Widgets.ButtonText(R(x + cardWidth - 33, y + 218, 25, 22), "›"))
                        { selectedTypes[entry.Id] = (selected + 1) % entry.Composition.Count; selectedAt[entry.Id] = elapsed; }
                        var detailCount = matchup?.ResolvedRosters.Count > 0 && roster.Members.Count > 0
                            ? roster.Members.Count(m => SquadGeneration.KindLabel(m.PawnKind) == detail.DisplayName) : detail.Count;
                        TextAt(R(x + 35, y + 216, cardWidth - 70, 23), detail.DisplayName + " ×" + detailCount, 12 * scale, Muted, TextAnchor.MiddleCenter);
                    }
                    Widgets.DrawBoxSolid(R(x + 22, y + 239, cardWidth - 44, 1), new Color(.16f, .2f, .26f));
                    var values = detail.Values();
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
                    if (roster.SquadId != null || matchup != null)
                    {
                        TextAt(R(x + 18, y + 527, cardWidth - 36, 23), "数量 " + roster.ResolvedCount + " · 总 CP " + roster.ActualCombatPower.ToString("0.##"), 12 * scale, Muted, TextAnchor.MiddleCenter);
                    }
                    if (roster.SquadId != null)
                    {
                        var description = "属性沿用初始巡礼代表成员；本场实际构成：\n" + string.Join("\n", roster.Members.GroupBy(m => SquadGeneration.KindLabel(m.PawnKind)).Select(g => g.Key + " ×" + g.Count() + " · CP " + g.Sum(m => m.Cost).ToString("0.##")));
                        TooltipHandler.TipRegion(R(x, y, cardWidth, 554), description);
                    }
                }

                var pages = standings || matchup != null ? 1 : tournament.Input.ShowcasePages.Count;
                TextAt(R(72, 805, 1150, 26), matchup != null ? "沿用参赛者巡礼属性快照  ·  混编每秒轮播，可用箭头切换  ·  同项最高值为满格" : standings ? "沿用赛前角色与属性快照  ·  条长仍以本届全体同项最高值为满格" : "原生装备与实际角色属性  ·  混编每秒轮播，可用箭头切换，悬停查看构成  ·  同项最高值为满格", 14 * scale, Muted);
                for (var i = 0; i < GradeColors.Length; i++)
                {
                    Widgets.DrawBoxSolid(R(72 + i * 193, 841, 7, 7), GradeColors[i]);
                    TextAt(R(86 + i * 193, 832, 179, 24), GradeLabels[i], 12.5f * scale, GradeColors[i]);
                }
                TextAt(R(1300, 814, 228, 32), matchup != null ? "即将开战 · " + ArenaPresentation.CountdownLabel(elapsed, ArenaPresentation.PreBattleSeconds) : (page + 1).ToString("D2") + "  /  " + pages.ToString("D2"), 22 * scale, Color.white, TextAnchor.MiddleRight);
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
