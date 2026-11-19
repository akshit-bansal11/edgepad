using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Edgepad.Dashboard;

/// <summary>
/// The laptop's numbers on Windows. CPU from <c>GetSystemTimes</c> deltas, memory from <c>GlobalMemoryStatusEx</c>, disks from
/// the fixed drives, network rates from the up, non-loopback, non-tunnel interfaces (the list re-read every 30 s), ping to the
/// default gateway, GPU from <see cref="GpuPdh"/> and <see cref="Nvml"/>. There is no CPU temperature Windows gives without a
/// driver, so it is always -1. Nothing here starts a thread or a timer: <see cref="StatsReporter"/> calls in only while the
/// phone watches, and <see cref="Stop"/> closes the GPU readers when it stops.
/// </summary>
internal sealed class WindowsStats : IStatsSource, IDisposable
{
    public const int MaxDisks = 6;

    private const long InterfacesEveryMs = 30_000;
    private const long GiB = 1L << 30;
    private const string CpuKey = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private static readonly TimeSpan PingTimeout = TimeSpan.FromMilliseconds(1000);

    private readonly Lock gate = new();
    private long prevIdle;
    private long prevTotal;
    private NetworkInterface[] interfaces = [];
    private IPAddress? gateway;
    private string netName = string.Empty;
    private long interfacesAt = long.MinValue;
    private RateMeter down = new();
    private RateMeter up = new();
    private string? cpuName;

    // The GPU half, opened on the first read while the phone watches and closed by Stop.
    private readonly Lock gpuGate = new();
    private readonly GpuPdh gpu = new();
    private Nvml? nvml;
    private bool nvmlTried;
    private (string Name, long Bytes)? adapter;

    public StatsSample ReadFast()
    {
        var cpu = ReadCpu();
        var ram = ReadMemory(out _);
        var (downRate, upRate) = ReadNetwork();
        var g = ReadGpu();
        return new StatsSample(cpu, ram, g.Util, g.Vram, downRate, upRate, -1, -1, -1, g.Temp, g.PowerW);
    }

    /// <summary>Closes the GPU counters and NVML; the next read opens them again. Called when the phone stops watching.</summary>
    public void Stop()
    {
        lock (gpuGate)
        {
            gpu.Close();
            nvml?.Dispose();
            nvml = null;
            nvmlTried = false;
        }
    }

    public void Dispose() => Stop();

    public StatsInfo ReadInfo()
    {
        ReadMemory(out var totalMiB);
        string name;
        lock (gate)
        {
            RefreshInterfaces();
            name = netName;
        }

        string gpuName;
        long vramBytes;
        lock (gpuGate)
        {
            var nvidia = Nvidia();
            var total = nvidia?.Read().Total ?? -1;
            var listed = Adapter();
            gpuName = nvidia is { Name.Length: > 0 } ? nvidia.Name : listed.Name;
            vramBytes = total > 0 ? total : listed.Bytes;
        }

        return new StatsInfo(CpuName(), totalMiB, gpuName, vramBytes > 0 ? (int)Math.Min(int.MaxValue, vramBytes >> 20) : -1, name);
    }

    public IReadOnlyList<DiskEntry> ReadDisks()
    {
        var drives = new List<(string Name, long Total, long Free)>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                {
                    drives.Add((drive.Name, drive.TotalSize, drive.TotalFreeSpace));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A drive that went away or locked between the list and the read is left out.
            }
        }

