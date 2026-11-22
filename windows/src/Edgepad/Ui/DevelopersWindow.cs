using System.Drawing.Drawing2D;
using System.Globalization;
using System.Reflection;
using Edgepad.Dashboard;

namespace Edgepad.Ui;

/// <summary>
/// The For developers window: the Developer mode switch, then AI tools and git hosts, one row each, built from
/// <see cref="DevProviders"/> (no screen keeps a tool list of its own). A row is a logo, a name, one status line and one button;
/// "How to connect" opens that row's setup steps under it, GitHub's sign-in code opens in a panel under its own row, and the
/// token hosts open a small inline form. Not modal, in the tray's own look, laid out right to left in Arabic.
/// </summary>
internal sealed class DevelopersWindow : Form
{
    private const int DialogWidth = 480;
    private const int Pad = 24;
    private const int RowHeight = 52;
    private const int LogoSize = 32;
    private const int TextIndent = 44;
    private const int ButtonHeight = 30;
    private const int FieldHeight = 36;
    // How to connect's arrow: down when the steps are open, else pointing along the reading direction. Built from code points
    // (U+2304, U+203A, U+2039) so no invisible or look-alike character sits in the source.
    private static readonly string ExpandedMark = char.ConvertFromUtf32(0x2304);
    private static readonly string CollapsedMark = char.ConvertFromUtf32(0x203A);
    private static readonly string CollapsedMarkRtl = char.ConvertFromUtf32(0x2039);

    private readonly IntegrationActions actions;
    private readonly Palette palette;
    private readonly CancellationTokenSource closing = new();
    private readonly List<DevRow> rows = [];
    private readonly HashSet<string> expanded = [];

    private readonly Label heading = new() { AutoSize = false, UseMnemonic = false, Text = Strings.Integrations_ForDevelopers };
    private readonly Label subtitle = new() { AutoSize = false, UseMnemonic = false, Text = Strings.Dev_Subtitle };
    private readonly Label developerHint = new() { AutoSize = false, UseMnemonic = false, Text = Strings.Dev_NeedDeveloper };
    private readonly CheckBox developerSwitch = new() { AutoSize = false, Text = Mnemonic.Escape(Strings.Menu_DeveloperMode), AccessibleName = Strings.Menu_DeveloperMode, UseVisualStyleBackColor = false };
    private readonly Label aiHeading = new() { AutoSize = false, UseMnemonic = false, Text = Strings.Dev_SectionAi.ToUpper(CultureInfo.CurrentCulture) };
    private readonly Label gitHeading = new() { AutoSize = false, UseMnemonic = false, Text = Strings.Dev_SectionGit.ToUpper(CultureInfo.CurrentCulture) };
    private readonly FlatButton close;

    // GitHub's sign-in code panel and its private-repositories box.
    private readonly Backdrop codeBackdrop;
    private readonly Label codeLabel = new() { AutoSize = false, UseMnemonic = false };
    private readonly Label waiting = new() { AutoSize = false, UseMnemonic = false, Text = Strings.Integrations_Waiting };
    private readonly FlatButton openPage;
    private readonly FlatButton copyCode;
    private readonly CheckBox includePrivate = new() { AutoSize = false, Text = Mnemonic.Escape(Strings.Integrations_Private), AccessibleName = Strings.Integrations_Private, UseVisualStyleBackColor = false };

    // The inline form a token host's Add opens (one at a time).
    private readonly Label urlLabel = new() { AutoSize = false, UseMnemonic = false, Text = Strings.Dev_FieldUrl };
    private readonly TextBox urlBox = new() { BorderStyle = BorderStyle.None, MaxLength = GitTokens.MaxUrl, AccessibleName = Strings.Dev_FieldUrl };
    private readonly Framed urlFrame;
    private readonly Label userLabel = new() { AutoSize = false, UseMnemonic = false, Text = Strings.Dev_FieldUser };
    private readonly TextBox userBox = new() { BorderStyle = BorderStyle.None, MaxLength = GitTokens.MaxUser, AccessibleName = Strings.Dev_FieldUser };
    private readonly Framed userFrame;
    private readonly Label tokenLabel = new() { AutoSize = false, UseMnemonic = false, Text = Strings.Dev_FieldToken };
    private readonly TextBox tokenBox = new() { BorderStyle = BorderStyle.None, MaxLength = GitTokens.MaxToken, UseSystemPasswordChar = true, AccessibleName = Strings.Dev_FieldToken };
    private readonly Framed tokenFrame;
    private readonly Label errorLabel = new() { AutoSize = false, UseMnemonic = false };
    private readonly FlatButton saveButton;
    private readonly FlatButton cancelForm;

