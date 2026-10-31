using Tmds.DBus.Protocol;

namespace Edgepad.Platform;

/// <summary>
/// The desktop's light or dark preference, from the freedesktop Settings portal: <c>org.freedesktop.appearance</c>
/// <c>color-scheme</c> is 0 for no preference, 1 for dark and 2 for light. Read on a poll rather than watched, as
/// <see cref="SessionWatch"/> does, so no signal API is relied on. No portal, an error or 0 all answer light.
/// </summary>
internal static class ColorScheme
{
    public static bool PrefersDark(Bus bus) => (Read(bus, "ReadOne") ?? Read(bus, "Read")) == 1;

    // ReadOne (portal version 2) answers a variant holding the value; the older Read wraps it in a second variant,
    // which the reader unwraps (UNKNOWN: not seen on a real portal). A portal without ReadOne answers an error, so null.
    private static uint? Read(Bus bus, string member)
    {
        var read = bus.Call(
            c =>
            {
                using var writer = c.GetMessageWriter();
                writer.WriteMethodCallHeader(
                    destination: "org.freedesktop.portal.Desktop",
                    path: "/org/freedesktop/portal/desktop",
                    @interface: "org.freedesktop.portal.Settings",
                    member: member,
                    signature: "ss");
                writer.WriteString("org.freedesktop.appearance");
                writer.WriteString("color-scheme");
                return writer.CreateMessage();
            },
            (Message reply, object? _) => reply.GetBodyReader().ReadVariantValue(),
            out var value);

        return read && value is { Type: VariantValueType.UInt32 } v ? v.GetUInt32() : null;
    }
}
