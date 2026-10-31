using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Edgepad.Controls;
using Edgepad.Injection;
using Edgepad.Protocol;
using Edgepad.Trust;

// Not Edgepad.Clipboard: a namespace of that name would hide WinForms' Clipboard from everything under Edgepad.
namespace Edgepad.Blob;

/// <summary>
/// Windows' clipboard. One for the app, made on the UI thread, which is the STA that owns every clipboard call: a
/// session's threads come here and wait, on a thread of their own, never on the read loop. A clipboard another app
/// holds is tried again from the caller's side, so the tray is not held up while it waits, and after that the copy is
/// lost and nothing else is. The owner's switch is checked again on the UI thread, where it changes, so nothing is read
/// or written once it is off.
/// </summary>
internal sealed class WindowsClipboard : IClipboardSurface, IDisposable
{
    private const int Attempts = 3;
    private const int RetryMs = 50;

    private const uint UnicodeText = 13;
    private const uint Dib = 8;

    /// <summary>The registered format apps put a PNG in, transparency and all.</summary>
    private const string PngFormat = "PNG";

    /// <summary>A UTF-16 text this long is over the UTF-8 cap whatever it says. The rest allows for the terminator.</summary>
    private const long MaxUtf16Bytes = (2 * ClipboardSync.MaxTextBytes) + 16;

    private readonly Lock gate = new();
    private readonly SynchronizationContext ui;
    private readonly Func<bool> enabled;
    private readonly Listener? listener;

    private Action? changed;

    /// <summary>The clipboard's stamp after this app's own write, so the change it causes is not taken for a copy.</summary>
    private uint written;

    /// <param name="ui">The UI thread's context. The constructor runs on that thread.</param>
    /// <param name="enabled">The owner's switch.</param>
    public WindowsClipboard(SynchronizationContext ui, Func<bool> enabled)
    {
        this.ui = ui;
        this.enabled = enabled;
        listener = Listen();
    }

    /// <summary>Calls <paramref name="onChange"/> on the UI thread when this clipboard changes, until disposed.</summary>
    public IDisposable Watch(Action onChange)
    {
        lock (gate)
        {
            changed += onChange;
        }

        return new Subscription(() =>
        {
            lock (gate)
            {
                changed -= onChange;
            }
        });
    }

    public ClipboardContent? Read()
    {
        ClipboardContent? content = null;
        OnUi(() => content = ReadHere());
        return content;
    }

    public void Write(ClipboardContent content)
    {
        // PNG keeps the transparency for the apps that read it; the bitmap is for the many that only take that.
        // Decoded here, so a large image costs this thread and not the tray.
        using var stream = new MemoryStream(content.Data);
        using var image = content.Kind == BlobKind.ClipImage ? new Bitmap(stream) : null;
        using var png = new MemoryStream(content.Data);
        var data = new DataObject();
        if (image is null)
        {
            data.SetText(Encoding.UTF8.GetString(content.Data), TextDataFormat.UnicodeText);
        }
        else
        {
            data.SetData(PngFormat, false, png);
            data.SetImage(image);
        }

        OnUi(() =>
        {
            if (enabled())
            {
                // No retries of its own: they would sleep on the UI thread.
                Clipboard.SetDataObject(data, true, 0, 0);
                written = NativeMethods.GetClipboardSequenceNumber();
            }
        });
    }

    /// <summary>On the UI thread, like everything else that touches the window.</summary>
    public void Dispose()
    {
        if (listener is not null)
        {
            _ = NativeMethods.RemoveClipboardFormatListener(listener.Handle);
            listener.DestroyHandle();
        }
    }

