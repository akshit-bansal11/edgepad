using System.ComponentModel;

namespace Edgepad.Ui;

/// <summary>The one confirmation the app asks before it throws trust away, worded the same wherever it is asked.</summary>
internal static class Ask
{
    /// <summary>"Cancel" / "Forget", with Cancel the default so a stray Enter forgets nothing. False when it cannot be put.</summary>
    public static bool Forget(IWin32Window owner, string heading, string text)
    {
        var forget = new TaskDialogButton("Forget");
        var cancel = new TaskDialogButton("Cancel");
        var page = new TaskDialogPage
        {
            Caption = "Security",
            Heading = heading,
            Text = text,
            Icon = TaskDialogIcon.Warning,
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
