using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Edgepad.Dashboard;

namespace Edgepad.Ui;

/// <summary>
/// The Integrations window: system stats and weather, each off until the owner turns it on. Switches write
/// <see cref="IntegrationSettings"/> at once. AI tools and git hosts live in <see cref="DevelopersWindow"/>. Non-modal.
/// </summary>
internal sealed class IntegrationsWindow
{
    private const double Width = 520;

    private readonly IntegrationSettings settings;
    private readonly Action<string, bool> switched;
    private readonly Look look = Look.Current;
    private readonly Window window;
    private readonly TextBox city;
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="switched">An integration (an <see cref="IntegrationSettings"/> key) was switched on or off, or its settings changed.</param>
    public IntegrationsWindow(IntegrationSettings settings, Action<string, bool> switched)
    {
        this.settings = settings;
        this.switched = switched;

        var body = new StackPanel { Margin = new Thickness(24), Spacing = 8 };
        body.Children.Add(look.Text(LinuxStrings.Integrations_Title, 18, FontWeight.Bold));
        body.Children.Add(look.Text(LinuxStrings.Integrations_Intro, 14, dim: true));
        body.Children.Add(Check(LinuxStrings.Integrations_Stats, settings.Flag(IntegrationSettings.Stats), on => Switch(IntegrationSettings.Stats, on)));
        body.Children.Add(Check(LinuxStrings.Integrations_Weather, settings.Flag(IntegrationSettings.Weather), on => Switch(IntegrationSettings.Weather, on)));
        city = new TextBox
        {
            Text = settings.Text(IntegrationSettings.City),
            MaxLength = IntegrationSettings.MaxCityLength,
            FontFamily = Look.Lato,
            FontSize = 14,
            Width = 240,
        };
        city.LostFocus += (_, _) => CommitCity();
        city.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitCity();
            }
        };
        var cityLabel = look.Text(LinuxStrings.Integrations_City, 14);
        cityLabel.VerticalAlignment = VerticalAlignment.Center;
        var cityRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(28, 0, 0, 0) };
        cityRow.Children.Add(cityLabel);
        cityRow.Children.Add(city);
        body.Children.Add(cityRow);
        var note = look.Text(LinuxStrings.Integrations_WeatherNote, 13, dim: true);
        note.Margin = new Thickness(28, 0, 0, 0);
        body.Children.Add(note);

        var close = look.Button(LinuxStrings.Common_Close, primary: true);
        close.IsDefault = true;
        var footer = Look.Footer(close);
        footer.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(footer);

        window = look.Dialog(LinuxStrings.Integrations_Title, Width, body);
        close.Click += (_, _) => window.Close();
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };
        window.Closed += (_, _) =>
        {
            CommitCity();
            closed.TrySetResult();
        };
    }

    /// <summary>Closes the window. Call on the UI thread.</summary>
    public void Close() => window.Close();

    /// <summary>Completes when the window has closed, however it closed.</summary>
    public Task Closed => closed.Task;

    /// <summary>Opens the window. Call on the UI thread.</summary>
    public void Show() => window.Show();

    private void Switch(string key, bool on)
    {
        settings.SetFlag(key, on);
        switched(key, on);
    }

    /// <summary>A new city forgets the old one's place, so the weather client looks the new name up.</summary>
    private void CommitCity()
    {
        var text = city.Text?.Trim() ?? string.Empty;
        if (text == settings.Text(IntegrationSettings.City))
        {
            return;
        }

        settings.Set(IntegrationSettings.City, text);
        settings.Set(IntegrationSettings.Lat, string.Empty);
        settings.Set(IntegrationSettings.Lon, string.Empty);
        settings.Set(IntegrationSettings.Place, string.Empty);
        switched(IntegrationSettings.Weather, settings.Flag(IntegrationSettings.Weather));
    }

    private CheckBox Check(string text, bool on, Action<bool> changed)
    {
        var box = new CheckBox { Content = text, IsChecked = on, FontFamily = Look.Lato, FontSize = 14, Foreground = look.Ink };
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        return box;
    }
}
