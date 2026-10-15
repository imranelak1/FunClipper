using System.Diagnostics;

namespace FunClipper.Core;

public sealed class RecorderService : IAsyncDisposable
{
    private readonly string _replayRoot;
    private readonly string _snapshotRoot;
    private readonly string _diagnosticsRoot;
    private readonly CaptureAudioMode _audioMode;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _saving = new(1, 1);
    private CaptureSession? _session;
    private bool _disposed;
    private string _status = "Recording is off";

    public RecorderService(string dataDirectory) : this(dataDirectory, CaptureAudioMode.System) { }

    internal RecorderService(string dataDirectory, CaptureAudioMode audioMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var root = Path.GetFullPath(dataDirectory);
        ClipsDirectory = Path.Combine(root, "clips");
        _replayRoot = Path.Combine(root, "replay");
        _snapshotRoot = Path.Combine(root, "snapshots");
        _diagnosticsRoot = Path.Combine(root, "diagnostics");
        _audioMode = audioMode;
        Directory.CreateDirectory(ClipsDirectory);
        Directory.CreateDirectory(_replayRoot);
        Directory.CreateDirectory(_snapshotRoot);
    }

    public bool IsRecording => IsSessionRecording(_session);
    public bool IsReplayReady => IsRecording && CompletedSegmentCount > 0;
    public int CompletedSegmentCount => _session?.Ring.Count ?? 0;
    public double BufferedSeconds => _session?.Ring.DurationSeconds ?? 0;
    public string? LastDiagnosticPath { get; private set; }
    public string Status => _status;
    public string ClipsDirectory { get; }
    public string EncoderName => _session?.Encoder.Description ?? "Not recording";
    public string AudioStatus => _session is { } session ? session.Audio is null
        ? "Video only · " + session.AudioUnavailableReason
        : session.Audio.Failure is { } failure ? "Audio capture failed · " + failure : "System audio · no microphone"
        : "System audio starts with recording";
    public int? CaptureProcessId
    {
        get
        {
            var session = _session;
            try { return IsSessionRecording(session) ? session!.Process.Id : null; }
            catch (InvalidOperationException) { return null; }
        }
    }
    public long? DroppedFrames => _session?.DroppedFrames;
    public event Action<string>? StatusChanged;

