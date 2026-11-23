using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Edgepad.Dashboard;

/// <summary>
/// The GPU's load, video memory, temperature and power. NVIDIA through NVML, loaded only from the absolute paths the driver
/// installs it at (never by bare name, so a library planted on the search path is never loaded); otherwise an amdgpu card's
/// sysfs files. Neither present: every number is -1. Nothing is opened until the first read, and nothing throws.
/// </summary>
internal sealed unsafe class LinuxGpu(string drm = "/sys/class/drm") : IDisposable
{
    private static readonly string[] NvmlPaths =
    [
        "/usr/lib/x86_64-linux-gnu/libnvidia-ml.so.1",
        "/usr/lib64/libnvidia-ml.so.1",
        "/usr/lib/aarch64-linux-gnu/libnvidia-ml.so.1",
        "/usr/lib/libnvidia-ml.so.1",
    ];

    private readonly Lock gate = new();
    private Nvml? nvml;
    private bool nvmlTried;
    private (string Device, string? Hwmon)? amd;
    private bool amdTried;
    private bool disposed;

    /// <summary>One reading: load and VRAM percent, temperature C, power W; -1 unknown.</summary>
    internal readonly record struct Reading(int Util, int Vram, int Temp, int PowerW)
    {
        public static readonly Reading None = new(-1, -1, -1, -1);
    }

    public Reading Read()
    {
        lock (gate)
        {
            if (disposed)
            {
                return Reading.None;
            }

            if (Nvidia() is { } n)
            {
                return n.Read();
            }

            if (Amd() is not { } card)
            {
                return Reading.None;
            }

            var used = ReadLong(Path.Combine(card.Device, "mem_info_vram_used"));
            var total = ReadLong(Path.Combine(card.Device, "mem_info_vram_total"));
            return new Reading(
                Math.Clamp(ProcParsers.Scaled(ReadText(Path.Combine(card.Device, "gpu_busy_percent")), 1), -1, 100),
                used < 0 ? -1 : ProcParsers.Percent(used, total),
                card.Hwmon is null ? -1 : ProcParsers.Scaled(ReadText(Path.Combine(card.Hwmon, "temp1_input")), 1000),
                card.Hwmon is null ? -1 : Power(card.Hwmon));
        }
    }

    /// <summary>The GPU's name (empty unknown) and its video memory in MiB (-1 unknown).</summary>
    public (string Name, int VramMiB) Info()
    {
        lock (gate)
        {
            if (disposed)
            {
                return (string.Empty, -1);
            }

            if (Nvidia() is { } n)
            {
                return n.Info();
            }

            if (Amd() is not { } card)
            {
                return (string.Empty, -1);
            }

            var total = ReadLong(Path.Combine(card.Device, "mem_info_vram_total"));
            var name = ReadText(Path.Combine(card.Device, "product_name"))?.Trim() ?? string.Empty;
            return (name.Length > 0 ? name : "AMD Radeon", total <= 0 ? -1 : (int)Math.Min(int.MaxValue, total / (1024 * 1024)));
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            nvml?.Dispose();
            nvml = null;
        }
    }

    /// <summary>Nobody is watching: shuts NVML down and unloads it; the next <see cref="Read"/> or <see cref="Info"/> opens it again.</summary>
    public void Idle()
    {
        lock (gate)
        {
            // Only a library that opened is tried again; a machine without one is not searched on every start.
            if (nvml is not null)
            {
                nvml.Dispose();
                nvml = null;
                nvmlTried = false;
            }
        }
    }

    private static int Power(string hwmon)
    {
        // power1_average on most kernels, power1_input on newer ones for some cards; microwatts.
        var watts = ProcParsers.Scaled(ReadText(Path.Combine(hwmon, "power1_average")), 1_000_000);
        return watts >= 0 ? watts : ProcParsers.Scaled(ReadText(Path.Combine(hwmon, "power1_input")), 1_000_000);
    }