        return ShapeDisks(drives);
    }

    public async ValueTask<int> PingAsync(CancellationToken cancel)
    {
        IPAddress? target;
        lock (gate)
        {
            RefreshInterfaces();
            target = gateway;
        }

        if (target is null)
        {
            return -1;
        }

        using var ping = new Ping();
        var reply = await ping.SendPingAsync(target, PingTimeout, null, null, cancel).ConfigureAwait(false);
        return reply.Status == IPStatus.Success ? (int)Math.Min(int.MaxValue, reply.RoundtripTime) : -1;
    }

    /// <summary>Whether an interface's traffic counts: up, and neither loopback nor a tunnel.</summary>
    internal static bool Counts(OperationalStatus status, NetworkInterfaceType type) =>
        status == OperationalStatus.Up && type is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel);

    /// <summary>The first IPv4 gateway that is a real address; null when there is none.</summary>
    internal static IPAddress? PickGateway(IEnumerable<IPAddress> gateways) =>
        gateways.FirstOrDefault(a =>
            a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.None) && !IPAddress.IsLoopback(a));

    /// <summary>
    /// Drives as the phone shows them: the letter alone (the wire's separators include ':'), used share rounded, sizes in whole
    /// GiB; a drive reporting no size is left out, and at most <see cref="MaxDisks"/> are kept.
    /// </summary>
    internal static IReadOnlyList<DiskEntry> ShapeDisks(IEnumerable<(string Name, long Total, long Free)> drives) =>
        drives
            .Where(d => d.Total > 0 && d.Free >= 0 && d.Free <= d.Total)
            .Select(d => new DiskEntry(
                d.Name.TrimEnd('\\', '/', ':'),
                (int)Math.Round(100.0 * (d.Total - d.Free) / d.Total, MidpointRounding.AwayFromZero),
                d.Total / GiB,
                d.Free / GiB))
            .Take(MaxDisks)
            .ToList();

    /// <summary>
    /// The counters lead on load and memory in use (every vendor feeds them); NVML fills temperature and power, and load or memory
    /// when the counters have none. The memory share is of NVML's total, else of the adapter the registry lists with the most.
    /// </summary>
    private (int Util, int Vram, int Temp, int PowerW) ReadGpu()
    {
        lock (gpuGate)
        {
            var (pdhUtil, dedicated) = gpu.Read();
            var n = Nvidia()?.Read() ?? (-1, -1, -1, -1, -1);
            var util = pdhUtil >= 0 ? pdhUtil : n.Util;
            var used = dedicated >= 0 ? dedicated : n.Used;
            var total = n.Total > 0 ? n.Total : Adapter().Bytes;
            return (util, VramPercent(used, total), n.Temp, n.PowerW);
        }
    }

    /// <summary>Memory in use as a share 0..100 of the total; -1 when either is unknown.</summary>
    internal static int VramPercent(long used, long total) =>
        used < 0 || total <= 0 ? -1 : (int)Math.Round(Math.Min(100.0, 100.0 * used / total), MidpointRounding.AwayFromZero);

    /// <summary>NVML, tried once per watch. Caller holds <see cref="gpuGate"/>.</summary>
    private Nvml? Nvidia()
    {
        if (!nvmlTried)
        {
            nvmlTried = true;
            nvml = Nvml.TryOpen();
        }

        return nvml;
    }

    // ponytail: with two adapters the share is the busiest adapter's use over the biggest adapter's size; per-LUID matching if a laptop shows odd numbers.
    private (string Name, long Bytes) Adapter() => adapter ??= GpuPdh.Adapter();

    private int ReadCpu()
    {
        if (!NativeStats.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return -1;
        }

        var total = kernel + user;
        lock (gate)
        {
            var percent = CpuMeter.Percent(prevIdle, prevTotal, idle, total);
            (prevIdle, prevTotal) = (idle, total);
            return percent;
        }
    }

    private static int ReadMemory(out int totalMiB)
    {
        var status = new NativeStats.MemoryStatusEx { Length = (uint)Marshal.SizeOf<NativeStats.MemoryStatusEx>() };
        if (!NativeStats.GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0)
        {
            totalMiB = -1;
            return -1;
        }

        totalMiB = (int)Math.Min(int.MaxValue, status.TotalPhys >> 20);
        return (int)Math.Round(100.0 * (status.TotalPhys - status.AvailPhys) / status.TotalPhys, MidpointRounding.AwayFromZero);
    }

    private (int Down, int Up) ReadNetwork()
    {
        lock (gate)
        {
            RefreshInterfaces();
            long received = 0;
            long sent = 0;
            foreach (var nic in interfaces)
            {
                try
                {
                    var stats = nic.GetIPv4Statistics();
                    received += stats.BytesReceived;
                    sent += stats.BytesSent;
                }
                catch (NetworkInformationException)
                {
                    // An adapter that vanished since the list was read adds nothing; the next refresh drops it.
                }
            }

            var now = Environment.TickCount64;
            return (down.Update(received, now), up.Update(sent, now));
        }
    }

    /// <summary>Re-reads the interface list at most every 30 s. Caller holds <see cref="gate"/>.</summary>
    private void RefreshInterfaces()
    {
        var now = Environment.TickCount64;
        if (now - interfacesAt < InterfacesEveryMs)
        {
            return;
        }

        interfacesAt = now;
        NetworkInterface[] found;
        try
        {
            found = [.. NetworkInterface.GetAllNetworkInterfaces().Where(n => Counts(n.OperationalStatus, n.NetworkInterfaceType))];
        }
        catch (NetworkInformationException)
        {
            found = [];
        }

        if (!found.Select(n => n.Id).SequenceEqual(interfaces.Select(n => n.Id)))
        {
            // A sum over a different set of adapters is not comparable with the last one: start the rates afresh.
            down = new RateMeter();
            up = new RateMeter();
        }

        interfaces = found;
        gateway = null;
        netName = string.Empty;
        foreach (var nic in found)
        {
            try
            {
                if (PickGateway(nic.GetIPProperties().GatewayAddresses.Select(g => g.Address)) is { } address)
                {
                    gateway = address;
                    netName = nic.Name;
                    break;
                }
            }
            catch (NetworkInformationException)
            {
                // No properties for this adapter; try the next.
            }
        }
    }

    private string CpuName()
    {
        if (cpuName is null)
        {
            try
            {
                cpuName = (Registry.GetValue(CpuKey, "ProcessorNameString", null) as string)?.Trim() ?? string.Empty;
            }
            catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
                cpuName = string.Empty;
            }
        }

        return cpuName;
    }
}
