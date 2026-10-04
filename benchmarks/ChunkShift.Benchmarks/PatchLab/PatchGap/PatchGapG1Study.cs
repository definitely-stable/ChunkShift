using System.Security.Cryptography;
using System.Text;
using ChunkShift.Patching.Creation;
using ChunkShift.Patching.Format;
using ChunkShift.Patching.Hashing;
using ChunkShift.Primitives;

namespace ChunkShift.Benchmarks.PatchLab.PatchGap;

internal sealed record PatchGapG1TrialEvidence(
    string EnvelopeId,
    int CandidateOrdinal,
    int StartIndex,
    string TrialKeySha256,
    string[] DictionaryChunkIds,
    int DictionaryBytes,
    int DictionaryReferences,
    int WindowBytes,
    bool Deduplicated,
    string? AliasOfTrialKeySha256,
    bool Eligible,
    string? IneligibleReason,
    int? FrameBytes,
    long? ReferenceBytes,
    long? CostBytes,
    long BaseBytesReadIncrement,
    int BaseReadCallsIncrement,
    int BaseSeeksIncrement,
    bool DecodedExactTarget,
    bool DecodedChunkIdMatches);

internal sealed record PatchGapG1WinnerEvidence(
    string Lane,
    string StoredForm,
    string EnvelopeId,
    int CandidateOrdinal,
    int StartIndex,
    long StoredBytes,
    int DictionaryReferences,
    long CostBytes);

internal sealed record PatchGapG1EntryEvidence(
    long TargetIndex,
    string TargetChunkId,
    long TargetOffset,
    int TargetLength,
    string H0StoredForm,
    int? H0SelectedCandidate,
    long H0StoredBytes,
    int H0DictionaryReferences,
    long H0CostBytes,
    int[] H0CandidateStarts,
    PatchGapG1TrialEvidence[] Trials,
    PatchGapG1WinnerEvidence[] Winners,
    long BaseBytesRead,
    int BaseReadCalls,
    int BaseSeeks);

internal sealed record PatchGapG1FileEvidence(
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    string BaseSha256,
    string TargetSha256,
    long H0PatchBytes,
    string H0PatchSha256,
    long H0EntryCostBytes,
    IReadOnlyDictionary<string, long> LanePatchBytes,
    long BaseBytesRead,
    int BaseReadCalls,
    int BaseSeeks,
    int EntryCount,
    int TrialCount,
    PatchGapG1EntryEvidence[] Entries);

internal sealed record PatchGapG1CompactFileRow(
    string Family,
    string BaseVersion,
    string TargetVersion,
    string Path,
    string BaseSha256,
    string TargetSha256,
    long TargetBytes,
    long UniqueMissingBytes,
    long H0PatchBytes,
    string H0PatchSha256,
    IReadOnlyDictionary<string, long> LanePatchBytes,
    long BaseBytesRead,
    int BaseReadCalls,
    int BaseSeeks,
    int EntryCount,
    int TrialCount,
    string DetailPath,
    string DetailSha256,
    long DetailBytes);

internal sealed record PatchGapG1LaneAggregate(
    string Lane,
    long H0Bytes,
    long FactorBytes,
    long SavedBytes,
    double ReductionVsCsp,
    bool MeetsRfcSizeGate);

internal sealed record PatchGapG1ByteStudyDocument(
    string Schema,
    string ExperimentId,
    string ProtocolCommit,
    string SourceCommit,
    string DatasetRole,
    string DatasetSha256,
    string RequestedLane,
    string ResearchPolicy,
    string BackendAssembly,
    string BackendVersion,
    string BackendSha256,
    PatchGapEvidenceProvenance Provenance,
    PatchGapG1LaneAggregate[] Lanes,
    PatchGapG1CompactFileRow[] Files);

internal sealed record PatchGapG1EntryEvaluation(
    PatchGapG1EntryEvidence Evidence,
    IReadOnlyDictionary<string, long> WinnerCostByLane);

/// <summary>
/// Pure per-entry G1 evaluator. Candidate start discovery and the H0 winner come
/// from production; this type changes only the frozen dictionary/history envelope.
/// </summary>
internal static class PatchGapG1Evaluator
{
    internal const string ResearchPolicy =
        "L19-H0STARTS-PREFIX-H20C20-REF32-NESTED-B1-B4-B8-B32";

