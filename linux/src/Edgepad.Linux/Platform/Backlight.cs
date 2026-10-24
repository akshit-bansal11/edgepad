using System.Globalization;
using Tmds.DBus.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// The laptop panel's brightness. Reading is the kernel's own file, <c>/sys/class/backlight/NAME/brightness</c>
/// against <c>max_brightness</c>, which any user may read. Writing that file needs root, so the write goes through
/// systemd-logind instead: <c>Session.SetBrightness("backlight", NAME, value)</c> on the system bus, a method it
/// offers so that "unprivileged programs [can] access hardware settings in a controlled way" for the caller's own
/// session. A desktop PC or a monitor-only setup has no backlight device, and then every call answers false.
/// </summary>
internal sealed class Backlight(string root = "/sys/class/backlight") : IDisposable
{
    // logind resolves "auto" to the session of the process that sends the call (or, failing that, its user's
    // display session), which is exactly the session this app runs in.
    private const string SessionPath = "/org/freedesktop/login1/session/auto";

    private readonly Lazy<(string Name, long Max)?> device = new(() => Find(root));
    private Bus? bus;

    /// <summary>True when the machine has a panel backlight this app can see.</summary>
    public bool Available => device.Value is not null;

    /// <summary>
    /// Whole percent of <paramref name="max"/>, rounded to the nearest, clamped to 0..100. Null when the device
    /// reports no range (<paramref name="max"/> of 0 or less), so nothing divides by zero.
    /// </summary>
    public static int? Percent(long value, long max) =>
        max <= 0 ? null : (int)Math.Clamp((long)Math.Round(value * 100.0 / max, MidpointRounding.AwayFromZero), 0, 100);

    /// <summary>
    /// The raw value for <paramref name="percent"/> of <paramref name="max"/>. Never below 1 for a device that has a
    /// range: on several panels 0 is fully dark, and a dial dragged to the bottom must not leave a laptop with no
    /// picture and no way to see the dial that did it.
    /// </summary>
    public static long Raw(int percent, long max) =>
        max <= 0 ? 0 : Math.Max(1, (long)Math.Round(max * (double)Math.Clamp(percent, 0, 100) / 100, MidpointRounding.AwayFromZero));

    /// <summary>The current brightness as a percent, or null with no backlight or an unreadable one.</summary>
    public int? Read()
    {
        if (device.Value is not { } found)
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(Path.Combine(root, found.Name, "brightness"));
            return long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? Percent(value, found.Max)
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool Set(int percent)
    {
        if (device.Value is not { } found)
        {
            return false;
        }

        var value = (uint)Math.Min(Raw(percent, found.Max), found.Max);
        bus ??= Bus.OnSystem();
        return bus.Call(c =>
        {
            using var writer = c.GetMessageWriter();
            writer.WriteMethodCallHeader(
                destination: "org.freedesktop.login1",
                path: SessionPath,
                @interface: "org.freedesktop.login1.Session",
                member: "SetBrightness",
                signature: "ssu");
            writer.WriteString("backlight");
            writer.WriteString(found.Name);
            writer.WriteUInt32(value);
            return writer.CreateMessage();
        });
    }

    /// <summary>Moves by <paramref name="delta"/> percent from where the panel is now.</summary>
    public bool Step(int delta) => Read() is { } now && Set(Math.Clamp(now + delta, 0, 100));

    public void Dispose() => bus?.Dispose();

    /// <summary>The first device, by name, that reports a range; null when the folder is missing or empty.</summary>
    private static (string Name, long Max)? Find(string root)
    {
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
            {
                var text = File.ReadAllText(Path.Combine(directory, "max_brightness"));
                if (long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var max) && max > 0)
                {
                    return (Path.GetFileName(directory), max);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // No such folder is the usual case on a desktop PC; a device that cannot be read ends the search as no device.
        }

        return null;
    }
}
