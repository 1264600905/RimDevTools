using System.Text;

namespace RimDevTools;

public sealed class IncrementalLogSignalScanner
{
    private readonly string[] _signals;
    private readonly HashSet<string> _seen;
    private readonly int _tailLength;
    private string _tail = "";

    public IncrementalLogSignalScanner(params string[] signals)
    {
        _signals = signals
            .Where(signal => !string.IsNullOrEmpty(signal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (_signals.Length == 0) throw new ArgumentException("至少需要一个日志信号", nameof(signals));
        _seen = new HashSet<string>(StringComparer.Ordinal);
        _tailLength = _signals.Max(signal => signal.Length) - 1;
    }

    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var combined = _tail.Length == 0 ? text : _tail + text;
        foreach (var signal in _signals)
        {
            if (!_seen.Contains(signal) && combined.Contains(signal, StringComparison.Ordinal))
                _seen.Add(signal);
        }

        _tail = _tailLength <= 0
            ? ""
            : combined.Length <= _tailLength
                ? combined
                : combined[^_tailLength..];
    }

    public bool HasSeen(string signal) => _seen.Contains(signal);

    public void Reset()
    {
        _tail = "";
        _seen.Clear();
    }
}

public sealed record IncrementalLogRead(string Text, long Offset, bool Reset);

public static class IncrementalLogReader
{
    public static async Task<IncrementalLogRead> ReadAsync(string path, long offset, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new IncrementalLogRead("", 0, offset != 0);

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
        var reset = stream.Length < offset;
        if (reset) offset = 0;
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return new IncrementalLogRead(text, stream.Position, reset);
    }
}
