namespace Edgepad.Macros;

/// <summary>
/// The tray menu's way into <see cref="MacroStore"/>. This dialog is the only place a macro is ever created,
/// which is the point: see the note at the top of <see cref="MacroStore"/> for why the owner typing a target
/// here, on the laptop, is what makes the phone's buttons safe to press.
/// </summary>
internal static class MacroEditor
{
    /// <summary>Opens the editor modally and saves on OK. Does nothing on Cancel.</summary>
    public static void Show(MacroStore store)
    {
        using var window = new MacroWindow(store);
        window.ShowDialog();
    }
}

/// <summary>
/// A settings dialog, deliberately plain: a list on the left, the selected macro's three fields on the right.
/// One window rather than a list plus an Add/Edit dialog on top of it — a second modal to fill in three text
/// boxes is the kind of ceremony that makes a setting feel expensive to change.
///
/// The working copy is a plain list that only reaches the store on OK, so Cancel needs to undo nothing.
/// </summary>
internal sealed class MacroWindow : Form
{
    private readonly List<Macro> working;

    private readonly ListBox list = new();
    private readonly TextBox name = new();
    private readonly TextBox target = new();
    private readonly TextBox arguments = new();
    private readonly Button browse = new();
    private readonly Button add = new();
    private readonly Button remove = new();
    private readonly Button up = new();
    private readonly Button down = new();

    /// <summary>Set while the controls are being written to from the list, so the writes do not echo back.</summary>
    private bool filling;

    public MacroWindow(MacroStore store)
    {
        working = [.. store.Macros];

        Text = "Edgepad macros";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(608, 316);

        list.SetBounds(12, 12, 230, 222);
        list.IntegralHeight = false;
        list.SelectedIndexChanged += (_, _) => Fill();

        Place(add, "Add", 12, 242, 56);
        Place(remove, "Remove", 74, 242, 72);
        Place(up, "Up", 152, 242, 42);
        Place(down, "Down", 200, 242, 42);
        add.Click += (_, _) => Add();
        remove.Click += (_, _) => Remove();
        up.Click += (_, _) => Reorder(-1);
        down.Click += (_, _) => Reorder(1);

        Field(name, "Name", 16, 340);
        Field(target, "Opens", 70, 250);
        Field(arguments, "Arguments (optional)", 124, 340);

        // Only so much of a name survives the trip to the phone, so the box stops there rather than letting
        // the store quietly cut a name the owner watched themselves type.
        // A byte budget cannot be spelled as a character count, and MaxLength is the only thing a TextBox
        // understands. This keeps typing roughly inside the limit; MacroStore does the real cut on save.
        name.MaxLength = MacroStore.MaxNameBytes;
        name.TextChanged += (_, _) => Edited();
        target.TextChanged += (_, _) => Edited();
        arguments.TextChanged += (_, _) => Edited();

        // The list shows the name, so it is restamped once the typing stops rather than on every keystroke.
        name.Leave += (_, _) => Relabel();
        target.Leave += (_, _) => Relabel();

        Place(browse, "Browse...", 514, 87, 82);
        browse.Click += (_, _) => Browse();

        var note = new Label
        {
            Text = "Only what you add here can be launched. The phone sends the button's position in this "
                + "list and nothing else, so it can never name a program of its own.",
            AutoSize = false,
        };
        note.SetBounds(256, 176, 340, 56);
        note.ForeColor = SystemColors.GrayText;

        var ok = new Button();
        var cancel = new Button();
        Place(ok, "OK", 430, 276, 80);
        Place(cancel, "Cancel", 516, 276, 80);
        ok.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (_, _) => store.Save(working);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.AddRange([list, add, remove, up, down, browse, note, ok, cancel]);
        Rebuild(working.Count > 0 ? 0 : -1);
    }

