using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Edgepad.Dashboard;

/// <summary>
/// GPU load and dedicated memory in use from the performance counters every WDDM driver feeds, whatever the vendor. The query
/// opens on the first read and is replaced every 30 s so engines of processes started since are counted; the replacement is
/// collected once before it takes over, so the rate counter never reads blank for a second. Any failure reads -1 and is logged
/// once. <see cref="Close"/> releases everything.
/// </summary>
internal sealed partial class GpuPdh : IDisposable
{
    internal const int ItemSize = 24; // PDH_FMT_COUNTERVALUE_ITEM_W: name pointer, then CStatus at 8 and the 8-byte value at 16.

    private const long RefreshMs = 30_000;
    private const uint FormatDouble = 0x00000200; // PDH_FMT_DOUBLE
    private const uint FormatLarge = 0x00000400; // PDH_FMT_LARGE
    private const uint FormatNoCap = 0x00008000; // PDH_FMT_NOCAP100: a sum over engines may pass 100; the math clamps it
    private const uint MoreData = 0x800007D2; // PDH_MORE_DATA
    private const string EnginePath = @"\GPU Engine(*)\Utilization Percentage";
    private const string MemoryPath = @"\GPU Adapter Memory(*)\Dedicated Usage";
    private const string AdaptersKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private Query? current;
    private Query? next;
    private long openedAt;
    private long retryAt;
    private bool logged;

    /// <summary>Busy share 0..100 of the busiest engine, and the most dedicated memory any adapter has in use, in bytes; -1 each when unknown.</summary>
    public (int Util, long Dedicated) Read()
    {
        var now = Environment.TickCount64;
        if (current is null)
        {
            if (now >= retryAt)
            {
                current = Open();
                openedAt = now;
                retryAt = current is null ? now + RefreshMs : 0;
            }

            return (-1, -1); // A rate counter needs a second collection before it has a value.
        }

        if (next is not null)
        {
            current.Dispose();
            current = next;
            next = null;
            openedAt = now;
        }
        else if (now - openedAt >= RefreshMs)
        {
            next = Open();
            openedAt = now; // A failed replacement is tried again in 30 s, not every second.
        }

        if (current is null || PdhCollectQueryData(current.Handle) != 0)
        {
            Fail("collect");
            return (-1, -1);
        }

        var engines = ReadArray(current.Engines, FormatDouble | FormatNoCap);
        var memory = ReadArray(current.Memory, FormatLarge);
        var util = engines is null ? -1 : Utilization(engines);
        var dedicated = memory is { Count: > 0 } ? (long)memory.Max(m => m.Value) : -1;
        return (util, dedicated);
    }

    /// <summary>Closes the counters; the next <see cref="Read"/> opens them again.</summary>
    public void Close()
    {
        current?.Dispose();
        next?.Dispose();
        current = null;
        next = null;
        retryAt = 0;
    }

    public void Dispose() => Close();

    /// <summary>
    /// The engine an instance name such as <c>pid_123_luid_0x0_0x0000C3F1_phys_0_eng_0_engtype_3D</c> measures: everything from
    /// <c>luid_</c> on, so every process's share of one engine adds up under one key. Null when the name has no <c>luid_</c>.
    /// </summary>
    internal static string? EngineKey(string instance)
    {
        var at = instance.IndexOf("luid_", StringComparison.Ordinal);
        return at < 0 ? null : instance[at..];
    }

    /// <summary>The engine type at the end of an instance name (<c>3D</c>, <c>Copy</c>, <c>VideoDecode</c>), or null.</summary>
    internal static string? EngineType(string instance)
    {
        var at = instance.LastIndexOf("engtype_", StringComparison.Ordinal);
        return at < 0 || at + 8 >= instance.Length ? null : instance[(at + 8)..];
    }

    /// <summary>
    /// What Task Manager shows as GPU load: each engine's per-process shares summed, the busiest engine wins, clamped to 0..100.
    /// -1 when no instance names an engine; a negative or non-finite value counts as 0.
    /// </summary>
    internal static int Utilization(IEnumerable<(string Name, double Value)> instances)
    {
        var sums = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (name, value) in instances)
        {
            if (EngineKey(name) is { } key)
            {
                sums[key] = sums.GetValueOrDefault(key) + (double.IsFinite(value) && value > 0 ? value : 0);
            }
        }