    internal static async Task<PatchGapG1EntryEvaluation> EvaluateAsync(
        Stream baseContent,
        List<CspPatchBuilder.BaseRecord> baseRecords,
        HashSuiteId hashSuite,
        CspCandidateTraceEntry h0,
        ReadOnlyMemory<byte> target,
        string requestedLane,
        PatchGapG1Codec codec,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseContent);
        ArgumentNullException.ThrowIfNull(baseRecords);
        ArgumentNullException.ThrowIfNull(hashSuite);
        ArgumentNullException.ThrowIfNull(h0);
        ArgumentNullException.ThrowIfNull(codec);

        if (!baseContent.CanSeek)
        {
            throw new ArgumentException("G1 base content must be seekable.", nameof(baseContent));
        }

        if (target.Length != h0.TargetLength)
        {
            throw new InvalidDataException("G1 target bytes do not match the H0 trace length.");
        }

        string targetChunkId = PatchHashing.Hash(hashSuite, target.Span).ToHexLower();
        if (!string.Equals(targetChunkId, h0.TargetChunkId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("G1 target bytes do not hash to the H0 trace ChunkId.");
        }

        CspEncoderPolicy policy = CspEncoderPolicy.Default;
        RequireFrozenH0Policy(policy);

        List<int> starts = CspPatchBuilder.FindCandidateStarts(
            baseRecords,
            h0.TargetOffset,
            policy);
        if (starts.Count > PatchGapG1Model.MaximumCandidateStarts)
        {
            throw new InvalidDataException("Production H0 exposed more than eight candidate starts.");
        }

        PatchGapG1BaseRecord[] modelRecords =
        [.. baseRecords.Select(static record =>
            new PatchGapG1BaseRecord(record.ChunkId.ToString(), record.Length))];

        PatchGapG1Trial[] trials = PatchGapG1Model.BuildTrials(
            modelRecords,
            starts,
            requestedLane);

        var uniqueOutcomes = new Dictionary<string, TrialOutcome>(StringComparer.Ordinal);
        var trialCosts = new List<PatchGapG1TrialCost>();
        var evidence = new List<PatchGapG1TrialEvidence>(trials.Length);
        long totalReadBytes = 0;
        int totalReadCalls = 0;
        int totalSeeks = 0;

        foreach (IGrouping<int, PatchGapG1Trial> startGroup in trials.GroupBy(static trial => trial.Start))
        {
            PatchGapG1Trial[] ordered = [.. startGroup];
            PatchGapG1Trial[] unique = [.. ordered.Where(static trial => !trial.Deduplicated)];
            byte[]? buffer = null;
            long readBytes = 0;
            int readCalls = 0;
            int seeks = 0;

            if (unique.Length != 0)
            {
                int maximumBytes = unique.Max(static trial => trial.Prefix.DictionaryBytes);
                buffer = new byte[maximumBytes];
                (readBytes, readCalls, seeks) = await ReadLargestPrefixOnceAsync(
                    baseContent,
                    baseRecords,
                    startGroup.Key,
                    maximumBytes,
                    buffer,
                    cancellationToken).ConfigureAwait(false);
                totalReadBytes = checked(totalReadBytes + readBytes);
                totalReadCalls = checked(totalReadCalls + readCalls);
                totalSeeks = checked(totalSeeks + seeks);
            }

            bool readCharged = false;

            foreach (PatchGapG1Trial trial in ordered)
            {
                string keySha = Sha256(trial.Identity.Key);

                if (trial.Deduplicated)
                {
                    if (!uniqueOutcomes.TryGetValue(trial.Identity.Key, out TrialOutcome? alias))
                    {
                        throw new InvalidDataException("G1 deduplicated trial has no earlier exact-identity owner.");
                    }

                    evidence.Add(ToEvidence(
                        trial,
                        keySha,
                        alias,
                        baseBytesReadIncrement: 0,
                        baseReadCallsIncrement: 0,
                        baseSeeksIncrement: 0,
                        aliasOf: alias.TrialKeySha256));
                    continue;
                }

                if (buffer is null)
                {
                    throw new InvalidDataException("G1 unique trial has no materialized dictionary prefix.");
                }

                ReadOnlySpan<byte> dictionary = buffer.AsSpan(0, trial.Prefix.DictionaryBytes);
                bool startsWithMagic = dictionary.StartsWith(CspFormat.ZstdDictionaryMagic);
                TrialOutcome outcome;

                if (startsWithMagic)
                {
                    outcome = new TrialOutcome(
                        keySha,
                        Eligible: false,
                        IneligibleReason: "zstd-trained-dictionary-magic",
                        FrameBytes: null,
                        CostBytes: null,
                        DecodedExactTarget: false,
                        DecodedChunkIdMatches: false);
                }
                else
                {
                    ReadOnlySpan<byte> frame = codec.Encode(
                        target.Span,
                        dictionary,
                        PatchGapG1Model.Get(trial.EnvelopeId));
                    byte[] decoded = new byte[target.Length];
                    codec.Decode(
                        frame,
                        dictionary,
                        decoded,
                        PatchGapG1Model.Get(trial.EnvelopeId));

                    bool exact = decoded.AsSpan().SequenceEqual(target.Span);
                    bool idMatches = string.Equals(
                        PatchHashing.Hash(hashSuite, decoded).ToHexLower(),
                        h0.TargetChunkId,
                        StringComparison.Ordinal);
                    if (!exact || !idMatches)
                    {
                        throw new InvalidDataException("G1 research codec failed exact target reconstruction.");
                    }

                    long cost = checked(
                        (long)frame.Length +
                        ((long)trial.Prefix.DictionaryReferences * PatchGapG1Model.DictionaryReferenceBytes));
                    outcome = new TrialOutcome(
                        keySha,
                        Eligible: true,
                        IneligibleReason: null,
                        FrameBytes: frame.Length,
                        CostBytes: cost,
                        DecodedExactTarget: exact,
                        DecodedChunkIdMatches: idMatches);
                    trialCosts.Add(new PatchGapG1TrialCost(trial, frame.Length));
                }

                uniqueOutcomes.Add(trial.Identity.Key, outcome);

                long chargedBytes = readCharged ? 0 : readBytes;
                int chargedCalls = readCharged ? 0 : readCalls;
                int chargedSeeks = readCharged ? 0 : seeks;
                readCharged = true;

                evidence.Add(ToEvidence(
                    trial,
                    keySha,
                    outcome,
                    chargedBytes,
                    chargedCalls,
                    chargedSeeks,
                    aliasOf: null));
            }
        }

        long h0Stored = h0.StoredBytes;
        int h0Refs = h0.DictionaryRefs;
        long h0Cost = checked(h0Stored + ((long)h0Refs * PatchGapG1Model.DictionaryReferenceBytes));
        string[] laneIds = [.. PatchGapG1Model.Nested(requestedLane).Select(static envelope => envelope.Id)];
        var winners = new List<PatchGapG1WinnerEvidence>(laneIds.Length);
        var winnerCosts = new Dictionary<string, long>(StringComparer.Ordinal);
        long previousCost = h0Cost;

        foreach (string laneId in laneIds)
        {
            var allowed = new HashSet<string>(
                PatchGapG1Model.Nested(laneId).Select(static envelope => envelope.Id),
                StringComparer.Ordinal);
            PatchGapG1Winner winner = PatchGapG1Model.ChooseWinner(
                h0Stored,
                h0Refs,
                trialCosts.Where(item => allowed.Contains(item.Trial.EnvelopeId)));

            if (winner.CostBytes > previousCost)
            {
                throw new InvalidDataException("G1 mandatory per-entry nesting oracle was violated.");
            }

            previousCost = winner.CostBytes;
            winnerCosts.Add(laneId, winner.CostBytes);
            winners.Add(new PatchGapG1WinnerEvidence(
                laneId,
                winner.StoredForm == "H0" ? h0.SelectedEncoding : winner.StoredForm,
                winner.EnvelopeId,
                winner.CandidateOrdinal,
                winner.Start,
                winner.StoredBytes,
                winner.DictionaryReferences,
                winner.CostBytes));
        }

        return new PatchGapG1EntryEvaluation(
            new PatchGapG1EntryEvidence(
                h0.TargetIndex,
                h0.TargetChunkId,
                h0.TargetOffset,
                h0.TargetLength,
                h0.SelectedEncoding,
                h0.SelectedCandidate,
                h0Stored,
                h0Refs,
                h0Cost,
                [.. starts],
                [.. evidence],
                [.. winners],
                totalReadBytes,
                totalReadCalls,
                totalSeeks),
            winnerCosts);
    }

