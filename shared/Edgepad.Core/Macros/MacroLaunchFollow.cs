using System.Diagnostics;

namespace Edgepad.Macros;

/// <summary>
/// What happens after a launch call returns, which that call cannot say: when the window turned up, and whether it
/// turned up in front. The log line is the measurement, for the owner to read. A window that arrives behind others
/// is raised once, because a process in the background is not always allowed to take the front for itself.
/// </summary>
internal static class MacroLaunchFollow
{
    /// <summary>A count rather than a deadline, so a test with an instant delay ends instead of spinning out the clock.</summary>
    private const int Polls = 40;

    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Polls <paramref name="survey"/> until the app has a window or the polls run out; the time it logs counts from
    /// this call, which is the launch call's return. Never throws: the store does not wait for the task it gets back.
    /// </summary>
    public static async Task Run(
        Func<OpenState> survey, Func<bool> raise, Func<TimeSpan, Task> delay, Action<string> log)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            for (var poll = 0; poll < Polls; poll++)
            {
                var state = survey();
                if (state is OpenState.Open or OpenState.Front)
                {
                    var front = state == OpenState.Front;
                    var seen = clock.ElapsedMilliseconds;
                    var raised = !front && raise();
                    log($"a window appeared after {seen} ms, in front: {(front ? "yes" : "no")}, raised: {(raised ? "yes" : "no")}");
                    return;
                }

                await delay(Every);
            }

            log($"no window appeared in {clock.ElapsedMilliseconds} ms");
        }
        catch (Exception e)
        {
            log($"the follow-up stopped: {e.Message}");
        }
    }
}
