using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Edgepad.Macros;

/// <summary>The program a .lnk file points at, through the shell's own ShellLink object.</summary>
internal static unsafe partial class ShortcutTarget
{
    private const uint InProcessServer = 1;
    private const uint ReadOnly = 0;
    private const int MaxPath = 1024;

    private static readonly Guid ShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid ShellLinkInterface = new("000214F9-0000-0000-C000-000000000046");

    /// <summary>
    /// The target path, or null on any failure: a broken shortcut, or one with no path behind it, such as a Store
    /// app's. Null only means the macro is not followed, and this runs once a second, so nothing is logged.
    /// </summary>
    public static string? Resolve(string path)
    {
        try
        {
            return Read(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Read(string path)
    {
        if (CoCreateInstance(ShellLink, 0, InProcessServer, ShellLinkInterface, out var instance) < 0)
        {
            return null;
        }

        try
        {
            var link = new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(instance, CreateObjectFlags.None);
            try
            {
                if (((IPersistFile)link).Load(path, ReadOnly) < 0)
                {
                    return null;
                }

                // A raw UTF-16 buffer, as in BrowserTitle. GetPath answers S_FALSE when the link has no path.
                var text = stackalloc char[MaxPath];
                return ((IShellLinkW)link).GetPath(text, MaxPath, 0, 0) == 0 ? new string(text) : null;
            }
            finally
            {
                ((ComObject)link).FinalRelease();
            }
        }
        finally
        {
            _ = Marshal.Release(instance);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid type, nint outer, uint context, in Guid iid, out nint instance);
}

/// <summary>Only the start of the COM vtable: GetPath is its first method, and nothing here calls one after it.</summary>
[GeneratedComInterface]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal unsafe partial interface IShellLinkW
{
    [PreserveSig]
    int GetPath(char* file, int size, nint findData, uint flags);
}

/// <summary>Only the start of the COM vtable, as far as Load: the two before it hold their slots.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0000010B-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    [PreserveSig]
    int GetClassID(out Guid type);

    [PreserveSig]
    int IsDirty();

    [PreserveSig]
    int Load(string file, uint mode);
}