    internal static IReadOnlyDictionary<string, long> FileLaneBytes(
        long h0PatchBytes,
        IReadOnlyList<PatchGapG1EntryEvaluation> entries,
        string requestedLane)
    {
        string[] lanes = [.. PatchGapG1Model.Nested(requestedLane).Select(static envelope => envelope.Id)];
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        long previous = h0PatchBytes;

        foreach (string lane in lanes)
        {
            long bytes = PatchGapG1Model.PhysicalPatchBytes(
                h0PatchBytes,
                entries.Select(entry =>
                    (entry.Evidence.H0CostBytes, entry.WinnerCostByLane[lane])));

            if (bytes > previous)
            {
                throw new InvalidDataException("G1 mandatory aggregate nesting oracle was violated.");
            }

            result.Add(lane, bytes);
            previous = bytes;
        }

        return result;
    }

    private static PatchGapG1TrialEvidence ToEvidence(
        PatchGapG1Trial trial,
        string keySha,
        TrialOutcome outcome,
        long baseBytesReadIncrement,
        int baseReadCallsIncrement,
        int baseSeeksIncrement,
        string? aliasOf) =>
        new(
            trial.EnvelopeId,
            trial.CandidateOrdinal,
            trial.Start,
            keySha,
            trial.Prefix.ChunkIdentities,
            trial.Prefix.DictionaryBytes,
            trial.Prefix.DictionaryReferences,
            trial.Prefix.WindowBytes,
            trial.Deduplicated,
            aliasOf,
            outcome.Eligible,
            outcome.IneligibleReason,
            outcome.FrameBytes,
            outcome.FrameBytes is null
                ? null
                : (long)trial.Prefix.DictionaryReferences * PatchGapG1Model.DictionaryReferenceBytes,
            outcome.CostBytes,
            baseBytesReadIncrement,
            baseReadCallsIncrement,
            baseSeeksIncrement,
            outcome.DecodedExactTarget,
            outcome.DecodedChunkIdMatches);

