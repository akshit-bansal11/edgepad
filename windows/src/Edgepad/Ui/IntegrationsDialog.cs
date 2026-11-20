using System.Globalization;
using Edgepad.Dashboard;

namespace Edgepad.Ui;

/// <summary>Where Claude Code or Codex stands with Edgepad, as the Integrations window shows it.</summary>
internal enum AgentState
{
    NotConnected,
    Connected,

    /// <summary>The agent's command-line tool is not on this computer.</summary>
    NotFound,

    /// <summary>Codex older than 0.159.</summary>
    UpdateCodex,

    /// <summary>Codex has the plugin but the owner has not trusted its hooks yet.</summary>
    AwaitingTrust,

    /// <summary>Edgepad could not run the commands; <see cref="AgentStatus.Commands"/> holds them for the owner.</summary>
    ManualSteps,
}

/// <summary>One agent's row: its state, the exact commands for the Copy box (ManualSteps only), and whether open sessions must restart.</summary>
internal sealed record AgentStatus(AgentState State, string Commands = "", bool Restart = false);

/// <summary>
/// What the Integrations window needs from the tray, which owns every service it describes. The window writes the switches and
/// the city to <see cref="Settings"/> itself and calls <see cref="Changed"/> with the key, so the tray can start or stop what that
/// key governs. Connect and Disconnect own the agent's switch (set on success, cleared on disconnect); the window only reads it.
/// </summary>
/// <param name="Status">The agent's state now (<c>claude</c> or <c>codex</c>).</param>
/// <param name="Connect">Installs the plugin for the agent; never throws for a failure it can report as a state.</param>
/// <param name="Disconnect">Removes it and clears the switch.</param>
/// <param name="EnsureCurrent">Re-stamps a connected plugin whose app version or Node path moved; called when the window opens.</param>
/// <param name="LastHeard">UTC minutes of the agent's last hook message, or -1.</param>
/// <param name="Login">The signed-in account's login, for display only; null when it cannot be read.</param>
/// <param name="Copy">Puts text on the laptop's clipboard without sharing it to the phone.</param>
/// <param name="Open">Opens a URL in the browser; the second argument names it for the log.</param>
internal sealed record IntegrationActions(
    IntegrationSettings Settings,
    GitHubSignIn SignIn,
    Func<string, AgentStatus> Status,
    Func<string, Task<AgentStatus>> Connect,
    Func<string, Task<AgentStatus>> Disconnect,
    Action EnsureCurrent,
    Func<string, long> LastHeard,
    Func<CancellationToken, Task<string?>> Login,
    Action<string> Copy,
    Action<string, string> Open,
    Action<string> Changed);

/// <summary>
/// The one place every dashboard integration is turned on: system stats, weather and its city, Claude Code, Codex and GitHub.
/// Everything is off until the owner turns it on here. Not modal, in the tray's own look (SecurityWindow's layout, FlatButton,
/// Framed), laid out right to left in Arabic. It makes no network call of its own except the GitHub sign-in the owner starts.
/// </summary>
internal sealed class IntegrationsWindow : Form
{
    private const int DialogWidth = 480;
    private const int Pad = 24;
    private const int Box = 36;
    private const int RowHeight = 28;

    private readonly IntegrationActions actions;
    private readonly Palette palette = Palette.Current;
    private readonly CancellationTokenSource closing = new();

    private readonly Label heading = new() { AutoSize = false, Text = Strings.Integrations_Title };
    private readonly Label intro = new() { AutoSize = false, Text = Strings.Integrations_Intro };
    private readonly CheckBox stats = Switch(Strings.Integrations_Stats);
    private readonly CheckBox weather = Switch(Strings.Integrations_Weather);
    private readonly Label cityLabel = new() { AutoSize = false, Text = Strings.Integrations_City };
    private readonly TextBox city = new() { BorderStyle = BorderStyle.None, MaxLength = IntegrationSettings.MaxCityLength, AccessibleName = Strings.Integrations_City };
    private readonly Framed cityFrame;
    private readonly Label weatherNote = new() { AutoSize = false, Text = Strings.Integrations_WeatherNote };
    private readonly Label developers = new() { AutoSize = false, Text = Strings.Integrations_ForDevelopers, AccessibleName = Strings.Integrations_ForDevelopers };
    private readonly AgentRow claude;
    private readonly AgentRow codex;

