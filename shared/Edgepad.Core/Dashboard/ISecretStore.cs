namespace Edgepad.Dashboard;

/// <summary>
/// The operating system's store for a secret such as the GitHub token: DPAPI on Windows, the Secret Service on Linux, the
/// Keychain on a Mac. None of the three throws into its caller: a failure reads as "no secret" or "not saved".
/// </summary>
internal interface ISecretStore
{
    /// <summary>The secret saved under <paramref name="name"/>; null when there is none or it cannot be read.</summary>
    string? Get(string name);

    /// <summary>Saves <paramref name="value"/> under <paramref name="name"/>, replacing any earlier one; false when it could not be saved.</summary>
    bool Set(string name, string value);

    /// <summary>Removes the secret under <paramref name="name"/>; nothing happens when there is none.</summary>
    void Delete(string name);
}
