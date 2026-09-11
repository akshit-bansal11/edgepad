namespace Edgepad;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One instance per user session: a second copy would advertise a second RFCOMM service.
        using var single = new Mutex(initiallyOwned: true, @"Local\Edgepad", out var isFirst);
        if (!isFirst)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Unhandled: {e.ExceptionObject}");
        Log.Write("Edgepad starting");

        ApplicationConfiguration.Initialize();
        using var tray = new TrayContext();
        Application.Run(tray);
    }
}
