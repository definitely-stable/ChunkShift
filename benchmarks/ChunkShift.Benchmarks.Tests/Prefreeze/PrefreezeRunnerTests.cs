using System.Security.Cryptography;
using System.Text.Json;
using ChunkShift.Benchmarks.Lab;
using ChunkShift.Benchmarks.Lab.Prefreeze;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.Tests.Prefreeze;

public class PrefreezeRunnerTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(PrefreezeCandidate.Current)]
    [InlineData(PrefreezeCandidate.WarmedPrefix)]
    [InlineData(PrefreezeCandidate.Fixed)]
    public void StreamChunkingMatchesSpanChunkingForAnyReadSegmentation(string name)
    {
        PrefreezeCandidate candidate = PrefreezeCandidate.Create(name, 4096);
        byte[] data = Generate("game-pak-like", 3 * 1024 * 1024 + 11);

        ChunkRecord[] expected = candidate.Chunk(data, HashSuiteIds.Blake3256V1);

        using var whole = new MemoryStream(data, writable: false);
        using var shortReads = new ShortReadStream(data, seed: 7);

        Assert.Equal(expected, candidate.Chunk(whole, HashSuiteIds.Blake3256V1));
        Assert.Equal(expected, candidate.Chunk(shortReads, HashSuiteIds.Blake3256V1));
    }

    [Theory]
    [InlineData(64 * 1024)]
    [InlineData(256 * 1024)]
    public void EverySingleByteRunIsForcedToMaximumUnderBothSemantics(int target)
    {
        // A run of one byte value never satisfies either predicate: not in the
        // current candidate's transient (state g * (2^(k+1) - 1)) and not in the
        // steady state both candidates reach (g * (2^64 - 1)).
        PrefreezeCandidate current = PrefreezeCandidate.Create(PrefreezeCandidate.Current, target);
        PrefreezeCandidate warmed = PrefreezeCandidate.Create(PrefreezeCandidate.WarmedPrefix, target);
        byte[] run = new byte[current.Maximum + 1];

        for (int value = 0; value < 256; value++)
        {
            run.AsSpan().Fill((byte)value);
            Assert.Equal(current.Maximum, current.FindCut(run));
            Assert.Equal(warmed.Maximum, warmed.FindCut(run));
        }
    }

    [Fact]
    public void SyntheticRunCoversEveryPlannedCellDeterministically()
    {
        PrefreezePlan plan = SmallPlan();

        PrefreezeRun first = PrefreezeRunner.Execute(plan, null, synthetic: true, null, TextWriter.Null);
        PrefreezeRun second = PrefreezeRunner.Execute(plan, null, synthetic: true, null, TextWriter.Null);

        int cells = plan.Lanes[0].Corpus.Length * plan.Lanes[0].Targets.Length;
        int mutationsWithAnchor = first.Rows.Count(static row => row.MutationKind == "boundary-edit") / 3;

        Assert.Equal(cells, first.Divergence.Length);
        Assert.Equal(cells * 3 * 2 + (mutationsWithAnchor * 3), first.Rows.Length);
        Assert.Equal(JsonSerializer.Serialize(first.Rows, Json), JsonSerializer.Serialize(second.Rows, Json));
        Assert.Equal(JsonSerializer.Serialize(first.Divergence, Json), JsonSerializer.Serialize(second.Divergence, Json));

        // On all-zero data every cut is forced, so there is no boundary to edit.
        Assert.DoesNotContain(first.Rows, static row => row.CorpusId == "zero" && row.MutationKind == "boundary-edit");
        Assert.Contains(first.Rows, static row => row.CorpusId == "random" && row.MutationKind == "boundary-edit");

        PrefreezeRow warmed = first.Rows.First(static row => row.Candidate == PrefreezeCandidate.WarmedPrefix);
        Assert.StartsWith("lab.", warmed.ProfileId, StringComparison.Ordinal);
        Assert.Equal(GearCandidate.WarmedAlgorithmId, warmed.Algorithm);

        // Synthetic families are aggregated too, but never count toward selection.
        Assert.All(first.Families, static family => Assert.Equal(PrefreezeAggregation.Synthetic, family.History));
        Assert.All(first.FamilyComparison, static comparison => Assert.Equal(0, comparison.SelectionEligibleFamilies));

        string markdown = PrefreezeRunner.Summarize(first);
        Assert.Contains("Current vs warmed-prefix", markdown, StringComparison.Ordinal);
        Assert.Contains("## Family-level results", markdown, StringComparison.Ordinal);
        Assert.Contains("## Across families", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundaryEditLandsAtTheRequestedAnchor()
    {
        byte[] source = Generate("random", 1024 * 1024);
        PrefreezeCandidate current = PrefreezeCandidate.Create(PrefreezeCandidate.Current, 4096);
        ChunkRecord[] chunks = current.Chunk(source, HashSuiteIds.Blake3256V1);
        var edit = new PrefreezeMutation("edit", "boundary-edit", 1, 0, "minimum", -1);

        MutationResult mutation = PrefreezeRunner.Mutate(source, edit, chunks, current.Minimum, current.Maximum)!;

        int changed = Enumerable.Range(0, source.Length).Single(index => source[index] != mutation.Target[index]);
        Assert.Equal(changed, mutation.AffectedTargetStart);
        Assert.Contains(chunks, chunk => chunk.Offset + current.Minimum - 1 == changed);
    }

    [Fact]
    public void CoalescingMergesRangesWithinTheGap()
    {
        var needed = new List<(long Start, long End)> { (0, 10), (15, 20), (100, 110) };

        Assert.Equal(new RangeProjection(0, 3, 25), PrefreezeScorecard.Coalesce(needed, 0));
        Assert.Equal(new RangeProjection(5, 2, 30), PrefreezeScorecard.Coalesce(needed, 5));
        Assert.Equal(new RangeProjection(80, 1, 110), PrefreezeScorecard.Coalesce(needed, 80));
    }

    [Fact]
    public void RealCorpusRunsAdjacentAndSkippedTransitionsAndLabelsShortHistory()
    {
        string directory = Directory.CreateTempSubdirectory("chunkshift-prefreeze-").FullName;

        try
        {
            string manifestPath = WriteFamily(directory, versions: 4, split: RealCorpus.Calibration, corruptDigest: false);
            PrefreezePlan plan = SmallPlan();

            PrefreezeRun run = PrefreezeRunner.Execute(plan, null, synthetic: false, manifestPath, TextWriter.Null);

            // 3 adjacent + 2 skip-2 + 1 skip-3 transitions, 3 candidates, 1 target.
            Assert.Equal(6 * 3, run.Rows.Length);
            Assert.Equal(3 * 2, run.Rows.Count(static row => row.MutationKind == "version-skip-2"));
            Assert.All(run.Rows, static row => Assert.Equal(RealCorpus.Calibration, row.Split));
            Assert.All(run.Rows, static row => Assert.Equal(ResynchronizationStatuses.NotApplicable, row.ResynchronizationStatus));
            Assert.Equal(4, run.Divergence.Length);
            Assert.Equal(["game"], run.RealCorpus!.ShortHistoryFamilies);
            Assert.Contains(run.RealCorpus.Warnings, static warning => warning.StartsWith("no holdout", StringComparison.Ordinal));
            Assert.All(
                run.Rows.Where(static row => row.MutationKind == "version-adjacent" && row.Candidate != PrefreezeCandidate.Fixed),
                static row => Assert.True(row.ReuseRatio > 0.5));

            // One family-level result per (candidate, scope); raw rows stay.
            Assert.Equal(3 * 2, run.Families.Length);
            Assert.All(run.Families, static family => Assert.Equal(PrefreezeAggregation.ShortHistory, family.History));
            Assert.Equal(
                [3, 3, 3],
                run.Families.Where(static family => family.Scope == PrefreezeAggregation.Adjacent).Select(static family => family.Transitions));
            Assert.Equal(
                [3, 3, 3],
                run.Families.Where(static family => family.Scope == PrefreezeAggregation.Skipped).Select(static family => family.Transitions));
            Assert.All(run.FamilyComparison, static comparison => Assert.Equal(1, comparison.Families));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FamiliesHaveEqualWeightWhateverTheirTransitionCount()
    {
        // Family "long" has three transitions with reuse 0; family "mid" has one
        // with reuse 1. Pooling the rows would give 0.25; per family, 0.5.
        PrefreezeRow[] rows =
        [
            Row("long", "1->2", reusedBytes: 0, missingBytes: 100),
            Row("long", "2->3", reusedBytes: 0, missingBytes: 100),
            Row("long", "3->4", reusedBytes: 0, missingBytes: 100),
            Row("mid", "1->2", reusedBytes: 100, missingBytes: 0),
        ];
        var real = new RealCorpusSummary(2, 0, 2, 0, 2, true, [], ["mid"], []);

        PrefreezeFamilyAggregate[] families = PrefreezeAggregation.ByFamily(rows, real);
        PrefreezeFamilyComparison comparison = Assert.Single(PrefreezeAggregation.AcrossFamilies(families));

        Assert.Equal(PrefreezeAggregation.ShortHistory, families.Single(static family => family.FamilyId == "mid").History);
        Assert.Equal(PrefreezeAggregation.FullHistory, families.Single(static family => family.FamilyId == "long").History);
        Assert.Equal(300, families.Single(static family => family.FamilyId == "long").UniqueMissingPayloadBytes);
        Assert.Equal(2, comparison.ComparedFamilies);
        Assert.Equal(0.5, comparison.MeanReuseRatio!.Value, 12);
        Assert.Equal(0, comparison.WorstReuseRatio!.Value, 12);
        Assert.Equal(0.5, comparison.MeanUniqueMissingPayloadRatio!.Value, 12);
        Assert.Equal(1, comparison.WorstUniqueMissingPayloadRatio!.Value, 12);
    }

    [Fact]
    public void ExtremePairOnlyFamilyNeverMovesTheEligibleComparison()
    {
        PrefreezeRow[] eligible =
        [
            Row("long", "1->2", reusedBytes: 90, missingBytes: 10),
            Row("mid", "1->2", reusedBytes: 70, missingBytes: 30),
        ];

        // Reuse 0 and missing 100%: the worst possible family, but pair-only.
        PrefreezeRow[] withPair = [.. eligible, Row("pair", "1->2", reusedBytes: 0, missingBytes: 100)];
        var real = new RealCorpusSummary(3, 0, 3, 0, 2, true, ["pair"], ["mid"], []);

        PrefreezeFamilyComparison without = Assert.Single(PrefreezeAggregation.AcrossFamilies(PrefreezeAggregation.ByFamily(eligible, real)));
        PrefreezeFamilyAggregate[] families = PrefreezeAggregation.ByFamily(withPair, real);
        PrefreezeFamilyComparison with = Assert.Single(PrefreezeAggregation.AcrossFamilies(families));

        // The pair-only family is still reported at family level.
        Assert.Equal(PrefreezeAggregation.PairOnly, families.Single(static family => family.FamilyId == "pair").History);
        Assert.Equal(3, with.Families);
        Assert.Equal(2, with.SelectionEligibleFamilies);
        Assert.Equal(2, with.ComparedFamilies);
        Assert.Equal(PrefreezeAggregation.SelectionEligibleBasis, with.Basis);

        // Every decision-level number is unchanged.
        Assert.Equal(without with { Families = 3 }, with);
        Assert.Equal(0.8, with.MeanReuseRatio!.Value, 12);
        Assert.Equal(0.7, with.WorstReuseRatio!.Value, 12);
        Assert.Equal(0.3, with.WorstUniqueMissingPayloadRatio!.Value, 12);
    }

    [Fact]
    public void HoldoutOnlyPairIsLabelledAsUnableToCalibrateOrSelect()
    {
        string directory = Directory.CreateTempSubdirectory("chunkshift-prefreeze-").FullName;

        try
        {
            string manifestPath = WriteFamily(directory, versions: 2, split: RealCorpus.Holdout, corruptDigest: false);

            PrefreezeRun run = PrefreezeRunner.Execute(SmallPlan(), null, synthetic: false, manifestPath, TextWriter.Null);

            Assert.Equal(["game"], run.RealCorpus!.PairOnlyFamilies);
            Assert.Contains(run.RealCorpus.Warnings, static warning => warning.StartsWith("no calibration", StringComparison.Ordinal));
            Assert.Contains(run.RealCorpus.Warnings, static warning => warning.StartsWith("pair-only", StringComparison.Ordinal));
            Assert.False(run.RealCorpus.SelectionPossible);
            Assert.All(run.FamilyComparison, static comparison =>
            {
                Assert.Equal(0, comparison.SelectionEligibleFamilies);
                Assert.Equal(0, comparison.ComparedFamilies);
                Assert.Null(comparison.MeanReuseRatio);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("corrupt-digest")]
    [InlineData("no-split")]
    [InlineData("uppercase-digest")]
    [InlineData("short-digest")]
    [InlineData("duplicate-version")]
    [InlineData("empty-version")]
    [InlineData("empty-path")]
    [InlineData("empty-id")]
    public void RealCorpusManifestIsRejectedBeforeAnyChunking(string defect)
    {
        string directory = Directory.CreateTempSubdirectory("chunkshift-prefreeze-").FullName;

        try
        {
            string manifestPath = WriteFamilies(directory, defect, ("game", 3, RealCorpus.Holdout));

            Assert.Throws<InvalidOperationException>(() =>
                PrefreezeRunner.Execute(SmallPlan(), null, synthetic: false, manifestPath, TextWriter.Null));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PairOnlyHoldoutCannotSelectEvenWithAnEligibleCalibrationFamily()
    {
        string directory = Directory.CreateTempSubdirectory("chunkshift-prefreeze-").FullName;

        try
        {
            string manifestPath = WriteFamilies(
                directory, null, ("game-a", 5, RealCorpus.Calibration), ("game-b", 2, RealCorpus.Holdout));

            PrefreezeRun run = PrefreezeRunner.Execute(SmallPlan(), null, synthetic: false, manifestPath, TextWriter.Null);

            Assert.Equal(1, run.RealCorpus!.EligibleCalibrationFamilies);
            Assert.Equal(0, run.RealCorpus.EligibleHoldoutFamilies);
            Assert.False(run.RealCorpus.SelectionPossible);
            Assert.Contains(run.RealCorpus.Warnings, static warning => warning.StartsWith("no selection-eligible holdout", StringComparison.Ordinal));
            Assert.All(
                run.FamilyComparison.Where(static comparison => comparison.Split == RealCorpus.Holdout),
                static comparison =>
                {
                    Assert.Equal(0, comparison.ComparedFamilies);
                    Assert.Null(comparison.MeanReuseRatio);
                    Assert.Null(comparison.WorstUniqueMissingPayloadRatio);
                });
            Assert.Contains("Selection possible: no", PrefreezeRunner.Summarize(run), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PairOnlyCalibrationIsWarnedButDoesNotBlockAnEligibleHoldout()
    {
        string directory = Directory.CreateTempSubdirectory("chunkshift-prefreeze-").FullName;

        try
        {
            string manifestPath = WriteFamilies(
                directory, null, ("game-a", 2, RealCorpus.Calibration), ("game-b", 3, RealCorpus.Holdout));

            PrefreezeRun run = PrefreezeRunner.Execute(SmallPlan(), null, synthetic: false, manifestPath, TextWriter.Null);

            Assert.Equal(0, run.RealCorpus!.EligibleCalibrationFamilies);
            Assert.Equal(1, run.RealCorpus.EligibleHoldoutFamilies);
            Assert.True(run.RealCorpus.SelectionPossible);
            Assert.Contains(run.RealCorpus.Warnings, static warning => warning.StartsWith("no selection-eligible calibration", StringComparison.Ordinal));
            Assert.All(
                run.FamilyComparison.Where(static comparison => comparison.Split == RealCorpus.Calibration),
                static comparison => Assert.Equal(0, comparison.ComparedFamilies));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CheckedInPlanIsValidAndListsEveryCandidate()
    {
        string path = Path.Combine(RepositoryRoot(), "benchmarks", "experiments", "prefreeze.v1.json");
        PrefreezePlan plan = JsonSerializer.Deserialize<PrefreezePlan>(File.ReadAllText(path), Json)!;

        Assert.Equal(PrefreezeCandidate.Names, plan.Candidates);
        Assert.Contains(plan.Lanes, static lane => lane.Name == "fine" && !lane.Exploratory);
        Assert.Contains(plan.Lanes, static lane => lane.Name == "coarse" && lane.Exploratory);

        foreach (PrefreezeLane lane in plan.Lanes)
        {
            foreach (int target in lane.Targets)
            {
                foreach (string name in plan.Candidates)
                {
                    _ = PrefreezeCandidate.Create(name, target);
                }
            }

            foreach (CorpusEntry entry in lane.Corpus)
            {
                Assert.NotEmpty(CorpusGenerator.Generate(entry with { SizeBytes = 4096 }));
            }
        }
    }

    private static PrefreezeRow Row(string family, string transition, long reusedBytes, long missingBytes) => new(
        "real", false, family, "game-pak", RealCorpus.Holdout, PrefreezeCandidate.Current, "algorithm", "profile", "fingerprint",
        65536, 16384, 262144, $"{family}:{transition}", "version-adjacent",
        100, 100, 1, 100, 1, 0, 100, 100, 100, 100, 0, 0, 1, reusedBytes / 100.0, reusedBytes, reusedBytes / 100.0,
        null, ResynchronizationStatuses.NotApplicable, 0, 0, ChangedBytesBases.None, missingBytes, 0,
        new DistributionProjection([], 0, []), "digest");

    private static PrefreezePlan SmallPlan() => new(
        1,
        HashSuiteIds.Blake3256V1.Value,
        PrefreezeCandidate.Names,
        [
            new PrefreezeLane(
                "test",
                false,
                [4096],
                [
                    new CorpusEntry("random", "random", "random", 512 * 1024, 11, "test"),
                    new CorpusEntry("zero", "zero", "zero", 512 * 1024, 12, "test"),
                ]),
        ],
        [
            new PrefreezeMutation("insert", "insert", 1000, 21),
            new PrefreezeMutation("edit", "boundary-edit", 1, 0, "boundary", 0),
        ]);

    private static string WriteFamily(string directory, int versions, string split, bool corruptDigest) =>
        WriteFamilies(directory, corruptDigest ? "corrupt-digest" : null, ("game", versions, split));

    /// <summary>Writes each family under its own folder; <paramref name="defect"/> breaks the first family.</summary>
    private static string WriteFamilies(string directory, string? defect, params (string Id, int Versions, string Split)[] families)
    {
        var written = new List<RealCorpusFamily>();

        foreach ((string id, int versions, string split) in families)
        {
            Directory.CreateDirectory(Path.Combine(directory, id));
            byte[] content = Generate("random", 256 * 1024);
            var entries = new List<RealCorpusVersion>();

            for (int version = 0; version < versions; version++)
            {
                if (version > 0)
                {
                    content = MutationGenerator.Apply(content, new MutationDefinition("insert", 3000, (ulong)(100 + version))).Target;
                }

                string file = $"{id}/v{version}.bin";
                File.WriteAllBytes(Path.Combine(directory, file), content);
                string digest = Convert.ToHexStringLower(SHA256.HashData(content));
                entries.Add(new RealCorpusVersion($"1.{version}", file, content.Length, digest));
            }

            if (written.Count == 0 && defect is not null)
            {
                RealCorpusVersion first = entries[0];
                entries[0] = defect switch
                {
                    "corrupt-digest" => first with { Sha256 = new string('0', 64) },
                    "uppercase-digest" => first with { Sha256 = first.Sha256.ToUpperInvariant() },
                    "short-digest" => first with { Sha256 = first.Sha256[..63] },
                    "duplicate-version" => first with { Version = entries[1].Version },
                    "empty-version" => first with { Version = " " },
                    "empty-path" => first with { Path = "" },
                    _ => first,
                };
            }

            written.Add(new RealCorpusFamily(
                defect == "empty-id" && written.Count == 0 ? "" : id,
                "game-pak",
                defect == "no-split" && written.Count == 0 ? "" : split,
                "generated by test",
                "test-only",
                "2026-09-25",
                [.. entries]));
        }

        string path = Path.Combine(directory, "real-corpus.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new RealCorpusManifest(1, [.. written]), Json));
        return path;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ChunkShift.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static byte[] Generate(string generator, int size) =>
        CorpusGenerator.Generate(new CorpusEntry("test", "test", generator, size, 0x99_2026UL, "test"));

    private sealed class ShortReadStream(byte[] data, ulong seed) : Stream
    {
        private readonly DeterministicPrng _random = new(seed);
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int length = Math.Min(Math.Min(count, 1 + _random.NextInt32(5000)), data.Length - _position);
            data.AsSpan(_position, length).CopyTo(buffer.AsSpan(offset));
            _position += length;
            return length;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
