using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Edgepad.Dashboard;

namespace Edgepad.Ui;

/// <summary>
/// What the For developers window needs from the rest of the app. <see cref="Agent"/>, <see cref="Opened"/> and <see cref="GitSignedIn"/>
/// may block (files, commands, the secret store) and are called off the UI thread; the rest are called on it.
/// </summary>
internal sealed class DevelopersHooks
{
    /// <summary>The row state of an AI tool (its provider id) right now.</summary>
    public required Func<string, AgentState> Agent { get; init; }

    /// <summary>Connects (true) or disconnects (false) the tool; the state it ends in.</summary>
    public required Func<string, bool, Task<AgentState>> Connect { get; init; }

    /// <summary>An integration (an <see cref="IntegrationSettings"/> key) was switched on or off, or its token changed.</summary>
    public required Action<string, bool> Switched { get; init; }

    /// <summary>Puts the text on the desktop clipboard.</summary>
    public required Action<string> Copy { get; init; }

    /// <summary>The window opened: the installers' EnsureCurrent.</summary>
    public required Action Opened { get; init; }

    /// <summary>The Developer mode switch.</summary>
    public required Action<bool> SetDeveloper { get; init; }

    /// <summary>Whether a token is saved for the git host with this id.</summary>
    public required Func<string, bool> GitSignedIn { get; init; }

    /// <summary>Checks and saves a pasted token: id, token, url, user.</summary>
    public required Func<string, string, string, string, CancellationToken, Task<GitSaveResult>> GitSave { get; init; }

    /// <summary>Forgets a token host's token.</summary>
    public required Action<string> GitSignOut { get; init; }

    /// <summary>The signed-in GitHub login, or null.</summary>
    public required Func<CancellationToken, Task<string?>> Login { get; init; }
}

/// <summary>
/// The For developers window: the Developer mode switch, then AI tools and git hosts, one row each, built from
/// <see cref="DevProviders"/>. A row is a mark, a name, one status line and one button; "How to connect" opens its steps,
/// GitHub's sign-in code opens in its own panel under its row, and the token hosts open a small inline form. Not modal.
/// With Developer mode off every row is disabled and says why.
/// </summary>
internal sealed class DevelopersWindow
{
    private const double Width = 520;
    private static readonly FontFamily Mono = new("monospace");

    private readonly IntegrationSettings settings;
    private readonly GitHubSignIn signIn;
    private readonly LinuxSecretStore secrets;
    private readonly DevelopersHooks hooks;
    private readonly Look look = Look.Current;
    private readonly Window window;
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StackPanel list = new() { Spacing = 8 };
    private readonly CheckBox developerSwitch;
    private readonly TextBlock hint;
    private readonly Dictionary<string, AgentState> states = [];
    private readonly HashSet<string> busy = [];
    private readonly HashSet<string> expanded = [];
    private readonly Dictionary<string, Control> tagged = [];

    // The persistent pieces: added under a row when it needs them, so their text survives a redraw.
    private readonly StackPanel codePanel = new() { Spacing = 8 };
    private readonly TextBlock codeText;
    private readonly CheckBox includePrivate;
    private readonly StackPanel formPanel = new() { Spacing = 6 };
    private readonly StackPanel urlField = new() { Spacing = 4 };
    private readonly StackPanel userField = new() { Spacing = 4 };
    private readonly TextBox urlBox;
    private readonly TextBox userBox;
    private readonly TextBox tokenBox;
    private readonly TextBlock errorText;
    private readonly Button saveButton;

    private string? editing;
    private bool saving;
    private string saveError = string.Empty;
    private string? userCode;
    private bool signingIn;
    private int signInRun;
    private bool signedIn;

    // Set while the window writes its own controls, so their events do not echo into the settings.
    private bool filling;

