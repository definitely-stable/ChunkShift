using System.Security.Cryptography;
using ChunkShift.Benchmarks.PatchLab;
using ChunkShift.Patching;
using ChunkShift.Patching.Creation;

namespace ChunkShift.Benchmarks.PatchLab.Tree;

/// <summary>
/// F1-A untimed observer. Existing public CSP and separately traced H0 CSP
/// must be byte-identical before any candidate record becomes admissible.
/// No production path is changed and neither input stream is owned.
/// </summary>
internal static class PatchTreeF1Trace
{
    internal const string Schema = "chunkshift.patch-tree-t1-physical-f1a.v1";
    internal const string Status = "SYNTHETIC_CONTRACT_OR_CALIBRATION_ONLY_NOT_DECISION";

    internal sealed record Candidate(
        int Ordinal, int StartIndex, long StartOffset, int RecordCount,
        string FirstChunkId, string Source, int? FinalFrameBytes,
        int? FinalCostBytes, bool Selected, string? FirstSourceFile);
    internal sealed record Entry(
        long TargetIndex, string TargetChunkId, int TargetLength,
        string SelectedEncoding, int StoredBytes, int DictionaryRefs,
        int NoDictionaryFrameBytes, int BaselineCostBytes,
        int? SelectedCandidate, Candidate[] Candidates);
    internal sealed record Observation(
        string Schema, string Status, string Lane, string SourceLabel,
        string PublicPatchSha256, string TracedPatchSha256,
        long PhysicalBytes, long ReconciledPhysicalBytes,
        string? BaseManifestId, long TargetCsmBytes, long PaylBlocks,
        long PayloadEntries, long StoredRawBytes, long StoredZstdBytes,
        long PayloadMetadataBytes, long IndexBytes, long OtherPhysicalBytes,
        long CandidateTrials, long SelectedDictionaryEntries,
        Entry[] Entries);

