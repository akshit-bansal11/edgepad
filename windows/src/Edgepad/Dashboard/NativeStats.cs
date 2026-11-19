using System.Runtime.InteropServices;

namespace Edgepad.Dashboard;

/// <summary>The kernel32 calls the stats source reads CPU time and memory with, and the structs they take.</summary>
internal static partial class NativeStats
{
    /// <summary>Cumulative idle, kernel (idle included) and user time of all processors, in 100 ns ticks.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemTimes(out long idle, out long kernel, out long user);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    /// <summary>MEMORYSTATUSEX; <see cref="Length"/> must be set to the struct's size before the call.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