    private readonly Label githubHeading = new() { AutoSize = false, Text = Strings.Integrations_GitHub, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label githubStatus = new() { AutoSize = false };
    private readonly Label githubSteps = new() { AutoSize = false, UseMnemonic = false, Text = Strings.Integrations_GitHubSteps };
    private readonly Label code = new() { AutoSize = false };
    private readonly Label waiting = new() { AutoSize = false, Text = Strings.Integrations_Waiting };
    private readonly CheckBox includePrivate = Switch(Strings.Integrations_Private);
    private readonly FlatButton signIn;
    private readonly FlatButton openPage;
    private readonly FlatButton copyCode;
    private readonly FlatButton cancelSignIn;
    private readonly FlatButton close;

    // GitHub sign-in: the code while one is shown, and a count that tells a stale flow's continuation it was replaced.
    private string? userCode;
    private bool signingIn;
    private int signInRun;
    private bool signedIn;

    // Set while the window writes its own controls, so their Changed events do not echo into the settings.
    private bool filling;

    public IntegrationsWindow(IntegrationActions actions)
    {
        this.actions = actions;

        Text = Strings.Integrations_Title;
        RightToLeft = Theme.Direction;
        RightToLeftLayout = Theme.IsRtl;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        AutoScroll = true;
        BackColor = palette.Card;

        cityFrame = new Framed(city, palette, radius: 8, inset: 12);
        claude = new AgentRow(IntegrationSettings.Claude, Strings.Integrations_Claude, palette);
        codex = new AgentRow(IntegrationSettings.Codex, Strings.Integrations_Codex, palette);
        signIn = new FlatButton(Strings.Integrations_SignIn, ButtonKind.Outline, palette);
        openPage = new FlatButton(Strings.Integrations_OpenPage, ButtonKind.Primary, palette);
        copyCode = new FlatButton(Strings.Integrations_Copy, ButtonKind.Outline, palette);
        cancelSignIn = new FlatButton(Strings.Common_Cancel, ButtonKind.Ghost, palette);
        close = new FlatButton(Strings.Common_Close, ButtonKind.Outline, palette);

        stats.CheckedChanged += (_, _) => Flip(IntegrationSettings.Stats, stats.Checked);
        weather.CheckedChanged += (_, _) => Flip(IntegrationSettings.Weather, weather.Checked);
        city.Leave += (_, _) => SaveCity();
        city.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SaveCity();
            }
        };
        foreach (var row in new[] { claude, codex })
        {
            row.Action.Click += (_, _) => Run(row);
            row.Copy.Click += (_, _) => actions.Copy(row.Status.Commands);
        }

        signIn.Click += (_, _) =>
        {
            if (signedIn)
            {
                SignOut();
            }
            else
            {
                SignInFlow(actions.Settings.Flag(IntegrationSettings.GitHubPrivate), revert: false);
            }
        };
        openPage.Click += (_, _) => actions.Open(GitHubApp.DevicePage.AbsoluteUri, "the GitHub sign-in page");
        copyCode.Click += (_, _) =>
        {
            if (userCode is { } text)
            {
                actions.Copy(text);
            }
        };
        cancelSignIn.Click += (_, _) => actions.SignIn.Cancel();
        includePrivate.CheckedChanged += (_, _) =>
        {
            if (!filling)
            {
                // A different scope is a different grant: sign in again, and put the box back if that does not finish.
                SignInFlow(includePrivate.Checked, revert: true);
            }
        };
        close.Click += (_, _) => Close();
        CancelButton = close;

