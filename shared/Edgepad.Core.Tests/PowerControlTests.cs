using Edgepad.Power;
using Edgepad.Protocol;
using Xunit;

namespace Edgepad.Tests;

public sealed class PowerControlTests
{
    private readonly FakePower platform = new();
    private readonly List<string> states = [];
    private readonly PowerControl control;

    public PowerControlTests() => control = new PowerControl(platform, states.Add);

    [Fact]
    public void RestartReportsTheCountdownThenNothingOnCancel()
    {
        Assert.True(control.Run(ActionId.Restart));
        Assert.True(control.Run(ActionId.PowerCancel));

        Assert.Equal(["restart/60", ""], states);
        Assert.Empty(platform.Executed);
    }

    [Fact]
    public void ExpiryExecutesOnceAndReportsNothing()
    {
        control.Run(ActionId.ShutDown);
        platform.Finish(true);

        Assert.Equal(["shutdown/60", ""], states);
        Assert.Equal([PowerKind.ShutDown], platform.Executed);
    }

    [Fact]
    public void ASecondRestartWhilePendingDoesNothing()
    {
        control.Run(ActionId.Restart);

        Assert.True(control.Run(ActionId.Restart));
        Assert.True(control.Run(ActionId.ShutDown));
        Assert.Equal(1, platform.Started);
        Assert.Equal(["restart/60"], states);
    }

    [Fact]
    public void SleepIsRefusedWithoutTheCap()
    {
        platform.Caps = PowerCaps.Restart;

        Assert.False(control.Run(ActionId.Sleep));
        Assert.False(control.Run(ActionId.Hibernate));
        Assert.False(control.Run(ActionId.ShutDown));
        Assert.Equal(0, platform.Slept);
    }

    [Fact]
    public void SleepAndHibernateAreRefusedWhileACountdownIsPending()
    {
        control.Run(ActionId.Restart);

        Assert.False(control.Run(ActionId.Sleep));
        Assert.False(control.Run(ActionId.Hibernate));
        Assert.Equal(0, platform.Slept);

        control.Run(ActionId.PowerCancel);

        Assert.True(control.Run(ActionId.Sleep));
    }

    [Fact]
    public void ACountdownFromAnEarlierSessionBlocksSleepAndAnotherStart()
    {
        // A new session's PowerControl, while the countdown an earlier one started is still on screen.
        platform.Pending = (PowerKind.ShutDown, 40);

        Assert.False(control.Run(ActionId.Sleep));
        Assert.False(control.Run(ActionId.Hibernate));
        Assert.True(control.Run(ActionId.Restart));
        Assert.Equal(0, platform.Slept);
        Assert.Equal(0, platform.Started);
        Assert.Empty(states);
    }

    [Fact]
    public void CurrentStateIsThePendingCountdown()
    {
        Assert.Equal("", control.CurrentState());
        platform.Pending = (PowerKind.Restart, 52);
        Assert.Equal("restart/52", control.CurrentState());
    }

    [Fact]
    public void FeaturesComeInAFixedOrder()
    {
        Assert.Equal("sleep/hibernate/restart/shutdown", PowerControl.Features(PowerCaps.ShutDown | PowerCaps.Sleep | PowerCaps.Restart | PowerCaps.Hibernate));
        Assert.Equal("sleep/shutdown", PowerControl.Features(PowerCaps.ShutDown | PowerCaps.Sleep));
        Assert.Equal("", PowerControl.Features(PowerCaps.None));
    }

    internal sealed class FakePower : IPowerPlatform
    {
        private Action<bool>? ended;

        public PowerCaps Caps { get; set; } = PowerCaps.Sleep | PowerCaps.Hibernate | PowerCaps.Restart | PowerCaps.ShutDown;

        public (PowerKind Kind, int SecondsLeft)? Pending { get; set; }

        public int Slept { get; private set; }

        public int Started { get; private set; }

        public List<PowerKind> Executed { get; } = [];

        public bool Sleep()
        {
            Slept++;
            return true;
        }

        public bool Hibernate() => true;

        public bool StartCountdown(PowerKind kind, int seconds, Action<bool> ended)
        {
            Started++;
            this.ended = ended;
            return true;
        }

        public void CancelCountdown() => Finish(false);

        public bool Execute(PowerKind kind)
        {
            Executed.Add(kind);
            return true;
        }

        public void Finish(bool fired)
        {
            var done = ended;
            ended = null;
            done?.Invoke(fired);
        }
    }
}