    public DevelopersWindow(IntegrationSettings settings, GitHubSignIn signIn, LinuxSecretStore secrets, DevelopersHooks hooks)
    {
        this.settings = settings;
        this.signIn = signIn;
        this.secrets = secrets;
        this.hooks = hooks;

        developerSwitch = Check(LinuxStrings.Menu_DeveloperMode, false, on =>
        {
            if (!filling)
            {
                // The dashboard applies it off the UI thread and raises DeveloperChanged, which refreshes this window.
                hooks.SetDeveloper(on);
            }
        });
        hint = look.Text(LinuxStrings.Dev_NeedDeveloper, 13, FontWeight.Bold);
        includePrivate = Check(LinuxStrings.Integrations_Private, false, on =>
        {
            if (!filling)
            {
                // The scope is chosen at sign-in, so a change while signed in signs in again; the box goes back if that does not finish.
                SignInFlow(on, revert: true);
            }
        });
        codeText = look.Text(string.Empty, 24, FontWeight.Bold);
        codeText.FontFamily = Mono;
        BuildCodePanel();

        urlBox = Field(GitTokens.MaxUrl, LinuxStrings.Dev_FieldUrl);
        userBox = Field(GitTokens.MaxUser, LinuxStrings.Dev_FieldUser);
        tokenBox = Field(GitTokens.MaxToken, LinuxStrings.Dev_FieldToken);
        tokenBox.PasswordChar = (char)0x2022;
        errorText = look.Text(string.Empty, 13);
        errorText.Foreground = look.Danger;
        saveButton = look.Button(LinuxStrings.Common_Save, primary: true);
        saveButton.Click += (_, _) => SaveToken();
        var cancelForm = look.Button(LinuxStrings.Common_Cancel);
        cancelForm.Click += (_, _) => CloseForm();
        AddField(urlField, LinuxStrings.Dev_FieldUrl, urlBox);
        AddField(userField, LinuxStrings.Dev_FieldUser, userBox);
        var tokenField = new StackPanel { Spacing = 4 };
        AddField(tokenField, LinuxStrings.Dev_FieldToken, tokenBox);
        formPanel.Children.Add(urlField);
        formPanel.Children.Add(userField);
        formPanel.Children.Add(tokenField);
        formPanel.Children.Add(errorText);
        formPanel.Children.Add(Look.Footer(saveButton, cancelForm));
        formPanel.Margin = new Thickness(44, 0, 0, 0);
        foreach (var box in new[] { urlBox, userBox, tokenBox })
        {
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    SaveToken();
                }
            };
        }

        var body = new StackPanel { Margin = new Thickness(24), Spacing = 8 };
        body.Children.Add(look.Text(LinuxStrings.Integrations_ForDevelopers, 18, FontWeight.Bold));
        body.Children.Add(developerSwitch);
        body.Children.Add(look.Text(LinuxStrings.Dev_Subtitle, 13, dim: true));
        body.Children.Add(hint);
        body.Children.Add(list);
        var close = look.Button(LinuxStrings.Common_Close, primary: true);
        close.IsDefault = true;
        var footer = Look.Footer(close);
        footer.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(footer);

        window = look.Dialog(LinuxStrings.Integrations_ForDevelopers, Width, new ScrollViewer { Content = body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        window.MaxHeight = 760;
        close.Click += (_, _) => CloseFormOrWindow();
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CloseFormOrWindow();
            }
        };
        window.Activated += (_, _) => Refresh();
        window.Closed += (_, _) =>
        {
            tokenBox.Text = string.Empty;
            signInRun++;
            if (signingIn)
            {
                signIn.Cancel();
            }

            closed.TrySetResult();
        };
        Render();
    }

    /// <summary>Completes when the window has closed, however it closed.</summary>
    public Task Closed => closed.Task;

    /// <summary>Opens the window and reads the tools' states off the UI thread. Call on the UI thread.</summary>
    public void Show()
    {
        window.Show();
        _ = Guard(() => LoadAsync(opened: true));
    }

    /// <summary>Reads every row again (a hook event, a changed switch, coming back to the window). Call on the UI thread.</summary>
    public void Refresh() => _ = Guard(() => LoadAsync(opened: false));

    /// <summary>Closes the window. Call on the UI thread.</summary>
    public void Close() => window.Close();

    private static async Task Guard(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            // The window closed under it.
        }
        catch (Exception e)
        {
            // The type and message only: neither ever holds a token or a code.
            Log.Write($"The For developers window hit {e.GetType().Name}: {e.Message}");
        }
    }

    private CheckBox Check(string text, bool on, Action<bool> changed)
    {
        var box = new CheckBox { Content = text, IsChecked = on, FontFamily = Look.Lato, FontSize = 14, Foreground = look.Ink };
        box.IsCheckedChanged += (_, _) => changed(box.IsChecked == true);
        return box;
    }

    private static TextBox Field(int max, string name)
    {
        var box = new TextBox { MaxLength = max, FontFamily = Look.Lato, FontSize = 14 };
        AutomationProperties.SetName(box, name);
        return box;
    }

    private void AddField(StackPanel field, string label, TextBox box)
    {
        field.Children.Add(look.Text(label, 12, dim: true));
        field.Children.Add(box);
    }

    private void BuildCodePanel()
    {
        var open = look.Button(LinuxStrings.Integrations_OpenPage, primary: true);
        open.Click += (_, _) => Tray.OpenWithDesktop(GitHubApp.DevicePage.AbsoluteUri, "GitHub");
        var copy = look.Button(LinuxStrings.Integrations_Copy);
        copy.Click += (_, _) =>
        {
            if (userCode is { } text)
            {
                hooks.Copy(text);
            }
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(open);
        row.Children.Add(copy);
        codePanel.Margin = new Thickness(44, 0, 0, 0);
        codePanel.Children.Add(codeText);
        codePanel.Children.Add(row);
        codePanel.Children.Add(look.Text(LinuxStrings.Integrations_Waiting, 13, dim: true));
    }

    private void CloseFormOrWindow()
    {
        if (editing is not null)
        {
            CloseForm();
        }
        else
        {
            window.Close();
        }
    }

    private async Task LoadAsync(bool opened)
    {
        var read = await Task.Run(() =>
        {
            if (opened)
            {
                hooks.Opened();
            }

            return DevProviders.Of(DevKind.Ai).ToDictionary(p => p.Id, p => hooks.Agent(p.Id));
        });
        foreach (var (id, state) in read)
        {
            if (!busy.Contains(id))
            {
                states[id] = state;
            }
        }

        signedIn = signIn.IsSignedIn && settings.Flag(IntegrationSettings.GitHub);
        Render();
    }

    // ---- drawing -------------------------------------------------------------------------------------------------------

    private void Render()
    {
        var focus = tagged.FirstOrDefault(pair => pair.Value.IsFocused).Key;
        tagged.Clear();
        list.Children.Clear();
        var developerOn = settings.Flag(IntegrationSettings.Developer);
        filling = true;
        developerSwitch.IsChecked = developerOn;
        includePrivate.IsChecked = settings.Flag(IntegrationSettings.GitHubPrivate);
        filling = false;
        hint.IsVisible = !developerOn;
        foreach (var (title, kind) in new[] { (LinuxStrings.Dev_SectionAi, DevKind.Ai), (LinuxStrings.Dev_SectionGit, DevKind.Git) })
        {
            var heading = look.Text(title.ToUpper(System.Globalization.CultureInfo.CurrentCulture), 11, FontWeight.Bold, dim: true);
            heading.Margin = new Thickness(0, 10, 0, 0);
            list.Children.Add(heading);
            foreach (var provider in DevProviders.Of(kind))
            {
                AddRow(provider, developerOn);
            }
        }

        if (focus is not null && tagged.TryGetValue(focus, out var again))
        {
            again.Focus();
        }
    }

    private RowView View(DevProvider provider)
    {
        var id = provider.Id;
        if (provider.Kind == DevKind.Ai)
        {
            return DevRows.Ai(provider.Name, states.GetValueOrDefault(id), busy.Contains(id), DateTimeOffset.Now);
        }

        if (id == "github")
        {
            return DevRows.GitHub(provider.Name, GitHubApp.IsConfigured(GitHubApp.ClientId), signedIn, signingIn, userCode is not null, settings.Text(IntegrationSettings.GitHubLogin));
        }

        return id == "gitlab"
            ? DevRows.GitLab()
            : DevRows.Token(provider.Name, settings.Flag(id) && hooks.GitSignedIn(id), settings.Text(id + "Login"), saving);
    }

    private Button RowButton(DevProvider provider, string text, bool enabled, bool primary, string tag, Action click)
    {
        var button = look.Button(text, primary: primary);
        button.IsEnabled = enabled && settings.Flag(IntegrationSettings.Developer);
        AutomationProperties.SetName(button, text);
        AutomationProperties.SetHelpText(button, provider.Name);
        button.Click += (_, _) => click();
        tagged[provider.Id + tag] = button;
        return button;
    }

    private void AddRow(DevProvider provider, bool developerOn)
    {
        var view = View(provider);
        var id = provider.Id;
        var mark = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            BorderBrush = look.Line,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = provider.Name[..1],
                FontFamily = Look.Lato,
                FontWeight = FontWeight.Bold,
                Foreground = look.Ink,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var status = look.Text(view.Status, 13, dim: view.Calm);
        var statusLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (view.Dot)
        {
            statusLine.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8, Fill = look.Accent, VerticalAlignment = VerticalAlignment.Center });
        }

        statusLine.Children.Add(status);
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(look.Text(provider.Name, 14, FontWeight.Bold));
        text.Children.Add(statusLine);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(mark);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (view.Go is { } go)
        {
            var button = RowButton(provider, go, view.GoEnabled, primary: true, ":go", () => OnGo(provider));
            Grid.SetColumn(button, 2);
            grid.Children.Add(button);
        }
        else if (view.Undo is { } undo)
        {
            var button = RowButton(provider, undo, view.UndoEnabled, primary: false, ":undo", () => OnUndo(provider));
            Grid.SetColumn(button, 2);
            grid.Children.Add(button);
        }

        list.Children.Add(grid);

        var detail = new StackPanel { Spacing = 6, Margin = new Thickness(44, 0, 0, 0) };
        if (view.CanHowTo && DevRows.StepsFor(id).Length > 0)
        {
            var open = expanded.Contains(id);
            var howTo = look.Button(LinuxStrings.Dev_HowTo + (open ? " " + (char)0x2304 : " " + (char)0x203A));
            howTo.IsEnabled = developerOn;
            howTo.HorizontalAlignment = HorizontalAlignment.Left;
            AutomationProperties.SetName(howTo, LinuxStrings.Dev_HowTo);
            AutomationProperties.SetHelpText(howTo, provider.Name);
            howTo.Click += (_, _) =>
            {
                if (!expanded.Remove(id))
                {
                    expanded.Add(id);
                }

                Render();
            };
            tagged[id + ":how"] = howTo;
            detail.Children.Add(howTo);
            if (open)
            {
                detail.Children.Add(look.Text(DevRows.StepsFor(id), 13, dim: true));
            }
        }

        if (provider.Kind == DevKind.Ai && states.GetValueOrDefault(id) is { } state && !busy.Contains(id))
        {
            AddAiDetail(detail, state);
        }

        if (detail.Children.Count > 0)
        {
            list.Children.Add(detail);
        }

        if (id == "github" && !(signingIn && userCode is null))
        {
            if (signingIn && userCode is not null)
            {
                codeText.Text = Say.Format(LinuxStrings.Integrations_Code, userCode);
                AutomationProperties.SetName(codeText, codeText.Text);
                list.Children.Add(codePanel);
            }
            else if (signedIn)
            {
                if (!secrets.IsEncrypted)
                {
                    var plain = look.Text(LinuxStrings.Integrations_NotEncrypted, 13, dim: true);
                    plain.Margin = new Thickness(44, 0, 0, 0);
                    list.Children.Add(plain);
                }

                includePrivate.Margin = new Thickness(44, 0, 0, 0);
                list.Children.Add(includePrivate);
            }
        }

        if (editing == id)
        {
            urlField.IsVisible = id == "gitea";
            userField.IsVisible = id == "bitbucket";
            errorText.Text = saveError;
            errorText.IsVisible = saveError.Length > 0;
            saveButton.IsEnabled = !saving;
            list.Children.Add(formPanel);
        }

        list.Children.Add(new Border { Height = 1, Background = look.Line, Margin = new Thickness(0, 4, 0, 0) });
    }

    private void AddAiDetail(StackPanel detail, AgentState state)
    {
        if (state.Status == AgentStatus.AwaitingTrust)
        {
            detail.Children.Add(look.Text(LinuxStrings.Integrations_TrustCodex, 13));
        }

        if (state.Restart)
        {
            detail.Children.Add(look.Text(LinuxStrings.Integrations_Restart, 13, dim: true));
        }

        if (state.Commands is { Length: > 0 } commands)
        {
            detail.Children.Add(look.Text(LinuxStrings.Integrations_RunYourself, 13));
            detail.Children.Add(new TextBox
            {
                Text = commands,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = Mono,
                FontSize = 13,
                Foreground = look.Ink,
            });
            var copy = look.Button(LinuxStrings.Integrations_Copy);
            copy.HorizontalAlignment = HorizontalAlignment.Left;
            copy.Click += (_, _) => hooks.Copy(commands);
            detail.Children.Add(copy);
        }
    }

    // ---- actions -------------------------------------------------------------------------------------------------------

    private void OnGo(DevProvider provider)
    {
        switch (provider.Id)
        {
            case "github":
                SignInFlow(settings.Flag(IntegrationSettings.GitHubPrivate), revert: false);
                break;
            case "gitea" or "bitbucket" or "gitee":
                OpenForm(provider.Id);
                break;
            default:
                if (provider.Kind == DevKind.Ai)
                {
                    _ = Guard(() => RunAsync(provider.Id));
                }

                break;
        }
    }

    private void OnUndo(DevProvider provider)
    {
        switch (provider.Id)
        {
            case "github":
                if (signingIn)
                {
                    signInRun++;
                    signIn.Cancel();
                    signingIn = false;
                    userCode = null;
                    Render();
                }
                else
                {
                    _ = Guard(SignOutAsync);
                }

                break;
            case "gitea" or "bitbucket" or "gitee":
                hooks.GitSignOut(provider.Id);
                hooks.Switched(provider.Id, false);
                Render();
                break;
            default:
                if (provider.Kind == DevKind.Ai)
                {
                    _ = Guard(() => RunAsync(provider.Id));
                }

                break;
        }
    }

    /// <summary>Connect or Disconnect, whichever the row offers; the row says "Working..." until it is done.</summary>
    private async Task RunAsync(string id)
    {
        if (!busy.Add(id))
        {
            return;
        }

        var connect = !DevRows.IsConnected(states.GetValueOrDefault(id));
        Render();
        try
        {
            var state = await hooks.Connect(id, connect);
            var on = DevRows.IsConnected(state);
            _ = settings.SetFlag(id, on);
            hooks.Switched(id, on);
            states[id] = state;
        }
        finally
        {
            busy.Remove(id);
            Render();
        }
    }

    /// <summary>
    /// The device flow: ask for a code, show it with Copy and the page to type it on, and wait. A newer flow replaces an older one,
    /// whose continuation then does nothing. <paramref name="revert"/> puts the private box back when this one does not finish.
    /// </summary>
    private async void SignInFlow(bool withPrivate, bool revert)
    {
        var run = ++signInRun;
        signingIn = true;
        userCode = null;
        Render();
        var state = SignInState.Failed;
        try
        {
            var started = await signIn.StartAsync(withPrivate, CancellationToken.None);
            if (run != signInRun)
            {
                return;
            }

            state = started.State;
            if (state == SignInState.Started)
            {
                userCode = started.UserCode;
                Render();
                state = await signIn.WaitAsync(CancellationToken.None);
            }
        }
        catch (Exception e)
        {
            // An escape from an async void handler ends the tray app.
            Log.Write($"GitHub sign-in failed: {e.GetType().Name}");
        }

        if (run != signInRun)
        {
            return;
        }

        signingIn = false;
        userCode = null;
        if (state == SignInState.SignedIn)
        {
            _ = settings.SetFlag(IntegrationSettings.GitHub, true);
            _ = settings.SetFlag(IntegrationSettings.GitHubPrivate, withPrivate);
            signedIn = true;
            Render();
            await ShowLogin(run);
            hooks.Switched(IntegrationSettings.GitHub, true);
        }
        else if (revert)
        {
            _ = settings.SetFlag(IntegrationSettings.GitHubPrivate, !withPrivate);
        }

        Render();
    }

    private async Task ShowLogin(int run)
    {
        string? login = null;
        try
        {
            login = await hooks.Login(CancellationToken.None);
        }
        catch (Exception e)
        {
            Log.Write($"GitHub login name unavailable: {e.GetType().Name}");
        }

        if (run == signInRun)
        {
            _ = settings.Set(IntegrationSettings.GitHubLogin, login ?? string.Empty);
        }
    }

    private async Task SignOutAsync()
    {
        signInRun++;
        signingIn = false;
        userCode = null;
        await Task.Run(signIn.SignOut);
        signedIn = false;
        _ = settings.SetFlag(IntegrationSettings.GitHub, false);
        _ = settings.Set(IntegrationSettings.GitHubLogin, string.Empty);
        hooks.Switched(IntegrationSettings.GitHub, false);
        Render();
    }

    private void OpenForm(string id)
    {
        editing = id;
        saveError = string.Empty;
        tokenBox.Text = string.Empty;
        urlBox.Text = settings.Text(GitTokens.UrlKey(id)) is { Length: > 0 } url ? url : GiteaHost.DefaultUrl;
        userBox.Text = settings.Text(GitTokens.UserKey(id));
        expanded.Remove(id);
        Render();
        (id == "gitea" ? urlBox : id == "bitbucket" ? userBox : tokenBox).Focus();
    }

    private void CloseForm()
    {
        tokenBox.Text = string.Empty;
        editing = null;
        saveError = string.Empty;
        Render();
    }

    /// <summary>Checks the pasted token with its host and saves it; the field is emptied whatever the answer.</summary>
    private async void SaveToken()
    {
        if (editing is not { } id || saving)
        {
            return;
        }

        saving = true;
        saveError = string.Empty;
        Render();
        GitSaveResult result;
        try
        {
            result = await hooks.GitSave(id, tokenBox.Text ?? string.Empty, urlBox.Text ?? string.Empty, userBox.Text ?? string.Empty, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e)
        {
            Log.Write($"{id} token save failed: {e.GetType().Name}");
            result = new GitSaveResult(GitSaveStatus.Failed, string.Empty);
        }

        saving = false;
        tokenBox.Text = string.Empty;
        if (DevRows.SaveMessage(result.Status) is { } message)
        {
            saveError = message;
        }
        else
        {
            editing = null;
            hooks.Switched(id, true);
        }

        Render();
    }
}
