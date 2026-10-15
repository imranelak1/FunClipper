using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FunClipper.Core;

// If the desktop process crashes or is forcibly closed, Windows closes this handle
// and terminates its FFmpeg child instead of leaving capture running in the background.
internal sealed class WindowsProcessJob : IDisposable
{
    private readonly SafeFileHandle _handle;
    private WindowsProcessJob(SafeFileHandle handle) => _handle = handle;

    internal static WindowsProcessJob? Attach(Process process)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle.IsInvalid) { handle.Dispose(); return null; }
        var information = new ExtendedLimitInformation
        { BasicLimitInformation = new() { LimitFlags = 0x2000 /* JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE */ } };
        if (!SetInformationJobObject(handle, 9, ref information, (uint)Marshal.SizeOf<ExtendedLimitInformation>()) ||
            !AssignProcessToJobObject(handle, process.Handle))
        { handle.Dispose(); return null; }
        return new(handle);
    }

    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        internal long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        internal uint LimitFlags;
        internal UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        internal uint ActiveProcessLimit;
        internal UIntPtr Affinity;
        internal uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        internal ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        internal BasicLimitInformation BasicLimitInformation;
        internal IoCounters IoInfo;
        internal UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass,
        ref ExtendedLimitInformation information, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
