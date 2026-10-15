using System.Text.Json;

namespace FunClipper.Core;

public static class MediaTools
{
    public static string FindFfmpeg() => FindExecutable("ffmpeg");

    internal static string FindExecutable(string name)
    {
        var executable = name + (OperatingSystem.IsWindows() ? ".exe" : "");
        var configured = Environment.GetEnvironmentVariable("FUNCLIPPER_FFMPEG");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var candidate = name == "ffmpeg" ? configured : Path.Combine(Path.GetDirectoryName(configured) ?? "", executable);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        var directories = new[] { Path.Combine(AppContext.BaseDirectory, "tools"), AppContext.BaseDirectory }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        foreach (var directory in directories)
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), executable);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch (ArgumentException) { }
        }
        throw new FileNotFoundException($"{executable} was not found. Put FFmpeg and FFprobe in the app's tools folder or add them to PATH.");
    }

    public static async Task<double> ProbeDurationAsync(string path, CancellationToken ct = default) =>
        (await ProbeAsync(path, ct)).Duration;

    internal static async Task<(double Duration, bool HasAudio, int Width, int Height)> ProbeAsync(string path,
        CancellationToken ct = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The clip could not be found.", path);
        var result = await ProcessRunner.RunAsync(FindExecutable("ffprobe"),
            ["-v", "error", "-show_entries", "format=duration:stream=codec_type,width,height", "-of", "json", path], ct,
            TimeSpan.FromSeconds(15));
        ProcessRunner.EnsureSuccess(result, "Reading clip information");
        using var json = JsonDocument.Parse(result.StandardOutput);
        var durationText = json.RootElement.GetProperty("format").GetProperty("duration").GetString();
        if (!double.TryParse(durationText, System.Globalization.CultureInfo.InvariantCulture, out var duration) || duration <= 0)
            throw new InvalidDataException("The clip has no readable duration.");
        bool audio = false; int width = 0, height = 0;
        foreach (var stream in json.RootElement.GetProperty("streams").EnumerateArray())
        {
            var type = stream.GetProperty("codec_type").GetString();
            if (type == "audio") audio = true;
            if (type == "video")
            {
                width = stream.GetProperty("width").GetInt32();
                height = stream.GetProperty("height").GetInt32();
            }
        }
        if (width <= 0 || height <= 0) throw new InvalidDataException("The file does not contain video.");
        return (duration, audio, width, height);
    }

    public static async Task<ClipInfo> TrimAsync(ClipInfo clip, double start, double end, bool mute,
        string outputDirectory, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(clip);
        // Decimal UI bounds such as 5.9 to 6.0 can subtract to just below 0.1 in binary floating point.
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start || end - start < 0.1 - 1e-9)
            throw new ArgumentOutOfRangeException(nameof(start), "Choose at least 0.1 seconds between the trim handles.");
        var source = await ProbeAsync(clip.FilePath, ct);
        if (end > source.Duration + 0.05)
            throw new ArgumentOutOfRangeException(nameof(end), "The trim extends beyond the end of the clip.");
        end = Math.Min(end, source.Duration);
        Directory.CreateDirectory(outputDirectory);
        var id = Guid.NewGuid().ToString("N");
        var destination = Path.GetFullPath(Path.Combine(outputDirectory, $"{id}.mp4"));
        var pending = destination + ".partial.mp4";
        var encoder = clip.Encoder is "h264_amf" or "h264_qsv" or "h264_nvenc" ? clip.Encoder : "libx264";
        List<string> args = ["-hide_banner", "-loglevel", "error", "-y", "-threads", "2", "-ss", CorePaths.Number(start),
            "-i", clip.FilePath, "-t", CorePaths.Number(end - start), "-map", "0:v:0"];
        if (!mute) args.AddRange(["-map", "0:a?"]);
        args.AddRange(["-c:v", encoder, "-pix_fmt", "yuv420p"]);
        args.AddRange(EncoderConfiguration.ExportArguments(encoder));
        args.AddRange(mute ? ["-an"] : ["-c:a", "aac", "-b:a", "128k"]);
        args.AddRange(["-movflags", "+faststart", pending]);
        try
        {
            var result = await ProcessRunner.RunAsync(FindFfmpeg(), args, ct);
            ProcessRunner.EnsureSuccess(result, "Exporting trim");
            var info = await ProbeAsync(pending, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(pending, destination);
            var saved = new ClipInfo(id, destination, DateTimeOffset.Now, info.Duration, clip.Profile)
            { Encoder = encoder, HasAudio = info.HasAudio, Width = info.Width, Height = info.Height, GameName = clip.GameName };
            await ClipCatalog.WriteAsync(saved, ct);
            return saved;
        }
        catch { DeleteOutput(destination); throw; }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    public static async Task<ClipInfo> CreateDemoAsync(string outputDirectory, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var id = Guid.NewGuid().ToString("N");
        var destination = Path.GetFullPath(Path.Combine(outputDirectory, $"{id}.mp4"));
        var pending = destination + ".partial.mp4";
        try
        {
            var result = await ProcessRunner.RunAsync(FindFfmpeg(),
                ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=854x480:rate=30",
                 "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "6", "-c:v", "libx264", "-threads", "2",
                 "-preset", "ultrafast", "-crf", "25", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "96k", "-movflags", "+faststart", pending], ct);
            ProcessRunner.EnsureSuccess(result, "Creating sample clip");
            var info = await ProbeAsync(pending, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(pending, destination);
            var clip = new ClipInfo(id, destination, DateTimeOffset.Now, info.Duration, CaptureProfile.Minimum)
                { Encoder = "libx264", HasAudio = info.HasAudio, Width = info.Width, Height = info.Height };
            await ClipCatalog.WriteAsync(clip, ct);
            return clip;
        }
        catch { DeleteOutput(destination); throw; }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    internal static void DeleteOutput(string destination)
    {
        if (File.Exists(destination)) File.Delete(destination);
        var metadata = Path.ChangeExtension(destination, ".json");
        if (File.Exists(metadata)) File.Delete(metadata);
    }
}
