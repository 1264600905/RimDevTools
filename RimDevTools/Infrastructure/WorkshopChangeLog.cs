using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RimDevTools;

internal sealed record WorkshopChangeLogEntry(string RecordId, DateTimeOffset UpdatedAt, string Text, string Url);
internal sealed record WorkshopChangeLogPage(string PublishedFileId, string Language, int Page, string Url,
    DateTimeOffset CheckedAt, string Status, WorkshopChangeLogEntry[] Entries, string? Reason = null, int? HttpStatus = null);
internal sealed record WorkshopChangeLogBaseline(int SchemaVersion, string ProjectRoot, string OperationId,
    string? PublishedFileId, string? Text, DateTimeOffset CapturedAt, DateTimeOffset? SubmittedAt,
    string RemoteOutcome, string[]? PreviousRecordIds, bool SubmissionStarted = false);
internal sealed record WorkshopChangeLogVerification(string Status, string ComparisonMode, string? ExpectedText,
    WorkshopChangeLogEntry? MatchedRecord, string? Reason = null);

internal static class WorkshopChangeLog
{
    internal const string BaselineName = "steamworkshops-last-publish.json";
    private const int MaximumBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = MaximumBytes };
    internal static Func<string, string, int, CancellationToken, Task<WorkshopChangeLogPage>>? ReadOverride { get; set; }

    internal static string Url(string id, string language = "english", int page = 1)
        => $"https://steamcommunity.com/sharedfiles/filedetails/changelog/{id}?l={Uri.EscapeDataString(language)}&p={page}";

    internal static string Language(string[] args)
        => WorkshopMetadata.Language(["--text-language", CliArgs.Value(args, "--language") ?? "english"]);

    internal static async Task<WorkshopChangeLogPage> ReadAsync(string id, string language, int page, CancellationToken token)
    {
        if (SteamWorkshopsCommand.ParseId(id)?.ToString(CultureInfo.InvariantCulture) != id)
            throw new ArgumentException("改动日志需要有效的数字工坊 ID");
        if (page is < 1 or > 100) throw new ArgumentException("改动日志页码必须为 1 到 100");
        if (ReadOverride is not null) return await ReadOverride(id, language, page, token);
        var url = Url(id, language, page);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.CacheControl = new() { NoCache = true };
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            var status = (int)response.StatusCode;
            if (status == 429) return Failed("rate_limited", "Steam 社区限制请求频率，请稍后重查；没有重发上传", status);
            if (status is 301 or 302 or 303 or 307 or 308 or 401 or 403)
                return Failed("unavailable", "Steam 社区要求登录或当前请求无权访问；Steam 客户端登录不等于网页登录", status);
            if (!response.IsSuccessStatusCode) return Failed("unavailable", "Steam 社区 HTTP " + status, status);
            if (response.Content.Headers.ContentLength > MaximumBytes) return Failed("unavailable", "改动日志页面超过读取上限", status);
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            var block = new byte[8192];
            int length;
            while ((length = await stream.ReadAsync(block, token)) != 0)
            {
                if (buffer.Length + length > MaximumBytes) return Failed("unavailable", "改动日志页面超过读取上限", status);
                buffer.Write(block, 0, length);
            }
            return Parse(id, language, page, Encoding.UTF8.GetString(buffer.ToArray()), DateTimeOffset.UtcNow) with { HttpStatus = status };
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return Failed("unavailable", "读取 Steam 社区超时；没有重发上传"); }
        catch (HttpRequestException ex) { return Failed("unavailable", ex.Message); }
        WorkshopChangeLogPage Failed(string status, string reason, int? http = null)
            => new(id, language, page, url, DateTimeOffset.UtcNow, status, [], reason, http);
    }

    internal static WorkshopChangeLogPage Parse(string id, string language, int page, string html, DateTimeOffset checkedAt)
    {
        var url = Url(id, language, page);
        if (html.Length > MaximumBytes) return new(id, language, page, url, checkedAt, "unavailable", [], "页面超过解析上限");
        if (IsMatch(html, "class=[\"'][^\"']*\\berror_ctn\\b") || IsMatch(html, "<title>\\s*Steam (?:Community|社区)\\s*::\\s*(?:Error|错误)"))
            return new(id, language, page, url, checkedAt, "unavailable", [], "工坊页面不可访问：条目可能为私有、已删除或需要网页登录；不能据此认定日志不存在");
        var entries = new List<WorkshopChangeLogEntry>();
        // Steam's record body has a numeric Unix timestamp anchor. Scope to an announcement
        // before looking for it: page-wide text matches can mistake an old or unrelated note.
        var blocks = Regex.Split(html, "<div\\b[^>]*class=[\"'][^\"']*\\bchangeLogCtn\\b[^\"']*[\"'][^>]*>", RegexOptions.IgnoreCase, RegexTimeout);
        foreach (var section in blocks.Skip(1))
        {
            var match = Match(section, "<p\\b[^>]*\\bid=[\"'](?<id>[0-9]{1,12})[\"'][^>]*>(?<body>.*?)</p\\s*>");
            if (!match.Success || !long.TryParse(match.Groups["id"].Value, out var timestamp) || timestamp > 253402300799L)
                return new(id, language, page, url, checkedAt, "unsupported_format", [], "Steam 改动日志记录格式改变，未将解析失败当成空历史");
            var key = match.Groups["id"].Value;
            entries.Add(new(key, DateTimeOffset.FromUnixTimeSeconds(timestamp), NormalizeHtml(match.Groups["body"].Value), url + "#" + key));
        }
        if (blocks.Length == 1 && !IsMatch(html, "<title>.*?::.*?(?:Change Notes|改动说明|更新日志|変更履歴|Änderungsnotizen).*?</title>"))
            return new(id, language, page, url, checkedAt, "unsupported_format", [], "未识别到工坊改动日志页面，可能需要登录或页面结构已改变");
        return new(id, language, page, url, checkedAt, "available", entries.ToArray());
    }

    internal static string NormalizeHtml(string value)
    {
        value = Replace(value, "<(?:script|style)\\b[^>]*>.*?</(?:script|style)>", "");
        value = Replace(value, "<br\\s*/?>|</(?:p|div|h[1-6]|li)>|<li\\b[^>]*>", "\n");
        return NormalizeWhitespace(WebUtility.HtmlDecode(Replace(value, "<[^>]*>", "")));
    }

    internal static string NormalizeExpected(string value)
    {
        // Compare rendered body text, not original BBCode bytes or translated meaning.
        value = Replace(value, @"\[\*\]", "\n");
        value = Replace(value, @"\[/?(?:h[1-6]|list|olist|p|br|hr|quote|table|tr)(?:=[^\]]*)?\]", "\n");
        value = Replace(value, @"\[/?(?:b|i|u|s|strike|spoiler|noparse|url|color|size|code|td|th)(?:=[^\]]*)?\]", "");
        return NormalizeWhitespace(value);
    }

    private static string NormalizeWhitespace(string value)
        => Replace(value.Replace("\r\n", "\n").Replace('\r', '\n'), @"\s+", " ").Trim();
    private static Match Match(string value, string pattern) => Regex.Match(value, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);
    private static bool IsMatch(string value, string pattern) => Match(value, pattern).Success;
    private static string Replace(string value, string pattern, string replacement)
        => Regex.Replace(value, pattern, replacement, RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexTimeout);

    internal static WorkshopChangeLogVerification Verify(WorkshopChangeLogPage? page, string? expected,
        WorkshopChangeLogBaseline? baseline = null, string? recordId = null)
    {
        if (string.IsNullOrWhiteSpace(expected)) return new("skipped_empty", "rendered_text", expected, null, "本次明确选择不提交改动日志");
        if (baseline is not null && (!baseline.SubmissionStarted || baseline.SubmittedAt is null))
            return new("unknown", "rendered_text", expected, null, "该操作尚无已开始提交的证据，不能把旧记录视为本次提交");
        if (page is null || page.Status != "available") return new("unknown", "rendered_text", expected, null, page?.Reason ?? "未取得更新历史");
        var candidates = page.Entries.AsEnumerable();
        if (recordId is not null) candidates = candidates.Where(x => x.RecordId == recordId);
        if (baseline is not null)
        {
            if (baseline.PreviousRecordIds is not null) candidates = candidates.Where(x => !baseline.PreviousRecordIds.Contains(x.RecordId, StringComparer.Ordinal));
            // Allow clock skew, but never accept a historical occurrence solely by body text.
            candidates = candidates.Where(x => x.UpdatedAt >= baseline.SubmittedAt!.Value.AddMinutes(-2));
        }
        var records = candidates.ToArray();
        var matches = records.Where(x => x.Text == NormalizeExpected(expected)).ToArray();
        if (matches.Length == 1) return new("passed", "rendered_text", expected, matches[0]);
        if (matches.Length > 1) return new("ambiguous", "rendered_text", expected, null, "多条记录正文相同，请指定 --record，不能确认唯一记录");
        return new(records.Length == 0 ? "pending" : "mismatch", "rendered_text", expected, null,
            records.Length == 0 ? "当前页尚未找到对应更新记录；可等待后重查或指定页码" : "已读取候选记录，但展示正文与预期不同");
    }

    internal static string PathFor(ProjectContext context) => ProjectDataPaths.File(context.Root, BaselineName);
    internal static void Save(ProjectContext context, WorkshopChangeLogBaseline baseline)
    {
        Directory.CreateDirectory(ProjectDataPaths.DirectoryPath(context.Root));
        AtomicFile.WriteAllText(PathFor(context), JsonSerializer.Serialize(baseline, JsonDefaults.StorageIndented));
    }
    internal static WorkshopChangeLogBaseline? ReadBaseline(ProjectContext context)
    {
        var path = PathFor(context);
        if (!File.Exists(path)) return null;
        var value = JsonSerializer.Deserialize<WorkshopChangeLogBaseline>(File.ReadAllText(path), JsonDefaults.Storage)
            ?? throw new InvalidDataException("改动日志复查基线为空");
        if (value.SchemaVersion != 1 || !Path.GetFullPath(value.ProjectRoot).Equals(context.Root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("改动日志复查基线的版本或项目不匹配");
        return value;
    }
}
