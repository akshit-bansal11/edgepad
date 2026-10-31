using Edgepad.Network;
using Xunit;

namespace Edgepad.Tests;

public sealed class FirewallMemoryTests : IDisposable
{
    private const string Exe = @"C:\Users\Owner\Downloads\Edgepad-4.0.0.exe";

    private readonly string dir = Path.Combine(Path.GetTempPath(), "edgepad-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(dir, "firewall.txt");

    [Theory]
    [InlineData(Exe, Exe, false)]
    [InlineData(@"c:\users\owner\downloads\EDGEPAD-4.0.0.EXE", Exe, false)]
    [InlineData(@"C:\Users\Owner\Downloads\Edgepad-3.9.0.exe", Exe, true)]
    [InlineData(null, Exe, true)]
    [InlineData("", Exe, true)]
    [InlineData(Exe, null, false)]
    [InlineData(null, null, false)]
    public void OnlyADifferentKnownExeIsNew(string? remembered, string? exe, bool expected) =>
        Assert.Equal(expected, FirewallMemory.IsNew(remembered, exe));

    [Fact]
    public void WhatIsRememberedIsReadBackAndIsNotNew()
    {
        new FirewallMemory(FilePath).Remember(Exe);

        var remembered = FirewallMemory.ForCurrentUser(dir).Load();

        Assert.Equal(Exe, remembered);
        Assert.False(FirewallMemory.IsNew(remembered, Exe));
    }

    [Fact]
    public void RememberingANewerExeReplacesTheOlder()
    {
        var memory = new FirewallMemory(FilePath);
        memory.Remember(@"C:\Users\Owner\Downloads\Edgepad-3.9.0.exe");

        memory.Remember(Exe);

        Assert.Equal(Exe, memory.Load());
    }

    [Fact]
    public void NoFileIsNothingRemembered() => Assert.Null(new FirewallMemory(FilePath).Load());

    [Fact]
    public void AnUnreadableFileIsNothingRemembered()
    {
        Directory.CreateDirectory(FilePath);

        Assert.Null(new FirewallMemory(FilePath).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
