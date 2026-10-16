using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Benchmarks.PatchLab.Tree;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchTreeF2EvidenceTests
{
    private const string Family = "dotnet-aspnetcore-win-x64";
    private const string BaseVersion = "v0";
    private const string TargetVersion = "v1";
    private const string PathName = "lib/x.dll";
    private static readonly string Digest0 = new('0', 64);
    private static readonly string Digest1 = new('1', 64);
    private static readonly string Digest2 = new('2', 64);
    private static readonly string Digest3 = new('3', 64);

    [Fact]
    public void FilenameUsesCompleteIdentityCaseSensitiveNotPathBasename()
    {
        string a = PatchTreeF2Evidence.Filename(Family, BaseVersion, TargetVersion, PathName);
        Assert.Equal(69, a.Length);
        Assert.EndsWith(".json", a, StringComparison.Ordinal);
        Assert.NotEqual(a, PatchTreeF2Evidence.Filename(Family, BaseVersion, TargetVersion, "x.dll"));
        Assert.NotEqual(a, PatchTreeF2Evidence.Filename(Family, BaseVersion, TargetVersion, "lib/X.dll"));
        Assert.NotEqual(a, PatchTreeF2Evidence.Filename(Family, TargetVersion, BaseVersion, PathName));
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("added")]
    public void ValidationAcceptsFullFourOrFiveLanePhysicalWireOnly(string kind)
    {
        var pair = Pair();
        var row = Row(kind);
        var observation = Observation(kind);
        var record = PatchTreeF2Evidence.ValidateOne(
            pair, row, observation, "sidecar.json", Digest3);
        Assert.Equal(100, record.RelativeLocalPhysicalBytes);
        Assert.Equal(100, record.NoDictionaryPhysicalBytes);
        Assert.Equal(kind, record.Kind);
        Assert.Equal("sidecar.json", record.ObservationFile);
    }

    [Fact]
    public void ValidationRejectsTamperedPhysicalParityTargetAndBase()
    {
        var pair = Pair();
        var row = Row("changed");
        var obs = Observation("changed");

        Assert.Throws<InvalidDataException>(() =>
            PatchTreeF2Evidence.ValidateOne(pair, row,
                obs with { TargetSha256 = Digest2 }, "x.json", Digest3));
        Assert.Throws<InvalidDataException>(() =>
            PatchTreeF2Evidence.ValidateOne(pair, row,
                obs with { Lanes = obs.Lanes[..^1] }, "x.json", Digest3));
        Assert.Throws<InvalidDataException>(() =>
            PatchTreeF2Evidence.ValidateOne(pair, row,
                obs with { Lanes = [.. obs.Lanes.Select(x =>
                    x.Name == "t1-relative-local" ? x with { PhysicalBytes = 99 } : x)] },
                "x.json", Digest3));
        Assert.Throws<InvalidDataException>(() =>
            PatchTreeF2Evidence.ValidateOne(pair, row,
                obs with { Lanes = [.. obs.Lanes.Select(x =>
                    x.Name == "t1-h0-parity" ? x with { CspSha256 = Digest2 } : x)] },
                "x.json", Digest3));
        Assert.Throws<InvalidDataException>(() =>
            PatchTreeF2Evidence.ValidateOne(pair, row,
                obs with { Lanes = [.. obs.Lanes.Select(x =>
                    x.Name == "t1-no-dictionary" ? x with { BaseManifestId = Digest2 } : x)] },
                "x.json", Digest3));
    }

    [Fact]
    public void AddedCannotInventStandaloneT0Csp()
    {
        var pair = Pair();
        var added = Row("added");
        var obs = Observation("added");
        Assert.Throws<InvalidDataException>(() =>
            PatchTreeF2Evidence.ValidateOne(pair, added with { T0PatchSha256 = Digest0 },
                obs, "x.json", Digest3));
        Assert.Throws<InvalidDataException>(() =>
            PatchTreeF2Evidence.ValidateOne(pair, added with { T0Bytes = 101 },
                obs, "x.json", Digest3));
    }

    [Fact]
    public void FlagsMustBePairedAndF1F2AreMutuallyExclusive()
    {
        Assert.Throws<PatchLabUsageException>(() =>
            PatchTreeCalibration.Execute(["--f2-dir", "any"]));
        Assert.Throws<PatchLabUsageException>(() =>
            PatchTreeCalibration.Execute(["--f2-output", "any.json"]));
        Assert.Throws<PatchLabUsageException>(() =>
            PatchTreeCalibration.Execute(["--f1-dir", "a", "--f1-output", "a.json",
                "--f2-dir", "b", "--f2-output", "b.json"]));
    }

    private static PatchTreeF2Physical.Lane Lane(string name) =>
        new(name, 100, 100, Digest0, Digest1, Digest2, 100, 0, 0, 0, 0, 1, 1.0, 1.0, true);

    private static PatchTreeF2Physical.Summary Observation(string kind)
    {
        var lanes = new List<PatchTreeF2Physical.Lane>();
        if (kind == "changed") lanes.Add(Lane("t0-public"));
        lanes.AddRange([
            Lane("t1-public"), Lane("t1-h0-parity"),
            Lane("t1-relative-local"), Lane("t1-no-dictionary")]);
        return new PatchTreeF2Physical.Summary(PatchTreeF2Physical.Schema,
            PatchTreeF2Physical.Status, Digest3, [.. lanes]);
    }

    private static PatchTreeCalibration.Row Row(string kind) =>
        new(Family, BaseVersion, TargetVersion, PathName, kind, 100,
            100, 100, 100, kind == "changed" ? Digest0 : null,
            Digest0, Digest3, 1, 1, 1, 1, 2, 100, 3, 1, 1, 1, 1);

    private static PatchTreeCalibration.PairResult Pair() =>
        new(Family, BaseVersion, TargetVersion, Digest1, 10, 10, 10,
            1, 0, 0, 0, 100, 100, 110, 110, 110, 120, 1, 1, 1, 1, [Row("changed")]);
}
