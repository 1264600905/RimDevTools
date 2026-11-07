using System.Text.Json;
using LogSyncMod.AutoTest.Scenarios;

namespace RimDevTools;

public static class GroupCombatResultValidator
{
    public static AutoResultValidation Validate(string outputPath, string runId, ScenarioPlan? expectedPlan = null)
    {
        var path = Path.Combine(outputPath, "groupcombat_" + runId + "_Result.json");
        try
        {
            if (!File.Exists(path)) return new(false, "GROUPCOMBAT_RESULT_MISSING", "缺少本轮独立群战结果", new { path });
            var report = JsonSerializer.Deserialize<GroupCombatRunReport>(File.ReadAllText(path));
            if (report is null || report.SchemaVersion != 1 || report.Purpose != "groupcombat" || report.RunId != runId) return new(false, "GROUPCOMBAT_RESULT_INVALID", "群战结果身份或版本不符", new { path });
            if (report.PlannedRounds is null || report.Rounds is null || report.PlannedRounds.Any(r => r is null) || report.Rounds.Any(r => r is null || r.Teams is null || r.Units is null)) return new(false, "GROUPCOMBAT_RESULT_INVALID", "群战结果缺少完整执行计划或数值集合", new { path });
            var expected = report.PlannedRounds.Select(r => r.CaseId).ToArray();
            var actual = report.Rounds.Select(r => r.CaseId).ToArray();
            if (report.Status != "completed" || expected.Length == 0 || expected.Distinct().Count() != expected.Length || actual.Distinct().Count() != actual.Length || !expected.Order().SequenceEqual(actual.Order()) || report.Rounds.Any(r => r.ExecutionStatus != "completed" || !r.CleanupComplete || r.IsPocketMap || r.Teams.Count != 2 || r.Units.Count == 0))
                return new(false, "GROUPCOMBAT_EXECUTION_INCOMPLETE", report.Error ?? "群战执行或清理未完成；保留独立数值产物", new { path, report.Status, planned = expected.Length, executed = actual.Length, rounds = report.Rounds.Select(r => new { r.CaseId, r.ExecutionStatus, r.CleanupComplete, r.Error }) });
            var integrityError = ValidateIntegrity(report, expectedPlan);
            if (integrityError is not null) return new(false, "GROUPCOMBAT_RESULT_INVALID", integrityError, new { path });
            if (report.Rounds.Any(r => r.RequireCompleteAttribution && !r.MeasurementRequirementsMet)) return new(false, "GROUPCOMBAT_MEASUREMENT_REQUIREMENTS_NOT_MET", "群战已执行并清理，但未达到显式的完整归属测量要求", new { path });
            return new(true, "", "群战数值评测完成", new { path, reportPath = Path.Combine(outputPath, "groupcombat_" + runId + "_Report.md"), rounds = report.Rounds.Count, results = report.Rounds.Select(r => new { r.CaseId, r.TestDefName, r.ElapsedTicks, r.EndReason, r.MeasurementQuality, r.MeasurementRequirementsMet, r.TotalEvents, r.CleanupComplete, teams = r.Teams.Select(t => new { t.Id, t.Outcome, t.Initial, t.Alive, t.Dead, t.Downed, t.EnemyDamage, t.Dps, t.MeanDps, t.DamageTaken }) }) });
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(false, "GROUPCOMBAT_RESULT_INVALID", ex.Message, new { path }); }
    }

