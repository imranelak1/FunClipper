using System.Diagnostics;
using System.IO.Pipes;
using NAudio.Wave;

namespace FunClipper.Core;

// A half-second bounded PCM queue bridges WASAPI's callbacks to FFmpeg's pipe.
// The clocked writer inserts silence when Windows stops delivering packets during silence.
internal sealed class LoopbackAudioPump : IAsyncDisposable
{
    internal const int SampleRate = 48_000;
    internal const int Channels = 2;
    private const int BytesPerSecond = SampleRate * Channels * sizeof(float);
    private readonly object _gate = new();
    private readonly byte[] _ring = new byte[BytesPerSecond / 2];
    private readonly NamedPipeServerStream _pipe;
    private readonly WasapiLoopbackCapture? _capture;
    private readonly bool _generatedTestTone;
    private readonly CancellationTokenSource _stop = new();
    private int _read, _count;
    private Task? _writer;
    private bool _disposed;
    internal string PipePath { get; }
    internal string? Failure { get; private set; }

    internal LoopbackAudioPump(bool generatedTestTone = false)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WASAPI audio requires Windows.");
        var pipeName = "FunClipper-audio-" + Guid.NewGuid().ToString("N");
        PipePath = @"\\.\pipe\" + pipeName;
        _pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 16_384);
        _generatedTestTone = generatedTestTone;
        if (generatedTestTone) return;
        try
        {
            _capture = new WasapiLoopbackCapture { WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels) };
            _capture.DataAvailable += OnAudio;
            _capture.RecordingStopped += (_, args) =>
            {
                if (args.Exception is not null) Failure = args.Exception.Message;
            };
            _capture.StartRecording();
        }
        catch
        {
            try { _capture?.Dispose(); }
            finally { _pipe.Dispose(); _stop.Dispose(); }
            throw;
        }
    }

    // Audio pacing must keep running when the desktop UI dispatcher is busy or hidden.
    internal void StartWriting() => _writer = Task.Run(() => WriteAsync(_stop.Token));

    private void OnAudio(object? sender, WaveInEventArgs args)
    {
        lock (_gate)
        {
            // WASAPI reuses its event buffer in NAudio 2; copy before the callback returns.
            var input = args.Buffer.AsSpan(0, args.BytesRecorded);
            if (input.Length > _ring.Length) input = input[^_ring.Length..];
            var overflow = Math.Max(0, _count + input.Length - _ring.Length);
            _read = (_read + overflow) % _ring.Length;
            _count -= overflow;
            var write = (_read + _count) % _ring.Length;
            var first = Math.Min(input.Length, _ring.Length - write);
            input[..first].CopyTo(_ring.AsSpan(write));
            input[first..].CopyTo(_ring);
            _count += input.Length;
        }
    }

    private async Task WriteAsync(CancellationToken ct)
    {
        try
        {
            await _pipe.WaitForConnectionAsync(ct);
            // Flush capture packets collected while the FFmpeg process was opening its inputs.
            lock (_gate) { _count = 0; _read = 0; }
            var block = new byte[BytesPerSecond / 50];
            var clock = Stopwatch.StartNew();
            long packets = 0;
            while (!ct.IsCancellationRequested)
            {
                Array.Clear(block);
                if (_generatedTestTone)
                {
                    // Deterministic input for the live screen-capture test. Never reads a sound device.
                    for (var sample = 0; sample < SampleRate / 50; sample++)
                    {
                        var value = (float)(0.15 * Math.Sin(2 * Math.PI * 440 * (packets * (SampleRate / 50) + sample) / SampleRate));
                        BitConverter.TryWriteBytes(block.AsSpan(sample * 8, 4), value);
                        BitConverter.TryWriteBytes(block.AsSpan(sample * 8 + 4, 4), value);
                    }
                }
                else lock (_gate)
                {
                    var take = Math.Min(block.Length, _count);
                    var first = Math.Min(take, _ring.Length - _read);
                    _ring.AsSpan(_read, first).CopyTo(block);
                    _ring.AsSpan(0, take - first).CopyTo(block.AsSpan(first));
                    _read = (_read + take) % _ring.Length;
                    _count -= take;
                }
                await _pipe.WriteAsync(block, ct);
                packets++;
                var remaining = packets * 20 - clock.ElapsedMilliseconds;
                if (remaining > 0) await Task.Delay((int)remaining, ct);
                // Keep the sample clock continuous after pipe backpressure or a scheduler pause.
                // Skipping the packet counter drops elapsed audio time: raw PCM has no separate
                // timestamps, so FFmpeg then throttles video to that shortened audio timeline.
                // Async pipe writes naturally yield while FFmpeg catches up; missing input is silence.
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { if (!ct.IsCancellationRequested) Failure = e.Message; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        try
        {
            if (_capture is not null)
            {
                _capture.DataAvailable -= OnAudio;
                try { _capture.StopRecording(); }
                finally { _capture.Dispose(); }
            }
        }
        finally
        {
            try { await _pipe.DisposeAsync(); }
            finally
            {
                try { if (_writer is not null) await _writer; }
                finally { _stop.Dispose(); }
            }
        }
    }
}
