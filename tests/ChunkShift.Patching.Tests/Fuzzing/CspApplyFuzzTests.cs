using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChunkShift.Patching.Application;
using ChunkShift.Patching.Format;
using ChunkShift.Patching.Tests.Application;
using ChunkShift.Patching.Tests.Creation;
using ChunkShift.Primitives;
using Xunit.Abstractions;

namespace ChunkShift.Patching.Tests.Fuzzing;

/// <summary>
/// Deterministic mutational fuzzing of CSP apply (RFC-0001 makes fuzzing
/// normative for readers; CSP-V1-CANDIDATE section 11 requires an independent
/// decoder).
/// </summary>
/// <remarks>
/// <para>
/// Every mutated patch is applied with <see cref="CspApplier"/> over the base
/// of the corpus entry it came from and checked against these oracles:
/// </para>
/// <list type="number">
/// <item>the only exceptions are <see cref="InvalidDataException"/>
/// (malformed) and <see cref="NotSupportedException"/> (unsupported); anything
/// else, such as an index, overflow or null-reference exception, is an applier
/// bug;</item>
/// <item>apply allocates at most <see cref="AllocationBudgetBytes"/> per
/// iteration, whatever lengths the patch declares;</item>
/// <item>a non-valid outcome leaves the destination directory empty, and a
/// valid outcome holds exactly the destination file;</item>
/// <item>a valid outcome targeting any known corpus ManifestId reproduces
/// that target's independently pinned bytes, not the mutation donor's target;
/// successful novel targets are included in the differential dump even if
/// larger than the normal small-case limit.</item>
/// </list>
/// <para>
/// The default run is small enough for every CI build. Heavy validation runs
/// more iterations through the environment variables below and also dumps
/// small cases for a differential check against the independent Python
/// applier (<c>tools/csp-fixtures/decode.py --compare</c>).
/// </para>
/// <list type="bullet">
/// <item><c>CHUNKSHIFT_FUZZ_ITERATIONS</c>: iterations per seed (default 150);</item>
/// <item><c>CHUNKSHIFT_FUZZ_SEEDS</c>: comma-separated seeds replacing the defaults;</item>
/// <item><c>CHUNKSHIFT_FUZZ_DUMP</c>: directory that receives small cases, the
/// base files they need and a <c>verdicts-&lt;seed&gt;.jsonl</c> with the .NET
/// verdict of each.</item>
/// </list>
/// </remarks>
public sealed class CspApplyFuzzTests : IDisposable
{
    // The apply of one case materializes one chunk buffer of at most the
    // profile's maximum chunk length (256 KiB), a dictionary of at most
    // CspDictionary.MaximumBytes (1 MiB), the payload index and entry list of
    // the patch (one record per PAYL entry actually present, and the mutator
    // grows a patch by at most a few blocks), the embedded CSM reader's fixed
    // buffers and a 64 KiB patch read buffer; the reconstruction lives in the
    // temporary file, not in memory. 64 MiB is far above that sum and still
    // fails when a length declared by the input drives an allocation.
    internal const long AllocationBudgetBytes = 64L * 1024 * 1024;

    private const int DefaultIterationsPerSeed = 150;
    private const int MaximumDumpedCaseBytes = 64 * 1024;
    private const int MaximumBaseContentBytes = 4 * 1024 * 1024;
    private const int MaximumReportedFailures = 5;

    private static readonly Lazy<List<CorpusEntry>> Corpus = new(BuildCorpus);

    private readonly ITestOutputHelper _output;
    private readonly string _directory =
        Directory.CreateTempSubdirectory("chunkshift-csp-fuzz-").FullName;

    public CspApplyFuzzTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    public static TheoryData<ulong> Seeds()
    {
        var seeds = new TheoryData<ulong>();
        string? configured = Environment.GetEnvironmentVariable("CHUNKSHIFT_FUZZ_SEEDS");

        if (string.IsNullOrWhiteSpace(configured))
        {
            seeds.Add(0x5EED_1001UL);
            seeds.Add(0x5EED_1002UL);
            seeds.Add(0x5EED_1003UL);
            return seeds;
        }

        foreach (string value in configured.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            seeds.Add(ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture));
        }