    private static string? ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long ReadLong(string path) =>
        long.TryParse(ReadText(path)?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : -1;

    private Nvml? Nvidia()
    {
        if (!nvmlTried)
        {
            nvmlTried = true;
            nvml = Nvml.Open(NvmlPaths);
        }

        return nvml;
    }

    private (string Device, string? Hwmon)? Amd()
    {
        if (amdTried)
        {
            return amd;
        }

        amdTried = true;
        try
        {
            // card0, card1 ... (not card0-DP-1, which is a connector); the first one with a load file is an amdgpu card.
            foreach (var card in Directory.EnumerateDirectories(drm, "card*").Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(card);
                var device = Path.Combine(card, "device");
                if (name.Contains('-', StringComparison.Ordinal) || !File.Exists(Path.Combine(device, "gpu_busy_percent")))
                {
                    continue;
                }

                var hwmonRoot = Path.Combine(device, "hwmon");
                var hwmon = Directory.Exists(hwmonRoot) ? Directory.EnumerateDirectories(hwmonRoot, "hwmon*").Order(StringComparer.Ordinal).FirstOrDefault() : null;
                amd = (device, hwmon);
                break;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            amd = null;
        }

        return amd;
    }

    /// <summary>The NVML calls used, for the first GPU only. A call that fails reads as -1.</summary>
    private sealed class Nvml : IDisposable
    {
        private const int Success = 0;
        private const int TemperatureGpu = 0;

        private readonly nint library;
        private readonly nint device;
        private readonly delegate* unmanaged<int> shutdown;
        private readonly delegate* unmanaged<nint, int, uint*, int> temperature;
        private readonly delegate* unmanaged<nint, uint*, int> powerUsage;
        private readonly delegate* unmanaged<nint, uint*, int> utilization;
        private readonly delegate* unmanaged<nint, ulong*, int> memory;
        private readonly delegate* unmanaged<nint, byte*, uint, int> name;

        private Nvml(nint library, nint device)
        {
            this.library = library;
            this.device = device;
            shutdown = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "nvmlShutdown");
            temperature = (delegate* unmanaged<nint, int, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetTemperature");
            powerUsage = (delegate* unmanaged<nint, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetPowerUsage");
            utilization = (delegate* unmanaged<nint, uint*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetUtilizationRates");
            memory = (delegate* unmanaged<nint, ulong*, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetMemoryInfo");
            name = (delegate* unmanaged<nint, byte*, uint, int>)NativeLibrary.GetExport(library, "nvmlDeviceGetName");
        }

        /// <summary>The first path that loads and initialises with a GPU at index 0; null for none, logged once.</summary>
        public static Nvml? Open(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var library))
                {
                    continue;
                }

                try
                {
                    if (!NativeLibrary.TryGetExport(library, "nvmlInit_v2", out var init)
                        || !NativeLibrary.TryGetExport(library, "nvmlDeviceGetHandleByIndex_v2", out var byIndex)
                        || ((delegate* unmanaged<int>)init)() != Success)
                    {
                        NativeLibrary.Free(library);
                        continue;
                    }

                    nint device;
                    if (((delegate* unmanaged<uint, nint*, int>)byIndex)(0, &device) != Success)
                    {
                        if (NativeLibrary.TryGetExport(library, "nvmlShutdown", out var stop))
                        {
                            _ = ((delegate* unmanaged<int>)stop)();
                        }

                        NativeLibrary.Free(library);
                        continue;
                    }

                    return new Nvml(library, device);
                }
                catch (EntryPointNotFoundException e)
                {
                    Log.Write($"NVML at {path} lacks a call: {e.Message}");
                    NativeLibrary.Free(library);
                }
            }

            return null;
        }

        public Reading Read()
        {
            // nvmlUtilization_t is { uint gpu; uint memory; }, nvmlMemory_t { ulong total; ulong free; ulong used; }.
            var u = stackalloc uint[2];
            var m = stackalloc ulong[3];
            uint temp;
            uint milliwatts;
            return new Reading(
                utilization(device, u) == Success ? (int)Math.Min(u[0], 100u) : -1,
                memory(device, m) == Success ? ProcParsers.Percent((long)m[2], (long)m[0]) : -1,
                temperature(device, TemperatureGpu, &temp) == Success ? (int)Math.Min(temp, 200u) : -1,
                powerUsage(device, &milliwatts) == Success ? (int)Math.Round(milliwatts / 1000.0, MidpointRounding.AwayFromZero) : -1);
        }

        public (string Name, int VramMiB) Info()
        {
            const int NameLength = 96;
            var buffer = stackalloc byte[NameLength];
            var text = string.Empty;
            if (name(device, buffer, NameLength) == Success)
            {
                var end = new ReadOnlySpan<byte>(buffer, NameLength).IndexOf((byte)0);
                text = Encoding.UTF8.GetString(buffer, end >= 0 ? end : NameLength).Trim();
            }

            var m = stackalloc ulong[3];
            var vram = memory(device, m) == Success ? (int)Math.Min((ulong)int.MaxValue, m[0] / (1024 * 1024)) : -1;
            return (text, vram);
        }

        public void Dispose()
        {
            _ = shutdown();
            NativeLibrary.Free(library);
        }
    }
}