    // The layout pass: where the next thing goes, what was placed (the rest is hidden) and the tab order.
    private readonly HashSet<Control> shown = [];
    private readonly List<Control> order = [];
    private int top;
    private int left;
    private int width;

    private Font? mono;
    private DevRow? editing;
    private bool saving;
    private string saveError = string.Empty;

    // GitHub sign-in: the code while one is shown, and a count that tells a stale flow's continuation it was replaced.
    private string? userCode;
    private bool signingIn;
    private int signInRun;
    private bool signedIn;

    // Set while the window writes its own controls, so their Changed events do not echo into the settings.
    private bool filling;
    private bool fitScreen = true;

    // Developer mode is on: while it is off every row is shown disabled, and the phone gets no developer data.
    private bool developerOn;

    /// <param name="actions">What the window reads and does.</param>
    /// <param name="colours">The colours; the system's light or dark set when null (the design gallery passes one).</param>
    public DevelopersWindow(IntegrationActions actions, Palette? colours = null)
    {
        this.actions = actions;
        this.palette = colours ?? Palette.Current;

        Text = Strings.Integrations_ForDevelopers;
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

        close = new FlatButton(Strings.Common_Close, ButtonKind.Outline, palette);
        openPage = new FlatButton(Strings.Integrations_OpenPage, ButtonKind.Primary, palette);
        copyCode = new FlatButton(Strings.Integrations_Copy, ButtonKind.Outline, palette);
        saveButton = new FlatButton(Strings.Common_Save, ButtonKind.Primary, palette);
        cancelForm = new FlatButton(Strings.Common_Cancel, ButtonKind.Outline, palette);
        codeBackdrop = new Backdrop(palette);
        urlFrame = new Framed(urlBox, palette, radius: 8, inset: 12);
        userFrame = new Framed(userBox, palette, radius: 8, inset: 12);
        tokenFrame = new Framed(tokenBox, palette, radius: 8, inset: 12);

        foreach (var provider in DevProviders.Of(DevKind.Ai).Concat(DevProviders.Of(DevKind.Git)))
        {
            var row = new DevRow(provider, palette);
            row.Go.Click += (_, _) => OnGo(row);
            row.Undo.Click += (_, _) => OnUndo(row);
            row.HowTo.Click += (_, _) => ToggleSteps(row);
            row.Copy.Click += (_, _) => actions.Copy(row.Status.Commands);
            rows.Add(row);
        }

        developerSwitch.CheckedChanged += (_, _) =>
        {
            if (!filling)
            {
                actions.SetDeveloper(developerSwitch.Checked);
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
        includePrivate.CheckedChanged += (_, _) =>
        {
            if (!filling)
            {
                // A different scope is a different grant: sign in again, and put the box back if that does not finish.
                SignInFlow(includePrivate.Checked, revert: true);
            }
        };
        saveButton.Click += (_, _) => SaveToken();
        cancelForm.Click += (_, _) => CloseForm();
        foreach (var box in new[] { urlBox, userBox, tokenBox })
        {
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    SaveToken();
                }
            };
        }

        // Escape closes the form first and the window second.
        close.Click += (_, _) =>
        {
            if (editing is not null)
            {
                CloseForm();
            }
            else
            {
                Close();
            }
        };
        CancelButton = close;

        Controls.Add(heading);
        Controls.Add(developerSwitch);
        Controls.Add(subtitle);
        Controls.Add(developerHint);
        Controls.Add(aiHeading);
        Controls.Add(gitHeading);
        foreach (var row in rows)
        {
            Controls.AddRange(row.Controls);
        }

        Controls.AddRange([codeLabel, waiting, openPage, copyCode, includePrivate, urlLabel, urlFrame, userLabel, userFrame, tokenLabel, tokenFrame, errorLabel, saveButton, cancelForm, close]);

        // Last, so the panel sits behind the labels drawn on it (a control added later is further back).
        Controls.Add(codeBackdrop);

        // A plugin stamped by an older Edgepad, or for a Node.js that moved, is brought up to date before its row is read.
        actions.EnsureCurrent();
        Reload();
    }

    /// <summary>The setup steps a provider's "How to connect" opens; empty for a provider with none (GitLab until its sign-in ships).</summary>
    internal static string StepsFor(string id) => id switch
    {
        "claude" => Strings.Integrations_ClaudeSteps,
        "codex" => Strings.Integrations_CodexSteps,
        "gemini" => Strings.Dev_GeminiSteps,
        "opencode" => Strings.Dev_OpenCodeSteps,
        "github" => Strings.Integrations_GitHubSteps,
        "gitea" => Strings.Dev_GiteaSteps,
        "bitbucket" => Strings.Dev_BitbucketSteps,
        "gitee" => Strings.Dev_GiteeSteps,
        _ => string.Empty,
    };

