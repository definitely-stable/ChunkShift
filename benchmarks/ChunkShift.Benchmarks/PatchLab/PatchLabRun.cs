using System.Diagnostics;
using ChunkShift.Patching;
using ChunkShift.Patching.Application;
using ChunkShift.Patching.Creation;
using ChunkShift.Patching.Format;
using static System.FormattableString;

namespace ChunkShift.Benchmarks.PatchLab;

/// <summary>
/// <c>patch-lab run</c>: creates one CSP lane for every changed file of the
/// selected corpus pairs, reads the patch back for its entry statistics and
/// optionally applies it with the re-chunk check on and off
/// (docs/benchmarks/PATCH-PREFREEZE-PROTOCOL.md sections 1-2).
/// </summary>
internal static class PatchLabRun
{
    internal const string Schema = "chunkshift.patch-lab.v1";

    private const string RunUsage =
        "patch-lab run requires --corpus, --lane and --output.";

    internal static int Execute(string[] args)
    {
        if (!PatchLabArguments.TryValue(args, "--corpus", out string corpusRoot) ||
            !PatchLabArguments.TryValue(args, "--lane", out string laneName) ||
            !PatchLabArguments.TryValue(args, "--output", out string outputPath))
        {
            Console.Error.WriteLine(RunUsage);
            return 2;
        }

        CspEncoderPolicy policy = PatchLabLane.Parse(laneName);
        PatchLabCorpus corpus = PatchLabCorpus.Load(
            corpusRoot,
            PatchLabArguments.Families(PatchLabArguments.Value(args, "--families")),
            PatchLabArguments.Value(args, "--work"));
        int workers = PatchLabArguments.PositiveInt(args, "--workers", 1);
        int applyRepeats = PatchLabArguments.PositiveInt(args, "--apply-repeats", 3);
        bool apply = !args.Contains("--no-apply", StringComparer.Ordinal);
        string? runId = PatchLabArguments.Value(args, "--run-id");

        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();
        PatchLabFileResult[] files = RunFiles(corpus, laneName, policy, workers, apply, applyRepeats);
        clock.Stop();

        PatchLabRunner.WriteJson(outputPath, new PatchLabRunResult(
            Schema,
            runId,
            laneName,
            PatchLabLane.Describe(policy),
            workers,
            // With --no-apply no repeat ran, and the record says so.
            apply ? applyRepeats : 0,
            corpus.PairsSha256,
            PatchLabRunner.Snapshot(),
            startedUtc,
            clock.Elapsed.TotalSeconds,
            files));

        return 0;
    }

    /// <summary>
    /// Processes every changed file of every pair, <paramref name="workers"/>
    /// files at a time, and keeps the results in corpus order.
    /// </summary>
    private static PatchLabFileResult[] RunFiles(
        PatchLabCorpus corpus,
        string lane,
        CspEncoderPolicy policy,
        int workers,
        bool apply,
        int applyRepeats)
    {
        var items = new List<WorkItem>();

        for (int pairIndex = 0; pairIndex < corpus.Pairs.Length; pairIndex++)
        {
            PatchLabPair pair = corpus.Pairs[pairIndex];

            for (int fileIndex = 0; fileIndex < pair.Changed.Length; fileIndex++)
            {
                items.Add(new WorkItem(pairIndex, pair, pair.Changed[fileIndex]));
            }
        }

        int[] offsets = new int[corpus.Pairs.Length];
        int[] remaining = new int[corpus.Pairs.Length];

        for (int pairIndex = 0; pairIndex < corpus.Pairs.Length; pairIndex++)
        {
            offsets[pairIndex] = pairIndex == 0
                ? 0
                : offsets[pairIndex - 1] + corpus.Pairs[pairIndex - 1].Changed.Length;
            remaining[pairIndex] = corpus.Pairs[pairIndex].Changed.Length;
        }

        var results = new PatchLabFileResult[items.Count];
        var clock = Stopwatch.StartNew();

        for (int pairIndex = 0; pairIndex < corpus.Pairs.Length; pairIndex++)
        {
            if (remaining[pairIndex] == 0)
            {
                Progress(lane, corpus.Pairs[pairIndex], results, offsets[pairIndex], 0, clock.Elapsed);
            }
        }

        Parallel.ForEachAsync(
            Enumerable.Range(0, items.Count),
            new ParallelOptions { MaxDegreeOfParallelism = workers },
            async (index, cancellationToken) =>
            {
                WorkItem item = items[index];
                results[index] = await RunFileAsync(
                    corpus,
                    policy,
                    item.Pair,
                    item.File,
                    apply,
                    applyRepeats,
                    cancellationToken).ConfigureAwait(false);

                if (Interlocked.Decrement(ref remaining[item.PairIndex]) == 0)
                {
                    Progress(
                        lane,
                        item.Pair,
                        results,
                        offsets[item.PairIndex],
                        item.Pair.Changed.Length,
                        clock.Elapsed);
                }
            }).GetAwaiter().GetResult();

        return results;
    }

