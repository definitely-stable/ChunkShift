using ChunkShift.Patching.Creation;
using ChunkShift.Patching.Encoding;
using ChunkShift.Patching.Format;
using ChunkShift.Primitives;

namespace ChunkShift.Patching.Tests.Creation;

public class PatchEnc005PhaseATests
{
    private const int Kibibyte = 1024;

    public static TheoryData<string> PhaseALanes => new()
    {
        "H4-L1-R2",
        "H7-L1-R2-E75",
        "H9-L9-K4-C16-R1M",
        "H9-L12-K4-C16-R1M",
        "H9-L15-K4-C16-R1M",
    };

    [Theory]
    [MemberData(nameof(PhaseALanes))]
    public async Task PhaseALane_IsDeterministicAndReconstructsExactly(string lane)
    {
        (byte[] baseContent, byte[] targetContent) =
            CreationExecutions.SparseEdits(384 * Kibibyte, 96 * Kibibyte, 0x5EED5001u);
        (byte[] baseManifest, byte[] targetManifest) =
            await CreationExecutions.ManifestsAsync(baseContent, targetContent);

        byte[] first = await CreationExecutions.CreateAsync(
            baseManifest,
            baseContent,
            targetManifest,
            targetContent,
            Policy(lane),
            H2W2());
        byte[] second = await CreationExecutions.CreateAsync(
            baseManifest,
            baseContent,
            targetManifest,
            targetContent,
            Policy(lane),
            H2W2());

        Assert.Equal(first, second);
        Assert.Equal(
            targetContent,
            await CreationTestSupport.ReconstructAsync(first, baseManifest, baseContent));
    }

    [Fact]
    public async Task H7_PreservesH4Bytes_WhenFrozenEarlyExitFires()
    {
        // Repeated text makes the closest dictionary decisively good. H7 may
        // omit rank two, but the resulting stored form must remain H4's.
        byte[] baseContent = RepeatedText(384 * Kibibyte, "base");
        byte[] targetContent = (byte[])baseContent.Clone();

        for (int offset = 32 * Kibibyte; offset < targetContent.Length; offset += 96 * Kibibyte)
        {
            targetContent[offset] ^= 0x01;
        }

        (byte[] baseManifest, byte[] targetManifest) =
            await CreationExecutions.ManifestsAsync(baseContent, targetContent);

        byte[] h4 = await CreationExecutions.CreateAsync(
            baseManifest,
            baseContent,
            targetManifest,
            targetContent,
            Policy("H4-L1-R2"),
            H2W2());
        var trace = new TraceSink();
        byte[] h7 = await CreationExecutions.CreateAsync(
            baseManifest,
            baseContent,
            targetManifest,
            targetContent,
            Policy("H7-L1-R2-E75"),
            H2W2() with { CandidateTraceSink = trace });

        Assert.Equal(h4, h7);
        Assert.Contains(
            trace.Entries,
            entry => entry.CandidateCount >= 2 && entry.ExpensiveTrialCount == 1);
    }

    [Fact]
    public async Task H4_TraceCountsActualCheapAndFinalTrials()
    {
        (byte[] baseContent, byte[] targetContent) =
            CreationExecutions.SparseEdits(384 * Kibibyte, 96 * Kibibyte, 0x5EED5002u);
        (byte[] baseManifest, byte[] targetManifest) =
            await CreationExecutions.ManifestsAsync(baseContent, targetContent);
        var trace = new TraceSink();

        _ = await CreationExecutions.CreateAsync(
            baseManifest,
            baseContent,
            targetManifest,
            targetContent,
            Policy("H4-L1-R2"),
            H2W2() with { CandidateTraceSink = trace });

        Assert.NotEmpty(trace.Entries);

        foreach (CspCandidateTraceEntry entry in trace.Entries)
        {
            Assert.Equal(entry.CandidateCount, entry.CheapTrialCount);
            Assert.Equal(Math.Min(2, entry.CandidateCount), entry.ExpensiveTrialCount);
            Assert.Equal(
                1 + entry.CheapTrialCount + entry.ExpensiveTrialCount,
                entry.TotalCompressionTrialCount);
            Assert.Equal(1 + entry.ExpensiveTrialCount, entry.Level19TrialCount);
            Assert.Equal(
                Math.Min(entry.TargetLength, entry.NoDictionaryFrameBytes),
                entry.BaselineCostBytes);
            Assert.Equal(entry.NoDictionaryFrameBytes, entry.L19NoDictionaryFrameBytes);
            Assert.All(entry.Candidates, candidate =>
            {
                Assert.Equal(1, candidate.CheapLevel);
                Assert.NotNull(candidate.CheapFrameBytes);
                Assert.NotNull(candidate.CheapCostBytes);
                Assert.Equal("offset", candidate.Source);
            });
            Assert.Equal(
                entry.ExpensiveTrialCount,
                entry.Candidates.Count(candidate => candidate.FinalFrameBytes is not null));
        }
    }

