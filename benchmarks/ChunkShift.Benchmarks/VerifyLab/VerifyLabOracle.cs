using System.Globalization;
using ChunkShift.Benchmarks.Lab;
using ChunkShift.Chunking;
using ChunkShift.Hashing;
using ChunkShift.Manifest;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.VerifyLab;

/// <summary>
/// The equivalence oracle of docs/benchmarks/CORE-VERIFY-001-PROTOCOL.md
/// section 3: V1 and every V2 must reach V0's verdict on every case, except
/// the declared difference D1, and V1 and every V2 must report the same first
/// mismatching record.
/// </summary>
internal static class VerifyLabOracle
{
    internal const string Schema = "chunkshift.verify-lab-oracle.v2";

    private const int O6RecordBytes = 64 * 1024;

    internal static readonly VerifyLabLane[] Lanes =
    [
        VerifyLabLane.V0,
        VerifyLabLane.V1,
        VerifyLabLane.V2(2),
        VerifyLabLane.V2(4),
        VerifyLabLane.V2(8),
    ];

    private static readonly HashSuiteId[] Suites = [HashSuiteIds.Blake3256V1, HashSuiteIds.Sha256V1];

    /// <summary>Runs the oracle and returns its report.</summary>
    /// <param name="fixtureDirectory">The CSM vector directory (O2), or null to skip O2.</param>
    /// <param name="quick">
    /// The reduced set of the unit tests: the contents up to 1 MiB and fewer
    /// mutation positions. The platform jobs run the full set.
    /// </param>
    /// <param name="scratchDirectory">A directory for the file-backed cases.</param>
    internal static async Task<VerifyLabOracleReport> RunAsync(
        string? fixtureDirectory,
        bool quick,
        string scratchDirectory)
    {
        var report = new VerifyLabOracleReport(Schema, quick);

        foreach (HashSuiteId suite in Suites)
        {
            foreach ((string name, byte[] bytes) in Contents(quick))
            {
                string id = string.Create(CultureInfo.InvariantCulture, $"{name}/{Suite(suite)}");
                byte[] manifest = await CreateManifestAsync(bytes, suite).ConfigureAwait(false);
                CsmChunkEntry[] records = await RecordsAsync(manifest).ConfigureAwait(false);

                await CheckAsync(report, "O1", id, new VerifyLabMemoryContent(bytes), manifest, declaredD1: false).ConfigureAwait(false);
                await MutationsAsync(report, "O3", id, bytes, manifest, records, quick).ConfigureAwait(false);
                await TruncationsAsync(report, id, bytes, manifest, records, quick, scratchDirectory: null).ConfigureAwait(false);

                if (bytes.Length >= 1 << 20)
                {
                    await DamagedManifestsAsync(report, id, bytes, manifest, records).ConfigureAwait(false);
                    await ProfileQuestionAsync(report, id, bytes, suite, quick).ConfigureAwait(false);
                }

                if (bytes.Length == 1 << 20)
                {
                    // The same truncations and extensions through real files, so
                    // V2's RandomAccess path meets a short and a long file too.
                    await TruncationsAsync(report, id + "/file", bytes, manifest, records, quick, scratchDirectory)
                        .ConfigureAwait(false);
                }
            }
        }

        if (fixtureDirectory is not null)
        {
            await VectorsAsync(report, fixtureDirectory).ConfigureAwait(false);
        }

        return report;
    }

    /// <summary>The oracle contents of section 3.</summary>
    internal static IEnumerable<(string Name, byte[] Bytes)> Contents(bool quick)
    {
        yield return ("empty", []);
        yield return ("one-byte", [0x5A]);
        yield return ("random-12k", Random(12 * 1024, 0xC0FE_0101));
        yield return ("random-1m", Random(1 << 20, 0xC0FE_0102));

        if (quick)
        {
            yield break;
        }

        yield return ("random-8m", Random(8 << 20, 0xC0FE_0103));
        yield return ("zeros-8m", new byte[8 << 20]);

        byte[] periodic = new byte[8 << 20];
        byte[] period = Random(1000, 0xC0FE_0104);

        for (int offset = 0; offset < periodic.Length; offset += period.Length)
        {
            period.AsSpan(0, Math.Min(period.Length, periodic.Length - offset)).CopyTo(periodic.AsSpan(offset));
        }

        yield return ("period-1000-8m", periodic);
    }

