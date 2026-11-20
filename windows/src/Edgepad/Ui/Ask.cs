using System.ComponentModel;

namespace Edgepad.Ui;

/// <summary>The questions and notices the app puts in dialogs, laid out right to left when the UI language reads that way.</summary>
internal static class Ask
{
    /// <summary>A notice with one button, "Close", rather than a bare "OK".</summary>
    public static void Message(IWin32Window? owner, string text, string caption, MessageBoxIcon icon)
    {
        var close = new TaskDialogButton(Strings.Common_Close);
        _ = Show(owner, caption, text, icon, close, [close], close);
    }

    /// <summary>A question whose buttons name what they do, with the second (the refusal) the default, so a stray Enter says no.</summary>
    public static bool Confirm(IWin32Window? owner, string text, string caption, MessageBoxIcon icon, string yes, string no)
    {
        var accept = new TaskDialogButton(yes);
        var refuse = new TaskDialogButton(no);
        return Show(owner, caption, text, icon, refuse, [accept, refuse], refuse) == accept;
    }

    private static TaskDialogButton? Show(
        IWin32Window? owner,
        string caption,
        string text,
        MessageBoxIcon icon,
        TaskDialogButton fallback,
        TaskDialogButton[] buttons,
        TaskDialogButton initial)
    {
        var page = new TaskDialogPage
        {
            Caption = caption,
            Text = text,
            Icon = icon switch
            {
                MessageBoxIcon.Error => TaskDialogIcon.Error,
                MessageBoxIcon.Warning => TaskDialogIcon.Warning,
                _ => TaskDialogIcon.Information,
            },
            RightToLeftLayout = Theme.IsRtl,
            AllowCancel = true,
            DefaultButton = initial,
        };
        foreach (var button in buttons)
        {
            page.Buttons.Add(button);
        }

        try
        {
            return owner is null ? TaskDialog.ShowDialog(page) : TaskDialog.ShowDialog(owner, page);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // The safe answer when the dialog cannot even be put is the refusal, and a notice has nothing to answer.
            Log.Write($"Could not show a dialog: {e.Message}");
            return fallback;
        }
    }

    /// <summary>"Cancel" / "Forget", with Cancel the default so a stray Enter forgets nothing. False when it cannot be put.</summary>
    public static bool Forget(IWin32Window owner, string heading, string text)
    {
        var forget = new TaskDialogButton(Strings.Common_Forget);
        var cancel = new TaskDialogButton(Strings.Common_Cancel);
        var page = new TaskDialogPage
        {
            Caption = Strings.Security_Title,
            Heading = heading,
            Text = text,
            Icon = TaskDialogIcon.Warning,
            RightToLeftLayout = Theme.IsRtl,
            AllowCancel = true,
            Buttons = { cancel, forget },
            DefaultButton = cancel,
        };

        try
        {
            return TaskDialog.ShowDialog(owner, page) == forget;
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // Not forgetting is the safe answer when the question cannot even be put.
            Log.Write($"Could not ask whether to forget: {e.Message}");
            return false;
        }
    }
}
