using System;
using System.Collections.Generic;
using System.Linq;
using LogSyncMod.AutoTest.Scenarios;
using UnityEngine;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed partial class Dialog_ArenaWorkbench
    {
        private void SaveCurrentPawnPreset()
        {
            Protect(() => {
                ArenaWorkbench.ValidateMember(Member); ArenaWorkbenchDefinitions.ValidatePawn(Member.Read());
                var preset = ArenaWorkbench.Copy(Member); preset.Count = 1;
                Find.WindowStack.Add(new Dialog_ArenaName("保存角色模板", preset.Name, name => { preset.Name = name; document.PawnPresets.Add(preset); notice = "角色模板已保存，可在编队管理中添加。"; }));
            });
        }
        private void SaveTeamPreset()
        {
            Protect(() => {
                ArenaWorkbench.ValidateTeam(Team); if (Team.IsEmpty) throw new InvalidOperationException("空编队不能保存为预设。");
                foreach (var member in Team.Members) ArenaWorkbenchDefinitions.ValidatePawn(member.Read());
                var preset = ArenaWorkbench.Copy(Team);
                Find.WindowStack.Add(new Dialog_ArenaName("保存编队预设", preset.Name, name => { preset.Name = name; document.TeamPresets.Add(preset); notice = "编队预设已保存。"; }));
            });
        }
        private void OpenRosterManagement()
        {
            var snapshot = ArenaWorkbench.Copy(document);
            Find.WindowStack.Add(new Dialog_ArenaWorkbench(snapshot, "赛事参赛编队", 1, edited => {
                foreach (var team in edited.Teams) ArenaWorkbench.ValidateTeam(team);
                document.Teams = ArenaWorkbench.Copy(edited.Teams); document.PawnPresets = ArenaWorkbench.Copy(edited.PawnPresets); document.TeamPresets = ArenaWorkbench.Copy(edited.TeamPresets); SelectTeam(0); Changed();
            }));
        }
        private void OpenPawnAdd()
        {
            var target = Team; var presets = document.PawnPresets.Select(ArenaWorkbench.Copy).ToList();
            Find.WindowStack.Add(new Dialog_ArenaPresetPicker("添加角色模板 · 支持多选", presets.Select(p => p.Name).ToList(), presets,
                presets.Select(p => p.Read().Element("basePawnKind")?.Value + " · 独立角色配置").ToList(), presets.Select(p => true).ToList(), true,
                ids => { foreach (var i in ids) ArenaWorkbenchDefinitions.ValidatePawn(presets[i].Read()); ArenaWorkbench.AddPawnPresets(target, ids.Select(i => presets[i])); notice = "已向“" + target.Name + "”添加 " + ids.Count + " 个角色组。"; Changed(); }));
        }
        private void OpenTeamAdd()
        {
            var presets = document.TeamPresets.Select(ArenaWorkbench.Copy).ToList();
            Find.WindowStack.Add(new Dialog_ArenaPresetPicker("添加编队预设", presets.Select(p => p.Name).ToList(),
                presets.Select(p => p.Source == "groups" ? p.Members.FirstOrDefault() : null).ToList(),
                presets.Select(p => (p.Source == "faction" ? "战斗组 · " + p.Faction : "人员编队") + " · " + (p.Mode == "count" ? p.MemberCount + " 人" : p.Budget + " CP")).ToList(),
                presets.Select(p => p.Enabled).ToList(), true, ids => {
                    if (document.Teams.Count + ids.Count > 64) throw new InvalidOperationException("最多 64 支编队。");
                    document.Teams.AddRange(ids.Select(i => { var t = ArenaWorkbench.Duplicate(presets[i]); t.Enabled = true; return t; }));
                    notice = "已添加 " + ids.Count + " 支编队，当前编辑位置保留。"; Changed();
                }));
        }
        private void OpenEventLoad()
        {
            var presets = document.EventPresets.Select(ArenaWorkbench.Copy).ToList();
            Find.WindowStack.Add(new Dialog_ArenaPresetPicker("载入完整赛事 · 将替换当前赛程和参赛编队", presets.Select(p => p.Name).ToList(),
                presets.Select(p => p.Teams.SelectMany(t => t.Members).FirstOrDefault()).ToList(), presets.Select(p => p.Teams.Count + " 支编队 · 完整角色与赛程规则").ToList(),
                presets.Select(p => p.Enabled).ToList(), false, ids => { ArenaWorkbench.ApplyEvent(document, presets[ids[0]]); SelectTeam(0); numberBuffers.Clear(); notice = "已载入完整赛事；模板库保留。"; }));
        }
        private void SaveEventPreset()
        {
            Protect(() => { NormalizeApparel(); ValidateCurrentEvent(); var preset = ArenaWorkbench.CaptureEvent(document); Find.WindowStack.Add(new Dialog_ArenaName("保存完整赛事预设", preset.Name,
                name => { preset.Name = name; document.EventPresets.Add(preset); Changed(); notice = "已保存完整赛事：赛程、编队与全部成员配置。"; })); });
        }
        private void EditPawnPreset(ArenaWorkbenchMember preset)
        {
            var draft = new ArenaWorkbenchDocument { Teams = new() { new ArenaWorkbenchTeam { Members = new() { ArenaWorkbench.Copy(preset) } } } };
            Find.WindowStack.Add(new Dialog_ArenaWorkbench(draft, "角色模板", 2, edited => {
                var member = edited.Teams[0].Members[0]; ArenaWorkbench.ValidateMember(member); ArenaWorkbenchDefinitions.ValidatePawn(member.Read());
                preset.Name = member.Name; preset.PawnXml = member.PawnXml; preset.Count = 1;
            }));
        }
        private void EditTeamPreset(ArenaWorkbenchTeam preset)
        {
            Find.WindowStack.Add(new Dialog_ArenaWorkbench(new ArenaWorkbenchDocument { Teams = new() { ArenaWorkbench.Copy(preset) }, PawnPresets = ArenaWorkbench.Copy(document.PawnPresets) }, "编队模板", 1, edited => {
                var value = edited.Teams.Single(); ArenaWorkbench.ValidateTeam(value); if (value.IsEmpty) throw new InvalidOperationException("空编队不能保存为模板。");
                foreach (var member in value.Members) ArenaWorkbenchDefinitions.ValidatePawn(member.Read());
                var index = document.TeamPresets.IndexOf(preset); document.TeamPresets[index] = ArenaWorkbench.Copy(value);
            }));
        }
        private void EditEventPreset(ArenaWorkbenchEventPreset preset)
        {
            Find.WindowStack.Add(new Dialog_ArenaWorkbench(new ArenaWorkbenchDocument { Schedule = ArenaWorkbench.Copy(preset.Schedule), Teams = ArenaWorkbench.Copy(preset.Teams), PawnPresets = ArenaWorkbench.Copy(document.PawnPresets), TeamPresets = ArenaWorkbench.Copy(document.TeamPresets) }, "赛事预设", 0, edited => {
                ArenaWorkbench.ValidateEvent(edited); var validationCopy = ArenaWorkbench.Copy(edited); if (validationCopy.Schedule.Format == "groupcombat") validationCopy.Schedule.Format = "knockout"; ArenaWorkbenchDefinitions.Validate(ArenaWorkbench.Compile(validationCopy));
                preset.Name = edited.Schedule.Name; preset.Schedule = ArenaWorkbench.Copy(edited.Schedule); preset.Teams = ArenaWorkbench.Copy(edited.Teams);
            }));
        }
    }
}
