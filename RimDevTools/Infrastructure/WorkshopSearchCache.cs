using System.Text.Json;

namespace RimDevTools;

internal sealed class WorkshopSearchException(string code, string message) : Exception(message)
{
    internal string Code { get; } = code;
}

internal sealed record WorkshopSearchCache(int SchemaVersion, string ProjectRoot, uint AppId, string Keyword,
    bool Details, int CurrentPage, int PageSize, int MaxPages, uint TotalMatchingResults,
    DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, Dictionary<int, WorkshopQueryPage> Pages);

internal static class WorkshopSearchPages
{
    internal const string LimitMessage = "搜索范围过大，请缩小搜索范围或使用更准确的关键词";
    internal static (int Size, int Max, int Native, int Offset) Map(bool details, int page)
    {
        if (page < 1) throw new ArgumentException("--page 必须是正整数");
        var size = details ? 10 : 25;
        var max = details ? 5 : 3;
        if (page > max) throw new WorkshopSearchException("SEARCH_PAGE_LIMIT_EXCEEDED", LimitMessage);
        var start = (page - 1) * size;
        return (size, max, start / 50 + 1, start % 50);
    }

    internal static WorkshopSearchCache Read(ProjectContext? context, DateTimeOffset now)
    {
        if (context is null) throw new WorkshopSearchException("SEARCH_CONTEXT_NOT_FOUND", "请进入项目或使用 --project，先执行带关键词的搜索");
        var path = ProjectDataPaths.File(context.Root, "steamworkshops-search-cache.json");
        if (!System.IO.File.Exists(path)) throw new WorkshopSearchException("SEARCH_CONTEXT_NOT_FOUND", "没有最近搜索；请先执行带关键词的搜索");
        try
        {
            var cache = JsonSerializer.Deserialize<WorkshopSearchCache>(System.IO.File.ReadAllText(path), JsonDefaults.Storage);
            if (cache is null || cache.SchemaVersion != 1 || cache.AppId != SteamworksClient.AppId
                || !cache.ProjectRoot.Equals(Path.GetFullPath(context.Root), StringComparison.OrdinalIgnoreCase)
                || cache.Pages is null || cache.Pages.Count == 0 || string.IsNullOrWhiteSpace(cache.Keyword)
                || cache.PageSize != (cache.Details ? 10 : 25) || cache.MaxPages != (cache.Details ? 5 : 3)
                || cache.CurrentPage < 1 || cache.CurrentPage > cache.MaxPages || cache.CreatedAt > now
                || cache.ExpiresAt != cache.CreatedAt.AddMinutes(10)
                || cache.Pages.Any(p => p.Key < 1 || p.Key > 2 || p.Value is null || p.Key != p.Value.NativePage || p.Value.Items is null || p.Value.Items.Length > 50))
                throw new InvalidDataException("缓存格式或项目/App ID 不匹配");
            if (now >= cache.ExpiresAt) throw new WorkshopSearchException("SEARCH_CONTEXT_EXPIRED", "搜索上下文已过期，请重新执行带关键词的搜索");
            return cache;
        }
        catch (WorkshopSearchException) { throw; }
        catch (Exception ex) { throw new WorkshopSearchException("SEARCH_CONTEXT_INVALID", "搜索缓存无效，请重新执行带关键词的搜索: " + ex.Message); }
    }

    internal static void Save(ProjectContext context, WorkshopSearchCache cache)
        => AtomicFile.WriteAllText(ProjectDataPaths.File(context.Root, "steamworkshops-search-cache.json"), JsonSerializer.Serialize(cache, JsonDefaults.Storage));

    internal static object Output(WorkshopSearchCache cache, int page, WorkshopQueryPage native, string source, bool end = false)
    {
        var map = Map(cache.Details, Math.Min(page, cache.MaxPages));
        var items = end ? [] : native.Items.Skip(map.Offset).Take(map.Size).ToArray();
        var hasMore = (long)page * map.Size < cache.TotalMatchingResults;
        var limit = !end && page == map.Max && hasMore;
        return new
        {
            keyword = cache.Keyword, cache.Details, page, pageSize = map.Size, maxPages = map.Max,
            returnedCount = items.Length, totalMatchingResults = cache.TotalMatchingResults,
            hasNextPage = !end && page < map.Max && hasMore, limitReached = limit, endOfResults = end,
            message = limit ? LimitMessage : null, source, native.FetchedAt, cache.ExpiresAt,
            items = items.Select(item => cache.Details
                ? (object)new { name = item.Title, item.PublishedFileId, item.SubscriptionCount, description = WorkshopText.Plain(item.Description) }
                : new { name = item.Title, item.PublishedFileId, item.SubscriptionCount }).ToArray(),
            subscriptionCountUnavailable = items.Any(item => item.SubscriptionCount is null) ? "Steam GetQueryUGCStatistic 未返回部分订阅人数；对应字段为 null" : null
        };
    }
}
