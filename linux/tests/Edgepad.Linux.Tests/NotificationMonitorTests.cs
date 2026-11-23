using System.Text;
using Edgepad.Platform;
using Edgepad.Ui;
using Xunit;

namespace Edgepad.Tests;

public sealed class NotificationMonitorTests
{
    private sealed record Sent(string Label, string Text);

    // Never SetPhone here: that would open a bus connection. PhoneOn is set directly, the way SetPhone does.
    private static (NotificationMonitor Monitor, List<Sent> Sent) Make(bool setting, bool phone, Func<long>? clock = null)
    {
        var sent = new List<Sent>();
        var monitor = new NotificationMonitor((label, data) => sent.Add(new Sent(label, Encoding.UTF8.GetString(data))), () => setting, clock)
        {
            PhoneOn = phone,
        };
        return (monitor, sent);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void SendsOnlyWithTheSwitchAndThePhoneBothOn(bool setting, bool phone, bool expected)
    {
        var (monitor, sent) = Make(setting, phone);
        using (monitor)
        {
            monitor.Deliver("Mail", "Hello", "World");
        }

        Assert.Equal(expected, sent.Count == 1);
    }

    [Fact]
    public void ABodyAndTitleFoldIntoTheThreeFieldMessage()
    {
        var (monitor, sent) = Make(setting: true, phone: true);
        using (monitor)
        {
            monitor.Deliver("Mail", "Subject\nline", "Body");
        }

        Assert.Equal("Mail\nSubject line\nBody", Assert.Single(sent).Text);
    }

    [Fact]
    public void EdgepadsOwnNotificationsAreIgnored()
    {
        var (monitor, sent) = Make(setting: true, phone: true);
        using (monitor)
        {
            monitor.Deliver("Edgepad", "Edgepad update available", "Click to open");
        }

        Assert.Empty(sent);
    }

    [Fact]
    public void AnEmptyTitleAndBodySendNothing()
    {
        var (monitor, sent) = Make(setting: true, phone: true);
        using (monitor)
        {
            monitor.Deliver("Mail", "", "\n");
        }

        Assert.Empty(sent);
    }

    [Fact]
    public void ADuplicateIsDroppedAndTheLabelsCount()
    {
        long now = 0;
        var (monitor, sent) = Make(setting: true, phone: true, () => now);
        using (monitor)
        {
            monitor.Deliver("Mail", "A", "one");
            monitor.Deliver("Mail", "A", "one");
            monitor.Deliver("Mail", "B", "two");
        }

        Assert.Equal(["1", "2"], sent.Select(s => s.Label));
    }

    [Fact]
    public void TheNotifyTokenFollowsTheFlag()
    {
        Assert.Equal("capture/notify", Annotator.Features(annotate: false, notify: true));
        Assert.Equal("capture", Annotator.Features(annotate: false));
    }

    [Fact]
    public void TheMonitorRuleNamesOnlyNotify()
    {
        Assert.Equal("type='method_call',interface='org.freedesktop.Notifications',member='Notify'", NotificationMonitor.Rule);
    }
}
