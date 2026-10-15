using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using FunClipper.Core;

internal static class CaptureLiveChecks
{
    internal static Task<int> RunAsync(bool withAudio, CaptureProfile profile)
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            using var form = new CapturePattern();
            form.Shown += async (_, _) =>
            {
                var result = 1;
                try
                {
                    // Ensure the complete app-owned pattern is presented before ddagrab starts.
                    await Task.Delay(800);
                    await CheckAsync(withAudio, profile);
                    result = 0;
                }
                catch (Exception e) { Console.Error.WriteLine(e); }
                finally { form.Close(); completion.TrySetResult(result); }
            };
            Application.Run(form);
        }) { IsBackground = true, Name = "FunClipper-owned-capture-pattern" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static async Task CheckAsync(bool withAudio, CaptureProfile profile)
    {
        var root = Path.Combine(Path.GetTempPath(), "FunClipper-live-checks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Console.WriteLine("Live capture workspace: " + root);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        await using var recorder = new RecorderService(root,
            withAudio ? CaptureAudioMode.GeneratedTestTone : CaptureAudioMode.Disabled);
        recorder.StatusChanged += message => Console.WriteLine("  " + message);
        try
        {
            await recorder.StartAsync(profile, deadline.Token);
            Require(recorder.IsRecording && recorder.IsReplayReady && recorder.CompletedSegmentCount > 0,
                "StartAsync must return only after the first completed segment exists.");
            var processId = recorder.CaptureProcessId ?? throw new InvalidOperationException("No capture PID.");
            Console.WriteLine($"  Ready PID={processId}; encoder={recorder.EncoderName}");
            var timelineStart = recorder.BufferedSeconds;
            var recordingClock = Stopwatch.StartNew();
            await Task.Delay(4500, deadline.Token);
            var first = await recorder.SaveReplayAsync(deadline.Token);
            await ValidateAsync(first, withAudio, deadline.Token);
            var firstEnd = recorder.BufferedSeconds;
            await Task.Delay(1800, deadline.Token);
            var second = await recorder.SaveReplayAsync(deadline.Token);
            await ValidateAsync(second, withAudio, deadline.Token);
            Require(recorder.IsRecording && recorder.CaptureProcessId == processId,
                "Saving replays must leave the same capture process running.");
            Require(recorder.BufferedSeconds > firstEnd, "Capture must continue advancing after a replay save.");
            var coverage = (recorder.BufferedSeconds - timelineStart) / recordingClock.Elapsed.TotalSeconds;
            Require(coverage > 0.8, $"Capture must cover real elapsed time; measured {coverage:0.00}x.");
            Console.WriteLine($"  Replay wall-clock coverage: {coverage:0.00}x");
            await recorder.StopAsync().WaitAsync(TimeSpan.FromSeconds(7));
            Require(!recorder.IsRecording && !recorder.IsReplayReady && recorder.CaptureProcessId is null,
                "Stop must clear the active recorder and readiness.");
            Require(!Directory.EnumerateFileSystemEntries(Path.Combine(root, "replay")).Any(),
                "Stop must remove replay segments.");
            Require(!Directory.EnumerateFileSystemEntries(Path.Combine(root, "snapshots")).Any(),
                "Saved snapshots must be cleaned up.");
            Console.WriteLine($"PASS: actual desktop Start → replay → repeated save → playable {(withAudio ? "video+tone" : "video")} → Stop.");
            // Preserve diagnostics only so failures are useful without retaining captured content.
        }
        finally
        {
            await recorder.StopAsync();
            foreach (var directory in new[] { "clips", "replay", "snapshots" })
                CorePaths.DeleteOwnedDirectory(Path.Combine(root, directory), root);
            Console.WriteLine("Text diagnostics: " + recorder.LastDiagnosticPath);
        }
    }

    private static async Task ValidateAsync(ClipInfo clip, bool audio, CancellationToken ct)
    {
        var settings = ProfileSettings.For(clip.Profile);
        Require(clip.Width == settings.Width && clip.Height == settings.Height && clip.DurationSeconds > 1 && clip.DurationSeconds <= settings.ReplaySeconds + 0.2,
            "Live replay must have the selected dimensions and duration.");
        Require(clip.HasAudio == audio, "Live replay audio stream does not match the test mode.");
        var decode = await ProcessRunner.RunAsync(MediaTools.FindFfmpeg(),
            ["-hide_banner", "-loglevel", "error", "-err_detect", "explode", "-i", clip.FilePath,
                "-fps_mode", "passthrough", "-enc_time_base", "demux", "-f", "null", "-"], ct, TimeSpan.FromSeconds(15));
        ProcessRunner.EnsureSuccess(decode, "Decoding actual replay");
        Require(string.IsNullOrWhiteSpace(decode.StandardError), "Live replay decode reported errors: " + decode.StandardError);
        var packets = await ProcessRunner.RunAsync(MediaTools.FindExecutable("ffprobe"),
            ["-v", "error", "-show_entries", "packet=stream_index,dts_time:stream=index,codec_type,duration", "-of", "json", clip.FilePath], ct, TimeSpan.FromSeconds(10));
        ProcessRunner.EnsureSuccess(packets, "Checking live replay timestamp order");
        using (var document = JsonDocument.Parse(packets.StandardOutput))
        {
            var previous = double.NegativeInfinity;
            var frameCount = 0;
            foreach (var packet in document.RootElement.GetProperty("packets").EnumerateArray())
            {
                if (packet.GetProperty("stream_index").GetInt32() != 0) continue;
                var timestamp = double.Parse(packet.GetProperty("dts_time").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                Require(timestamp > previous, "Saved replay video DTS must strictly increase.");
                previous = timestamp;
                frameCount++;
            }
            Require(frameCount >= 25, "The live replay must contain real encoded frames.");
            if (audio)
            {
                var streams = document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                var videoDuration = double.Parse(streams.First(s => s.GetProperty("codec_type").GetString() == "video")
                    .GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                var audioDuration = double.Parse(streams.First(s => s.GetProperty("codec_type").GetString() == "audio")
                    .GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                Require(Math.Abs(videoDuration - audioDuration) < 0.25,
                    $"Audio and video must retain the same timeline ({videoDuration:0.00}s vs {audioDuration:0.00}s).");
            }
        }
        if (audio)
        {
            var tone = await ProcessRunner.RunAsync(MediaTools.FindFfmpeg(),
                ["-hide_banner", "-loglevel", "info", "-i", clip.FilePath, "-vn", "-af", "volumedetect", "-f", "null", "-"], ct, TimeSpan.FromSeconds(10));
            ProcessRunner.EnsureSuccess(tone, "Checking generated test tone");
            Require(!tone.StandardError.Contains("mean_volume: -inf", StringComparison.Ordinal) &&
                tone.StandardError.Contains("mean_volume:", StringComparison.Ordinal), "The audio track must contain the generated tone.");
        }
        Console.WriteLine($"  Saved and decoded {clip.DurationSeconds:0.00}s / {new FileInfo(clip.FilePath).Length} bytes / audio={clip.HasAudio}");
    }

    private static void Require(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }

    private sealed class CapturePattern : Form
    {
        private readonly System.Windows.Forms.Timer _animation = new() { Interval = 33 };
        private int _frame;

        internal CapturePattern()
        {
            Text = "FunClipper controlled capture check";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen!.Bounds;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(15, 35, 65);
            _animation.Tick += (_, _) => { _frame++; Invalidate(); };
            _animation.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var bright = new SolidBrush(Color.FromArgb(65, 210, 185));
            using var text = new SolidBrush(Color.White);
            using var font = new Font("Segoe UI", 22);
            var x = (_frame * 13) % Math.Max(1, ClientSize.Width - 150);
            e.Graphics.FillRectangle(bright, x, ClientSize.Height / 2, 150, 150);
            e.Graphics.DrawString("FunClipper — controlled recorder test", font, text, 60, 60);
            e.Graphics.DrawString("Only this generated pattern is being captured. This window closes automatically.",
                font, text, 60, 120);
            e.Graphics.DrawString($"Frame {_frame}", font, text, 60, 180);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _animation.Dispose();
            base.Dispose(disposing);
        }
    }
}
