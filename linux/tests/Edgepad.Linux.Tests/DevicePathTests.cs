using Edgepad.Bluetooth;
using Xunit;

namespace Edgepad.Tests;

/// <summary>
/// The phone's address is read from the device path BlueZ passes to NewConnection, and trust is granted to that
/// address, so anything that is not exactly a BlueZ device path is refused rather than read loosely.
/// </summary>
public sealed class DevicePathTests
{
    [Theory]
    [InlineData("/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("/org/bluez/hci0/dev_aa_bb_cc_dd_ee_ff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("/org/bluez/hci0/dev_0a_1B_2c_3D_4e_5F", "0A:1B:2C:3D:4E:5F")]
    [InlineData("/org/bluez/hci12/dev_01_23_45_67_89_AB", "01:23:45:67:89:AB")]
    public void ReadsTheAddressInUpperCase(string path, string expected)
    {
        Assert.True(BluezProfile.TryAddress(path, out var address));
        Assert.Equal(expected, address);
    }

    [Theory]
    // Not an adapter.
    [InlineData("/org/bluez/hcix/dev_AA_BB_CC_DD_EE_FF")]
    [InlineData("/org/bluez/hci/dev_AA_BB_CC_DD_EE_FF")]
    [InlineData("/org/bluez/dev_AA_BB_CC_DD_EE_FF")]
    [InlineData("/org/other/hci0/dev_AA_BB_CC_DD_EE_FF")]
    // Not a device, or something below one.
    [InlineData("/org/bluez/hci0")]
    [InlineData("/org/bluez/hci0/")]
    [InlineData("/org/bluez/hci0/AA_BB_CC_DD_EE_FF")]
    [InlineData("/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF/service0001")]
    // A malformed address.
    [InlineData("/org/bluez/hci0/dev_AA_BB_CC_DD_EE")]
    [InlineData("/org/bluez/hci0/dev_AA_BB_CC_DD_EE_FF_00")]
    [InlineData("/org/bluez/hci0/dev_AA:BB:CC:DD:EE:FF")]
    [InlineData("/org/bluez/hci0/dev_AABB_CC_DD_EE_FF_")]
    [InlineData("/org/bluez/hci0/dev_GG_BB_CC_DD_EE_FF")]
    [InlineData("")]
    public void RefusesAnythingElse(string path)
    {
        Assert.False(BluezProfile.TryAddress(path, out var address));
        Assert.Equal("", address);
    }
}
