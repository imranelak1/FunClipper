using System.Globalization;

namespace FunClipper.Core;

internal record CompletedSegment(string FilePath, double StartSeconds, double EndSeconds);
internal record SegmentSnapshot(string Directory, IReadOnlyList<string> Files, double DurationSeconds);

internal sealed class SegmentRing(string directory, int replaySeconds)
{
    private readonly object _gate = new();
    private readonly List<CompletedSegment> _segments = [];
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    internal double EndSeconds { get { lock (_gate) return _segments.Count == 0 ? 0 : _segments[^1].EndSeconds; } }
    internal int Count { get { lock (_gate) return _segments.Count; } }
    internal double DurationSeconds { get { lock (_gate) return _segments.Count == 0 ? 0 : _segments[^1].EndSeconds - _segments[0].StartSeconds; } }

    internal void Refresh()
    {
        var listPath = Path.Combine(directory, "segments.csv");
        if (!File.Exists(listPath)) return;
        string content;
        try
        {
            using var stream = new FileStream(listPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            content = reader.ReadToEnd();
        }
        catch (IOException) { return; }
        // A list entry is written only once FFmpeg closes a segment. Ignore an incomplete final line.
        var lastNewline = content.LastIndexOf('\n');
        if (lastNewline < 0) return;
        foreach (var line in content[..(lastNewline + 1)].Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Trim().Split(',');
            if (fields.Length != 3) continue;
            var name = fields[0].Trim('"');
            // Our generated segment names never include commas or directory separators.
            if (Path.GetFileName(name) != name || !name.StartsWith("segment-", StringComparison.Ordinal) || !name.EndsWith(".ts", StringComparison.Ordinal)) continue;
            if (!double.TryParse(fields[1], CultureInfo.InvariantCulture, out var start) ||
                !double.TryParse(fields[2], CultureInfo.InvariantCulture, out var end) || !double.IsFinite(start) || !double.IsFinite(end) || end <= start) continue;
            var file = Path.Combine(directory, name);
            lock (_gate)
            {
                if (File.Exists(file) && _seen.Add(name))
                {
                    _segments.Add(new(file, start, end));
                    // A saver and the monitor can read two generations of the index concurrently.
                    _segments.Sort((a, b) => a.StartSeconds.CompareTo(b.StartSeconds));
                }
                PruneLocked();
            }
        }
        // If the app was briefly suspended, FFmpeg may have rotated its bounded index before
        // we observed old files. Remove only generated segment files older than that index.
        lock (_gate)
        {
            if (_segments.Count == 0) return;
            var first = Path.GetFileName(_segments[0].FilePath);
            foreach (var file in Directory.EnumerateFiles(directory, "segment-????????.ts"))
            {
                if (string.CompareOrdinal(Path.GetFileName(file), first) < 0)
                {
                    try { File.Delete(file); } catch (IOException) { }
                }
            }
        }
    }

    private void PruneLocked()
    {
        if (_segments.Count == 0) return;
        var cutoff = _segments[^1].EndSeconds - replaySeconds - 2;
        while (_segments.Count > 1 && _segments[0].EndSeconds < cutoff)
        {
            var oldest = _segments[0];
            try { File.Delete(oldest.FilePath); _segments.RemoveAt(0); _seen.Remove(Path.GetFileName(oldest.FilePath)); }
            catch (IOException) { break; }
        }
    }

    internal SegmentSnapshot Snapshot(string snapshotRoot, CancellationToken ct = default)
    {
        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();
            if (_segments.Count == 0) throw new InvalidOperationException("The replay buffer is still warming up. Try again in a second.");
            var cutoff = _segments[^1].EndSeconds - replaySeconds;
            // Use complete intervals inside the requested window. This avoids discarding the
            // hotkey moment by applying a duration cap to an overlapping oldest interval.
            var selected = _segments.Where(s => s.StartSeconds >= cutoff - 0.05).ToArray();
            if (selected.Length == 0) selected = [_segments[^1]];
            var snapshotDirectory = Path.Combine(snapshotRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(snapshotDirectory);
            List<string> files = [];
            try
            {
                foreach (var segment in selected)
                {
                    ct.ThrowIfCancellationRequested();
                    var destination = Path.Combine(snapshotDirectory, Path.GetFileName(segment.FilePath));
                    File.Copy(segment.FilePath, destination);
                    files.Add(destination);
                }
                return new(snapshotDirectory, files, selected[^1].EndSeconds - selected[0].StartSeconds);
            }
            catch { CorePaths.DeleteOwnedDirectory(snapshotDirectory, snapshotRoot); throw; }
        }
    }
}
