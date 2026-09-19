using System.Runtime.InteropServices;
using Edgepad.Controls;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The filtering is tested against hand-built DEVMODEs. CI has no panel worth asking, and the one on a
/// developer's desk would make the answers change machine to machine; only the two facts that hold
/// everywhere — the struct's size and the shape of whatever the real display reports — are asserted live.
/// </summary>
public sealed class DisplayModesTests
{
    private const uint Interlaced = 0x0002;

    private static DisplayMode Mode(uint frequency, uint width = 2560, uint height = 1440, uint depth = 32, uint flags = 0) =>
        new()
        {
            PelsWidth = width,
            PelsHeight = height,
            BitsPerPel = depth,
            DisplayFrequency = frequency,
            DisplayFlags = flags,
        };

    private static uint[] Rates(params DisplayMode[] all) =>
        [.. DisplayModes.Offerable(all, Mode(60)).Select(mode => mode.DisplayFrequency)];

    /// <summary>A mistyped field would shift every offset after it and Win32 would write past the struct.</summary>
    [Fact]
    public void DevModeHasTheWin32Size() =>
        Assert.Equal(220, Marshal.SizeOf<DisplayMode>());

    [Fact]
    public void RatesComeBackAscendingAndWithoutRepeats() =>
        Assert.Equal<uint[]>([60, 120, 144, 165], Rates(Mode(144), Mode(60), Mode(165), Mode(120), Mode(60), Mode(144)));

    [Fact]
    public void AnotherResolutionOrColourDepthIsNeverOffered() =>
        Assert.Equal<uint[]>([60], Rates(Mode(60), Mode(75, width: 1920, height: 1080), Mode(100, depth: 16)));

    [Fact]
    public void InterlacedModesAreNeverOffered() =>
        Assert.Equal<uint[]>([60], Rates(Mode(60), Mode(120, flags: Interlaced)));

    /// <summary>dmDisplayFrequency 0 and 1 mean "the hardware default", which is not a rate a dial can show.</summary>
    [Fact]
    public void ThePlaceholderFrequenciesAreNotRates() =>
        Assert.Empty(Rates(Mode(0), Mode(1)));

    [Fact]
    public void AnIndexOutsideTheListIsRefusedWithoutTouchingTheDisplay()
    {
        using var display = new DisplayModes();

        Assert.False(display.Set(-1));
        Assert.False(display.Set(display.Rates.Count));
    }

    /// <summary>Whatever this machine's display turns out to be, these two hold; nothing else can be asserted.</summary>
    [Fact]
    public void TheLiveDisplayReportsAConsistentListAndIndex()
    {
        using var display = new DisplayModes();
        int[] rates = [.. display.Rates];

        Assert.Equal<int[]>([.. rates.Distinct().Order()], rates);
        Assert.InRange(display.Current, -1, rates.Length - 1);
    }
}