        return seeds;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void MutatedPatches_AreClassifiedWithinBoundsAndConsistently(ulong seed)
    {
        List<CorpusEntry> corpus = Corpus.Value;
        byte[][] patches = [.. corpus.Select(static entry => entry.Patch)];
        int iterations = ReadIterations();
        var random = new FuzzRandom(seed);
        var failures = new List<string>();
        var outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
        using DumpWriter? dump = DumpWriter.CreateFromEnvironment(seed);
        long started = Stopwatch.GetTimestamp();
        long maximumAllocated = 0;

        _output.WriteLine(
            $"seed=0x{seed:x} corpus={corpus.Count} entries/" +
            $"{patches.Sum(static patch => (long)patch.Length)} bytes");

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            var trace = new StringBuilder();
            CorpusEntry entry = random.Pick(corpus);
            byte[] patch = CspMutator.Mutate(entry.Patch, patches, random, trace);
            string destinationDirectory = Path.Combine(
                _directory,
                iteration.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(destinationDirectory);
            string destination = Path.GetFullPath(
                Path.Combine(destinationDirectory, "output.bin"));

            (PatchApplyResult? result, Exception? failure, long allocated) =
                Apply(entry, patch, destination, CspApplier.DefaultChunkingCheck);

            string verdict;
            string? detail = null;
            string? problem = null;

            if (failure is InvalidDataException)
            {
                verdict = "malformed";
            }
            else if (failure is NotSupportedException)
            {
                verdict = "unsupported";
            }
            else if (failure is not null)
            {
                verdict = "rejected";
                detail = failure.ToString();
                problem = $"apply threw {failure.GetType().FullName}";
            }
            else if (result!.IsApplied)
            {
                verdict = "valid";
            }
            else if ((result.Failures & PatchApplyFailure.ResourceLimit) != 0)
            {
                verdict = "limit";
            }
            else
            {
                verdict = "verification:" + string.Join(",", FailureNames(result.Failures));
            }

            if (problem is null)
            {
                if (allocated > AllocationBudgetBytes)
                {
                    problem = $"allocated {allocated} bytes (budget {AllocationBudgetBytes})";
                }
                else
                {
                    string[] entries = Directory.GetFileSystemEntries(destinationDirectory);

                    if (result is not null && result.IsApplied)
                    {
                        if (entries.Length != 1 ||
                            !string.Equals(entries[0], destination, StringComparison.Ordinal))
                        {
                            problem =
                                $"a valid outcome left {entries.Length} entries in the destination directory";
                        }
                        else
                        {
                            string outputSha256 = Sha256File(destination);
                            verdict = "valid:" + outputSha256;

                            problem = CheckKnownTarget(result.Target?.ManifestId, outputSha256, corpus);
                        }
                    }
                    else if (entries.Length != 0)
                    {
                        problem =
                            $"a {verdict} outcome left {entries.Length} entries in the destination directory";
                    }
                }
            }

            if (problem is null)
            {
                // The other way of running the re-chunk check reaches the
                // same outcome and output for the same case.
                problem = CompareOtherCheck(
                    entry,
                    patch,
                    destinationDirectory,
                    Outcome(result, failure, destination));
            }

            if (problem is not null)
            {
                dump?.WriteFailure(patch, verdict, entry, iteration, trace.ToString(), problem, detail);
                failures.Add(Describe(seed, iteration, entry, trace, patch, problem, detail));
                if (failures.Count >= MaximumReportedFailures)
                {
                    break;
                }

                continue;
            }

            string bucket = verdict.StartsWith("verification:", StringComparison.Ordinal)
                ? verdict
                : verdict.Split(':')[0];
            outcomes[bucket] = outcomes.GetValueOrDefault(bucket) + 1;
            maximumAllocated = Math.Max(maximumAllocated, allocated);
            // A successful novel target has no pinned corpus digest. Do not
            // omit it from the independent decoder merely because it is large.
            bool novelTarget = result is { IsApplied: true, Target: { } target } &&
                !corpus.Any(candidate => candidate.TargetManifestId == target.ManifestId);
            dump?.Write(patch, verdict, entry, iteration, force: novelTarget);
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        _output.WriteLine(
            $"seed=0x{seed:x} iterations={iterations} elapsed={elapsed.TotalSeconds:0.0}s " +
            $"maxAllocated={maximumAllocated}");
        _output.WriteLine(Summary(outcomes));
        Assert.True(
            failures.Count == 0,
            string.Join(Environment.NewLine + Environment.NewLine, failures));
    }

    private static string? CheckKnownTarget(
        ManifestId? targetId, string outputSha256, IReadOnlyList<CorpusEntry> corpus)
    {
        if (targetId is null)
        {
            return "a successful apply did not return a target manifest identity";
        }

        // Splicing can replace the entire target with another valid CSP.
        // Retain the strong byte oracle for every known target identity.
        foreach (CorpusEntry candidate in corpus)
        {
            if (candidate.TargetManifestId == targetId &&
                !string.Equals(outputSha256, candidate.ExpectedOutputSha256, StringComparison.Ordinal))
            {
                return $"the output SHA-256 {outputSha256} differs from known target " +
                    candidate.ExpectedOutputSha256;
            }
        }

        return null;
    }

    [Fact]
    public void ValidReplacementTarget_UsesItsOwnPinnedDigest()
    {
        // Heavy validation 45800817961, seed 0x562a, iteration 1475:
        // the mutated bytes were exactly the existing 789-byte fixture.
        List<CorpusEntry> corpus = Corpus.Value;
        CorpusEntry origin = corpus.Single(entry => entry.Name == "scenario-xor-region");
        CorpusEntry donor = corpus.Single(entry => entry.Name == "zstd-no-dictionary");
        const string expected = "967dcabb78ed6db5d8911bb78c322b35be9293c96d5d5981fa540c4adde19b0c";
        Assert.Equal(789, donor.Patch.Length);
        Assert.Equal(expected, donor.ExpectedOutputSha256);
        Assert.NotEqual(origin.TargetManifestId, donor.TargetManifestId);
        Assert.NotEqual(origin.ExpectedOutputSha256, expected);

        string destination = Path.Combine(_directory, "output.bin");
        (PatchApplyResult? result, Exception? failure, _) =
            Apply(origin, donor.Patch, destination, CspApplier.DefaultChunkingCheck);
        Assert.Null(failure);
        Assert.NotNull(result);
        Assert.True(result!.IsApplied);
        Assert.NotNull(result.Target);
        Assert.Equal(donor.TargetManifestId, result.Target!.ManifestId);
        Assert.Equal(900L, new FileInfo(destination).Length);
        string actual = Sha256File(destination);
        Assert.Equal(expected, actual);
        Assert.Null(CheckKnownTarget(result.Target.ManifestId, actual, corpus));
        Assert.NotNull(CheckKnownTarget(origin.TargetManifestId, actual, corpus));
        Assert.NotNull(CheckKnownTarget(donor.TargetManifestId, origin.ExpectedOutputSha256, corpus));
        Assert.Null(CompareOtherCheck(origin, donor.Patch, _directory, Outcome(result, failure, destination)));
    }

    private static int ReadIterations()
    {
        string? configured = Environment.GetEnvironmentVariable("CHUNKSHIFT_FUZZ_ITERATIONS");
        return string.IsNullOrWhiteSpace(configured)
            ? DefaultIterationsPerSeed
            : int.Parse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Applies one case and reports what it threw, if anything, and the bytes
    /// this thread allocated around the call. Blocking keeps both counter
    /// reads on the thread that starts the apply: MemoryStream completes every
    /// read synchronously, so the reader's length-driven allocations are
    /// measured where they happen, and work the library hands to the pool
    /// after a real asynchronous hop is not charged to this iteration.
    /// </summary>
    /// <summary>
    /// Applies the case again with every other way of running the re-chunk
    /// check except none, and returns a problem when an outcome differs from
    /// the default's.
    /// </summary>
    private static string? CompareOtherCheck(
        CorpusEntry entry,
        byte[] patch,
        string destinationDirectory,
        string expected)
    {
        foreach (ChunkingCheck other in Enum.GetValues<ChunkingCheck>())
        {
            if (other == ChunkingCheck.Off || other == CspApplier.DefaultChunkingCheck)
            {
                continue;
            }

            string otherDirectory = Path.Combine(destinationDirectory, "other");
            Directory.CreateDirectory(otherDirectory);
            string otherDestination = Path.Combine(otherDirectory, "output.bin");

            (PatchApplyResult? result, Exception? failure, _) =
                Apply(entry, patch, otherDestination, other);
            string actual = Outcome(result, failure, otherDestination);
            Directory.Delete(otherDirectory, recursive: true);

            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                return $"the {other} re-chunk check reached {actual}, the default {expected}";
            }
        }

        return null;
    }

    /// <summary>Renders an outcome for comparison: exception type, failures or output digest.</summary>
    private static string Outcome(PatchApplyResult? result, Exception? failure, string destination) =>
        failure is not null
            ? failure.GetType().Name
            : result!.IsApplied
                ? "applied:" + Sha256File(destination)
                : "failed:" + result.Failures;

    private static (PatchApplyResult? Result, Exception? Failure, long Allocated) Apply(
        CorpusEntry entry,
        byte[] patch,
        string destination,
        ChunkingCheck check)
    {
        using var patchStream = new MemoryStream(patch, writable: false);
        using MemoryStream? baseManifest = entry.BaseManifest is null
            ? null
            : new MemoryStream(entry.BaseManifest, writable: false);
        using MemoryStream? baseContent = entry.BaseContent is null
            ? null
            : new MemoryStream(entry.BaseContent, writable: false);
        long before = GC.GetAllocatedBytesForCurrentThread();

        try
        {
            PatchApplyResult result = CspApplier
                .ApplyAsync(
                    patchStream,
                    baseManifest,
                    baseContent,
                    destination,
                    CspFormat.DefaultMaximumPayloadEntries,
                    check,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            return (result, null, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        catch (Exception exception)
        {
            return (null, exception, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }

    /// <summary>
    /// Builds the corpus once: the SHA-256 patches <c>CreateAsync</c> makes
    /// for every creation scenario, each with the base it applies over, plus
    /// every committed vector whose expected verdict is <c>valid</c>, with the
    /// base files it names. BLAKE3 enters through the committed BLAKE3
    /// vectors, whose bases are a few KiB: the independent decoder computes
    /// BLAKE3 in pure Python at about 1 MB/s, too slow for thousands of cases
    /// over the megabyte scenario bases. Base content above four MiB is
    /// dropped so a dumped case stays small.
    /// </summary>
    private static List<CorpusEntry> BuildCorpus()
    {
        var corpus = new List<CorpusEntry>();

        foreach (PatchScenario scenario in PatchScenarios.All)
        {
            byte[] baseManifest = CreationTestSupport
                .CreateManifestAsync(scenario.BaseContent, HashSuiteIds.Sha256V1)
                .GetAwaiter()
                .GetResult();
            byte[] targetManifest = CreationTestSupport
                .CreateManifestAsync(scenario.TargetContent, HashSuiteIds.Sha256V1)
                .GetAwaiter()
                .GetResult();
            (byte[] patch, PatchInfo info) = CreationTestSupport
                .CreatePatchAsync(scenario, baseManifest, targetManifest)
                .GetAwaiter()
                .GetResult();
            byte[]? baseContent = scenario.SelfContained ? null : scenario.BaseContent;

            if (baseContent is { Length: > MaximumBaseContentBytes })
            {
                continue;
            }

            corpus.Add(new CorpusEntry(
                "scenario-" + scenario.Name,
                patch,
                scenario.SelfContained ? null : baseManifest,
                baseContent,
                info.TargetManifestId,
                Convert.ToHexStringLower(SHA256.HashData(scenario.TargetContent))));
        }

        foreach (CspApplyVector vector in CspApplyVectors.All)
        {
            if (vector.Verdict != "valid")
            {
                continue;
            }

            byte[]? baseManifest = vector.BaseName is null
                ? null
                : CspApplyVectors.ReadBaseManifest(vector);
            byte[]? baseContent = vector.BaseName is null
                ? null
                : CspApplyVectors.ReadBaseContent(vector);

            if (baseContent is { Length: > MaximumBaseContentBytes })
            {
                continue;
            }

            byte[] patch = CspApplyVectors.ReadPatch(vector.Name);
            using var readerStream = new MemoryStream(patch, writable: false);
            CspReader reader = CspReader.OpenAsync(readerStream).GetAwaiter().GetResult();
            corpus.Add(new CorpusEntry(
                Path.GetFileNameWithoutExtension(vector.Name),
                patch,
                baseManifest,
                baseContent,
                reader.TargetManifest.Manifest.ManifestId,
                vector.OutputSha256 ?? throw new InvalidDataException("Valid vector lacks its output digest.")));
        }

        return corpus;
    }

    private static string[] FailureNames(PatchApplyFailure failures)
    {
        var names = new List<string>();

        // The .NET surface names the patch TRAILER digest PatchFileDigest; the
        // independent decoder calls it FileDigest.
        if ((failures & PatchApplyFailure.PatchFileDigest) != 0)
        {
            names.Add("FileDigest");
        }

        if ((failures & PatchApplyFailure.EmbeddedManifest) != 0)
        {
            names.Add("EmbeddedManifest");
        }

        if ((failures & PatchApplyFailure.ProfileSemantics) != 0)
        {
            names.Add("ProfileSemantics");
        }

        if ((failures & PatchApplyFailure.DuplicatePayload) != 0)
        {
            names.Add("DuplicatePayload");
        }

        if ((failures & PatchApplyFailure.PayloadNotInTarget) != 0)
        {
            names.Add("PayloadNotInTarget");
        }

        if ((failures & PatchApplyFailure.PayloadLength) != 0)
        {
            names.Add("PayloadLength");
        }

        if ((failures & PatchApplyFailure.BaseMismatch) != 0)
        {
            names.Add("BaseMismatch");
        }

        if ((failures & PatchApplyFailure.BaseManifest) != 0)
        {
            names.Add("BaseManifest");
        }

        if ((failures & PatchApplyFailure.BaseChunk) != 0)
        {
            names.Add("BaseChunk");
        }

        if ((failures & PatchApplyFailure.DictionaryChunk) != 0)
        {
            names.Add("DictionaryChunk");
        }

        if ((failures & PatchApplyFailure.PayloadChunk) != 0)
        {
            names.Add("PayloadChunk");
        }

        if ((failures & PatchApplyFailure.MissingPayload) != 0)
        {
            names.Add("MissingPayload");
        }

        if ((failures & PatchApplyFailure.ContentLength) != 0)
        {
            names.Add("ContentLength");
        }

        if ((failures & PatchApplyFailure.ProfileContent) != 0)
        {
            names.Add("ProfileContent");
        }

        names.Sort(StringComparer.Ordinal);
        return [.. names];
    }

    private static string Sha256File(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string Describe(
        ulong seed,
        int iteration,
        CorpusEntry entry,
        StringBuilder trace,
        byte[] input,
        string problem,
        string? detail)
    {
        var message = new StringBuilder()
            .Append(
                CultureInfo.InvariantCulture,
                $"seed=0x{seed:x} iteration={iteration} entry={entry.Name}: {problem}")
            .AppendLine()
            .Append(CultureInfo.InvariantCulture, $"mutations: {trace}")
            .AppendLine();

        if (detail is not null)
        {
            message.AppendLine(detail);
        }

        message.Append(
            input.Length <= 4096
                ? $"patch ({input.Length} bytes, base64): {Convert.ToBase64String(input)}"
                : $"patch: {input.Length} bytes (replay with the seed and iteration above)");

        return message.ToString();
    }

    private static string Summary(Dictionary<string, int> outcomes) =>
        "outcomes: " + string.Join(", ", outcomes.OrderBy(
            static pair => pair.Key,
            StringComparer.Ordinal).Select(static pair => $"{pair.Key}={pair.Value}"));

    /// <summary>One corpus patch with the base it applies over.</summary>
    private sealed record CorpusEntry(
        string Name,
        byte[] Patch,
        byte[]? BaseManifest,
        byte[]? BaseContent,
        ManifestId TargetManifestId,
        string ExpectedOutputSha256);

    /// <summary>
    /// Writes small mutated patches, the base files each corpus entry needs
    /// and their .NET verdicts for the Python differential check.
    /// </summary>
    private sealed class DumpWriter : IDisposable
    {
        private readonly string _directory;
        private readonly StreamWriter _verdicts;
        private readonly ulong _seed;
        private readonly HashSet<string> _writtenBases = new(StringComparer.Ordinal);

        private DumpWriter(string directory, ulong seed)
        {
            _directory = directory;
            _seed = seed;
            Directory.CreateDirectory(directory);
            _verdicts = new StreamWriter(
                Path.Combine(directory, $"verdicts-{seed:x}.jsonl"),
                append: false,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                NewLine = "\n",
            };
        }

        internal static DumpWriter? CreateFromEnvironment(ulong seed)
        {
            string? directory = Environment.GetEnvironmentVariable("CHUNKSHIFT_FUZZ_DUMP");
            return string.IsNullOrWhiteSpace(directory) ? null : new DumpWriter(directory, seed);
        }

        internal void WriteFailure(byte[] patch, string verdict, CorpusEntry entry,
            int iteration, string trace, string problem, string? detail)
        {
            // A maximum of MaximumReportedFailures per seed; the finite
            // mutator corpus bounds input sizes. Keep failures separate from
            // the successful differential listing and never omit a large repro.
            string directory = Path.Combine(_directory, "failures", $"{_seed:x}-{iteration}");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "case.csp"), patch);
            if (entry.BaseManifest is not null)
            {
                File.WriteAllBytes(Path.Combine(directory, "base.csm"), entry.BaseManifest);
            }

            if (entry.BaseContent is not null)
            {
                File.WriteAllBytes(Path.Combine(directory, "base.bin"), entry.BaseContent);
            }

            File.WriteAllText(Path.Combine(directory, "failure.json"), JsonSerializer.Serialize(new
            {
                seed = _seed, iteration, entry = entry.Name, trace, problem, detail, verdict,
                originalTarget = entry.TargetManifestId.ToString(),
                originalTargetSha256 = entry.ExpectedOutputSha256,
                patchSha256 = Convert.ToHexStringLower(SHA256.HashData(patch)),
            }));
        }

        internal void Write(byte[] patch, string verdict, CorpusEntry entry, int iteration, bool force)
        {
            if (!force && patch.Length > MaximumDumpedCaseBytes)
            {
                return;
            }

            string? baseManifest = null;
            string? baseContent = null;

            if (entry.BaseManifest is not null && entry.BaseContent is not null)
            {
                baseManifest = $"base-{entry.Name}.csm";
                baseContent = $"base-{entry.Name}.bin";

                if (_writtenBases.Add(entry.Name))
                {
                    File.WriteAllBytes(Path.Combine(_directory, baseManifest), entry.BaseManifest);
                    File.WriteAllBytes(Path.Combine(_directory, baseContent), entry.BaseContent);
                }
            }

            string name = $"case-{_seed:x}-{iteration}.csp";
            File.WriteAllBytes(Path.Combine(_directory, name), patch);
            _verdicts.WriteLine(JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["file"] = name,
                ["baseManifest"] = baseManifest,
                ["base"] = baseContent,
                ["verdict"] = verdict,
            }));
        }

        public void Dispose() => _verdicts.Dispose();
    }
}
