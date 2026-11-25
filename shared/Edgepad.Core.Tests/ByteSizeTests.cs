using System.Globalization;
using Edgepad.Transfer;
using Xunit;

namespace Edgepad.Tests;

/// <summary>The byte counts both laptops' transfer panels show.</summary>
public sealed class ByteSizeTests
{
    [Theory]
    [InlineData(0UL, "0 B")]
    [InlineData(1023UL, "1023 B")]
    [InlineData(1024UL, "1.0 KB")]
    [InlineData(1572864UL, "1.5 MB")]
    [InlineData(2147483648UL, "2.0 GB")]
    public void BytesUseBinaryUnits(ulong bytes, string expected)
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal(expected, ByteSize.Format(bytes));
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }
}