    internal static byte[] Random(int length, ulong seed)
    {
        byte[] bytes = new byte[length];
        new DeterministicPrng(seed).Fill(bytes);
        return bytes;
    }

    internal static string Suite(HashSuiteId suite) =>
        suite == HashSuiteIds.Sha256V1 ? "sha256" : "blake3";

    internal static async Task<byte[]> CreateManifestAsync(byte[] content, HashSuiteId suite)
    {
        using var destination = new MemoryStream();
        _ = await ChunkManifest
            .CreateAsync(
                new MemoryStream(content, writable: false),
                destination,
                new ManifestCreationOptions { HashSuite = suite })
            .ConfigureAwait(false);
        return destination.ToArray();
    }

    internal static async Task<CsmChunkEntry[]> RecordsAsync(byte[] manifest)
    {
        var records = new List<CsmChunkEntry>();
        _ = await CsmReader
            .ReadAndVerifyAsync(
                new MemoryStream(manifest, writable: false),
                (entry, _) =>
                {
                    records.Add(entry);
                    return ValueTask.CompletedTask;
                })
            .ConfigureAwait(false);
        return [.. records];
    }

    /// <summary>The manifest's records in the lab's own type.</summary>
    internal static async Task<VerifyLabRecord[]> LabRecordsAsync(byte[] manifest) =>
        [.. (await RecordsAsync(manifest).ConfigureAwait(false))
            .Select(static entry => new VerifyLabRecord((long)entry.Index, (long)entry.Offset, (int)entry.Length))];

    /// <summary>
    /// Runs every lane over one case and checks it. <paramref name="declaredD1"/> marks
    /// the one case where the declared difference is expected.
    /// </summary>
    private static async Task CheckAsync(
        VerifyLabOracleReport report,
        string caseClass,
        string caseId,
        VerifyLabContent content,
        byte[] manifest,
        bool declaredD1)
    {
        var verdicts = new VerifyLabVerdict[Lanes.Length];

        for (int index = 0; index < Lanes.Length; index++)
        {
            VerifyLabLane lane = Lanes[index];
            verdicts[index] = await VerifyLabVerdict
                .CaptureAsync(() => VerifyLabLanes.RunAsync(
                    lane,
                    content,
                    () => new MemoryStream(manifest, writable: false),
                    channels: null,
                    CancellationToken.None))
                .ConfigureAwait(false);
            report.Count(caseClass, lane.Name, verdicts[index]);
        }

        report.Cases++;
        VerifyLabVerdict v0 = verdicts[0];
        long? firstMismatch = verdicts[1].FirstMismatchRecord;

        for (int index = 1; index < Lanes.Length; index++)
        {
            VerifyLabVerdict candidate = verdicts[index];
            string lane = Lanes[index].Name;

            if (declaredD1)
            {
                bool declared =
                    v0.Outcome == VerifyLabVerdict.Result &&
                    v0.Failures == ManifestVerificationFailure.Content &&
                    candidate.IsValid &&
                    candidate.ContentMode == VerifyLabVerdict.Slices &&
                    candidate.ProfileConformance == VerifyLabVerdict.NotChecked;

                if (!declared)
                {
                    report.Fail(caseClass, caseId, lane, "not the declared difference D1", v0, candidate);
                }
                else
                {
                    report.DeclaredDifferences++;
                }
            }
            else if (!candidate.SameVerdict(v0))
            {
                report.Fail(caseClass, caseId, lane, "verdict differs from V0", v0, candidate);
            }

            if (candidate.FirstMismatchRecord != firstMismatch)
            {
                report.Fail(caseClass, caseId, lane, "first mismatching record differs from V1", verdicts[1], candidate);
            }

            if (caseClass == "O5" && candidate.Outcome == VerifyLabVerdict.Result &&
                candidate.ContentMode != VerifyLabVerdict.CdcFallback)
            {
                report.Fail(caseClass, caseId, lane, "an untrusted manifest must take cdc-fallback", v0, candidate);
            }

            if (caseClass is "O3" or "O4" && candidate.ContentMode != VerifyLabVerdict.Slices)
            {
                report.Fail(caseClass, caseId, lane, "a mutation of valid content must be verified by slices", v0, candidate);
            }
        }

        if (caseClass is "O3" or "O4" && v0.Failures != ManifestVerificationFailure.Content)
        {
            report.Fail(caseClass, caseId, "V0", "V0 must report Content for a changed content", v0, v0);
        }
    }