    [Theory]
    [InlineData("H9-L9-K4-C16-R1M")]
    [InlineData("H9-L12-K4-C16-R1M")]
    [InlineData("H9-L15-K4-C16-R1M")]
    public async Task H9_TraceHasNoCheapOrLevel19Trials(string lane)
    {
        (byte[] baseContent, byte[] targetContent) =
            CreationExecutions.SparseEdits(256 * Kibibyte, 64 * Kibibyte, 0x5EED5003u);
        (byte[] baseManifest, byte[] targetManifest) =
            await CreationExecutions.ManifestsAsync(baseContent, targetContent);
        var trace = new TraceSink();

        _ = await CreationExecutions.CreateAsync(
            baseManifest,
            baseContent,
            targetManifest,
            targetContent,
            Policy(lane),
            H2W2() with { CandidateTraceSink = trace });

        Assert.NotEmpty(trace.Entries);

        foreach (CspCandidateTraceEntry entry in trace.Entries)
        {
            Assert.Equal(0, entry.CheapTrialCount);
            Assert.Equal(0, entry.Level19TrialCount);
            Assert.Equal(entry.CandidateCount, entry.ExpensiveTrialCount);
            Assert.Equal(1 + entry.ExpensiveTrialCount, entry.TotalCompressionTrialCount);
            Assert.Null(entry.L19NoDictionaryFrameBytes);
            Assert.All(entry.Candidates, candidate =>
            {
                Assert.Null(candidate.CheapLevel);
                Assert.Null(candidate.CheapFrameBytes);
                Assert.Null(candidate.CheapCostBytes);
                Assert.Null(candidate.L19FrameBytes);
                Assert.Null(candidate.L19CostBytes);
            });
        }
    }

