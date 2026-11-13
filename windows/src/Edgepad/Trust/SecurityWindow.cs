using Edgepad.Protocol;
using Edgepad.Ui;
using Palette = Edgepad.Ui.Palette;

namespace Edgepad.Trust;

/// <summary>What the Security window can do about the trusted phones; the tray owns the stores and the sessions.</summary>
internal sealed record PhoneActions(
    Func<IReadOnlyList<PhoneEntry>> List,
    Func<IWin32Window, PhoneEntry, bool> ForgetOne,
    Action<IWin32Window> ForgetAll);

/// <summary>
/// The owner's way into <see cref="PinStore"/> and the trusted phones: choose the security level, set the PIN, forget
/// one phone or all of them, or give up on a forgotten PIN. It is only ever opened by the tray, under
/// <c>SessionHost.HoldOff</c>, because a connected phone is a keyboard and could otherwise answer it. The window says
/// so, since opening it hangs up the phone that is connected.
///
/// Level and PIN are a working copy until Save, so closing the window any other way changes nothing. Forgetting is the
/// exception on purpose: each Forget and Reset is its own confirmed action, not a setting waiting for Save.
/// </summary>
internal sealed class SecurityWindow : Form
{
    // Measurements at 96 dpi, turned into pixels by Relayout for the monitor the window is on.
    private const int DialogWidth = 480;
    private const int Pad = 24;
    private const int Box = 36;
    private const int RowHeight = 28;
    private const int NoteIndent = 26;

    private static readonly string[] LevelNames =
    [
        Strings.Security_Level1,
        Strings.Security_Level2,
        Strings.Security_Level3,
    ];

    private static readonly string[] LevelNotes =
    [
        Strings.Security_Level1Note,
        Strings.Security_Level2Note,
        Strings.Security_Level3Note,
    ];

    private readonly PinStore pin;
    private readonly TrustStore trust;
    private readonly PhoneActions phones;
    private readonly Action hangUp;

    // Forgets the Wi-Fi and USB pairings and tells the owner if it could not, as Forget all does.
    private readonly Action<IWin32Window> forgetPairings;
    private readonly Palette palette = Palette.Current;

    private readonly Label heading = new() { AutoSize = false, Text = Strings.Security_Title };
    private readonly Label description = new()
    {
        AutoSize = false,
        Text = Strings.Security_Intro,
    };

    private readonly Label pinState = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label warning = new()
    {
        AutoSize = false,
        Text = Strings.Security_PinNote,
    };

    private readonly Label phonesHeading = new() { AutoSize = false, Text = Strings.Security_TrustedPhones };
    private readonly Label noPhones = new()
    {
        AutoSize = false,
        Text = Strings.Security_NoPhones,
    };

    private readonly List<(Label Name, FlatButton Forget, PhoneEntry Phone)> phoneRows = [];
    private readonly RadioButton[] levels;
    private readonly Label[] notes;
    private readonly FlatButton setPin;
    private readonly FlatButton forgetAll;
    private readonly FlatButton reset;
    private readonly FlatButton cancel;
    private readonly FlatButton save;

    // A PIN chosen here and not yet saved. The digits live only as long as this window.
    private string? staged;

    // The level the radios show; writing the radios back must not echo into the choice.
    private int level;
    private bool filling;

