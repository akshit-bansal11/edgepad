using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Edgepad.Trust;

namespace Edgepad.Ui;

/// <summary>
/// The owner's way into <see cref="PinStore"/>: choose the security level, set the PIN, or give up on a forgotten
/// PIN. The tray opens it only under the server's HoldOff, because a connected phone is a keyboard and could
/// otherwise answer it. It touches only the Core stores.
///
/// Level and PIN are a working copy until Save, so closing the window any other way changes nothing. Reset is the
/// exception on purpose: it is its own confirmed action, not a setting waiting for Save. Changing the PIN or the
/// level clears the list of phones that entered the PIN once; the stores do that.
/// </summary>
internal sealed class SecurityWindow
{
    private static readonly string[] LevelNames =
    [
        "Trust only",
        "PIN once per phone",
        "PIN on every connection",
    ];

    private readonly PinStore pin;
    private readonly TrustStore trust;
    private readonly Action hangUp;
    private readonly Look look = Look.Current;
    private readonly Window window;
    private readonly RadioButton[] levels;
    private readonly Button setPin;
    private readonly Button save;
    private readonly TextBlock pinState;
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // A PIN chosen here and not yet saved. The digits live only as long as this window.
    private string? staged;

    // The level the radios show; writing the radios back must not echo into the choice.
    private int level;
    private bool filling;
    private bool busy;