    private static async Task<(long Bytes, int Calls, int Seeks)> ReadLargestPrefixOnceAsync(
        Stream source,
        IReadOnlyList<CspPatchBuilder.BaseRecord> records,
        int start,
        int length,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        if (length <= 0 || destination.Length != length)
        {
            throw new InvalidDataException("G1 requested an invalid prefix read.");
        }

        long expectedOffset = records[start].Offset;
        int covered = 0;
        for (int index = start; index < records.Count && covered < length; index++)
        {
            CspPatchBuilder.BaseRecord record = records[index];
            if (record.Offset != expectedOffset)
            {
                throw new InvalidDataException("G1 base manifest records are not physically contiguous.");
            }

            covered = checked(covered + record.Length);
            expectedOffset = checked(expectedOffset + record.Length);
        }

        if (covered < length)
        {
            throw new InvalidDataException("G1 prefix exceeds the available contiguous base records.");
        }

        source.Position = records[start].Offset;
        int written = 0;
        int calls = 0;
        while (written < length)
        {
            int read = await source.ReadAsync(destination[written..], cancellationToken).ConfigureAwait(false);
            calls++;
            if (read == 0)
            {
                throw new InvalidDataException("G1 base content ended before the frozen dictionary prefix.");
            }

            written += read;
        }

        return (written, calls, 1);
    }

    private static void RequireFrozenH0Policy(CspEncoderPolicy policy)
    {
        if (policy.Level != 19 ||
            policy.DictionaryChunks != 4 ||
            policy.MaxCandidates != 8 ||
            policy.SearchRadius != 256 * 1024 ||
            policy.DictionaryLoad != ChunkShift.Patching.Encoding.CspDictionaryLoad.Prefix ||
            policy.DictionaryHashLog != 20 ||
            policy.DictionaryChainLog != 20 ||
            policy.CandidateSelection != CspCandidateSelection.Exhaustive)
        {
            throw new InvalidDataException("Current production policy no longer matches frozen PATCH-GAP G1 H0.");
        }
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record TrialOutcome(
        string TrialKeySha256,
        bool Eligible,
        string? IneligibleReason,
        int? FrameBytes,
        long? CostBytes,
        bool DecodedExactTarget,
        bool DecodedChunkIdMatches);
}
