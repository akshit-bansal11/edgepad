using Edgepad.Protocol;
using Edgepad.Ui;
using Palette = Edgepad.Ui.Palette;

namespace Edgepad.Trust;

/// <summary>
/// The owner's way into <see cref="PinStore"/>: choose the security level, set the PIN, or give up on a forgotten
/// PIN. It is only ever opened by the tray, under <c>SessionHost.HoldOff</c>, because a connected phone is a
/// keyboard and could otherwise answer it.
///
/// Level and PIN are a working copy until Save, so closing the window any other way changes nothing. Reset is the
/// exception on purpose: it is its own confirmed action, not a setting waiting for Save.
/// </summary>
internal sealed class SecurityWindow : Form
{
    // Measurements at 96 dpi, turned into pixels by Relayout for the monitor the window is on.
    private const int DialogWidth = 460;
    private const int Pad = 24;
    private const int Box = 36;
    private const int RowHeight = 30;

    private static readonly string[] LevelNames =
    [
        "Trust only",
        "PIN once per phone",
        "PIN on every connection",
    ];

    private readonly PinStore pin;
    private readonly TrustStore trust;
    private readonly Action hangUp;

    // Forgets the Wi-Fi and USB pairings and tells the owner if it could not, as Forget trusted phones does.
    private readonly Action forgetPairings;
    private readonly Palette palette = Palette.Current;

    private readonly Label heading = new() { AutoSize = false, Text = "Security" };
    private readonly Label description = new()
    {
        AutoSize = false,
        Text = "Every phone is asked for your yes first. Levels 2 and 3 also ask for a six-digit PIN.",
    };

    private readonly Label pinState = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label warning = new()
    {
        AutoSize = false,
        Text = "Saving a new PIN or level makes phones that entered the PIN once enter it again.",
    };

    private readonly RadioButton[] levels;
    private readonly FlatButton setPin;
    private readonly FlatButton reset;
    private readonly FlatButton cancel;
    private readonly FlatButton save;

    // A PIN chosen here and not yet saved. The digits live only as long as this window.
    private string? staged;

    // The level the radios show; writing the radios back must not echo into the choice.
    private int level;
    private bool filling;

    public SecurityWindow(PinStore pin, TrustStore trust, Action forgetPairings, Action hangUp)
    {
        this.pin = pin;
        this.trust = trust;
        this.hangUp = hangUp;
        this.forgetPairings = forgetPairings;
        level = pin.Level;

        Text = "Security";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = palette.Card;

        levels = new RadioButton[LevelNames.Length];
        for (var i = 0; i < levels.Length; i++)
        {
            var chosen = i + 1;
            var radio = new RadioButton
            {
                Text = $"{chosen}. {LevelNames[i]}",
                AccessibleName = $"Level {chosen}, {LevelNames[i]}",
                Checked = chosen == level,
                UseVisualStyleBackColor = false,
            };
            radio.CheckedChanged += (_, _) => Choose(radio, chosen);
            levels[i] = radio;
        }

        setPin = new FlatButton("Set PIN…", ButtonKind.Outline, palette);
        reset = new FlatButton("Reset PIN and forget phones…", ButtonKind.Ghost, palette, Icons.Unlink) { Danger = true };
        cancel = new FlatButton("Cancel", ButtonKind.Outline, palette) { DialogResult = DialogResult.Cancel };
        save = new FlatButton("Save", ButtonKind.Primary, palette) { DialogResult = DialogResult.OK };
        setPin.Click += (_, _) => SetPinFlow();
        reset.Click += (_, _) => ConfirmReset();
        save.Click += (_, _) => Save();
        AcceptButton = save;
        CancelButton = cancel;

        // In tab order: the levels, then the PIN, then the footer from left to right.
        Controls.AddRange([heading, description, .. levels, pinState, setPin, warning, reset, cancel, save]);
        Relayout();
    }

    private bool HavePin => staged is not null || pin.HasPin;

