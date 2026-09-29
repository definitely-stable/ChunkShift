using ChunkShift.Benchmarks.VerifyLab;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests.VerifyLab;

public sealed class VerifyLabOracleTests : IDisposable
{
    private readonly string _scratch = Directory.CreateTempSubdirectory("chunkshift-verify-lab-tests-").FullName;

    public void Dispose() => Directory.Delete(_scratch, recursive: true);

    [Fact]
    public async Task QuickOraclePassesWithTheDeclaredDifferenceOnly()
    {
        VerifyLabOracleReport report = await VerifyLabOracle.RunAsync(
            FixtureDirectory(),
            quick: true,
            _scratch);

        Assert.True(
            report.Passed,
            string.Join(Environment.NewLine, report.Failures.Take(20).Select(static failure =>
                $"{failure.CaseClass} {failure.CaseId} {failure.Lane}: {failure.Reason} (expected {failure.Expected}, actual {failure.Actual})")));
        Assert.True(report.Vectors >= 60);

        // D1 appears once per suite for the 1 MiB content, in V1 and three V2 lanes.
        Assert.Equal(2, report.ProfileNonConformingManifests);
        Assert.Equal(2 * 4, report.DeclaredDifferences);

        // The equivalence is not carried by the fallback alone: every class that
        // should reach the slice path did, and O5 took the fallback.
        Assert.True(report.Modes["O3"]["V1"][VerifyLabVerdict.Slices] > 50);
        Assert.True(report.Modes["O4"]["V2-W8"][VerifyLabVerdict.Slices] > 20);
        Assert.Equal(8, report.Modes["O5"]["V2-W4"].GetValueOrDefault(VerifyLabVerdict.CdcFallback)
            + report.Modes["O5"]["V2-W4"].GetValueOrDefault(VerifyLabVerdict.InvalidData));
        Assert.True(report.Modes["O5"]["V1"].GetValueOrDefault(VerifyLabVerdict.CdcFallback) > 0);
        Assert.True(report.Modes["O2"]["V1"].GetValueOrDefault(VerifyLabVerdict.NotSupported) > 0);
        Assert.True(report.Modes["O2"]["V1"].GetValueOrDefault(VerifyLabVerdict.InvalidData) > 0);
    }