    /// <summary>The bitmap as a PNG, for a copy that came with no PNG of its own.</summary>
    internal static byte[] Png(Image image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>CF_UNICODETEXT as the wire's UTF-8. The block is NUL-terminated and may be longer than the text.</summary>
    internal static byte[] Utf8(byte[] utf16)
    {
        var text = Encoding.Unicode.GetString(utf16);
        var end = text.IndexOf('\0', StringComparison.Ordinal);
        return Encoding.UTF8.GetBytes(end < 0 ? text : text[..end]);
    }

    /// <summary>Without this window only laptop to phone stops: the phone's copies are still written.</summary>
    private Listener? Listen()
    {
        try
        {
            var window = new Listener(Notify);
            window.CreateHandle(new CreateParams { Parent = NativeMethods.MessageOnlyParent });
            if (!NativeMethods.AddClipboardFormatListener(window.Handle))
            {
                Log.Write($"Clipboard changes cannot be watched: error {Marshal.GetLastPInvokeError()}");
            }

            return window;
        }
        catch (Win32Exception e)
        {
            Log.Write($"Clipboard changes cannot be watched: {e.Message}");
            return null;
        }
    }

    private void Notify()
    {
        if (NativeMethods.GetClipboardSequenceNumber() != written)
        {
            Volatile.Read(ref changed)?.Invoke();
        }
    }

    /// <summary>Runs <paramref name="work"/> on the UI thread, again if the clipboard is held. The waiting is on this thread.</summary>
    private void OnUi(Action work)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            var held = false;
            ui.Send(
                _ =>
                {
                    try
                    {
                        work();
                    }
                    catch (ExternalException)
                    {
                        held = true;
                    }
                },
                null);
            if (!held)
            {
                return;
            }

            Thread.Sleep(RetryMs);
        }

        Log.Write("The clipboard stayed held by another app, so one copy was not shared");
    }

    /// <summary>What can be shared, on the UI thread. Text wins: a spreadsheet's cells also come as a picture of them.</summary>
    private ClipboardContent? ReadHere()
    {
        if (!enabled())
        {
            return null;
        }

        var stamp = NativeMethods.GetClipboardSequenceNumber();
        if (!NativeMethods.OpenClipboard(listener?.Handle ?? 0))
        {
            throw new Win32Exception();
        }

        try
        {
            if (ClipboardFlags.IsSensitive(OfferedFlags()))
            {
                return null;
            }

            if (NativeMethods.IsClipboardFormatAvailable(UnicodeText))
            {
                return Bytes(UnicodeText, MaxUtf16Bytes) is { } text ? new ClipboardContent(BlobKind.ClipText, Utf8(text)) : null;
            }

            var png = NativeMethods.RegisterClipboardFormatW(PngFormat);
            if (png != 0 && NativeMethods.IsClipboardFormatAvailable(png) && Bytes(png, ClipboardSync.MaxImageBytes) is { } image)
            {
                return new ClipboardContent(BlobKind.ClipImage, image);
            }

            if (!NativeMethods.IsClipboardFormatAvailable(Dib))
            {
                return null;
            }
        }
        finally
        {
            _ = NativeMethods.CloseClipboard();
        }

        return Encoded(stamp);
    }

    /// <summary>
    /// The clipboard's bitmap as a PNG, read through WinForms, which opens the clipboard itself. Thrown away if a new
    /// copy came in since the flags were read, because those flags were not this copy's; its own change event follows.
    /// </summary>
    // ponytail: encodes on the UI thread, a hitch only on a large copy with no PNG. Hand the image out if it shows.
    private static ClipboardContent? Encoded(uint stamp)
    {
        using var image = Clipboard.GetImage();
        if (image is null || (ulong)image.Width * (ulong)image.Height > ClipboardSync.MaxImagePixels)
        {
            return null;
        }

        return NativeMethods.GetClipboardSequenceNumber() == stamp ? new ClipboardContent(BlobKind.ClipImage, Png(image)) : null;
    }

    /// <summary>Each flag format on offer, with its DWORD. Inside OpenClipboard. A format with no id reads as unreadable.</summary>
    private static Dictionary<string, uint?> OfferedFlags()
    {
        var flags = new Dictionary<string, uint?>();
        foreach (var name in ClipboardFlags.Names)
        {
            var format = NativeMethods.RegisterClipboardFormatW(name);
            if (format == 0)
            {
                flags[name] = null;
            }
            else if (NativeMethods.IsClipboardFormatAvailable(format))
            {
                flags[name] = Bytes(format, ClipboardFlags.MaxFlagBytes) is { Length: >= sizeof(uint) } value ? BitConverter.ToUInt32(value) : null;
            }
        }

        return flags;
    }

    /// <summary>A format's bytes, or null for one that is missing, empty or over <paramref name="cap"/>. Inside OpenClipboard.</summary>
    private static byte[]? Bytes(uint format, long cap)
    {
        var block = NativeMethods.GetClipboardData(format);
        if (block == 0)
        {
            return null;
        }

        var size = (long)NativeMethods.GlobalSize(block);
        if (size == 0 || size > cap)
        {
            return null;
        }

        var memory = NativeMethods.GlobalLock(block);
        if (memory == 0)
        {
            return null;
        }

        try
        {
            var bytes = new byte[size];
            Marshal.Copy(memory, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            _ = NativeMethods.GlobalUnlock(block);
        }
    }

    /// <summary>A window with no surface, there to receive WM_CLIPBOARDUPDATE.</summary>
    private sealed class Listener(Action changed) : NativeWindow
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WmClipboardUpdate)
            {
                changed();
            }

            base.WndProc(ref m);
        }
    }
}

