using Edgepad.Annotation;
using Xunit;

namespace Edgepad.Tests;

/// <summary>The file a saved signature is written to, shared by the Windows and Linux apps.</summary>
public sealed class SignatureFileTests
{
    [Fact]
    public void TwoSavesInOneSecondKeepEverySignature()
    {
        // Same stem, as saves within one second get: each takes a new name, never an earlier one's file.
        var folder = Path.Combine(Path.GetTempPath(), $"edgepad-signatures-{Guid.NewGuid():N}");
        try
        {
            byte[] one = [1];
            byte[] two = [2];
            byte[] three = [3];
            var first = SignatureFile.WriteNew(folder, "signature-20261024-120000", one);
            var second = SignatureFile.WriteNew(folder, "signature-20261024-120000", two);
            var third = SignatureFile.WriteNew(folder, "signature-20261024-120000", three);

            Assert.Equal(Path.Combine(folder, "signature-20261024-120000.png"), first);
            Assert.Equal(Path.Combine(folder, "signature-20261024-120000-2.png"), second);
            Assert.Equal(Path.Combine(folder, "signature-20261024-120000-3.png"), third);
            Assert.Equal(one, File.ReadAllBytes(first));
            Assert.Equal(two, File.ReadAllBytes(second));
            Assert.Equal(three, File.ReadAllBytes(third));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheStemIsTheLocalTimeToTheSecond() =>
        Assert.Equal("signature-20261024-090507", SignatureFile.Stem(new DateTime(2026, 10, 24, 9, 5, 7)));
}
