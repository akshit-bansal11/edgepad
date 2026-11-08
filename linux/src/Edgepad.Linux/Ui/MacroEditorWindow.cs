using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Edgepad.Macros;
using Edgepad.Platform;

namespace Edgepad.Ui;

/// <summary>
/// The Macros… editor: the phone's launcher buttons, without a hand-edited text file. A macro is a name and a target
/// (a program, a document, a folder or a link) and optional arguments. Nothing is saved until Save; the store checks
/// the list again and tells every connected phone. Call on the UI thread.
/// </summary>
internal sealed class MacroEditorWindow
{
    private readonly MacroStore store;
    private readonly Look look = Look.Current;
    private readonly Window window;
    private readonly StackPanel rowsPanel = new() { Spacing = 10 };
    private readonly TextBlock empty;
    private readonly Button add;
    private readonly List<Row> rows = [];
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MacroEditorWindow(MacroStore store)
    {
        this.store = store;
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        body.Children.Add(look.Text("Macros", 18, FontWeight.Bold));
        body.Children.Add(look.Text($"Each macro is a button on the phone. Up to {MacroStore.MaxMacros}.", 14, dim: true));
        empty = look.Text("No macros yet", 14, dim: true);
        body.Children.Add(empty);
        body.Children.Add(new ScrollViewer { MaxHeight = 380, Content = rowsPanel });

        add = look.Button("Add macro");
        add.Click += (_, _) => Guard(() => AddRow("", "", null, null));
        var choose = look.Button("Choose…");
        choose.Click += (_, _) => _ = GuardAsync(ChooseAsync);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        tools.Children.Add(add);
        tools.Children.Add(choose);
        body.Children.Add(tools);

        var cancel = look.Button("Cancel");
        var save = look.Button("Save", primary: true);
        save.IsDefault = true;
        save.Click += (_, _) => _ = GuardAsync(SaveAsync);
        body.Children.Add(Look.Footer(cancel, save));

        window = look.Dialog("Macros", 560, body);
        cancel.Click += (_, _) => window.Close();
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };
        window.Closed += (_, _) => closed.TrySetResult();
        foreach (var macro in store.Macros)
        {
            AddRow(macro.Name, macro.Target, macro.Arguments, macro.Icon);
        }

        Refresh();
    }

    /// <summary>Completes when the window has closed, however it closed.</summary>
    public Task Closed => closed.Task;

    public void Show() => window.Show();

    private void AddRow(string name, string target, string? arguments, string? icon)
    {
        if (rows.Count >= MacroStore.MaxMacros)
        {
            return;
        }

        var row = new Row(icon);
        row.Name.Text = name;
        row.Name.Watermark = "Name";
        row.Name.MaxLength = MacroStore.MaxNameBytes;
        row.Target.Text = target;
        row.Target.Watermark = "Program, file, folder or link";
        row.Arguments.Text = arguments;
        row.Arguments.Watermark = "Arguments (optional)";
        foreach (var box in new[] { row.Name, row.Target, row.Arguments })
        {
            box.FontFamily = Look.Lato;
            box.FontSize = 14;
        }

        var remove = look.Button("Remove", danger: true);
        remove.Click += (_, _) => Guard(() => Remove(row));
        var browse = look.Button("Browse…");
        browse.Click += (_, _) => _ = GuardAsync(() => BrowseAsync(row));

        var first = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        first.Children.Add(row.Name);
        Grid.SetColumn(remove, 1);
        first.Children.Add(remove);
        var second = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        second.Children.Add(row.Target);
        Grid.SetColumn(browse, 1);
        second.Children.Add(browse);
        var view = new StackPanel { Spacing = 6 };
        view.Children.Add(first);
        view.Children.Add(second);
        view.Children.Add(row.Arguments);
        row.View = new Border
        {
            Child = view,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            BorderBrush = look.Line,
            BorderThickness = new Thickness(1),
        };
        rows.Add(row);
        rowsPanel.Children.Add(row.View);
        Refresh();
    }

    private void Remove(Row row)
    {
        rows.Remove(row);
        rowsPanel.Children.Remove(row.View!);
        Refresh();
    }

    private void Refresh()
    {
        empty.IsVisible = rows.Count == 0;
        add.IsEnabled = rows.Count < MacroStore.MaxMacros;
    }

    private async Task BrowseAsync(Row row)
    {
        var picked = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose a program or file", AllowMultiple = false });
        if (picked.Count == 1 && picked[0].TryGetLocalPath() is { } path)
        {
            row.Target.Text = path;
            row.Icon = null;
            if (string.IsNullOrWhiteSpace(row.Name.Text))
            {
                row.Name.Text = Path.GetFileNameWithoutExtension(path);
            }
        }
    }

    /// <summary>Adds the installed app the owner picks: the same list the phone's "add from the laptop's apps" offers.</summary>
    private async Task ChooseAsync()
    {
        var apps = await Task.Run(() => new DesktopEntryCatalog().Scan());
        var list = new ListBox { ItemsSource = apps.Select(app => app.Name).ToList(), MaxHeight = 320, FontFamily = Look.Lato };
        var cancel = look.Button("Cancel");
        var pick = look.Button("Add", primary: true);
        pick.IsDefault = true;
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        body.Children.Add(look.Text(apps.Count == 0 ? "No apps were found on this laptop." : "Add an app", 18, FontWeight.Bold));
        body.Children.Add(list);
        body.Children.Add(Look.Footer(cancel, pick));
        var picker = look.Dialog("Add an app", 420, body);
        cancel.Click += (_, _) => picker.Close();
        pick.Click += (_, _) =>
        {
            if (list.SelectedIndex is >= 0 and var index && index < apps.Count)
            {
                var app = apps[index];
                AddRow(app.Name, app.Target, app.Arguments, app.IconPath);
            }

            picker.Close();
        };
        await picker.ShowDialog(window);
    }

    private async Task SaveAsync()
    {
        var macros = rows
            .Select(row => new Macro(row.Name.Text ?? "", row.Target.Text ?? "", row.Arguments.Text, row.Icon))
            .ToList();
        if (MacroStore.WithoutTarget(macros) is var missing and >= 0)
        {
            await Dialogs.Inform(window, $"Macro {missing + 1} has nothing to open. Fill in its program, file, folder or link, or remove it.");
            return;
        }

        if (store.Save(macros) is { } failure)
        {
            await Dialogs.Inform(window, $"The macros could not be saved, so nothing was changed. {failure}");
            return;
        }

        window.Close();
    }

    /// <summary>Nothing may throw out of a handler: an exception on the UI thread ends the whole tray app.</summary>
    private static void Guard(Action work)
    {
        try
        {
            work();
        }
        catch (Exception e)
        {
            Log.Write($"The Macros window hit {e.GetType().Name}: {e.Message}");
        }
    }

    private static async Task GuardAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception e)
        {
            Log.Write($"The Macros window hit {e.GetType().Name}: {e.Message}");
        }
    }

    private sealed class Row(string? icon)
    {
        public TextBox Name { get; } = new();

        public TextBox Target { get; } = new();

        public TextBox Arguments { get; } = new();

        public string? Icon { get; set; } = icon;

        public Control? View { get; set; }
    }
}
