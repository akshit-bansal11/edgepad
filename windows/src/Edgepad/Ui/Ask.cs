using System.ComponentModel;

namespace Edgepad.Ui;

/// <summary>The questions and notices the app puts in dialogs, laid out right to left when the UI language reads that way.</summary>
internal static class Ask
{
    /// <summary>An OK-only message box.</summary>
    public static void Message(IWin32Window? owner, string text, string caption, MessageBoxIcon icon) =>
        _ = MessageBox.Show(
            owner,
            text,
            caption,
            MessageBoxButtons.OK,
            icon,
            MessageBoxDefaultButton.Button1,
            Theme.IsRtl ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : default);

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