    private static async Task MutationsAsync(
        VerifyLabOracleReport report,
        string caseClass,
        string id,
        byte[] bytes,
        byte[] manifest,
        CsmChunkEntry[] records,
        bool quick)
    {
        foreach (long position in MutationPositions(bytes.Length, records, quick))
        {
            bytes[position] ^= 0x01;

            try
            {
                await CheckAsync(
                    report,
                    caseClass,
                    string.Create(CultureInfo.InvariantCulture, $"{id}/xor@{position}"),
                    new VerifyLabMemoryContent(bytes),
                    manifest,
                    declaredD1: false).ConfigureAwait(false);
            }
            finally
            {
                bytes[position] ^= 0x01;
            }
        }
    }

    /// <summary>
    /// O3 positions: offset 0, the last byte, the first and last byte of the
    /// first and last four records, both sides of eight record boundaries and
    /// 16 SplitMix64 positions (quick: two boundaries and four positions).
    /// </summary>
    internal static SortedSet<long> MutationPositions(int length, CsmChunkEntry[] records, bool quick)
    {
        var positions = new SortedSet<long>();

        if (length == 0)
        {
            return positions;
        }

        positions.Add(0);
        positions.Add(length - 1);

        foreach (CsmChunkEntry record in records.Take(4).Concat(records.TakeLast(4)))
        {
            positions.Add((long)record.Offset);
            positions.Add((long)record.Offset + record.Length - 1);
        }

        foreach (long boundary in Boundaries(records, quick ? 2 : 8))
        {
            positions.Add(boundary - 1);
            positions.Add(boundary);
        }

        var prng = new DeterministicPrng(0xC0FE_0201 ^ (ulong)length);

        for (int index = 0; index < (quick ? 4 : 16); index++)
        {
            positions.Add((long)(prng.NextUInt64() % (ulong)length));
        }

        return positions;
    }

    /// <summary>Evenly spread record starts, the first record excluded.</summary>
    internal static IEnumerable<long> Boundaries(CsmChunkEntry[] records, int count)
    {
        if (records.Length < 2)
        {
            yield break;
        }

        var seen = new HashSet<long>();

        for (int index = 1; index <= count; index++)
        {
            int record = 1 + (int)((long)(records.Length - 2) * (index - 1) / Math.Max(1, count - 1));

            if (seen.Add(record))
            {
                yield return (long)records[record].Offset;
            }
        }
    }

    private static async Task TruncationsAsync(
        VerifyLabOracleReport report,
        string id,
        byte[] bytes,
        byte[] manifest,
        CsmChunkEntry[] records,
        bool quick,
        string? scratchDirectory)
    {
        var lengths = new SortedSet<long> { 0 };

        foreach (long boundary in Boundaries(records, quick ? 2 : 8))
        {
            lengths.Add(boundary - 1);
            lengths.Add(boundary);
            lengths.Add(boundary + 1);
        }

        if (records.Length > 0)
        {
            CsmChunkEntry middle = records[records.Length / 2];
            lengths.Add((long)middle.Offset + (middle.Length / 2));
        }

        lengths.Add(bytes.Length - 1L);
        lengths.RemoveWhere(length => length < 0 || length >= bytes.Length);

        foreach (long length in lengths)
        {
            await CheckAsync(
                report,
                "O4",
                string.Create(CultureInfo.InvariantCulture, $"{id}/truncate@{length}"),
                Content(bytes, (int)length, scratchDirectory),
                manifest,
                declaredD1: false).ConfigureAwait(false);
        }

        foreach ((string name, byte[] tail) in new[] { ("extend-1", new byte[] { 0x5A }), ("extend-64k", Random(64 * 1024, 0xC0FE_0301)) })
        {
            byte[] extended = [.. bytes, .. tail];
            await CheckAsync(
                report,
                "O4",
                $"{id}/{name}",
                Content(extended, extended.Length, scratchDirectory),
                manifest,
                declaredD1: false).ConfigureAwait(false);
        }
    }