    public SecurityWindow(PinStore pin, TrustStore trust, PhoneActions phones, Action<IWin32Window> forgetPairings, Action hangUp)
    {
        this.pin = pin;
        this.trust = trust;
        this.phones = phones;
        this.hangUp = hangUp;
        this.forgetPairings = forgetPairings;
        level = pin.Level;

        Text = Strings.Security_Title;
        RightToLeft = Theme.Direction;
        RightToLeftLayout = Theme.IsRtl;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = palette.Card;

        levels = new RadioButton[LevelNames.Length];
        notes = new Label[LevelNames.Length];
        for (var i = 0; i < levels.Length; i++)
        {
            var chosen = i + 1;
            var radio = new RadioButton
            {
                Text = LevelNames[i],
                AccessibleName = Strings.Security_LevelName.Fill(chosen, LevelNames[i]),
                AccessibleDescription = LevelNotes[i],
                Checked = chosen == level,
                UseVisualStyleBackColor = false,
            };
            radio.CheckedChanged += (_, _) => Choose(radio, chosen);
            levels[i] = radio;
            notes[i] = new Label { AutoSize = false, Text = LevelNotes[i] };
        }

        setPin = new FlatButton(Strings.Security_SetPinButton, ButtonKind.Outline, palette);
        forgetAll = new FlatButton(Strings.Security_ForgetAll, ButtonKind.Ghost, palette, Icons.Unlink) { Danger = true };
        reset = new FlatButton(Strings.Security_Reset, ButtonKind.Ghost, palette) { Danger = true };
        cancel = new FlatButton(Strings.Common_Cancel, ButtonKind.Outline, palette) { DialogResult = DialogResult.Cancel };
        save = new FlatButton(Strings.Common_Save, ButtonKind.Primary, palette) { DialogResult = DialogResult.OK };
        setPin.Click += (_, _) => SetPinFlow();
        forgetAll.Click += (_, _) => ConfirmForgetAll();
        reset.Click += (_, _) => ConfirmReset();
        save.Click += (_, _) => Save();
        AcceptButton = save;
        CancelButton = cancel;

        // In tab order: the levels, then the PIN, then the phones, then the footer from left to right.
        Controls.AddRange([heading, description, .. levels, .. notes, pinState, setPin, warning, phonesHeading, noPhones, forgetAll, reset, cancel, save]);
        Refill();
    }

    /// <summary>The name the menu and the radios use for <paramref name="level"/>, 1 to 3.</summary>
    public static string LevelName(int level) => LevelNames[Math.Clamp(level, 1, LevelNames.Length) - 1];

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

