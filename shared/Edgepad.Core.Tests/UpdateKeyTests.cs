using System.Security.Cryptography;
using Edgepad.Updates;
using Xunit;

namespace Edgepad.Tests;

public sealed class UpdateKeyTests
{
    [Fact]
    public void TheEmbeddedKeyIsTheSharedOne() =>
        Assert.Equal(UpdateKey.PublicKeySpkiBase64, Fixtures.Lines("update-key.txt").Single());

    [Fact]
    public void TheEmbeddedKeyIsAP256PublicKey()
    {
        Assert.True(UpdateKey.IsSet);
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(UpdateKey.Spki(), out var read);
        Assert.Equal(UpdateKey.Spki().Length, read);
        Assert.Equal(256, key.KeySize);
    }
}
