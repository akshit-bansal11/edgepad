using Microsoft.Win32;

namespace Edgepad.Startup;

/// <summary>The per-user Run key: starts the tray app at sign-in, with no admin rights and no service.</summary>
internal static class RunAtLogin
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Edgepad";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
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
}
