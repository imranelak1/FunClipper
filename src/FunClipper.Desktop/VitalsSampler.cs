using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace FunClipper.Desktop;

public sealed record VitalsSnapshot(DateTimeOffset At, double? CpuPercent, double UsedRamGb, double TotalRamGb,
    double AppCpuPercent, double AppRamMb, double? RecorderCpuPercent, double? RecorderRamMb,
    double? GpuPercent, double? DedicatedGpuGb, double? SharedGpuGb);

public sealed class VitalsSampler : IDisposable
{
    private ulong _previousIdle, _previousKernel, _previousUser;
    private readonly Process _app = Process.GetCurrentProcess();
    private double _previousAppCpu;
    private double _previousRecorderCpu;
    private long _previousTime = Stopwatch.GetTimestamp();
    private int? _recorderId;
    private Process? _recorder;
    private IntPtr _query;
    private IntPtr _gpuCounter, _dedicatedCounter, _sharedCounter;
    private readonly Queue<VitalsSnapshot> _history = new();

    public VitalsSampler()
    {
        GetSystemTimes(out var idle, out var kernel, out var user);
        _previousIdle = idle.Value; _previousKernel = kernel.Value; _previousUser = user.Value;
        _previousAppCpu = _app.TotalProcessorTime.TotalMilliseconds;
        try
        {
            if (PdhOpenQuery(null, UIntPtr.Zero, out _query) == 0)
            {
                PdhAddEnglishCounter(_query, "\\GPU Engine(*)\\Utilization Percentage", UIntPtr.Zero, out _gpuCounter);
                PdhAddEnglishCounter(_query, "\\GPU Adapter Memory(*)\\Dedicated Usage", UIntPtr.Zero, out _dedicatedCounter);
                PdhAddEnglishCounter(_query, "\\GPU Adapter Memory(*)\\Shared Usage", UIntPtr.Zero, out _sharedCounter);
                PdhCollectQueryData(_query);
            }
        }
        catch { _query = IntPtr.Zero; }
    }

    public VitalsSnapshot Sample(int? recorderProcessId = null)
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedMs = Math.Max(1, (now - _previousTime) * 1000d / Stopwatch.Frequency);
        _previousTime = now;
        double? cpu = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong total = kernel.Value - _previousKernel + user.Value - _previousUser;
            ulong idleDelta = idle.Value - _previousIdle;
            if (total > 0) cpu = Math.Clamp(100d * (total - Math.Min(total, idleDelta)) / total, 0, 100);
            _previousIdle = idle.Value; _previousKernel = kernel.Value; _previousUser = user.Value;
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        GlobalMemoryStatusEx(ref memory);
        _app.Refresh();
        double appCpu = _app.TotalProcessorTime.TotalMilliseconds;
        double appUsage = Math.Clamp((appCpu - _previousAppCpu) / elapsedMs / Environment.ProcessorCount * 100, 0, 100);
        _previousAppCpu = appCpu;
        double? recorderUsage = null, recorderRam = null;
        try
        {
            if (_recorderId != recorderProcessId)
            {
                _recorder?.Dispose();
                _recorder = recorderProcessId.HasValue ? Process.GetProcessById(recorderProcessId.Value) : null;
                _recorderId = recorderProcessId;
                _previousRecorderCpu = _recorder?.TotalProcessorTime.TotalMilliseconds ?? 0;
            }
            if (_recorder is { HasExited: false })
            {
                _recorder.Refresh();
                double recorderCpu = _recorder.TotalProcessorTime.TotalMilliseconds;
                recorderUsage = Math.Clamp((recorderCpu - _previousRecorderCpu) / elapsedMs / Environment.ProcessorCount * 100, 0, 100);
                _previousRecorderCpu = recorderCpu;
                recorderRam = _recorder.WorkingSet64 / 1048576d;
            }
        }
        catch { _recorder?.Dispose(); _recorder = null; _recorderId = null; }
        double? gpu = null, dedicated = null, shared = null;
        if (_query != IntPtr.Zero && PdhCollectQueryData(_query) == 0)
        {
            var engines = ReadCounter(_gpuCounter, groupGpuEngines: true);
            // Parallel engine percentages must not be summed. The busiest engine is a useful conservative reading.
            if (engines.Count > 0) gpu = Math.Clamp(Maximum(engines), 0, 100);
            var d = ReadCounter(_dedicatedCounter);
            var s = ReadCounter(_sharedCounter);
            if (d.Count > 0) dedicated = Sum(d) / 1073741824d;
            if (s.Count > 0) shared = Sum(s) / 1073741824d;
        }
        var sample = new VitalsSnapshot(DateTimeOffset.UtcNow, cpu,
            (memory.TotalPhysical - memory.AvailablePhysical) / 1073741824d, memory.TotalPhysical / 1073741824d,
            appUsage, _app.WorkingSet64 / 1048576d, recorderUsage, recorderRam, gpu, dedicated, shared);
        _history.Enqueue(sample);
        while (_history.Count > 120) _history.Dequeue();
        return sample;
    }

    public VitalsSnapshot[] History() => _history.ToArray();
    private static double Maximum(List<double> values) { double result = 0; foreach (var v in values) result = Math.Max(result, v); return result; }
    private static double Sum(List<double> values) { double result = 0; foreach (var v in values) result += v; return result; }

    private static List<double> ReadCounter(IntPtr counter, bool groupGpuEngines = false)
    {
        var result = new List<double>();
        var groups = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (counter == IntPtr.Zero) return result;
        uint bytes = 0, count = 0;
        const uint formatDouble = 0x200;
        uint status = PdhGetFormattedCounterArray(counter, formatDouble, ref bytes, out count, IntPtr.Zero);
        if (status != 0x800007D2 || bytes == 0 || bytes > 16 * 1024 * 1024) return result;
        IntPtr buffer = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (PdhGetFormattedCounterArray(counter, formatDouble, ref bytes, out count, buffer) != 0) return result;
            int size = Marshal.SizeOf<CounterItem>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, i * size));
                if ((item.Value.Status != 0 && item.Value.Status != 1) || !double.IsFinite(item.Value.Number)) continue;
                if (groupGpuEngines)
                {
                    string name = Marshal.PtrToStringUni(item.Name) ?? "";
                    int offset = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
                    if (offset < 0) continue;
                    string engine = name[offset..];
                    groups[engine] = groups.GetValueOrDefault(engine) + Math.Max(0, item.Value.Number);
                }
                else result.Add(item.Value.Number);
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        if (groupGpuEngines) result.AddRange(groups.Values);
        return result;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) PdhCloseQuery(_query);
        _query = IntPtr.Zero;
        _recorder?.Dispose(); _app.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; public readonly ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length, MemoryLoad; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual; }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct CounterValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Number; }
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem { public IntPtr Name; public CounterValue Value; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, UIntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, UIntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")] private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr items);
}
