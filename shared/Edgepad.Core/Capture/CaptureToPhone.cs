using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Edgepad.Dispatch;
using Edgepad.Protocol;

namespace Edgepad.Capture;

/// <summary>
/// Sends a capture to the phone when the phone asked for captures to be saved there (SET CAPTURE_TARGET 1), and
/// deletes the copy here only once the phone says it saved it. One per session: the target is forgotten with it.
/// <para>
/// Around the capture shortcut it lists the folders the OS tool saves into, then watches them on its own task for
/// the one new file (<see cref="CapturePick"/>), waits for it to stop changing, and sends it as FILE_BEGIN, FILE_CHUNKs
/// and FILE_END. Every doubt keeps the file: no file, two files, a file that vanishes, a file too large, an answer
/// other than SAVED or none at all. Only a SAVED that arrives after FILE_END deletes anything, and only the file
/// that was sent.
/// </para>
/// <para>
/// Chunks never starve input: each one is its own send under the stream's lock, so a PONG or a STATE gets between any
/// two, and they are paced, <see cref="ChunksPerTick"/> every <see cref="TickMs"/>, and only
/// <see cref="BusyChunksPerTick"/> while the phone has sent input in the last <see cref="InputQuietMs"/>.
/// </para>
/// </summary>
internal sealed class CaptureToPhone(ICaptureFolders folders, Action<Frame> send, Func<long> lastInputMs) : IDisposable
{
    public const int PollMs = 500;

    /// <summary>A region is a drag on this screen and Print on Linux opens a tool to pick in, so both get time.</summary>
    public const long ScreenshotWindowMs = 120_000;

    /// <summary>A recording's file is complete only once it stops.</summary>
    public const long RecordingWindowMs = 3_600_000;

    /// <summary>What is left of a watch once the phone cancels the overlay: enough for a file already written.</summary>
    public const long CancelledWindowMs = 3_000;

    /// <summary>How long a clipboard image waits for the file the Snipping Tool also writes, which is preferred.</summary>
    public const long ClipboardGraceMs = 3_000;

    public const int TickMs = 20;
    public const int ChunksPerTick = 16;
    public const int BusyChunksPerTick = 2;
    public const long InputQuietMs = 250;
    public const int AnswerTimeoutMs = 30_000;

    private const int ScreenshotSettledPolls = 2;
    private const int RecordingSettledPolls = 6;
    private const int NoAnswer = -1;

    private readonly CancellationTokenSource ended = new();
    private readonly SemaphoreSlim sending = new(1, 1);
    private readonly ManualResetEventSlim answered = new();
    private readonly Lock gate = new();

    private CancellationTokenSource? watching;
    private long startedAt;
    private long deadline;
    private volatile bool toPhone;
    private int answer = NoAnswer;
    private bool awaiting;

    private static long Now => Environment.TickCount64;

    /// <summary>SET CAPTURE_TARGET. False, dropped and counted, for a value that is neither Laptop nor Phone.</summary>
    public bool SetTarget(byte value)
    {
        if (value > (byte)CaptureTarget.Phone)
        {
            return false;
        }

        toPhone = value == (byte)CaptureTarget.Phone;
        if (!toPhone)
        {
            lock (gate)
            {
                watching?.Cancel();
            }
        }

        return true;
    }

    /// <summary>
    /// Presses a capture shortcut through <paramref name="press"/>, and when the phone wants captures, watches for the
    /// file it makes. The folders are listed before the press, so a file the press makes can never count as old.
    /// </summary>
    public bool Run(ActionId capture, Func<bool> press)
    {
        if (!toPhone)
        {
            return press();
        }

        var watched = folders.For(capture);
        var before = CapturePick.Before(List(watched));
        var recording = capture == ActionId.CaptureRecord;
        var clipboard = recording ? null : folders.ClipboardStamp();
        if (!press())
        {
            return false;
        }

        var token = StartWatch(recording ? RecordingWindowMs : ScreenshotWindowMs);
        var settled = recording ? RecordingSettledPolls : ScreenshotSettledPolls;
        _ = Task.Run(() => Watch(watched, before, clipboard, settled, token), CancellationToken.None);
        return true;
    }

    /// <summary>
    /// The phone left its capture screen. A watch started while the overlay may still be open is cut short, so a
    /// cancelled drag leaves nothing running; an older one, a recording under way, is left alone.
    /// </summary>
    public void Cancel()
    {
        lock (gate)
        {
            if (Now - startedAt < CaptureOverlay.PendingMs)
            {
                deadline = Math.Min(deadline, Now + CancelledWindowMs);
            }
        }
    }

    /// <summary>The phone's answer to the file on its way. False, dropped and counted, when none is.</summary>
    public bool Take(FileResult result)
    {
        lock (gate)
        {
            if (!awaiting)
            {
                return false;
            }

            answer = result.Status;
        }

        answered.Set();
        return true;
    }

