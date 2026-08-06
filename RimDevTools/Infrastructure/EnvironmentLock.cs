using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RimDevTools;

public sealed class EnvironmentLock : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _infoPath;
    private bool _owned;

    private EnvironmentLock(Mutex mutex, string infoPath) { _mutex = mutex; _infoPath = infoPath; }

    public static EnvironmentLock? TryAcquire(string rimWorldConfigDirectory, string command, string projectRoot, string runId, out string? busyInfo)
    {
        Directory.CreateDirectory(rimWorldConfigDirectory);
        var normalized = Path.GetFullPath(rimWorldConfigDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        var mutex = new Mutex(false, $"Global\\RimDevTools.RimWorld.{hash}");
        var handle = new EnvironmentLock(mutex, Path.Combine(rimWorldConfigDirectory, "RimDevTools.lock.json"));
        busyInfo = null;
        try
        {
            if (!mutex.WaitOne(0)) { busyInfo = File.Exists(handle._infoPath) ? File.ReadAllText(handle._infoPath) : null; handle.Dispose(); return null; }
            handle._owned = true;
            File.WriteAllText(handle._infoPath, JsonSerializer.Serialize(new { pid = Environment.ProcessId, command, projectPath = projectRoot, startedUtc = DateTimeOffset.UtcNow, runId }));
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    public void Dispose()
    {
        if (_owned)
        {
            try { if (File.Exists(_infoPath)) File.Delete(_infoPath); } catch { }
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex.Dispose();
    }
}
