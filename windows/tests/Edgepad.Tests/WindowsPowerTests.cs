using Edgepad.Dispatch;
using Edgepad.Power;
using Xunit;

namespace Edgepad.Tests;

public sealed class WindowsPowerTests
{
    private const PowerCaps Always = PowerCaps.Restart | PowerCaps.ShutDown;

    [Fact]
    public void S3OnlySleepsButCannotHibernate() =>
        Assert.Equal(Always | PowerCaps.Sleep, WindowsPower.CapsFrom(s1: false, s2: false, s3: true, s4: false, hiberFile: false, aoac: false));

    [Fact]
    public void ModernStandbyOnlyStillSleeps() =>
        Assert.Equal(Always | PowerCaps.Sleep, WindowsPower.CapsFrom(s1: false, s2: false, s3: false, s4: false, hiberFile: false, aoac: true));

    [Fact]
    public void S4WithoutAHibernationFileCannotHibernate() =>
        Assert.Equal(Always, WindowsPower.CapsFrom(s1: false, s2: false, s3: false, s4: true, hiberFile: false, aoac: false));

    [Fact]
    public void S4WithAHibernationFileHibernates() =>
        Assert.Equal(Always | PowerCaps.Hibernate, WindowsPower.CapsFrom(s1: false, s2: false, s3: false, s4: true, hiberFile: true, aoac: false));
}
