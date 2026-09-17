using System.Runtime.InteropServices;

namespace Edgepad.Controls;

/// <summary>
/// The refresh rates the primary display will accept, and the switch between them: the phone's dial steps
/// through <see cref="Rates"/> by index. This is the display's refresh rate in Hz, not a game's frame rate —
/// nothing here makes anything render faster, it changes how often the panel is scanned out.
///
/// Only modes at the resolution and colour depth already in use are offered. A panel advertises 1024x768 at
/// 75 Hz as readily as 2560x1440 at 165 Hz, and a dial that shrank the desktop to buy a rate would be a
/// nasty surprise from a control the user is not looking at.
///
/// Primary display only. Multi-monitor is out of scope by choice, not by oversight: the phone has one dial
/// and no way to name which screen it means. A null device name is how Win32 spells "the display this
/// session runs on", which is the primary one.
/// </summary>
internal sealed partial class DisplayModes : IDisposable
{
    /// <summary>Nothing is waiting to be applied.</summary>
    private const int None = -1;

    /// <summary>ENUM_CURRENT_SETTINGS: the mode in force right now rather than one from the driver's list.</summary>
    private const uint CurrentSettings = uint.MaxValue;

    /// <summary>DM_INTERLACED. An interlaced 120 Hz is not the 120 Hz anyone means by it.</summary>
    private const uint Interlaced = 0x0002;

    /// <summary>DISP_CHANGE_SUCCESSFUL. Every other code — restart required, bad mode, bad flags — is a refusal.</summary>
    private const int ChangeSuccessful = 0;

    /// <summary>The DEVMODEs behind <see cref="rates"/>, kept so a switch replays exactly what the driver offered.</summary>
    private DisplayMode[] modes = [];
    private int[] rates = [];
    private int current = -1;

    /// <summary>
    /// One line per process, not per call. A dial spun against a display that cannot be read would otherwise
    /// write a log entry per flick, and the log is the only error channel a tray app has.
    /// </summary>
    private bool logged;

    private readonly AutoResetEvent wake = new(initialState: false);
    private readonly Thread worker;
    private int pending = None;
    private volatile bool disposed;

    public DisplayModes()
    {
        Refresh();
        worker = new Thread(Run) { IsBackground = true, Name = "edgepad-display" };
        worker.Start();
    }

    /// <summary>The primary display's distinct refresh rates in Hz, ascending. Empty when they cannot be read.</summary>
    public IReadOnlyList<int> Rates => rates;

    /// <summary>Index into Rates of the rate now in use, or -1.</summary>
    public int Current => current;

    /// <summary>Re-reads the display. Call after a mode change or a monitor being plugged in.</summary>
    public void Refresh()
    {
        modes = [];
        rates = [];
        current = -1;

        if (!Query(CurrentSettings, out var now))
        {
            LogOnce("Display refresh rates unavailable: the primary display's current mode could not be read.");
            return;
        }

        var all = new List<DisplayMode>();
        for (var index = 0u; Query(index, out var mode); index++)
        {
            all.Add(mode);
        }

        var offerable = Offerable(all, now);
        modes = [.. offerable];
        rates = [.. offerable.Select(mode => (int)mode.DisplayFrequency)];

        // -1 when the live mode is one this list excludes: interlaced, or the 0/1 placeholder frequency.
        current = Array.IndexOf(rates, (int)now.DisplayFrequency);
    }

    /// <summary>Switches the primary display to Rates[index]. False when the index is out of range or Windows refused.</summary>
    public bool Set(int index)
    {
        // One read, then work from the snapshot. Refresh() runs on the session thread and empties both
        // arrays before refilling them, so reading the field twice could see the empty one between a guard
        // that passed and the index that used it — a managed bounds check, but an unhandled one on a
        // background thread, which ends the process rather than the dial.
        var offered = modes;
        if ((uint)index >= (uint)offered.Length)
        {
            return false;
        }

        var mode = offered[index];

        // Already there. Without this a phone alternating two indices would switch modes for ever, and each
        // switch blanks the panel for about a second: the desktop becomes unusable and the tray hard to reach.
        if (index == current)
        {
            return true;
        }

        int result;
        try
        {
            // Flags 0 changes the mode for this session only. CDS_UPDATEREGISTRY would outlive it, and a
            // desktop that boots at whatever a dial was last flicked to is not what anyone asked for.
            result = ChangeDisplaySettingsExW(null, ref mode, 0, 0, 0);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            LogOnce($"Display refresh rate could not be changed: {e.Message}");
            return false;
        }

        if (result != ChangeSuccessful)
        {
            // Read from the snapshot: the other array is only kept in step with this one by convention,
            // and it was never what the bounds check above was measured against.
            LogOnce($"Display refused {mode.DisplayFrequency} Hz: ChangeDisplaySettingsEx returned {result}.");
            return false;
        }

        current = index;
        return true;
    }

