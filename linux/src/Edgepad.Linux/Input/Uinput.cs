using System.Runtime.InteropServices;
using System.Text;

namespace Edgepad.Input;

/// <summary>
/// One <c>struct input_event</c> as the x86_64 kernel reads it from /dev/uinput (linux/input.h): a 16-byte
/// <c>struct timeval</c>, then type u16, code u16 and value s32, 24 bytes in all. The time is left at zero, so it
/// has no fields here: uinput ignores it and the input core stamps each event itself.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal readonly struct InputEvent(ushort type, ushort code, int value)
{
    [FieldOffset(16)]
    public readonly ushort Type = type;

    [FieldOffset(18)]
    public readonly ushort Code = code;

    [FieldOffset(20)]
    public readonly int Value = value;
}

/// <summary><c>struct input_id</c> (linux/input.h): four u16s.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct InputId(ushort bus, ushort vendor, ushort product, ushort version)
{
    public readonly ushort Bus = bus;
    public readonly ushort Vendor = vendor;
    public readonly ushort Product = product;
    public readonly ushort Version = version;
}

/// <summary>
/// <c>struct uinput_setup</c> (linux/uinput.h): the id, a NUL-terminated name of at most UINPUT_MAX_NAME_SIZE (80)
/// bytes, and ff_effects_max u32, left at zero (no force feedback). 8 + 80 + 4 = 92 bytes, with no padding: every
/// field is already aligned.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 92)]
internal unsafe struct UinputSetup
{
    public const int NameSize = 80;

    [FieldOffset(0)]
    public InputId Id;

    [FieldOffset(8)]
    public fixed byte Name[NameSize];

    public static UinputSetup Create(InputId id, string name)
    {
        var setup = new UinputSetup { Id = id };
        // One byte short of the buffer, so the kernel always finds the terminating NUL.
        var bytes = Encoding.UTF8.GetBytes(name);
        bytes.AsSpan(0, Math.Min(bytes.Length, NameSize - 1)).CopyTo(new Span<byte>(setup.Name, NameSize));
        return setup;
    }
}

/// <summary>
/// <c>struct uinput_abs_setup</c> (linux/uinput.h): the axis code u16, then <c>struct input_absinfo</c>
/// (linux/input.h), six s32s: value, minimum, maximum, fuzz, flat, resolution. The absinfo's s32 alignment puts two
/// bytes of padding after the code (the header marks them <c>__u16 filler</c>), so it starts at 4: 4 + 24 = 28 bytes.
/// The value and the resolution are left at zero; a resolution means units per millimetre, which a stick lacks.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 28)]
internal readonly struct UinputAbsSetup(ushort code, int minimum, int maximum, int fuzz, int flat)
{
    [FieldOffset(0)]
    public readonly ushort Code = code;

    [FieldOffset(8)]
    public readonly int Minimum = minimum;

    [FieldOffset(12)]
    public readonly int Maximum = maximum;

    [FieldOffset(16)]
    public readonly int Fuzz = fuzz;

    [FieldOffset(20)]
    public readonly int Flat = flat;
}

/// <summary>
/// A virtual input device made through /dev/uinput: open, say which event types and codes it can send, set it up,
/// create it, then write events. Not thread-safe; its owner serialises calls. Disposing destroys the device, and
/// the kernel then lets go of any key it still showed as down.
/// </summary>
internal sealed partial class Uinput : IDisposable
{
    public const ushort EvSyn = 0x00;
    public const ushort EvKey = 0x01;
    public const ushort EvRel = 0x02;
    public const ushort EvAbs = 0x03;
    public const ushort SynReport = 0;

    public const ushort BusUsb = 0x03;
    public const ushort BusVirtual = 0x06;

    // ioctl numbers, from asm-generic/ioctl.h: dir << 30 | size << 16 | type << 8 | nr, with _IOC_NONE = 0 and
    // _IOC_WRITE = 1. UINPUT_IOCTL_BASE is 'U' = 0x55 (linux/uinput.h).
    //   UI_DEV_CREATE  = _IO('U', 1)                       = 0x5501
    //   UI_DEV_DESTROY = _IO('U', 2)                       = 0x5502
    //   UI_DEV_SETUP   = _IOW('U', 3, struct uinput_setup) = 1 << 30 | 92 (0x5C) << 16 | 0x55 << 8 | 3 = 0x405C5503
    //   UI_SET_EVBIT   = _IOW('U', 100, int)               = 1 << 30 | 4 << 16 | 0x55 << 8 | 100 (0x64) = 0x40045564
    //   UI_SET_KEYBIT  = _IOW('U', 101, int)               = ... | 101 (0x65)                           = 0x40045565
    //   UI_SET_RELBIT  = _IOW('U', 102, int)               = ... | 102 (0x66)                           = 0x40045566
    //   UI_SET_ABSBIT  = _IOW('U', 103, int)               = ... | 103 (0x67)                           = 0x40045567
    //   UI_ABS_SETUP   = _IOW('U', 4, struct uinput_abs_setup) = 1 << 30 | 28 (0x1C) << 16 | 0x55 << 8 | 4 = 0x401C5504
    public const uint DevCreate = 0x5501;
    public const uint DevDestroy = 0x5502;
    public const uint DevSetup = 0x405C5503;
    public const uint SetEvBit = 0x40045564;
    public const uint SetKeyBit = 0x40045565;
    public const uint SetRelBit = 0x40045566;
    public const uint SetAbsBit = 0x40045567;
    public const uint AbsSetup = 0x401C5504;

