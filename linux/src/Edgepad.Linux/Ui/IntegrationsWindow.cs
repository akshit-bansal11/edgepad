using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Edgepad.Dashboard;

namespace Edgepad.Ui;

/// <summary>Where a Claude Code or Codex connection stands, as the window shows it.</summary>
internal enum AgentStatus
{
    NotConnected,
    Connected,

    /// <summary>The agent's command line is not on this computer.</summary>
    NotFound,

    /// <summary>Codex is older than 0.159.</summary>
    UpdateCodex,

    /// <summary>Codex has the plugin but the owner has not trusted its hooks in /hooks yet.</summary>
    AwaitingTrust,
}

/// <summary>
/// One agent row's state. <paramref name="Restart"/> asks the owner to restart open sessions (after a Claude connect or a
/// re-stamp); <paramref name="Commands"/> is set when Edgepad could not run the vendor's commands itself, and holds them, one per line.
/// </summary>
internal sealed record AgentState(AgentStatus Status, DateTimeOffset? LastHeard = null, bool Restart = false, string? Commands = null);

/// <summary>
/// What the Integrations window needs from the rest of the app, filled by the wiring step: the plugin installer, the pieces an
/// integration starts and stops, and the clipboard. <see cref="Agent"/> and <see cref="Opened"/> may block (they read files and
/// run commands) and are called off the UI thread; the rest are called on it.
/// </summary>
internal sealed class IntegrationsHooks
{
    /// <summary>The row state of <c>claude</c> or <c>codex</c> right now.</summary>
    public required Func<string, AgentState> Agent { get; init; }

    /// <summary>Connects (true) or disconnects (false) the agent through the plugin installer; the state it ends in.</summary>
    public required Func<string, bool, Task<AgentState>> Connect { get; init; }

    /// <summary>An integration (an <see cref="IntegrationSettings"/> key) was switched on or off, or its settings changed.</summary>
    public required Action<string, bool> Switched { get; init; }

    /// <summary>Puts the text on the desktop clipboard.</summary>
    public required Action<string> Copy { get; init; }

    /// <summary>The window opened: the installer's EnsureCurrent (re-stamps an updated or moved app).</summary>
    public required Action Opened { get; init; }
}

/// <summary>
/// The Integrations window: system stats, weather, Claude Code, Codex and GitHub, every one off until the owner turns it on.
/// Switches write <see cref="IntegrationSettings"/> at once. No network call is made from here except the GitHub sign-in the
/// owner starts. Non-modal; closing it stops a sign-in in progress.
/// </summary>
internal sealed class IntegrationsWindow
{
    private const double Width = 520;
    private static readonly FontFamily Mono = new("monospace");

    private readonly IntegrationSettings settings;
    private readonly GitHubSignIn signIn;
    private readonly LinuxSecretStore secrets;
    private readonly IntegrationsHooks hooks;
    private readonly Look look = Look.Current;
    private readonly Window window;
    private readonly CheckBox stats;
    private readonly CheckBox weather;
    private readonly TextBox city;
    private readonly AgentRow claude;
    private readonly AgentRow codex;
    private readonly StackPanel github = new() { Spacing = 8 };
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Bumped by every sign-in start, cancel and close, so a sign-in that ends after it was replaced changes nothing.
    private int signInGeneration;
    private bool signedIn;
    private bool signingIn;
    private string? gitHubProblem;