    private int S(int value) => Theme.Scale(value, DeviceDpi);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle, palette.IsDark);

        // The window can open on a monitor other than the one the constructor measured for.
        Relayout();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Relayout();
    }

    private void Relayout()
    {
        var width = S(DialogWidth - (Pad * 2));
        var left = S(Pad);
        var y = S(Pad);

        void Put(Label label, float pixels, Weight weight, Color ink, int gap)
        {
            label.Font = Theme.Font(Theme.Scale(pixels, DeviceDpi), weight);
            label.ForeColor = ink;
            label.BackColor = palette.Card;
            var height = TextRenderer.MeasureText(
                label.Text, label.Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
            label.SetBounds(left, y, width, height);
            y += height + gap;
        }

        Put(heading, 18f, Weight.Bold, palette.Ink, S(6));
        Put(description, 14f, Weight.Regular, palette.Dim, S(14));

        foreach (var radio in levels)
        {
            radio.Font = Theme.Font(Theme.Scale(14f, DeviceDpi), Weight.Regular);
            radio.ForeColor = palette.Ink;
            radio.BackColor = palette.Card;
            radio.SetBounds(left, y, width, S(RowHeight));
            y += S(RowHeight);
        }

        y += S(14);
        setPin.Text = HavePin ? "Change PIN…" : "Set PIN…";
        setPin.SetBounds(left, y, setPin.FitWidth(DeviceDpi), S(Box));
        pinState.Text = staged is not null ? "A new PIN will be saved." : pin.HasPin ? "A PIN is set." : "No PIN is set.";
        pinState.Font = Theme.Font(Theme.Scale(14f, DeviceDpi), Weight.Regular);
        pinState.ForeColor = palette.Dim;
        pinState.BackColor = palette.Card;
        pinState.SetBounds(setPin.Right + S(12), y, width - setPin.Width - S(12), S(Box));
        y += S(Box + 14);

        Put(warning, 14f, Weight.Regular, palette.Dim, S(18));

        reset.SetBounds(left, y, reset.FitWidth(DeviceDpi), S(Box));
        save.SetBounds(left + width - save.FitWidth(DeviceDpi), y + S(Box + 10), save.FitWidth(DeviceDpi), S(Box));
        cancel.SetBounds(save.Left - S(8) - cancel.FitWidth(DeviceDpi), save.Top, cancel.FitWidth(DeviceDpi), S(Box));

        ClientSize = new Size(S(DialogWidth), save.Bottom + S(Pad));
        Invalidate();
    }

    /// <summary>
    /// A radio was ticked. Levels 2 and 3 with no PIN anywhere would lock every phone out, so the PIN is asked for
    /// right now, and the choice goes back to what it was if the owner does not give one.
    /// </summary>
    private void Choose(RadioButton radio, int chosen)
    {
        if (filling || !radio.Checked)
        {
            return;
        }

        if (chosen > 1 && !HavePin && !SetPinFlow())
        {
            filling = true;
            levels[level - 1].Checked = true;
            filling = false;
            return;
        }

        level = chosen;
    }

    /// <summary>Asks for a new PIN twice. True when one is staged; nothing is saved until Save.</summary>
    private bool SetPinFlow()
    {
        using var dialog = new PinDialog(palette);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        staged = dialog.Digits;
        Relayout();
        return true;
    }

    private void Save()
    {
        // Save is the dialog's OK button, which has already set the result that closes it.
        DialogResult = DialogResult.None;

        if (staged is null && level == pin.Level)
        {
            DialogResult = DialogResult.Cancel;
            return;
        }

        // The PIN first: SetLevel refuses level 2 or 3 without one. SetPin also empties the verified list, so
        // the level needs its own write only when it changed.
        if (staged is not null)
        {
            if (!pin.SetPin(staged))
            {
                Fail("The PIN could not be saved, so nothing was changed. The log has the reason.");
                return;
            }

            staged = null;
        }

        if (level != pin.Level && !pin.SetLevel(level))
        {
            Relayout();
            Fail("The level could not be saved, so the previous level still applies. The log has the reason.");
            return;
        }

        DialogResult = DialogResult.OK;
    }

    private void ConfirmReset()
    {
        var answer = MessageBox.Show(
            this,
            "Delete the PIN, go back to level 1 and forget every trusted phone? Each phone, the one connected included, "
                + "will have to be trusted here again. A forgotten PIN cannot be recovered, only reset.",
            "Edgepad",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        // Trust first, and the PIN only once the phones are gone: a PIN deleted while they stay trusted would start
        // the next run at level 1, where a stolen trusted phone gets in with nothing to answer. The phone is hung up
        // on either way, so a half-done reset leaves none connected.
        var forgotten = trust.Forget();

        // Every way of forgetting phones forgets the network pairings too, whatever became of the trust file: a
        // paired phone would otherwise resume its link and meet only the trust question.
        forgetPairings();

        var pinGone = forgotten && pin.Reset();
        hangUp();
        if (!forgotten)
        {
            Fail(
                "Nothing was reset: the trusted phones could not be forgotten, so the PIN and the level were left as "
                    + "they were. No phone can connect while Edgepad stays running, but those phones will be trusted "
                    + "again, behind the same PIN, the next time it starts. Close whatever is holding the file and "
                    + $"choose Reset again, or delete it yourself:\n\n{trust.FilePath}");
        }
        else if (!pinGone)
        {
            Fail(
                "The trusted phones were forgotten, but the PIN could not be reset: a settings file could not be "
                    + "removed, so the old PIN and level may still apply. Each phone has to be trusted here again. "
                    + "Close whatever is holding the file and choose Reset again, or delete security.txt and "
                    + $"pin-verified.txt yourself from:\n\n{pin.Folder}");
        }

        DialogResult = DialogResult.Abort;
    }

    private void Fail(string message) =>
        MessageBox.Show(this, message, "Edgepad", MessageBoxButtons.OK, MessageBoxIcon.Error);
}

/// <summary>Two masked boxes that take six digits only; OK is refused until they match.</summary>
internal sealed class PinDialog : Form
{
    private const int DialogWidth = 360;
    private const int Pad = 24;
    private const int Box = 36;

    private readonly Palette palette;
    private readonly TextBox first = new()
    {
        BorderStyle = BorderStyle.None,
        UseSystemPasswordChar = true,
        MaxLength = Pin.Length,
        AccessibleName = "New PIN",
    };

    private readonly TextBox again = new()
    {
        BorderStyle = BorderStyle.None,
        UseSystemPasswordChar = true,
        MaxLength = Pin.Length,
        AccessibleName = "New PIN again",
    };

    private readonly Label heading = new() { AutoSize = false, Text = "New PIN" };
    private readonly Label firstLabel = new() { AutoSize = false, Text = "Six digits" };
    private readonly Label againLabel = new() { AutoSize = false, Text = "Again" };
    private readonly Framed firstFrame;
    private readonly Framed againFrame;
    private readonly FlatButton cancel;
    private readonly FlatButton ok;

    public PinDialog(Palette palette)
    {
        this.palette = palette;
        Text = "New PIN";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = palette.Card;

        firstFrame = new Framed(first, palette, radius: 8, inset: 12);
        againFrame = new Framed(again, palette, radius: 8, inset: 12);
        cancel = new FlatButton("Cancel", ButtonKind.Outline, palette) { DialogResult = DialogResult.Cancel };
        ok = new FlatButton("Use this PIN", ButtonKind.Primary, palette) { DialogResult = DialogResult.OK };
        foreach (var box in new[] { first, again })
        {
            box.KeyPress += (_, e) => e.Handled = !char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar);

            // A paste skips KeyPress.
            box.TextChanged += (_, _) =>
            {
                var digits = new string([.. box.Text.Where(char.IsAsciiDigit)]);
                if (digits != box.Text)
                {
                    box.Text = digits;
                }
            };
        }

        ok.Click += (_, _) => Check();
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange([heading, firstLabel, firstFrame, againLabel, againFrame, cancel, ok]);
        Relayout();
    }

    /// <summary>The PIN typed. Read only after the dialog closed with OK.</summary>
    public string Digits => first.Text;

    private int S(int value) => Theme.Scale(value, DeviceDpi);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(Handle, palette.IsDark);
        Relayout();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Relayout();
    }

    private void Relayout()
    {
        var width = S(DialogWidth - (Pad * 2));
        var left = S(Pad);
        var y = S(Pad);

        void Put(Label label, float pixels, Weight weight, int gap)
        {
            label.Font = Theme.Font(Theme.Scale(pixels, DeviceDpi), weight);
            label.ForeColor = palette.Ink;
            label.BackColor = palette.Card;
            label.SetBounds(left, y, width, label.Font.Height);
            y += label.Height + gap;
        }

        Put(heading, 18f, Weight.Bold, S(14));
        Put(firstLabel, 14f, Weight.Bold, S(6));
        firstFrame.Place(new Rectangle(left, y, width, S(Box)), DeviceDpi);
        y += S(Box + 14);
        Put(againLabel, 14f, Weight.Bold, S(6));
        againFrame.Place(new Rectangle(left, y, width, S(Box)), DeviceDpi);
        y += S(Box + 18);

        ok.SetBounds(left + width - ok.FitWidth(DeviceDpi), y, ok.FitWidth(DeviceDpi), S(Box));
        cancel.SetBounds(ok.Left - S(8) - cancel.FitWidth(DeviceDpi), y, cancel.FitWidth(DeviceDpi), S(Box));
        ClientSize = new Size(S(DialogWidth), y + S(Box + Pad));
    }

    private void Check()
    {
        // OK has already set the result that closes the dialog.
        DialogResult = DialogResult.None;
        if (first.Text.Length != Pin.Length)
        {
            Refuse("A PIN is exactly six digits.", first);
        }
        else if (first.Text != again.Text)
        {
            Refuse("The two PINs are not the same.", again);
        }
        else
        {
            DialogResult = DialogResult.OK;
        }
    }

    private void Refuse(string message, TextBox where)
    {
        MessageBox.Show(this, message, "Edgepad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        where.Focus();
    }
}