    /// <summary>The sentence for a failed token save; null for <see cref="GitSaveStatus.Saved"/>.</summary>
    internal static string? SaveMessage(GitSaveStatus status) => status switch
    {
        GitSaveStatus.Saved => null,
        GitSaveStatus.BadToken or GitSaveStatus.BadUrl or GitSaveStatus.BadUser => Strings.Dev_SaveInvalid,
        GitSaveStatus.Unauthorized => Strings.Dev_SaveUnauthorized,
        GitSaveStatus.Forbidden => Strings.Dev_SaveForbidden,
        GitSaveStatus.NotFound => Strings.Dev_SaveNotFound,
        GitSaveStatus.Network => Strings.Dev_SaveNetwork,
        _ => Strings.Dev_SaveFailed,
    };

    /// <summary>For the design gallery: shows GitHub's sign-in panel with this code, without asking GitHub for one.</summary>
    internal void PreviewCode(string code)
    {
        signingIn = true;
        userCode = code;
        Relayout();
    }

    /// <summary>
    /// For the design gallery: the window as a bitmap, without showing it. A form that was never shown creates no child handles
    /// of its own, so DrawToBitmap on it paints an empty client area; each placed child is drawn at its place instead, back to front.
    /// </summary>
    internal Bitmap Render()
    {
        _ = Handle;
        var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(BackColor);
        for (var i = Controls.Count - 1; i >= 0; i--)
        {
            var control = Controls[i];
            if (shown.Contains(control))
            {
                using var part = new Bitmap(control.Width, control.Height);
                control.DrawToBitmap(part, new Rectangle(Point.Empty, control.Size));
                g.DrawImageUnscaled(part, control.Location);
            }
        }

        return bitmap;
    }

    /// <summary>For the design gallery: sizes the window to all its content, whatever the screen.</summary>
    internal void ShowInFull()
    {
        fitScreen = false;
        Relayout();
    }

