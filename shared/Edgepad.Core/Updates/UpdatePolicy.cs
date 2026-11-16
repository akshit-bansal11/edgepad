namespace Edgepad.Updates;

public enum UpdateDecision
{
    NotNewer,
    Skipped,
    NoKey,
    Install,
    InstallNeedsMajorConfirm,
}

/// <summary>What to do with an offered release. Pure.</summary>
internal static class UpdatePolicy
{
    /// <summary>
    /// <paramref name="offered"/> must be a version <see cref="UpdateCheck.VersionIn"/> produced (three plain parts); anything
    /// else is "not newer", so nothing is installed. With no signing key the answer is to open the release page. A different
    /// first number means the phone must be updated too, so the owner confirms first.
    /// </summary>
    public static UpdateDecision Decide(Version offered, string running, Version? skipped, bool keySet)
    {
        if (offered.Build < 0 || offered.Revision >= 0
            || !Version.TryParse(running.Split('-')[0], out var ours)
            || offered <= ours)
        {
            return UpdateDecision.NotNewer;
        }

        if (skipped == offered)
        {
            return UpdateDecision.Skipped;
        }

        if (!keySet)
        {
            return UpdateDecision.NoKey;
        }

        return offered.Major != ours.Major ? UpdateDecision.InstallNeedsMajorConfirm : UpdateDecision.Install;
    }
}
