using System.Security.Cryptography;
using System.Text;
using ChunkShift.Benchmarks.PatchLab.Tree;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeManifestCandidateTests
{
    private const string ZeroDigest = "0000000000000000000000000000000000000000000000000000000000000000";

    private static PatchTreeManifestCandidate.Entry[] Sample() =>
    [
        new("empty", PatchTreeManifestCandidate.Kind.Directory),
        new("bin/current", PatchTreeManifestCandidate.Kind.SymbolicLink,
            LinkTarget: "app.dll"),
        new("bin/app.dll", PatchTreeManifestCandidate.Kind.File,
            Length: 42, ContentSha256: ZeroDigest, Executable: true),
        new("bin", PatchTreeManifestCandidate.Kind.Directory)
    ];

    [Fact]
    public void CanonicalBytesHaveIndependentPinnedShaAndDoNotDependOnInputOrder()
    {
        var m1 = PatchTreeManifestCandidate.Build(Sample());
        var m2 = PatchTreeManifestCandidate.Build(Sample().Reverse().ToArray());
        Assert.Equal(PatchTreeManifestCandidate.Schema, m1.Schema);
        Assert.Equal(4, m1.EntryCount);
        Assert.Equal(42, m1.FileContentBytes);
        Assert.Equal(145, m1.CanonicalBytes.Length);
        // Computed separately with Python struct.pack("<i"/"<q") and SHA256.
        Assert.Equal("e17fd2ce1f210e05dd995551f1218b38ef7b0baae8675a33c3d58eaa05a3abcc",
            m1.IdentitySha256);
        Assert.Equal(m1.IdentitySha256, m2.IdentitySha256);
        Assert.Equal(m1.CanonicalBytes, m2.CanonicalBytes);
        Assert.Equal(m1.IdentitySha256, Convert.ToHexStringLower(
            SHA256.HashData(m1.CanonicalBytes)));
        Assert.Equal(Encoding.ASCII.GetBytes(PatchTreeManifestCandidate.Schema + "\0"),
            m1.CanonicalBytes.AsSpan(0, PatchTreeManifestCandidate.Schema.Length + 1).ToArray());
    }

    [Fact]
    public void FileSizeDigestExecutableBitAndSymlinkAreIdentityBearing()
    {
        var input = Sample();
        var original = PatchTreeManifestCandidate.Build(input).IdentitySha256;
        Assert.NotEqual(original, PatchTreeManifestCandidate.Build(
            input.Select(e => e.Path == "bin/app.dll" ? e with { Length = 43 } : e).ToArray())
            .IdentitySha256);
        Assert.NotEqual(original, PatchTreeManifestCandidate.Build(
            input.Select(e => e.Path == "bin/app.dll" ? e with { Executable = false } : e).ToArray())
            .IdentitySha256);
        Assert.NotEqual(original, PatchTreeManifestCandidate.Build(
            input.Select(e => e.Path == "bin/app.dll" ?
                e with { ContentSha256 = new string('1', 64) } : e).ToArray()).IdentitySha256);
        Assert.NotEqual(original, PatchTreeManifestCandidate.Build(
            input.Where(e => e.Path != "empty").ToArray()).IdentitySha256);
        Assert.NotEqual(original, PatchTreeManifestCandidate.Build(
            input.Where(e => e.Path != "bin/current").ToArray()).IdentitySha256);
    }

    [Theory]
    [InlineData("/root")]
    [InlineData("../escape")]
    [InlineData("a/../b")]
    [InlineData("a/./b")]
    [InlineData("C:/base")]
    [InlineData(@"a\b")]
    [InlineData("dir//file")]
    [InlineData("name.")]
    [InlineData("name ")]
    [InlineData("CON")]
    [InlineData("com1.txt")]
    [InlineData("LPT9")]
    [InlineData("a*?b")]
    [InlineData("a:b")]
    [InlineData("ümlaut")]
    public void NonportableOrEscapingPathsRejectBeforeHash(string path)
    {
        Assert.Throws<InvalidDataException>(() =>
            PatchTreeManifestCandidate.Build(
                [new(path, PatchTreeManifestCandidate.Kind.File,
                    Length: 4, ContentSha256: ZeroDigest)]));
    }

    [Fact]
    public void CaseAliasAndExactDuplicateRejectOnEveryHost()
    {
        var a = new PatchTreeManifestCandidate.Entry("File", PatchTreeManifestCandidate.Kind.File,
            Length: 1, ContentSha256: ZeroDigest);
        var b = a with { Path = "file" };
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build([a, b]));
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build([a, a]));
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build(
        [
            new("DIR", PatchTreeManifestCandidate.Kind.Directory),
            new("dir", PatchTreeManifestCandidate.Kind.Directory)
        ]));
    }

    [Fact]
    public void MissingParentsAndSymlinkAncestorsReject()
    {
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build(
            [new("a/b", PatchTreeManifestCandidate.Kind.Directory)]));
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build(
        [
            new("a", PatchTreeManifestCandidate.Kind.File, ContentSha256: ZeroDigest),
            new("a/b", PatchTreeManifestCandidate.Kind.File, ContentSha256: ZeroDigest)
        ]));
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build(
        [
            new("a", PatchTreeManifestCandidate.Kind.SymbolicLink, LinkTarget: "b"),
            new("a/file", PatchTreeManifestCandidate.Kind.File, ContentSha256: ZeroDigest),
            new("b", PatchTreeManifestCandidate.Kind.Directory)
        ]));
    }

    [Fact]
    public void SymlinkMustResolveToExistingLocalNonSymlinkEntry()
    {
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build(
            [new("link", PatchTreeManifestCandidate.Kind.SymbolicLink, LinkTarget: "missing")]));
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build(
        [
            new("a", PatchTreeManifestCandidate.Kind.SymbolicLink, LinkTarget: "b"),
            new("b", PatchTreeManifestCandidate.Kind.SymbolicLink, LinkTarget: "a")
        ]));
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build(
        [
            new("dir", PatchTreeManifestCandidate.Kind.Directory),
            new("other", PatchTreeManifestCandidate.Kind.File, ContentSha256: ZeroDigest),
            new("dir/link", PatchTreeManifestCandidate.Kind.SymbolicLink, LinkTarget: "../other")
        ]));
    }

    [Fact]
    public void IllFormedEntryFieldsAndDigestReject()
    {
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build(
            [new("a", PatchTreeManifestCandidate.Kind.File, Length: -1, ContentSha256: ZeroDigest)]));
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build(
            [new("a", PatchTreeManifestCandidate.Kind.File, ContentSha256: new string('Z', 64))]));
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build(
            [new("a", PatchTreeManifestCandidate.Kind.File, ContentSha256: "abcd")]));
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build(
            [new("a", PatchTreeManifestCandidate.Kind.Directory, Executable: true)]));
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build(
            [new("a", (PatchTreeManifestCandidate.Kind)255)]));
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build([]));
    }

    [Fact]
    public void PathAndEntryHardLimitsReject()
    {
        string segment = new('x', 256);
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build(
            [new(segment, PatchTreeManifestCandidate.Kind.Directory)]));
        var many = Enumerable.Range(0, PatchTreeManifestCandidate.MaximumEntries + 1)
            .Select(i => new PatchTreeManifestCandidate.Entry($"f{i:D6}",
                PatchTreeManifestCandidate.Kind.File, ContentSha256: ZeroDigest)).ToArray();
        Assert.Throws<InvalidDataException>(() => PatchTreeManifestCandidate.Build(many));
    }
}
