using System.Globalization;
using Edgepad.Trust;

namespace Edgepad.Net;

/// <summary>What <c>network.txt</c> says: whether the laptop listens for Wi-Fi and USB phones, and on which port.</summary>
internal sealed record NetworkConfig(bool Enabled, int Port);

/// <summary>
/// <c>network.txt</c>: <c>enabled=0|1</c> and <c>port=</c>. Nothing here ever means on without the file saying so: no
/// file is off too. "On by default" is the first-launch question alone, put when <see cref="HasChoice"/> is false,
/// because phones found nothing while the owner had to go looking for the switch. Read strictly, like
/// <c>security.txt</c>: a file that cannot be read, or holds anything Edgepad would not have written, leaves the network
/// link OFF (fail closed) and never changes the file, so the owner can see what is wrong. Bluetooth does not read this
/// file.
/// </summary>
internal sealed class NetworkSettings(string path)
{
    /// <summary>Chosen, not checked against any registry.</summary>
    public const int DefaultPort = 47321;

    private static readonly NetworkConfig Off = new(false, DefaultPort);

    private readonly Lock gate = new();

    public static NetworkSettings ForCurrentUser(string directory) => new(Path.Combine(directory, "network.txt"));

    /// <summary>
    /// True once the file exists, whatever it holds: a choice has been saved, or something there needs the owner's
    /// eye. False is the only reason to put the first-launch question. A folder in its place counts as there.
    /// </summary>
    public bool HasChoice => Path.Exists(path);

    public NetworkConfig Load()
    {
        lock (gate)
        {
            return Read();
        }
    }

    /// <summary>Turns the link on or off, keeping the port. False, logged, when the file cannot be written.</summary>
    public bool SetEnabled(bool enabled)
    {
        lock (gate)
        {
            var port = Read().Port;
            try
            {
                PrivateStorage.Replace(path, temp => File.WriteAllText(temp, $"enabled={(enabled ? 1 : 0)}\nport={port}\n"));
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Write($"The network settings could not be saved to {path}: {e.Message}");
                return false;
            }
        }
    }

    private NetworkConfig Read()
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return Off;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The network settings could not be read from {path}, so Wi-Fi and USB phones are refused: {e.Message}");
            return Off;
        }

        var enabled = false;
        var port = DefaultPort;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = line.Split('=', 2, StringSplitOptions.TrimEntries);
            var known = pair.Length == 2 && pair[0] switch
            {
                "enabled" => (enabled = pair[1] == "1") || pair[1] == "0",
                "port" => int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535,
                _ => false,
            };
            if (!known)
            {
                Log.Write($"The network settings in {path} hold something Edgepad did not write, so Wi-Fi and USB phones are refused");
                return Off;
            }
        }

        return new NetworkConfig(enabled, port);
    }
}
