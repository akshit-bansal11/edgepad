using System.Globalization;

namespace Edgepad.Platform;

/// <summary>A volume as the phone's dial shows it: 0..100, and whether the endpoint is muted.</summary>
internal readonly record struct Level(int Percent, bool Muted);

/// <summary>
/// The default speakers or the default microphone, driven through the sound server's command-line tool: wpctl
/// (PipeWire's WirePlumber, the default on current GNOME and KDE) first, pactl (PulseAudio, and PipeWire's
/// compatibility layer) when wpctl is not installed. The tools are the supported interface; no sound library is
/// linked. Every method blocks on a child process, so they run on the worker and poll threads, never the reader.
/// </summary>
internal sealed class Audio(bool microphone)
{
    private readonly string wpctlTarget = microphone ? "@DEFAULT_AUDIO_SOURCE@" : "@DEFAULT_AUDIO_SINK@";
    private readonly string pactlKind = microphone ? "source" : "sink";
    private readonly string pactlTarget = microphone ? "@DEFAULT_SOURCE@" : "@DEFAULT_SINK@";

    // Set once wpctl could not be started. ponytail: it stays set for the run (a timeout counts), so installing
    // wireplumber needs an app restart; re-probing on a schedule if that matters.
    private volatile bool pactlOnly;

    /// <summary>
    /// <c>Volume: 0.55</c> or <c>Volume: 0.55 [MUTED]</c>, wpctl's answer to get-volume. A volume above 100 % (wpctl
    /// allows up to 150 % by default) is reported as 100, the top of the phone's dial. Null for anything else.
    /// </summary>
    public static Level? ParseWpctl(string? output)
    {
        const string Prefix = "Volume:";
        var text = output?.Trim();
        if (text is null || !text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var fields = text[Prefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 0
            || !double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction)
            || !double.IsFinite(fraction)
            || fraction < 0)
        {
            return null;
        }

        return new Level(Percent(fraction * 100), fields.Contains("[MUTED]"));
    }

    /// <summary>
    /// pactl's two answers: <c>Volume: front-left: 36044 /  55% / -15.18 dB, front-right: ...</c> from
    /// get-sink-volume (the first channel's percentage is taken) and <c>Mute: yes</c> from get-sink-mute. Null when
    /// the volume line holds no percentage; a mute line that cannot be read counts as not muted.
    /// </summary>
    public static Level? ParsePactl(string? volume, string? mute)
    {
        if (volume is null)
        {
            return null;
        }

        var sign = volume.IndexOf('%', StringComparison.Ordinal);
        if (sign <= 0)
        {
            return null;
        }

        var start = sign;
        while (start > 0 && char.IsAsciiDigit(volume[start - 1]))
        {
            start--;
        }

        if (start == sign || !int.TryParse(volume.AsSpan(start, sign - start), NumberStyles.None, CultureInfo.InvariantCulture, out var percent))
        {
            return null;
        }

        var muted = mute?.Trim().Equals("Mute: yes", StringComparison.OrdinalIgnoreCase) == true;
        return new Level(Math.Clamp(percent, 0, 100), muted);
    }

    /// <summary>The current level, or null when there is no such endpoint (no microphone) or no tool to ask.</summary>
    public Level? Read()
    {
        if (!pactlOnly)
        {
            var result = Proc.Run("wpctl", ["get-volume", wpctlTarget]);
            if (result is not null)
            {
                return result.ExitCode == 0 ? ParseWpctl(result.Output) : null;
            }

            pactlOnly = true;
        }

        var volume = Proc.Run("pactl", [$"get-{pactlKind}-volume", pactlTarget]);
        if (volume is not { ExitCode: 0 })
        {
            return null;
        }

        var mute = Proc.Run("pactl", [$"get-{pactlKind}-mute", pactlTarget]);
        return ParsePactl(volume.Output, mute?.Output);
    }

    public bool SetLevel(int percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        return Tool(
            ["set-volume", wpctlTarget, (clamped / 100.0).ToString("0.00", CultureInfo.InvariantCulture)],
            [$"set-{pactlKind}-volume", pactlTarget, $"{clamped.ToString(CultureInfo.InvariantCulture)}%"]);
    }

    public bool ToggleMute() => Tool(["set-mute", wpctlTarget, "toggle"], [$"set-{pactlKind}-mute", pactlTarget, "toggle"]);

    /// <summary>Runs the wpctl form, or the pactl one when wpctl is not there. True when the tool reported success.</summary>
    private bool Tool(string[] wpctl, string[] pactl)
    {
        if (!pactlOnly)
        {
            var result = Proc.Run("wpctl", wpctl);
            if (result is not null)
            {
                return result.ExitCode == 0;
            }

            pactlOnly = true;
        }

        return Proc.Run("pactl", pactl) is { ExitCode: 0 };
    }

    private static int Percent(double value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 100);
}
