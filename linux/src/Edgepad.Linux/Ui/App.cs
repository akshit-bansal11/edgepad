using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Edgepad.Ui;

/// <summary>
/// The Avalonia application: the Fluent theme and nothing else. There is no main window; the tray, built by
/// <see cref="Shell"/> in <paramref name="ready"/>, is the whole UI, and the app lives until the Quit row calls
/// Shutdown. Built in C#, not XAML: the UI is a few dialogs, and it keeps the build free of XAML compilation.
/// </summary>
internal sealed class App(Action<App> ready) : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        try
        {
            ready(this);
        }
        catch (Exception e)
        {
            // The Bluetooth service does not depend on this: without a tray the app still serves, and trust fails closed.
            Log.Write($"The tray could not be set up: {e}");
        }

        base.OnFrameworkInitializationCompleted();
    }
}