    /// <summary>Lists the trusted phones again, after one was forgotten, and lays the window out for them.</summary>
    private void Refill()
    {
        foreach (var (name, forget, _) in phoneRows)
        {
            Controls.Remove(name);
            Controls.Remove(forget);
            name.Dispose();
            forget.Dispose();
        }

        phoneRows.Clear();
        foreach (var phone in phones.List())
        {
            var name = new Label { AutoSize = false, Text = LastConnected.RowText($"{phone.Name} · {phone.Way}", phone.Last, DateTimeOffset.Now), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            var forget = new FlatButton(Strings.Common_Forget, ButtonKind.Outline, palette) { Danger = true, AccessibleName = Strings.Security_ForgetPhoneName.Fill(phone.Name) };
            forget.Click += (_, _) => ConfirmForgetOne(phone);
            phoneRows.Add((name, forget, phone));
            Controls.Add(name);
            Controls.Add(forget);
        }

        Relayout();
    }

    private void Relayout()
    {
        var width = S(DialogWidth - (Pad * 2));
        var left = S(Pad);
        var y = S(Pad);

        void Put(Label label, float pixels, Weight weight, Color ink, int gap, int indent = 0)
        {
            label.Font = Theme.Font(Theme.Scale(pixels, DeviceDpi), weight);
            label.ForeColor = ink;
            label.BackColor = palette.Card;
            var height = TextRenderer.MeasureText(
                label.Text, label.Font, new Size(width - indent, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
            label.SetBounds(left + indent, y, width - indent, height);
            y += height + gap;
        }

        Put(heading, 18f, Weight.Bold, palette.Ink, S(6));
        Put(description, 14f, Weight.Regular, palette.Dim, S(14));

        for (var i = 0; i < levels.Length; i++)
        {
            var radio = levels[i];
            radio.Font = Theme.Font(Theme.Scale(14f, DeviceDpi), Weight.Regular);
            radio.ForeColor = palette.Ink;
            radio.BackColor = palette.Card;
            radio.SetBounds(left, y, width, S(RowHeight));
            y += S(RowHeight);
            Put(notes[i], 13f, Weight.Regular, palette.Dim, S(8), S(NoteIndent));
        }

        y += S(8);
        setPin.Text = HavePin ? Strings.Security_ChangePin : Strings.Security_SetPin;
        setPin.SetBounds(left, y, setPin.FitWidth(DeviceDpi), S(Box));
        pinState.Text = staged is not null ? Strings.Security_PinStaged : pin.HasPin ? Strings.Security_PinSet : Strings.Security_PinNone;
        pinState.Font = Theme.Font(Theme.Scale(14f, DeviceDpi), Weight.Regular);
        pinState.ForeColor = palette.Dim;
        pinState.BackColor = palette.Card;
        pinState.SetBounds(setPin.Right + S(12), y, width - setPin.Width - S(12), S(Box));
        y += S(Box + 14);

        Put(warning, 14f, Weight.Regular, palette.Dim, S(18));

        Put(phonesHeading, 15f, Weight.Bold, palette.Ink, S(8));
        noPhones.Visible = phoneRows.Count == 0;
        if (phoneRows.Count == 0)
        {
            Put(noPhones, 14f, Weight.Regular, palette.Dim, S(12));
        }

        foreach (var (name, forget, _) in phoneRows)
        {
            name.Font = Theme.Font(Theme.Scale(14f, DeviceDpi), Weight.Regular);
            name.ForeColor = palette.Ink;
            name.BackColor = palette.Card;
            forget.SetBounds(left + width - forget.FitWidth(DeviceDpi), y, forget.FitWidth(DeviceDpi), S(Box));
            name.SetBounds(left, y, forget.Left - left - S(8), S(Box));
            y += S(Box + 4);
        }

        y += S(8);
        forgetAll.Enabled = phoneRows.Count > 0;
        forgetAll.SetBounds(left, y, forgetAll.FitWidth(DeviceDpi), S(Box));
        reset.SetBounds(forgetAll.Right + S(8), y, reset.FitWidth(DeviceDpi), S(Box));
        y += S(Box + 18);

        save.SetBounds(left + width - save.FitWidth(DeviceDpi), y, save.FitWidth(DeviceDpi), S(Box));
        cancel.SetBounds(save.Left - S(8) - cancel.FitWidth(DeviceDpi), save.Top, cancel.FitWidth(DeviceDpi), S(Box));

        // The phones' Forget buttons come after the PIN and before the footer, wherever Refill added them.
        var order = new List<Control>([.. levels, setPin]);
        order.AddRange(phoneRows.Select(row => row.Forget));
        order.AddRange([forgetAll, reset, cancel, save]);
        for (var i = 0; i < order.Count; i++)
        {
            order[i].TabIndex = i;
        }

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
                Fail(Strings.Error_PinNotSaved);
                return;
            }

            staged = null;
        }

        if (level != pin.Level && !pin.SetLevel(level))
        {
            Relayout();
            Fail(Strings.Error_LevelNotSaved);
            return;
        }

        DialogResult = DialogResult.OK;
    }

    private void ConfirmForgetOne(PhoneEntry phone)
    {
        if (!Ask.Forget(
            this,
            Strings.Security_ForgetOneHeading.Fill(phone.Name),
            Strings.Security_ForgetOneText.Fill(phone.Name, phone.Way)))
        {
            return;
        }

        phones.ForgetOne(this, phone);
        Refill();
    }

    private void ConfirmForgetAll()
    {
        var names = string.Join(", ", phoneRows.Select(row => row.Phone.Name));
        if (!Ask.Forget(
            this,
            Strings.Security_ForgetAllHeading,
            names.Length > 0 ? Strings.Security_ForgetAllNamed.Fill(names) : Strings.Security_ForgetAllEvery))
        {
            return;
        }

        phones.ForgetAll(this);
        Refill();
    }

    private void ConfirmReset()
    {
        if (!Ask.Forget(
            this,
            Strings.Security_ResetHeading,
            Strings.Security_ResetText))
        {
            return;
        }

        // Trust first, and the PIN only once the phones are gone: a PIN deleted while they stay trusted would start
        // the next run at level 1, where a stolen trusted phone gets in with nothing to answer. The phone is hung up
        // on either way, so a half-done reset leaves none connected.
        var forgotten = trust.Forget();

        // Every way of forgetting phones forgets the network pairings too, whatever became of the trust file: a
        // paired phone would otherwise resume its link and meet only the trust question.
        forgetPairings(this);

        var pinGone = forgotten && pin.Reset();
        hangUp();
        if (!forgotten)
        {
            Fail(
                Strings.Error_ResetTrust.Fill(trust.FilePath));
        }
        else if (!pinGone)
        {
            Fail(
                Strings.Error_ResetPin.Fill(pin.Folder));
        }

        DialogResult = DialogResult.Abort;
    }

    private void Fail(string message) =>
        Ask.Message(this, message, Strings.Security_Title, MessageBoxIcon.Error);
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
        MaxLength = DigitInput.PasteLimit,
        AccessibleName = Strings.Pin_Title,
    };

