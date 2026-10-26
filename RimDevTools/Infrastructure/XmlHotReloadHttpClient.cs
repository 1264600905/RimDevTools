using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace RimDevTools;

public sealed record XmlHotReloadRequestResult(string Status, int? GameProcessId = null, string? ErrorCode = null, string? Message = null);

public sealed record XmlHotReloadEndpoint(int Version, int ProcessId, int Port, string Token, string FilePath);

public static class XmlHotReloadHttpClient
{
    private const string EndpointPrefix = "RimDevTools.LogSyncMod.XmlHotReload.v1.";

    public static string EndpointFilePath(string configDirectory, int processId)
        => Path.Combine(configDirectory, EndpointPrefix + processId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".endpoint");

    public static IReadOnlyList<XmlHotReloadEndpoint> FindLiveEndpoints(string configDirectory)
    {
        if (!Directory.Exists(configDirectory)) return [];

        var endpoints = new List<XmlHotReloadEndpoint>();
        foreach (var path in Directory.EnumerateFiles(configDirectory, EndpointPrefix + "*.endpoint", SearchOption.TopDirectoryOnly))
        {
            if (!TryReadEndpoint(path, out var endpoint)) continue;
            try
            {
                using var process = Process.GetProcessById(endpoint.ProcessId);
                if (!process.HasExited) endpoints.Add(endpoint);
            }
            catch (ArgumentException)
            {
                TryDeleteStaleEndpoint(path);
            }
            catch (InvalidOperationException)
            {
                TryDeleteStaleEndpoint(path);
            }
        }

        return endpoints;
    }

    public static async Task<XmlHotReloadRequestResult> RequestReloadAsync(string configDirectory, int timeoutMilliseconds = 5000, CancellationToken cancellationToken = default)
    {
        var endpoints = FindLiveEndpoints(configDirectory);
        if (endpoints.Count == 0)
            return new XmlHotReloadRequestResult("unavailable", ErrorCode: "XML_RELOAD_ENDPOINT_NOT_FOUND", Message: "未找到运行中的 LogSyncMod HTTP endpoint");
        if (endpoints.Count > 1)
            return new XmlHotReloadRequestResult("ambiguous", ErrorCode: "RIMWORLD_INSTANCE_AMBIGUOUS", Message: "检测到多个运行中的 LogSyncMod 实例，无法确定目标");

        return await RequestReloadAsync(endpoints[0], timeoutMilliseconds, cancellationToken);
    }

    public static async Task<XmlHotReloadRequestResult> RequestReloadAsync(XmlHotReloadEndpoint endpoint, int timeoutMilliseconds = 5000, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutMilliseconds);
            using var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("http://127.0.0.1:" + endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/v1/xml/hotreload"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token);
            request.Content = new ByteArrayContent([]);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            string body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (response.StatusCode == HttpStatusCode.Accepted && body.Trim() == "queued")
                return new XmlHotReloadRequestResult("queued", endpoint.ProcessId, Message: "游戏已接受热重载请求");

            if (response.StatusCode == HttpStatusCode.Conflict)
                return new XmlHotReloadRequestResult("rejected", endpoint.ProcessId, body.Trim(), "游戏拒绝了热重载请求");
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new XmlHotReloadRequestResult("unavailable", endpoint.ProcessId, "XML_RELOAD_ENDPOINT_AUTH_FAILED", "HTTP endpoint token 不匹配");
            if (response.StatusCode == HttpStatusCode.GatewayTimeout)
                return new XmlHotReloadRequestResult("timeout", endpoint.ProcessId, "XML_RELOAD_TIMEOUT", body.Trim());

            return new XmlHotReloadRequestResult("protocol_error", endpoint.ProcessId, "XML_RELOAD_HTTP_ERROR", "游戏返回 HTTP " + (int)response.StatusCode + ": " + body.Trim());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new XmlHotReloadRequestResult("timeout", endpoint.ProcessId, "XML_RELOAD_TIMEOUT", "等待游戏 HTTP 响应超时");
        }
        catch (HttpRequestException ex)
        {
            return new XmlHotReloadRequestResult("unavailable", endpoint.ProcessId, "XML_RELOAD_HTTP_UNAVAILABLE", ex.Message);
        }
        catch (IOException ex)
        {
            return new XmlHotReloadRequestResult("unavailable", endpoint.ProcessId, "XML_RELOAD_HTTP_UNAVAILABLE", ex.Message);
        }
    }

    private static bool TryReadEndpoint(string path, out XmlHotReloadEndpoint endpoint)
    {
        endpoint = null!;
        try
        {
            var fields = File.ReadAllLines(path)
                .Select(line => line.Split(new[] { '=' }, 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
            if (!fields.TryGetValue("version", out var versionText)
                || !int.TryParse(versionText, out var version)
                || version != 1
                || !fields.TryGetValue("pid", out var pidText)
                || !int.TryParse(pidText, out var processId)
                || processId <= 0
                || !fields.TryGetValue("port", out var portText)
                || !int.TryParse(portText, out var port)
                || port < IPEndPoint.MinPort
                || port > IPEndPoint.MaxPort
                || !fields.TryGetValue("token", out var token)
                || string.IsNullOrWhiteSpace(token))
                return false;

            endpoint = new XmlHotReloadEndpoint(version, processId, port, token, path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static void TryDeleteStaleEndpoint(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