    /// <summary>
    /// The session ended. A watch stops and a transfer stops where it is; either one still running meets a disposed
    /// object, which it catches, and the file stays here.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            watching?.Cancel();
            watching?.Dispose();
        }

        ended.Cancel();
        ended.Dispose();
        answered.Dispose();
        sending.Dispose();
    }

    private CancellationToken StartWatch(long window)
    {
        lock (gate)
        {
            // One watch at a time: a capture taken while another is watched for makes the older one's file
            // impossible to tell from the newer, so the older stops and its file stays here. ponytail: the old
            // linked source is cancelled, not disposed, since its task may still be reading the token.
            watching?.Cancel();
            watching = CancellationTokenSource.CreateLinkedTokenSource(ended.Token);
            startedAt = Now;
            deadline = Now + window;
            return watching.Token;
        }
    }

    private long Deadline()
    {
        lock (gate)
        {
            return deadline;
        }
    }

    private void Watch(IReadOnlyList<CaptureFolder> watched, HashSet<string> before, long? clipboard, int settledPolls, CancellationToken token)
    {
        try
        {
            FileStamp? last = null;
            var still = 0;
            var clipboardSince = -1L;
            while (!token.WaitHandle.WaitOne(PollMs))
            {
                if (Now > Deadline())
                {
                    Status(CaptureStatus.NotFound);
                    return;
                }

                var found = CapturePick.NewFile(before, List(watched), out var ambiguous);
                if (ambiguous)
                {
                    Log.Write("Capture: more than one new file appeared, so none was sent");
                    Status(CaptureStatus.NotFound);
                    return;
                }

                if (found is { } file)
                {
                    still = CapturePick.Unchanged(last, file) ? still + 1 : 0;
                    last = file;
                    if (still >= settledPolls && Readable(file.Path))
                    {
                        SendFile(file.Path);
                        return;
                    }

                    continue;
                }

                if (last is not null)
                {
                    Log.Write("Capture: the new file went away before it settled, so none was sent");
                    Status(CaptureStatus.NotFound);
                    return;
                }

                // No file: the Snipping Tool with autosave off leaves the shot only on the clipboard.
                if (clipboard is { } stamp && folders.ClipboardStamp() != stamp)
                {
                    if (clipboardSince < 0)
                    {
                        clipboardSince = Now;
                    }
                    else if (Now - clipboardSince >= ClipboardGraceMs)
                    {
                        if (folders.ClipboardPng() is { } png)
                        {
                            var name = string.Create(CultureInfo.InvariantCulture, $"Screenshot {DateTime.Now:yyyy-MM-dd HHmmss}.png");
                            using var data = new MemoryStream(png, writable: false);
                            Send(name, FileType.Png, data, null);
                            return;
                        }

                        clipboard = null;
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException
            or OperationCanceledException or COMException or InvalidOperationException)
        {
            // A dead link, a folder taken away, the session ending: the file, if there was one, stays here.
            Log.Write($"Capture: nothing was sent to the phone: {e.Message}");
        }
    }

    private void SendFile(string path)
    {
        if (FileTypes.FromPath(path) is not { } type)
        {
            return;
        }

        using (var data = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (!Send(Path.GetFileName(path), type, data, path))
            {
                return;
            }
        }

        // Closed first: Windows will not delete a file this process still has open.
        File.Delete(path);
        Log.Write("Capture: saved on the phone, and deleted here");
    }

    /// <summary>
    /// Sends one file and waits for the phone's answer. True only for SAVED after FILE_END, which is the one case in
    /// which the caller may delete its copy.
    /// </summary>
    private bool Send(string name, FileType type, Stream data, string? path)
    {
        if (data.Length > FileTypes.Limit(type))
        {
            Status(CaptureStatus.TooLarge);
            return false;
        }

        sending.Wait(ended.Token);
        try
        {
            lock (gate)
            {
                answer = NoAnswer;
                awaiting = true;
            }

            answered.Reset();
            send(new FileBegin((byte)type, (uint)data.Length, name));
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[FrameCodec.MaxChunkBytes];
            byte seq = 0;
            var sent = 0;
            int read;
            while ((read = data.Read(buffer)) > 0)
            {
                // The phone answers early only to refuse, and then it ignores the rest.
                if (Volatile.Read(ref answer) != NoAnswer)
                {
                    break;
                }

                var chunk = buffer[..read];
                hash.AppendData(chunk);
                send(new FileChunk(seq++, chunk));
                if (++sent >= Budget())
                {
                    sent = 0;
                    if (ended.Token.WaitHandle.WaitOne(TickMs))
                    {
                        return false;
                    }
                }
            }

            if (Volatile.Read(ref answer) == NoAnswer)
            {
                send(new FileEnd(hash.GetHashAndReset()));
                answered.Wait(AnswerTimeoutMs, ended.Token);
            }

            var status = Volatile.Read(ref answer);
            if (status == (int)FileStatus.Saved)
            {
                return true;
            }

            Log.Write(status == NoAnswer
                ? $"Capture: the phone did not answer, so {path ?? "the clipboard image"} stays here"
                : $"Capture: the phone answered {(FileStatus)status}, so {path ?? "the clipboard image"} stays here");
            return false;
        }
        finally
        {
            lock (gate)
            {
                awaiting = false;
            }

            sending.Release();
        }
    }

    private int Budget() => Now - lastInputMs() < InputQuietMs ? BusyChunksPerTick : ChunksPerTick;

    private void Status(string token) => send(new Text((byte)TextKind.CaptureStatus, token));

    /// <summary>A file no other process is still writing: Windows refuses a read that denies writers while one has it open.</summary>
    private static bool Readable(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>The top level of each folder, skipping one that is missing or unreadable.</summary>
    private static List<FileStamp> List(IReadOnlyList<CaptureFolder> watched)
    {
        var files = new List<FileStamp>();
        foreach (var folder in watched)
        {
            try
            {
                foreach (var info in new DirectoryInfo(folder.Path).EnumerateFiles())
                {
                    files.Add(new FileStamp(info.FullName, info.Length, info.LastWriteTimeUtc, folder.Shared));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Not there (yet): the tool creates its folder on the first capture, which is then a new file.
            }
        }

        return files;
    }
}