    public IntegrationsWindow(IntegrationSettings settings, GitHubSignIn signIn, LinuxSecretStore secrets, IntegrationsHooks hooks)
    {
        this.settings = settings;
        this.signIn = signIn;
        this.secrets = secrets;
        this.hooks = hooks;

        var body = new StackPanel { Margin = new Thickness(24), Spacing = 8 };
        body.Children.Add(look.Text(LinuxStrings.Integrations_Title, 18, FontWeight.Bold));
        body.Children.Add(look.Text(LinuxStrings.Integrations_Intro, 14, dim: true));

        stats = Check(LinuxStrings.Integrations_Stats, settings.Flag(IntegrationSettings.Stats), on => Switch(IntegrationSettings.Stats, on));
        body.Children.Add(stats);

        weather = Check(LinuxStrings.Integrations_Weather, settings.Flag(IntegrationSettings.Weather), on => Switch(IntegrationSettings.Weather, on));
        body.Children.Add(weather);
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

        claude = new AgentRow(this, IntegrationSettings.Claude, LinuxStrings.Integrations_Claude);
        codex = new AgentRow(this, IntegrationSettings.Codex, LinuxStrings.Integrations_Codex);
        body.Children.Add(claude.Panel);
        body.Children.Add(codex.Panel);

        body.Children.Add(Heading(LinuxStrings.Integrations_GitHub));
        body.Children.Add(github);

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
            signInGeneration++;
            signIn.Cancel();
            closed.TrySetResult();
        };
    }

    /// <summary>Completes when the window has closed, however it closed.</summary>
    public Task Closed => closed.Task;

    /// <summary>Opens the window and reads the agents' and GitHub's state off the UI thread. Call on the UI thread.</summary>
    public void Show()
    {
        window.Show();
        _ = Guard(() => LoadAsync(opened: true));
    }

    /// <summary>Reads everything again (the wiring calls it when a hook event or a GitHub login arrives). Call on the UI thread.</summary>
    public void Refresh() => _ = Guard(() => LoadAsync(opened: false));

    /// <summary>Nothing may throw out of a handler: an exception on the UI thread ends the whole tray app.</summary>
    private static async Task Guard(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception e)
        {
            // The type and message only: neither ever holds the GitHub token or a code.
            Log.Write($"The Integrations window hit {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>"14:05" today, "8 Nov" before, "never" when nothing was heard.</summary>
    internal static string Heard(DateTimeOffset? last, DateTimeOffset now, CultureInfo culture)
    {
        if (last is not { } at)
        {
            return LinuxStrings.Integrations_Never;
        }

        var local = at.ToLocalTime();
        return local.Date == now.ToLocalTime().Date ? local.ToString("t", culture) : local.ToString("d MMM", culture);
    }

    private async Task LoadAsync(bool opened)
    {
        var (c, x, signed) = await Task.Run(() =>
        {
            if (opened)
            {
                hooks.Opened();
            }

            return (hooks.Agent(IntegrationSettings.Claude), hooks.Agent(IntegrationSettings.Codex), signIn.IsSignedIn);
        });
        claude.Show(c);
        codex.Show(x);
        signedIn = signed;
        if (!signingIn)
        {
            ShowGitHub();
        }
    }

    private void Switch(string key, bool on)
    {
        settings.SetFlag(key, on);
        hooks.Switched(key, on);
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
        hooks.Switched(IntegrationSettings.Weather, settings.Flag(IntegrationSettings.Weather));
    }

    private CheckBox Check(string text, bool on, Action<bool> changed)
    {
        var box = new CheckBox { Content = text, IsChecked = on, FontFamily = Look.Lato, FontSize = 14, Foreground = look.Ink };
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    private TextBlock Heading(string text)
    {
        var heading = look.Text(text, 14, FontWeight.Bold);
        heading.Margin = new Thickness(0, 10, 0, 0);
        return heading;
    }

    private TextBox Commands(string text) => new()
    {
        Text = text,
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = Mono,
        FontSize = 13,
        Foreground = look.Ink,
    };

    private Button CopyButton(string text)
    {
        var copy = look.Button(LinuxStrings.Integrations_Copy);
        copy.HorizontalAlignment = HorizontalAlignment.Left;
        copy.Click += (_, _) => hooks.Copy(text);
        return copy;
    }

    private void ShowGitHub()
    {
        github.Children.Clear();
        if (!GitHubApp.IsConfigured(GitHubApp.ClientId))
        {
            github.Children.Add(look.Text(LinuxStrings.Integrations_NoClientId, 14, dim: true));
            var off = look.Button(LinuxStrings.Integrations_SignIn);
            off.IsEnabled = false;
            off.HorizontalAlignment = HorizontalAlignment.Left;
            github.Children.Add(off);
            return;
        }

        if (gitHubProblem is { } problem)
        {
            var line = look.Text(problem, 14);
            line.Foreground = look.Danger;
            github.Children.Add(line);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (signedIn)
        {
            var login = settings.Text(IntegrationSettings.GitHubLogin);
            github.Children.Add(look.Text(Say.Format(LinuxStrings.Integrations_SignedIn, login.Length > 0 ? login : "—"), 14));
            if (!secrets.IsEncrypted)
            {
                github.Children.Add(look.Text(LinuxStrings.Integrations_NotEncrypted, 13, dim: true));
            }

            var signOut = look.Button(LinuxStrings.Integrations_SignOut, danger: true);
            signOut.Click += (_, _) => _ = Guard(SignOutAsync);
            buttons.Children.Add(signOut);
        }
        else
        {
            github.Children.Add(look.Text(LinuxStrings.Integrations_GitHubSteps, 13, dim: true));
            var start = look.Button(LinuxStrings.Integrations_SignIn, primary: true);
            start.Click += (_, _) => _ = Guard(SignInAsync);
            buttons.Children.Add(start);
        }

        github.Children.Add(buttons);
        github.Children.Add(Check(LinuxStrings.Integrations_Private, settings.Flag(IntegrationSettings.GitHubPrivate), on =>
        {
            settings.SetFlag(IntegrationSettings.GitHubPrivate, on);

            // The scope is chosen at sign-in, so a change while signed in signs in again with the new one.
            if (signedIn)
            {
                _ = Guard(SignInAsync);
            }
        }));
    }

    private void ShowCode(string userCode)
    {
        github.Children.Clear();
        github.Children.Add(look.Text(LinuxStrings.Integrations_GitHubSteps, 13, dim: true));
        github.Children.Add(look.Text(Say.Format(LinuxStrings.Integrations_Code, userCode), 24, FontWeight.Bold));
        var copy = CopyButton(userCode);
        var open = look.Button(LinuxStrings.Integrations_OpenPage, primary: true);
        open.Click += (_, _) => Tray.OpenWithDesktop(GitHubApp.DevicePage.AbsoluteUri, LinuxStrings.Integrations_GitHub);
        var cancel = look.Button(LinuxStrings.Common_Cancel);
        cancel.Click += (_, _) =>
        {
            signInGeneration++;
            signIn.Cancel();
            signingIn = false;
            ShowGitHub();
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(open);
        row.Children.Add(copy);
        row.Children.Add(cancel);
        github.Children.Add(row);
        github.Children.Add(look.Text(LinuxStrings.Integrations_Waiting, 14, dim: true));
    }

    private async Task SignInAsync()
    {
        var generation = ++signInGeneration;
        gitHubProblem = null;
        signingIn = true;
        try
        {
            var code = await signIn.StartAsync(settings.Flag(IntegrationSettings.GitHubPrivate), CancellationToken.None);
            if (generation != signInGeneration)
            {
                return;
            }

            if (code.State != SignInState.Started)
            {
                gitHubProblem = code.State == SignInState.NotConfigured ? LinuxStrings.Integrations_NoClientId : LinuxStrings.Integrations_SignInFailed;
                return;
            }

            ShowCode(code.UserCode);

            // Cancel, Close and a newer sign-in all end this wait as Cancelled through signIn.Cancel().
            var end = await signIn.WaitAsync(CancellationToken.None);
            if (generation != signInGeneration)
            {
                return;
            }

            if (end == SignInState.SignedIn)
            {
                signedIn = true;
                settings.SetFlag(IntegrationSettings.GitHub, true);
                hooks.Switched(IntegrationSettings.GitHub, true);
            }
            else if (end != SignInState.Cancelled)
            {
                gitHubProblem = LinuxStrings.Integrations_SignInFailed;
            }
        }
        finally
        {
            if (generation == signInGeneration)
            {
                signingIn = false;
                ShowGitHub();
            }
        }
    }

    private async Task SignOutAsync()
    {
        signInGeneration++;
        gitHubProblem = null;
        await Task.Run(signIn.SignOut);
        signedIn = false;
        settings.SetFlag(IntegrationSettings.GitHub, false);
        settings.Set(IntegrationSettings.GitHubLogin, string.Empty);
        hooks.Switched(IntegrationSettings.GitHub, false);
        signingIn = false;
        ShowGitHub();
    }

    /// <summary>One agent: its name, where it stands, what the owner can do about it, and Connect or Disconnect.</summary>
    private sealed class AgentRow
    {
        private readonly IntegrationsWindow owner;
        private readonly string agent;
        private readonly string name;
        private readonly TextBlock status;
        private readonly StackPanel details = new() { Spacing = 6 };
        private readonly Button action;
        private bool connected;

        public AgentRow(IntegrationsWindow owner, string agent, string name)
        {
            this.owner = owner;
            this.agent = agent;
            this.name = name;
            status = owner.look.Text(string.Empty, 14, dim: true);
            status.VerticalAlignment = VerticalAlignment.Center;
            action = owner.look.Button(LinuxStrings.Integrations_Connect);
            action.IsEnabled = false;
            action.Click += (_, _) => _ = Guard(ToggleAsync);

            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            line.Children.Add(status);
            Grid.SetColumn(action, 1);
            line.Children.Add(action);
            Panel = new StackPanel { Spacing = 6 };
            Panel.Children.Add(owner.Heading(name));
            Panel.Children.Add(line);
            Panel.Children.Add(details);
        }

        public StackPanel Panel { get; }

        public void Show(AgentState state)
        {
            var look = owner.look;
            connected = state.Status is AgentStatus.Connected or AgentStatus.AwaitingTrust;
            status.Text = state.Status switch
            {
                AgentStatus.Connected => Say.Format(LinuxStrings.Integrations_Connected, Heard(state.LastHeard, DateTimeOffset.Now, CultureInfo.CurrentCulture)),
                AgentStatus.NotFound => Say.Format(LinuxStrings.Integrations_NotFound, name),
                AgentStatus.UpdateCodex => LinuxStrings.Integrations_UpdateCodex,
                AgentStatus.AwaitingTrust => LinuxStrings.Integrations_WaitingTrust,
                _ => LinuxStrings.Integrations_NotConnected,
            };
            action.Content = connected ? LinuxStrings.Integrations_Disconnect : LinuxStrings.Integrations_Connect;
            action.IsEnabled = true;

            details.Children.Clear();
            if (state.Status == AgentStatus.AwaitingTrust)
            {
                details.Children.Add(look.Text(LinuxStrings.Integrations_TrustCodex, 14));
            }

            if (state.Restart)
            {
                details.Children.Add(look.Text(LinuxStrings.Integrations_Restart, 14, dim: true));
            }

            if (!connected)
            {
                details.Children.Add(look.Text(agent == IntegrationSettings.Claude ? LinuxStrings.Integrations_ClaudeSteps : LinuxStrings.Integrations_CodexSteps, 13, dim: true));
            }

            if (state.Commands is { Length: > 0 } commands)
            {
                details.Children.Add(look.Text(LinuxStrings.Integrations_RunYourself, 14));
                details.Children.Add(owner.Commands(commands));
                details.Children.Add(owner.CopyButton(commands));
            }
        }

        private async Task ToggleAsync()
        {
            action.IsEnabled = false;
            try
            {
                var state = await owner.hooks.Connect(agent, !connected);
                var on = state.Status is AgentStatus.Connected or AgentStatus.AwaitingTrust;
                owner.settings.SetFlag(agent, on);
                owner.hooks.Switched(agent, on);
                Show(state);
            }
            finally
            {
                action.IsEnabled = true;
            }
        }
    }
}
