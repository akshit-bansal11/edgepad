using Microsoft.Win32;

namespace Edgepad.Startup;

/// <summary>
/// The per-user Run key: starts the tray app at sign-in, with no admin rights and no service.
///
/// Both members are reached from the tray menu — one as it opens, one from a click — where an unhandled
/// exception ends the process. The registry is reliable enough that neither has been seen to fail, and
/// that is exactly why neither should be the thing that takes the app down when policy, a corrupt hive or
/// a locked key finally makes one of them throw.
/// </summary>
internal static class RunAtLogin
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Edgepad";

    /// <summary>Whether this app starts at sign-in. False when the key cannot be read.</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
                return key?.GetValue(ValueName) is string;
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                Log.Write($"Start with Windows could not be read: {e.Message}");
                return false;
            }
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (enabled)
            {
                var exe = Environment.ProcessPath ?? throw new InvalidOperationException("The running executable has no path");
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException
            or IOException or InvalidOperationException)
        {
            // The tick in the menu will disagree with the registry until it is opened again, which reads
            // the real state back. A wrong tick is a better outcome than a tray app that vanished.
            Log.Write($"Start with Windows could not be set to {enabled}: {e.Message}");
        }
    }
}
