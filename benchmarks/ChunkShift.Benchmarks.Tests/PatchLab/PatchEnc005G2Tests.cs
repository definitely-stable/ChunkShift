using ChunkShift.Benchmarks.PatchLab;

namespace ChunkShift.Benchmarks.Tests.PatchLab;

public sealed class PatchEnc005G2Tests
{
    [Fact]
    public void Sample_key_matches_frozen_null_separated_preimage()
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
    public void Sample_key_rejects_non_lowercase_chunk_hex()
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
    public void Pair_sample_is_digest_then_path_then_index_and_capped_at_64()
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
                    (69 - index).ToString("x64"))),
        ];

        PatchEnc005G2SampleRow[] selected =
            PatchEnc005G2Protocol.SelectPairSample(rows);

        Assert.Equal(64, selected.Length);
        Assert.Equal(69, selected[0].TargetIndex);
        Assert.Equal(6, selected[^1].TargetIndex);
    }

    [Fact]
    public void Whole_base_order_is_nearest_first_with_lower_index_ties()
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
    public void Whole_base_order_enumerates_every_start_once()
    {
        int[] starts =
        [
            .. PatchEnc005G2Protocol.WholeBaseCandidateStarts(
                [0L, 64L, 128L, 192L, 256L],
                129),
        ];

        Assert.Equal(5, starts.Length);
        Assert.Equal(5, starts.Distinct().Count());
        Assert.Equal([2, 1, 3, 0, 4], starts);
    }

    [Fact]
    public void Production_h0_wrapper_uses_same_nearest_first_semantics()
    {
        int[] starts = PatchEnc005G2Protocol.ProductionH0CandidateStartsForTests(
            [0L, 100L, 200L, 300L],
            150);

        Assert.Equal([1, 2, 0, 3], starts);
    }

    [Fact]
    public void Candidate_measurement_reuses_production_k4_and_one_mib_bound()
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
    public void Candidate_cost_uses_frozen_ref32_accounting()
    {
        Assert.Equal(
            1000 + (4 * 32),
            PatchEnc005G2Protocol.CandidateCostForTests(1000, 4));
    }

    [Fact]
    public void Sample_rows_hash_is_order_sensitive_and_deterministic()
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