        Controls.AddRange(
        [
            heading, intro, stats, weather, cityLabel, cityFrame, weatherNote, developers,
            .. claude.Controls, .. codex.Controls,
            githubHeading, signIn, githubStatus, githubSteps, code, openPage, copyCode, waiting, cancelSignIn, includePrivate, close,
        ]);

        // A plugin stamped by an older Edgepad, or for a Node.js that moved, is brought up to date before its row is read.
        actions.EnsureCurrent();
        Reload();
    }

    /// <summary>
    /// The status line for an agent: connected with when it was last heard, not connected, or what stands in the way.
    /// </summary>
    internal static string StatusText(string name, AgentStatus status, long heardMinute, DateTimeOffset now) => status.State switch
    {
        AgentState.Connected => Strings.Integrations_Connected.Fill(Heard(heardMinute, now)),
        AgentState.NotFound => Strings.Integrations_NotFound.Fill(name),
        AgentState.UpdateCodex => Strings.Integrations_UpdateCodex,
        AgentState.AwaitingTrust => Strings.Integrations_WaitingTrust,
        AgentState.ManualSteps => Strings.Integrations_RunYourself,
        _ => Strings.Integrations_NotConnected,
    };

    /// <summary>"never", the local time when it was today, else the local day and month. No count, so no plural.</summary>
    internal static string Heard(long minute, DateTimeOffset now)
    {
        // DateTimeOffset's range ends in the year 9999; anything past it is no time at all.
        if (minute is < 0 or > 253_402_300_799 / 60)
        {
            return Strings.Integrations_Never;
        }

        var at = DateTimeOffset.FromUnixTimeSeconds(minute * 60).ToLocalTime();
        return at.ToString(at.Date == now.ToLocalTime().Date ? "t" : "d MMM", CultureInfo.CurrentCulture);
    }

    /// <summary>Reads every row again: the settings, the agents' states and the sign-in. The tray may call it after a hook event.</summary>
    public void Reload()
    {
        filling = true;
        stats.Checked = actions.Settings.Flag(IntegrationSettings.Stats);
        weather.Checked = actions.Settings.Flag(IntegrationSettings.Weather);
        if (!city.Focused)
        {
            city.Text = actions.Settings.Text(IntegrationSettings.City);
        }

        includePrivate.Checked = actions.Settings.Flag(IntegrationSettings.GitHubPrivate);
        filling = false;

        foreach (var row in new[] { claude, codex })
        {
            if (!row.Busy)
            {
                row.Status = actions.Status(row.Agent);
            }
        }

        signedIn = actions.Settings.Flag(IntegrationSettings.GitHub) && actions.SignIn.IsSignedIn;
        Relayout();
    }

    private int S(int value) => Theme.Scale(value, DeviceDpi);

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Reveal.Window(this);
    }

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

    /// <summary>Coming back to the window after trusting Codex or approving on GitHub shows the new state.</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Reload();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SaveCity();
        closing.Cancel();
        if (signingIn)
        {
            actions.SignIn.Cancel();
        }

        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            closing.Dispose();
        }

        base.Dispose(disposing);
    }

    private static CheckBox Switch(string text) => new() { AutoSize = false, Text = Mnemonic.Escape(text), AccessibleName = text, UseVisualStyleBackColor = false };

    private void Flip(string key, bool on)
    {
        if (filling)
        {
            return;
        }

        _ = actions.Settings.SetFlag(key, on);
        actions.Changed(key);
    }

    private void SaveCity()
    {
        var text = city.Text.Trim();
        if (text == actions.Settings.Text(IntegrationSettings.City))
        {
            return;
        }

        _ = actions.Settings.Set(IntegrationSettings.City, text);
        actions.Changed(IntegrationSettings.City);
    }

    /// <summary>Connect or Disconnect, whichever the row offers; the row says "Working…" until it is done.</summary>
    private async void Run(AgentRow row)
    {
        if (row.Busy)
        {
            return;
        }

        var connected = actions.Settings.Flag(row.Agent);
        row.Busy = true;
        Relayout();
        AgentStatus result;
        try
        {
            result = connected ? await actions.Disconnect(row.Agent) : await actions.Connect(row.Agent);
        }
        catch (Exception e)
        {
            // An escape from an async void handler ends the tray app; the row says where things stand instead.
            Log.Write($"{row.Agent} {(connected ? "disconnect" : "connect")} failed: {e.GetType().Name}");
            result = actions.Status(row.Agent);
        }

        if (IsDisposed)
        {
            return;
        }

        row.Busy = false;
        row.Status = result;
        actions.Changed(row.Agent);
        Relayout();
    }

    /// <summary>
    /// The device flow: ask for a code, show it with Copy and the page to type it on, and wait. A newer flow replaces an older
    /// one, whose continuation then does nothing. <paramref name="revert"/> puts the private box back when this one does not finish.
    /// </summary>
    private async void SignInFlow(bool withPrivate, bool revert)
    {
        var run = ++signInRun;
        signingIn = true;
        userCode = null;
        Relayout();

        var state = SignInState.Failed;
        try
        {
            var started = await actions.SignIn.StartAsync(withPrivate, closing.Token);
            if (IsDisposed || run != signInRun)
            {
                return;
            }

            state = started.State;
            if (state == SignInState.Started)
            {
                userCode = started.UserCode;
                Relayout();
                state = await actions.SignIn.WaitAsync(closing.Token);
            }
        }
        catch (Exception e)
        {
            Log.Write($"GitHub sign-in failed: {e.GetType().Name}");
        }

        if (IsDisposed || run != signInRun)
        {
            return;
        }

        signingIn = false;
        userCode = null;
        if (state == SignInState.SignedIn)
        {
            _ = actions.Settings.SetFlag(IntegrationSettings.GitHub, true);
            _ = actions.Settings.SetFlag(IntegrationSettings.GitHubPrivate, withPrivate);
            signedIn = true;
            Relayout();
            await ShowLogin(run);
            actions.Changed(IntegrationSettings.GitHub);
        }
        else if (revert)
        {
            filling = true;
            includePrivate.Checked = !withPrivate;
            filling = false;
        }

        Relayout();
    }

    private async Task ShowLogin(int run)
    {
        string? login = null;
        try
        {
            login = await actions.Login(closing.Token);
        }
        catch (Exception e)
        {
            Log.Write($"GitHub login name unavailable: {e.GetType().Name}");
        }

        if (!IsDisposed && run == signInRun)
        {
            _ = actions.Settings.Set(IntegrationSettings.GitHubLogin, login ?? string.Empty);
        }
    }

    private void SignOut()
    {
        signInRun++;
        signingIn = false;
        userCode = null;
        actions.SignIn.SignOut();
        _ = actions.Settings.SetFlag(IntegrationSettings.GitHub, false);
        _ = actions.Settings.Set(IntegrationSettings.GitHubLogin, string.Empty);
        signedIn = false;
        actions.Changed(IntegrationSettings.GitHub);
        Relayout();
    }

    private void Relayout()
    {
        var width = S(DialogWidth - (Pad * 2));
        var left = S(Pad);
        var y = S(Pad) + AutoScrollPosition.Y;
        var order = new List<Control>();

        void Put(Label label, float pixels, Weight weight, Color ink, int gap)
        {
            label.Font = Theme.Font(Theme.Scale(pixels, DeviceDpi), weight);
            label.ForeColor = ink;
            label.BackColor = palette.Card;
            var height = TextRenderer.MeasureText(label.Text, label.Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
            label.SetBounds(left, y, width, height);
            label.Visible = true;
            y += height + gap;
        }

        void Check(CheckBox box)
        {
            box.Font = Theme.Font(Theme.Scale(14f, DeviceDpi), Weight.Regular);
            box.ForeColor = palette.Ink;
            box.BackColor = palette.Card;
            box.SetBounds(left, y, width, S(RowHeight));
            box.Visible = true;
            order.Add(box);
            y += S(RowHeight);
        }

        // A section's title with its one button at the far end, as SecurityWindow lays a phone and its Forget.
        void Head(Label title, FlatButton button)
        {
            title.Font = Theme.Font(Theme.Scale(15f, DeviceDpi), Weight.Bold);
            title.ForeColor = palette.Ink;
            title.BackColor = palette.Card;
            button.SetBounds(left + width - button.FitWidth(DeviceDpi), y, button.FitWidth(DeviceDpi), S(Box));
            title.SetBounds(left, y, button.Left - left - S(8), S(Box));
            order.Add(button);
            y += S(Box + 4);
        }

        // Buttons side by side from the start edge, wrapping to a new line when the next would not fit.
        void Buttons(params FlatButton[] buttons)
        {
            var x = left;
            foreach (var button in buttons)
            {
                var w = button.FitWidth(DeviceDpi);
                if (x > left && x + w > left + width)
                {
                    x = left;
                    y += S(Box + 8);
                }

                button.SetBounds(x, y, w, S(Box));
                button.Visible = true;
                order.Add(button);
                x += w + S(8);
            }

            y += S(Box + 12);
        }

        Put(heading, 18f, Weight.Bold, palette.Ink, S(6));
        Put(intro, 14f, Weight.Regular, palette.Dim, S(16));

        Check(stats);
        y += S(8);
        Check(weather);
        Put(cityLabel, 13f, Weight.Regular, palette.Dim, S(6));
        cityFrame.Place(new Rectangle(left, y, width, S(Box)), DeviceDpi);
        order.Add(cityFrame);
        y += S(Box + 6);
        Put(weatherNote, 13f, Weight.Regular, palette.Dim, S(20));
        Put(developers, 14f, Weight.Bold, palette.Dim, S(10));

        foreach (var row in new[] { claude, codex })
        {
            var connected = actions.Settings.Flag(row.Agent);
            Words(row.Action, connected ? Strings.Integrations_Disconnect : Strings.Integrations_Connect);
            row.Action.Enabled = !row.Busy;
            Head(row.Title, row.Action);

            row.Line.Text = row.Busy
                ? Strings.Integrations_Working
                : StatusText(row.Name, row.Status, actions.LastHeard(row.Agent), DateTimeOffset.Now);
            Put(row.Line, 14f, Weight.Regular, row.Status.State is AgentState.Connected or AgentState.NotConnected || row.Busy ? palette.Dim : palette.Ink, S(6));

            var extra = new List<string>();
            if (!row.Busy && row.Status.State == AgentState.AwaitingTrust)
            {
                extra.Add(Strings.Integrations_TrustCodex);
            }

            if (!row.Busy && row.Status.Restart)
            {
                extra.Add(Strings.Integrations_Restart);
            }

            if (!row.Busy && !connected)
            {
                extra.Add(row.Agent == IntegrationSettings.Claude ? Strings.Integrations_ClaudeSteps : Strings.Integrations_CodexSteps);
            }

            row.Extra.Text = string.Join(Environment.NewLine, extra);
            row.Extra.Visible = extra.Count > 0;
            if (extra.Count > 0)
            {
                Put(row.Extra, 13f, Weight.Regular, palette.Dim, S(6));
            }

            var manual = !row.Busy && row.Status.State == AgentState.ManualSteps && row.Status.Commands.Length > 0;
            row.Frame.Visible = manual;
            row.Copy.Visible = manual;
            if (manual)
            {
                row.Commands.Text = row.Status.Commands;
                row.Commands.Font = Theme.Font(Theme.Scale(13f, DeviceDpi), Weight.Regular);
                row.Commands.ForeColor = palette.Ink;
                var inner = width - S(24);
                var height = TextRenderer.MeasureText(row.Commands.Text, row.Commands.Font, new Size(inner, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
                row.Frame.Place(new Rectangle(left, y, width, height + S(24)), DeviceDpi);
                y += height + S(24 + 8);
                Buttons(row.Copy);
            }

            y += S(10);
        }

        var configured = GitHubApp.IsConfigured(GitHubApp.ClientId);
        Words(signIn, signedIn ? Strings.Integrations_SignOut : Strings.Integrations_SignIn);
        signIn.Enabled = configured && !signingIn;
        Head(githubHeading, signIn);
        var login = actions.Settings.Text(IntegrationSettings.GitHubLogin);
        githubStatus.Text = !configured ? Strings.Integrations_NoClientId
            : signedIn ? Strings.Integrations_SignedIn.Fill(login.Length > 0 ? login : Strings.Integrations_GitHub)
            : signingIn && userCode is null ? Strings.Integrations_Working
            : Strings.Integrations_NotConnected;
        githubStatus.Visible = !(signingIn && userCode is not null);
        if (githubStatus.Visible)
        {
            Put(githubStatus, 14f, Weight.Regular, palette.Dim, S(8));
        }

        githubSteps.Visible = configured && !signedIn;
        if (githubSteps.Visible)
        {
            Put(githubSteps, 13f, Weight.Regular, palette.Dim, S(8));
        }

        var showing = signingIn && userCode is not null;
        foreach (var part in new Control[] { code, openPage, copyCode, waiting, cancelSignIn })
        {
            part.Visible = showing;
        }

        if (showing)
        {
            code.Text = Strings.Integrations_Code.Fill(userCode);
            code.AccessibleName = code.Text;
            Put(code, 24f, Weight.Bold, palette.Ink, S(10));
            Buttons(openPage, copyCode, cancelSignIn);
            Put(waiting, 13f, Weight.Regular, palette.Dim, S(8));
        }

        includePrivate.Visible = signedIn && !signingIn;
        if (includePrivate.Visible)
        {
            Check(includePrivate);
        }

        y += S(18);
        close.SetBounds(left + width - close.FitWidth(DeviceDpi), y, close.FitWidth(DeviceDpi), S(Box));
        order.Add(close);
        var content = y + S(Box + Pad) - AutoScrollPosition.Y;

        for (var i = 0; i < order.Count; i++)
        {
            order[i].TabIndex = i;
        }

        // Taller than the screen (both agents showing their commands, at a large text size): the window scrolls instead.
        var room = Screen.FromControl(this).WorkingArea.Height - S(64);
        var scrolls = content > room;
        AutoScrollMinSize = new Size(0, content);
        ClientSize = new Size(S(DialogWidth) + (scrolls ? SystemInformation.VerticalScrollBarWidth : 0), Math.Min(content, room));
        Invalidate();
    }

    /// <summary>Gives a button new words, and Narrator the same words.</summary>
    private static void Words(FlatButton button, string text)
    {
        button.Text = text;
        button.AccessibleName = Mnemonic.Plain(text);
    }

    /// <summary>One agent's controls: title and Connect or Disconnect, the status, notes, the commands box with Copy, and the Node.js link.</summary>
    private sealed class AgentRow
    {
        public AgentRow(string agent, string name, Palette palette)
        {
            Agent = agent;
            Name = name;
            Title = new Label { AutoSize = false, UseMnemonic = false, Text = name, TextAlign = ContentAlignment.MiddleLeft };
            Action = new FlatButton(Strings.Integrations_Connect, ButtonKind.Outline, palette) { AccessibleDescription = name };
            Commands = new Label { AutoSize = false, UseMnemonic = false, BackColor = palette.Card };
            Frame = new Framed(Commands, palette, radius: 8, inset: 12) { Visible = false };
            Copy = new FlatButton(Strings.Integrations_Copy, ButtonKind.Outline, palette) { Visible = false, AccessibleDescription = name };
        }

        public string Agent { get; }

        public string Name { get; }

        public Label Title { get; }

        public FlatButton Action { get; }

        public Label Line { get; } = new() { AutoSize = false };

        public Label Extra { get; } = new() { AutoSize = false, Visible = false };

        public Label Commands { get; }

        public Framed Frame { get; }

        public FlatButton Copy { get; }

        public AgentStatus Status { get; set; } = new(AgentState.NotConnected);

        public bool Busy { get; set; }

        public Control[] Controls => [Title, Action, Line, Extra, Frame, Copy];
    }
}