    public SecurityWindow(PinStore pin, TrustStore trust, Action hangUp)
    {
        this.pin = pin;
        this.trust = trust;
        this.hangUp = hangUp;
        level = pin.Level;

        var body = new StackPanel { Margin = new Thickness(24), Spacing = 8 };
        body.Children.Add(look.Text("Security", 18, FontWeight.Bold));
        body.Children.Add(look.Text("Every phone is asked for your yes first. Levels 2 and 3 also ask for a six-digit PIN.", 14, dim: true));

        levels = new RadioButton[LevelNames.Length];
        for (var i = 0; i < levels.Length; i++)
        {
            var chosen = i + 1;
            var radio = new RadioButton
            {
                GroupName = "level",
                Content = $"{chosen}. {LevelNames[i]}",
                IsChecked = chosen == level,
                FontFamily = Look.Lato,
                FontSize = 14,
                Foreground = look.Ink,
            };
            radio.IsCheckedChanged += (_, _) => _ = Guard(() => ChooseAsync(radio, chosen));
            levels[i] = radio;
            body.Children.Add(radio);
        }

        setPin = look.Button("Set PIN…");
        setPin.Click += (_, _) => _ = Guard(() => SetPinFlowAsync());
        pinState = look.Text("", 14, dim: true);
        pinState.VerticalAlignment = VerticalAlignment.Center;
        var pinRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 8, 0, 0) };
        pinRow.Children.Add(setPin);
        pinRow.Children.Add(pinState);
        body.Children.Add(pinRow);
        body.Children.Add(look.Text("Saving a new PIN or level makes phones that entered the PIN once enter it again.", 14, dim: true));

        var reset = look.Button("Reset PIN and forget phones…", danger: true);
        reset.Click += (_, _) => _ = Guard(ConfirmResetAsync);
        var cancel = look.Button("Cancel");
        save = look.Button("Save", primary: true);
        save.IsDefault = true;
        save.Click += (_, _) => _ = Guard(SaveAsync);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 10, 0, 0) };
        footer.Children.Add(reset);
        var buttons = Look.Footer(cancel, save);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        body.Children.Add(footer);

        window = look.Dialog("Security", 480, body);
        cancel.Click += (_, _) => window.Close();
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };
        window.Closed += (_, _) =>
        {
            staged = null;
            closed.TrySetResult();
        };
        Refresh();
    }

    /// <summary>Completes when the window has closed, however it closed.</summary>
    public Task Closed => closed.Task;

    private bool HavePin => staged is not null || pin.HasPin;

    /// <summary>Opens the window. Call on the UI thread.</summary>
    public void Show() => window.Show();

    private void Refresh()
    {
        setPin.Content = HavePin ? "Change PIN…" : "Set PIN…";
        pinState.Text = staged is not null ? "A new PIN will be saved." : pin.HasPin ? "A PIN is set." : "No PIN is set.";
    }

    /// <summary>Nothing may throw out of a handler: an exception on the UI thread ends the whole tray app.</summary>
    private static async Task Guard(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception e)
        {
            // The message only: it never holds the PIN, and the stack trace has no use here.
            Log.Write($"The Security window hit {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// A radio was ticked. Levels 2 and 3 with no PIN anywhere would lock every phone out, so the PIN is asked for
    /// right now, and the choice goes back to what it was if the owner does not give one.
    /// </summary>
    private async Task ChooseAsync(RadioButton radio, int chosen)
    {
        if (filling || radio.IsChecked != true)
        {
            return;
        }

        if (chosen > 1 && !HavePin && !await SetPinFlowAsync())
        {
            filling = true;
            levels[level - 1].IsChecked = true;
            filling = false;
            return;
        }

        level = chosen;
    }

    /// <summary>Asks for a new PIN twice. True when one is staged; nothing is saved until Save.</summary>
    private async Task<bool> SetPinFlowAsync()
    {
        var digits = await PinDialog.Ask(window);
        if (digits is null)
        {
            return false;
        }

        staged = digits;
        Refresh();
        return true;
    }

    private async Task SaveAsync()
    {
        if (busy)
        {
            return;
        }

        if (staged is null && level == pin.Level)
        {
            window.Close();
            return;
        }

        busy = true;
        save.IsEnabled = false;
        try
        {
            // The PIN first: SetLevel refuses level 2 or 3 without one. SetPin also empties the verified list, so
            // the level needs its own write only when it changed. Hashing takes hundreds of milliseconds, so it
            // runs off the UI thread.
            if (staged is { } digits)
            {
                if (!await Task.Run(() => pin.SetPin(digits)))
                {
                    await Dialogs.Inform(window, "The PIN could not be saved, so nothing was changed. The log has the reason.");
                    return;
                }

                staged = null;
            }

            if (level != pin.Level && !pin.SetLevel(level))
            {
                Refresh();
                await Dialogs.Inform(window, "The level could not be saved, so the previous level still applies. The log has the reason.");
                return;
            }

            window.Close();
        }
        finally
        {
            busy = false;
            save.IsEnabled = true;
        }
    }

    private async Task ConfirmResetAsync()
    {
        var yes = await Dialogs.Confirm(
            window,
            "Delete the PIN, go back to level 1 and forget every trusted phone? Each phone, the one connected included, "
                + "will have to be trusted here again. A forgotten PIN cannot be recovered, only reset.",
            "Reset",
            "Cancel",
            dangerous: true);
        if (!yes)
        {
            return;
        }

        // Trust first, and the PIN only once the phones are gone: a PIN deleted while they stay trusted would start
        // the next run at level 1, where a stolen trusted phone gets in with nothing to answer. The phone is hung up
        // on either way, so a half-done reset leaves none connected.
        var forgotten = trust.Forget();
        var pinGone = forgotten && pin.Reset();
        hangUp();
        if (!forgotten)
        {
            await Dialogs.Inform(
                window,
                "Nothing was reset: the trusted phones could not be forgotten, so the PIN and the level were left as "
                    + "they were. No phone can connect while Edgepad stays running, but those phones will be trusted "
                    + "again, behind the same PIN, the next time it starts. Close whatever is holding the file and "
                    + $"choose Reset again, or delete it yourself:\n\n{trust.FilePath}");
        }
        else if (!pinGone)
        {
            await Dialogs.Inform(
                window,
                "The trusted phones were forgotten, but the PIN could not be reset: a settings file could not be "
                    + "removed, so the old PIN and level may still apply. Each phone has to be trusted here again. "
                    + "Close whatever is holding the file and choose Reset again, or delete security.txt and "
                    + $"pin-verified.txt yourself from:\n\n{pin.Folder}");
        }

        window.Close();
    }
}
