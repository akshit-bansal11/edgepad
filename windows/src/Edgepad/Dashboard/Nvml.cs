using System.Runtime.InteropServices;
using System.Text;

namespace Edgepad.Dashboard;

/// <summary>
/// NVIDIA's management library, for what the performance counters cannot give: GPU temperature and board power, and load and
/// memory when the counters have none. It is loaded only by absolute path from the two places the NVIDIA driver installs it,
/// never by bare name, so a planted nvml.dll in the working or app folder is never picked up. A machine without it, or a call
/// that fails, simply reads -1. Only the first GPU is read.
/// </summary>
internal sealed unsafe class Nvml : IDisposable
{
    private const int Success = 0; // NVML_SUCCESS
    private const int TemperatureGpu = 0; // NVML_TEMPERATURE_GPU
    private const int NameLength = 96; // NVML_DEVICE_NAME_V2_BUFFER_SIZE

    private readonly nint library;
    private readonly nint device;
    private readonly delegate* unmanaged<int> shutdown;
    private readonly delegate* unmanaged<nint, int, uint*, int> temperature;
    private readonly delegate* unmanaged<nint, uint*, int> power;
    private readonly delegate* unmanaged<nint, Utilization*, int> utilization;
    private readonly delegate* unmanaged<nint, Memory*, int> memory;
    private bool closed;

    private Nvml(nint library, nint device, nint shutdown, nint temperature, nint power, nint utilization, nint memory, string name)
    {
        this.library = library;
        this.device = device;
        this.shutdown = (delegate* unmanaged<int>)shutdown;
        this.temperature = (delegate* unmanaged<nint, int, uint*, int>)temperature;
        this.power = (delegate* unmanaged<nint, uint*, int>)power;
        this.utilization = (delegate* unmanaged<nint, Utilization*, int>)utilization;
        this.memory = (delegate* unmanaged<nint, Memory*, int>)memory;
        Name = name;
    }

    /// <summary>The first GPU's marketing name, empty when NVML gave none.</summary>
    public string Name { get; }

    /// <summary>Where nvml.dll may be loaded from, in order: the NVSMI folder, then System32. Nothing else is ever tried.</summary>
    internal static IReadOnlyList<string> Paths() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
        Path.Combine(Environment.SystemDirectory, "nvml.dll"),
    ];

    /// <summary>NVML opened on the first GPU, or null when the library is absent, refuses to start, or has no GPU.</summary>
    public static Nvml? TryOpen()
    {
        foreach (var path in Paths())
        {
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path) || !NativeLibrary.TryLoad(path, out var library))
            {
                continue;
            }

            var opened = TryStart(library);
            if (opened is not null)
            {
                return opened;
            }

            NativeLibrary.Free(library);
        }

        return null;
    }

    /// <summary>(load 0..100, temperature C, power W, memory used and total in bytes); -1 for each that did not read.</summary>
    public (int Util, int Temp, int PowerW, long Used, long Total) Read()
    {
        if (closed)
        {
            return (-1, -1, -1, -1, -1);
        }

        uint t;
        uint mw;
        Utilization u;
        Memory m;
        var temp = temperature(device, TemperatureGpu, &t) == Success ? (int)Math.Min(t, 200) : -1;
        var watts = power(device, &mw) == Success ? (int)Math.Round(mw / 1000.0, MidpointRounding.AwayFromZero) : -1;
        var util = utilization(device, &u) == Success ? (int)Math.Min(u.Gpu, 100) : -1;
        var (used, total) = memory(device, &m) == Success && m.Total > 0
            ? ((long)Math.Min(m.Used, long.MaxValue), (long)Math.Min(m.Total, long.MaxValue))
            : (-1L, -1L);
        return (util, temp, watts, used, total);
    }

    public void Dispose()
    {
        if (closed)
        {
            return;
        }

        closed = true;
        _ = shutdown();
        NativeLibrary.Free(library);
    }

    private static Nvml? TryStart(nint library)
    {
        if (!NativeLibrary.TryGetExport(library, "nvmlInit_v2", out var init)
            || !NativeLibrary.TryGetExport(library, "nvmlShutdown", out var shutdown)
            || !NativeLibrary.TryGetExport(library, "nvmlDeviceGetHandleByIndex_v2", out var byIndex)
            || !NativeLibrary.TryGetExport(library, "nvmlDeviceGetName", out var name)
            || !NativeLibrary.TryGetExport(library, "nvmlDeviceGetTemperature", out var temperature)
            || !NativeLibrary.TryGetExport(library, "nvmlDeviceGetPowerUsage", out var power)
            || !NativeLibrary.TryGetExport(library, "nvmlDeviceGetUtilizationRates", out var utilization)
            || !NativeLibrary.TryGetExport(library, "nvmlDeviceGetMemoryInfo", out var memory))
        {
            return null;
        }

        if (((delegate* unmanaged<int>)init)() != Success)
        {
            return null;
        }

        nint device;
        if (((delegate* unmanaged<uint, nint*, int>)byIndex)(0, &device) != Success)
        {
            _ = ((delegate* unmanaged<int>)shutdown)();
            return null;
        }

        byte* buffer = stackalloc byte[NameLength];
        var named = ((delegate* unmanaged<nint, byte*, uint, int>)name)(device, buffer, NameLength) == Success;
        buffer[NameLength - 1] = 0; // Whatever the library wrote, the read below stops inside the buffer.
        var text = named
            ? Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(buffer))
            : string.Empty;
        return new Nvml(library, device, shutdown, temperature, power, utilization, memory, text.Trim());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        public uint Gpu;
        public uint MemoryBus;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Memory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }
}
