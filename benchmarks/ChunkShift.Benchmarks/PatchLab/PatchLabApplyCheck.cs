using System.Diagnostics;
using ChunkShift.Chunking;
using ChunkShift.Hashing;
using ChunkShift.Manifest;
using ChunkShift.Patching;
using ChunkShift.Patching.Application;
using ChunkShift.Patching.Creation;
using ChunkShift.Patching.Format;
using ChunkShift.Primitives;
using static System.FormattableString;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>
/// <c>patch-lab apply-check</c>: the lanes of <c>PATCH-APPLY-002</c>
/// (docs/benchmarks/PATCH-APPLY-002-PROTOCOL.md) over the frozen patch corpus.
/// <code>
/// patch-lab apply-check prepare    --corpus &lt;root&gt; [--work &lt;dir&gt;] [--output &lt;file&gt;] [--families &lt;id,...&gt;] [--workers &lt;n&gt;] [--run-id &lt;id&gt;]
/// patch-lab apply-check time       --corpus &lt;root&gt; [--work &lt;dir&gt;] --repetition &lt;r&gt; --output &lt;file&gt; [--families &lt;id,...&gt;] [--warmup-files &lt;n&gt;] [--run-id &lt;id&gt;]
/// patch-lab apply-check concurrent --corpus &lt;root&gt; [--work &lt;dir&gt;] --repetition &lt;r&gt; --output &lt;file&gt; [--families &lt;id,...&gt;] [--concurrency &lt;c,...&gt;] [--run-id &lt;id&gt;]
/// patch-lab apply-check memory     --corpus &lt;root&gt; [--work &lt;dir&gt;] --output &lt;file&gt; [--families &lt;id,...&gt;] [--min-bytes &lt;n&gt;] [--run-id &lt;id&gt;]
/// </code>
/// <c>prepare</c> creates one <c>csp</c> patch per changed file under
/// <c>&lt;work&gt;/apply-check/</c>; the other actions apply those patches
/// with the internal re-chunk check switch of <see cref="CspApplier"/>.
/// </summary>
internal static class PatchLabApplyCheck
{
    internal const string Schema = "chunkshift.patch-lab-apply-check.v1";

    private const int DefaultWarmupFiles = 20;
    private const long DefaultMemoryMinBytes = 1024 * 1024;
    private const int BlockBytes = 4096;

    /// <summary>The protocol's lanes, in their base order.</summary>
    internal static IReadOnlyList<string> LaneNames { get; } = ["off", "seq", "overlap"];

    private static readonly int[] DefaultConcurrency = [1, 2, 4, 8];

    internal static int Execute(string[] args)
    {
        if (args.Length == 0)
        {
            throw new PatchLabUsageException(
                "patch-lab apply-check needs an action: prepare, time, concurrent or memory.");
        }

        return args[0] switch
        {
            "prepare" => PrepareAsync(args[1..]).GetAwaiter().GetResult(),
            "time" => TimeAsync(args[1..]).GetAwaiter().GetResult(),
            "concurrent" => ConcurrentAsync(args[1..]).GetAwaiter().GetResult(),
            "memory" => MemoryAsync(args[1..]).GetAwaiter().GetResult(),
            _ => throw new PatchLabUsageException(
                $"Unknown patch-lab apply-check action '{args[0]}'; expected prepare, time, concurrent or memory."),
        };
    }

    /// <summary>Maps a lane name of the protocol to the applier's check.</summary>
    internal static bool TryParseLane(string name, out ChunkingCheck check)
    {
        switch (name)
        {
            case "off":
                check = ChunkingCheck.Off;
                return true;
            case "seq":
                check = ChunkingCheck.Sequential;
                return true;
            case "overlap":
                check = ChunkingCheck.Overlapped;
                return true;
            default:
                check = ChunkingCheck.Sequential;
                return false;
        }
    }

    /// <summary>
    /// Returns the lane order of a repetition: the base order rotated by
    /// <c>r mod 3</c>, run twice (protocol §5).
    /// </summary>
    internal static string[] LaneOrder(int repetition)
    {
        int start = repetition % LaneNames.Count;
        var once = new string[LaneNames.Count];

        for (int index = 0; index < once.Length; index++)
        {
            once[index] = LaneNames[(start + index) % LaneNames.Count];
        }

        return [.. once, .. once];
    }