    /// <summary>Reads every row again: the settings, the tools' states and the sign-in. The tray may call it after a hook event.</summary>
    public void Reload()
    {
        filling = true;
        developerSwitch.Checked = actions.Settings.Flag(IntegrationSettings.Developer);
        includePrivate.Checked = actions.Settings.Flag(IntegrationSettings.GitHubPrivate);
        filling = false;

        foreach (var row in rows)
        {
            if (row.Provider.Kind == DevKind.Ai && !row.Busy)
            {
                row.Status = actions.Status(row.Provider.Id);
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
        tokenBox.Clear();
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
            mono?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static string Mark(bool open) => open ? ExpandedMark : Theme.IsRtl ? CollapsedMarkRtl : CollapsedMark;

    private void ToggleSteps(DevRow row)
    {
        if (!expanded.Remove(row.Provider.Id))
        {
            expanded.Add(row.Provider.Id);
        }

        Relayout();
    }

    private void OnGo(DevRow row)
    {
        switch (row.Provider.Id)
        {
            case "github":
                SignInFlow(actions.Settings.Flag(IntegrationSettings.GitHubPrivate), revert: false);
                break;
            case "gitea" or "bitbucket" or "gitee":
                OpenForm(row);
                break;
            default:
                if (row.Provider.Kind == DevKind.Ai)
                {
                    Run(row);
                }

                break;
        }
    }

    private void OnUndo(DevRow row)
    {
        switch (row.Provider.Id)
        {
            case "github":
                if (signingIn)
                {
                    actions.SignIn.Cancel();
                }
                else
                {
                    SignOut();
                }

                break;
            case "gitea" or "bitbucket" or "gitee":
                actions.GitSignOut(row.Provider.Id);
                actions.Changed(row.Provider.Id);
                Relayout();
                break;
            default:
                if (row.Provider.Kind == DevKind.Ai)
                {
                    Run(row);
                }

                break;
        }
    }

    /// <summary>Connect or Disconnect, whichever the row offers; the row says "Working…" until it is done.</summary>
    private async void Run(DevRow row)
    {
        if (row.Busy)
        {
            return;
        }

        var id = row.Provider.Id;
        var connected = actions.Settings.Flag(id);
        row.Busy = true;
        Relayout();
        AgentStatus result;
        try
        {
            result = connected ? await actions.Disconnect(id) : await actions.Connect(id);
        }
        catch (Exception e)
        {
            // An escape from an async void handler ends the tray app; the row says where things stand instead.
            Log.Write($"{id} {(connected ? "disconnect" : "connect")} failed: {e.GetType().Name}");
            result = actions.Status(id);
        }

        if (IsDisposed)
        {
            return;
        }

        row.Busy = false;
        row.Status = result;
        actions.Changed(id);
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

    private void OpenForm(DevRow row)
    {
        var id = row.Provider.Id;
        editing = row;
        saveError = string.Empty;
        tokenBox.Clear();
        urlBox.Text = actions.Settings.Text(GitTokens.UrlKey(id)) is { Length: > 0 } url ? url : GiteaHost.DefaultUrl;
        userBox.Text = actions.Settings.Text(GitTokens.UserKey(id));
        expanded.Remove(id);
        Relayout();
        (id == "gitea" ? urlBox : id == "bitbucket" ? userBox : tokenBox).Focus();
    }

    private void CloseForm()
    {
        tokenBox.Clear();
        editing = null;
        saveError = string.Empty;
        Relayout();
    }

    /// <summary>Checks the pasted token with its host and saves it; the field is emptied whatever the answer.</summary>
    private async void SaveToken()
    {
        if (editing is not { } row || saving)
        {
            return;
        }

        var id = row.Provider.Id;
        saving = true;
        saveError = string.Empty;
        Relayout();
        GitSaveResult result;
        try
        {
            result = await actions.GitSave(id, tokenBox.Text, urlBox.Text, userBox.Text, closing.Token);
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

        if (IsDisposed)
        {
            return;
        }

        saving = false;
        tokenBox.Clear();
        if (SaveMessage(result.Status) is { } message)
        {
            saveError = message;
        }
        else
        {
            editing = null;
            actions.Changed(id);
        }

        Relayout();
    }

    private RowView View(DevRow row)
    {
        var provider = row.Provider;
        var id = provider.Id;
        if (provider.Kind == DevKind.Ai)
        {
            var connected = actions.Settings.Flag(id);
            var line = row.Busy ? Strings.Integrations_Working : IntegrationsWindow.StatusText(provider.Name, row.Status, actions.LastHeard(id), DateTimeOffset.Now);
            return new RowView(
                line,
                Dot: !row.Busy && row.Status.State == AgentState.Connected,
                Calm: row.Busy || row.Status.State is AgentState.Connected or AgentState.NotConnected,
                Go: connected ? null : Strings.Integrations_Connect,
                GoEnabled: !row.Busy,
                Undo: connected ? Strings.Integrations_Disconnect : null,
                UndoEnabled: !row.Busy,
                CanHowTo: !connected && !row.Busy);
        }

        if (id == "github")
        {
            var configured = GitHubApp.IsConfigured(GitHubApp.ClientId);
            var login = actions.Settings.Text(IntegrationSettings.GitHubLogin);
            var line = !configured ? Strings.Integrations_NoClientId
                : signedIn ? Strings.Integrations_SignedIn.Fill(login.Length > 0 ? login : provider.Name)
                : signingIn && userCode is null ? Strings.Integrations_Working
                : Strings.Integrations_NotConnected;
            return new RowView(
                line,
                Dot: signedIn,
                Calm: !signedIn,
                Go: signedIn || signingIn ? null : Strings.Dev_SignIn,
                GoEnabled: configured,
                Undo: signedIn ? Strings.Integrations_SignOut : signingIn ? Strings.Common_Cancel : null,
                UndoEnabled: true,
                CanHowTo: configured && !signedIn && !signingIn);
        }

        if (id == "gitlab")
        {
            // Device sign-in for GitLab needs a registered client ID (C22); until then the row says so and does nothing.
            return new RowView(Strings.Integrations_NotConnected, Dot: false, Calm: true, Go: Strings.Dev_ComingSoon, GoEnabled: false, Undo: null, UndoEnabled: false, CanHowTo: false);
        }

        var tokenSignedIn = actions.GitSignedIn(id);
        var name = actions.Settings.Text(id + "Login");
        return new RowView(
            tokenSignedIn ? Strings.Integrations_SignedIn.Fill(name.Length > 0 ? name : provider.Name) : Strings.Integrations_NotConnected,
            Dot: tokenSignedIn,
            Calm: !tokenSignedIn,
            Go: tokenSignedIn ? null : Strings.Dev_Add,
            GoEnabled: !saving,
            Undo: tokenSignedIn ? Strings.Integrations_SignOut : null,
            UndoEnabled: true,
            CanHowTo: !tokenSignedIn);
    }

    private static int Measure(string text, Font font, int wide) =>
        TextRenderer.MeasureText(text, font, new Size(wide, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;

    private static void Words(FlatButton button, string text, string name)
    {
        button.Text = text;
        button.AccessibleName = Mnemonic.Plain(text);
        button.AccessibleDescription = name;
    }

    private void Place(Control control, int x, int y, int wide, int high)
    {
        control.SetBounds(x, y, wide, high);
        control.Visible = true;
        _ = shown.Add(control);
    }

    private void Put(Label label, int x, int wide, float pixels, Weight weight, Color ink, Color back, int gap)
    {
        label.Font = Theme.Font(Theme.Scale(pixels, DeviceDpi), weight);
        label.ForeColor = ink;
        label.BackColor = back;
        var high = Measure(label.Text, label.Font, wide);
        Place(label, x, top, wide, high);
        top += high + gap;
    }

    // Buttons side by side from the start edge, wrapping to a new line when the next would not fit.
    private void Buttons(int x, int wide, params FlatButton[] buttons)
    {
        var at = x;
        foreach (var button in buttons)
        {
            var w = button.FitWidth(DeviceDpi);
            if (at > x && at + w > x + wide)
            {
                at = x;
                top += S(ButtonHeight + 8);
            }

            Place(button, at, top, w, S(ButtonHeight));
            order.Add(button);
            at += w + S(8);
        }

        top += S(ButtonHeight + 10);
    }

    private void Relayout()
    {
        width = S(DialogWidth - (Pad * 2));
        left = S(Pad);
        top = S(Pad) + AutoScrollPosition.Y;
        developerOn = actions.Settings.Flag(IntegrationSettings.Developer);
        shown.Clear();
        order.Clear();

        heading.Font = Theme.Font(Theme.Scale(18f, DeviceDpi), Weight.Bold);
        heading.ForeColor = palette.Ink;
        heading.BackColor = palette.Card;
        developerSwitch.Font = Theme.Font(Theme.Scale(14f, DeviceDpi), Weight.Regular);
        developerSwitch.ForeColor = palette.Ink;
        developerSwitch.BackColor = palette.Card;
        var switchWidth = TextRenderer.MeasureText(developerSwitch.Text, developerSwitch.Font, Size.Empty, TextFormatFlags.NoPadding).Width + S(44);
        Place(heading, left, top, width - switchWidth - S(8), S(32));
        Place(developerSwitch, left + width - switchWidth, top, switchWidth, S(32));
        order.Add(developerSwitch);
        top += S(36);
        Put(subtitle, left, width, 13f, Weight.Regular, palette.Dim, palette.Card, S(developerOn ? 14 : 6));
        if (!developerOn)
        {
            Put(developerHint, left, width, 13f, Weight.Bold, palette.Ink, palette.Card, S(14));
        }

        foreach (var (title, kind) in new[] { (aiHeading, DevKind.Ai), (gitHeading, DevKind.Git) })
        {
            Put(title, left, width, 11f, Weight.Bold, palette.Dim, palette.Card, S(6));
            var group = rows.Where(r => r.Provider.Kind == kind).ToList();
            foreach (var row in group)
            {
                PlaceRow(row, separator: row != group[^1]);
            }

            top += S(10);
        }

        top += S(8);
        var closeWidth = close.FitWidth(DeviceDpi);
        Place(close, left + width - closeWidth, top, closeWidth, S(36));
        order.Add(close);
        var content = top + S(36 + Pad) - AutoScrollPosition.Y;

        // Everything not placed this pass is hidden, so a row that lost its Copy or its steps shows nothing of them.
        foreach (Control control in Controls)
        {
            if (!shown.Contains(control) && control != codeBackdrop)
            {
                control.Visible = false;
            }
        }

        codeBackdrop.Visible = shown.Contains(codeBackdrop);
        for (var i = 0; i < order.Count; i++)
        {
            order[i].TabIndex = i;
        }

        // Taller than the screen (a form and steps open at a large text size): the window scrolls instead.
        var room = fitScreen ? Screen.FromControl(this).WorkingArea.Height - S(64) : int.MaxValue;
        var scrolls = content > room;
        AutoScrollMinSize = new Size(0, content);
        ClientSize = new Size(S(DialogWidth) + (scrolls ? SystemInformation.VerticalScrollBarWidth : 0), Math.Min(content, room));
        Invalidate();
    }

    private void PlaceRow(DevRow row, bool separator)
    {
        var view = View(row);
        var provider = row.Provider;
        var rowTop = top;
        var logo = S(LogoSize);
        Place(row.Logo, left, rowTop + ((S(RowHeight) - logo) / 2), logo, logo);

        FlatButton? button = null;
        if (view.Go is { } go)
        {
            Words(row.Go, go, provider.Name);
            row.Go.Enabled = view.GoEnabled;
            button = row.Go;
        }
        else if (view.Undo is { } undo)
        {
            Words(row.Undo, undo, provider.Name);
            row.Undo.Enabled = view.UndoEnabled;
            button = row.Undo;
        }

        if (button is not null && !developerOn)
        {
            button.Enabled = false;
        }

        var buttonWidth = button?.FitWidth(DeviceDpi) ?? 0;
        var textX = left + S(TextIndent);
        var textWidth = width - S(TextIndent) - (buttonWidth > 0 ? buttonWidth + S(12) : 0);

        row.Name.Font = Theme.Font(Theme.Scale(14f, DeviceDpi), Weight.Bold);
        row.Name.ForeColor = palette.Ink;
        row.Name.BackColor = palette.Card;
        Place(row.Name, textX, rowTop + S(6), textWidth, S(20));

        var statusX = textX + (view.Dot ? S(14) : 0);
        row.Line.Font = Theme.Font(Theme.Scale(12f, DeviceDpi), Weight.Regular);
        row.Line.ForeColor = view.Calm ? palette.Dim : palette.Ink;
        row.Line.BackColor = palette.Card;
        row.Line.Text = view.Status;
        var howToWidth = 0;
        if (view.CanHowTo)
        {
            row.HowTo.Text = Strings.Dev_HowTo + " " + Mark(expanded.Contains(provider.Id));
            row.HowTo.AccessibleName = Strings.Dev_HowTo;
            row.HowTo.AccessibleDescription = provider.Name;
            row.HowTo.Enabled = developerOn;
            howToWidth = row.HowTo.FitWidth(DeviceDpi);
        }

        var room = Math.Max(textWidth - (statusX - textX) - howToWidth, S(80));
        var natural = TextRenderer.MeasureText(view.Status, row.Line.Font, Size.Empty, TextFormatFlags.NoPadding).Width + S(2);
        var statusWidth = Math.Min(natural, room);
        var statusHeight = Math.Max(S(18), Measure(view.Status, row.Line.Font, statusWidth));
        if (view.Dot)
        {
            row.Dot.BackColor = palette.Card;
            Place(row.Dot, textX, rowTop + S(26) + ((S(18) - S(8)) / 2), S(8), S(8));
        }

        Place(row.Line, statusX, rowTop + S(26), statusWidth, statusHeight);
        if (view.CanHowTo)
        {
            Place(row.HowTo, statusX + statusWidth + S(2), rowTop + S(23), howToWidth, S(24));
            order.Add(row.HowTo);
        }

        if (button is not null)
        {
            Place(button, left + width - buttonWidth, rowTop + ((S(RowHeight) - S(ButtonHeight)) / 2), buttonWidth, S(ButtonHeight));
            order.Add(button);
        }

        top = rowTop + S(RowHeight) + Math.Max(0, statusHeight - S(18));

        var detailX = left + S(TextIndent);
        var detailWidth = width - S(TextIndent);
        if (view.CanHowTo && expanded.Contains(provider.Id) && StepsFor(provider.Id) is { Length: > 0 } steps)
        {
            row.Steps.Text = steps;
            Put(row.Steps, detailX, detailWidth, 13f, Weight.Regular, palette.Dim, palette.Card, S(8));
        }

        var extra = new List<string>();
        var manual = !row.Busy && row.Status.State == AgentState.ManualSteps && row.Status.Commands.Length > 0;
        if (!row.Busy && row.Status.State == AgentState.AwaitingTrust)
        {
            extra.Add(Strings.Integrations_TrustCodex);
        }

        if (!row.Busy && row.Status.Restart)
        {
            extra.Add(Strings.Integrations_Restart);
        }

        if (manual)
        {
            extra.Add(row.Status.Commands);
        }

        if (extra.Count > 0)
        {
            row.Extra.Text = string.Join(Environment.NewLine, extra);
            Put(row.Extra, detailX, detailWidth, 13f, Weight.Regular, palette.Ink, palette.Card, S(6));
        }

        if (manual)
        {
            row.Copy.Enabled = developerOn;
            Place(row.Copy, detailX, top, row.Copy.FitWidth(DeviceDpi), S(ButtonHeight));
            order.Add(row.Copy);
            top += S(ButtonHeight + 8);
        }

        if (provider.Id == "github")
        {
            if (signingIn && userCode is not null)
            {
                PlaceCode(detailX, detailWidth);
            }

            if (signedIn && !signingIn)
            {
                includePrivate.Font = Theme.Font(Theme.Scale(13f, DeviceDpi), Weight.Regular);
                includePrivate.ForeColor = palette.Ink;
                includePrivate.BackColor = palette.Card;
                Place(includePrivate, detailX, top, detailWidth, S(28));
                order.Add(includePrivate);
                top += S(32);
            }
        }

        if (editing == row)
        {
            PlaceForm(row, detailX, detailWidth);
        }

        if (separator)
        {
            Place(row.Separator, left, top, width, 1);
            top += S(2);
        }
    }

    private void PlaceCode(int x, int wide)
    {
        codeLabel.Text = Strings.Integrations_Code.Fill(userCode);
        codeLabel.AccessibleName = codeLabel.Text;
        var font = new Font("Consolas", Theme.Scale(20f, DeviceDpi), FontStyle.Bold, GraphicsUnit.Pixel);
        codeLabel.Font = font;
        mono?.Dispose();
        mono = font;
        codeLabel.ForeColor = palette.Ink;
        codeLabel.BackColor = palette.Faint;
        waiting.Font = Theme.Font(Theme.Scale(12f, DeviceDpi), Weight.Regular);
        waiting.ForeColor = palette.Dim;
        waiting.BackColor = palette.Faint;

        var inner = wide - S(24);
        var codeHeight = Measure(codeLabel.Text, font, inner);
        var waitHeight = Measure(waiting.Text, waiting.Font, inner);
        var panel = S(10) + codeHeight + S(4) + waitHeight + S(10);
        Place(codeBackdrop, x, top, wide, panel);
        Place(codeLabel, x + S(12), top + S(10), inner, codeHeight);
        Place(waiting, x + S(12), top + S(14) + codeHeight, inner, waitHeight);
        top += panel + S(8);
        Buttons(x, wide, openPage, copyCode);
    }

    private void PlaceForm(DevRow row, int x, int wide)
    {
        void Field(Label label, Framed frame)
        {
            Put(label, x, wide, 12f, Weight.Regular, palette.Dim, palette.Card, S(4));
            frame.Place(new Rectangle(x, top, wide, S(FieldHeight)), DeviceDpi);
            frame.Visible = true;
            _ = shown.Add(frame);
            order.Add(frame);
            top += S(FieldHeight + 10);
        }

        if (row.Provider.Id == "gitea")
        {
            Field(urlLabel, urlFrame);
        }

        if (row.Provider.Id == "bitbucket")
        {
            Field(userLabel, userFrame);
        }

        Field(tokenLabel, tokenFrame);
        if (saveError.Length > 0)
        {
            errorLabel.Text = saveError;
            Put(errorLabel, x, wide, 12f, Weight.Regular, palette.Danger, palette.Card, S(8));
        }

        saveButton.Enabled = !saving;
        Words(saveButton, Strings.Common_Save, row.Provider.Name);
        Words(cancelForm, Strings.Common_Cancel, row.Provider.Name);
        Buttons(x, wide, saveButton, cancelForm);
    }

    /// <summary>What one row shows: its status line, the button it offers (one of Go or Undo) and whether it has setup steps.</summary>
    private sealed record RowView(string Status, bool Dot, bool Calm, string? Go, bool GoEnabled, string? Undo, bool UndoEnabled, bool CanHowTo);

    /// <summary>One provider's controls: logo, name, status line with its dot, the How to connect link, the two buttons, steps, notes and Copy.</summary>
    private sealed class DevRow
    {
        public DevRow(DevProvider provider, Palette palette)
        {
            Provider = provider;
            Logo = new LogoBox(provider, palette);
            Name = new Label { AutoSize = false, UseMnemonic = false, Text = provider.Name, TextAlign = ContentAlignment.MiddleLeft };
            Dot = new Dot(palette.Card);
            Line = new Label { AutoSize = false, UseMnemonic = false };
            HowTo = new FlatButton(Strings.Dev_HowTo, ButtonKind.Ghost, palette);
            Go = new FlatButton(Strings.Integrations_Connect, ButtonKind.Primary, palette);
            Undo = new FlatButton(Strings.Integrations_Disconnect, ButtonKind.Outline, palette);
            Copy = new FlatButton(Strings.Integrations_Copy, ButtonKind.Outline, palette) { AccessibleDescription = provider.Name };
            Steps = new Label { AutoSize = false, UseMnemonic = false };
            Extra = new Label { AutoSize = false, UseMnemonic = false };
            Separator = new Panel { BackColor = palette.Line, TabStop = false };
        }

        public DevProvider Provider { get; }

        public LogoBox Logo { get; }

        public Label Name { get; }

        public Dot Dot { get; }

        public Label Line { get; }

        public FlatButton HowTo { get; }

        public FlatButton Go { get; }

        public FlatButton Undo { get; }

        public FlatButton Copy { get; }

        public Label Steps { get; }

        public Label Extra { get; }

        public Panel Separator { get; }

        public AgentStatus Status { get; set; } = new(AgentState.NotConnected);

        public bool Busy { get; set; }

        public Control[] Controls => [Logo, Name, Dot, Line, HowTo, Go, Undo, Copy, Steps, Extra, Separator];
    }

    /// <summary>The green dot before a connected status line.</summary>
    private sealed class Dot : Control
    {
        public Dot(Color back)
        {
            TabStop = false;
            BackColor = back;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Theme.Connected);
            e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>A rounded faint panel, behind the sign-in code.</summary>
    private sealed class Backdrop : Control
    {
        private readonly Palette palette;

        public Backdrop(Palette palette)
        {
            this.palette = palette;
            TabStop = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(palette.Card);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var shape = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Theme.Scale(8f, DeviceDpi));
            using var fill = new SolidBrush(palette.Faint);
            e.Graphics.FillPath(fill, shape);
        }
    }

    /// <summary>
    /// A provider's logo in a 32 dp rounded tile: GitHub's mark drawn from its path, an official mark from the PNG the icons job
    /// renders (<see cref="Logos"/>) when the build has one, otherwise a tile in the provider's brand colour with its first letter.
    /// </summary>
    private sealed class LogoBox : Control
    {
        private readonly DevProvider provider;
        private readonly Palette palette;

        public LogoBox(DevProvider provider, Palette palette)
        {
            this.provider = provider;
            this.palette = palette;
            TabStop = false;
            AccessibleName = provider.Name;
            AccessibleRole = AccessibleRole.Graphic;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(palette.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var box = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            var inset = Theme.Scale(6f, DeviceDpi);
            var inner = RectangleF.Inflate(box, -inset, -inset);
            using var tile = Theme.Rounded(box, Theme.Scale(8f, DeviceDpi));
            if (provider.LogoId == "github")
            {
                using var back = new SolidBrush(palette.Faint);
                g.FillPath(back, tile);
                Icons.Fill(g, Icons.GitHubMark, inner, palette.Ink, 16f);
            }
            else if (Logos.Find(provider.LogoId, palette.IsDark) is { } picture)
            {
                using var back = new SolidBrush(palette.Faint);
                g.FillPath(back, tile);
                g.DrawImage(picture, inner);
            }
            else
            {
                using var back = new SolidBrush(Logos.Brand(provider.LogoId, palette.Accent));
                g.FillPath(back, tile);
                var font = Theme.Font(Theme.Scale(16f, DeviceDpi), Weight.Bold);
                TextRenderer.DrawText(g, provider.Name[..1].ToUpper(CultureInfo.CurrentCulture), font, Rectangle.Round(box), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    /// <summary>The official marks the icons job renders from assets/brand/logos (embedded as <c>Edgepad.Assets.logo-&lt;name&gt;.png</c>), and the brand colours of the letter tiles.</summary>
    private static class Logos
    {
        private static readonly Dictionary<string, Bitmap?> Cache = [];

        /// <summary>The PNG for the logo (the dark or light variant first, then the plain one), or null when this build has none.</summary>
        public static Bitmap? Find(string logoId, bool dark)
        {
            foreach (var name in new[] { logoId + (dark ? "-dark" : "-light"), logoId })
            {
                if (!Cache.TryGetValue(name, out var picture))
                {
                    picture = Load(name);
                    Cache[name] = picture;
                }

                if (picture is not null)
                {
                    return picture;
                }
            }

            return null;
        }

        public static Color Brand(string logoId, Color fallback) => logoId switch
        {
            "claude" => Color.FromArgb(0xD9, 0x77, 0x57),
            "openai" => Color.FromArgb(0x10, 0xA3, 0x7F),
            "gemini" => Color.FromArgb(0x42, 0x85, 0xF4),
            "opencode" => Color.FromArgb(0x4B, 0x4B, 0x4B),
            "gitlab" => Color.FromArgb(0xFC, 0x6D, 0x26),
            "gitea" => Color.FromArgb(0x60, 0x99, 0x26),
            "bitbucket" => Color.FromArgb(0x26, 0x84, 0xFF),
            "gitee" => Color.FromArgb(0xC7, 0x1D, 0x23),
            _ => fallback,
        };

        private static Bitmap? Load(string name)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Edgepad.Assets.logo-{name}.png");
            if (stream is null)
            {
                return null;
            }

            // Copied, because a Bitmap made from a stream needs the stream for as long as it lives.
            using var raw = new Bitmap(stream);
            return new Bitmap(raw);
        }
    }
}
