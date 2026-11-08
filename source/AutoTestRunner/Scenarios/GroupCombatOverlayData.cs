using System;
using System.Collections.Generic;
using System.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    // Detached display data: sorting, grouping and formatting run only when the HUD refreshes.
    internal sealed class GroupCombatOverlayData
    {
        internal sealed class Group
        {
            internal GroupCombatUnit Representative;
            internal string Label;
        }

        internal sealed class Rank
        {
            internal GroupCombatUnit Unit;
            internal double Value;
            internal string Name, DetailedValue, CompactValue;
        }

        internal sealed class Team
        {
            internal GroupCombatTeamResult Result;
            internal Group[] LivingGroups, IntroGroups;
            internal Rank[] Output, Taken;
            internal string Population, Reinforcements, RemainingKinds, Mean, Total, Outcome;
            internal string[] Settlement;
            internal GroupCombatUnit Hero;
        }

        internal GroupCombatRoundResult Snapshot { get; private set; }
        internal Team[] Teams { get; private set; } = Array.Empty<Team>();
        internal double MaximumOutput { get; private set; }
        internal double MaximumTaken { get; private set; }
        internal string TimeLabel { get; private set; }
        internal int RefreshCount { get; private set; }
        private string phase;
        private float nextRefresh;

        internal bool Refresh(GroupCombatRoundResult value, string currentPhase, float now)
        {
            if (value == null || Snapshot != null && phase == currentPhase && (now < nextRefresh || ReferenceEquals(Snapshot, value))) return false;
            Snapshot = value; phase = currentPhase; nextRefresh = now + .25f; RefreshCount++;
            MaximumOutput = value.Units.Count == 0 ? 0 : value.Units.Max(u => u.EnemyDamage);
            MaximumTaken = value.Units.Count == 0 ? 0 : value.Units.Max(u => u.DamageTaken);
            TimeLabel = value.SimulatedSeconds.ToString("F1") + " 模拟秒  ·  ";
            Teams = value.Teams.Select(t => BuildTeam(value, t)).ToArray();
            return true;
        }

        private static Team BuildTeam(GroupCombatRoundResult snapshot, GroupCombatTeamResult team)
        {
            var members = snapshot.Units.Where(u => u.Team == team.Id).ToList();
            var other = snapshot.Teams.FirstOrDefault(t => t.Id != team.Id);
            var groups = members.Where(u => u.Alive).GroupBy(u => GroupCombatPresentation.UnitTypeName(u.KindLabel, u.Kind), StringComparer.Ordinal)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToArray();
            var living = GroupCombatPresentation.SurvivorTotals(members);
            var otherLiving = GroupCombatPresentation.SurvivorTotals(snapshot.Units.Where(u => u.Team == other?.Id));
            return new Team
            {
                Result = team,
                LivingGroups = groups.Take(5).Select(MakeGroup).ToArray(),
                IntroGroups = members.GroupBy(u => GroupCombatPresentation.UnitTypeName(u.KindLabel, u.Kind), StringComparer.Ordinal).Select(MakeGroup).ToArray(),
                Output = Rankings(members, u => u.EnemyDamage), Taken = Rankings(members, u => u.DamageTaken),
                Population = "存活 " + team.Alive + "/" + team.Total + "   倒地 " + team.Downed + "   死亡 " + team.Dead,
                Reinforcements = team.Joined > 0 ? "初始 " + team.Initial + "   新增 " + team.Joined + "   待复活 " + team.PendingRevival : null,
                RemainingKinds = groups.Length > 5 ? "其余 " + (groups.Length - 5) + " 种（完整阵容见结果）" : groups.Length == 0 ? "零存活" : null,
                Mean = "平均输出 " + Compared(team.MeanOutput, other?.MeanOutput) + "   存活人均承伤 " + Compared(team.MeanDamageTaken, other?.MeanDamageTaken),
                Total = "累计输出 " + Compared(team.EnemyDamage, other?.EnemyDamage) + "   总承伤 " + Compared(team.DamageTaken, other?.DamageTaken),
                Outcome = Label(team.Outcome) + "  ·  排名按存活期间累计伤害",
                Settlement = new[] {
                    "存活 " + Compared(team.Alive, other?.Alive, "F0") + "/" + Compared(team.Total, other?.Total, "F0")
                        + " · 倒地 " + Compared(team.Downed, other?.Downed, "F0") + " · 死亡 " + Compared(team.Dead, other?.Dead, "F0"),
                    "全队累计输出  " + Compared(team.EnemyDamage, other?.EnemyDamage),
                    "全队累计承伤  " + Compared(team.DamageTaken, other?.DamageTaken),
                    "存活成员输出  " + Compared(living.output, other == null ? null : otherLiving.output),
                    "存活成员承伤  " + Compared(living.taken, other == null ? null : otherLiving.taken)
                },
                Hero = members.Where(u => !u.JoinedDuringBattle).OrderByDescending(u => u.State == "standing").ThenByDescending(u => u.EnemyDamage).FirstOrDefault()
            };
        }

        private static Group MakeGroup(IGrouping<string, GroupCombatUnit> group)
        {
            var first = group.First();
            return new Group { Representative = first, Label = (first.KindLabel ?? first.Kind) + " ×" + group.Count() };
        }

        private static Rank[] Rankings(List<GroupCombatUnit> members, Func<GroupCombatUnit, double> metric)
        {
            return members.OrderByDescending(metric).ThenBy(u => u.Id, StringComparer.Ordinal).Take(10).Select((u, i) => new Rank
            {
                Unit = u, Value = metric(u), Name = (i + 1) + "." + u.Name,
                DetailedValue = metric(u).ToString("F1") + " " + Label(u.State), CompactValue = metric(u).ToString("F0")
            }).ToArray();
        }

        private static string Compared(double value, double? other, string format = "F1")
        {
            var text = value.ToString(format);
            return !other.HasValue || value == other.Value ? text : "<color=#" + (value > other.Value ? "66FF73FF" : "FF5252FF") + ">" + text + "</color>";
        }

        internal static string Label(string state) => state switch { "standing" => "存活", "downed" => "倒地", "dead" => "死亡", "escaped" => "离场", "removed" => "移除", "win" => "胜利", "loss" => "落败", "draw" => "平局", "undetermined" => "进行中", "prepared" or "preparing" => "准备", "countdown" or "roster" => "即将开战", "running" => "战斗", "finishing" => "结算", _ => state };
    }
}
