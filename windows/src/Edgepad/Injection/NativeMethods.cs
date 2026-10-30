using System.Runtime.InteropServices;

namespace Edgepad.Injection;

/// <summary>
/// The Win32 surface: SendInput and LockWorkStation for input, the few GDI calls the annotation layer needs to
/// draw with per-pixel alpha, the clipboard calls WinForms has no way to make, and the structs they take.
/// </summary>
internal static partial class NativeMethods
{
    public const uint InputMouse = 0;
    public const uint InputKeyboard = 1;

    public const uint MouseMove = 0x0001;
    public const uint MouseLeftDown = 0x0002;
    public const uint MouseLeftUp = 0x0004;
    public const uint MouseRightDown = 0x0008;
    public const uint MouseRightUp = 0x0010;
    public const uint MouseMiddleDown = 0x0020;
    public const uint MouseMiddleUp = 0x0040;
    public const uint MouseWheel = 0x0800;
    public const uint MouseHorizontalWheel = 0x1000;

    public const uint KeyExtended = 0x0001;
    public const uint KeyUp = 0x0002;
    public const uint KeyUnicode = 0x0004;

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint SendInput(uint inputCount, [In] NativeInput[] inputs, int size);

    /// <summary>MAPVK_VK_TO_VSC: the hardware scan code a virtual-key produces, so apps reading scan codes see one too.</summary>
    [LibraryImport("user32.dll")]
    public static partial uint MapVirtualKeyW(uint code, uint mapType);

    /// <summary>Win+L cannot be injected — Windows ignores it from SendInput — so locking has its own call.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LockWorkStation();

    /// <summary>A memory DC compatible with the screen, which is what a null DC asks for.</summary>
    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint dc);

    /// <summary>
    /// A bitmap whose pixels are ordinary memory, so GDI+ can draw into them directly and UpdateLayeredWindow can
    /// read them with no copy in between. A 32 bpp BI_RGB bitmap has no colour table, so the header alone is the
    /// whole BITMAPINFO.
    /// </summary>
    [LibraryImport("gdi32.dll")]
    public static partial nint CreateDIBSection(nint dc, in BitmapInfoHeader info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint dc, nint gdiObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint gdiObject);

    /// <summary>Puts a premultiplied 32 bpp bitmap on a layered window, with alpha per pixel.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateLayeredWindow(
        nint window,
        nint screenDc,
        in NativePoint position,
        in NativeSize size,
        nint sourceDc,
        in NativePoint sourceOrigin,
        uint colourKey,
        in BlendFunction blend,
        uint flags);

    /// <summary>WM_CLIPBOARDUPDATE, posted to a window that asked with <see cref="AddClipboardFormatListener"/>.</summary>
    public const int WmClipboardUpdate = 0x031D;

    /// <summary>HWND_MESSAGE: the parent that makes a window message-only, with no surface and no place on any desktop.</summary>
    public const nint MessageOnlyParent = -3;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AddClipboardFormatListener(nint window);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RemoveClipboardFormatListener(nint window);

    /// <summary>Changes whenever the clipboard does; 0 when this session cannot see it.</summary>
    [LibraryImport("user32.dll")]
    public static partial uint GetClipboardSequenceNumber();

    /// <summary>Fails while another window has the clipboard open. Every open is paired with <see cref="CloseClipboard"/>.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsClipboardFormatAvailable(uint format);

    /// <summary>A named format's id, the same for every process; 0 on failure.</summary>
    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterClipboardFormatW(string name);

    /// <summary>The clipboard's own block for a format, which it keeps. Only between open and close.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint GetClipboardData(uint format);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nuint GlobalSize(nint block);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint GlobalLock(nint block);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(nint block);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativePoint(int x, int y)
{
    public readonly int X = x;
    public readonly int Y = y;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeSize(int width, int height)
{
    public readonly int Width = width;
    public readonly int Height = height;
}

/// <summary>BLENDFUNCTION. Its flags byte is reserved and must stay zero, so it is not a parameter.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct BlendFunction
{
    public readonly byte Operation;
    public readonly byte Flags;
    public readonly byte ConstantAlpha;
    public readonly byte AlphaFormat;

    public BlendFunction(byte operation, byte constantAlpha, byte alphaFormat)
    {
        Operation = operation;
        Flags = 0;
        ConstantAlpha = constantAlpha;
        AlphaFormat = alphaFormat;
    }
}

/// <summary>
/// BITMAPINFOHEADER for an uncompressed 32 bpp bitmap; a negative height is a top-down one. Every field is laid
/// out, used or not, because Windows reads all 40 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct BitmapInfoHeader
{
    public readonly uint Size;
    public readonly int Width;
    public readonly int Height;
    public readonly ushort Planes;
    public readonly ushort BitCount;
    public readonly uint Compression;
    public readonly uint SizeImage;
    public readonly int XPelsPerMeter;
    public readonly int YPelsPerMeter;
    public readonly uint ColoursUsed;
    public readonly uint ColoursImportant;

    public BitmapInfoHeader(int width, int height)
    {
        Size = 40;
        Width = width;
        Height = height;
        Planes = 1;
        BitCount = 32;
        Compression = 0; // BI_RGB
        SizeImage = 0;
        XPelsPerMeter = 0;
        YPelsPerMeter = 0;
        ColoursUsed = 0;
        ColoursImportant = 0;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeInput
{
    public uint Type;
    public NativeInputData Data;
}

[StructLayout(LayoutKind.Explicit)]
internal struct NativeInputData
{
    [FieldOffset(0)]
    public MouseInput Mouse;

    [FieldOffset(0)]
    public KeyboardInput Keyboard;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MouseInput
{
    public int Dx;
    public int Dy;
    public int MouseData;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KeyboardInput
{
    public ushort VirtualKey;
    public ushort ScanCode;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}
