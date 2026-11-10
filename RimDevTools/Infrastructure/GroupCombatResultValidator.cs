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
            return new(true, "", "群战数值评测完成", new { path, reportPath = Path.Combine(outputPath, "groupcombat_" + runId + "_Report.md"), rounds = report.Rounds.Count, results = report.Rounds.Select(r => new { r.CaseId, r.TestDefName, r.ElapsedTicks, r.EndReason, r.MeasurementQuality, r.MeasurementRequirementsMet, r.TotalEvents, r.CleanupComplete, teams = r.Teams.Select(t => new { t.Id, t.Outcome, t.Initial, t.Joined, t.Total, t.Alive, t.Dead, t.Downed, t.EnemyDamage, t.Dps, t.MeanDps, t.MeanOutput, t.DamageTaken }) }) });
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or NullReferenceException or ArgumentException or OverflowException or ScenarioConfigurationException or System.Xml.XmlException) { return new(false, "GROUPCOMBAT_RESULT_INVALID", ex.Message, new { path }); }
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
                var battle = System.Xml.Linq.XElement.Parse(expected.Xml);
                var preparationError = CombatPreparation.Validate(actual, battle);
                if (preparationError != null) return preparationError;
                var generator = ScenarioLoader.A(battle.Element("Scene"), "generatorDef", "LogSync_AutoTestStudioMap");
                if (generator == "LogSync_AutoTestStudioMap" && actual.StudioIsolation == null) return "缺少测试场地隔离记录";
                if (actual.StudioIsolation is { } isolation && (!isolation.WorldTileUnchanged || isolation.RemainingThingsBeforeActors != 0 || isolation.NonSoilCellsBeforeActors != 0)) return "测试场地隔离失败";
                var groups = battle.Element("Actors")!.Elements().Where(g => g.Name == "PawnGroup" || g.Name == "Pawn").ToList();
                if (actual.ResolvedRosters.Count != groups.Count) return "群战预算编组快照缺失或多余";
                foreach (var group in groups)
                {
                    var resolved = actual.ResolvedRosters.SingleOrDefault(r => r.Id == ScenarioLoader.A(group, "id"));
                    if (group.Attribute("squad") != null)
                    {
                        var template = SquadTemplates.Parse(group.Element("SquadTemplate")!, expected.Source);
                        var error = SquadTemplates.ValidateRoster(resolved!, template,
                            group.Attribute("combatPowerBudget") == null ? null : CombatRoster.Positive(ScenarioLoader.A(group, "combatPowerBudget")),
                            ScenarioLoader.I(group, "count", 1), ScenarioLoader.A(group, "costMode", "pawnKind"));
                        if (error != null) return error;
                        if (resolved!.Team != ScenarioLoader.A(group, "team")) return "混编队伍阵营不符";
                        var memberError = SquadTemplates.ValidateUnits(resolved, actual.Units.Where(u => u.Team == resolved.Team), resolved.Id);
                        if (memberError != null) return memberError;
                        continue;
                    }
                    var budget = group.Attribute("combatPowerBudget") == null ? (decimal?)null : CombatRoster.Positive(ScenarioLoader.A(group, "combatPowerBudget"));
                    var generatedError = CombatRoster.ValidateGenerated(resolved!, group, budget, ScenarioLoader.I(group, "count", 1), ScenarioLoader.A(group, "costMode", "pawnKind"));
                    if (generatedError != null) return generatedError;
                    if (resolved!.Team != ScenarioLoader.A(group, "team")) return "群战生成队伍归属不符";
                    var unitError = SquadTemplates.ValidateUnits(resolved, actual.Units.Where(u => u.Team == resolved.Team), resolved.Id);
                    if (unitError != null) return unitError;
                }
            }
        }
        foreach (var round in report.Rounds)
        {
            if (round.ElapsedTicks < 0 || !Equal(round.SimulatedSeconds, round.ElapsedTicks / 60d) || round.Matrix == null || round.Events == null || round.Units.Any(u => u == null) || round.Teams.Any(t => t == null) || round.Matrix.Any(m => m == null) || round.Events.Any(e => e == null)) return "群战时钟或账本集合无效";
            if (round.Units.Select(u => u.Id).Distinct().Count() != round.Units.Count || round.Teams.Select(t => t.Id).Distinct().Count() != round.Teams.Count || round.Units.Any(u => !round.Teams.Any(t => t.Id == u.Team))) return "群战参战单位或队伍身份不一致";
            if (round.TotalEvents != round.Matrix.Sum(c => c.Events) || round.TotalEvents != round.Events.Count + round.DroppedDetailEvents || round.DroppedDetailEvents < 0 || round.Events.Select(e => e.Id).Distinct().Count() != round.Events.Count || round.Events.Any(e => e.Id <= 0 || e.Amount < 0 || !double.IsFinite(e.Amount))) return "群战累计事件与矩阵/保留明细不一致";
            foreach (var team in round.Teams)
            {
                var units = round.Units.Where(u => u.Team == team.Id).ToList();
                if (team.Initial <= 0 || team.Initial > CombatRoster.MaxTeamMembers || team.Initial != units.Count(u => !u.JoinedDuringBattle) || team.Joined != units.Count(u => u.JoinedDuringBattle) || team.Total != units.Count || team.Total != team.Standing + team.Downed + team.Dead + team.Escaped + team.Removed || team.Alive != team.Standing || team.Alive != units.Count(u => u.Alive) || team.PendingRevival != units.Count(u => u.PendingRevival)) return "群战初始/新增人数与最终状态分项不一致";
                if (!Equal(team.EnemyDamage, units.Sum(u => u.EnemyDamage) + team.UnrosteredEnemyDamage) || !Equal(team.DamageTaken, round.Matrix.Where(m => m.Channel == "pawn" && m.Victim == team.Id).Sum(m => m.Amount))) return "群战输出/承伤未与逐单位及矩阵核对一致";
                var dps = round.SimulatedSeconds > 0 ? team.EnemyDamage / round.SimulatedSeconds : 0;
                var living = units.Where(u => u.Alive).ToList();
                if (!Equal(team.Dps, dps) || !Equal(team.MeanDps, living.Count > 0 ? living.Average(u => u.Dps) : 0) || !Equal(team.MeanDamageTaken, living.Count > 0 ? living.Average(u => u.DamageTaken) : 0) || units.Any(u => u.AliveTicks < 0 || u.JoinedAtTick < 0 || u.JoinedAtTick > round.ElapsedTicks || !u.JoinedDuringBattle && u.JoinedAtTick != 0 || u.AliveTicks > round.ElapsedTicks - u.JoinedAtTick || u.Alive && u.AliveTicks == 0 && round.ElapsedTicks > u.JoinedAtTick || !Equal(u.Dps, u.AliveTicks > 0 ? u.EnemyDamage * 60d / u.AliveTicks : 0))) return "群战个人 DPS 未采用存活模拟时间，或人均值未采用当前场上存活成员";
                foreach (var pair in new[] {
                    (team.Damage, GroupCombatDistribution.Calculate(units.Select(u => KeyValuePair.Create(u.Id, u.EnemyDamage)))),
                    (team.DpsDistribution, GroupCombatDistribution.Calculate(units.Select(u => KeyValuePair.Create(u.Id, u.Dps)))),
                    (team.Taken, GroupCombatDistribution.Calculate(units.Select(u => KeyValuePair.Create(u.Id, u.DamageTaken)))) })
                {
                    var (actual, expected) = pair;
                    if (actual == null || actual.Count != expected.Count || !Equal(actual.Min, expected.Min) || !Equal(actual.Max, expected.Max) || !Equal(actual.Mean, expected.Mean) || !Equal(actual.Median, expected.Median) || actual.MinIds == null || actual.MaxIds == null || !actual.MinIds.Order().SequenceEqual(expected.MinIds.Order()) || !actual.MaxIds.Order().SequenceEqual(expected.MaxIds.Order())) return "群战分布或最高/最低单位与全部参战成员不一致";
                }
            }
        }
        return null;
    }
}
