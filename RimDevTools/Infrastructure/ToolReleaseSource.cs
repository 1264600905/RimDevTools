using System.Net.Http;
using System.Text.Json;

namespace RimDevTools;

internal sealed record ToolRelease(string Version, string AssetName, string DownloadUrl, long Size, string? Sha256, string? ChecksumUrl);

internal static class ToolReleaseSource
{
    internal const string ApiUrl = "https://api.github.com/repos/1264600905/RimDevTools/releases/latest";
    private const string DownloadPrefix = "https://github.com/1264600905/RimDevTools/releases/download/";
    internal static Func<HttpClient>? ClientFactory;

    internal static Version ParseVersion(string value)
    {
        var parsed = Version.Parse(value.TrimStart('v', 'V'));
        if (parsed.Build < 0) throw new InvalidDataException("发行版本至少包含三段数字");
        return new(parsed.Major, parsed.Minor, parsed.Build, Math.Max(0, parsed.Revision));
    }

    internal static HttpClient CreateClient()
    {
        var client = ClientFactory?.Invoke() ?? new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(3);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RimDevTools/" + typeof(CliApplication).Assembly.GetName().Version);
        return client;
    }

    internal static async Task<ToolRelease> LatestAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(ApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (json.Length > 2_000_000) throw new InvalidDataException("Release 元数据过大");
        using var document = JsonDocument.Parse(json);
        var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) throw new InvalidDataException("更新来源未返回正式发行版");
        var version = ParseVersion(release.GetProperty("tag_name").GetString()!).ToString();
        var name = $"RimDevTools-{version}-Tools.zip";
        var assets = release.GetProperty("assets").EnumerateArray().ToArray();
        var matches = assets.Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (matches.Length == 0 && ParseVersion(version) <= typeof(CliApplication).Assembly.GetName().Version)
            return new(version, name, "", 0, null, null);
        if (matches.Length != 1) throw new InvalidDataException($"正式发行缺少唯一的简化工具包: {name}");
        var asset = matches[0];
        var url = ValidateUrl(asset.GetProperty("browser_download_url").GetString()!);
        var size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > 512L * 1024 * 1024) throw new InvalidDataException("工具包大小无效或超限");
        var digest = asset.TryGetProperty("digest", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        string? hash = null;
        if (digest is not null)
        {
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || !ValidHash(digest[7..])) throw new InvalidDataException("附件 digest 无效");
            hash = digest[7..].ToLowerInvariant();
        }
        var checksum = assets.Where(a => a.GetProperty("name").GetString() == name + ".sha256").ToArray();
        var checksumUrl = checksum.Length == 1 ? ValidateUrl(checksum[0].GetProperty("browser_download_url").GetString()!) : null;
        if (hash is null && checksumUrl is null) throw new InvalidDataException("附件缺少 SHA-256 校验信息");
        return new(version, name, url, size, hash, checksumUrl);
    }

    internal static async Task DownloadAsync(ToolRelease release, string path, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        var hash = release.Sha256;
        if (hash is null)
        {
            var text = await client.GetStringAsync(ValidateUrl(release.ChecksumUrl!), cancellationToken);
            var fields = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 2 || !ValidHash(fields[0]) || fields[1] != release.AssetName) throw new InvalidDataException("发行校验文件内容无效");
            hash = fields[0];
        }
        using var response = await client.GetAsync(ValidateUrl(release.DownloadUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } length && length != release.Size) throw new InvalidDataException("下载长度与发行信息不一致");
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
        {
            var buffer = new byte[65536];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > release.Size) throw new InvalidDataException("下载内容超过声明大小");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if (total != release.Size) throw new InvalidDataException("工具包下载不完整");
        }
        if (!ToolPackageManifest.Hash(path).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("工具包 SHA-256 校验失败");
    }

    internal static bool ValidHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    private static string ValidateUrl(string url)
    {
        if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal) || !Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme != "https" || !string.IsNullOrEmpty(parsed.UserInfo))
            throw new InvalidDataException("附件地址不属于官方发行来源");
        return url;
    }
}
