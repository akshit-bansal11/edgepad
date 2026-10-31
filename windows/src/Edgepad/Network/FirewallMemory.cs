using Edgepad.Trust;

namespace Edgepad.Network;

/// <summary>
/// <c>firewall.txt</c>: the exe the firewall step last ran for, whatever the owner answered. Each release is a new
/// <c>Edgepad-version.exe</c>, which the old rule does not name, so a changed path means the step is due once more; a
/// decline is remembered too, or it would nag at every launch. It decides only whether to ask, so a file that cannot be
/// read just counts as a different exe. <c>network.txt</c> stays out of it, because its parser refuses anything it would
/// not have written.
/// </summary>
internal sealed class FirewallMemory(string path)
{
    public static FirewallMemory ForCurrentUser(string directory) => new(Path.Combine(directory, "firewall.txt"));

    /// <summary>
    /// Whether <paramref name="exe"/> is not the <paramref name="remembered"/> one. Windows paths ignore case. An unknown
    /// exe is never new: the firewall step could not name it anyway.
    /// </summary>
    public static bool IsNew(string? remembered, string? exe) =>
        exe is not null && !string.Equals(remembered, exe, StringComparison.OrdinalIgnoreCase);

    /// <summary>The remembered exe, or null when there is none to read.</summary>
    public string? Load()
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Remembers <paramref name="exe"/>. A failure is logged and costs one more prompt next launch.</summary>
    public void Remember(string exe)
    {
        try
        {
            PrivateStorage.Replace(path, temp => File.WriteAllText(temp, exe + "\n"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"The firewall step could not be remembered in {path}: {e.Message}");
        }
    }
}
