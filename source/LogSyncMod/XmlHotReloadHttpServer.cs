using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using Verse;

namespace LogSyncMod
{
    internal static class XmlHotReloadHttpServer
    {
        private const string EndpointPrefix = "RimDevTools.LogSyncMod.XmlHotReload.v1.";
        private const string RequestPath = "/v1/xml/hotreload";
        private const int RequestTimeoutMilliseconds = 10000;
        private static readonly ConcurrentQueue<PendingRequest> requests = new ConcurrentQueue<PendingRequest>();
        private static int started;
        private static int running;
        private static string endpointFilePath;
        private static string authToken;
        private static TcpListener listener;
        private static string lastListenerError;

        public static void Start()
        {
            if (Interlocked.CompareExchange(ref started, 1, 0) != 0) return;

            try
            {
                int processId = Process.GetCurrentProcess().Id;
                int port;
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start(1);
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
                authToken = Guid.NewGuid().ToString("N");
                endpointFilePath = Path.Combine(GenFilePaths.ConfigFolderPath, EndpointPrefix + processId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".endpoint");
                WriteEndpointFile(processId, port, authToken);

                Interlocked.Exchange(ref running, 1);
                AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                var thread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "LogSyncMod XML hot reload HTTP server"
                };
                thread.Start();
                Log.Message("[LogSyncMod] XML hot reload HTTP server started on 127.0.0.1:" + port);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref running, 0);
                try { listener?.Stop(); } catch { }
                TryDeleteEndpointFile();
                Interlocked.Exchange(ref started, 0);
                Log.Warning("[LogSyncMod] XML hot reload HTTP server failed to start: " + ex.Message);
            }
        }

        public static void ProcessPendingRequests()
        {
            string listenerError = Interlocked.Exchange(ref lastListenerError, null);
            if (!string.IsNullOrEmpty(listenerError))
                Log.Warning("[LogSyncMod] XML hot reload HTTP server recovered: " + listenerError);

            while (requests.TryDequeue(out PendingRequest request))
            {
                if (!request.TryBegin()) continue;

                string response;
                if (Current.ProgramState != ProgramState.Playing)
                {
                    response = "game_not_playing";
                }
                else if (!Prefs.DevMode)
                {
                    response = "dev_mode_required";
                }
                else
                {
                    try
                    {
                        Log.Message("[LogSyncMod] XML_HOT_RELOAD_REQUESTED");
                        PlayDataLoader.HotReloadDefs();
                        Log.Message("[LogSyncMod] XML_HOT_RELOAD_QUEUED");
                        response = "queued";
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[LogSyncMod] XML hot reload request failed: " + ex);
                        response = "reload_failed";
                    }
                }

                request.Complete(response);
            }
        }

        private static void WriteEndpointFile(int processId, int port, string token)
        {
            string directory = Path.GetDirectoryName(endpointFilePath);
            Directory.CreateDirectory(directory);
            string temporaryPath = endpointFilePath + ".tmp";
            string contents = "version=1\npid=" + processId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\nport=" + port.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\ntoken=" + token + "\n";
            File.WriteAllText(temporaryPath, contents, new UTF8Encoding(false));
            if (File.Exists(endpointFilePath)) File.Delete(endpointFilePath);
            File.Move(temporaryPath, endpointFilePath);
        }

        private static void ListenLoop()
        {
            while (Volatile.Read(ref running) == 1)
            {
                try
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                        HandleClient(client);
                }
                catch (SocketException ex)
                {
                    if (Volatile.Read(ref running) == 0) break;
                    Interlocked.Exchange(ref lastListenerError, ex.Message);
                    Thread.Sleep(250);
                }
                catch (Exception ex)
                {
                    if (Volatile.Read(ref running) == 0) break;
                    Interlocked.Exchange(ref lastListenerError, ex.Message);
                    Thread.Sleep(250);
                }
            }
        }

        private static void HandleClient(TcpClient client)
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            NetworkStream stream = client.GetStream();
            stream.ReadTimeout = 5000;
            stream.WriteTimeout = 5000;

            using (var reader = new StreamReader(stream, new UTF8Encoding(false), false, 1024, true))
            {
                string requestLine = reader.ReadLine();
                string[] requestParts = requestLine == null ? new string[0] : requestLine.Split(' ');
                if (requestParts.Length != 3 || requestParts[0] != "POST" || requestParts[1] != RequestPath)
                {
                    WriteResponse(stream, 404, "Not Found", "unsupported_endpoint");
                    return;
                }

                string authorization = null;
                int contentLength = 0;
                int totalHeaderCharacters = 0;
                string headerLine;
                while ((headerLine = reader.ReadLine()) != null && headerLine.Length > 0)
                {
                    totalHeaderCharacters += headerLine.Length;
                    if (totalHeaderCharacters > 8192)
                    {
                        WriteResponse(stream, 400, "Bad Request", "headers_too_large");
                        return;
                    }

                    int separator = headerLine.IndexOf(':');
                    if (separator <= 0) continue;
                    string headerName = headerLine.Substring(0, separator).Trim();
                    string headerValue = headerLine.Substring(separator + 1).Trim();
                    if (headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) authorization = headerValue;
                    else if (headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                        && (!int.TryParse(headerValue, out contentLength) || contentLength != 0))
                    {
                        WriteResponse(stream, 400, "Bad Request", "request_body_not_supported");
                        return;
                    }
                }

                IPAddress remoteAddress = ((IPEndPoint)client.Client.RemoteEndPoint).Address;
                if (!IPAddress.IsLoopback(remoteAddress))
                {
                    WriteResponse(stream, 403, "Forbidden", "loopback_only");
                    return;
                }
                if (!string.Equals(authorization, "Bearer " + authToken, StringComparison.Ordinal))
                {
                    WriteResponse(stream, 401, "Unauthorized", "unauthorized");
                    return;
                }

                var request = new PendingRequest();
                requests.Enqueue(request);
                string result = request.WaitForResponse(RequestTimeoutMilliseconds);
                if (result == "queued") WriteResponse(stream, 202, "Accepted", result);
                else if (result == "request_timeout") WriteResponse(stream, 504, "Gateway Timeout", result);
                else if (result == "reload_failed") WriteResponse(stream, 500, "Internal Server Error", result);
                else WriteResponse(stream, 409, "Conflict", result);
            }
        }

        private static void WriteResponse(NetworkStream stream, int statusCode, string reason, string body)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            string headers = "HTTP/1.1 " + statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + reason
                + "\r\nContent-Type: text/plain; charset=utf-8"
                + "\r\nContent-Length: " + bodyBytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();
        }

        private static void OnProcessExit(object sender, EventArgs args)
        {
            Interlocked.Exchange(ref running, 0);
            try { listener?.Stop(); } catch { }
            TryDeleteEndpointFile();
        }

        private static void TryDeleteEndpointFile()
        {
            if (string.IsNullOrEmpty(endpointFilePath)) return;
            try { if (File.Exists(endpointFilePath)) File.Delete(endpointFilePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private sealed class PendingRequest
        {
            private readonly TaskCompletionSource<string> completed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            private int state;

            public bool TryBegin() => Interlocked.CompareExchange(ref state, 1, 0) == 0;

            public void Complete(string value)
            {
                if (Interlocked.CompareExchange(ref state, 2, 1) == 1)
                    completed.TrySetResult(value);
            }

            public string WaitForResponse(int timeoutMilliseconds)
            {
                if (completed.Task.Wait(timeoutMilliseconds)) return completed.Task.Result;
                if (Interlocked.CompareExchange(ref state, 3, 0) == 0) return "request_timeout";
                return completed.Task.Wait(1000) ? completed.Task.Result : "request_timeout";
            }
        }
    }

    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.WindowStackOnGUI))]
    internal static class XmlHotReloadHttpPumpPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            XmlHotReloadHttpServer.ProcessPendingRequests();
        }
    }
}