    internal static async Task<Observation> CompareAsync(
        string lane, string sourceLabel, string publicCsp,
        Stream? baseManifest, Stream? baseContent,
        Stream targetManifest, Stream targetContent,
        string traceCspPath, IReadOnlyList<VerifiedTreeStream.TreeFile>? baseFiles = null)
    {
        if (lane is not ("T0" or "T1"))
            throw new ArgumentOutOfRangeException(nameof(lane), "Only frozen T0 and T1 lanes.");
        if ((baseManifest is null) != (baseContent is null))
            throw new ArgumentException("Both base streams must be present or absent.");
        if (baseManifest is null)
            throw new InvalidOperationException("F1-A traced comparisons require a real base.");

        PatchTreePhysicalAttribution.Ledger physical;
        await using (var source = File.OpenRead(publicCsp))
            physical = await PatchTreePhysicalAttribution.InspectAsync(source, sourceLabel);

        var collector = new PatchLabCandidateTraceCollector();
        var execution = CspCreateExecution.Sequential with
        {
            CandidateTraceSink = collector,
        };
        // Keep the source-verified caller streams alive. The caller owns them.
        baseManifest.Position = 0;
        baseContent!.Position = 0;
        targetManifest.Position = 0;
        targetContent.Position = 0;
        try
        {
            await using (var output = new FileStream(traceCspPath, FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None))
            {
                _ = await CspPatchBuilder.CreateAsync(
                    baseManifest, baseContent, targetManifest, targetContent,
                    output, CspEncoderPolicy.Default, execution, CancellationToken.None);
            }

            long tracedLength = new FileInfo(traceCspPath).Length;
            if (tracedLength != physical.PhysicalBytes)
                throw new InvalidDataException("Traced H0 and public CSP physical lengths differ.");

            string tracedHash;
            await using (var traced = File.OpenRead(traceCspPath))
            {
                tracedHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(traced));
            }
            if (!string.Equals(tracedHash, physical.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("Traced H0 CSP bytes differ from public default.");

            CspCandidateTraceEntry[] traces = collector.Snapshot();
            if (traces.Length != physical.Entries.Length)
                throw new InvalidDataException("Payload-index versus trace cardinality mismatch.");

            var entries = new Entry[traces.Length];
            long trials = 0, dictionaries = 0;
            for (int i = 0; i < traces.Length; i++)
            {
                CspCandidateTraceEntry trace = traces[i];
                PatchTreePhysicalAttribution.Entry emitted = physical.Entries[i];
                string actualEncoding = emitted.Encoding == "raw"
                    ? "raw" : emitted.DictionaryChunkIds.Length == 0 ? "zstd" : "zstd-dictionary";
                if (trace.TargetIndex < 0 || (ulong)trace.TargetIndex != emitted.FirstTargetIndex ||
                    !string.Equals(trace.TargetChunkId, emitted.ChunkId, StringComparison.Ordinal) ||
                    trace.StoredBytes != emitted.StoredBytes ||
                    trace.DictionaryRefs != emitted.DictionaryChunkIds.Length ||
                    trace.SelectedEncoding != actualEncoding ||
                    trace.CandidateCount != trace.Candidates.Count)
                    throw new InvalidDataException("Trace does not match verified physical PIDX/PAYL.");

                var candidates = new Candidate[trace.Candidates.Count];
                int selectedCount = 0;
                for (int ci = 0; ci < candidates.Length; ci++)
                {
                    CspCandidateTraceCandidate c = trace.Candidates[ci];
                    string? file = LocateFile(baseFiles, c.StartOffset);
                    if (c.Selected)
                    {
                        selectedCount++;
                        if (trace.SelectedCandidate != c.Ordinal ||
                            emitted.DictionaryChunkIds.Length == 0 ||
                            !string.Equals(c.FirstChunkId, emitted.DictionaryChunkIds[0],
                                StringComparison.Ordinal))
                            throw new InvalidDataException("Selected dictionary does not match CSP.");
                    }
                    candidates[ci] = new Candidate(c.Ordinal, c.StartIndex, c.StartOffset,
                        c.RecordCount, c.FirstChunkId, c.Source, c.FinalFrameBytes,
                        c.FinalCostBytes, c.Selected, file);
                }
                if (selectedCount != (emitted.DictionaryChunkIds.Length == 0 ? 0 : 1) ||
                    (trace.SelectedCandidate is not null) != (selectedCount == 1))
                    throw new InvalidDataException("Ambiguous selected candidate.");
                dictionaries += selectedCount;
                trials = checked(trials + trace.TotalCompressionTrialCount);
                entries[i] = new Entry(trace.TargetIndex, trace.TargetChunkId,
                    trace.TargetLength, trace.SelectedEncoding, trace.StoredBytes,
                    trace.DictionaryRefs, trace.NoDictionaryFrameBytes,
                    trace.BaselineCostBytes, trace.SelectedCandidate, candidates);
            }

            long payloadMetadata = checked(physical.PaylPrefixBytes +
                physical.PaylEntryHeaderBytes + physical.DictionaryReferenceBytes +
                physical.PaylCrcBytes);
            long indexBytes = checked(physical.PidxPrefixBytes +
                physical.PidxEntryBytes + physical.PidxCrcBytes +
                physical.Sections.Where(s => s.Kind == "PIDX").Sum(s => s.PhysicalBytes - s.PayloadBytes));
            long other = checked(physical.PhysicalBytes - physical.RawStoredBytes -
                physical.ZstdStoredBytes - payloadMetadata - indexBytes);
            if (other < 0 || checked(physical.RawStoredBytes + physical.ZstdStoredBytes +
                payloadMetadata + indexBytes + other) != physical.PhysicalBytes)
                throw new InvalidDataException("F1-A physical byte conservation failed.");
            return new Observation(Schema, Status, lane, sourceLabel,
                physical.Sha256, tracedHash, physical.PhysicalBytes,
                physical.ReconciledPhysicalBytes, physical.ExpectedBaseManifestId,
                physical.EmbeddedTargetCsmBytes, physical.PaylBlocks,
                physical.PayloadEntries, physical.RawStoredBytes, physical.ZstdStoredBytes,
                payloadMetadata, indexBytes, other, trials, dictionaries, entries);
        }
        finally
        {
            if (File.Exists(traceCspPath))
                File.Delete(traceCspPath);
        }
    }

    private static string? LocateFile(
        IReadOnlyList<VerifiedTreeStream.TreeFile>? files, long offset)
    {
        if (files is null)
            return null;
        // Exact source occurrence is a half-open tree interval, not a ChunkId.
        int left = 0, right = files.Count - 1;
        while (left <= right)
        {
            int mid = left + ((right - left) / 2);
            var f = files[mid];
            if (offset < f.Offset)
                right = mid - 1;
            else if (offset >= checked(f.Offset + f.Length))
                left = mid + 1;
            else
                return f.Path;
        }
        return null;
    }
}
