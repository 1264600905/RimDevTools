using System;
using System.Collections.Generic;
using System.Linq;
using LogSyncMod.AutoTest.Scenarios;

namespace LogSyncMod.AutoTest
{
    public sealed class ArenaWorkbenchEventPreset
    {
        public string Name { get; set; } = "赛事预设";
        public bool Enabled { get; set; } = true;
        public ArenaWorkbenchSchedule Schedule { get; set; } = new();
        public List<ArenaWorkbenchTeam> Teams { get; set; } = new();
    }
    public static partial class ArenaWorkbench
    {
        public static ArenaWorkbenchEventPreset CaptureEvent(ArenaWorkbenchDocument document)
        {
            var saved = ForStorage(document, true); ValidateEvent(saved);
            return new ArenaWorkbenchEventPreset { Name = saved.Schedule.Name, Schedule = Copy(saved.Schedule), Teams = Copy(saved.Teams) };
        }
        public static void ApplyEvent(ArenaWorkbenchDocument document, ArenaWorkbenchEventPreset preset)
        {
            if (!preset.Enabled) throw new ScenarioConfigurationException("该赛事预设已禁用。");
            var next = new ArenaWorkbenchDocument { Schedule = Copy(preset.Schedule), Teams = Copy(preset.Teams) };
            ValidateEvent(next); document.Schedule = next.Schedule; document.Teams = next.Teams;
        }
        public static void AddPawnPresets(ArenaWorkbenchTeam team, IEnumerable<ArenaWorkbenchMember> selected)
        {
            if (team.Source != "groups") throw new ScenarioConfigurationException("角色模板只能添加到人员模式编队。");
            var additions = selected.Select(p => { var m = Copy(p); m.Count = 1; return m; }).ToList();
            if (team.MemberCount + additions.Count > 200) throw new ScenarioConfigurationException("添加后编队总人数将超过 200。");
            foreach (var m in additions) ValidateMember(m);
            team.Members.AddRange(additions);
        }
        public static string EventXml(ArenaWorkbenchEventPreset preset) => Export(new ArenaWorkbenchDocument { Schedule = Copy(preset.Schedule), Teams = Copy(preset.Teams) }, false).ToString();
        public static void ValidateStructure(ArenaWorkbenchDocument document)
        {
            if (document?.Schedule == null || document.Teams == null || document.PawnPresets == null || document.TeamPresets == null || document.SchedulePresets == null || document.EventPresets == null)
                throw new ScenarioConfigurationException("工作台配置不完整。");
            if (document.Teams.Count > 64 || document.PawnPresets.Count > 1000 || document.TeamPresets.Count > 1000 || document.EventPresets.Count > 100)
                throw new ScenarioConfigurationException("编队最多 64 支，角色 / 编队预设最多 1000 项，赛事预设最多 100 项。");
            foreach (var t in document.Teams.Concat(document.TeamPresets)) ValidateTeam(t);
            ValidateSchedule(document.Schedule);
            foreach (var preset in document.SchedulePresets) ValidateSchedule(preset);
            foreach (var p in document.PawnPresets) ValidateMember(p);
            foreach (var e in document.EventPresets)
            {
                if (e?.Schedule == null || e.Teams == null || string.IsNullOrWhiteSpace(e.Name) || e.Teams.Count > 64) throw new ScenarioConfigurationException("赛事预设不完整。");
                foreach (var t in e.Teams) ValidateTeam(t);
                ValidateSchedule(e.Schedule);
            }
            if (document.Teams.Select(t => t.Id).Distinct().Count() != document.Teams.Count) throw new ScenarioConfigurationException("编队 ID 重复。");
        }
        public static void ValidateSchedule(ArenaWorkbenchSchedule s)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 120 || !new[] { "groupcombat", "knockout", "bossRoundRobin", "limitChallenge" }.Contains(s.Format)
                || !new[] { "closestCount", "random" }.Contains(s.Matchmaking) || !new[] { "normal", "fast", "superfast" }.Contains(s.Speed)
                || !double.IsFinite(s.Duration) || s.Duration <= 0 || s.Duration > 86400)
                throw new ScenarioConfigurationException("赛事名称、赛制、配对、速度或单场时限无效。");
            CombatMapCatalog.Validate(s.Battlefield, s.FeatureDensity, s.SpawnClearance);
            if (s.Width < 30 || s.Width > 250 || s.Height < 30 || s.Height > 250 || !double.IsFinite(s.Temperature) || s.Temperature < -100 || s.Temperature > 100)
                throw new ScenarioConfigurationException("地图尺寸或温度无效。");
        }
        public static void ValidateMember(ArenaWorkbenchMember member)
        {
            if (member == null || string.IsNullOrWhiteSpace(member.Name) || member.Name.Length > 120 || member.Count < 0 || member.Count > 200) throw new ScenarioConfigurationException("角色组名称或人数无效。");
            TemporaryPawnData.Create(member.Read(), "workbench.validation", Array.Empty<string>(), Array.Empty<string>());
        }
        public static void ValidateTeam(ArenaWorkbenchTeam t)
        {
            if (t != null && !new[] { "inherit", "assaultColony", "defendPoint", "lure", "native", "battle", "seekAndAttack", "assaultThings" }.Contains(t.Ai))
                throw new ScenarioConfigurationException("编队战斗 AI 无效。");
            if (t == null || t.Members == null || string.IsNullOrWhiteSpace(t.Id) || string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 120 || t.Members.Count > 200)
                throw new ScenarioConfigurationException("编队名称、ID 或成员列表无效。");
            if (!new[] { "groups", "kind", "faction" }.Contains(t.Source) || !new[] { "count", "combatPower" }.Contains(t.Mode) || !new[] { "boss", "challenger" }.Contains(t.Role))
                throw new ScenarioConfigurationException("编队模式或赛事角色无效：" + t.Name);
            if (!new[] { "native", "pawnKind" }.Contains(t.CostMode) || string.IsNullOrWhiteSpace(t.Faction)) throw new ScenarioConfigurationException("派系或战力计费方式无效：" + t.Name);
            if (t.Count < 0 || t.Count > 200 || t.Budget < 0 || t.Budget > 10000000 || !float.IsFinite(t.GroupReferencePoints) || t.GroupReferencePoints <= 0 || t.GroupReferencePoints > 10000000 || t.GroupMakerIndex < 0)
                throw new ScenarioConfigurationException("编队人数、预算或战斗组点数超出范围：" + t.Name);
            if (t.Source == "groups" && t.Members.Sum(m => m?.Count ?? 0) > 200) throw new ScenarioConfigurationException("编队总人数超过 200：" + t.Name);
            foreach (var member in t.Members) ValidateMember(member);
        }
    }
}