    /// <summary>What a row reads as. A macro being filled in has neither yet, and a blank row looks broken.</summary>
    private static string Label(Macro macro) =>
        macro.Name.Length > 0 ? macro.Name : macro.Target.Length > 0 ? macro.Target : "(new macro)";

    private static void Place(Button button, string text, int x, int y, int width)
    {
        button.Text = text;
        button.SetBounds(x, y, width, 26);
    }

    /// <summary>A label above its box, both added here so the three fields cannot drift apart.</summary>
    private void Field(TextBox box, string caption, int y, int width)
    {
        var label = new Label { Text = caption, AutoSize = true };
        label.SetBounds(256, y, 200, 18);
        box.SetBounds(256, y + 20, width, 23);
        Controls.Add(label);
        Controls.Add(box);
    }

    /// <summary>Rebuilds the list and selects <paramref name="select"/>, clamped to what is left of it.</summary>
    private void Rebuild(int select)
    {
        filling = true;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var macro in working)
        {
            list.Items.Add(Label(macro));
        }

        list.EndUpdate();
        filling = false;

        // Assigning the selection outside the guard so the fields follow it; -1 is a valid answer and clears them.
        list.SelectedIndex = working.Count == 0 ? -1 : Math.Clamp(select, 0, working.Count - 1);
        Fill();
    }

    /// <summary>Writes the selected macro into the fields, and settles what can be clicked.</summary>
    private void Fill()
    {
        if (filling)
        {
            return;
        }

        var index = list.SelectedIndex;
        var macro = index >= 0 ? working[index] : null;

        filling = true;
        name.Text = macro?.Name ?? string.Empty;
        target.Text = macro?.Target ?? string.Empty;
        arguments.Text = macro?.Arguments ?? string.Empty;
        filling = false;

        name.Enabled = target.Enabled = arguments.Enabled = browse.Enabled = macro is not null;
        remove.Enabled = macro is not null;
        up.Enabled = index > 0;
        down.Enabled = index >= 0 && index < working.Count - 1;
        add.Enabled = working.Count < MacroStore.MaxMacros;
    }

    private void Edited()
    {
        var index = list.SelectedIndex;
        if (filling || index < 0)
        {
            return;
        }

        working[index] = new Macro(name.Text, target.Text, arguments.Text.Length > 0 ? arguments.Text : null);
    }

    private void Relabel()
    {
        var index = list.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        filling = true;
        list.Items[index] = Label(working[index]);
        list.SelectedIndex = index;
        filling = false;
    }

    private void Add()
    {
        if (working.Count >= MacroStore.MaxMacros)
        {
            return;
        }

        working.Add(new Macro(string.Empty, string.Empty, null));
        Rebuild(working.Count - 1);
        name.Focus();
    }

    private void Remove()
    {
        var index = list.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        working.RemoveAt(index);
        Rebuild(index);
    }

    /// <summary>
    /// Moves the selected macro one place, and the selection with it: the row is what is being dragged.
    /// Named Reorder rather than Move because Control already has a Move event, and a method hiding it is
    /// an error under warnings-as-errors rather than the harmless shadowing it looks like.
    /// </summary>
    private void Reorder(int by)
    {
        var index = list.SelectedIndex;
        var to = index + by;
        if (index < 0 || to < 0 || to >= working.Count)
        {
            return;
        }

        (working[index], working[to]) = (working[to], working[index]);
        Rebuild(to);
    }

    private void Browse()
    {
        if (list.SelectedIndex < 0)
        {
            return;
        }

        using var picker = new OpenFileDialog
        {
            Title = "Choose what this button opens",
            Filter = "Programs and documents (*.*)|*.*",
            CheckFileExists = true,
        };

        if (picker.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        target.Text = picker.FileName;
        if (name.Text.Length == 0)
        {
            // The file's own name is nearly always the right button label, and is far less work than typing it.
            name.Text = Path.GetFileNameWithoutExtension(picker.FileName);
        }

        Relabel();
    }
}