/// <summary>
/// The formats an app sets to say a copy is not for us: password managers set them. Their absence proves nothing about
/// the content, and their presence with a value that cannot be read counts as set.
/// </summary>
internal static class ClipboardFlags
{
    /// <summary>Present is enough.</summary>
    public const string ExcludeFromMonitors = "ExcludeClipboardContentFromMonitorProcessing";

    /// <summary>The older way to say the same, which password managers still set beside it.</summary>
    public const string ViewerIgnore = "Clipboard Viewer Ignore";

    /// <summary>A DWORD: 0 keeps the copy out of Clipboard history.</summary>
    public const string History = "CanIncludeInClipboardHistory";

    /// <summary>A DWORD: 0 keeps the copy out of the cloud clipboard.</summary>
    public const string Cloud = "CanUploadToCloudClipboard";

    /// <summary>More than a DWORD needs, for the allocator's rounding.</summary>
    public const long MaxFlagBytes = 64;

    public static readonly string[] Names = [ExcludeFromMonitors, ViewerIgnore, History, Cloud];

    /// <summary>
    /// Whether a copy offering <paramref name="flags"/> must stay on this laptop. The keys are the formats on offer, the
    /// values their DWORDs, and null one that could not be read.
    /// </summary>
    public static bool IsSensitive(IReadOnlyDictionary<string, uint?> flags) =>
        flags.ContainsKey(ExcludeFromMonitors) || flags.ContainsKey(ViewerIgnore) || Forbids(flags, History) || Forbids(flags, Cloud);

    private static bool Forbids(IReadOnlyDictionary<string, uint?> flags, string name) =>
        flags.TryGetValue(name, out var value) && value is null or 0;
}

/// <summary>
/// <c>clipboard.txt</c>: the tray's Share clipboard switch, <c>enabled=0|1</c>. No file is on, the owner's chosen
/// default. A file that cannot be read, or holds anything Edgepad would not have written, is off, because this switch
/// lets clipboard content leave the laptop. Read once and kept: the sync asks for every frame of a transfer.
/// </summary>
internal sealed class ClipboardSetting(string path)
{
    private const string Off = "enabled=0";
    private const string On = "enabled=1";

    private volatile bool enabled = Load(path);

    public bool Enabled => enabled;

    public static ClipboardSetting ForCurrentUser(string directory) => new(Path.Combine(directory, "clipboard.txt"));

    /// <summary>
    /// Takes effect at once whatever the disk says, and false, logged, means the next launch will not remember it.
    /// </summary>
    public bool Set(bool on)
    {
        enabled = on;
        try
        {
            PrivateStorage.Replace(path, temp => File.WriteAllText(temp, (on ? On : Off) + "\n"));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The clipboard setting could not be saved to {path}: {e.Message}");
            return false;
        }
    }

    private static bool Load(string file)
    {
        string text;
        try
        {
            text = File.ReadAllText(file).Trim();
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The clipboard setting could not be read from {file}, so the clipboard is not shared: {e.Message}");
            return false;
        }

        if (text is not (On or Off))
        {
            Log.Write($"The clipboard setting in {file} holds something Edgepad did not write, so the clipboard is not shared");
        }

        return text == On;
    }
}
