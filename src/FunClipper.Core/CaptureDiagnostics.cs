namespace FunClipper.Core;

// Text-only, bounded diagnostics survive startup cleanup; no captured media is written here.
internal sealed class CaptureDiagnostics
{
    private readonly object _gate = new();
    private readonly Queue<string> _errors = new();
    private int _written;
    internal string FilePath { get; }
    internal string ErrorSummary { get { lock (_gate) return string.Join(" ", _errors.TakeLast(3)); } }

    internal CaptureDiagnostics(string directory)
    {
        FilePath = Path.Combine(directory, $"capture-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        try
        {
            Directory.CreateDirectory(directory);
            // Retain a small recent history; diagnostics are not the replay storage.
            foreach (var old in new DirectoryInfo(directory).GetFiles("capture-*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc).Skip(19))
                try { old.Delete(); } catch (IOException) { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal void Error(string message)
    {
        lock (_gate)
        {
            _errors.Enqueue(message);
            while (_errors.Count > 8) _errors.Dequeue();
            Write("FFmpeg: " + message);
        }
    }

    internal void Write(string message)
    {
        lock (_gate)
        {
            if (_written > 512_000) return;
            try
            {
                var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
                File.AppendAllText(FilePath, line);
                _written += line.Length;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
