#nullable disable
using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LogSyncMod.AutoTest.Scenarios
{
    public static class GroupCombatReport
    {
        public static string Write(GroupCombatRunReport report)
        {
            var text = new StringBuilder();
            string N(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
            string E(string value) => (value ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
            string D(GroupCombatDistribution d) => d == null ? "未开始" : N(d.Min) + " / " + N(d.Max) + " / " + N(d.Mean) + " / " + N(d.Median);
            text.AppendLine("# 群战数值评测").AppendLine().AppendLine("运行：`" + report.RunId + "`，执行状态：" + report.Status + "，游戏：" + report.GameVersion).AppendLine();
            text.AppendLine("本报告按引擎结算伤害计算，团队 DPS 窗口为模拟 ticks / 60，个人 DPS 按自身在场存活 ticks / 60 计算；人均 DPS 和人均承伤只包含当前场上存活单位（含倒地），零存活时记 0。承伤为存活单位累计承伤均值，不是每秒承伤；落败不属于 Bug 回归失败。所有初始成员（包括阵亡和零输出）参与分布。护盾、装甲和建筑分项不计入 Pawn 对敌主 DPS。").AppendLine();
            if (report.Error != null) text.AppendLine("执行诊断：" + E(report.Error)).AppendLine();
            foreach (var round in report.Rounds)
            {
                text.AppendLine("## " + E(round.Name) + " — " + round.CaseId).AppendLine();
                text.AppendLine("测试定义：`" + round.TestDefName + "`；种子：" + round.Seed + "；交换站位：" + round.Swapped + "；模拟秒：" + N(round.SimulatedSeconds) + "；终止：" + round.EndReason + "；执行：" + round.ExecutionStatus + "；测量质量：" + round.MeasurementQuality + "；清理：" + round.CleanupComplete).AppendLine();
                text.AppendLine("地图：" + round.MapId + "，普通主地图：" + !round.IsPocketMap + "，生成器：" + round.GeneratorDef + "，" + round.Environment).AppendLine();
                text.AppendLine("| 队伍 | 结局 | 初始/站立/倒地/死亡/逃离/移除 | 对敌伤害 | 团队/存活人均 DPS | 总/存活人均承伤 | 初始全员输出 min/max/mean/median | 初始全员存活期 DPS min/max/mean/median | 初始全员承伤 min/max/mean/median |").AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
                foreach (var team in round.Teams) text.AppendLine("| " + E(team.Name) + " | " + team.Outcome + " | " + string.Join("/", team.Initial, team.Standing, team.Downed, team.Dead, team.Escaped, team.Removed) + " | " + N(team.EnemyDamage) + " | " + N(team.Dps) + "/" + N(team.MeanDps) + " | " + N(team.DamageTaken) + "/" + N(team.MeanDamageTaken) + " | " + D(team.Damage) + " | " + D(team.DpsDistribution) + " | " + D(team.Taken) + " |");
                text.AppendLine().AppendLine("### 全部初始单位").AppendLine();
                text.AppendLine("| 单位 ID / 原生 ID | 队伍 | 编组 / PawnKind | 名称 | 结局状态 | 初始坐标 | 对敌伤害 | 存活 ticks / DPS | 承伤 | 友伤 / 自伤 |").AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
                foreach (var u in round.Units) text.AppendLine("| " + u.Id + " / " + u.NativeId + " | " + u.Team + " | " + u.Group + " / " + u.Kind + " | " + E(u.Name) + " | " + u.State + " | " + u.X + "," + u.Z + " | " + N(u.EnemyDamage) + " | " + u.AliveTicks + " / " + N(u.Dps) + " | " + N(u.DamageTaken) + " | " + N(u.FriendlyDamage) + " / " + N(u.SelfDamage) + " |");
                text.AppendLine().AppendLine("### 伤害矩阵").AppendLine().AppendLine("| 来源 | 受击方 | 通道 | 引擎结算量 | 事件数 |").AppendLine("| --- | --- | --- | --- | --- |");
                foreach (var cell in round.Matrix) text.AppendLine("| " + cell.Source + " | " + cell.Victim + " | " + cell.Channel + " | " + N(cell.Amount) + " | " + cell.Events + " |");
                text.AppendLine().AppendLine("累计事件 " + round.TotalEvents + "；保留明细 " + round.Events.Count + "；未保留明细 " + round.DroppedDetailEvents + "（不影响累计值）。").AppendLine();
                foreach (var limit in round.MeasurementLimits) text.AppendLine("- " + limit);
                if (round.Error != null) text.AppendLine().AppendLine("执行错误：" + E(round.Error));
                foreach (var diagnostic in round.Diagnostics.Distinct()) text.AppendLine("- " + E(diagnostic));
                text.AppendLine();
                foreach (var screenshot in round.Screenshots) text.AppendLine("![群战榜单](" + screenshot.Replace("\\", "/") + ")").AppendLine();
            }
            text.AppendLine("## 跨轮次统计").AppendLine().AppendLine("轮次团队分布与合并单位分布分别计算；不将单场中位数的均值称为合并中位数。").AppendLine();
            text.AppendLine("| 测试定义 | 队伍 | 完成/胜/平/负 | 胜率 | 各轮团队 DPS min/max/mean/median | 各轮存活数 min/max/mean/median | 合并初始全员存活期 DPS min/max/mean/median | 合并初始全员承伤 min/max/mean/median |").AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var experiment in report.Experiments) text.AppendLine("| " + experiment.TestDefName + " | " + experiment.Team + " | " + string.Join("/", experiment.CompletedRounds, experiment.Wins, experiment.Draws, experiment.Losses) + " | " + N(experiment.WinRate) + " | " + D(experiment.RoundTeamDps) + " | " + D(experiment.RoundAliveCount) + " | " + D(experiment.PooledUnitDps) + " | " + D(experiment.PooledUnitDamageTaken) + " |");
            return text.ToString();
        }
    }
}
