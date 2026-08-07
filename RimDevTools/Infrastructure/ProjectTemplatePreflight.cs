using System.Xml.Linq;

namespace RimDevTools;

public static class ProjectTemplatePreflight
{
    public static CliError? Validate(ProjectContext context)
    {
        var issues = new List<object>();
        var config = context.Config;

        if (IsTemplateValue(config.ModName)) issues.Add(new { field = "mod_config.mod_name", reason = "仍为 MyCustomMod 模板名称。它决定部署目录和项目身份，必须替换为实际模组名称。" });
        if (config.CsprojPaths.Count == 0 || config.CsprojPaths.Any(IsTemplateValue)) issues.Add(new { field = "mod_config.csproj_paths", reason = "未填写实际编译工程，或仍指向 MyCustomMod 模板目录/程序集。必须改为实际源码目录和 DLL 工程路径。" });
        if (config.LogKeywords.Count == 0 || config.LogKeywords.Any(value => IsTemplateValue(value) || value.Equals("LogSyncAutoTest", StringComparison.OrdinalIgnoreCase))) issues.Add(new { field = "mod_config.log_keywords", reason = "仍使用模板或 LogSyncAutoTest 关键字。必须改为当前模组的专用日志关键词。" });

        var aboutPath = Path.Combine(context.Root, "About", "About.xml");
        if (!File.Exists(aboutPath)) issues.Add(new { field = "About/About.xml", reason = "缺少模组元数据文件，无法确认实际模组名称和 packageId。" });
        else
        {
            try
            {
                var root = XDocument.Load(aboutPath).Root;
                if (IsTemplateValue(root?.Element("name")?.Value)) issues.Add(new { field = "About/About.xml:name", reason = "仍为 MyCustomMod 模板名称。必须填写面向玩家显示的实际模组名称。" });
                if (IsTemplateValue(root?.Element("packageId")?.Value)) issues.Add(new { field = "About/About.xml:packageId", reason = "仍为 mycustommod 模板 packageId。必须使用唯一的实际 packageId。" });
            }
            catch (Exception ex) { issues.Add(new { field = "About/About.xml", reason = "无法读取元数据: " + ex.Message }); }
        }

        return issues.Count == 0 ? null : new CliError("PROJECT_TEMPLATE_NOT_CONFIGURED", "部署或测试被拒绝：项目仍包含模板占位符。请让 AI 按 details 中的字段逐项改为当前模组信息后重试。", new { issues });
    }

    private static bool IsTemplateValue(string? value)
        => string.IsNullOrWhiteSpace(value) || value.Contains("mycustommod", StringComparison.OrdinalIgnoreCase);
}
