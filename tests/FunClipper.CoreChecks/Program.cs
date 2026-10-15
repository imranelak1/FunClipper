using System.IO.Pipes;
using FunClipper.Core;

if (args.Contains("--live"))
{
    var profile = args.Contains("--standard") ? CaptureProfile.Standard : args.Contains("--low") ? CaptureProfile.LowEnd : CaptureProfile.Minimum;
    Environment.ExitCode = await CaptureLiveChecks.RunAsync(args.Contains("--audio"), profile);
    return;
}

var workspace = Path.Combine(Path.GetTempPath(), "FunClipper-core-checks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workspace);
var passed = 0;
try
{
    await Check("synthetic demo, media metadata, and persistent catalog", async () =>
    {
        await using var recorder = new RecorderService(Path.Combine(workspace, "library"));
        Assert(!recorder.IsRecording, "A recorder must remain off until explicitly started.");
        var clip = await MediaTools.CreateDemoAsync(recorder.ClipsDirectory);
        Assert(Math.Abs(clip.DurationSeconds - 6) < 0.1, "Demo duration must be six seconds.");
        Assert(clip.HasAudio && clip.Width == 854 && clip.Height == 480, "Demo stream metadata is incorrect.");
        await File.WriteAllTextAsync(clip.FilePath + ".vitals.json", "[{\"cpu\":42}]");
        Assert(recorder.GetClips().Count == 1, "The catalog must ignore vitals sidecars.");
        await recorder.StopAsync();
        await Throws<InvalidOperationException>(() => recorder.SaveReplayAsync());
    });

    await Check("accurate trim, optional audio, original preservation, and invalid bounds", async () =>
    {
        var source = await MediaTools.CreateDemoAsync(Path.Combine(workspace, "trim-source"));
        var original = new FileInfo(source.FilePath).Length;
        var output = Path.Combine(workspace, "trim-output");
        var muted = await MediaTools.TrimAsync(source, 1.25, 3.75, true, output);
        Assert(Math.Abs(muted.DurationSeconds - 2.5) < 0.08, "Trim must match the selected 2.5-second interval.");
        Assert(!muted.HasAudio, "A muted export must not contain an audio stream.");
        var withAudio = await MediaTools.TrimAsync(source, 0.5, 2.5, false, output);
        Assert(withAudio.HasAudio && Math.Abs(withAudio.DurationSeconds - 2) < 0.08, "Unmuted export must retain audio and duration.");
        var minimum = await MediaTools.TrimAsync(source, 5.9, 6, true, output);
        Assert(Math.Abs(minimum.DurationSeconds - 0.1) < 0.04, "The valid 5.9-to-6.0 minimum trim must export.");
        Assert(new FileInfo(source.FilePath).Length == original, "Trimming must preserve the source.");
        await Throws<ArgumentOutOfRangeException>(() => MediaTools.TrimAsync(source, -1, 3, false, output));
        await Throws<ArgumentOutOfRangeException>(() => MediaTools.TrimAsync(source, 4, 3, false, output));
        await Throws<ArgumentOutOfRangeException>(() => MediaTools.TrimAsync(source, 0, 100, false, output));
        await Throws<ArgumentOutOfRangeException>(() => MediaTools.TrimAsync(source, double.NaN, 2, false, output));
        await Throws<FileNotFoundException>(() => MediaTools.ProbeDurationAsync(Path.Combine(workspace, "missing.mp4")));
    });

    await Check("cancellation kills media processes and removes partial exports", async () =>
    {
        var output = Path.Combine(workspace, "cancelled");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Throws<OperationCanceledException>(() => MediaTools.CreateDemoAsync(output, cancelled.Token));
        Assert(!Directory.EnumerateFiles(output).Any(), "Cancelled exports must leave no output or metadata.");
        using var active = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Throws<OperationCanceledException>(() => ProcessRunner.RunAsync(MediaTools.FindFfmpeg(),
            ["-hide_banner", "-loglevel", "error", "-re", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30", "-t", "60", "-f", "null", "-"], active.Token));
    });

    await Check("bounded completed-segment ring, concurrent snapshots, and replay remux", async () =>
    {
        var directory = Path.Combine(workspace, "ring");
        var snapshots = Path.Combine(workspace, "snapshots");
        Directory.CreateDirectory(directory);
        var result = await ProcessRunner.RunAsync(MediaTools.FindFfmpeg(),
            ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=30",
             "-f", "lavfi", "-i", "sine=frequency=220:sample_rate=48000", "-t", "36", "-c:v", "libx264",
             "-threads", "2", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-g", "30", "-bf", "0", "-sc_threshold", "0",
             "-c:a", "aac", "-f", "segment", "-segment_time", "1", "-reset_timestamps", "1", "-segment_list_size", "20",
             "-segment_list", Path.Combine(directory, "segments.csv"), "-segment_list_type", "csv", Path.Combine(directory, "segment-%08d.ts")]);
        ProcessRunner.EnsureSuccess(result, "Synthetic segmentation");
        var ring = new SegmentRing(directory, 15);
        ring.Refresh();
        Assert(ring.Count is >= 15 and <= 19, "The ring must contain only a bounded replay history.");
        Assert(Directory.GetFiles(directory, "*.ts").Length <= 19, "Segments missed while the app was suspended must also be pruned.");
        var snapshot = ring.Snapshot(snapshots);
        Assert(snapshot.DurationSeconds is >= 14 and <= 15.05, "A snapshot must preserve the latest complete 15-second interval.");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Throws<OperationCanceledException>(() => Task.Run(() => ring.Snapshot(snapshots, cancelled.Token)));
        // Advance the writer index and prune source files while a saver owns independent copies.
        var more = Enumerable.Range(36, 20).Select(i => $"segment-{i:00000000}.ts,{i},{i + 1}\n");
        foreach (var entry in more)
        {
            var name = entry.Split(',')[0];
            File.Copy(snapshot.Files[0], Path.Combine(directory, name));
            await File.AppendAllTextAsync(Path.Combine(directory, "segments.csv"), entry);
        }
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(ring.Refresh)));
        Assert(snapshot.Files.All(File.Exists), "Pruning must not delete snapshot copies.");
        var list = Path.Combine(snapshot.Directory, "concat.txt");
        await File.WriteAllLinesAsync(list, snapshot.Files.Select(f => $"file '{Path.GetFileName(f)}'"));
        var replay = Path.Combine(workspace, "synthetic-replay.mp4");
        var remux = await ProcessRunner.RunAsync(MediaTools.FindFfmpeg(),
            ["-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "1", "-i", list, "-c", "copy", "-movflags", "+faststart", replay]);
        ProcessRunner.EnsureSuccess(remux, "Synthetic replay save");
        var metadata = await MediaTools.ProbeAsync(replay);
        Assert(metadata.Duration is >= 14 and <= 15.2 && metadata.HasAudio, "A saved replay must play with audio and bounded duration.");
    });

    await Check("FFmpeg can ingest a Windows named audio pipe alongside synthetic video", async () =>
    {
        if (!OperatingSystem.IsWindows()) return;
        var name = "FunClipper-test-" + Guid.NewGuid().ToString("N");
        await using var pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var output = Path.Combine(workspace, "pipe-audio.mp4");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var process = ProcessRunner.RunAsync(MediaTools.FindFfmpeg(),
            ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=black:size=320x180:rate=30",
             "-f", "f32le", "-ar", "48000", "-ac", "2", "-probesize", "32", "-analyzeduration", "0", "-i", @"\\.\pipe\" + name,
             "-t", "2", "-c:v", "libx264", "-threads", "2", "-preset", "ultrafast", "-c:a", "aac", output], timeout.Token);
        await pipe.WaitForConnectionAsync(timeout.Token);
        // Two seconds of generated silence; no system audio is recorded during this test.
        await pipe.WriteAsync(new byte[48_000 * 2 * 4 * 2], timeout.Token);
        await pipe.DisposeAsync();
        ProcessRunner.EnsureSuccess(await process, "Named-pipe audio");
        var metadata = await MediaTools.ProbeAsync(output);
        Assert(metadata.HasAudio && Math.Abs(metadata.Duration - 2) < 0.1, "The named pipe must produce a valid two-second audio track.");
    });

    await Check("clocked audio pipe preserves elapsed samples after backpressure without reading system audio", async () =>
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var pump = new LoopbackAudioPump(generatedTestTone: true);
        await using var reader = new NamedPipeClientStream(".", Path.GetFileName(pump.PipePath), PipeDirection.In,
            PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        pump.StartWriting();
        await reader.ConnectAsync(timeout.Token);
        // Deliberately fill the small output pipe. Once reads resume, raw PCM must
        // catch up instead of silently throwing away nearly a second of elapsed time.
        await Task.Delay(800, timeout.Token);
        var bytes = 0;
        var block = new byte[16_384];
        var readClock = System.Diagnostics.Stopwatch.StartNew();
        while (bytes < 48_000 * 2 * 4)
            bytes += await reader.ReadAsync(block, timeout.Token);
        Assert(readClock.Elapsed < TimeSpan.FromMilliseconds(650),
            "The pipe must promptly recover the continuous one-second sample clock after backpressure.");
        Assert(pump.Failure is null, "The clocked audio pipe must remain healthy.");
    });

    await Check("hardware encoder detection uses synthetic frames and never starts screen capture", async () =>
    {
        var encoder = await EncoderConfiguration.SelectAsync(CaptureProfile.Minimum, Console.WriteLine, CancellationToken.None);
        Assert(encoder.Name is "h264_amf" or "h264_nvenc" or "h264_qsv" or "libx264", "Unknown encoder selected.");
        Console.WriteLine($"  Available pipeline: {encoder.Description}");
        if (encoder.Name != "libx264")
        {
            var source = await MediaTools.CreateDemoAsync(Path.Combine(workspace, "hardware-source"));
            var exported = await MediaTools.TrimAsync(source with { Encoder = encoder.Name }, 1, 3, false,
                Path.Combine(workspace, "hardware-export"));
            Assert(exported.HasAudio && Math.Abs(exported.DurationSeconds - 2) < 0.1,
                "Hardware trim export must retain audio and the selected duration.");
        }
        var segmentDirectory = Path.Combine(workspace, "selected-encoder-segments");
        Directory.CreateDirectory(segmentDirectory);
        List<string> capture = ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=854x480:rate=30",
            "-vf", "format=nv12", "-frames:v", "95"];
        capture.AddRange(encoder.CaptureArguments(ProfileSettings.For(CaptureProfile.Minimum)));
        capture.AddRange(["-an", "-f", "segment", "-segment_time", "1", "-reset_timestamps", "1", "-segment_list_type", "csv",
            "-segment_list", Path.Combine(segmentDirectory, "segments.csv"), Path.Combine(segmentDirectory, "segment-%08d.ts")]);
        var segmented = await ProcessRunner.RunAsync(MediaTools.FindFfmpeg(), capture, timeout: TimeSpan.FromSeconds(20));
        ProcessRunner.EnsureSuccess(segmented, "Selected hardware encoder replay boundaries");
        var ring = new SegmentRing(segmentDirectory, 15);
        ring.Refresh();
        Assert(ring.Count >= 3,
            "The selected encoder must emit independent one-second replay segments, not one never-ending file.");
        var middle = Directory.GetFiles(segmentDirectory, "*.ts").Order().ElementAt(1);
        var independent = await ProcessRunner.RunAsync(MediaTools.FindFfmpeg(),
            ["-hide_banner", "-loglevel", "error", "-err_detect", "explode", "-i", middle, "-fps_mode", "passthrough", "-f", "null", "-"],
            timeout: TimeSpan.FromSeconds(10));
        ProcessRunner.EnsureSuccess(independent, "Decoding an independent replay segment");
        Assert(string.IsNullOrWhiteSpace(independent.StandardError), "Each middle segment must decode independently from its IDR.");
    });

    await Check("AMD GPU resizing retains dimensions, audio, aspect ratio, and monotonic timestamps", async () =>
    {
        if (!OperatingSystem.IsWindows()) return;
        var encoder = new EncoderConfiguration("h264_amf", true, FrameCopy: true);
        if (!await EncoderConfiguration.ProbeAsync(encoder, ProfileSettings.For(CaptureProfile.Minimum), CancellationToken.None))
        {
            Console.WriteLine("  AMD VPP is unavailable on this PC; the CPU resize fallback remains selected.");
            return;
        }
        foreach (var profile in Enum.GetValues<CaptureProfile>())
        {
            var settings = ProfileSettings.For(profile);
            foreach (var size in new[] { "1920x1080", "1600x1200", "800x1200" })
            {
                Console.WriteLine($"  AMD resize check: {profile}, {size}");
                var output = Path.Combine(workspace, $"amf-{profile}-{size}.mp4");
                // Repeated capture timestamps must be normalized before packets reach the muxer.
                var inputTiming = size == "1920x1080" ? $"setpts=floor(N/2)/({settings.FrameRate}*TB)," : "";
                List<string> arguments = ["-hide_banner", "-loglevel", "error", "-y", "-threads", "2", "-filter_threads", "2",
                    "-init_hw_device", "d3d11va=fc", "-init_hw_device", "amf=af@fc", "-filter_hw_device", "af",
                    "-f", "lavfi", "-i", $"color=c=white:size={size}:rate={settings.FrameRate}",
                    "-f", "lavfi", "-i", "sine=frequency=220:sample_rate=48000",
                    "-map", "0:v:0", "-map", "1:a:0", "-vf", inputTiming + "format=bgra,hwupload=derive_device=d3d11va," + encoder.CaptureFilter(settings)];
                arguments.AddRange(encoder.CaptureArguments(settings));
                arguments.AddRange(["-c:a", "aac", "-t", "1", "-movflags", "+faststart", output]);
                var result = await ProcessRunner.RunAsync(MediaTools.FindFfmpeg(), arguments, timeout: TimeSpan.FromSeconds(30));
                ProcessRunner.EnsureSuccess(result, "Copied AMD GPU resize");
                var metadata = await MediaTools.ProbeAsync(output);
                Assert(metadata.Width == settings.Width && metadata.Height == settings.Height && metadata.HasAudio && Math.Abs(metadata.Duration - 1) < 0.1,
                    $"AMD {profile} output must retain the exact profile dimensions, audio, and one-second duration.");
                var packets = await ProcessRunner.RunAsync(MediaTools.FindExecutable("ffprobe"),
                    ["-v", "error", "-select_streams", "v:0", "-show_packets", "-show_entries", "packet=dts_time", "-of", "json", output],
                    timeout: TimeSpan.FromSeconds(10));
                ProcessRunner.EnsureSuccess(packets, "Checking encoded capture timestamps");
                using (var timestamps = System.Text.Json.JsonDocument.Parse(packets.StandardOutput))
                {
                    var previous = double.NegativeInfinity;
                    var count = 0;
                    foreach (var packet in timestamps.RootElement.GetProperty("packets").EnumerateArray())
                    {
                        var timestamp = double.Parse(packet.GetProperty("dts_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                        Assert(double.IsFinite(timestamp) && timestamp > previous,
                            "Encoded video DTS must strictly increase even when capture input timestamps repeat.");
                        previous = timestamp;
                        count++;
                    }
                    Assert(Math.Abs(count - settings.FrameRate) <= 1,
                        "Timestamp normalization must retain the selected output frame rate.");
                }
                var detected = await ProcessRunner.RunAsync(MediaTools.FindFfmpeg(),
                    ["-hide_banner", "-loglevel", "info", "-i", output, "-vf", "cropdetect=limit=24:round=2", "-frames:v", "8", "-an", "-f", "null", "-"],
                    timeout: TimeSpan.FromSeconds(10));
                ProcessRunner.EnsureSuccess(detected, "Checking AMD letterbox geometry");
                var bounds = System.Text.RegularExpressions.Regex.Matches(detected.StandardError, @"crop=(\d+):(\d+):\d+:\d+");
                Assert(bounds.Count > 0, "The decoded AMD output must contain a visible test rectangle.");
                var visible = bounds[^1];
                var width = int.Parse(visible.Groups[1].Value);
                var height = int.Parse(visible.Groups[2].Value);
                var expectedAspect = size == "1600x1200" ? 4.0 / 3 : size == "800x1200" ? 2.0 / 3 : 16.0 / 9;
                Assert(Math.Abs((double)width / height - expectedAspect) < 0.015,
                    "The full display must retain its aspect ratio with padding, without stretching or cropping.");
            }
        }
    });

    await Check("Windows job ownership terminates an orphaned FFmpeg child", async () =>
    {
        if (!OperatingSystem.IsWindows()) return;
        using var child = ProcessRunner.Create(MediaTools.FindFfmpeg(),
            ["-hide_banner", "-loglevel", "error", "-re", "-f", "lavfi", "-i", "color=c=black:size=320x180:rate=30", "-t", "60", "-f", "null", "-"]);
        child.Start();
        var stdout = ProcessRunner.DrainAsync(child.StandardOutput);
        var stderr = ProcessRunner.DrainAsync(child.StandardError);
        try
        {
            using var ownership = WindowsProcessJob.Attach(child);
            Assert(ownership is not null, "The OS must support kill-on-close ownership for this check.");
            ownership!.Dispose();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await child.WaitForExitAsync(timeout.Token);
            Assert(child.HasExited, "Closing the owner handle must terminate the child.");
        }
        finally
        {
            ProcessRunner.Kill(child);
            await child.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }
    });

    await Check("unexpected process exits and reader failures release recorder ownership despite observer errors", async () =>
    {
        if (!OperatingSystem.IsWindows()) return;
        // Exercise the real session monitor with synthetic FFmpeg only; constructing the
        // private session through reflection avoids invoking desktop or WASAPI startup.
        const System.Reflection.BindingFlags members = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var sessionType = typeof(RecorderService).GetNestedType("CaptureSession", System.Reflection.BindingFlags.NonPublic)!;
        foreach (var readerFailure in new[] { false, true })
        {
            var root = Path.Combine(workspace, "lifecycle-" + readerFailure);
            await using var recorder = new RecorderService(root);
            recorder.StatusChanged += _ => throw new InvalidOperationException("Synthetic UI observer failure");
            var directory = Path.Combine(root, "replay", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            using var child = ProcessRunner.Create(MediaTools.FindFfmpeg(),
                ["-hide_banner", "-loglevel", "error", "-re", "-f", "lavfi", "-i", "color=c=black:size=320x180:rate=30", "-t", "60", "-f", "null", "-"], stdin: true);
            child.Start();
            using var observedProcess = System.Diagnostics.Process.GetProcessById(child.Id);
            var stdout = ProcessRunner.DrainAsync(child.StandardOutput);
            var stderr = ProcessRunner.DrainAsync(child.StandardError);
            var session = Activator.CreateInstance(sessionType, members, null,
                [child, directory, CaptureProfile.Minimum, ProfileSettings.For(CaptureProfile.Minimum), new EncoderConfiguration("libx264", false), null, "Synthetic audio disabled"], null)!;
            sessionType.GetProperty("Started", members)!.SetValue(session, true);
            sessionType.GetProperty("Job", members)!.SetValue(session, WindowsProcessJob.Attach(child));
            sessionType.GetProperty("Progress", members)!.SetValue(session, stdout);
            sessionType.GetProperty("Errors", members)!.SetValue(session, readerFailure ? Task.FromException<string>(new IOException("Synthetic reader failure")) : stderr);
            typeof(RecorderService).GetField("_session", members)!.SetValue(recorder, session);
            try
            {
                var monitor = (Task)typeof(RecorderService).GetMethod("MonitorAsync", members)!.Invoke(recorder, [session])!;
                sessionType.GetProperty("Monitor", members)!.SetValue(session, monitor);
                ProcessRunner.Kill(child);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (typeof(RecorderService).GetField("_session", members)!.GetValue(recorder) is not null)
                    await Task.Delay(20, deadline.Token);
                await monitor.WaitAsync(deadline.Token);
                Assert(observedProcess.HasExited && !recorder.IsRecording && !Directory.Exists(directory),
                    "An unexpected exit or reader error must terminate capture, clear ownership, and remove the replay session.");
                await recorder.StopAsync();
                Assert(recorder.Status == "Recording is off", "A broken UI observer must not interrupt StopAsync.");
            }
            finally
            {
                ProcessRunner.Kill(child);
                await Task.WhenAll(stdout, stderr);
            }
        }
    });

    Console.WriteLine($"PASS: {passed} core checks. No screen or real audio capture was started.");
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}
finally { CorePaths.DeleteOwnedDirectory(workspace, Path.Combine(Path.GetTempPath(), "FunClipper-core-checks")); }

async Task Check(string name, Func<Task> action)
{
    await action();
    passed++;
    Console.WriteLine("PASS: " + name);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
