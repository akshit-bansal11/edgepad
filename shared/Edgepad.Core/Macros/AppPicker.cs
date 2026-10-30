using System.Globalization;
using System.Text;
using Edgepad.Blob;
using Edgepad.Protocol;

namespace Edgepad.Macros;

/// <summary>
/// Lets the phone add a macro by picking from a list this laptop generated, and is the only path from phone text to the
/// <see cref="MacroStore"/>. The phone never supplies a target: a token is <c>epoch.index</c>, the rows it indexes stay on
/// this side of the link, and <see cref="Add"/> has nothing else to go on. Each APPS_LIST starts a new epoch and only the
/// latest is valid, so a token that names no row of it is stale and changes nothing. One per session, so a list dies with it.
/// <para>
/// A list is scanned and sent off the read thread, since a scan is slow and a transfer is paced. Its blob is one
/// <c>token TAB name</c> line per app, sorted by name. An icon follows each app that has one, labelled with its token.
/// </para>
/// </summary>
/// <param name="catalog">This OS's apps.</param>
/// <param name="store">Where a picked app goes.</param>
/// <param name="sender">The session's sender.</param>
/// <param name="ended">The session's end.</param>
/// <param name="schedule">Runs work off the read thread, for tests.</param>
/// <param name="pause">Waits this many milliseconds and answers whether the session is still up, for tests.</param>
internal sealed class AppPicker(
    IAppCatalog catalog,
    MacroStore store,
    BlobSender sender,
    CancellationToken ended,
    Action<Action>? schedule = null,
    Func<int, bool>? pause = null)
{
    public const int MaxApps = 400;
    public const int MaxNameChars = 40;
    public const int MaxIconBytes = 8 * 1024;

    /// <summary>The list needs no name; its rows carry their own tokens.</summary>
    private const string NoLabel = "";

    private readonly Lock gate = new();
    private readonly Action<Action> start = schedule ?? (work => ThreadPool.UnsafeQueueUserWorkItem(_ => work(), null));
    private readonly Func<int, bool> wait = pause ?? (ms => !ended.WaitHandle.WaitOne(ms));

    /// <summary>The rows of <see cref="epoch"/>. Empty until its scan is done, so no token works meanwhile.</summary>
    private List<AppEntry> apps = [];

    private int epoch;

    /// <summary>The epoch still being sent, or 0 after a cancel. A cancel stops the sending and leaves the list usable.</summary>
    private volatile int sending;

    /// <summary>APPS_LIST. Starts a new epoch, which also stops whatever the last one was still sending.</summary>
    public bool List()
    {
        int current;
        lock (gate)
        {
            current = ++epoch;
            sending = current;
            apps = [];
        }

        start(() => Guarded(() => Send(current)));
        return true;
    }

    /// <summary>APPS_CANCEL. A row already sent can still be added: the phone may leave the list and the add follow it.</summary>
    public bool Cancel()
    {
        sending = 0;
        return true;
    }

    /// <summary>APP_ADD: adds the row <paramref name="token"/> names, as an <see cref="AppAddStatus"/> token.</summary>
    public string Add(string token)
    {
        AppEntry app;
        lock (gate)
        {
            if (!TryParse(token, out var named, out var index) || named != epoch || (uint)index >= (uint)apps.Count)
            {
                return AppAddStatus.Stale;
            }

            app = apps[index];
        }

        return store.TryAdd(new Macro(app.Name, app.Target, app.Arguments, app.IconPath));
    }

    private static string Token(int epoch, int index) => string.Create(CultureInfo.InvariantCulture, $"{epoch}.{index}");

    /// <summary>Digits, a dot, digits, and nothing else: no sign, no space, no second dot.</summary>
    private static bool TryParse(string token, out int epoch, out int index)
    {
        epoch = 0;
        index = 0;
        var dot = token.IndexOf('.');
        return dot > 0
            && int.TryParse(token.AsSpan(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out epoch)
            && int.TryParse(token.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }

    /// <summary>The rows the phone is offered. Sorted before the cap, so the same machine always loses the same apps.</summary>
    private static List<AppEntry> Rows(IReadOnlyList<AppEntry> scanned) => scanned
        .Select(app => app with { Name = ShortName(MacroStore.Plain(app.Name)) })
        .Where(app => app.Name.Length > 0)
        .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
        .Take(MaxApps)
        .ToList();

    /// <summary>Cut on a whole character: half a surrogate pair is an invalid name, not a short one.</summary>
    private static string ShortName(string name)
    {
        if (name.Length <= MaxNameChars)
        {
            return name;
        }

        var cut = char.IsHighSurrogate(name[MaxNameChars - 1]) ? MaxNameChars - 1 : MaxNameChars;
        return name[..cut].TrimEnd();
    }

    private static byte[] ListBytes(int epoch, List<AppEntry> rows)
    {
        var text = new StringBuilder();
        for (var index = 0; index < rows.Count; index++)
        {
            text.Append(Token(epoch, index)).Append('\t').Append(rows[index].Name).Append('\n');
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }

    /// <summary>The last stop before a thread with nothing above it, so a list that fails costs that list and no more.</summary>
    private static void Guarded(Action work)
    {
        try
        {
            work();
        }
        catch (Exception e)
        {
            Log.Write($"App list: {e.GetType().Name}: {e.Message}");
        }
    }

    private void Send(int current)
    {
        var rows = Rows(Scan());
        lock (gate)
        {
            if (sending != current)
            {
                return;
            }

            apps = rows;
        }

        bool Abandoned() => sending != current || ended.IsCancellationRequested;

        if (!sender.Send(BlobKind.AppList, NoLabel, ListBytes(current, rows), Abandoned))
        {
            return;
        }

        for (var index = 0; index < rows.Count && !Abandoned(); index++)
        {
            // The sender paces inside one blob and an icon can fit in one budget, so without a tick here 400 small
            // icons would go in one burst. ponytail: one icon per tick is about 8 s for a full list, on any link.
            if (IconOf(rows[index]) is { } png
                && (!sender.Send(BlobKind.AppIcon, Token(current, index), png, Abandoned) || !wait(BlobSender.TickMs)))
            {
                return;
            }
        }
    }

    /// <summary>The apps, or none when the scan failed: an empty list answers the phone, and silence would not.</summary>
    private IReadOnlyList<AppEntry> Scan()
    {
        try
        {
            return catalog.Scan();
        }
        catch (Exception e)
        {
            Log.Write($"App list: the scan failed: {e.GetType().Name}: {e.Message}");
            return [];
        }
    }

    /// <summary>An icon the phone can take, or null. One that fails or is too big costs that picture, not the rest.</summary>
    private byte[]? IconOf(AppEntry app)
    {
        try
        {
            return catalog.Icon(app) is { Length: > 0 and <= MaxIconBytes } png ? png : null;
        }
        catch (Exception e)
        {
            Log.Write($"App list: no icon for {app.Name}: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }
}
