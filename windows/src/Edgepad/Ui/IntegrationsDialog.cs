using System.Globalization;
using Edgepad.Dashboard;

namespace Edgepad.Ui;

/// <summary>Where an AI tool stands with Edgepad, as the For developers window shows it.</summary>
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
/// What the Integrations and For developers windows need from the tray, which owns every service they describe. They write the
/// switches and the city to <see cref="Settings"/> themselves and call <see cref="Changed"/> with the key, so the tray can start or
/// stop what that key governs. Connect and Disconnect own an AI tool's switch (set on success, cleared on disconnect); the window only reads it.
/// </summary>
/// <param name="Status">The agent's state now (a <see cref="DevProvider.Id"/> of the AI kind).</param>
/// <param name="Connect">Installs the plugin or hooks for the agent; never throws for a failure it can report as a state.</param>
/// <param name="Disconnect">Removes it and clears the switch.</param>
/// <param name="EnsureCurrent">Re-stamps a connected plugin whose app version or Node path moved; called when the window opens.</param>
/// <param name="LastHeard">UTC minutes of the agent's last hook message, or -1.</param>
/// <param name="Login">The signed-in GitHub account's login, for display only; null when it cannot be read.</param>
/// <param name="Copy">Puts text on the laptop's clipboard without sharing it to the phone.</param>
/// <param name="Open">Opens a URL in the browser; the second argument names it for the log.</param>
/// <param name="SetDeveloper">Turns developer mode on or off (the one place it is kept is the settings; the tray reads it from there).</param>
/// <param name="GitSignedIn">Whether a token is saved for a token-pasting git host (by provider id).</param>
/// <param name="GitSave">Checks and saves a pasted token: provider id, token, server address, user name.</param>
/// <param name="GitSignOut">Forgets a git host's token (and, for GitLab, its refresh token).</param>
/// <param name="GitLab">The gitlab.com device sign-in; null where there is none (the design gallery).</param>
/// <param name="GitLabLogin">The signed-in GitLab account's login, for display only.</param>
/// <param name="GitLabExpired">Whether GitLab refused to renew the gitlab.com sign-in, so the person is asked to sign in again.</param>
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
    Action<string> Changed,
    Action<bool> SetDeveloper,
    Func<string, bool> GitSignedIn,
    Func<string, string, string, string, CancellationToken, Task<GitSaveResult>> GitSave,
    Action<string> GitSignOut,
    GitLabSignIn? GitLab = null,
    Func<CancellationToken, Task<string?>>? GitLabLogin = null,
    Func<bool>? GitLabExpired = null);

/// <summary>
/// The place the dashboard's stats, weather and city are turned on. Everything is off until the owner turns it on here. Not
/// modal, in the tray's own look (SecurityWindow's layout, FlatButton, Framed), laid out right to left in Arabic. It makes no
/// network call of its own. The AI tools and git hosts live in <see cref="DevelopersWindow"/>.
/// </summary>
internal sealed class IntegrationsWindow : Form
{
    private const int DialogWidth = 480;
    private const int Pad = 24;
    private const int Box = 36;
    private const int RowHeight = 28;

    private readonly IntegrationActions actions;
    private readonly Palette palette = Palette.Current;

    private readonly Label heading = new() { AutoSize = false, Text = Strings.Integrations_Title };
    private readonly Label intro = new() { AutoSize = false, Text = Strings.Integrations_Intro };
    private readonly CheckBox stats = Switch(Strings.Integrations_Stats);
    private readonly CheckBox weather = Switch(Strings.Integrations_Weather);
    private readonly Label cityLabel = new() { AutoSize = false, Text = Strings.Integrations_City };
    private readonly TextBox city = new() { BorderStyle = BorderStyle.None, MaxLength = IntegrationSettings.MaxCityLength, AccessibleName = Strings.Integrations_City };
    private readonly Framed cityFrame;
    private readonly Label weatherNote = new() { AutoSize = false, Text = Strings.Integrations_WeatherNote };
    private readonly FlatButton close;

    // Set while the window writes its own controls, so their Changed events do not echo into the settings.
    private bool filling;

    /// <param name="actions">What the window reads and does.</param>
    public IntegrationsWindow(IntegrationActions actions)
    {
        this.actions = actions;

        Text = Strings.Integrations_Title;
        heading.Text = Text;
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
        close.Click += (_, _) => Close();
        CancelButton = close;

        Controls.Add(heading);
        Controls.Add(close);
        Controls.AddRange([intro, stats, weather, cityLabel, cityFrame, weatherNote]);

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

    /// <summary>Reads the settings again. The tray may call it after a change made elsewhere.</summary>
    public void Reload()
    {
        filling = true;
        stats.Checked = actions.Settings.Flag(IntegrationSettings.Stats);
        weather.Checked = actions.Settings.Flag(IntegrationSettings.Weather);
        if (!city.Focused)
        {
            city.Text = actions.Settings.Text(IntegrationSettings.City);
        }

        filling = false;
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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SaveCity();
        base.OnFormClosed(e);
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

        y += S(18);
        close.SetBounds(left + width - close.FitWidth(DeviceDpi), y, close.FitWidth(DeviceDpi), S(Box));
        order.Add(close);
        var content = y + S(Box + Pad) - AutoScrollPosition.Y;

        for (var i = 0; i < order.Count; i++)
        {
            order[i].TabIndex = i;
        }

        var room = Screen.FromControl(this).WorkingArea.Height - S(64);
        var scrolls = content > room;
        AutoScrollMinSize = new Size(0, content);
        ClientSize = new Size(S(DialogWidth) + (scrolls ? SystemInformation.VerticalScrollBarWidth : 0), Math.Min(content, room));
        Invalidate();
    }
}