    public static string? ValidateIntegrity(GroupCombatRunReport report, ScenarioPlan? expectedPlan = null)
    {
        static bool Equal(double a, double b) => double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) <= 1e-8 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
        if (expectedPlan != null)
        {
            var frozen = GroupCombatPlan.Expand(expectedPlan);
            if (!frozen.Select(r => r.CaseId).Order().SequenceEqual(report.PlannedRounds.Select(r => r.CaseId).Order())) return "群战结果计划与 CLI 固定的指名输入不符";
            foreach (var expected in frozen)
            {
                var actual = report.Rounds.SingleOrDefault(r => r.CaseId == expected.CaseId);
                if (actual == null || actual.TestDefName != expected.TestDefName || actual.InputHash != expected.InputHash || actual.Seed != expected.Seed || actual.Swapped != expected.Swapped) return "群战结果定义、输入哈希或重复实验参数与 CLI 固定输入不符";
            }
        }
        foreach (var round in report.Rounds)
        {
            if (round.ElapsedTicks < 0 || !Equal(round.SimulatedSeconds, round.ElapsedTicks / 60d) || round.Matrix == null || round.Events == null || round.Units.Any(u => u == null) || round.Teams.Any(t => t == null) || round.Matrix.Any(m => m == null) || round.Events.Any(e => e == null)) return "群战时钟或账本集合无效";
            if (round.Units.Select(u => u.Id).Distinct().Count() != round.Units.Count || round.Teams.Select(t => t.Id).Distinct().Count() != round.Teams.Count || round.Units.Any(u => !round.Teams.Any(t => t.Id == u.Team))) return "群战初始单位或队伍身份不一致";
            if (round.TotalEvents != round.Matrix.Sum(c => c.Events) || round.TotalEvents != round.Events.Count + round.DroppedDetailEvents || round.DroppedDetailEvents < 0 || round.Events.Select(e => e.Id).Distinct().Count() != round.Events.Count || round.Events.Any(e => e.Id <= 0 || e.Amount < 0 || !double.IsFinite(e.Amount))) return "群战累计事件与矩阵/保留明细不一致";
            foreach (var team in round.Teams)
            {
                var units = round.Units.Where(u => u.Team == team.Id).ToList();
                if (team.Initial != units.Count || team.Initial == 0 || team.Initial != team.Standing + team.Downed + team.Dead + team.Escaped + team.Removed || team.Alive != team.Standing + team.Downed || team.Alive != units.Count(u => u.Alive)) return "群战初始人数与最终状态分项不一致";
                if (!Equal(team.EnemyDamage, units.Sum(u => u.EnemyDamage) + team.UnrosteredEnemyDamage) || !Equal(team.DamageTaken, round.Matrix.Where(m => m.Channel == "pawn" && m.Victim == team.Id).Sum(m => m.Amount))) return "群战输出/承伤未与逐单位及矩阵核对一致";
                var dps = round.SimulatedSeconds > 0 ? team.EnemyDamage / round.SimulatedSeconds : 0;
                if (!Equal(team.Dps, dps) || !Equal(team.MeanDps, dps / team.Initial) || !Equal(team.MeanDamageTaken, team.DamageTaken / team.Initial) || units.Any(u => !Equal(u.Dps, round.SimulatedSeconds > 0 ? u.EnemyDamage / round.SimulatedSeconds : 0))) return "群战 DPS/人均值未采用共同模拟窗口和初始人数";
                foreach (var pair in new[] {
                    (team.Damage, GroupCombatDistribution.Calculate(units.Select(u => KeyValuePair.Create(u.Id, u.EnemyDamage)))),
                    (team.DpsDistribution, GroupCombatDistribution.Calculate(units.Select(u => KeyValuePair.Create(u.Id, u.Dps)))),
                    (team.Taken, GroupCombatDistribution.Calculate(units.Select(u => KeyValuePair.Create(u.Id, u.DamageTaken)))) })
                {
                    var (actual, expected) = pair;
                    if (actual == null || actual.Count != expected.Count || !Equal(actual.Min, expected.Min) || !Equal(actual.Max, expected.Max) || !Equal(actual.Mean, expected.Mean) || !Equal(actual.Median, expected.Median) || actual.MinIds == null || actual.MaxIds == null || !actual.MinIds.Order().SequenceEqual(expected.MinIds.Order()) || !actual.MaxIds.Order().SequenceEqual(expected.MaxIds.Order())) return "群战分布或最高/最低单位与全部初始成员不一致";
                }
            }
        }
        return null;
    }
}
