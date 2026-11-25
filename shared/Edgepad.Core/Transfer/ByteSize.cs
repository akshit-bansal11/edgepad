using System.Globalization;

namespace Edgepad.Transfer;

/// <summary>A byte count as the transfer panels show it.</summary>
internal static class ByteSize
{
    /// <summary>1024-based units: "0 B", "1023 B", "1.0 KB", "12.4 MB". The number follows the current culture.</summary>
    public static string Format(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{bytes} B")
            : string.Create(CultureInfo.CurrentCulture, $"{value:0.0} {units[unit]}");
    }
}