    /// <summary>Gets the prepared patch of one changed file.</summary>
    internal static string PatchPath(string workDirectory, PatchLabChangedFile file) =>
        Path.Combine(
            workDirectory,
            "apply-check",
            PolicyTag(CspEncoderPolicy.Default),
            file.BaseSha256 + "-" + file.TargetSha256 + ".csp");

    private static string PolicyTag(CspEncoderPolicy policy) =>
        Invariant($"L{policy.Level}-K{policy.DictionaryChunks}-C{policy.MaxCandidates}-R{policy.SearchRadius}");

    private static async Task<int> PrepareAsync(string[] args)
    {
        PatchLabCorpus corpus = LoadCorpus(args, "prepare");
        int workers = PatchLabArguments.PositiveInt(args, "--workers", Environment.ProcessorCount);
        string? output = PatchLabArguments.Value(args, "--output");
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();
        List<Item> items = Items(corpus);
        var files = new PatchLabPreparedFile[items.Count];
        int done = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, items.Count),
            new ParallelOptions { MaxDegreeOfParallelism = workers },
            async (index, cancellationToken) =>
            {
                files[index] = await PrepareFileAsync(corpus, items[index], cancellationToken)
                    .ConfigureAwait(false);
                int finished = Interlocked.Increment(ref done);

                if (finished % 100 == 0 || finished == items.Count)
                {
                    Console.Error.WriteLine(Invariant(
                        $"apply-check prepare: {finished}/{items.Count} files, {clock.Elapsed.TotalSeconds:F1} s"));
                }
            }).ConfigureAwait(false);

        if (output is not null)
        {
            PatchLabRunner.WriteJson(output, new PatchLabApplyCheckPrepareResult(
                Schema,
                "prepare",
                PatchLabArguments.Value(args, "--run-id"),
                PatchLabLane.Describe(CspEncoderPolicy.Default),
                corpus.PairsSha256,
                PatchLabRunner.Snapshot(),
                startedUtc,
                clock.Elapsed.TotalSeconds,
                files));
        }

        return 0;
    }

    /// <summary>
    /// Creates the file's patch once (published by a temporary file and a
    /// move, so an interrupted prepare is resumed), and measures the
    /// record-only alignment share from the two manifests.
    /// </summary>
    private static async Task<PatchLabPreparedFile> PrepareFileAsync(
        PatchLabCorpus corpus,
        Item item,
        CancellationToken cancellationToken)
    {
        string baseContent = corpus.ContentPath(item.Pair, item.Pair.Base, item.File.Path);
        string targetContent = corpus.ContentPath(item.Pair, item.Pair.Target, item.File.Path);
        string baseManifest = await PatchLabManifests
            .EnsureAsync(baseContent, corpus.WorkDirectory, item.File.BaseSha256, cancellationToken)
            .ConfigureAwait(false);
        string targetManifest = await PatchLabManifests
            .EnsureAsync(targetContent, corpus.WorkDirectory, item.File.TargetSha256, cancellationToken)
            .ConfigureAwait(false);
        string patchPath = PatchPath(corpus.WorkDirectory, item.File);

        if (!File.Exists(patchPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(patchPath)!);
            string temporary = patchPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

            try
            {
                await using (FileStream baseManifestStream = PatchLabFiles.OpenRead(baseManifest))
                await using (FileStream baseContentStream = PatchLabFiles.OpenRead(baseContent))
                await using (FileStream targetManifestStream = PatchLabFiles.OpenRead(targetManifest))
                await using (FileStream targetContentStream = PatchLabFiles.OpenRead(targetContent))
                await using (FileStream destination = PatchLabFiles.CreateNew(temporary))
                {
                    _ = await CspPatchBuilder
                        .CreateAsync(
                            baseManifestStream,
                            baseContentStream,
                            targetManifestStream,
                            targetContentStream,
                            destination,
                            CspEncoderPolicy.Default,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                File.Move(temporary, patchPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        List<ChunkInfo> baseRecords = await ReadRecordsAsync(baseManifest, cancellationToken)
            .ConfigureAwait(false);
        List<ChunkInfo> targetRecords = await ReadRecordsAsync(targetManifest, cancellationToken)
            .ConfigureAwait(false);
        (long sameOffsetBytes, long alignedBytes) = Alignment(
            [.. baseRecords.Select(static record => (record.Offset, record.Length, record.Id))],
            [.. targetRecords.Select(static record => (record.Offset, record.Length, record.Id))]);

        return new PatchLabPreparedFile(
            item.Pair.Family,
            item.Pair.Base,
            item.Pair.Target,
            item.File.Path,
            item.File.TargetSize,
            targetRecords.Count,
            new FileInfo(patchPath).Length,
            sameOffsetBytes,
            alignedBytes);
    }

    /// <summary>
    /// Target bytes in records whose <c>ChunkId</c> is the base record at the
    /// same offset, merged into extents, and the whole 4 KiB blocks inside
    /// those extents.
    /// </summary>
    internal static (long SameOffsetBytes, long AlignedBytes) Alignment(
        IReadOnlyList<(long Offset, int Length, ChunkId Id)> baseRecords,
        IReadOnlyList<(long Offset, int Length, ChunkId Id)> targetRecords)
    {
        var baseAt = new Dictionary<long, ChunkId>(baseRecords.Count);

        foreach ((long Offset, int Length, ChunkId Id) record in baseRecords)
        {
            baseAt[record.Offset] = record.Id;
        }

        long same = 0;
        long aligned = 0;
        long extentStart = -1;
        long extentEnd = -1;

        foreach ((long Offset, int Length, ChunkId Id) record in targetRecords)
        {
            bool matches = baseAt.TryGetValue(record.Offset, out ChunkId baseId) && baseId == record.Id;

            if (matches && extentEnd == record.Offset)
            {
                extentEnd += record.Length;
                continue;
            }

            if (extentStart >= 0)
            {
                same += extentEnd - extentStart;
                aligned += WholeBlocks(extentStart, extentEnd);
                extentStart = -1;
            }

            if (matches)
            {
                extentStart = record.Offset;
                extentEnd = record.Offset + record.Length;
            }
        }

        if (extentStart >= 0)
        {
            same += extentEnd - extentStart;
            aligned += WholeBlocks(extentStart, extentEnd);
        }

        return (same, aligned);
    }

    private static long WholeBlocks(long start, long end)
    {
        long first = (start + BlockBytes - 1) / BlockBytes * BlockBytes;
        long last = end / BlockBytes * BlockBytes;
        return Math.Max(0, last - first);
    }

    private static async Task<int> TimeAsync(string[] args)
    {
        PatchLabCorpus corpus = LoadCorpus(args, "time");
        int repetition = Repetition(args);
        string output = RequiredOutput(args, "time");
        int warmupFiles = PatchLabArguments.PositiveInt(args, "--warmup-files", DefaultWarmupFiles);
        string[] order = LaneOrder(repetition);
        List<Item> items = Items(corpus);
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();
        string directory = Directory.CreateTempSubdirectory("chunkshift-apply-check-").FullName;

        try
        {
            // JIT warm-up: every lane once over the first files, unmeasured.
            foreach (Item item in items.Take(warmupFiles))
            {
                foreach (string lane in LaneNames)
                {
                    _ = await ApplyAsync(corpus, item, lane, directory, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }

            var files = new PatchLabApplyCheckFile[items.Count];

            for (int index = 0; index < items.Count; index++)
            {
                files[index] = await TimeFileAsync(corpus, items[index], order, directory)
                    .ConfigureAwait(false);

                if ((index + 1) % 100 == 0 || index + 1 == items.Count)
                {
                    Console.Error.WriteLine(Invariant(
                        $"apply-check time r{repetition}: {index + 1}/{items.Count} files, {clock.Elapsed.TotalSeconds:F1} s"));
                }
            }

            PatchLabRunner.WriteJson(output, new PatchLabApplyCheckTimeResult(
                Schema,
                "time",
                PatchLabArguments.Value(args, "--run-id"),
                repetition,
                order,
                "warm",
                OutputsVerified: true,
                PatchLabLane.Describe(CspEncoderPolicy.Default),
                corpus.PairsSha256,
                PatchLabRunner.Snapshot(),
                startedUtc,
                clock.Elapsed.TotalSeconds,
                files));
            return 0;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// One unmeasured warm-up apply in lane <c>off</c>, the six measured applies
    /// of the repetition's order (the first of each lane checked against the
    /// target digest), then the decomposition of the sequential check.
    /// </summary>
    private static async Task<PatchLabApplyCheckFile> TimeFileAsync(
        PatchLabCorpus corpus,
        Item item,
        string[] order,
        string directory)
    {
        _ = await ApplyAsync(corpus, item, "off", directory, CancellationToken.None)
            .ConfigureAwait(false);

        var runs = LaneNames.ToDictionary(
            static lane => lane,
            static _ => new List<PatchLabCost>(2),
            StringComparer.Ordinal);

        foreach (string lane in order)
        {
            bool verify = runs[lane].Count == 0;
            runs[lane].Add(await ApplyAsync(
                corpus,
                item,
                lane,
                directory,
                CancellationToken.None,
                verify ? item.File.TargetSha256 : null).ConfigureAwait(false));
        }

        string outputPath = Path.Combine(directory, "decomposition.out");
        _ = await ApplyAsync(corpus, item, "off", directory, CancellationToken.None, keepOutput: outputPath)
            .ConfigureAwait(false);

        try
        {
            (PatchLabCheckDecomposition decomposition, long chunks) = await DecomposeAsync(
                PatchPath(corpus.WorkDirectory, item.File),
                outputPath,
                CancellationToken.None).ConfigureAwait(false);

            return new PatchLabApplyCheckFile(
                item.Pair.Family,
                item.Pair.Base,
                item.Pair.Target,
                item.File.Path,
                item.File.TargetSize,
                chunks,
                runs.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.ToArray(),
                    StringComparer.Ordinal),
                decomposition);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    /// <summary>
    /// Measures, one part at a time, the work the sequential check repeats
    /// after the reconstruction (protocol §4), and the whole check. The parts
    /// must reproduce the embedded manifest, so they measure the real work.
    /// </summary>
    internal static async Task<(PatchLabCheckDecomposition Decomposition, long Chunks)> DecomposeAsync(
        string patchPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        await using FileStream patch = PatchLabFiles.OpenRead(patchPath);
        CspReader reader = await CspReader
            .OpenAsync(patch, CspFormat.DefaultMaximumPayloadEntries, cancellationToken)
            .ConfigureAwait(false);
        ManifestInfo target = reader.TargetManifest.Manifest;
        HashSuiteId hashSuite = target.HashSuite;

        PatchLabCost csmParse = await MeasureAsync(async () =>
        {
            CsmReadResult result = await CsmReader
                .ReadAndVerifyAsync(reader.OpenTargetManifest(), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsValid)
            {
                throw new InvalidDataException($"{patchPath}: the embedded CSM does not verify.");
            }
        }).ConfigureAwait(false);

        byte[] readBuffer = new byte[ChunkingKernel.IoBufferSize];
        PatchLabCost reread = await MeasureAsync(async () =>
        {
            await using FileStream stream = OpenOutput(outputPath);

            while (await stream.ReadAsync(readBuffer, cancellationToken).ConfigureAwait(false) != 0)
            {
            }
        }).ConfigureAwait(false);

        byte[] content = await File.ReadAllBytesAsync(outputPath, cancellationToken).ConfigureAwait(false);
        ChunkScanConfiguration.ProfileRegistration registration =
            ChunkScanConfiguration.ResolveProfileRegistration(target.ProfileId);
        var cuts = new List<int>((int)Math.Min(target.ChunkCount, int.MaxValue));

        PatchLabCost boundary = Measure(() =>
        {
            var state = new ChunkBoundaryState(registration.KernelProfile);
            int offset = 0;

            while (offset < content.Length)
            {
                ChunkBoundaryScanResult result = state.Scan(content.AsSpan(offset));
                offset += result.Consumed;

                if (result.HasBoundary)
                {
                    cuts.Add(result.CompletedChunkLength);
                }
            }

            int last = state.Finish();

            if (last != 0)
            {
                cuts.Add(last);
            }
        });

        var ids = new ChunkId[cuts.Count];
        PatchLabCost hash = Measure(() =>
        {
            int offset = 0;

            for (int index = 0; index < ids.Length; index++)
            {
                ids[index] = new ChunkId(HashSuiteHasher.Hash(hashSuite, content.AsSpan(offset, cuts[index])));
                offset += cuts[index];
            }
        });

        ManifestId computed = default;
        PatchLabCost manifestId = Measure(() =>
        {
            using var accumulator = new ManifestIdAccumulator(
                hashSuite,
                target.ProfileId,
                target.ProfileFingerprint);

            for (int index = 0; index < ids.Length; index++)
            {
                accumulator.Append(ids[index], cuts[index]);
            }

            computed = accumulator.Complete();
        });

        if (computed != target.ManifestId || ids.Length != target.ChunkCount)
        {
            throw new InvalidDataException(
                $"{patchPath}: the decomposition does not reproduce the embedded manifest.");
        }

        PatchLabCost check = await MeasureAsync(async () =>
        {
            await using FileStream stream = OpenOutput(outputPath);
            ManifestVerificationResult result = await ChunkManifest
                .VerifyAsync(stream, reader.OpenTargetManifest(), cancellationToken)
                .ConfigureAwait(false);

            if (!result.IsValid)
            {
                throw new InvalidDataException($"{patchPath}: the re-chunk check fails: {result.Failures}.");
            }
        }).ConfigureAwait(false);

        return (new PatchLabCheckDecomposition(csmParse, reread, boundary, hash, manifestId, check), ids.Length);
    }

    private static async Task<int> ConcurrentAsync(string[] args)
    {
        PatchLabCorpus corpus = LoadCorpus(args, "concurrent");
        int repetition = Repetition(args);
        string output = RequiredOutput(args, "concurrent");
        int[] levels = Concurrency(PatchLabArguments.Value(args, "--concurrency"));
        string[] order = LaneOrder(repetition);
        List<Item> items = Items(corpus);
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();
        string directory = Directory.CreateTempSubdirectory("chunkshift-apply-check-").FullName;

        try
        {
            var passes = new List<PatchLabConcurrentPass>();

            foreach (int concurrency in levels)
            {
                // One unmeasured pass per level warms the cache and the code.
                _ = await PassAsync(corpus, items, "off", concurrency, directory).ConfigureAwait(false);
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);

                foreach (string lane in order)
                {
                    int pass = counts.GetValueOrDefault(lane);
                    counts[lane] = pass + 1;
                    PatchLabCost cost = await PassAsync(corpus, items, lane, concurrency, directory)
                        .ConfigureAwait(false);
                    passes.Add(new PatchLabConcurrentPass(
                        concurrency,
                        lane,
                        pass,
                        cost.WallSeconds,
                        cost.CpuSeconds));
                }

                Console.Error.WriteLine(Invariant(
                    $"apply-check concurrent r{repetition} c{concurrency}: {clock.Elapsed.TotalSeconds:F1} s"));
            }

            PatchLabRunner.WriteJson(output, new PatchLabApplyCheckConcurrentResult(
                Schema,
                "concurrent",
                PatchLabArguments.Value(args, "--run-id"),
                repetition,
                order,
                "warm",
                PatchLabLane.Describe(CspEncoderPolicy.Default),
                corpus.PairsSha256,
                PatchLabRunner.Snapshot(),
                startedUtc,
                clock.Elapsed.TotalSeconds,
                items.Count,
                [.. passes]));
            return 0;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Applies every item with <paramref name="concurrency"/> applies in flight.</summary>
    private static Task<PatchLabCost> PassAsync(
        PatchLabCorpus corpus,
        List<Item> items,
        string lane,
        int concurrency,
        string directory) =>
        MeasureAsync(() => Parallel.ForEachAsync(
            items,
            new ParallelOptions { MaxDegreeOfParallelism = concurrency },
            async (item, cancellationToken) =>
            {
                string own = Path.Combine(directory, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(own);

                try
                {
                    _ = await ApplyAsync(corpus, item, lane, own, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Directory.Delete(own, recursive: true);
                }
            }));

    private static async Task<int> MemoryAsync(string[] args)
    {
        PatchLabCorpus corpus = LoadCorpus(args, "memory");
        string output = RequiredOutput(args, "memory");
        long minBytes = DefaultMemoryMinBytes;
        string? minBytesValue = PatchLabArguments.Value(args, "--min-bytes");

        if (minBytesValue is not null &&
            (!long.TryParse(minBytesValue, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out minBytes) ||
             minBytes < 0))
        {
            throw new PatchLabUsageException("--min-bytes requires a non-negative integer.");
        }

        string directory = Directory.CreateTempSubdirectory("chunkshift-apply-check-memory-").FullName;

        try
        {
            long[] idle = new long[3];

            for (int run = 0; run < idle.Length; run++)
            {
                idle[run] = PatchLabMemory.RunChild(["one", "idle"], "idle");
            }

            var files = new List<PatchLabApplyCheckMemoryFile>();

            foreach (Item item in Items(corpus))
            {
                if (item.File.TargetSize < minBytes)
                {
                    continue;
                }

                string patchPath = RequirePatch(corpus, item);
                var peaks = new Dictionary<string, long>(StringComparer.Ordinal);

                foreach (string lane in LaneNames)
                {
                    string outputPath = Path.Combine(directory, "output-" + lane);
                    peaks[lane] = PatchLabMemory.RunChild(
                        [
                            "one", "apply",
                            "--corpus", corpus.Root,
                            "--family", item.Pair.Family,
                            "--base", item.Pair.Base,
                            "--target", item.Pair.Target,
                            "--path", item.File.Path,
                            "--work", corpus.WorkDirectory,
                            "--patch", patchPath,
                            "--output", outputPath,
                            "--check", lane,
                        ],
                        $"{lane} apply of {item.File.Path}");
                    File.Delete(outputPath);
                }

                files.Add(new PatchLabApplyCheckMemoryFile(
                    item.Pair.Family,
                    item.Pair.Base,
                    item.Pair.Target,
                    item.File.Path,
                    item.File.TargetSize,
                    peaks));

                Console.Error.WriteLine(Invariant(
                    $"apply-check memory {item.Pair.Family} {item.File.Path}: off {peaks["off"]} B, seq {peaks["seq"]} B, overlap {peaks["overlap"]} B"));
            }

            PatchLabRunner.WriteJson(output, new PatchLabApplyCheckMemoryResult(
                Schema,
                "memory",
                PatchLabArguments.Value(args, "--run-id"),
                PatchLabLane.Describe(CspEncoderPolicy.Default),
                corpus.PairsSha256,
                PatchLabRunner.Snapshot(),
                PatchLabMemory.MemoryEnvironment(),
                PatchLabRunner.Median(idle),
                [.. files]));
            return 0;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Applies the prepared patch of one item in <paramref name="lane"/> into a
    /// new output in <paramref name="directory"/> and returns the call's wall
    /// and process CPU time. The streams are opened before the clocks start.
    /// The output is checked against <paramref name="targetSha256"/> when it
    /// is given, and deleted unless <paramref name="keepOutput"/> names it.
    /// </summary>
    private static async Task<PatchLabCost> ApplyAsync(
        PatchLabCorpus corpus,
        Item item,
        string lane,
        string directory,
        CancellationToken cancellationToken,
        string? targetSha256 = null,
        string? keepOutput = null)
    {
        if (!TryParseLane(lane, out ChunkingCheck check))
        {
            throw new PatchLabUsageException($"Unknown apply-check lane '{lane}'.");
        }

        string patchPath = RequirePatch(corpus, item);
        string baseContentPath = corpus.ContentPath(item.Pair, item.Pair.Base, item.File.Path);
        string baseManifestPath = RequireBaseManifest(corpus, item);
        string outputPath = keepOutput ?? Path.Combine(directory, Guid.NewGuid().ToString("N") + ".out");
        PatchLabCost cost;

        await using (FileStream patch = PatchLabFiles.OpenRead(patchPath))
        await using (FileStream baseManifest = PatchLabFiles.OpenRead(baseManifestPath))
        await using (FileStream baseContent = PatchLabFiles.OpenRead(baseContentPath))
        {
            PatchApplyResult? result = null;
            cost = await MeasureAsync(async () =>
            {
                result = await CspApplier
                    .ApplyAsync(
                        patch,
                        baseManifest,
                        baseContent,
                        outputPath,
                        CspFormat.DefaultMaximumPayloadEntries,
                        check,
                        cancellationToken)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);

            if (result!.Failures != PatchApplyFailure.None)
            {
                throw new InvalidDataException($"Applying '{patchPath}' in lane {lane} failed with {result.Failures}.");
            }
        }

        try
        {
            if (targetSha256 is not null)
            {
                string actual = await PatchLabManifests
                    .DigestAsync(outputPath, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.Equals(actual, targetSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"{patchPath}: lane {lane} wrote a target that hashes to {actual}, not to the corpus digest {targetSha256}.");
                }
            }
        }
        finally
        {
            if (keepOutput is null)
            {
                File.Delete(outputPath);
            }
        }

        return cost;
    }

    /// <summary>Opens an output the way the sequential check reads its temporary file.</summary>
    private static FileStream OpenOutput(string path) =>
        new(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = ChunkingKernel.IoBufferSize,
                Options = FileOptions.Asynchronous,
            });

    private static PatchLabCost Measure(Action action)
    {
        TimeSpan cpu = Environment.CpuUsage.TotalTime;
        long started = Stopwatch.GetTimestamp();
        action();
        return new PatchLabCost(
            Stopwatch.GetElapsedTime(started).TotalSeconds,
            (Environment.CpuUsage.TotalTime - cpu).TotalSeconds);
    }

    private static async Task<PatchLabCost> MeasureAsync(Func<Task> action)
    {
        TimeSpan cpu = Environment.CpuUsage.TotalTime;
        long started = Stopwatch.GetTimestamp();
        await action().ConfigureAwait(false);
        return new PatchLabCost(
            Stopwatch.GetElapsedTime(started).TotalSeconds,
            (Environment.CpuUsage.TotalTime - cpu).TotalSeconds);
    }

    private static async Task<List<ChunkInfo>> ReadRecordsAsync(string manifestPath, CancellationToken cancellationToken)
    {
        await using FileStream stream = PatchLabFiles.OpenRead(manifestPath);
        await using ManifestReader reader = await ManifestReader
            .OpenAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        var records = new List<ChunkInfo>();
        var batch = new ChunkInfo[256];
        int count;

        while ((count = await reader.ReadAsync(batch, cancellationToken).ConfigureAwait(false)) != 0)
        {
            records.AddRange(batch.AsSpan(0, count));
        }

        return records;
    }

    /// <summary>
    /// Gets the base manifest <c>prepare</c> cached under the base digest, so a
    /// timed apply does not hash the base content again.
    /// </summary>
    private static string RequireBaseManifest(PatchLabCorpus corpus, Item item)
    {
        string manifestPath = Path.Combine(corpus.WorkDirectory, "csm", item.File.BaseSha256 + ".csm");

        return File.Exists(manifestPath)
            ? manifestPath
            : throw new InvalidOperationException(
                $"{item.Pair.Family}/{item.File.Path}: no base manifest at '{manifestPath}'; run 'patch-lab apply-check prepare' first.");
    }

    private static string RequirePatch(PatchLabCorpus corpus, Item item)
    {
        string patchPath = PatchPath(corpus.WorkDirectory, item.File);

        return File.Exists(patchPath)
            ? patchPath
            : throw new InvalidOperationException(
                $"{item.Pair.Family}/{item.File.Path}: no prepared patch at '{patchPath}'; run 'patch-lab apply-check prepare' first.");
    }

    private static PatchLabCorpus LoadCorpus(string[] args, string action)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot))
        {
            throw new PatchLabUsageException($"patch-lab apply-check {action} requires --corpus.");
        }

        return PatchLabCorpus.Load(
            corpusRoot,
            PatchLabArguments.Families(PatchLabArguments.Value(args, "--families")),
            PatchLabArguments.Value(args, "--work"));
    }

    private static string RequiredOutput(string[] args, string action) =>
        PatchLabArguments.TryValue(args, "--output", out string output)
            ? output
            : throw new PatchLabUsageException($"patch-lab apply-check {action} requires --output.");

    private static int Repetition(string[] args)
    {
        string? value = PatchLabArguments.Value(args, "--repetition");

        return value is not null &&
            int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int repetition)
            ? repetition
            : throw new PatchLabUsageException("--repetition requires a non-negative integer.");
    }

    internal static int[] Concurrency(string? value)
    {
        if (value is null)
        {
            return DefaultConcurrency;
        }

        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var levels = new int[parts.Length];

        for (int index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out levels[index]) ||
                levels[index] < 1)
            {
                throw new PatchLabUsageException("--concurrency requires a comma list of positive integers.");
            }
        }

        return levels.Length == 0
            ? throw new PatchLabUsageException("--concurrency requires at least one level.")
            : levels;
    }

    private static List<Item> Items(PatchLabCorpus corpus)
    {
        var items = new List<Item>();

        foreach (PatchLabPair pair in corpus.Pairs)
        {
            foreach (PatchLabChangedFile file in pair.Changed)
            {
                items.Add(new Item(pair, file));
            }
        }

        return items;
    }

    private readonly record struct Item(PatchLabPair Pair, PatchLabChangedFile File);
}
