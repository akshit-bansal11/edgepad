namespace Edgepad.Updates;

/// <summary>What <see cref="UpdatePolicy.Decide"/> says to do with an offered release.</summary>
public enum UpdateDecision
{
    /// <summary>Not newer than the running version, or not a version a release would carry: nothing to do.</summary>
    NotNewer,

    /// <summary>The owner chose to skip this version.</summary>
    Skipped,

    /// <summary>This build has no update-signing key, so nothing can be verified: open the release page instead.</summary>
    NoKey,

    /// <summary>Download, verify and install it.</summary>
    Install,

    /// <summary>A new major version, which needs the phone updated too: the owner confirms before it is installed.</summary>
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
