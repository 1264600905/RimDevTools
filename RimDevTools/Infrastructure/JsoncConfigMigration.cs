using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RimDevTools;

internal sealed record ConfigMigration(byte[] Content, string OriginalHash, bool Changed);

internal static class JsoncConfigMigration
{
    internal static ConfigMigration Prepare(byte[] original, ToolPackageManifest target)
    {
        var bom = original.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? 3 : 0;
        var bytes = original.AsMemory(bom);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        CheckDuplicates(document.RootElement);
        var config = JsonSerializer.Deserialize<ProjectConfig>(bytes.Span, new JsonSerializerOptions
        {
            AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("配置为空");
        if (config.ConfigVersion != 2 || target.ConfigVersion != 2) throw new InvalidDataException("自动更新只支持 config_version=2");
        var hash = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
        if (document.RootElement.EnumerateObject().Any(p => p.Name.Equals("existing_project_mode", StringComparison.OrdinalIgnoreCase)))
            return new(original, hash, false);

        var reader = new Utf8JsonReader(bytes.Span, new JsonReaderOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1) continue;
            var offset = checked((int)reader.TokenStartIndex) + bom;
            var prefix = Encoding.UTF8.GetString(original.AsSpan(0, offset));
            var newline = Encoding.UTF8.GetString(original).Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lastLine = prefix[(prefix.LastIndexOf('\n') + 1)..];
            var indent = lastLine.All(char.IsWhiteSpace) ? lastLine : "";
            var addition = Encoding.UTF8.GetBytes("\"existing_project_mode\": false," + newline + indent);
            var result = new byte[original.Length + addition.Length];
            original.AsSpan(0, offset).CopyTo(result);
            addition.CopyTo(result, offset);
            original.AsSpan(offset).CopyTo(result.AsSpan(offset + addition.Length));
            return new(result, hash, true);
        }
        throw new InvalidDataException("配置缺少有效根对象字段");
    }

    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException($"配置含重复字段: {property.Name}");
                CheckDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) CheckDuplicates(child);
    }
}
