using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RimDevTools;

public sealed class EnvironmentLock : IDisposable
{
    private readonly Thread _ownerThread;
    private readonly ManualResetEventSlim _release;
    private readonly string _infoPath;
    private bool _disposed;

    private EnvironmentLock(Thread ownerThread, ManualResetEventSlim release, string infoPath)
    {
        _ownerThread = ownerThread;
        _release = release;
        _infoPath = infoPath;
    }

    public static EnvironmentLock? TryAcquire(string rimWorldConfigDirectory, string command, string projectRoot, string runId, out string? busyInfo)
    {
        Directory.CreateDirectory(rimWorldConfigDirectory);
        var mutexName = MutexName(rimWorldConfigDirectory);
        var infoPath = Path.Combine(rimWorldConfigDirectory, "RimDevTools.lock.json");
        var acquiredSignal = new ManualResetEventSlim();
        var releaseSignal = new ManualResetEventSlim();
        Exception? acquisitionError = null;
        var acquired = false;

        var ownerThread = new Thread(() =>
        {
            try
            {
                using var mutex = new Mutex(false, mutexName);
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                finally { acquiredSignal.Set(); }

                if (!acquired) return;
                releaseSignal.Wait();
                mutex.ReleaseMutex();
            }
            catch (Exception ex)
            {
                acquisitionError = ex;
                acquiredSignal.Set();
            }
        })
        {
            IsBackground = true,
            Name = "RimDevTools environment mutex owner"
        };
        ownerThread.Start();
        acquiredSignal.Wait();
        acquiredSignal.Dispose();

        if (acquisitionError is not null)
        {
            releaseSignal.Set();
            ownerThread.Join();
            releaseSignal.Dispose();
            throw acquisitionError;
        }
        if (!acquired)
        {
            busyInfo = File.Exists(infoPath) ? File.ReadAllText(infoPath) : null;
            ownerThread.Join();
            releaseSignal.Dispose();
            return null;
        }

        var handle = new EnvironmentLock(ownerThread, releaseSignal, infoPath);
        busyInfo = null;
        try
        {
            AtomicFile.WriteAllText(infoPath, JsonSerializer.Serialize(new
            {
                pid = Environment.ProcessId,
                command,
                projectPath = projectRoot,
                startedUtc = DateTimeOffset.UtcNow,
                runId
            }));
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public static bool IsBusy(string rimWorldConfigDirectory)
    {
        try
        {
            if (!Mutex.TryOpenExisting(MutexName(rimWorldConfigDirectory), out var mutex)) return false;
            using (mutex)
            {
                var acquired = false;
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (acquired) mutex.ReleaseMutex();
                return !acquired;
            }
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
    }

    private static string MutexName(string rimWorldConfigDirectory)
    {
        var normalized = Path.GetFullPath(rimWorldConfigDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return $"Global\\RimDevTools.RimWorld.{hash}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (File.Exists(_infoPath)) File.Delete(_infoPath); } catch { }
        _release.Set();
        _ownerThread.Join();
        _release.Dispose();
    }
}
