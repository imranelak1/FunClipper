using System.Globalization;
using System.Text.Json;

namespace FunClipper.Core;

public enum CaptureProfile { Minimum, LowEnd, Standard }

public record ClipInfo(string Id, string FilePath, DateTimeOffset CreatedAt,
    double DurationSeconds, CaptureProfile Profile)
{
    public string Encoder { get; init; } = "unknown";
    public bool HasAudio { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

internal record ProfileSettings(int Width, int Height, int FrameRate, int ReplaySeconds, string Bitrate)
{
    internal static ProfileSettings For(CaptureProfile profile) => profile switch
    {
        CaptureProfile.Minimum => new(854, 480, 30, 15, "1500k"),
        CaptureProfile.LowEnd => new(1280, 720, 30, 30, "3500k"),
        CaptureProfile.Standard => new(1920, 1080, 60, 30, "8000k"),
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };
}

internal static class ClipCatalog
{
    internal static async Task WriteAsync(ClipInfo clip, CancellationToken ct = default)
    {
        var target = Path.ChangeExtension(clip.FilePath, ".json");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(clip), ct);
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static IReadOnlyList<ClipInfo> Read(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var clips = new List<ClipInfo>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (file.EndsWith(".vitals.json", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var clip = JsonSerializer.Deserialize<ClipInfo>(File.ReadAllText(file));
                if (clip is not null && File.Exists(clip.FilePath)) clips.Add(clip);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return clips.OrderByDescending(c => c.CreatedAt).ToArray();
    }
}

internal static class CorePaths
{
    internal static void DeleteOwnedDirectory(string directory, string owner)
    {
        var target = Path.GetFullPath(directory);
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(owner)) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(parent, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to remove a directory outside the replay workspace.");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }

    internal static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
