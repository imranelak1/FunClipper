using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace FunClipper.Desktop.GameDetection;

internal sealed class WindowsGameProcessReader
{
    private const uint SnapshotProcesses = 0x00000002, QueryLimitedInformation = 0x1000;
    private readonly Dictionary<int, CachedProcess> _cache = new();
    private sealed record CachedProcess(string FileName, long CreatedAt, string? Path);
    internal readonly record struct ProcessImage(int ProcessId, string ExecutablePath);
    internal int CachedProcessCount => _cache.Count;

    internal IReadOnlyList<ProcessImage> Read()
    {
        var images = new List<ProcessImage>();
        var alive = new HashSet<int>();
        IntPtr snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == new IntPtr(-1)) return images;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), ExecutableFile = "" };
            bool found = Process32FirstW(snapshot, ref entry);
            while (found)
            {
                int processId = unchecked((int)entry.ProcessId);
                if (processId > 0 && processId != Environment.ProcessId)
                {
                    alive.Add(processId);
                    string? path = ReadImagePath(processId, entry.ExecutableFile);
                    if (path != null) images.Add(new(processId, path));
                }
                found = Process32NextW(snapshot, ref entry);
            }
        }
        finally { CloseHandle(snapshot); }
        foreach (int id in _cache.Keys.Where(id => !alive.Contains(id)).ToArray()) _cache.Remove(id);
        // The cache never outlives the current process list and has an absolute upper bound.
        if (_cache.Count > 8192) _cache.Clear();
        return images;
    }

    private string? ReadImagePath(int processId, string fileName)
    {
        IntPtr handle = OpenProcess(QueryLimitedInformation, false, (uint)processId);
        if (handle == IntPtr.Zero) { _cache.Remove(processId); return null; }
        try
        {
            if (!GetProcessTimes(handle, out long creation, out _, out _, out _)) return null;
            if (_cache.TryGetValue(processId, out CachedProcess? cached) && cached.CreatedAt == creation && cached.FileName == fileName) return cached.Path;
            var path = new StringBuilder(1024);
            int length = path.Capacity;
            if (!QueryFullProcessImageNameW(handle, 0, path, ref length))
            {
                if (Marshal.GetLastWin32Error() != 122) { _cache[processId] = new(fileName, creation, null); return null; }
                path = new StringBuilder(32768);
                length = path.Capacity;
                if (!QueryFullProcessImageNameW(handle, 0, path, ref length)) { _cache[processId] = new(fileName, creation, null); return null; }
            }
            string image = path.ToString();
            _cache[processId] = new(fileName, creation, image);
            return image;
        }
        finally { CloseHandle(handle); }
    }

    internal static int ForegroundProcessId()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(window, out uint processId);
        return unchecked((int)processId);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExecutableFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(IntPtr handle, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageNameW(IntPtr handle, uint flags, StringBuilder path, ref int length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
