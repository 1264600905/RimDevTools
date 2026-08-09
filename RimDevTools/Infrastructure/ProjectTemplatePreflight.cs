using System.Xml.Linq;

namespace RimDevTools;

public sealed record ProjectTemplateIssue(string field, string reason);

public static class ProjectTemplatePreflight
{
    public const string TemplateModName = "TriggersProjectTemplate";
    public const string TemplateDisplayName = "Trigger's Project Template(白金的开发模板)";
    public const string TemplatePackageId = "trigger.projectTemplate";
    public const string TemplateAssemblyName = "MyCustomMod";

    public static CliError? Validate(ProjectContext context)
    {
        var issues = Inspect(context);
        return issues.Count == 0
            ? null
            : new CliError("PROJECT_TEMPLATE_NOT_CONFIGURED", "部署或测试被拒绝：项目仍包含模板占位符。请让 AI 按 details 中的字段逐项改为当前模组信息后重试。", new { issues });
    }

    public static IReadOnlyList<ProjectTemplateIssue> Inspect(ProjectContext context)
    {
        var issues = new List<ProjectTemplateIssue>();
        var config = context.Config;

        if (IsTemplateValue(config.ModName)) issues.Add(new ProjectTemplateIssue("mod_config.mod_name", "仍为模板默认模组名称。它决定部署目录和项目身份，必须替换为实际模组名称。"));
        if (config.CsprojPaths.Count == 0 || config.CsprojPaths.Any(IsTemplateValue)) issues.Add(new ProjectTemplateIssue("mod_config.csproj_paths", "未填写实际编译工程，或仍指向模板源码目录。必须改为实际源码目录和 DLL 工程路径。"));
        if (config.LogKeywords.Count == 0 || config.LogKeywords.Any(value => IsTemplateValue(value) || value.Equals("LogSyncAutoTest", StringComparison.OrdinalIgnoreCase))) issues.Add(new ProjectTemplateIssue("mod_config.log_keywords", "仍使用模板或 LogSyncAutoTest 关键字。必须改为当前模组的专用日志关键词。"));
        AddTemplateAssemblyIssues(issues, context);

        var aboutPath = Path.Combine(context.Root, "About", "About.xml");
        if (!File.Exists(aboutPath)) issues.Add(new ProjectTemplateIssue("About/About.xml", "缺少模组元数据文件，无法确认实际模组名称和 packageId。"));
        else
        {
            try
            {
                var root = XDocument.Load(aboutPath).Root;
                if (IsTemplateValue(root?.Element("name")?.Value)) issues.Add(new ProjectTemplateIssue("About/About.xml:name", "仍为模板默认显示名称。必须填写面向玩家显示的实际模组名称。"));
                if (IsTemplateValue(root?.Element("packageId")?.Value)) issues.Add(new ProjectTemplateIssue("About/About.xml:packageId", "仍为模板默认 packageId。必须使用唯一的实际 packageId。"));
            }
            catch (Exception ex) { issues.Add(new ProjectTemplateIssue("About/About.xml", "无法读取元数据: " + ex.Message)); }
        }

        return issues;
    }

    private static void AddTemplateAssemblyIssues(List<ProjectTemplateIssue> issues, ProjectContext context)
    {
        var outputPath = Path.GetFullPath(Path.Combine(context.Root, context.Config.GameVersion ?? "1.6", "Assemblies", TemplateAssemblyName + ".dll"));
        if (File.Exists(outputPath))
            issues.Add(new ProjectTemplateIssue(Path.GetRelativePath(context.Root, outputPath), $"部署输出目录仍包含模板 DLL 名称 {TemplateAssemblyName}.dll。必须删除或改名后再部署或测试。"));

        foreach (var projectFile in context.Config.CsprojPaths)
        {
            var projectPath = Path.GetFullPath(projectFile, context.Root);
            if (!File.Exists(projectPath)) continue;
            try
            {
                var assemblyName = XDocument.Load(projectPath).Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "AssemblyName")?.Value.Trim();
                assemblyName = string.IsNullOrWhiteSpace(assemblyName) ? Path.GetFileNameWithoutExtension(projectPath) : assemblyName;
                if (assemblyName.Equals(TemplateAssemblyName, StringComparison.OrdinalIgnoreCase))
                    issues.Add(new ProjectTemplateIssue($"{projectFile}:AssemblyName", $"编译输出仍为模板 DLL 名称 {TemplateAssemblyName}.dll。必须改为当前模组的程序集名称。"));
            }
            catch
            {
                // Invalid project files are reported by the normal build and doctor checks.
            }
        }
    }

    private static bool IsTemplateValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var normalized = value.Trim();
        return normalized.Contains("mycustommod", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(TemplateModName, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(TemplateDisplayName, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(TemplatePackageId, StringComparison.OrdinalIgnoreCase);
    }
}
