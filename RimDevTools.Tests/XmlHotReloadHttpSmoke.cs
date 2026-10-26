using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using RimDevTools;

internal static class XmlHotReloadHttpSmoke
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static async Task<int> RunClientAsync()
    {
        string endpointDirectory = Path.Combine(Path.GetTempPath(), "XmlHotReloadHttpSmoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(endpointDirectory);
        string assemblyPath = typeof(XmlHotReloadHttpSmoke).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add(assemblyPath);
        start.ArgumentList.Add("--xml-hotload-http-server");
        start.ArgumentList.Add(endpointDirectory);

        try
        {
            using Process server = Process.Start(start) ?? throw new InvalidOperationException("无法启动 HTTP 服务端进程");
            using var timeout = new CancellationTokenSource(Timeout);
            string endpointPath = XmlHotReloadHttpClient.EndpointFilePath(endpointDirectory, server.Id);
            while (!File.Exists(endpointPath))
            {
                if (server.HasExited)
                    throw new InvalidOperationException("HTTP 服务端提前退出: " + await server.StandardError.ReadToEndAsync(timeout.Token));
                await Task.Delay(25, timeout.Token);
            }

            XmlHotReloadRequestResult response = await XmlHotReloadHttpClient.RequestReloadAsync(endpointDirectory, (int)Timeout.TotalMilliseconds, timeout.Token);
            await server.WaitForExitAsync(timeout.Token);
            if (server.ExitCode != 0)
                throw new InvalidOperationException("HTTP 服务端失败: " + await server.StandardError.ReadToEndAsync(timeout.Token));
            if (response.Status != "queued")
                throw new InvalidOperationException("HTTP 响应不符合预期: " + response.Status + " " + response.ErrorCode + " " + response.Message);

            Console.WriteLine("PASS: 独立进程通过本机 HTTP POST 发送 reloadDefs 并收到应答");
            return 0;
        }
        finally
        {
            if (Directory.Exists(endpointDirectory)) Directory.Delete(endpointDirectory, true);
        }
    }

    public static async Task<int> RunServerAsync(string endpointDirectory)
    {
        Directory.CreateDirectory(endpointDirectory);
        int processId = Environment.ProcessId;
        string token = Guid.NewGuid().ToString("N");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string endpointPath = XmlHotReloadHttpClient.EndpointFilePath(endpointDirectory, processId);
            File.WriteAllText(endpointPath,
                "version=1\npid=" + processId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\nport=" + port.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\ntoken=" + token + "\n", new UTF8Encoding(false));

            using var timeout = new CancellationTokenSource(Timeout);
            using TcpClient client = await listener.AcceptTcpClientAsync(timeout.Token);
            NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            string? requestLine = await reader.ReadLineAsync(timeout.Token);
            string? authorization = null;
            string? header;
            while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(timeout.Token)))
            {
                int separator = header.IndexOf(':');
                if (separator > 0 && header[..separator].Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    authorization = header[(separator + 1)..].Trim();
            }

            if (requestLine != "POST /v1/xml/hotreload HTTP/1.1" || authorization != "Bearer " + token)
            {
                await WriteResponseAsync(stream, 401, "Unauthorized", "unauthorized", timeout.Token);
                return 1;
            }

            await WriteResponseAsync(stream, 202, "Accepted", "queued", timeout.Token);
            return 0;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int code, string reason, string body, CancellationToken cancellationToken)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        string headers = "HTTP/1.1 " + code.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + reason
            + "\r\nContent-Type: text/plain; charset=utf-8"
            + "\r\nContent-Length: " + bodyBytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "\r\nConnection: close\r\n\r\n";
        byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
        await stream.WriteAsync(headerBytes, cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