    [Fact]
    public async Task H4_PreCancelledAndActiveCancellationSurfaceOperationCanceledException()
    {
        (byte[] baseContent, byte[] targetContent) =
            CreationExecutions.SparseEdits(256 * Kibibyte, 64 * Kibibyte, 0x5EED5004u);
        (byte[] baseManifest, byte[] targetManifest) =
            await CreationExecutions.ManifestsAsync(baseContent, targetContent);

        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                CreationExecutions.CreateAsync(
                    baseManifest,
                    baseContent,
                    targetManifest,
                    targetContent,
                    Policy("H4-L1-R2"),
                    H2W2(),
                    cancelled.Token));
        }

        using var active = new CancellationTokenSource();
        CspCreateExecution execution = H2W2() with
        {
            WorkerDelay = (_, token) =>
            {
                active.Cancel();
                token.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            },
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreationExecutions.CreateAsync(
                baseManifest,
                baseContent,
                targetManifest,
                targetContent,
                Policy("H4-L1-R2"),
                execution,
                active.Token));
    }

    [Fact]
    public async Task H4_HandlesShortBaseReadsWithoutChangingBytes()
    {
        (byte[] baseContent, byte[] targetContent) =
            CreationExecutions.SparseEdits(256 * Kibibyte, 64 * Kibibyte, 0x5EED5005u);
        (byte[] baseManifest, byte[] targetManifest) =
            await CreationExecutions.ManifestsAsync(baseContent, targetContent);
        CspEncoderPolicy policy = Policy("H4-L1-R2");

        byte[] expected = await CreationExecutions.CreateAsync(
            baseManifest,
            baseContent,
            targetManifest,
            targetContent,
            policy,
            H2W2());

        await using var shortReads = new ShortReadSeekableStream(baseContent, maximumRead: 7);
        byte[] actual = await CreationExecutions.CreateAsync(
            new MemoryStream(baseManifest, writable: false),
            shortReads,
            new MemoryStream(targetManifest, writable: false),
            new MemoryStream(targetContent, writable: false),
            policy,
            H2W2());

        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(PhaseALanes))]
    public async Task PhaseALane_HandlesShortTargetReadsWithoutChangingBytes(string lane)
    {
        (byte[] baseContent, byte[] targetContent) =
            CreationExecutions.SparseEdits(256 * Kibibyte, 64 * Kibibyte, 0x5EED5006u);
        (byte[] baseManifest, byte[] targetManifest) =
            await CreationExecutions.ManifestsAsync(baseContent, targetContent);
        CspEncoderPolicy policy = Policy(lane);

        byte[] expected = await CreationExecutions.CreateAsync(
            baseManifest,
            baseContent,
            targetManifest,
            targetContent,
            policy,
            H2W2());

        await using var shortReads = new ShortReadSeekableStream(targetContent, maximumRead: 5);
        byte[] actual = await CreationExecutions.CreateAsync(
            new MemoryStream(baseManifest, writable: false),
            new MemoryStream(baseContent, writable: false),
            new MemoryStream(targetManifest, writable: false),
            shortReads,
            policy,
            H2W2());

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void H4_RankingUsesTotalCostAndLowerOrdinalTieBreak()
    {
        int smallerFrameButFourRefs = CspPatchBuilder.DictionaryCandidateCost(50, 4);
        int largerFrameButOneRef = CspPatchBuilder.DictionaryCandidateCost(80, 1);

        Assert.Equal(178, smallerFrameButFourRefs);
        Assert.Equal(112, largerFrameButOneRef);
        Assert.True(CspPatchBuilder.CompareRankKeys(
            smallerFrameButFourRefs,
            0,
            largerFrameButOneRef,
            1) > 0);

        Assert.True(CspPatchBuilder.CompareRankKeys(
            leftCost: 112,
            leftOrdinal: 3,
            rightCost: 112,
            rightOrdinal: 4) < 0);
    }

    [Fact]
    public async Task H4_CorruptLosingCandidateDoesNotCreateAnEagerHashFailure()
    {
        byte[] baseContent = RepeatedText(2 * 1024 * Kibibyte, "losing-candidate");
        byte[] targetContent = (byte[])baseContent.Clone();

        for (int offset = 256 * Kibibyte; offset < targetContent.Length; offset += 512 * Kibibyte)
        {
            targetContent[offset] ^= 0x31;
        }

        (byte[] baseManifest, byte[] targetManifest) =
            await CreationExecutions.ManifestsAsync(baseContent, targetContent);
        var beforeTrace = new TraceSink();

        _ = await CreationExecutions.CreateAsync(
            baseManifest,
            baseContent,
            targetManifest,
            targetContent,
            Policy("H4-L1-R2"),
            H2W2() with { CandidateTraceSink = beforeTrace });

        List<ChunkInfo> records = await CreationTestSupport.ReadRecordsAsync(baseManifest);
        bool provedLosingCorruption = false;

        foreach (CspCandidateTraceEntry entry in beforeTrace.Entries)
        {
            if (entry.SelectedCandidate is not int selectedOrdinal ||
                entry.Candidates.Count < 2)
            {
                continue;
            }

            CspCandidateTraceCandidate selected =
                entry.Candidates.Single(candidate => candidate.Ordinal == selectedOrdinal);
            int selectedEnd = selected.StartIndex + selected.RecordCount;

            foreach (CspCandidateTraceCandidate loser in entry.Candidates
                .Where(candidate => candidate.Ordinal != selectedOrdinal)
                .OrderByDescending(candidate => candidate.CheapCostBytes)
                .ThenByDescending(candidate => candidate.Ordinal))
            {
                int uniqueRecord = -1;

                for (int index = loser.StartIndex;
                    index < loser.StartIndex + loser.RecordCount;
                    index++)
                {
                    if (index < selected.StartIndex || index >= selectedEnd)
                    {
                        uniqueRecord = index;
                        break;
                    }
                }

                if (uniqueRecord < 0)
                {
                    continue;
                }

                byte[] corrupt = (byte[])baseContent.Clone();
                corrupt[checked((int)records[uniqueRecord].Offset)] ^= 0x01;
                var afterTrace = new TraceSink();
                byte[] patch;

                try
                {
                    patch = await CreationExecutions.CreateAsync(
                        baseManifest,
                        corrupt,
                        targetManifest,
                        targetContent,
                        Policy("H4-L1-R2"),
                        H2W2() with { CandidateTraceSink = afterTrace });
                }
                catch (InvalidDataException)
                {
                    // This corruption may legitimately change ranking so a
                    // dictionary containing the damaged record wins. Try the
                    // next deterministic losing edge; eager verification would
                    // make every losing edge fail and the final assertion catch it.
                    continue;
                }

                CspCandidateTraceEntry after = afterTrace.Entries.Single(
                    candidateEntry => candidateEntry.TargetChunkId == entry.TargetChunkId);

                if (after.SelectedCandidate == loser.Ordinal)
                {
                    continue;
                }

                Assert.Equal(
                    targetContent,
                    await CreationTestSupport.ReconstructAsync(
                        patch,
                        baseManifest,
                        baseContent));
                provedLosingCorruption = true;
                break;
            }

            if (provedLosingCorruption)
            {
                break;
            }
        }

        Assert.True(
            provedLosingCorruption,
            "The fixture must expose a corrupt losing candidate that remains non-authoritative.");
    }

    [Fact]
    public async Task H4_CorruptSelectedDictionaryStillFailsCreate()
    {
        byte[] baseContent = RepeatedText(512 * Kibibyte, "dictionary");
        byte[] targetContent = (byte[])baseContent.Clone();
        targetContent[targetContent.Length / 2] ^= 0x5A;
        (byte[] baseManifest, byte[] targetManifest) =
            await CreationExecutions.ManifestsAsync(baseContent, targetContent);
        CspEncoderPolicy policy = Policy("H4-L1-R2");

        byte[] patch = await CreationExecutions.CreateAsync(
            baseManifest,
            baseContent,
            targetManifest,
            targetContent,
            policy,
            H2W2());
        CspReader reader = await CreationTestSupport.OpenAsync(patch);
        ChunkId? chosen = null;

        for (int ordinal = 0; ordinal < reader.Index.Count && chosen is null; ordinal++)
        {
            if (reader.Index[ordinal].DictionaryCount == 0)
            {
                continue;
            }

            chosen = (await reader.ReadEntryAsync(ordinal)).DictionaryChunkIds[0];
        }

        Assert.NotNull(chosen);
        ChunkInfo record = (await CreationTestSupport.ReadRecordsAsync(baseManifest))
            .First(item => item.Id == chosen!.Value);
        byte[] corrupt = (byte[])baseContent.Clone();
        corrupt[checked((int)record.Offset)] ^= 0x01;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreationExecutions.CreateAsync(
                baseManifest,
                corrupt,
                targetManifest,
                targetContent,
                policy,
                H2W2()));
    }

    private static CspCreateExecution H2W2() => new(2, UseBaseCandidateCache: false);

    private static CspEncoderPolicy Policy(string lane) => lane switch
    {
        "H4-L1-R2" => CspEncoderPolicy.Default with
        {
            CandidateSelection = CspCandidateSelection.RankLevel1Top2,
        },
        "H7-L1-R2-E75" => CspEncoderPolicy.Default with
        {
            CandidateSelection = CspCandidateSelection.RankLevel1Top2EarlyExit75,
        },
        "H9-L9-K4-C16-R1M" => H9(9),
        "H9-L12-K4-C16-R1M" => H9(12),
        "H9-L15-K4-C16-R1M" => H9(15),
        _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, "Unknown Phase-A lane."),
    };

    private static CspEncoderPolicy H9(int level) =>
        new(level, DictionaryChunks: 4, MaxCandidates: 16, SearchRadius: 1024 * Kibibyte)
        {
            DictionaryLoad = CspDictionaryLoad.Prefix,
            DictionaryHashLog = 20,
            DictionaryChainLog = 20,
        };

    private static byte[] RepeatedText(int length, string marker)
    {
        byte[] line = System.Text.Encoding.UTF8.GetBytes(
            $"ChunkShift PATCH-ENC-005 {marker}: repeated dictionary material.\n");
        var result = new byte[length];

        for (int offset = 0; offset < result.Length;)
        {
            int count = Math.Min(line.Length, result.Length - offset);
            line.AsSpan(0, count).CopyTo(result.AsSpan(offset, count));
            offset += count;
        }

        return result;
    }

    private sealed class TraceSink : ICspCandidateTraceSink
    {
        private readonly object _gate = new();

        internal List<CspCandidateTraceEntry> Entries { get; } = [];

        public void Record(CspCandidateTraceEntry entry)
        {
            lock (_gate)
            {
                Entries.Add(entry);
            }
        }
    }

    private sealed class ShortReadSeekableStream(byte[] bytes, int maximumRead)
        : Stream
    {
        private readonly MemoryStream _inner = new(bytes, writable: false);

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, Math.Min(count, maximumRead));

        public override int Read(Span<byte> buffer) =>
            _inner.Read(buffer[..Math.Min(buffer.Length, maximumRead)]);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(
                buffer[..Math.Min(buffer.Length, maximumRead)],
                cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
