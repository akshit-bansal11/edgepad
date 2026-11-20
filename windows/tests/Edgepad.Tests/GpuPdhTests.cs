using System.Runtime.InteropServices;
using Edgepad.Dashboard;
using Xunit;

namespace Edgepad.Tests;

public sealed class GpuPdhTests
{
    private const string Engine3D = "pid_123_luid_0x00000000_0x0000C3F1_phys_0_eng_0_engtype_3D";

    [Fact]
    public void TheEngineKeyDropsTheProcess()
    {
        Assert.Equal("luid_0x00000000_0x0000C3F1_phys_0_eng_0_engtype_3D", GpuPdh.EngineKey(Engine3D));
        Assert.Equal("3D", GpuPdh.EngineType(Engine3D));
        Assert.Equal("VideoDecode", GpuPdh.EngineType("pid_9_luid_0x0_0x1_phys_0_eng_3_engtype_VideoDecode"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("_Total")]
    [InlineData("pid_123")]
    public void ANameWithNoEngineHasNoKey(string instance)
    {
        Assert.Null(GpuPdh.EngineKey(instance));
        Assert.Null(GpuPdh.EngineType(instance));
    }

    [Fact]
    public void ProcessesAddUpPerEngineAndTheBusiestEngineWins()
    {
        var util = GpuPdh.Utilization(
        [
            ("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 20.4),
            ("pid_2_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 15.0),
            ("pid_1_luid_0x0_0x1_phys_0_eng_1_engtype_Copy", 50.0),
            ("pid_3_luid_0x0_0x1_phys_0_eng_2_engtype_VideoDecode", 12.0),
        ]);

        Assert.Equal(50, util);
    }

    [Fact]
    public void ASumPast100IsClampedAndBadValuesCountAsZero()
    {
        Assert.Equal(100, GpuPdh.Utilization([("pid_1_" + Engine3D[8..], 80.0), ("pid_2_" + Engine3D[8..], 70.0)]));
        Assert.Equal(0, GpuPdh.Utilization([(Engine3D, -5.0), (Engine3D, double.NaN), (Engine3D, double.PositiveInfinity)]));
    }

    [Fact]
    public void NoEnginesIsUnknown()
    {
        Assert.Equal(-1, GpuPdh.Utilization([]));
        Assert.Equal(-1, GpuPdh.Utilization([("_Total", 40.0)]));
    }

    [Fact]
    public void ACounterArrayIsReadAndBadItemsAreSkipped()
    {
        var names = new[] { Marshal.StringToHGlobalUni("a"), Marshal.StringToHGlobalUni("b"), Marshal.StringToHGlobalUni("c") };
        var buffer = Marshal.AllocHGlobal(GpuPdh.ItemSize * 4);
        try
        {
            Write(buffer, 0, names[0], 0, BitConverter.DoubleToInt64Bits(12.5));
            Write(buffer, 1, names[1], 0xC0000BC6, BitConverter.DoubleToInt64Bits(99)); // PDH_CSTATUS_INVALID_DATA
            Write(buffer, 2, 0, 0, BitConverter.DoubleToInt64Bits(7));
            Write(buffer, 3, names[2], 1, BitConverter.DoubleToInt64Bits(3.25));

            var items = GpuPdh.ParseItems(buffer, 4, asDouble: true);

            Assert.Equal(new (string, double)[] { ("a", 12.5), ("c", 3.25) }, items);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            foreach (var name in names)
            {
                Marshal.FreeHGlobal(name);
            }
        }
    }

    [Fact]
    public void ALargeCounterArrayKeepsWholeBytes()
    {
        var name = Marshal.StringToHGlobalUni("luid_0x0_0x1_phys_0");
        var buffer = Marshal.AllocHGlobal(GpuPdh.ItemSize);
        try
        {
            Write(buffer, 0, name, 0, 4L << 30);

            Assert.Equal(4L << 30, (long)Assert.Single(GpuPdh.ParseItems(buffer, 1, asDouble: false)).Value);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(name);
        }
    }

    [Fact]
    public void AdapterMemoryReadsEveryRegistryShape()
    {
        Assert.Equal(8L << 30, GpuPdh.MemoryBytes(8L << 30));
        Assert.Equal(0x8000_0000L, GpuPdh.MemoryBytes(unchecked((int)0x8000_0000)));
        Assert.Equal(6L << 30, GpuPdh.MemoryBytes(BitConverter.GetBytes(6L << 30)));
        Assert.Equal(512L << 20, GpuPdh.MemoryBytes(BitConverter.GetBytes((uint)(512 << 20))));
        Assert.Equal(-1, GpuPdh.MemoryBytes(null));
        Assert.Equal(-1, GpuPdh.MemoryBytes("8 GB"));
        Assert.Equal(-1, GpuPdh.MemoryBytes(new byte[3]));
    }

    [Theory]
    [InlineData(2L << 30, 8L << 30, 25)]
    [InlineData(9L << 30, 8L << 30, 100)]
    [InlineData(-1L, 8L << 30, -1)]
    [InlineData(1L << 30, -1L, -1)]
    [InlineData(1L << 30, 0L, -1)]
    public void VramShareIsClampedAndUnknownStaysUnknown(long used, long total, int expected) =>
        Assert.Equal(expected, WindowsStats.VramPercent(used, total));

    [Fact]
    public void NvmlIsOnlyEverLoadedByAbsolutePath() =>
        Assert.All(Nvml.Paths(), p =>
        {
            Assert.True(Path.IsPathFullyQualified(p));
            Assert.Equal("nvml.dll", Path.GetFileName(p));
        });

    [Fact]
    public void TheRealMachineReadsInRangeOrUnknown()
    {
        using var gpu = new GpuPdh();
        gpu.Read();
        var (util, dedicated) = gpu.Read();

        Assert.InRange(util, -1, 100);
        Assert.True(dedicated >= -1);
    }

    private static void Write(nint buffer, int index, nint name, uint status, long value)
    {
        var item = buffer + (index * GpuPdh.ItemSize);
        Marshal.WriteIntPtr(item, name);
        Marshal.WriteInt32(item, 8, unchecked((int)status));
        Marshal.WriteInt32(item, 12, 0);
        Marshal.WriteInt64(item, 16, value);
    }
}
