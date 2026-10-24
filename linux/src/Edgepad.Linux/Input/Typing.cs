using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Edgepad.Input;

/// <summary>One step of typing: a key, with Shift held around it or not, or a run of text to paste.</summary>
internal readonly record struct TypingStep(ushort Key, bool Shift, string? Paste)
{
    public static TypingStep Press(ushort key, bool shift = false) => new(key, shift, null);

    public static TypingStep PasteText(string text) => new(0, false, text);
}

/// <summary>A clipboard tool: the program and its arguments; the text goes in on its standard input.</summary>
internal sealed record ClipboardTool(string Program, IReadOnlyList<string> Arguments);

/// <summary>
/// How TEXT 3 becomes input. A virtual keyboard sends key positions, never characters, and the desktop's layout
/// turns them into characters; so a key is only the right one when the layout is known to be the US one this table
/// is written for. On any other layout, a layout that cannot be read, or for any character outside ASCII, the text
/// goes through the clipboard and a Ctrl+V instead, which is right whatever the layout.
/// </summary>
internal static class Typing
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The keys and pastes for <paramref name="text"/>, in order. Backspace, Enter and Tab are always keys (no layout
    /// moves them); everything else is a key only on a US layout, and runs of what is not are joined into one paste.
    /// </summary>
    public static List<TypingStep> Plan(string text, bool usLayout)
    {
        var steps = new List<TypingStep>();
        var paste = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            var step = rune.Value switch
            {
                '\b' => TypingStep.Press(KeyMap.Backspace),
                '\n' => TypingStep.Press(KeyMap.Enter),
                '\t' => TypingStep.Press(KeyMap.Tab),
                _ when usLayout && rune.IsAscii => UsKey((char)rune.Value),
                _ => (TypingStep?)null,
            };

            if (step is null)
            {
                paste.Append(rune.ToString());
                continue;
            }

            Flush();
            steps.Add(step.Value);
        }

        Flush();
        return steps;

        void Flush()
        {
            if (paste.Length > 0)
            {
                steps.Add(TypingStep.PasteText(paste.ToString()));
                paste.Clear();
            }
        }
    }

    /// <summary>
    /// The tool that can set the clipboard in this session: wl-copy on Wayland (WAYLAND_DISPLAY set), else xclip on
    /// X11 (DISPLAY set), else none. Both are optional packages (wl-clipboard, xclip), documented in docs/LINUX.md.
    /// </summary>
    public static ClipboardTool? ClipboardFor(string? waylandDisplay, string? display) =>
        !string.IsNullOrEmpty(waylandDisplay) ? new ClipboardTool("wl-copy", [])
        : !string.IsNullOrEmpty(display) ? new ClipboardTool("xclip", ["-selection", "clipboard"])
        : null;

    /// <summary>
    /// Whether <c>localectl status</c> output names exactly the plain US layout: one layout, <c>us</c>, no variant.
    /// A variant (dvorak, intl) moves keys or makes dead keys, and a list (<c>us,de</c>) does not say which one is
    /// active, so both count as not US. So does output with no X11 layout line at all.
    /// </summary>
    public static bool IsUsLayout(string localectlStatus)
    {
        string? layout = null;
        string? variant = null;
        foreach (var line in localectlStatus.Split('\n'))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                continue;
            }

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (name == "X11 Layout")
            {
                layout = value;
            }
            else if (name == "X11 Variant")
            {
                variant = value;
            }
        }

        return layout == "us" && string.IsNullOrEmpty(variant);
    }

    /// <summary>
    /// Asks systemd-localed, through <c>localectl status</c>, for the X11 keyboard layout; false when that cannot be
    /// told. localed is the one documented place both GNOME and KDE (and the installers) write the layout to, and it
    /// answers the same on Wayland and on X11, where <c>setxkbmap -query</c> under XWayland reports a layout the
    /// compositor may not be using. Its limit: it is the system default, not a layout a user picked for their own
    /// session in GNOME's or KDE's settings. Getting it wrong costs more one way than the other: a non-US layout
    /// typed as US gives wrong characters, while a US layout pasted only costs the clipboard, so anything unsure is
    /// not US.
    /// </summary>
    // ponytail: system layout only, read once per run; reading GNOME's input-sources or KDE's kxkbrc too would see
    // a per-user layout, if testers report one being missed.
    public static bool DetectUsLayout()
    {
        try
        {
            var start = new ProcessStartInfo("localectl") { UseShellExecute = false, RedirectStandardOutput = true };
            start.ArgumentList.Add("status");
            // The field names parsed above are the untranslated ones.
            start.Environment["LC_ALL"] = "C";
            using var process = Process.Start(start);
            if (process is null)
            {
                return false;
            }

            // Read before waiting, so a full pipe can never stall it; the output is a few lines.
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(ToolTimeout) || !output.Wait(ToolTimeout))
            {
                process.Kill();
                Log.Write("localectl did not answer; typing everything through the clipboard");
                return false;
            }

            var us = process.ExitCode == 0 && IsUsLayout(output.Result);
            Log.Write(us ? "Keyboard layout is US: typing ASCII as keys" : "Keyboard layout is not plain US: typing through the clipboard");
            return us;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            Log.Write($"The keyboard layout could not be read ({e.Message}); typing everything through the clipboard");
            return false;
        }
    }

    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard with <paramref name="tool"/>, started directly with its
    /// arguments and never through a shell; the text goes in on standard input. Null on success, else the reason.
    /// Both tools leave a copy of themselves running to serve the clipboard and let the started process exit, so
    /// that exit is what is waited for. Standard output and error are not redirected: that running copy would keep
    /// the pipes open, and nothing would ever finish reading them.
    /// </summary>
    public static string? Copy(ClipboardTool tool, string text)
    {
        try
        {
            var start = new ProcessStartInfo(tool.Program)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            };
            foreach (var argument in tool.Arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return $"{tool.Program} did not start";
            }

            process.StandardInput.Write(text);
            process.StandardInput.Close();
            if (!process.WaitForExit(ToolTimeout))
            {
                process.Kill();
                return $"{tool.Program} did not finish";
            }

            return process.ExitCode == 0 ? null : $"{tool.Program} exited with {process.ExitCode}";
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            return $"{tool.Program} could not be run: {e.Message}";
        }
    }

    /// <summary>The US key for one ASCII character, Shift included; null for a control character.</summary>
    private static TypingStep? UsKey(char c)
    {
        // Written as the Windows code of the key in that place, so KeyMap stays the only table of evdev numbers.
        (ushort Code, bool Shift)? key = c switch
        {
            >= 'a' and <= 'z' => (char.ToUpperInvariant(c), false),
            >= 'A' and <= 'Z' => (c, true),
            >= '0' and <= '9' => (c, false),
            ' ' => (0x20, false),
            '!' => ('1', true),
            '@' => ('2', true),
            '#' => ('3', true),
            '$' => ('4', true),
            '%' => ('5', true),
            '^' => ('6', true),
            '&' => ('7', true),
            '*' => ('8', true),
            '(' => ('9', true),
            ')' => ('0', true),
            '-' => (0xBD, false),
            '_' => (0xBD, true),
            '=' => (0xBB, false),
            '+' => (0xBB, true),
            '[' => (0xDB, false),
            '{' => (0xDB, true),
            ']' => (0xDD, false),
            '}' => (0xDD, true),
            '\\' => (0xDC, false),
            '|' => (0xDC, true),
            ';' => (0xBA, false),
            ':' => (0xBA, true),
            '\'' => (0xDE, false),
            '"' => (0xDE, true),
            '`' => (0xC0, false),
            '~' => (0xC0, true),
            ',' => (0xBC, false),
            '<' => (0xBC, true),
            '.' => (0xBE, false),
            '>' => (0xBE, true),
            '/' => (0xBF, false),
            '?' => (0xBF, true),
            _ => null,
        };

        return key is { } k ? TypingStep.Press(KeyMap.ToEvdev(k.Code), k.Shift) : null;
    }
}
