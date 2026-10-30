using System.Globalization;

namespace Edgepad.Macros;

/// <summary>
/// The size, the weight cap and the TEXT framing of a macro's icon: the half of the wire format whose twin
/// is the phone's (TEXT 6, protocol/actions.txt). It is shared by the Windows and Linux apps so the two
/// cannot drift apart on a number the phone checks.
/// </summary>
internal static class MacroIconChunks
{
    /// <summary>
    /// The square a phone is sent unless it asks otherwise: what an empty or old WANT_ICONS means. A size of
    /// the phone's choosing rather than the source's own, so one oversized icon cannot cost the whole grid.
    /// <para>
    /// It was 48 until 3.0.1, which was enough while a macro button drew its picture at a fixed 28dp. The
    /// grid now sizes its buttons to the screen, so a short list draws them large, and 48 upscaled to that
    /// was visibly soft. 128 is sharp at the sizes the grid actually produces. The cost is real and lands
    /// on the link: an icon is several times the bytes it was. It is paid only when the macro grid opens,
    /// which is a screen with no trackpad on it, and never while a finger is moving the pointer.
    /// </para>
    /// </summary>
    public const int DefaultSize = 128;

    /// <summary>
    /// The squares a phone can be sent, smallest first. A request between two rounds up, so a phone never
    /// draws a picture it had to stretch, and a cache holds a handful of sizes rather than one per screen.
    /// </summary>
    public static IReadOnlyList<int> Tiers { get; } = [64, 128, 256, 384, 512];

    /// <summary>
    /// The most <paramref name="size"/> may weigh on the wire, growing with the side. Twenty-four kilobytes
    /// is 137 frames for one button at <see cref="ChunkBytes"/> — the point past which a picture is costing
    /// the link more than a label is worth. A real program icon is a fraction of it; the cap is there for the photograph somebody points
    /// the override at.
    /// <para>
    /// It was six kilobytes and 35 frames while the square was 48. Raising the square raised this with it,
    /// and the phone's own ceiling on chunks per icon had to rise in the same change — a count it refuses is
    /// an icon silently dropped, not a smaller one.
    /// </para>
    /// <para>
    /// Sides under <see cref="DefaultSize"/> keep its budget, because 64 was the fallback under a flat cap and
    /// a phone that never asks for a size has to go on receiving every icon it received then.
    /// </para>
    /// </summary>
    public static int MaxBytes(int size) => 24 * 1024 * Math.Max(size, DefaultSize) / DefaultSize;

    /// <summary>
    /// How much of an icon's base64 rides in one TEXT frame. A payload is 255 bytes and the rest is the
    /// "slot/chunk/chunks/" header in front of it. A slot is two digits at <see cref="MacroStore.MaxMacros"/>
    /// and a chunk count is three at <see cref="MaxBytes"/> of the top tier (547), so the header is at most
    /// eleven characters and 240 of payload leaves four to spare. It was nine characters and six to spare
    /// while an icon was capped at six kilobytes and the count stayed under a hundred; the margin narrowed when the square
    /// grew, and it is still a margin. A header that outgrew it would cost a truncated icon, so the count
    /// and this number have to be checked together if either moves again.
    /// </summary>
    public const int ChunkBytes = 240;

    /// <summary>
    /// The square a WANT_ICONS payload asks for: its decimal side, no larger than <paramref name="max"/>,
    /// rounded up to a tier. An empty payload, which is what a phone older than the size sends, and anything
    /// else that is not a positive decimal mean <see cref="DefaultSize"/>. <paramref name="max"/> is a tier.
    /// </summary>
    public static int Snap(string? payload, int max)
    {
        var asked = int.TryParse(payload, NumberStyles.None, CultureInfo.InvariantCulture, out var side) && side > 0
            ? side
            : DefaultSize;
        var wanted = Math.Min(asked, max);
        foreach (var tier in Tiers)
        {
            if (tier >= wanted)
            {
                return tier;
            }
        }

        return max;
    }

    /// <summary>The tiers to try for <paramref name="size"/>, largest first; a photograph will overshoot some.</summary>
    public static IReadOnlyList<int> FallbackChain(int size) => [.. Tiers.Where(tier => tier <= size).Reverse()];

    /// <summary>
    /// <paramref name="png"/> as the TEXT payloads that carry it: "slot/chunk/chunks/" then that much of its
    /// base64. Base64 because TEXT is UTF-8 by definition, and it is ASCII, so a character is a byte here and
    /// the budget can be counted in either without the two drifting apart.
    ///
    /// Here rather than in the session that sends them, because this is the half of a wire format whose other
    /// half is on the phone, and a format neither side can test alone is one that drifts. An empty icon
    /// yields no frames rather than one empty frame, which would reach the phone as a finished icon of
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Built into a list rather than yielded: a span cannot live across a yield, and the alternative —
    /// a substring per chunk — allocates the same pieces this does, one iterator object later.
    /// </remarks>
    public static IReadOnlyList<string> Chunks(int slot, byte[] png)
    {
        var encoded = Convert.ToBase64String(png);
        var chunks = (encoded.Length + ChunkBytes - 1) / ChunkBytes;
        var frames = new List<string>(chunks);
        for (var chunk = 0; chunk < chunks; chunk++)
        {
            var start = chunk * ChunkBytes;
            var piece = encoded.AsSpan(start, Math.Min(ChunkBytes, encoded.Length - start));
            frames.Add(string.Create(CultureInfo.InvariantCulture, $"{slot}/{chunk}/{chunks}/{piece}"));
        }

        return frames;
    }
}