    [Fact]
    public async Task FixedSliceManifestIsTheDeclaredDifference()
    {
        byte[] content = VerifyLabOracle.Random(1 << 20, 7);
        byte[] manifest = await VerifyLabOracle.CreateFixedSliceManifestAsync(content, HashSuiteIds.Blake3256V1, 64 * 1024);

        VerifyLabVerdict v0 = await Run(VerifyLabLane.V0, new VerifyLabMemoryContent(content), manifest);
        VerifyLabVerdict v1 = await Run(VerifyLabLane.V1, new VerifyLabMemoryContent(content), manifest);

        Assert.Equal(ManifestVerificationFailure.Content, v0.Failures);
        Assert.Equal(VerifyLabVerdict.Checked, v0.ProfileConformance);
        Assert.True(v1.IsValid);
        Assert.Equal(VerifyLabVerdict.Slices, v1.ContentMode);
        Assert.Equal(VerifyLabVerdict.NotChecked, v1.ProfileConformance);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    public async Task V2ReportsTheLowestMismatchingRecordWhateverTheWorkerCount(int workers)
    {
        byte[] content = VerifyLabOracle.Random(6 << 20, 11);
        byte[] manifest = await VerifyLabOracle.CreateManifestAsync(content, HashSuiteIds.Blake3256V1);
        VerifyLabRecord[] records = await VerifyLabOracle.LabRecordsAsync(manifest);

        // Two mismatches in different ranges: the earlier one must win.
        VerifyLabRecord early = records[3];
        VerifyLabRecord late = records[^2];
        content[late.Offset] ^= 0xFF;
        content[early.Offset + 1] ^= 0xFF;

        VerifyLabVerdict v1 = await Run(VerifyLabLane.V1, new VerifyLabMemoryContent(content), manifest);
        VerifyLabVerdict v2 = await Run(VerifyLabLane.V2(workers), new VerifyLabMemoryContent(content), manifest);

        Assert.Equal(early.Index, v1.FirstMismatchRecord);
        Assert.Equal(v1, v2);
    }

    [Fact]
    public async Task V2OverAFileMatchesV0()
    {
        byte[] content = VerifyLabOracle.Random(3 << 20, 13);
        byte[] manifest = await VerifyLabOracle.CreateManifestAsync(content, HashSuiteIds.Sha256V1);
        string path = Path.Combine(_scratch, "content.bin");
        await File.WriteAllBytesAsync(path, content);

        VerifyLabVerdict v0 = await Run(VerifyLabLane.V0, new VerifyLabFileContent(path), manifest);
        VerifyLabVerdict v2 = await Run(VerifyLabLane.V2(4), new VerifyLabFileContent(path), manifest);

        Assert.True(v0.IsValid);
        Assert.True(v2.IsValid);
        Assert.Equal(VerifyLabVerdict.Slices, v2.ContentMode);
    }

    [Fact]
    public async Task RecordsLongerThanARangeAreHashedInPieces()
    {
        byte[] content = VerifyLabOracle.Random((VerifyLabLanes.RangeBytes * 2) + 12345, 17);
        byte[] manifest = await VerifyLabOracle.CreateFixedSliceManifestAsync(
            content,
            HashSuiteIds.Blake3256V1,
            VerifyLabLanes.RangeBytes + 1000);

        foreach (VerifyLabLane lane in new[] { VerifyLabLane.V1, VerifyLabLane.V2(2) })
        {
            Assert.True((await Run(lane, new VerifyLabMemoryContent(content), manifest)).IsValid);

            VerifyLabVerdict truncated = await Run(lane, new VerifyLabMemoryContent(content, content.Length - 1), manifest);
            Assert.Equal(2L, truncated.FirstMismatchRecord);

            content[VerifyLabLanes.RangeBytes + 500] ^= 1;
            Assert.Equal(0L, (await Run(lane, new VerifyLabMemoryContent(content), manifest)).FirstMismatchRecord);
            content[VerifyLabLanes.RangeBytes + 500] ^= 1;
        }
    }

    [Theory]
    [InlineData("V1")]
    [InlineData("V2-W4")]
    public async Task CancellationStopsTheLane(string laneName)
    {
        byte[] content = VerifyLabOracle.Random(64 << 20, 19);
        byte[] manifest = await VerifyLabOracle.CreateManifestAsync(content, HashSuiteIds.Blake3256V1);
        VerifyLabLane lane = VerifyLabLane.Parse(laneName);

        // Every request of a throttled channel costs at least 2 ms, so the
        // lane is still running when the token fires.
        VerifyLabThrottleChannel[] channels = [.. Enumerable.Range(0, lane.ChannelCount).Select(static _ => new VerifyLabThrottleChannel())];
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VerifyLabLanes.RunAsync(
            lane,
            new VerifyLabMemoryContent(content),
            () => new MemoryStream(manifest, writable: false),
            channels,
            cancellation.Token));
    }

    [Fact]
    public void ThrottleChannelChargesLatencyPlusSizeAndKeepsOnlyASmallCredit()
    {
        var channel = new VerifyLabThrottleChannel();
        TimeSpan first = channel.Charge(VerifyLabThrottleChannel.MaximumRequestBytes);
        TimeSpan second = channel.Charge(VerifyLabThrottleChannel.MaximumRequestBytes);

        // 2 ms + 1 MiB at 100 MiB/s = 12 ms per request; the second waits for both.
        Assert.InRange(first.TotalMilliseconds, 11.0, 12.5);
        Assert.InRange(second.TotalMilliseconds, 23.0, 24.5);

        var idle = new VerifyLabThrottleChannel();
        Thread.Sleep(100);
        Assert.InRange(idle.Charge(0).TotalMilliseconds, 0.0, 0.001);

        // After the idle time the channel owes at most the 16 ms credit.
        TimeSpan afterIdle = idle.Charge(VerifyLabThrottleChannel.MaximumRequestBytes);
        Assert.InRange(afterIdle.TotalMilliseconds, 0.0, 12.5);
    }

    [Fact]
    public void LaneNamesRoundTrip()
    {
        foreach (string name in new[] { "V0", "V1", "V2-W2", "V2-W4", "V2-W8" })
        {
            Assert.Equal(name, VerifyLabLane.Parse(name).Name);
        }

        Assert.Throws<FormatException>(() => VerifyLabLane.Parse("V2-W0"));
        Assert.Throws<FormatException>(() => VerifyLabLane.Parse("V3"));
    }

    internal static string FixtureDirectory()
    {
        string? directory = AppContext.BaseDirectory;

        while (directory is not null)
        {
            string candidate = Path.Combine(directory, "tests", "ChunkShift.Tests", "Fixtures", "CsmV1");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new DirectoryNotFoundException("The CSM vector directory was not found above the test output.");
    }

    private static Task<VerifyLabVerdict> Run(VerifyLabLane lane, VerifyLabContent content, byte[] manifest) =>
        VerifyLabLanes.RunAsync(
            lane,
            content,
            () => new MemoryStream(manifest, writable: false),
            channels: null,
            CancellationToken.None);
}