    /// <summary>
    /// Creates, reads back and optionally applies one file's patch, in a
    /// temporary directory of its own that is removed afterwards.
    /// </summary>
    private static async Task<PatchLabFileResult> RunFileAsync(
        PatchLabCorpus corpus,
        CspEncoderPolicy policy,
        PatchLabPair pair,
        PatchLabChangedFile file,
        bool apply,
        int applyRepeats,
        CancellationToken cancellationToken)
    {
        string baseContentPath = corpus.ContentPath(pair, pair.Base, file.Path);
        string targetContentPath = corpus.ContentPath(pair, pair.Target, file.Path);
        string baseManifestPath = await PatchLabManifests
            .EnsureAsync(baseContentPath, corpus.WorkDirectory, file.BaseSha256, cancellationToken)
            .ConfigureAwait(false);
        string targetManifestPath = await PatchLabManifests
            .EnsureAsync(targetContentPath, corpus.WorkDirectory, file.TargetSha256, cancellationToken)
            .ConfigureAwait(false);

        long uniqueMissingBytes;

        await using (FileStream baseManifest = PatchLabFiles.OpenRead(baseManifestPath))
        await using (FileStream targetManifest = PatchLabFiles.OpenRead(targetManifestPath))
        {
            PatchPlan plan = await ChunkPatch
                .PlanAsync(baseManifest, targetManifest, cancellationToken)
                .ConfigureAwait(false);

            if (!plan.IsValid)
            {
                throw new InvalidDataException(
                    $"{pair.Family}/{pair.Base}->{pair.Target}/{file.Path}: the manifests do not verify.");
            }

            uniqueMissingBytes = plan.UniqueMissingBytes;
        }

        string directory = Directory.CreateTempSubdirectory("chunkshift-patch-lab-").FullName;

        try
        {
            string patchPath = Path.Combine(directory, "patch.csp");
            double createSeconds;

            await using (FileStream baseManifest = PatchLabFiles.OpenRead(baseManifestPath))
            await using (FileStream baseContent = PatchLabFiles.OpenRead(baseContentPath))
            await using (FileStream targetManifest = PatchLabFiles.OpenRead(targetManifestPath))
            await using (FileStream targetContent = PatchLabFiles.OpenRead(targetContentPath))
            await using (FileStream destination = PatchLabFiles.Create(patchPath))
            {
                var clock = Stopwatch.StartNew();
                _ = await CspPatchBuilder
                    .CreateAsync(
                        baseManifest,
                        baseContent,
                        targetManifest,
                        targetContent,
                        destination,
                        policy,
                        cancellationToken)
                    .ConfigureAwait(false);
                createSeconds = clock.Elapsed.TotalSeconds;
            }

            PatchFacts facts = await ReadPatchAsync(patchPath, cancellationToken).ConfigureAwait(false);
            double? applySeconds = null;
            double? applyNoCheckSeconds = null;

            if (apply)
            {
                applySeconds = await ApplyRepeatedAsync(
                    patchPath,
                    baseManifestPath,
                    baseContentPath,
                    directory,
                    "check",
                    file.TargetSha256,
                    verifyChunking: true,
                    applyRepeats,
                    cancellationToken).ConfigureAwait(false);
                applyNoCheckSeconds = await ApplyRepeatedAsync(
                    patchPath,
                    baseManifestPath,
                    baseContentPath,
                    directory,
                    "no-check",
                    targetSha256: null,
                    verifyChunking: false,
                    applyRepeats,
                    cancellationToken).ConfigureAwait(false);
            }

            return new PatchLabFileResult(
                pair.Family,
                pair.Base,
                pair.Target,
                file.Path,
                file.TargetSize,
                uniqueMissingBytes,
                facts.PatchBytes,
                facts.TcsmBytes,
                facts.PayloadEntries,
                facts.RawEntries,
                facts.ZstdEntries,
                facts.DictionaryEntries,
                facts.StoredPayloadBytes,
                facts.DictionaryReferences,
                facts.TargetChunks,
                createSeconds,
                applySeconds,
                applyNoCheckSeconds);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Applies the patch <paramref name="repeats"/> times into fresh temporary
    /// outputs and returns the median wall time. When
    /// <paramref name="targetSha256"/> is set, the first output is verified
    /// against it.
    /// </summary>
    private static async Task<double> ApplyRepeatedAsync(
        string patchPath,
        string baseManifestPath,
        string baseContentPath,
        string directory,
        string name,
        string? targetSha256,
        bool verifyChunking,
        int repeats,
        CancellationToken cancellationToken)
    {
        var samples = new double[repeats];

        for (int repeat = 0; repeat < repeats; repeat++)
        {
            string outputPath = Path.Combine(directory, Invariant($"{name}-{repeat}.out"));
            samples[repeat] = await ApplyOnceAsync(
                patchPath,
                baseManifestPath,
                baseContentPath,
                outputPath,
                verifyChunking,
                cancellationToken).ConfigureAwait(false);

            if (repeat == 0 && targetSha256 is not null)
            {
                string actual = await PatchLabManifests
                    .DigestAsync(outputPath, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.Equals(actual, targetSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"{patchPath}: the applied target hashes to {actual}, not to the corpus digest {targetSha256}.");
                }
            }
        }

        return PatchLabRunner.Median(samples);
    }

    /// <summary>
    /// Applies one patch into <paramref name="outputPath"/> with the production
    /// applier and returns the wall time of the call. Streams are opened fresh
    /// for every repeat.
    /// </summary>
    internal static async Task<double> ApplyOnceAsync(
        string patchPath,
        string baseManifestPath,
        string baseContentPath,
        string outputPath,
        bool verifyChunking,
        CancellationToken cancellationToken)
    {
        await using FileStream patch = PatchLabFiles.OpenRead(patchPath);
        await using FileStream baseManifest = PatchLabFiles.OpenRead(baseManifestPath);
        await using FileStream baseContent = PatchLabFiles.OpenRead(baseContentPath);
        var clock = Stopwatch.StartNew();
        PatchApplyResult result = await CspApplier
            .ApplyAsync(
                patch,
                baseManifest,
                baseContent,
                outputPath,
                CspFormat.DefaultMaximumPayloadEntries,
                verifyChunking,
                cancellationToken)
            .ConfigureAwait(false);
        double seconds = clock.Elapsed.TotalSeconds;

        if (result.Failures != PatchApplyFailure.None)
        {
            throw new InvalidDataException($"Applying '{patchPath}' failed with {result.Failures}.");
        }

        return seconds;
    }

    /// <summary>Reads a created patch back for its physical and entry statistics.</summary>
    private static async Task<PatchFacts> ReadPatchAsync(string patchPath, CancellationToken cancellationToken)
    {
        await using FileStream patch = PatchLabFiles.OpenRead(patchPath);
        CspReader reader = await CspReader
            .OpenAsync(patch, CspFormat.DefaultMaximumPayloadEntries, cancellationToken)
            .ConfigureAwait(false);

        if (reader.Failures != CspVerificationFailure.None)
        {
            throw new InvalidDataException($"The created patch '{patchPath}' does not verify: {reader.Failures}.");
        }

        long rawEntries = 0;
        long zstdEntries = 0;
        long dictionaryEntries = 0;
        long storedPayloadBytes = 0;
        long dictionaryReferences = 0;

        foreach (CspIndexEntry entry in reader.Index)
        {
            storedPayloadBytes += entry.StoredLength;
            dictionaryReferences += entry.DictionaryCount;

            if (entry.Encoding == CspFormat.EncodingRaw)
            {
                rawEntries++;
            }
            else
            {
                zstdEntries++;

                if (entry.DictionaryCount > 0)
                {
                    dictionaryEntries++;
                }
            }
        }

        return new PatchFacts(
            new FileInfo(patchPath).Length,
            reader.TargetManifestLength,
            reader.PayloadChunkIds.Count,
            rawEntries,
            zstdEntries,
            dictionaryEntries,
            storedPayloadBytes,
            dictionaryReferences,
            reader.TargetManifest.Manifest.ChunkCount);
    }

    /// <summary>Prints one progress line for a finished pair.</summary>
    private static void Progress(
        string lane,
        PatchLabPair pair,
        PatchLabFileResult[] results,
        int offset,
        int count,
        TimeSpan elapsed)
    {
        long patchBytes = 0;

        for (int index = 0; index < count; index++)
        {
            patchBytes += results[offset + index].PatchBytes;
        }

        Console.Error.WriteLine(Invariant(
            $"{lane} {pair.Family} {pair.Base}->{pair.Target}: {count} files, {patchBytes} patch bytes, {elapsed.TotalSeconds:F1} s"));
    }

    private readonly record struct WorkItem(int PairIndex, PatchLabPair Pair, PatchLabChangedFile File);

    /// <summary>What a read-back of the created patch reports.</summary>
    private readonly record struct PatchFacts(
        long PatchBytes,
        long TcsmBytes,
        long PayloadEntries,
        long RawEntries,
        long ZstdEntries,
        long DictionaryEntries,
        long StoredPayloadBytes,
        long DictionaryReferences,
        long TargetChunks);
}
