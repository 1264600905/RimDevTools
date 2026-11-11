using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;
using LogSyncMod.AutoTest.Scenarios;
using RimWorld;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class Dialog_ArenaWorkbench
    {
        private Vector2 presetScroll, teamsScroll;
        private string librarySearch = "";
        private void DrawSchedule(Rect area)
        {
            var s = document.Schedule;
            ArenaWorkbench.ApplyUiDefaults(s);
            var side = new Rect(area.x, area.y, 222, area.height); ArenaWorkbenchUi.Panel(side);
            Label(new Rect(side.x + 16, side.y + 12, side.width - 32, 30), "赛程预设", gold);
            var presetArea = new Rect(side.x + 12, side.y + 55, side.width - 24, side.height - 148);
            var pv = new Rect(0, 0, presetArea.width - 18, document.SchedulePresets.Count * 61);
            Widgets.BeginScrollView(presetArea, ref presetScroll, pv);
            var fixedPresets = ArenaWorkbench.BuiltInSchedules();
            for (var i = 0; i < fixedPresets.Count; i++)
            {
                var preset = fixedPresets[i];
                if (Button(new Rect(0, i * 61, pv.width, 50), preset.Name, preset.Format == s.Format))
                { document.Schedule = ArenaWorkbench.Copy(preset); ArenaWorkbench.ApplyUiDefaults(document.Schedule); numberBuffers.Clear(); choiceResults.Clear(); Changed(); }
            }
            Widgets.EndScrollView();
            if (Button(new Rect(side.x + 12, side.yMax - 80, side.width - 24, 31), "＋ 保存完整赛事预设"))
            { SaveEventPreset(); }
            if (Button(new Rect(side.x + 12, side.yMax - 40, side.width - 24, 29), "复制预设 XML")) Protect(() => CopyXml(ArenaWorkbench.LibraryXml(document)));
            var content = new Rect(side.xMax + 18, area.y, area.width - side.width - 18, area.height);
            var view = new Rect(0, 0, content.width - 18, Math.Max(content.height, scheduleContentHeight));
            Widgets.BeginScrollView(content, ref scheduleScroll, view);
            Label(new Rect(16, 5, view.width - 200, 32), "本届赛事", gold, GameFont.Medium);
            if (Button(new Rect(view.width - 166, 5, 150, 30), "复制赛程 XML")) CopyAll();
            s.Name = Widgets.TextField(new Rect(16, 47, view.width - 32, 32), s.Name);
            var col = (view.width - 50) / 2;
            var a = new Form(this, 28, col - 24, 103); var b = new Form(this, col + 46, col - 24, 103);
            ArenaWorkbenchUi.Panel(new Rect(16, 96, col, Math.Max(570, content.height - 120)));
            ArenaWorkbenchUi.Panel(new Rect(col + 34, 96, col, Math.Max(570, content.height - 120)));
            a.Title("赛制与对局");
            a.Note("当前：" + fixedPresets.First(p => p.Format == s.Format).Name + "。切换请使用左侧四种预设。", 36);
            if (s.Format == "knockout") s.Matchmaking = a.Choice("配对方式", s.Matchmaking, "closestCount|人数接近优先", "random|随机抽签");
            s.Duration = a.Number("duration", "单场模拟秒数", s.Duration, .01, 86400);
            s.Speed = a.Choice("比赛速度", s.Speed, "normal|标准", "fast|快速", "superfast|极快");
            s.Ai = a.Choice("默认战斗 AI", s.Ai, "assaultColony|原生立刻进攻", "defendPoint|固守", "lure|勾引");
            if (s.Format == "groupcombat") a.Note("右侧可为两支编队分别选择 AI；固守和勾引均保留远程射击职责。", 42);
            a.Title("随机种子");
            s.Seed = (int)a.Number("seed", "种子（-1 为随机）", s.Seed, int.MinValue, int.MaxValue, true);
            var yy = a.Y;
            if (Button(new Rect(a.X, yy, (a.Width - 8) / 2, 30), "随机生成一个种子")) { s.Seed = RandomNumberGenerator.GetInt32(int.MaxValue); numberBuffers.Remove("seed"); }
            if (Button(new Rect(a.X + (a.Width + 8) / 2, yy, (a.Width - 8) / 2, 30), "每届随机（-1）", s.Seed == -1)) { s.Seed = -1; numberBuffers.Remove("seed"); }
            a.Y += 42; a.Note("可直接输入种子复现赛事；-1 在每次开赛时随机抽取，整届沿用同一个实际种子。", 48);
            a.Title("战场地图");
            var previousMap = s.Battlefield;
            s.Battlefield = a.Choice("地图风格", s.Battlefield, CombatMapCatalog.Choices);
            if (s.Battlefield != previousMap) { s.Temperature = CombatMapCatalog.Get(s.Battlefield).Temperature; numberBuffers.Remove("mapTemperature"); }
            a.Note(CombatMapCatalog.Get(s.Battlefield).Description, 48);
            s.Width = (int)a.Number("mapWidth", "地图宽度", s.Width, 80, 250, true);
            s.Height = (int)a.Number("mapHeight", "地图高度", s.Height, 80, 250, true);
            if (s.Battlefield != "open") s.FeatureDensity = a.Number("featureDensity", "地形疏密", s.FeatureDensity, .5, 1.5);
            s.SpawnClearance = (int)a.Number("spawnClearance", "出生区清理（格）", s.SpawnClearance, 2, 12, true);
            s.Temperature = a.Number("mapTemperature", "战场温度（℃）", s.Temperature, -100, 100);
            a.Note("出生区会清除障碍和积雪，并检查双方通路。相同种子可复现地图。", 48);
            a.Title("战场清理");
            s.ClearCorpses = a.Check("清理尸体", s.ClearCorpses); s.ClearItems = a.Check("清理掉落物", s.ClearItems);
            b.Title("本届参赛编队");
            foreach (var t in document.Teams.Where(t => !t.IsEmpty))
            {
                var faction = DefDatabase<FactionDef>.GetNamedSilentFail(t.Faction);
                ArenaWorkbenchUi.DefIcon(new Rect(b.X, b.Y + 2, 24, 24), faction);
                var enabled = t.Enabled;
                Widgets.CheckboxLabeled(new Rect(b.X + 34, b.Y, b.Width - 34, 29), t.Name + (t.Mode == "combatPower" ? " · " + t.Budget + " CP" : " · " + t.MemberCount + " 人"), ref enabled);
                t.Enabled = enabled; b.Y += 37;
                if (s.Format is "bossRoundRobin" or "limitChallenge") t.Role = b.Choice("角色：" + t.Name, t.Role, "boss|魔王", "challenger|挑战者");
                if (s.Format == "groupcombat") t.Ai = b.Choice("第 " + (document.Teams.IndexOf(t) + 1) + " 队 AI：" + t.Name, t.Ai, "inherit|跟随默认", "assaultColony|原生立刻进攻", "defendPoint|固守", "lure|勾引");
            }
            if (document.Teams.All(t => t.IsEmpty)) b.Note("还没有可参赛的编队，请先添加人员或战斗组。", 42);
            if (b.Action("添加 / 启用编队…")) OpenRosterManagement();
            if (b.Action("加载完整赛事预设…")) OpenEventLoad();
            if (s.Format == "limitChallenge")
            {
                b.Title("挑战者增长");
                s.GrowthTarget = b.Choice("增长目标", s.GrowthTarget, "count|人数", "budget|CP 预算");
                s.GrowthMode = b.Choice("增长方式", s.GrowthMode, "multiply|乘法", "add|固定加法");
                s.GrowthStep = (decimal)b.Number("growthStep", s.GrowthMode == "add" ? "每档增量" : "每档倍率", (double)s.GrowthStep, 0, 10000000);
                s.GrowthCap = (decimal)b.Number("growthCap", "增长上限", (double)s.GrowthCap, 1, s.GrowthTarget == "count" ? 200 : 10000000, s.GrowthTarget == "count");
                b.Note("人员模式使用人数增长；CP 增长使用战斗组模式。换一个魔王后，挑战者从初始规模重新开始。", 58);
            }
            b.Title("对局规则");
            b.Note(s.Format == "groupcombat" ? "恰好两支编队直接对战，不进行晋级与魔王分组。" : s.Format == "knockout" ? "胜者晋级，败者淘汰。\n每场双方重新以完整阵容出战。" : "每位挑战者依次迎战所有魔王。\n每场双方重新以完整阵容出战。", 65);
            scheduleContentHeight = Math.Max(Math.Max(a.Y, b.Y) + 32, 690);
            Widgets.EndScrollView();
        }
        private void DrawTeamList(Rect rect)
        {
            ArenaWorkbenchUi.Panel(rect);
            Label(new Rect(rect.x + 15, rect.y + 10, rect.width - 30, 30), "编队 · " + document.Teams.Count, gold);
            var area = new Rect(rect.x + 10, rect.y + 52, rect.width - 20, rect.height - 185);
            var view = new Rect(0, 0, area.width - 18, document.Teams.Count * 76);
            Widgets.BeginScrollView(area, ref teamsScroll, view);
            for (var i = 0; i < document.Teams.Count; i++)
            {
                var t = document.Teams[i]; var row = new Rect(0, i * 76, view.width, 68);
                ArenaWorkbenchUi.Box(row, i == teamIndex ? new Color(.18f, .225f, .25f) : ArenaWorkbenchUi.Field, i == teamIndex ? gold : ArenaWorkbenchUi.Border);
                ArenaWorkbenchUi.DefIcon(new Rect(10, row.y + 16, 34, 34), DefDatabase<FactionDef>.GetNamedSilentFail(t.Faction));
                Label(new Rect(53, row.y + 7, row.width - 60, 28), t.Name, i == teamIndex ? gold : Color.white);
                Label(new Rect(53, row.y + 35, row.width - 60, 23), t.IsEmpty ? "空编队 · 未保存" : (t.Source == "faction" ? "战斗组" : "人员") + " · " + (t.Mode == "combatPower" ? t.Budget + " CP" : t.MemberCount + " 人"), muted, GameFont.Tiny);
                var isEnabled = t.Enabled; Widgets.Checkbox(new Vector2(row.width - 24, row.y + 42), ref isEnabled, 18); t.Enabled = isEnabled;
                if (GUI.enabled && Widgets.ButtonInvisible(new Rect(row.x, row.y, row.width, 39))) SelectTeam(i);
            }
            Widgets.EndScrollView();
            if (Button(new Rect(rect.x + 10, rect.yMax - 124, rect.width - 20, 31), "＋ 从编队预设添加…")) OpenTeamAdd();
            if (Button(new Rect(rect.x + 10, rect.yMax - 80, rect.width - 20, 31), "＋ 新建空编队")) { document.Teams.Add(new ArenaWorkbenchTeam()); SelectTeam(document.Teams.Count - 1); }
            if (Button(new Rect(rect.x + 10, rect.yMax - 40, rect.width - 20, 29), "复制当前编队", active: Team != null)) { document.Teams.Add(ArenaWorkbench.Duplicate(Team)); SelectTeam(document.Teams.Count - 1); }
        }
        private void AddKind(string category = null, int? replace = null)
        {
            Pick("pawnKind", d => {
                if (replace.HasValue) ArenaWorkbench.ReadAndChange(Team.Members[replace.Value], x => Scalar(x, "basePawnKind", d.defName));
                else
                {
                    Team.Members.Add(new ArenaWorkbenchMember { Name = d.LabelCap, PawnXml = new XElement("PawnTemplate", new XAttribute("id", "pawn"), new XElement("basePawnKind", d.defName)).ToString() });
                    SelectMember(Team.Members.Count - 1);
                }
            }, c => category == null || c.Category == category);
        }
        private void PickBattleGroup()
        {
            PickMany("battleGroup", choices => { if (choices.Count == 0) return; var c = choices[0]; Team.Faction = c.Definition.defName; Team.GroupMakerIndex = c.GroupMakerIndex; }, false);
        }
        private void DrawRoster(Rect area)
        {
            var left = new Rect(area.x, area.y, 230, area.height); DrawTeamList(left);
            if (Team == null) { Label(new Rect(left.xMax + 24, area.y + 30, area.width - 280, 50), "创建一个编队，再添加人员或选择战斗组。", muted); return; }
            var rightW = Math.Min(320, area.width * .235f);
            var middle = new Rect(left.xMax + 18, area.y, area.width - left.width - rightW - 36, area.height);
            var right = new Rect(middle.xMax + 18, area.y, rightW, area.height);
            ArenaWorkbenchUi.Panel(middle); ArenaWorkbenchUi.Panel(right);
            Team.Name = Widgets.TextField(new Rect(middle.x + 16, middle.y + 14, middle.width - 185, 33), Team.Name);
            if (Button(new Rect(middle.xMax - 157, middle.y + 14, 141, 33), "复制编队 XML", active: !Team.IsEmpty)) Protect(() => CopyXml(ArenaWorkbench.TeamXml(Team)));
            var modeY = middle.y + 63;
            if (Button(new Rect(middle.x + 16, modeY, (middle.width - 40) / 2, 35), "人员模式", Team.Source != "faction"))
            {
                if (Team.Source == "kind") { Team.Members.Add(new ArenaWorkbenchMember { Name = LabelOf<PawnKindDef>(Team.PawnKind), Count = Math.Max(0, Team.Count), PawnXml = new XElement("PawnTemplate", new XAttribute("id", "pawn"), new XElement("basePawnKind", Team.PawnKind)).ToString() }); }
                Team.Source = "groups"; Team.Mode = "count"; SelectMember(0);
            }
            if (Button(new Rect(middle.x + 24 + (middle.width - 40) / 2, modeY, (middle.width - 40) / 2, 35), "战斗组模式", Team.Source == "faction")) Team.Source = "faction";
            var faction = DefDatabase<FactionDef>.GetNamedSilentFail(Team.Faction);
            ArenaWorkbenchUi.DefIcon(new Rect(middle.x + 18, modeY + 49, 30, 30), faction);
            if (Button(new Rect(middle.x + 60, modeY + 46, middle.width - 76, 33), (faction?.LabelCap.ToString() ?? Team.Faction) + " · " + (faction?.modContentPack?.Name ?? "核心")))
                Pick("faction", d => { Team.Faction = d.defName; Team.GroupMakerIndex = null; });
            var body = new Rect(middle.x + 16, modeY + 100, middle.width - 32, middle.yMax - modeY - 172);
            if (Team.Source == "faction" || Team.Source == "kind")
            {
                var form = new Form(this, body.x, body.width, body.y);
                form.Title(Team.Source == "faction" ? "继承派系战斗组" : "兼容单兵种配兵");
                if (Team.Source == "faction" && form.Action(Team.GroupMakerIndex.HasValue ? "战斗组 #" + Team.GroupMakerIndex.Value + " · 点击更换…" : "选择派系 / 模组的战斗组…")) PickBattleGroup();
                if (Team.Source == "kind" && form.Action("兵种：" + LabelOf<PawnKindDef>(Team.PawnKind))) Pick("pawnKind", d => Team.PawnKind = d.defName);
                Team.Mode = form.Choice("配兵方式", Team.Mode, "count|按人数", "combatPower|CP 预算");
                if (Team.Mode == "count") Team.Count = (int)form.Number("teamCount", "队伍人数", Team.Count, 0, 200, true);
                else Team.Budget = (decimal)form.Number("teamBudget", "CP 预算", (double)Team.Budget, 0, 10000000);
                if (Team.Source == "faction" && Team.Mode == "count") Team.GroupReferencePoints = (float)form.Number("groupPoints", "编队参考点数", Team.GroupReferencePoints, 1, 10000000);
                if (form.Action("异种：" + (Team.Xenotype == null ? "继承原生" : LabelOf<XenotypeDef>(Team.Xenotype)))) Pick("xenotype", d => Team.Xenotype = d.defName);
                if (Team.Xenotype != null && form.Action("恢复原生异种")) Team.Xenotype = null;
                form.Note("以下为候选兵种示例，并非整队实际抽签结果。参考点数用于原生战斗组适用性判断。", 38);
                if (Team.Source == "faction") DrawBattleGroupMembers(new Rect(body.x, form.Y + 6, body.width, Math.Max(40, body.yMax - form.Y - 6)));
            }
            else
            {
                Team.Mode = "count";
                Label(new Rect(body.x, body.y, body.width - 160, 30), "编队组成 · " + Team.MemberCount + " 人", gold);
                if (Button(new Rect(body.xMax - 150, body.y, 150, 28), "按名称排序")) { var selected = Member; Team.Members = Team.Members.OrderBy(m => m.Name, StringComparer.CurrentCulture).ToList(); memberIndex = selected == null ? 0 : Team.Members.IndexOf(selected); numberBuffers.Clear(); }
                var list = new Rect(body.x, body.y + 41, body.width, Math.Max(30, body.height - 90));
                var view = new Rect(0, 0, list.width - 18, Math.Max(90, Team.Members.Count * 82));
                Widgets.BeginScrollView(list, ref rosterScroll, view);
                rosterViewportHeight = list.height;
                var firstVisible = Math.Max(0, (int)(rosterScroll.y / 82));
                var lastVisible = Math.Min(Team.Members.Count, firstVisible + (int)(list.height / 82) + 2);
                for (var i = firstVisible; i < lastVisible; i++)
                {
                    var member = Team.Members[i]; var row = new Rect(0, i * 82, view.width, 74);
                    ArenaWorkbenchUi.Box(row, i == memberIndex ? new Color(.16f, .215f, .24f) : ArenaWorkbenchUi.Field, i == memberIndex ? ArenaWorkbenchUi.Cyan : ArenaWorkbenchUi.Border);
                    DrawPortrait(new Rect(5, row.y + 4, 58, 66), member, false);
                    var kind = member.Read().Element("basePawnKind")?.Value;
                    Label(new Rect(75, row.y + 6, row.width - 308, 28), member.Name);
                    if (Button(new Rect(75, row.y + 39, Math.Max(70, row.width - 312), 25), LabelOf<PawnKindDef>(kind))) { var index = i; AddKind(replace: index); }
                    var text = Widgets.TextField(new Rect(row.width - 220, row.y + 23, 57, 28), member.Count.ToString());
                    if (int.TryParse(text, out var count) && count is >= 0 and <= 200) member.Count = count;
                    Label(new Rect(row.width - 158, row.y + 22, 20, 28), "人", muted, GameFont.Tiny);
                    if (Button(new Rect(row.width - 132, row.y + 22, 30, 28), "↑", active: i > 0)) { (Team.Members[i - 1], Team.Members[i]) = (Team.Members[i], Team.Members[i - 1]); SelectMember(i - 1); break; }
                    if (Button(new Rect(row.width - 95, row.y + 22, 30, 28), "↓", active: i < Team.Members.Count - 1)) { (Team.Members[i + 1], Team.Members[i]) = (Team.Members[i], Team.Members[i + 1]); SelectMember(i + 1); break; }
                    if (Button(new Rect(row.width - 57, row.y + 22, 45, 28), "删除")) { Team.Members.RemoveAt(i); SelectMember(Math.Min(i, Math.Max(0, Team.Members.Count - 1))); break; }
                    if (GUI.enabled && Widgets.ButtonInvisible(new Rect(66, row.y, Math.Max(1, row.width - 304), 35))) SelectMember(i);
                }
                if (Team.Members.Count == 0) Label(new Rect(16, 15, view.width - 32, 60), "编队还是空的。\n从模板、兵种、动物或机械族中添加成员。", muted);
                Widgets.EndScrollView();
                if (Button(new Rect(body.x, body.yMax - 36, body.width, 31), "＋ 添加成员…"))
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> {
                        new("从角色模板添加（多选）…", OpenPawnAdd),
                        new("指定 PawnKind…", () => AddKind()), new("添加人类…", () => AddKind("人类")),
                        new("添加动物…", () => AddKind("动物 / 其他")), new("添加机械族…", () => AddKind("机械族")) }));
            }
            var actionsY = middle.yMax - 62; var aw = (middle.width - 48) / 3;
            if (Button(new Rect(middle.x + 16, actionsY, aw, 31), "编辑角色 →", active: Team.Source == "groups" && Member != null)) page = 2;
            if (Button(new Rect(middle.x + 24 + aw, actionsY, aw, 31), "保存编队模板", active: !Team.IsEmpty)) { SaveTeamPreset(); }
            if (Button(new Rect(middle.x + 32 + aw * 2, actionsY, aw, 31), "删除编队")) { document.Teams.RemoveAt(teamIndex); SelectTeam(0); }
            Label(new Rect(right.x + 16, right.y + 12, right.width - 32, 30), Team.Source == "faction" ? "战斗组来源" : "当前角色", gold);
            if (Team.Source == "faction")
            {
                ArenaWorkbenchUi.DefIcon(new Rect(right.x + 45, right.y + 75, right.width - 90, 140), faction);
                Label(new Rect(right.x + 16, right.y + 237, right.width - 32, 35), faction?.LabelCap.ToString() ?? Team.Faction, gold);
                Label(new Rect(right.x + 16, right.y + 280, right.width - 32, 44), faction?.modContentPack?.Name ?? "核心", muted);
            }
            else DrawPreview(new Rect(right.x + 16, right.y + 57, right.width - 32, right.height - 72));
        }
        private void DrawLibrary(Rect area)
        {
            var left = new Rect(area.x, area.y, 220, area.height); ArenaWorkbenchUi.Panel(left);
            var labels = new[] { "角色模板", "编队预设", "赛事预设" };
            for (var i = 0; i < labels.Length; i++) if (Button(new Rect(left.x + 12, left.y + 18 + i * 48, 196, 36), labels[i], libraryPage == i)) { libraryPage = i; libraryScroll = Vector2.zero; }
            if (Button(new Rect(left.x + 12, left.y + 179, 196, 32), "保存当前完整赛事…")) SaveEventPreset();
            Label(new Rect(left.x + 14, left.y + 234, 192, 66), "这里只管理和编辑预设。添加角色或编队请在编队管理中打开选择弹窗。", muted, GameFont.Tiny);
            if (Button(new Rect(left.x + 12, left.yMax - 48, 196, 31), "复制模板库 XML")) Protect(() => CopyXml(ArenaWorkbench.LibraryXml(document)));
            var body = new Rect(left.xMax + 18, area.y, area.width - left.width - 18, area.height);
            librarySearch = Widgets.TextField(new Rect(body.x, body.y + 5, body.width, 32), librarySearch);
            var list = new Rect(body.x, body.y + 53, body.width, body.height - 53);
            var count = libraryPage == 0 ? document.PawnPresets.Count : libraryPage == 1 ? document.TeamPresets.Count : document.EventPresets.Count;
            string Name(int i) => libraryPage == 0 ? document.PawnPresets[i].Name : libraryPage == 1 ? document.TeamPresets[i].Name : document.EventPresets[i].Name;
            var ids = Enumerable.Range(0, count).Where(i => Name(i).IndexOf(librarySearch, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            var columns = Math.Max(1, (int)(list.width / 415)); var cardW = (list.width - 18 - (columns - 1) * 14) / columns;
            var view = new Rect(0, 0, list.width - 18, (float)Math.Ceiling(ids.Count / (double)columns) * 213);
            Widgets.BeginScrollView(list, ref libraryScroll, view);
            for (var n = 0; n < ids.Count; n++)
            {
                var i = ids[n]; var y = n / columns * 213f;
                if (y + 205 < libraryScroll.y || y > libraryScroll.y + list.height + 20) continue;
                var x = n % columns * (cardW + 14); var card = new Rect(x, y, cardW, 200);
                ArenaWorkbenchUi.Box(card, ArenaWorkbenchUi.PanelColor, ArenaWorkbenchUi.Border);
                var m = libraryPage == 0 ? document.PawnPresets[i] : libraryPage == 1 ? document.TeamPresets[i].Members.FirstOrDefault() : document.EventPresets[i].Teams.SelectMany(t => t.Members).FirstOrDefault();
                if (m != null) DrawPortrait(new Rect(x + 9, y + 10, 99, 128), m, false);
                else if (libraryPage == 1) ArenaWorkbenchUi.DefIcon(new Rect(x + 19, y + 30, 79, 79), DefDatabase<FactionDef>.GetNamedSilentFail(document.TeamPresets[i].Faction));
                Label(new Rect(x + 121, y + 14, cardW - 134, 31), Name(i), gold);
                var detail = libraryPage == 0 ? "独立角色配置 · 仅编辑模板" : libraryPage == 1 ? (document.TeamPresets[i].Source == "faction" ? "战斗组 · " + document.TeamPresets[i].Faction : "人员编队") : "完整赛事 · " + document.EventPresets[i].Teams.Count + " 支编队";
                Label(new Rect(x + 121, y + 56, cardW - 134, 47), detail, muted, GameFont.Tiny);
                if (libraryPage != 0)
                {
                    var enabled = libraryPage == 1 ? document.TeamPresets[i].Enabled : document.EventPresets[i].Enabled;
                    Widgets.CheckboxLabeled(new Rect(x + 121, y + 109, cardW - 137, 25), "启用此预设", ref enabled);
                    if (libraryPage == 1) document.TeamPresets[i].Enabled = enabled; else document.EventPresets[i].Enabled = enabled;
                }
                var w = (cardW - 44) / 3;
                if (Button(new Rect(x + 12, y + 155, w, 31), libraryPage == 0 ? "编辑角色" : libraryPage == 1 ? "编辑编队" : "编辑赛事", true))
                { if (libraryPage == 0) EditPawnPreset(document.PawnPresets[i]); else if (libraryPage == 1) EditTeamPreset(document.TeamPresets[i]); else EditEventPreset(document.EventPresets[i]); }
                if (Button(new Rect(x + 22 + w, y + 155, w, 31), "复制 XML")) Protect(() => CopyXml(libraryPage == 0 ? ArenaWorkbench.PawnTemplate(document.PawnPresets[i], "pawn").ToString() : libraryPage == 1 ? ArenaWorkbench.TeamXml(document.TeamPresets[i]) : ArenaWorkbench.EventXml(document.EventPresets[i])));
                if (Button(new Rect(x + 32 + 2 * w, y + 155, w, 31), "更多…"))
                {
                    var section = libraryPage; var index = i;
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption> {
                        new("复制预设", () => { if (section == 0) document.PawnPresets.Add(ArenaWorkbench.Copy(document.PawnPresets[index])); else if (section == 1) document.TeamPresets.Add(ArenaWorkbench.Copy(document.TeamPresets[index])); else document.EventPresets.Add(ArenaWorkbench.Copy(document.EventPresets[index])); }),
                        new("删除预设", () => { if (section == 0) document.PawnPresets.RemoveAt(index); else if (section == 1) document.TeamPresets.RemoveAt(index); else document.EventPresets.RemoveAt(index); }) }));
                }
            }
            Widgets.EndScrollView();
        }
    }
}
