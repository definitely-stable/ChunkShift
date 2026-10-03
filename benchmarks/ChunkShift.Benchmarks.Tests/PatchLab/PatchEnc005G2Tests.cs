using System.Globalization;
using ChunkShift.Benchmarks.PatchLab;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchEnc005G2Tests
{
    [Fact]
    public void SampleKeyMatchesFrozenNullSeparatedPreimage()
    {
        string digest = PatchEnc005G2Protocol.SampleKeySha256(
            "family",
            "1.0",
            "1.1",
            "dir/file.bin",
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            7);

        Assert.Equal(
            "3a6901c35ce4aadd2326bc93cbf1ce0d68a4a0bf431521d3e193bfdf822092e5",
            digest);
    }

    [Fact]
    public void SampleKeyRejectsNonLowercaseChunkHex()
    {
        Assert.Throws<ArgumentException>(() =>
            PatchEnc005G2Protocol.SampleKeySha256(
                "family",
                "1.0",
                "1.1",
                "file",
                new string('A', 64),
                0));
    }

    [Fact]
    public void PairSampleIsDigestThenPathThenIndexAndCappedAt64()
    {
        PatchEnc005G2SampleRow[] rows =
        [
            .. Enumerable.Range(0, 70).Select(index =>
                new PatchEnc005G2SampleRow(
                    "f",
                    "b",
                    "t",
                    index % 2 == 0 ? "b" : "a",
                    index,
                    new string('0', 64),
                    index,
                    1,
                    (69 - index).ToString("x64", CultureInfo.InvariantCulture))),
        ];

        PatchEnc005G2SampleRow[] selected =
            PatchEnc005G2Protocol.SelectPairSample(rows);

        Assert.Equal(64, selected.Length);
        Assert.Equal(69, selected[0].TargetIndex);
        Assert.Equal(6, selected[^1].TargetIndex);
    }

    [Fact]
    public void WholeBaseOrderIsNearestFirstWithLowerIndexTies()
    {
        int[] starts =
        [
            .. PatchEnc005G2Protocol.WholeBaseCandidateStarts(
                [0L, 100L, 200L, 300L],
                150),
        ];

        Assert.Equal([1, 2, 0, 3], starts);
    }

    [Fact]
    public void WholeBaseOrderEnumeratesEveryStartOnce()
    {
        int[] starts =
        [
            .. PatchEnc005G2Protocol.WholeBaseCandidateStarts(
                [0L, 64L, 128L, 192L, 256L],
                129),
        ];

        Assert.Equal(5, starts.Length);
        Assert.Equal(5, starts.Distinct().Count());
        Assert.Equal([2, 3, 1, 4, 0], starts);
    }

    [Fact]
    public void ProductionH0WrapperUsesSameNearestFirstSemantics()
    {
        int[] starts = PatchEnc005G2Protocol.ProductionH0CandidateStartsForTests(
            [0L, 100L, 200L, 300L],
            150);

        Assert.Equal([1, 2, 0, 3], starts);
    }

    [Fact]
    public void CandidateMeasurementReusesProductionK4AndOneMibBound()
    {
        var tooLarge = PatchEnc005G2Protocol.MeasureCandidateForTests(
            [300_000, 300_000, 300_000, 300_000],
            0);
        var tail = PatchEnc005G2Protocol.MeasureCandidateForTests(
            [300_000, 300_000, 300_000, 300_000],
            1);

        Assert.False(tooLarge.Valid);
        Assert.Equal(4, tooLarge.Count);
        Assert.Equal(0, tooLarge.Length);

        Assert.True(tail.Valid);
        Assert.Equal(3, tail.Count);
        Assert.Equal(900_000, tail.Length);
    }

    [Fact]
    public void CandidateCostUsesFrozenRef32Accounting()
    {
        Assert.Equal(
            1000 + (4 * 32),
            PatchEnc005G2Protocol.CandidateCostForTests(1000, 4));
    }

    [Fact]
    public void SampleRowsHashIsOrderSensitiveAndDeterministic()
    {
        var first = new PatchEnc005G2SampleRow(
            "f", "b", "t", "a", 1, new string('0', 64), 10, 20, new string('1', 64));
        var second = first with
        {
            Path = "b",
            TargetIndex = 2,
            SampleKeySha256 = new string('2', 64),
        };

        string a = PatchEnc005G2Protocol.RowsSha256([first, second]);
        string b = PatchEnc005G2Protocol.RowsSha256([first, second]);
        string reversed = PatchEnc005G2Protocol.RowsSha256([second, first]);

        Assert.Equal(a, b);
        Assert.NotEqual(a, reversed);
    }
}
