using System.Xml;
using LogSyncMod.AutoTest.Scenarios;

namespace RimDevTools;

public sealed record ScenarioDiagnostic(string Severity, string Code, string Message, string Suggestion,
    string? File = null, int? Line = null, int? Column = null, string? Element = null);

public sealed class ScenarioValidation
{
    public bool Valid => Diagnostics.All(d => d.Severity != "error");
    public string Stage => "static";
    public List<ScenarioDiagnostic> Diagnostics { get; } = [];
    public List<string> SelectedTestDefNames { get; set; } = [];
    public int? CaseCount { get; set; }
    public int? RoundCount { get; set; }
    public int? TournamentCount { get; set; }
    public int SkippedFiles { get; set; }
    public string[] RuntimeChecks { get; } = [
        "Def、DLC 和模组依赖是否在实际游戏中加载，以及 Def 的类型与组件是否符合用途。",
        "定制 AI、反射绑定的方法签名与包归属、地图生成器和部队生成器的实际执行效果。",
        "实际生成的人数、装备、坐标可用性、寻路和战斗过程，以及断言能否在时限内满足。"
    ];
    public IEnumerable<string> Warnings => Diagnostics.Where(d => d.Severity == "warning").Select(d => d.Message + " 建议：" + d.Suggestion);
}

/// <summary>Uses the same compiler and frozen plan as execution; does not touch game state or allocate artifacts.</summary>
public static class ScenarioPreflight
{
    public static ScenarioValidation Validate(string path, string mode, IReadOnlyList<string> names, out ScenarioPlan? plan, string? basePath = null)
    {
        var report = new ScenarioValidation();
        plan = null;
        try
        {
            path = Path.GetFullPath(path, basePath ?? Environment.CurrentDirectory);
            if (mode == "arena")
            {
                var arena = ArenaLoader.Load(path, names);
                plan = new ScenarioPlan { Purpose = "arena", Arena = arena, Inputs = arena.Inputs, SelectedTestDefNames = arena.SelectedTestDefNames };
                report.TournamentCount = arena.Tournaments.Count;
                report.SkippedFiles = arena.SkippedFiles;
                if (arena.SkippedFiles > 0)
                    report.Diagnostics.Add(new("warning", "SCENARIO_FILES_SKIPPED", $"目录中有 {arena.SkippedFiles} 个非 ArenaSuite 文件被跳过。", "确认要运行的文件使用 ArenaSuite 根节点；普通回归和群战分别使用 auto/groupcombat。", path));
            }
            else
            {
                plan = ScenarioLoader.Load(path, mode == "groupcombat");
                ScenarioLoader.SelectRules(plan, names);
                if (mode == "groupcombat") report.RoundCount = GroupCombatPlan.Expand(plan).Count;
                report.CaseCount = plan.Cases.Count;
            }
            report.SelectedTestDefNames = mode == "arena" ? plan.SelectedTestDefNames
                : plan.Cases.Select(c => c.TestDefName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or ScenarioConfigurationException or ArgumentException or FormatException or OverflowException)
        {
            var configuration = ex as ScenarioConfigurationException;
            var xml = ex as XmlException;
            report.Diagnostics.Add(new("error", configuration?.Code ?? (mode == "arena" ? "ARENA_CONFIG_INVALID" : "SCENARIO_INVALID"),
                ex.Message, configuration?.Suggestion ?? Suggest(ex), configuration?.FilePath ?? path,
                Positive(configuration?.LineNumber ?? xml?.LineNumber), Positive(configuration?.LinePosition ?? xml?.LinePosition), configuration?.ElementPath));
            plan = null;
        }
        return report;
    }

    private static int? Positive(int? value) => value > 0 ? value : null;

    private static string Suggest(Exception ex)
    {
        if (ex is IOException or UnauthorizedAccessException) return "确认路径存在且可读；目录入口只读取当前目录的 *.autotest.xml，引用的部队文件也必须可读。";
        if (ex is XmlException) return "检查标签闭合、属性引号、转义和编码声明；不允许 DTD 或外部实体。";
        if (ex is FormatException or OverflowException) return "检查数字的类型、取值范围和小数点格式；整数不能使用小数，数值不能为 NaN 或 Infinity。";
        var message = ex.Message;
        if (message.Contains("test name", StringComparison.OrdinalIgnoreCase) || message.Contains("test-name", StringComparison.OrdinalIgnoreCase))
            return "核对 --test-name 与 XML 的 testDefName；擂台必须使用 testDefName，群战必须显式指名，普通回归还可使用唯一显示名或完整 caseId。";
        if (message.Contains("reference", StringComparison.OrdinalIgnoreCase) || message.Contains("baseline", StringComparison.OrdinalIgnoreCase) || message.Contains("unknown template", StringComparison.OrdinalIgnoreCase))
            return "核对引用的 id、大小写及所在作用域；actor/team/template/step 必须已定义，since 基线必须在使用前到达。";
        if (message.Contains("requires", StringComparison.OrdinalIgnoreCase) || message.Contains("required", StringComparison.OrdinalIgnoreCase))
            return "按错误列出的必填字段补齐配置；确认 auto/groupcombat/arena 模式与 XML 根节点、场景类型相匹配。";
        return "按错误中的允许值、范围或冲突项修改指定节点；对照 XML skill 字段参考，修复后重新校验。";
    }
}
