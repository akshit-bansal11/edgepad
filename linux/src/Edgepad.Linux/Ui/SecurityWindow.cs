using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Edgepad.Platform;
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
    internal const int Levels = 3;

    /// <summary>The name of level 1 to 3, in the desktop's language.</summary>
    internal static string LevelName(int level) => level switch
    {
        <= 1 => LinuxStrings.Security_Level1,
        2 => LinuxStrings.Security_Level2,
        _ => LinuxStrings.Security_Level3,
    };

    private readonly PinStore pin;
    private readonly TrustStore trust;
    private readonly Action hangUp;
    private readonly Func<Window, Task> forgetAll;
    private readonly PairedLinks paired;
    private readonly StackPanel phoneRows = new() { Spacing = 6 };
    private readonly Button forgetAllButton;

    // Forgets the Wi-Fi and USB pairings and tells the owner if it could not, as Forget trusted phones does.
    private readonly Func<Window, Task> forgetPairings;
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

    // The PIN is being hashed and written off the UI thread: closing now would lift the hold under that write.
    private bool hashing;
    private readonly Button cancel;

    public SecurityWindow(PinStore pin, TrustStore trust, PairedLinks paired, Func<Window, Task> forgetPairings, Func<Window, Task> forgetAll, Action hangUp)
    {
        this.forgetAll = forgetAll;
        this.paired = paired;
        this.pin = pin;
        this.trust = trust;
        this.hangUp = hangUp;
        this.forgetPairings = forgetPairings;
        level = pin.Level;

        var body = new StackPanel { Margin = new Thickness(24), Spacing = 8 };
        body.Children.Add(look.Text(LinuxStrings.Security_Title, 18, FontWeight.Bold));
        body.Children.Add(look.Text(LinuxStrings.Security_Intro, 14, dim: true));

        levels = new RadioButton[Levels];
        for (var i = 0; i < levels.Length; i++)
        {
            var chosen = i + 1;
            var radio = new RadioButton
            {
                GroupName = "level",
                Content = $"{chosen}. {LevelName(chosen)}",
                IsChecked = chosen == level,
                FontFamily = Look.Lato,
                FontSize = 14,
                Foreground = look.Ink,
            };
            radio.IsCheckedChanged += (_, _) => _ = Guard(() => ChooseAsync(radio, chosen));
            levels[i] = radio;
            body.Children.Add(radio);
        }

        setPin = look.Button(LinuxStrings.Security_SetPin);
        setPin.Click += (_, _) => _ = Guard(() => SetPinFlowAsync());
        pinState = look.Text("", 14, dim: true);
        pinState.VerticalAlignment = VerticalAlignment.Center;
        var pinRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 8, 0, 0) };
        pinRow.Children.Add(setPin);
        pinRow.Children.Add(pinState);
        body.Children.Add(pinRow);
        body.Children.Add(look.Text(LinuxStrings.Security_PinNote, 14, dim: true));

        body.Children.Add(look.Text(LinuxStrings.Security_TrustedPhones, 14, FontWeight.Bold));
        body.Children.Add(phoneRows);
        forgetAllButton = look.Button(LinuxStrings.Security_ForgetAll, danger: true);
        forgetAllButton.HorizontalAlignment = HorizontalAlignment.Left;
        forgetAllButton.Click += (_, _) => _ = Guard(ForgetAllAsync);
        body.Children.Add(forgetAllButton);

        var reset = look.Button(LinuxStrings.Security_Reset, danger: true);
        reset.Click += (_, _) => _ = Guard(ConfirmResetAsync);
        cancel = look.Button(LinuxStrings.Common_Cancel);
        save = look.Button(LinuxStrings.Common_Save, primary: true);
        save.IsDefault = true;
        save.Click += (_, _) => _ = Guard(SaveAsync);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 10, 0, 0) };
        footer.Children.Add(reset);
        var buttons = Look.Footer(cancel, save);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        body.Children.Add(footer);

        window = look.Dialog(LinuxStrings.Security_Title, 480, body);
        cancel.Click += (_, _) => window.Close();
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };
        window.Closing += (_, e) =>
        {
            if (hashing)
            {
                e.Cancel = true;
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
        setPin.Content = HavePin ? LinuxStrings.Security_ChangePin : LinuxStrings.Security_SetPin;
        pinState.Text = staged is not null ? LinuxStrings.Security_PinStaged : pin.HasPin ? LinuxStrings.Security_PinSet : LinuxStrings.Security_PinNone;
        FillPhones();
        forgetAllButton.IsEnabled = trust.CanForget || paired.CanForget;
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

    /// <summary>One row per trusted phone (Bluetooth) and per paired phone (Wi-Fi and USB), each with its own Forget.</summary>
    private void FillPhones()
    {
        phoneRows.Children.Clear();
        var any = false;
        var last = trust.Connections.Snapshot();
        foreach (var address in trust.TrustedPhones)
        {
            any = true;
            var name = PhoneName.Cached(address) is { } known ? $"{known} ({address})" : address;
            AddPhone(Row(name, last(address)), () => ForgetOneAsync(name, CoreStrings.Way_Bluetooth, () => trust.ForgetOne(address) & (!Paired(address) || paired.ForgetOne(address))));
        }

        foreach (var (id, linkName) in paired.List())
        {
            // Already listed above: a network phone is trusted under its link id, and its Forget removes both.
            if (trust.TrustedPhones.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            any = true;
            AddPhone(Row($"{linkName} ({CoreStrings.Way_WifiOrUsb})", last(id)), () => ForgetOneAsync(linkName, CoreStrings.Way_WifiOrUsb, () => paired.ForgetOne(id)));
        }

        if (!any)
        {
            phoneRows.Children.Add(look.Text(LinuxStrings.Security_NoPhones, 14, dim: true));
        }
    }

    // "NAME · last connected 8 Nov", or that it is not known since this update (shared wording).
    private static string Row(string name, DateTimeOffset? last) => LastConnected.RowText(name, last, DateTimeOffset.UtcNow);

    private bool Paired(string id) => paired.List().Any(link => string.Equals(link.Id, id, StringComparison.OrdinalIgnoreCase));

    private void AddPhone(string label, Func<Task> forget)
    {
        var forgetButton = look.Button(LinuxStrings.Common_Forget, danger: true);
        forgetButton.Click += (_, _) => _ = Guard(forget);
        var text = look.Text(label, 14, dim: true);
        text.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(text);
        Grid.SetColumn(forgetButton, 1);
        row.Children.Add(forgetButton);
        phoneRows.Children.Add(row);
    }

    private async Task ForgetOneAsync(string name, string way, Func<bool> forget)
    {
        var yes = await Dialogs.Confirm(window, Say.Format(LinuxStrings.Security_ForgetOneText, name, way), LinuxStrings.Common_Forget, LinuxStrings.Common_Cancel, dangerous: true);
        if (!yes)
        {
            return;
        }

        var done = forget();

        // A forgotten phone that had entered the PIN would otherwise skip it at level 2 once trusted again. Clears every
        // phone's, which is the safe side.
        var verified = pin.ForgetVerified();
        hangUp();
        if (!done)
        {
            await Dialogs.Inform(window, Say.Format(LinuxStrings.Error_ForgetOne, pin.Folder, name));
        }
        else if (!verified)
        {
            await Dialogs.Inform(window, $"{LinuxStrings.Error_ForgetPin}

{pin.Folder}");
        }

        Refresh();
    }

    private async Task ForgetAllAsync()
    {
        await forgetAll(window);
        Refresh();
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
        cancel.IsEnabled = false;
        try
        {
            // The PIN first: SetLevel refuses level 2 or 3 without one. SetPin also empties the verified list, so
            // the level needs its own write only when it changed. Hashing takes hundreds of milliseconds, so it
            // runs off the UI thread, and the window cannot close meanwhile.
            if (staged is { } digits)
            {
                hashing = true;
                bool saved;
                try
                {
                    saved = await Task.Run(() => pin.SetPin(digits));
                }
                finally
                {
                    hashing = false;
                }

                if (!saved)
                {
                    await Dialogs.Inform(window, LinuxStrings.Error_PinNotSaved);
                    return;
                }

                staged = null;
            }

            if (level != pin.Level && !pin.SetLevel(level))
            {
                Refresh();
                await Dialogs.Inform(window, LinuxStrings.Error_LevelNotSaved);
                return;
            }

            window.Close();
        }
        finally
        {
            busy = false;
            save.IsEnabled = true;
            cancel.IsEnabled = true;
        }
    }

    private async Task ConfirmResetAsync()
    {
        var yes = await Dialogs.Confirm(
            window,
            LinuxStrings.Security_ResetText,
            LinuxStrings.Common_Reset,
            LinuxStrings.Common_Cancel,
            dangerous: true);
        if (!yes)
        {
            return;
        }

        // Trust first, and the PIN only once the phones are gone: a PIN deleted while they stay trusted would start
        // the next run at level 1, where a stolen trusted phone gets in with nothing to answer. The phone is hung up
        // on either way, so a half-done reset leaves none connected.
        var forgotten = trust.Forget();

        // Every way of forgetting phones forgets the network pairings too, whatever became of the trust file: a
        // paired phone would otherwise resume its link and meet only the trust question.
        await forgetPairings(window);

        var pinGone = forgotten && pin.Reset();
        hangUp();
        if (!forgotten)
        {
            await Dialogs.Inform(window, Say.Format(LinuxStrings.Error_ResetTrust, trust.FilePath));
        }
        else if (!pinGone)
        {
            await Dialogs.Inform(window, Say.Format(LinuxStrings.Error_ResetPin, pin.Folder));
        }

        window.Close();
    }
}
