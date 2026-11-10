using Edgepad.Platform;
using Edgepad.Power;
using Xunit;

namespace Edgepad.Tests;

public sealed class PowerSourceTests
{
    [Fact]
    public void NoBusReadsAsNoBatteries()
    {
        using var source = new PowerSource(new Bus(() => null, "test"));
        Assert.Empty(source.ReadBatteries());
    }

    [Fact]
    public void LaptopComesFirstAndChargingIsRead()
    {
        var entries = PowerSource.Entries(
        [
            new UPowerDevice(5, 40.4, 2, "MX Master 3", true, false),
            new UPowerDevice(1, 0, 0, "", true, true),
            new UPowerDevice(2, 81.6, 1, "", true, true),
            new UPowerDevice(19, 55, 4, "AirPods Pro", true, false),
        ]);
        Assert.Equal(
            new BatteryEntry[]
            {
                new BatteryEntry("laptop", 82, true, ""),
                new BatteryEntry("mouse", 40, false, "MX Master 3"),
                new BatteryEntry("headphones", 55, true, "AirPods Pro"),
            },
            entries);
    }

    [Fact]
    public void AbsentAndLevellessDevicesAreLeftOut()
    {
        Assert.Empty(PowerSource.Entries(
        [
            new UPowerDevice(2, 70, 2, "", false, true),
            new UPowerDevice(6, 0, 0, "Keyboard K380", true, false),
            new UPowerDevice(0, 50, 0, "", true, false),
        ]));
    }

    [Fact]
    public void WpctlDescriptionWithSpacesAndParenthesesIsRead()
    {
        const string Output = """
            id 52, type PipeWire:Interface:Node
                alsa.card = "0"
              * device.api = "alsa"
              * node.description = "Built-in Audio (Analog Stereo)"
              * node.name = "alsa_output.pci-0000_00_1f.3.analog-stereo"
            """;
        Assert.Equal<(string, string)?>(("speaker", "Built-in Audio (Analog Stereo)"), PowerSource.ParseWpctlInspect(Output));
    }

    [Fact]
    public void WpctlBluetoothHeadsetIsHeadphones()
    {
        const string Output = """
            id 71, type PipeWire:Interface:Node
              * device.api = "bluez5"
                device.form-factor = "headset"
              * node.description = "AirPods Pro"
              * node.name = "bluez_output.AA_BB_CC_DD_EE_FF.1"
            """;
        Assert.Equal<(string, string)?>(("headphones", "AirPods Pro"), PowerSource.ParseWpctlInspect(Output));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("id 52, type PipeWire:Interface:Node\n")]
    public void WpctlWithoutDescriptionIsNull(string? output) =>
        Assert.Null(PowerSource.ParseWpctlInspect(output));

    private const string Sinks = """
        Sink #52
        	State: SUSPENDED
        	Name: alsa_output.pci-0000_01_00.1.hdmi-stereo
        	Description: GA104 High Definition Audio Controller Digital Stereo (HDMI)
        	Properties:
        		device.bus = "pci"
        		device.form_factor = "internal"
        Sink #53
        	State: RUNNING
        	Name: alsa_output.pci-0000_00_1f.3.analog-stereo
        	Description: Built-in Audio Analog Stereo
        	Properties:
        		device.bus = "pci"
        """;

    [Theory]
    [InlineData("alsa_output.pci-0000_00_1f.3.analog-stereo\n", "speaker", "Built-in Audio Analog Stereo")]
    [InlineData("alsa_output.pci-0000_01_00.1.hdmi-stereo", "monitor", "GA104 High Definition Audio Controller Digital Stereo (HDMI)")]
    public void PactlDefaultSinkIsFound(string sink, string kind, string name) =>
        Assert.Equal<(string, string)?>((kind, name), PowerSource.ParsePactlSinks(Sinks, sink));

    [Theory]
    [InlineData("missing")]
    [InlineData("")]
    public void PactlUnknownSinkIsNull(string sink) =>
        Assert.Null(PowerSource.ParsePactlSinks(Sinks, sink));
}