    private readonly TextBox again = new()
    {
        BorderStyle = BorderStyle.None,
        UseSystemPasswordChar = true,
        MaxLength = DigitInput.PasteLimit,
        AccessibleName = Strings.Pin_AgainName,
    };

    private readonly Label heading = new() { AutoSize = false, Text = Strings.Pin_Title };
    private readonly Label firstLabel = new() { AutoSize = false, Text = Strings.Pin_Digits };
    private readonly Label againLabel = new() { AutoSize = false, Text = Strings.Pin_Again };

    // Where a refused PIN says why, in the danger ink, under the fields; empty until then. Its slot is always reserved.
    private readonly Label problem = new() { AutoSize = false, AccessibleRole = AccessibleRole.Alert };
    private readonly Framed firstFrame;
    private readonly Framed againFrame;
    private readonly FlatButton cancel;
    private readonly FlatButton ok;

    public PinDialog(Palette palette)
    {
        this.palette = palette;
        Text = Strings.Pin_Title;
        RightToLeft = Theme.Direction;
        RightToLeftLayout = Theme.IsRtl;
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
        cancel = new FlatButton(Strings.Common_Cancel, ButtonKind.Outline, palette) { DialogResult = DialogResult.Cancel };
        ok = new FlatButton(Strings.Pin_Use, ButtonKind.Primary, palette) { DialogResult = DialogResult.OK };
        foreach (var box in new[] { first, again })
        {
            box.KeyPress += (_, e) => e.Handled = !char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar);

            // A paste skips KeyPress.
            box.TextChanged += (_, _) =>
            {
                DigitInput.Apply(box, Pin.Length);
                problem.Text = "";
            };
        }

        ok.Click += (_, _) => Check();
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange([heading, firstLabel, firstFrame, againLabel, againFrame, problem, cancel, ok]);
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

        int Measure(string words) =>
            TextRenderer.MeasureText(words, problem.Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;

        Put(heading, 18f, Weight.Bold, S(14));
        Put(firstLabel, 14f, Weight.Bold, S(6));
        firstFrame.Place(new Rectangle(left, y, width, S(Box)), DeviceDpi);
        y += S(Box + 14);
        Put(againLabel, 14f, Weight.Bold, S(6));
        againFrame.Place(new Rectangle(left, y, width, S(Box)), DeviceDpi);
        y += S(Box + 6);
        problem.Font = Theme.Font(Theme.Scale(13f, DeviceDpi), Weight.Regular);
        problem.ForeColor = palette.Danger;
        problem.BackColor = palette.Card;
        var room = Math.Max(Measure(Strings.Pin_NotSix), Measure(Strings.Pin_Mismatch));
        problem.SetBounds(left, y, width, room);
        y += room + S(12);

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
            Refuse(Strings.Pin_NotSix, first);
        }
        else if (first.Text != again.Text)
        {
            Refuse(Strings.Pin_Mismatch, again);
        }
        else
        {
            DialogResult = DialogResult.OK;
        }
    }

    private void Refuse(string message, TextBox where)
    {
        problem.Text = message;
        where.Focus();
    }
}