    private static VerifyLabContent Content(byte[] bytes, int length, string? scratchDirectory)
    {
        if (scratchDirectory is null)
        {
            return new VerifyLabMemoryContent(bytes, length);
        }

        Directory.CreateDirectory(scratchDirectory);
        string path = Path.Combine(scratchDirectory, "oracle-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, bytes.AsSpan(0, length));
        return new VerifyLabFileContent(path);
    }

    /// <summary>
    /// O5: the stored ManifestId, a CBLK byte (inside the first ChunkId), the
    /// CEND content length and the FileDigest, each damaged in a copy.
    /// </summary>
    private static async Task DamagedManifestsAsync(
        VerifyLabOracleReport report,
        string id,
        byte[] bytes,
        byte[] manifest,
        CsmChunkEntry[] records)
    {
        ManifestVerificationResult verified = await ChunkManifest
            .VerifyManifestAsync(new MemoryStream(manifest, writable: false))
            .ConfigureAwait(false);
        int manifestIdAt = LastIndexOf(manifest, Bytes(verified.Manifest.ManifestId.Value));
        int fileDigestAt = LastIndexOf(manifest, Bytes(verified.Manifest.FileDigest));
        int chunkIdAt = LastIndexOf(manifest, Bytes(records[0].Id.Value));

        var damages = new (string Name, int Offset, byte Xor)[]
        {
            ("stored-manifest-id", manifestIdAt, 0x01),
            ("cblk-chunk-id", chunkIdAt, 0x01),
            ("cend-content-length", manifestIdAt - 8, 0x01),
            ("file-digest", fileDigestAt, 0x01),
        };

        foreach ((string name, int offset, byte xor) in damages)
        {
            if (offset < 0)
            {
                throw new InvalidOperationException($"{id}: the {name} bytes were not found in the manifest.");
            }

            byte[] damaged = (byte[])manifest.Clone();
            damaged[offset] ^= xor;
            await CheckAsync(report, "O5", $"{id}/{name}", new VerifyLabMemoryContent(bytes), damaged, declaredD1: false)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// O6: a manifest under the shipped ProfileId and fingerprint whose records
    /// are fixed 64 KiB slices, not the profile's cuts. The exact content is the
    /// declared difference D1; its mutations must agree.
    /// </summary>
    private static async Task ProfileQuestionAsync(
        VerifyLabOracleReport report,
        string id,
        byte[] bytes,
        HashSuiteId suite,
        bool quick)
    {
        byte[] manifest = await CreateFixedSliceManifestAsync(bytes, suite, O6RecordBytes).ConfigureAwait(false);
        CsmChunkEntry[] records = await RecordsAsync(manifest).ConfigureAwait(false);
        long[] profileCuts = await ProfileLengthsAsync(bytes).ConfigureAwait(false);

        if (profileCuts.SequenceEqual(records.Select(static record => (long)record.Length)))
        {
            throw new InvalidOperationException($"{id}: the fixed-slice manifest happens to follow the profile; O6 would not test D1.");
        }

        report.ProfileNonConformingManifests++;
        await CheckAsync(report, "O6", id + "/fixed-64k", new VerifyLabMemoryContent(bytes), manifest, declaredD1: true)
            .ConfigureAwait(false);

        foreach (long position in MutationPositions(bytes.Length, records, quick))
        {
            bytes[position] ^= 0x01;

            try
            {
                await CheckAsync(
                    report,
                    "O6",
                    string.Create(CultureInfo.InvariantCulture, $"{id}/fixed-64k/xor@{position}"),
                    new VerifyLabMemoryContent(bytes),
                    manifest,
                    declaredD1: false).ConfigureAwait(false);
            }
            finally
            {
                bytes[position] ^= 0x01;
            }
        }
    }

    internal static async Task<byte[]> CreateFixedSliceManifestAsync(byte[] content, HashSuiteId suite, int recordBytes)
    {
        ChunkScanConfiguration.ProfileRegistration registration =
            ChunkScanConfiguration.ResolveProfileRegistration(requested: null);
        using var destination = new MemoryStream();

        using (CsmEncoderSession encoder = await CsmEncoderSession
            .CreateAsync(destination, suite, registration.Id, registration.Fingerprint, includeBlockIndex: false, CancellationToken.None)
            .ConfigureAwait(false))
        {
            for (int offset = 0; offset < content.Length; offset += recordBytes)
            {
                int length = Math.Min(recordBytes, content.Length - offset);
                await encoder
                    .AppendAsync(new ChunkId(HashSuiteHasher.Hash(suite, content.AsSpan(offset, length))), (uint)length, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            _ = await encoder.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
        }

        return destination.ToArray();
    }

    /// <summary>The chunk lengths the shipped profile cuts, found independently of any manifest.</summary>
    internal static async Task<long[]> ProfileLengthsAsync(byte[] content)
    {
        var lengths = new List<long>();
        await ChunkScanner
            .ScanAsync(
                new MemoryStream(content, writable: false),
                (chunk, _, _) =>
                {
                    lengths.Add(chunk.Length);
                    return ValueTask.CompletedTask;
                })
            .ConfigureAwait(false);
        return [.. lengths];
    }

    /// <summary>O2: every committed CSM vector with empty content and with 4 KiB of random content.</summary>
    private static async Task VectorsAsync(VerifyLabOracleReport report, string fixtureDirectory)
    {
        string[] vectors = [.. Directory.GetFiles(fixtureDirectory, "*.csm").Order(StringComparer.Ordinal)];

        if (vectors.Length == 0)
        {
            throw new InvalidOperationException($"No CSM vectors in '{fixtureDirectory}'.");
        }

        byte[] random = Random(4096, 0xC0FE_0401);

        foreach (string path in vectors)
        {
            byte[] manifest = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            string name = Path.GetFileName(path);
            await CheckAsync(report, "O2", name + "/empty", new VerifyLabMemoryContent([]), manifest, declaredD1: false)
                .ConfigureAwait(false);
            await CheckAsync(report, "O2", name + "/random-4k", new VerifyLabMemoryContent(random), manifest, declaredD1: false)
                .ConfigureAwait(false);
            report.Vectors++;
        }
    }

    private static byte[] Bytes(Hash256 hash)
    {
        byte[] bytes = new byte[32];
        hash.CopyTo(bytes);
        return bytes;
    }

    private static int LastIndexOf(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().LastIndexOf(needle);
}

/// <summary>
/// The oracle's report: counts per case class, lane and content mode, and every
/// failure. <c>verify-lab run</c> binds the report of its job to the execution
/// by recording the RunId, execution id, commit and platform in it
/// (docs/benchmarks/CORE-VERIFY-003-PROTOCOL.md section 3.2); the oracle
/// leaves them null.
/// </summary>
internal sealed class VerifyLabOracleReport(string schema, bool quick)
{
    public string Schema { get; } = schema;

    public string ExperimentId { get; } = VerifyLabRun.ExperimentId;

    public string? RunId { get; set; }

    public string? ExecutionId { get; set; }

    public string? Commit { get; set; }

    public string? Platform { get; set; }

    public bool Quick { get; } = quick;

    public int Cases { get; set; }

    public int Vectors { get; set; }

    public int ProfileNonConformingManifests { get; set; }

    public int DeclaredDifferences { get; set; }

    public bool Passed => Failures.Count == 0;

    /// <summary>Case class → lane → content mode or exception → count.</summary>
    public SortedDictionary<string, SortedDictionary<string, SortedDictionary<string, int>>> Modes { get; } =
        new(StringComparer.Ordinal);

    public List<VerifyLabOracleFailure> Failures { get; } = [];

    internal void Count(string caseClass, string lane, VerifyLabVerdict verdict)
    {
        string mode = verdict.Outcome == VerifyLabVerdict.Result ? verdict.ContentMode : verdict.Outcome;

        if (!Modes.TryGetValue(caseClass, out SortedDictionary<string, SortedDictionary<string, int>>? lanes))
        {
            lanes = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
            Modes[caseClass] = lanes;
        }

        if (!lanes.TryGetValue(lane, out SortedDictionary<string, int>? modes))
        {
            modes = new SortedDictionary<string, int>(StringComparer.Ordinal);
            lanes[lane] = modes;
        }

        modes[mode] = modes.GetValueOrDefault(mode) + 1;
    }

    internal void Fail(
        string caseClass,
        string caseId,
        string lane,
        string reason,
        VerifyLabVerdict expected,
        VerifyLabVerdict actual) =>
        Failures.Add(new VerifyLabOracleFailure(caseClass, caseId, lane, reason, expected, actual));
}

/// <summary>One manifest record, as the tests see it.</summary>
internal readonly record struct VerifyLabRecord(long Index, long Offset, int Length);

internal sealed record VerifyLabOracleFailure(
    string CaseClass,
    string CaseId,
    string Lane,
    string Reason,
    VerifyLabVerdict Expected,
    VerifyLabVerdict Actual);
