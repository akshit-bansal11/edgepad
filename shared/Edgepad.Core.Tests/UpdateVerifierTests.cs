using System.Security.Cryptography;
using System.Text;
using Edgepad.Updates;
using Xunit;

namespace Edgepad.Tests;

public sealed class UpdateVerifierTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly byte[] Sums = Encoding.UTF8.GetBytes($"{Hash}  Edgepad-5.0.0.exe\n");

    private static (byte[] Signature, byte[] Spki) Sign(byte[] data, ECCurve curve)
    {
        using var key = ECDsa.Create(curve);
        return (key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence), key.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void ASignatureFromTheKeyIsAccepted()
    {
        var (signature, spki) = Sign(Sums, ECCurve.NamedCurves.nistP256);
        Assert.True(UpdateVerifier.VerifySums(Sums, signature, spki));
    }

    [Fact]
    public void OneChangedByteOfTheSumsIsRefused()
    {
        var (signature, spki) = Sign(Sums, ECCurve.NamedCurves.nistP256);
        var flipped = (byte[])Sums.Clone();
        flipped[0] ^= 1;
        Assert.False(UpdateVerifier.VerifySums(flipped, signature, spki));
    }

    [Fact]
    public void OneChangedByteOfTheSignatureIsRefused()
    {
        var (signature, spki) = Sign(Sums, ECCurve.NamedCurves.nistP256);
        signature[^1] ^= 1;
        Assert.False(UpdateVerifier.VerifySums(Sums, signature, spki));
    }

    [Fact]
    public void ASignatureByAnotherKeyIsRefused()
    {
        var (signature, _) = Sign(Sums, ECCurve.NamedCurves.nistP256);
        var (_, otherSpki) = Sign(Sums, ECCurve.NamedCurves.nistP256);
        Assert.False(UpdateVerifier.VerifySums(Sums, signature, otherSpki));
    }

    [Fact]
    public void AP384KeyIsRefusedEvenWithItsOwnValidSignature()
    {
        var (signature, spki) = Sign(Sums, ECCurve.NamedCurves.nistP384);
        Assert.False(UpdateVerifier.VerifySums(Sums, signature, spki));
    }

    [Fact]
    public void EmptyInputsAreRefused()
    {
        var (signature, spki) = Sign(Sums, ECCurve.NamedCurves.nistP256);
        Assert.False(UpdateVerifier.VerifySums([], signature, spki));
        Assert.False(UpdateVerifier.VerifySums(Sums, [], spki));
        Assert.False(UpdateVerifier.VerifySums(Sums, signature, []));
    }

    [Fact]
    public void GarbageInputsAreRefusedWithoutThrowing()
    {
        var (signature, spki) = Sign(Sums, ECCurve.NamedCurves.nistP256);
        var garbage = new byte[] { 1, 2, 3, 4, 5 };
        Assert.False(UpdateVerifier.VerifySums(Sums, garbage, spki));
        Assert.False(UpdateVerifier.VerifySums(Sums, signature, garbage));
        Assert.False(UpdateVerifier.VerifySums(garbage, garbage, garbage));
    }

    [Fact]
    public void TrailingBytesAfterTheKeyAreRefused()
    {
        var (signature, spki) = Sign(Sums, ECCurve.NamedCurves.nistP256);
        Assert.False(UpdateVerifier.VerifySums(Sums, signature, [.. spki, 0]));
    }

    [Fact]
    public void TheHashOfAListedFileIsFoundByItsExactName()
    {
        Assert.True(UpdateVerifier.TryHashFor($"{Hash}  Edgepad-5.0.0.exe\n", "Edgepad-5.0.0.exe", out var hash));
        Assert.Equal(Convert.FromHexString(Hash), hash);
        Assert.True(UpdateVerifier.TryHashFor($"{Hash} *Edgepad-5.0.0.exe\r\n", "Edgepad-5.0.0.exe", out _));
    }

    [Theory]
    [InlineData("", "Edgepad-5.0.0.exe")]
    [InlineData("Edgepad-5.0.0.exe", "Edgepad-5.0.0.exe")]
    [InlineData("edgepad-5.0.0.exe", "Edgepad-5.0.0.exe")]
    [InlineData("Edgepad-5.0.0.exe", "")]
    [InlineData("dist/Edgepad-5.0.0.exe", "dist/Edgepad-5.0.0.exe")]
    [InlineData("dist\\Edgepad-5.0.0.exe", "dist\\Edgepad-5.0.0.exe")]
    [InlineData("dist/Edgepad-5.0.0.exe", "Edgepad-5.0.0.exe")]
    public void ANameThatIsMissingOrDifferentOrCarriesAPathIsRefused(string listed, string asked)
    {
        var sums = listed.Length == 0 ? "" : $"{Hash}  {listed}\n";
        Assert.False(UpdateVerifier.TryHashFor(sums, asked, out _));
    }

    [Fact]
    public void ANameListedTwiceIsRefused() =>
        Assert.False(UpdateVerifier.TryHashFor($"{Hash}  a.exe\n{Hash}  a.exe\n", "a.exe", out _));

    [Fact]
    public void AShortHashIsRefused() =>
        Assert.False(UpdateVerifier.TryHashFor($"{Hash[..62]}  a.exe\n", "a.exe", out _));

    [Fact]
    public void UppercaseHexIsRefused() =>
        Assert.False(UpdateVerifier.TryHashFor($"{Hash.ToUpperInvariant()}  a.exe\n", "a.exe", out _));

    [Fact]
    public void ALineInAnyOtherShapeSpoilsTheWholeList() =>
        Assert.False(UpdateVerifier.TryHashFor($"{Hash}  a.exe\nnot a hash line\n", "a.exe", out _));

    [Fact]
    public void FilesAreHashedAndHashesComparedExactly()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var hash = UpdateVerifier.HashFile(stream);
        Assert.Equal(SHA256.HashData([1, 2, 3]), hash);
        Assert.True(UpdateVerifier.HashesMatch(hash, SHA256.HashData([1, 2, 3])));
        Assert.False(UpdateVerifier.HashesMatch(hash, SHA256.HashData([1, 2, 4])));
        Assert.False(UpdateVerifier.HashesMatch(hash, hash.AsSpan(..31)));
    }
}