    public const string DevicePath = "/dev/uinput";

    private const string Library = "libc.so.6";

    // x86_64 values from fcntl.h. Close-on-exec as well, so the clipboard tools Typing launches never inherit it.
    private const int WriteOnly = 0x1;
    private const int NonBlocking = 0x800;
    private const int CloseOnExec = 0x80000;

    private int fd;

    private Uinput(int fd) => this.fd = fd;

    /// <summary>The opened device node, or null with the reason (most often EACCES: no udev rule; ENOENT: no module).</summary>
    public static Uinput? Open(out string error) => Open(DevicePath, out error);

    /// <summary>
    /// The same for another node. Only tests pass one: a path that does not exist is a laptop without uinput, and
    /// /dev/null is a node that opens and then refuses every ioctl, which is a device that cannot be created.
    /// </summary>
    public static Uinput? Open(string path, out string error)
    {
        var fd = OpenFile(path, WriteOnly | NonBlocking | CloseOnExec);
        if (fd < 0)
        {
            error = $"{path} could not be opened: {Marshal.GetLastPInvokeErrorMessage()}";
            return null;
        }

        error = "";
        return new Uinput(fd);
    }

    /// <summary>One UI_SET_*BIT call: <paramref name="request"/> is <see cref="SetEvBit"/>, <see cref="SetKeyBit"/>, <see cref="SetRelBit"/> or <see cref="SetAbsBit"/>.</summary>
    public bool Enable(uint request, int code) => Ioctl(fd, request, code) >= 0;

    /// <summary>UI_ABS_SETUP for one axis whose UI_SET_ABSBIT is already set; before <see cref="Create"/>.</summary>
    public unsafe bool SetUpAxis(ushort code, int minimum, int maximum, int fuzz, int flat)
    {
        var setup = new UinputAbsSetup(code, minimum, maximum, fuzz, flat);
        return IoctlPointer(fd, AbsSetup, &setup) >= 0;
    }

    /// <summary>UI_DEV_SETUP then UI_DEV_CREATE, once every bit is enabled.</summary>
    public unsafe bool Create(InputId id, string name)
    {
        var setup = UinputSetup.Create(id, name);
        return IoctlPointer(fd, DevSetup, &setup) >= 0 && IoctlPointer(fd, DevCreate, null) >= 0;
    }

    /// <summary>Writes the events in one write(2). False, with errno set, when the kernel took fewer bytes.</summary>
    public unsafe bool Write(ReadOnlySpan<InputEvent> events)
    {
        var bytes = MemoryMarshal.AsBytes(events);
        fixed (byte* buffer = bytes)
        {
            return WriteFile(fd, buffer, bytes.Length) == bytes.Length;
        }
    }

    public unsafe void Dispose()
    {
        if (fd < 0)
        {
            return;
        }

        // Fails harmlessly on a device that was never created; close(2) is what frees it either way.
        _ = IoctlPointer(fd, DevDestroy, null);
        _ = Close(fd);
        fd = -1;
    }

    // open and ioctl are variadic, and are called with their x86_64 SysV argument types directly, as Libc.Fcntl
    // is: for integer and pointer arguments that is the same call. size_t and ssize_t are nint on linux-x64.
    [LibraryImport(Library, EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenFile(string path, int flags);

    [LibraryImport(Library, EntryPoint = "ioctl", SetLastError = true)]
    private static partial int Ioctl(int fd, nuint request, int value);

    [LibraryImport(Library, EntryPoint = "ioctl", SetLastError = true)]
    private static unsafe partial int IoctlPointer(int fd, nuint request, void* argument);

    [LibraryImport(Library, EntryPoint = "write", SetLastError = true)]
    private static unsafe partial nint WriteFile(int fd, byte* buffer, nint count);

    [LibraryImport(Library, EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int fd);
}