    /// <summary>
    /// Queues a switch instead of making one. A mode change blanks the panel for the best part of a second,
    /// and it is asked for by a dial the finger is still sliding, so the receive thread must not wait on one
    /// and the indices passed on the way to the wanted one must not each cost a blank. Latest value wins,
    /// exactly as brightness does: while a switch is in flight, newer requests overwrite the waiting one
    /// rather than queueing behind it, so a drag across four rates settles on the fourth after at most two.
    /// </summary>
    public void SetLatest(int index)
    {
        Interlocked.Exchange(ref pending, index);
        wake.Set();
    }

    public void Dispose()
    {
        disposed = true;
        wake.Set();
        // The worker only ever waits on the handle, so it leaves promptly; a switch already in flight is
        // inside a Win32 call that has to finish either way.
        worker.Join(TimeSpan.FromSeconds(2));
        wake.Dispose();
    }

    private void Run()
    {
        while (!disposed)
        {
            wake.WaitOne();
            var index = Interlocked.Exchange(ref pending, None);
            if (disposed || index == None)
            {
                continue;
            }

            try
            {
                Set(index);
            }
            catch (Exception e)
            {
                // Last line before the process. This is a background worker in a tray app, and an escape
                // from here would take the tray, the link and the user's session with it.
                Log.Write($"Display mode change failed: {e}");
            }
        }
    }

    /// <summary>
    /// The modes worth putting on the dial out of <paramref name="all"/>, ascending by rate: same resolution
    /// and colour depth as <paramref name="now"/>, progressive, one entry per rate. Split out from the
    /// enumeration so the filtering can be tested — CI has no panel whose real mode list means anything.
    /// </summary>
    internal static List<DisplayMode> Offerable(IEnumerable<DisplayMode> all, DisplayMode now)
    {
        var seen = new HashSet<uint>();
        var offerable = new List<DisplayMode>();
        foreach (var mode in all)
        {
            if (mode.PelsWidth != now.PelsWidth || mode.PelsHeight != now.PelsHeight || mode.BitsPerPel != now.BitsPerPel)
            {
                continue;
            }

            // dmDisplayFrequency 0 and 1 both mean "the hardware's default rate" — placeholders, not rates.
            if (mode.DisplayFrequency < 2 || (mode.DisplayFlags & Interlaced) != 0 || !seen.Add(mode.DisplayFrequency))
            {
                continue;
            }

            offerable.Add(mode);
        }

        offerable.Sort((left, right) => left.DisplayFrequency.CompareTo(right.DisplayFrequency));
        return offerable;
    }

    private static DisplayMode Empty() => new() { Size = (ushort)Marshal.SizeOf<DisplayMode>() };

    /// <summary>One mode from the driver's list, or the live one for <see cref="CurrentSettings"/>. False past the end.</summary>
    private bool Query(uint index, out DisplayMode mode)
    {
        mode = Empty();
        try
        {
            return EnumDisplaySettingsExW(null, index, ref mode, 0);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            LogOnce($"Display modes could not be enumerated: {e.Message}");
            return false;
        }
    }

    private void LogOnce(string message)
    {
        if (logged)
        {
            return;
        }

        logged = true;
        Log.Write(message);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplaySettingsExW(string? deviceName, uint modeIndex, ref DisplayMode mode, uint flags);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int ChangeDisplaySettingsExW(string? deviceName, ref DisplayMode mode, nint window, uint flags, nint param);
}

/// <summary>
/// DEVMODEW. The two WCHAR[32] names are declared as ushort buffers and never read: nothing here needs them,
/// but Win32 writes through the whole 220-byte struct and every field after them sits at the offset they push
/// it to. A test pins that 220 so a mistyped field cannot silently shift the rest.
///
/// The printer half of the union (orientation, paper size, copies) is spelled here as the display half
/// (position, orientation, fixed output); both branches are the same sixteen bytes and only the display one
/// is ever meaningful for a screen.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DisplayMode
{
    public fixed ushort DeviceName[32];
    public ushort SpecVersion;
    public ushort DriverVersion;
    public ushort Size;
    public ushort DriverExtra;
    public uint Fields;
    public int PositionX;
    public int PositionY;
    public uint DisplayOrientation;
    public uint DisplayFixedOutput;
    public short Color;
    public short Duplex;
    public short YResolution;
    public short TtOption;
    public short Collate;
    public fixed ushort FormName[32];
    public ushort LogPixels;
    public uint BitsPerPel;
    public uint PelsWidth;
    public uint PelsHeight;
    public uint DisplayFlags;
    public uint DisplayFrequency;
    public uint IcmMethod;
    public uint IcmIntent;
    public uint MediaType;
    public uint DitherType;
    public uint Reserved1;
    public uint Reserved2;
    public uint PanningWidth;
    public uint PanningHeight;
}