    public async Task StartAsync(CaptureProfile profile, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Desktop capture requires Windows 10 or newer.");
        await _lifecycle.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRecording) return;
            if (_session is not null) await StopSessionAsync(_session);
            SetStatus("Checking capture support…");
            var settings = ProfileSettings.For(profile);
            var diagnostics = new CaptureDiagnostics(_diagnosticsRoot);
            LastDiagnosticPath = diagnostics.FilePath;
            var encoder = await EncoderConfiguration.SelectAsync(profile,
                message => { diagnostics.Write(message); SetStatus(message); }, ct);
            ct.ThrowIfCancellationRequested();
            var directory = Path.Combine(_replayRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            diagnostics.Write($"Starting {profile}; encoder={encoder.Description}; audio={_audioMode}");
            LoopbackAudioPump? audio = null;
            string audioReason = _audioMode == CaptureAudioMode.Disabled ? "Audio disabled" : "Audio device unavailable";
            try { if (_audioMode != CaptureAudioMode.Disabled) audio = new LoopbackAudioPump(_audioMode == CaptureAudioMode.GeneratedTestTone); }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException or NotSupportedException)
            { audioReason = e.Message; }
            Process? process = null;
            CaptureSession? session = null;
            try
            {
                var arguments = BuildCaptureArguments(directory, settings, encoder, audio);
                diagnostics.Write("FFmpeg: " + string.Join(" ", arguments));
                process = ProcessRunner.Create(MediaTools.FindFfmpeg(), arguments, stdin: true);
                session = new CaptureSession(process, directory, profile, settings, encoder, audio, audioReason);
                session.Diagnostics = diagnostics;
                _session = session;
                process.Start();
                session.Started = true;
                session.Job = WindowsProcessJob.Attach(process);
                // Use normal scheduling with bounded encoder/filter threads. BelowNormal
                // can starve capture on a busy PC and shorten the actual replay timeline.
                session.Errors = Task.Run(() => ProcessRunner.DrainAsync(process.StandardError, diagnostics.Error));
                session.Progress = Task.Run(() => ProcessRunner.DrainAsync(process.StandardOutput, line =>
                {
                    if (line.StartsWith("frame=", StringComparison.Ordinal) || line.StartsWith("out_time=", StringComparison.Ordinal))
                        diagnostics.Write("Progress: " + line);
                    if (line.StartsWith("drop_frames=", StringComparison.Ordinal) && long.TryParse(line.AsSpan(12), out var dropped))
                        session.DroppedFrames = dropped;
                }));
                audio?.StartWriting();
                session.Monitor = Task.Run(() => MonitorAsync(session));
                SetStatus($"Warming up · {encoder.Description} · {AudioStatus}");
                var deadline = Stopwatch.StartNew();
                while (session.Ring.Count == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    if (process.HasExited)
                        throw new InvalidOperationException("Desktop capture could not start: " + (await session.Errors).Trim());
                    if (deadline.Elapsed > TimeSpan.FromSeconds(12))
                    {
                        foreach (var artifact in new DirectoryInfo(directory).GetFiles())
                            diagnostics.Write($"Startup artifact: {artifact.Name}; {artifact.Length} bytes");
                        throw new TimeoutException("Desktop capture did not produce a replay segment. " + diagnostics.ErrorSummary + " Diagnostics: " + diagnostics.FilePath);
                    }
                    await Task.Delay(100, ct);
                }
                SetStatus($"Recording · {encoder.Description} · {AudioStatus}");
                diagnostics.Write($"Ready; complete segments={session.Ring.Count}; buffered seconds={session.Ring.DurationSeconds:0.00}");
            }
            catch (Exception error)
            {
                diagnostics.Write("Startup failed: " + error);
                if (session is not null) await StopSessionAsync(session);
                else
                {
                    try { if (audio is not null) await audio.DisposeAsync(); }
                    finally { process?.Dispose(); CorePaths.DeleteOwnedDirectory(directory, _replayRoot); }
                }
                SetStatus("Recording is off · capture could not start");
                throw;
            }
        }
        catch
        {
            if (_session is null) SetStatus("Recording is off");
            throw;
        }
        finally { _lifecycle.Release(); }
    }

    private static IReadOnlyList<string> BuildCaptureArguments(string directory, ProfileSettings settings,
        EncoderConfiguration encoder, LoopbackAudioPump? audio)
    {
        List<string> args = ["-hide_banner", "-loglevel", "info", "-y", "-threads", "2", "-filter_threads", "2",
            "-thread_queue_size", "4", "-f", "lavfi", "-i", $"ddagrab=output_idx=0:framerate={settings.FrameRate}:draw_mouse=0"];
        if (audio is not null)
            args.AddRange(["-thread_queue_size", "8", "-f", "f32le", "-ar", LoopbackAudioPump.SampleRate.ToString(),
                "-ac", LoopbackAudioPump.Channels.ToString(), "-probesize", "32", "-analyzeduration", "0", "-i", audio.PipePath]);
        // Keep the source clock even if a busy PC misses capture frames. Re-stamping
        // by frame number shortens real elapsed time; resampling duplicates missing
        // frames and drops repeated timestamps while retaining the replay timeline.
        args.AddRange(["-map", "0:v:0", "-vf", encoder.CaptureFilter(settings) + $",setpts=PTS-STARTPTS,fps={settings.FrameRate}:start_time=0"]);
        if (audio is not null) args.AddRange(["-map", "1:a:0", "-c:a", "aac", "-b:a", "96k", "-af", "aresample=async=1:first_pts=0"]);
        else args.Add("-an");
        args.AddRange(encoder.CaptureArguments(settings));
        args.AddRange(["-f", "segment", "-segment_time", "1", "-segment_format", "mpegts", "-reset_timestamps", "1",
            "-segment_list", Path.Combine(directory, "segments.csv"), "-segment_list_type", "csv",
            "-segment_list_size", (settings.ReplaySeconds + 5).ToString(), "-progress", "pipe:1", "-stats_period", "1", "-nostats",
            Path.Combine(directory, "segment-%08d.ts")]);
        return args;
    }

    private async Task MonitorAsync(CaptureSession session)
    {
        string? failure = null;
        try
        {
            while (!session.Stop.IsCancellationRequested && !session.Process.HasExited)
            {
                session.Ring.Refresh();
                await Task.Delay(200, session.Stop.Token);
            }
            session.Ring.Refresh();
            if (!session.Stopping && ReferenceEquals(_session, session))
            {
                failure = (await session.Errors).Trim();
            }
        }
        catch (OperationCanceledException) when (session.Stop.IsCancellationRequested) { }
        catch (Exception error) { failure = error.Message; }
        finally
        {
            if (failure is not null && !session.Stopping)
            {
                // A broken monitor must not leave capture running. Cleanup owns the lifecycle
                // lock and awaits this monitor, so dispatch it without awaiting it here.
                ProcessRunner.Kill(session.Process);
                _ = CleanupUnexpectedSessionAsync(session, failure);
            }
        }
    }

    private async Task CleanupUnexpectedSessionAsync(CaptureSession session, string failure)
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (!ReferenceEquals(_session, session) || session.Stopping) return;
            await StopSessionAsync(session);
            SetStatus("Recording stopped unexpectedly" + (string.IsNullOrEmpty(failure) ? "" : " · " + failure));
        }
        catch (Exception error) { SetStatus("Recording stopped unexpectedly · cleanup: " + error.Message); }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_session is not null) await StopSessionAsync(_session);
            SetStatus("Recording is off");
        }
        finally { _lifecycle.Release(); }
    }

    private async Task StopSessionAsync(CaptureSession session)
    {
        session.Stopping = true;
        session.Diagnostics?.Write("Stopping capture");
        try
        {
            session.Stop.Cancel();
            // FFmpeg's stdin remains a command pipe; loopback PCM uses a separate named pipe.
            try
            {
                if (!session.Process.HasExited)
                {
                    await session.Process.StandardInput.WriteLineAsync("q");
                    await session.Process.StandardInput.FlushAsync();
                }
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException) { }
            try { if (session.Audio is not null) await session.Audio.DisposeAsync(); }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException)
            { SetStatus("Closing recording · audio cleanup: " + e.Message); }
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await session.Process.WaitForExitAsync(deadline.Token);
            }
            catch (OperationCanceledException) { ProcessRunner.Kill(session.Process); await session.Process.WaitForExitAsync(); }
            catch (InvalidOperationException) { }
            try { await Task.WhenAll(session.Errors, session.Progress, session.Monitor); }
            finally
            {
                // A saver keeps its own copies; coordinate copying before deleting the ring.
                await _saving.WaitAsync();
                try { CorePaths.DeleteOwnedDirectory(session.Directory, _replayRoot); }
                finally { _saving.Release(); }
            }
        }
        finally
        {
            // Even an early cleanup exception must release OS ownership and end capture.
            ProcessRunner.Kill(session.Process);
            try { session.Job?.Dispose(); }
            finally
            {
                try { session.Process.Dispose(); }
                finally
                {
                    session.Stop.Dispose();
                    if (ReferenceEquals(_session, session)) _session = null;
                    session.Diagnostics?.Write("Capture stopped; process ownership released");
                }
            }
        }
    }

    public Task<ClipInfo> SaveReplayAsync(CancellationToken ct = default) => SaveReplayAsync(null, ct);

    public async Task<ClipInfo> SaveReplayAsync(string? gameName, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var session = _session;
        if (session is null || !IsSessionRecording(session))
            throw new InvalidOperationException("Start recording before saving a replay.");
        SegmentSnapshot? snapshot = null;
        await _saving.WaitAsync(ct);
        try
        {
            if (!ReferenceEquals(_session, session) || session.Stopping)
                throw new InvalidOperationException("Recording stopped before this replay could be saved.");
            var latest = session.Ring.EndSeconds;
            var wait = Stopwatch.StartNew();
            // Include the segment containing the hotkey moment, without stopping the recorder.
            while (session.Ring.EndSeconds <= latest && IsSessionRecording(session) && wait.Elapsed < TimeSpan.FromSeconds(2))
                await Task.Delay(50, ct);
            session.Ring.Refresh();
            snapshot = await Task.Run(() => session.Ring.Snapshot(_snapshotRoot, ct), ct);
        }
        finally { _saving.Release(); }
        var id = Guid.NewGuid().ToString("N");
        var destination = Path.Combine(ClipsDirectory, id + ".mp4");
        var pending = destination + ".partial.mp4";
        try
        {
            // Safe relative file names eliminate concat-file quoting and arbitrary path interpretation.
            var list = Path.Combine(snapshot.Directory, "concat.txt");
            await File.WriteAllLinesAsync(list, snapshot.Files.Select(file => $"file '{Path.GetFileName(file)}'"), ct);
            var result = await ProcessRunner.RunAsync(MediaTools.FindFfmpeg(),
                ["-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "1", "-i", list,
                    "-t", session.Settings.ReplaySeconds.ToString(), "-map", "0:v:0", "-map", "0:a?", "-c", "copy", "-movflags", "+faststart", pending], ct);
            ProcessRunner.EnsureSuccess(result, "Saving replay");
            var info = await MediaTools.ProbeAsync(pending, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(pending, destination);
            var clip = new ClipInfo(id, destination, DateTimeOffset.Now, info.Duration, session.Profile)
            {
                Encoder = session.Encoder.Name, HasAudio = info.HasAudio, Width = info.Width, Height = info.Height,
                GameName = string.IsNullOrWhiteSpace(gameName) ? null : gameName.Trim()
            };
            await ClipCatalog.WriteAsync(clip, ct);
            session.Diagnostics?.Write($"Replay saved; duration={clip.DurationSeconds:0.00}s; size={clip.Width}x{clip.Height}; audio={clip.HasAudio}");
            return clip;
        }
        catch { MediaTools.DeleteOutput(destination); throw; }
        finally
        {
            if (File.Exists(pending)) File.Delete(pending);
            CorePaths.DeleteOwnedDirectory(snapshot.Directory, _snapshotRoot);
        }
    }

    public IReadOnlyList<ClipInfo> GetClips() => ClipCatalog.Read(ClipsDirectory);

    private static bool IsSessionRecording(CaptureSession? session)
    {
        if (session is not { Stopping: false, Started: true }) return false;
        try { return !session.Process.HasExited; }
        catch (InvalidOperationException) { return false; }
    }

    private void SetStatus(string message)
    {
        _status = message;
        // UI observers cannot break recorder ownership or resource cleanup.
        foreach (Action<string> observer in StatusChanged?.GetInvocationList() ?? [])
        {
            try { observer(message); }
            catch (Exception error) { Trace.WriteLine("Recorder status observer failed: " + error); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
        // Semaphores intentionally remain valid for a racing operation to release safely.
    }

    private sealed class CaptureSession(Process process, string directory, CaptureProfile profile, ProfileSettings settings,
        EncoderConfiguration encoder, LoopbackAudioPump? audio, string audioUnavailableReason)
    {
        internal Process Process { get; } = process;
        internal string Directory { get; } = directory;
        internal CaptureProfile Profile { get; } = profile;
        internal ProfileSettings Settings { get; } = settings;
        internal EncoderConfiguration Encoder { get; } = encoder;
        internal LoopbackAudioPump? Audio { get; } = audio;
        internal string AudioUnavailableReason { get; } = audioUnavailableReason;
        internal SegmentRing Ring { get; } = new(directory, settings.ReplaySeconds);
        internal CancellationTokenSource Stop { get; } = new();
        internal bool Stopping { get; set; }
        internal bool Started { get; set; }
        internal WindowsProcessJob? Job { get; set; }
        internal long? DroppedFrames { get; set; }
        internal Task<string> Errors { get; set; } = Task.FromResult("");
        internal Task<string> Progress { get; set; } = Task.FromResult("");
        internal Task Monitor { get; set; } = Task.CompletedTask;
        internal CaptureDiagnostics? Diagnostics { get; set; }
    }
}

internal enum CaptureAudioMode { System, Disabled, GeneratedTestTone }
