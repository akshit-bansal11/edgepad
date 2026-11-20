using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Edgepad.Platform;
using Xunit;

namespace Edgepad.Tests;

public sealed class LinuxUpdaterTests : IDisposable
{
    private static readonly Version V = new(5, 0, 0);
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"edgepad-{Guid.NewGuid():N}");

    public LinuxUpdaterTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string Archive(params (string Name, TarEntryType Type, string Body)[] entries)
    {
        var path = Path.Combine(folder, "a.tar.gz");
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionMode.Compress);
        using var writer = new TarWriter(gzip, TarEntryFormat.Pax);
        foreach (var (name, type, body) in entries)
        {
            var entry = type is TarEntryType.SymbolicLink or TarEntryType.HardLink
                ? new PaxTarEntry(type, name) { LinkName = body }
                : new PaxTarEntry(type, name);
            if (type == TarEntryType.RegularFile)
            {
                entry.DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
            }

            writer.WriteEntry(entry);
        }

        return path;
    }

    private string Dest => Path.Combine(folder, "out");

    private string Elf(byte machine, byte[]? bytes = null)
    {
        var path = Path.Combine(folder, "prog");
        var head = bytes ?? [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 3, 0, machine, 0, 1, 0, 0, 0];
        File.WriteAllBytes(path, head);
        return path;
    }

    [Fact]
    public void AProgramForAnotherCpuIsRefused()
    {
        Assert.True(LinuxUpdater.IsProgramFor(Elf(0x3E), Architecture.X64));
        Assert.True(LinuxUpdater.IsProgramFor(Elf(0xB7), Architecture.Arm64));
        Assert.False(LinuxUpdater.IsProgramFor(Elf(0x3E), Architecture.Arm64));
        Assert.False(LinuxUpdater.IsProgramFor(Elf(0xB7), Architecture.X64));
        Assert.False(LinuxUpdater.IsProgramFor(Elf(0x3E), Architecture.Arm));
        Assert.False(LinuxUpdater.IsProgramFor(Elf(0, [1, 2, 3]), Architecture.X64));
        Assert.False(LinuxUpdater.IsProgramFor(Path.Combine(folder, "missing"), Architecture.X64));
    }

    [Fact]
    public void ExtractsOnlyTheProgramEntry()
    {
        var a = Archive(
            ("edgepad-5.0.0/install.sh", TarEntryType.RegularFile, "other"),
            ("edgepad-5.0.0/edgepad", TarEntryType.RegularFile, "program"));
        Assert.True(LinuxUpdater.ExtractProgram(a, V, Dest));
        Assert.Equal("program", File.ReadAllText(Dest));
    }

    [Theory]
    [InlineData("../evil", TarEntryType.RegularFile)]
    [InlineData("edgepad-5.0.0/../../evil", TarEntryType.RegularFile)]
    [InlineData("/etc/evil", TarEntryType.RegularFile)]
    [InlineData("edgepad-5.0.0/link", TarEntryType.SymbolicLink)]
    [InlineData("edgepad-5.0.0/link", TarEntryType.HardLink)]
    public void AnUnsafeEntryRefusesTheWholeArchive(string name, TarEntryType type)
    {
        var a = Archive(
            ("edgepad-5.0.0/edgepad", TarEntryType.RegularFile, "program"),
            (name, type, "/etc/passwd"));
        Assert.False(LinuxUpdater.ExtractProgram(a, V, Dest));
        Assert.False(File.Exists(Dest));
    }

    [Fact]
    public void AWrongVersionFolderMissingEntrySymlinkedProgramOrOversizeIsRefused()
    {
        Assert.False(LinuxUpdater.ExtractProgram(Archive(("edgepad-4.0.0/edgepad", TarEntryType.RegularFile, "x")), V, Dest));
        Assert.False(LinuxUpdater.ExtractProgram(Archive(("edgepad-5.0.0/edgepad", TarEntryType.SymbolicLink, "/bin/sh")), V, Dest));
        Assert.False(LinuxUpdater.ExtractProgram(Archive(("edgepad-5.0.0/edgepad", TarEntryType.RegularFile, "program")), V, Dest, cap: 3));
        Assert.False(File.Exists(Dest));
    }

    [Fact]
    public void ARepeatedProgramEntryIsRefused()
    {
        var a = Archive(
            ("edgepad-5.0.0/edgepad", TarEntryType.RegularFile, "one"),
            ("edgepad-5.0.0/edgepad", TarEntryType.RegularFile, "two"));
        Assert.False(LinuxUpdater.ExtractProgram(a, V, Dest));
        Assert.False(File.Exists(Dest));
    }

    [Fact]
    public void WritabilityIsFoundByTrying()
    {
        Assert.True(LinuxUpdater.CanWrite(folder));
        Assert.False(LinuxUpdater.CanWrite(Path.Combine(folder, "missing")));
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void TheRootCommandQuotesThePath()
    {
        Assert.Equal(
            "cd '/home/o'\\''x/.cache/edgepad/update' && tar -xzf 'Edgepad-5.0.0-linux-x64.tar.gz' && cd edgepad-5.0.0 && sudo ./install.sh",
            LinuxUpdater.RootCommand("/home/o'x/.cache/edgepad/update/Edgepad-5.0.0-linux-x64.tar.gz", V));
    }
}