        return sums.Count == 0 ? -1 : (int)Math.Round(Math.Clamp(sums.Values.Max(), 0, 100), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The items of a PDH_FMT_COUNTERVALUE_ITEM_W array in <paramref name="buffer"/>. An item whose CStatus is neither valid (0)
    /// nor new (1), or whose name pointer is null, is skipped.
    /// </summary>
    internal static List<(string Name, double Value)> ParseItems(nint buffer, uint count, bool asDouble)
    {
        var items = new List<(string Name, double Value)>((int)Math.Min(count, 4096));
        for (var i = 0L; i < count; i++)
        {
            var item = buffer + (nint)(i * ItemSize);
            var status = (uint)Marshal.ReadInt32(item, 8);
            var namePointer = Marshal.ReadIntPtr(item);
            if (status > 1 || namePointer == 0)
            {
                continue;
            }

            var raw = Marshal.ReadInt64(item, 16);
            items.Add((Marshal.PtrToStringUni(namePointer) ?? string.Empty, asDouble ? BitConverter.Int64BitsToDouble(raw) : raw));
        }

        return items;
    }

    /// <summary>The display adapter with the most dedicated memory, by its driver's name, and that memory in bytes; ("", -1) when none reads.</summary>
    internal static (string Name, long Bytes) Adapter()
    {
        var best = (Name: string.Empty, Bytes: -1L);
        try
        {
            using var adapters = Registry.LocalMachine.OpenSubKey(AdaptersKey);
            foreach (var sub in adapters?.GetSubKeyNames() ?? [])
            {
                if (sub.Length != 4 || !sub.All(char.IsAsciiDigit))
                {
                    continue; // "Properties" and the like, which only SYSTEM may read.
                }

                try
                {
                    using var key = adapters!.OpenSubKey(sub);
                    var name = key?.GetValue("DriverDesc") as string ?? string.Empty;
                    var bytes = MemoryBytes(key?.GetValue("HardwareInformation.qwMemorySize") ?? key?.GetValue("HardwareInformation.MemorySize"));
                    if (name.Length > 0 && bytes > best.Bytes)
                    {
                        best = (name, bytes);
                    }
                }
                catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    // An adapter key this user cannot read is left out.
                }
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // No adapter list: the name stays empty.
        }

        return best;
    }

    /// <summary>The size a <c>HardwareInformation</c> value holds: a QWORD, a DWORD, or 4 or 8 little-endian bytes.</summary>
    internal static long MemoryBytes(object? value) => value switch
    {
        long l when l > 0 => l,
        int i => (uint)i,
        byte[] { Length: 8 } b => BitConverter.ToInt64(b),
        byte[] { Length: 4 } b => BitConverter.ToUInt32(b),
        _ => -1,
    };

    private Query? Open()
    {
        if (PdhOpenQuery(null, 0, out var handle) != 0)
        {
            Fail("open");
            return null;
        }

        var query = new Query(handle);
        if (PdhAddEnglishCounter(handle, EnginePath, 0, out query.Engines) != 0
            || PdhAddEnglishCounter(handle, MemoryPath, 0, out query.Memory) != 0
            || PdhCollectQueryData(handle) != 0)
        {
            query.Dispose();
            Fail("add");
            return null;
        }

        return query;
    }

    private static List<(string Name, double Value)>? ReadArray(nint counter, uint format)
    {
        uint size = 0;
        var status = PdhGetFormattedCounterArray(counter, format, ref size, out var count, 0);
        if (status == 0 && count == 0)
        {
            return [];
        }

        if (status != MoreData || size == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((nint)size);
        try
        {
            return PdhGetFormattedCounterArray(counter, format, ref size, out count, buffer) == 0
                ? ParseItems(buffer, count, (format & FormatDouble) != 0)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void Fail(string what)
    {
        if (!logged)
        {
            logged = true;
            Log.Write(string.Create(CultureInfo.InvariantCulture, $"GPU counters unavailable ({what})"));
        }
    }

    private sealed class Query(nint handle) : IDisposable
    {
        public nint Engines;
        public nint Memory;

        public nint Handle { get; private set; } = handle;

        public void Dispose()
        {
            if (Handle != 0)
            {
                _ = PdhCloseQuery(Handle);
                Handle = 0;
            }
        }
    }

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhOpenQuery(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhAddEnglishCounter(nint query, string path, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhGetFormattedCounterArray(nint counter, uint format, ref uint bufferSize, out uint itemCount, nint buffer);

    [LibraryImport("pdh.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint PdhCloseQuery(nint query);
}
