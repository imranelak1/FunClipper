using System.Diagnostics;
using System.Text;

namespace FunClipper.Core;

internal record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal static class ProcessRunner
{
    internal static Process Create(string executable, IEnumerable<string> arguments, bool stdin = false)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = stdin
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return new Process { StartInfo = info, EnableRaisingEvents = true };
    }

    internal static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments,
        CancellationToken ct = default, TimeSpan? timeout = null)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout ?? TimeSpan.FromMinutes(3));
        // Export/probe commands must never inherit a console input handle or consume
        // keystrokes. The long-running recorder uses Create(stdin: true) separately.
        var command = string.Equals(Path.GetFileNameWithoutExtension(executable), "ffmpeg", StringComparison.OrdinalIgnoreCase)
            ? new[] { "-nostdin" }.Concat(arguments) : arguments;
        using var process = Create(executable, command, stdin: true);
        ct.ThrowIfCancellationRequested();
        process.Start();
        process.StandardInput.Close();
        using var job = WindowsProcessJob.Attach(process);
        // Keep short probes and exports at normal priority. On a busy desktop,
        // BelowNormal was observed to leave a 6-second synthetic export idle for
        // minutes and make ffprobe time out despite requiring under a second of CPU.
        // ffprobe can return structured packet JSON; a tiny stderr-style tail would
        // silently corrupt otherwise valid output. Retain a bounded larger stdout.
        var output = Task.Run(() => DrainAsync(process.StandardOutput, maximumCharacters: 2 * 1024 * 1024));
        var error = Task.Run(() => DrainAsync(process.StandardError));
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            throw new TimeoutException("The media operation exceeded its time limit.");
        }
    }

    internal static async Task<string> DrainAsync(StreamReader reader, Action<string>? onLine = null,
        int maximumCharacters = 16_384)
    {
        var text = new StringBuilder();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            onLine?.Invoke(line);
            text.AppendLine(line);
            if (text.Length > maximumCharacters) text.Remove(0, text.Length - maximumCharacters * 3 / 4);
        }
        return text.ToString();
    }

    internal static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    internal static void EnsureSuccess(ProcessResult result, string operation)
    {
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{operation} failed: {result.StandardError.Trim()}");
    }
}
