namespace Edgepad.Ui;

/// <summary>
/// Windows access-key text: an ampersand marks the letter after it as the key, and a doubled one is a literal
/// ampersand. The controls already act on the key; this is what Narrator is told (the label without the marks) and
/// how a label that is not ours to mark is kept from being read as one.
/// </summary>
internal static class Mnemonic
{
    /// <summary>The label as a screen reader should say it: the access-key marker removed, a doubled one made single.</summary>
    public static string Plain(string? label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return string.Empty;
        }

        var plain = new System.Text.StringBuilder(label.Length);
        for (var i = 0; i < label.Length; i++)
        {
            if (label[i] == '&' && i + 1 < label.Length)
            {
                i++;
            }

            plain.Append(label[i]);
        }

        return plain.ToString();
    }

    /// <summary>The access key the label names, upper-cased, or null when it has none.</summary>
    public static char? Key(string? label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return null;
        }

        for (var i = 0; i + 1 < label.Length; i++)
        {
            if (label[i] != '&')
            {
                continue;
            }

            if (label[i + 1] != '&')
            {
                return char.ToUpperInvariant(label[i + 1]);
            }

            i++;
        }

        return null;
    }

    /// <summary>Text with no access key of its own, so an ampersand in it is drawn as one.</summary>
    public static string Escape(string text) => text.Replace("&", "&&", StringComparison.Ordinal);
}
